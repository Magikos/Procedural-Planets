# Scatter gather performance — design (for review)

Branch `scatter-placement`. Goal: make the scatter gather fast enough that props stay visible while
flying at full speed, **without changing where anything is placed** (seed-based determinism is a hard
requirement — see Invariants). Today the gather takes ~9–14 s and the camera out-runs it, so scatter
appears empty in motion (stopgap `4564b76` capped fly speed).

## Measured bottleneck

Per candidate the CPU gather (`ScatterField.GatherCore`, background thread) does:
1. `AnalyticGroundSampler.TrySampleGround` — **3** `ShapeGenerator` elevation evals (1 for radius, 2
   tangent probes for the slope normal).
2. `ColorGenerator.EvaluateBiome` → `ClimateProvider.Evaluate` — **live** temperature + moisture noise
   fields + `ResolveBiome`. This is the dominant cost and it is done for every position-passing candidate.

At ~370–600k candidates (per-prototype-culled) over a ~520 m region, re-run every 10 m of camera move.
`scatter.verify` measured ~24 µs/candidate.

**The biome is already baked.** `BiomeAtlasService` produces per-face biome atlases (`ids`, `weights`)
that the terrain shader + grass read. The grass placement is a **GPU compute** shader that reads baked
surface-radius + biome textures and writes instances to a buffer drawn indirect — it is real-time. The
scatter gather recomputes, live and on the CPU, data that is already a texture.

## Invariants (must not break)

- **Deterministic:** a prop's existence/position is a pure function of `(worldSeed, face, level, x, y,
  slot)` via `ScatterHash` → `ScatterId`. Same seed ⇒ identical placement every load. Persistence
  (`ScatterId` keys, `SurfaceEditStamp`) depends on this.
- **Order-independent & region-independent:** `scatter.verify` gathers forward, reverse, and at two
  radii and asserts identical sets. Any cache must return a value that is a pure function of position,
  never of iteration order or cache state.
- Gates that decide placement (biome membership, slope, altitude, water clearance) may change *source*
  but must stay deterministic functions of position. A one-time placement shift (new seed→layout) is
  acceptable; per-load inconsistency is not.

## Tier 1 — cheaper gates on the CPU (target: 5–15× faster, low risk)

**1a. Coarse-cell biome memo.** Biomes are large; sampling the biome per fine candidate is wasteful.
Sample the biome at a fixed coarse level `Lb≈9` cell **center** and reuse it for every finer candidate
in that coarse cell:
- For candidate `(face, level, x, y)`: `xb = x >> (level-Lb)`, `yb = y >> (level-Lb)`; coarse-center
  dir via `CubeFaceToUnitSphere`; `EvaluateBiome(coarseDir, coarseElevation)` where `coarseElevation`
  comes from one radius sample at the coarse center.
- Memoize by `(face, xb, yb)` — a single-entry (or tiny) memo is enough given spatial-coherent
  iteration; it is a pure optimization (miss computes the same value a hit returns), so it stays
  **order-independent**. Evaluated at the cell center (not the first candidate) ⇒ no order dependence.
- Effect: fine prototypes (grass 2.5 m, flowers) drop from 1 biome eval/candidate to ~1 per ~32 m cell
  (~100×+ fewer); coarse prototypes (trees) sample at their own level. Placement shifts to ~32 m biome
  resolution — matches what the terrain atlas already paints, and stays deterministic.
- Elevation caveat: the biome uses altitude; using the coarse center's elevation can nudge biome
  membership within ~32 m of a border. Acceptable (biomes are 100s of m). If it matters, key the memo
  by `(coarse cell, elevation bucket)`.

**1b. Lazy slope normal.** Compute only the radius (1 eval) up front; run the position + biome + altitude
gates; compute the slope normal (2 more evals) **only** for survivors. Most candidates fail biome, so
this drops ~2/3 of ground evals. Requires: split the sampler into `TrySampleRadius` + `SampleNormal`,
and move the altitude/water gates ahead of the slope gate in `GatherCore` (do them inline, then
`TryPlace` with the precomputed slopeCos). 100% value-preserving (same samples, deferred) ⇒ no
placement change.

**1c. (alt to 1a) Read the baked biome atlas directly.** At `Configure`, `GetPixels32` the
`BiomeAtlasService` `ids`/`weights` atlases into per-face `Color32[]` (main thread), then sample those
arrays in the gather thread instead of `EvaluateBiome`. Fastest per-lookup and makes scatter agree
exactly with the visible terrain, but needs the atlas encoding decoded + `dir→(face,uv)` mapping;
higher integration risk than 1a. Prefer 1a first; revisit 1c if 1a isn't enough.

Verification for Tier 1: `scatter.verify` must still PASS (unique/order/region/transform), the EditMode
`ScatterHashTests`/`ScatterId`/`PlacementMath` suites stay green, and re-timing shows the gather well
under ~2 s so the fly-speed cap (`4564b76`) can be lifted.

## Tier 2 — GPU-driven scatter (the AAA form; bigger effort)

Port the gather to a compute shader modeled on the existing grass compute: per cell in the camera
region, hash `(seed, cell, slot)`, read the baked biome/slope/altitude maps, run the same
`ScatterPlacementMath` gates, and append surviving instance transforms to per-prototype `AppendBuffer`s
drawn with `DrawMeshInstancedIndirect`. Zero CPU per-instance; scales to millions; no gather lag at any
speed. Determinism is preserved because the hash is unchanged. Reuses grass infrastructure
(`GrassChunkDispatcher`, buffer pools, baked textures). This is the ceiling and the eventual target.

## How this compares to other titles

- **Density/placement maps** (Ubisoft-style vegetation): placement reads a baked map instead of
  evaluating noise — this is Tier 1c / the biome atlas we already bake.
- **GPU indirect instancing** (Ghost of Tsushima, Horizon, thatgamecompany): compute → buffer → indirect
  draw — this is Tier 2, and exactly what our grass already does.
- **Deterministic tiled streaming + persisted diffs** (Space Engineers, No Man's Sky, Star Citizen):
  base content is seed-derived per tile, regenerated on approach; only player edits are stored. We
  already have the deterministic quadtree + `ScatterId`/`SurfaceEditStamp`. The only gap is fast tile
  generation — which Tier 1/2 fix.

## Recommendation

Do **Tier 1a + 1b** first (CPU, low risk, keeps the current architecture, likely lifts the fly-speed
cap on its own), gated on `scatter.verify` staying green. Consider Tier 2 later for full GPU scale.
