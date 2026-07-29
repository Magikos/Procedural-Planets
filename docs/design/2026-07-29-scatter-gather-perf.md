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

---

## Codex feedback — 2026-07-29

### Audit Summary

**Findings only — no code changed.**

Reviewed against branch `scatter-placement`, HEAD `4b077c6`, the current scatter audits,
and the live gather, analytic sampler, biome atlas, renderer, camera, settings, and EditMode
test paths. Graphify returned only a stale generic performance checklist for this scope, so
all findings below were verified directly against the working tree. No build, Unity run, or
performance measurement was performed.

**Verdict: REVISE BEFORE IMPLEMENTATION — 3 High, 2 Medium, 1 Low.**

The bottleneck diagnosis is credible, but the plan currently mixes a behavior-preserving
optimization with a layout-changing approximation, contains an invalid level mapping, and
uses an acceptance target that cannot prove props remain populated at full flight speed.

### What Came Back Clean

- The hot-path accounting is correct: `AnalyticGroundSampler.TrySampleGround` performs one
  center and two tangent elevation samples (`AnalyticGroundSampler.cs:21-45`), then
  `ScatterField.Membership` calls the live biome pipeline (`ScatterField.cs:183-197,291-297`).
- Tier 1b is the smallest high-leverage first move: the current code pays for the normal
  before ROI, biome, altitude, water, or density rejection.
- Gather configuration and transform inputs are already immutable snapshots, and the
  renderer supplies a separate range buffer for background work
  (`ScatterField.cs:72-112,222-231`).
- Prior audit N1 is resolved: matrices are bucketed once per swap and `Draw` now visits
  each prototype's own bucket (`ScatterRenderer.cs:30-33,175-200`).

### Findings

#### P1 — Choose one placement-compatibility contract

**Category:** Architecture  
**Severity:** High  
**Description:** The goal promises no placement change, but the invariants later allow a
one-time layout change and Tier 1a explicitly moves biome membership to a coarse cell
center. That changes which stable IDs are accepted near every biome boundary. Passing
`scatter.verify` proves the new algorithm is internally deterministic; it does not prove
that the old and new accepted sets are equal.  
**Evidence:** The no-change promise is at `docs/design/2026-07-29-scatter-gather-perf.md:3-5`;
the conflicting allowance is at `:31-34`; Tier 1a acknowledges placement shifts at
`:38-52`. Membership feeds `densityKeep` and therefore acceptance at
`Assets/Scripts/Planet/Scatter/ScatterField.cs:189-202`. Current `ScatterId` comments say
chop/collect persistence is future SP5 work, not current `SurfaceEditStamp` behavior
(`ScatterId.cs:3-4`).  
**Impact:** An executor can claim behavior preservation while changing the accepted ID
set, and future save compatibility has no algorithm-version rule.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Pick one contract. The minimal path is Tier 1b first with exact
before/after ID and transform equality. If Tier 1a is still needed, label it a
behavior-changing layout revision, quantify ID-set churn at fixed seeds/poses, and define
when a saved world is allowed to adopt it. Remove the `SurfaceEditStamp` dependency claim;
it is a separate surface-edit ledger.  
**Refactor Option:** None; this is a design decision, not a new abstraction.  
**Behavior note:** Tier 1b can preserve behavior. Tier 1a changes accepted instances and
requires explicit approval.

#### P2 — Make the coarse-cell key total over the live quadtree levels

**Category:** Bug  
**Severity:** High  
**Description:** The proposed `x >> (level-Lb)` mapping is invalid when a prototype level
is below `Lb`, and the key omits the sampled level. The live library contains level-8
through level-12 prototypes, so `Lb=9` is not merely an edge case. In C#, shifting by
`-1` masks the shift count to 31; a level-8 prototype would collapse coordinates instead
of “sampling at its own level.” A cache shared across levels can also alias different cell
centers under the same `(face, xb, yb)` key.  
**Evidence:** The formula and key are at
`docs/design/2026-07-29-scatter-gather-perf.md:39-46`.
`ScatterQuadtree.LevelForSpacing` uses
`ceil(log2(2R/spacing))` (`ScatterQuadtree.cs:9-23`). With the live radius 5,000
(`Assets/Game Data/Planet Settings/Planet.asset:15`), the 40 m Grassland Rock and Desert
Dead Tree assets select level 8 (`.../Grassland Rock Prototype.asset:17`,
`.../Desert Dead Tree Prototype.asset:17`); current assets span levels 8-12. Forward and
reverse prototype traversal is intentional (`ScatterField.cs:143-146`).  
**Impact:** The proposed cache can return biome data for the wrong position and may fail
the order-independence invariant it is intended to preserve.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Define `sampleLevel = min(level, Lb)`, shift only by
`level - sampleLevel`, compute the center at `sampleLevel`, and key by
`(face, sampleLevel, xb, yb)`. Require the memo to be invocation-local; diagnostics can
run synchronously while the renderer gathers in the background, so a field/static cache
would introduce a race.  
**Refactor Option:** Use one local last-key/last-value entry first. Add a small dictionary
only if measurement shows cross-prototype misses matter.  
**Behavior note:** Deterministic within the new Tier 1a layout, but still behavior-changing
relative to the current per-candidate biome sampling.

#### P3 — Measure the production path against a speed-derived freshness budget

**Category:** Architecture  
**Severity:** High  
**Description:** `scatter.verify` does not time the renderer's production gather shape,
and “well under ~2 s” is not sufficient to restore full speed for all prototypes. Verify
uses a uniform, synchronous 60 m gather by default; production uses a background,
per-prototype-cull gather over the farthest region. With one gather in flight, the front
buffer can remain centered on an old camera position throughout the following gather.
At the former 0.02 surface multiplier, base speed is about 100 m/s on the live 5,000 m
planet (300 m/s while sprinting), while a live grass prototype gathers only to 90 m.  
**Evidence:** Verify's path and default are at `ScatterField.cs:340-394`; the production
path and one-in-flight swap are at `ScatterRenderer.cs:124-171`; production enables
per-prototype culling at `ScatterField.cs:225-231`. Camera speed is
`radius * SurfaceSpeedMultiplier`, then multiplied by sprint
(`FreeCameraController.cs:263-267,375`); the stopgap is 0.006 at `:12-15`, formerly
0.02 in commit `4564b76`. Grassland Grass ends at 90 m
(`Assets/Resources/Settings/Scatter/Grassland Grass Prototype.asset:37-39`).  
**Impact:** Tier 1 can meet the written two-second target while nearby grass, flowers, and
bushes still disappear during full-speed travel; the plan's stated user outcome remains
unproven.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Add one production-shape measurement that invokes the same
per-prototype-cull work with the current renderer region and reports candidates,
accepted instances, elapsed time, and the fixed seed/pose/library. Define the pass budget
from the requested speed and the smallest prototype radius that must remain populated;
keep the speed cap until that threshold passes on repeated fresh gathers. Report median
and worst observed latency, not only one verify total.  
**Refactor Option:** Extend an existing `scatter.*` diagnostic; do not add a profiler
framework.  
**Behavior note:** Preserving.

#### P4 — Keep Tier 1b's sampler and gate ownership coherent

**Category:** Maintainability  
**Severity:** Medium  
**Description:** Tier 1b says to split the sampler and duplicate altitude/water checks
inline before calling `TryPlace`. The current sampler deliberately returns radius and
normal together as the future SDF seam, while `ScatterPlacementMath.TryPlace` is the sole
owner of altitude/water rejection. A mechanical split plus duplicated gates can make the
fast path choose a different surface hit or drift from the final placement function.
The proposed verification also lacks a pre-change layout checksum.  
**Evidence:** The proposed split/inline checks are at
`docs/design/2026-07-29-scatter-gather-perf.md:54-59`.
The coupled sampler contract and SDF rationale are at
`Assets/Scripts/Core/Interfaces/ISurfaceGroundSampler.cs:3-12`; altitude/water gates live
at `ScatterPlacementMath.cs:32-46`. `scatter.verify` compares traversals of one current
implementation, not old versus new (`ScatterField.cs:340-394`).  
**Impact:** A claimed no-behavior-change optimization can introduce two placement-rule
owners or weaken the terrain-abstraction seam.  
**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Specify a two-stage sampler contract where the normal query receives
the exact selected radius/hit, and keep the existing combined method as the reference
composition. Extract one pure altitude/water predicate that both the precheck and
`TryPlace` call. Before changing code, capture a fixed-seed ID+position+rotation+scale set;
Tier 1b passes only on exact equality.  
**Refactor Option:** Two sampler methods plus one shared placement predicate are enough;
do not add a sampler factory or caching service.  
**Behavior note:** Must be preserving.

#### P5 — Tier 1c cannot read the current atlases as written

**Category:** Bug  
**Severity:** Medium  
**Description:** `GetPixels32` at `Configure` will not work because the face atlases are
uploaded with `makeNoLongerReadable: true`. Retaining CPU copies would also add a material
memory cost that the design does not budget: at live depth 4 the atlas is 1009x1009, so
six ID plus six weight `Color32[]` arrays consume about 46.6 MiB before managed-array
overhead. The baked representation is top-K area-weighted data, not the same point sample
returned by `EvaluateBiome`.  
**Evidence:** Tier 1c is at `docs/design/2026-07-29-scatter-gather-perf.md:61-65`.
Atlas creation calls `Apply(..., makeNoLongerReadable: true)` at
`BiomeAtlasService.cs:535-545`. Resolution is
`(1 << depth) * (64 - 1) + 1` at `:55-66`; live depth is 4
(`Planet.asset:17`). The top-K 13x13 vote and packed ID/weight semantics are documented
and implemented at `BiomeMapBaker.cs:3-18,127-204`. Unity 6 documents that CPU pixel
access requires `isReadable`, and that `Apply(..., makeNoLongerReadable: true)` clears it:
[Texture.isReadable](https://docs.unity3d.com/cn/6000.0/ScriptReference/Texture-isReadable.html).  
**Impact:** The alternative implementation throws when reading textures or quietly grows
world memory by roughly 47 MiB while also changing biome-membership semantics.  
**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Remove Tier 1c from the first implementation slice. If Tier 1b/1a
miss the measured target, design an immutable CPU atlas snapshot owned and released by
`BiomeAtlasService`, created from bake buffers before upload, with an explicit memory
budget and a CPU sampler matching the grass top-K interpolation.  
**Refactor Option:** Reuse the bake output; do not add GPU readback or make the render
textures permanently readable.  
**Behavior note:** Changes the biome source and accepted set; approval required.

#### P6 — Defer the GPU architecture until Tier 1 supplies a failing number

**Category:** Complexity  
**Severity:** Low  
**Description:** Tier 2 promises zero CPU instances and reuse of grass dispatchers,
append buffers, and baked data without defining the CPU authority needed by future
interaction/persistence, buffer ownership across regeneration, or CPU/HLSL parity.
The existing grass path is blade-specific and uses explicit `RWStructuredBuffer`
atomics, not `AppendBuffer`. This detail is premature while Tier 1 is unmeasured.  
**Evidence:** The promise is at `docs/design/2026-07-29-scatter-gather-perf.md:71-78`.
`GrassChunkDispatcher` is coupled to grass params, chunk runtime, and grass buffer stride
(`GrassChunkDispatcher.cs:47-98,134-226`); `GrassNearFieldPlace.compute:11,58-61`
documents the explicit-counter design. The current parity surface is still C# at
`ScatterPlacementMath.cs:15-18`.  
**Impact:** A later executor may treat a broad rewrite as ready architecture and lose the
headless authoritative instance stream or duplicate grass-specific infrastructure.  
**Effort:** S (doc correction)  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Reduce Tier 2 to a trigger and constraints: write a separate design
only if Tier 1 misses the derived latency budget or measured instance scale requires GPU
generation. That design must choose CPU-near/GPU-far authority, parity proof, and buffer
lifetime before naming reusable classes.  
**Refactor Option:** None now; YAGNI.  
**Behavior note:** No current behavior change.

### Refactoring Plan

1. Resolve the two user decisions below: allowed layout churn and target flight speed.
2. Implement/measure Tier 1b alone, with one shared altitude/water predicate and an exact
   before/after placement checksum.
3. Add a production-shape gather measurement and derive its pass threshold from speed
   and the smallest prototype radius that must stay populated.
4. Only if Tier 1b misses that threshold, add Tier 1a with the corrected level-aware,
   invocation-local single-entry memo; record fixed-seed accepted-ID churn.
5. Leave Tier 1c out unless the measured gap justifies its ~47 MiB CPU snapshot and
   behavior change.
6. Keep Tier 2 as a later trigger, not an implementation promise.

### Prior Audit Reconciliation

| Prior item | Status | Current evidence |
|---|---|---|
| 2026-07-25 F1 — LOD-dependent ground sampling | RESOLVED | `ScatterField` uses `ISurfaceGroundSampler`; `AnalyticGroundSampler` is deterministic and LOD-independent. |
| 2026-07-25 F2 — synchronous main-thread gather | RESOLVED | Renderer gathering is off-thread with immutable snapshots; this design addresses remaining throughput/staleness, not the old main-thread hitch. |
| 2026-07-25 F4 — far-field cap/LOD | PARTIAL, UNAFFECTED | Per-prototype gather bands and impostors exist; far representation quality remains separate from gather generation cost. |
| 2026-07-26 N1 — full-list draw rescans | RESOLVED | Per-prototype matrix/position buckets are rebuilt once on swap at `ScatterRenderer.cs:175-200`. |

Other 07-25/07-26 findings concern assets, materials, lighting, dither, and LOD quality;
they do not overlap this gather-performance design and retain their recorded statuses.

## Resolution — 2026-07-29 (Codex feedback accepted)

Codex's findings are correct and adopted. Decisions from Bryan:
- **Placement contract:** ALLOW a one-time deterministic re-layout (no saved worlds yet). Tier 1a is
  approved as a *behavior-changing* layout revision; the new algorithm must stay deterministic +
  order-independent, and its accepted-ID churn vs the current layout is recorded (not hidden behind
  "no change"). The `SurfaceEditStamp` dependency claim above is withdrawn (chop/collect is future SP5).
- **Target:** full/vehicle surface speed (~100+ m/s, sprint 3×). Likely needs Tier 2 for the sprint
  case; Tier 1 is pushed as far as it goes and measured honestly.

Revised implementation order (per Codex):
1. **Production-shape measurement first.** Instrument the real background gather
   (`ScatterRenderer.GatherAndSwapAsync` → `GatherOffThread`, per-prototype-cull, renderer region):
   log candidates, accepted, and elapsed ms per gather at a fixed seed/pose. Derive the pass budget
   from target speed and the smallest prototype radius that must stay populated (bush ~120 m). Report
   median + worst, not one number.
2. **Tier 1b (lazy normal), exact-preserving.** Split the sampler into radius-only + normal-from-hit;
   extract ONE pure altitude/water predicate shared by the pre-check and `TryPlace`; reorder gates
   (radius → ROI → biome → altitude/water → normal → slope → accept). Gate on an exact fixed-seed
   ID+pos+rot+scale checksum equality, plus `scatter.verify` PASS and the EditMode suites.
3. **Tier 1a (coarse-cell biome memo), behavior-changing (approved).** `sampleLevel = min(level, Lb)`,
   shift by `level - sampleLevel`, key `(face, sampleLevel, xb, yb)`, **invocation-local** single-entry
   memo (no field/static — diagnostics run sync while the renderer gathers in background). Record
   accepted-ID churn at fixed seed/pose. Gate on `scatter.verify` PASS (new layout is internally
   deterministic + order-independent).
4. **Tier 1c dropped** (atlases are `makeNoLongerReadable`; ~47 MiB CPU snapshot; top-K ≠ point sample).
5. **Tier 2** is a trigger only: pursue a separate design if Tier 1 misses the derived budget.

### Questions for the User

1. Does “without changing where anything is placed” mean exact accepted-ID compatibility,
   or may Tier 1a create a one-time new layout before SP5 persistence ships?
2. Does “full speed” mean unsprinted surface flight (~100 m/s at the live radius), Shift
   sprint (~300 m/s), or only keeping the far tree tier populated? The latency budget
   differs by several times.

## Results — 2026-07-29 (Tier 1b + Tier 1a measured)

Forest surface pose, production-shape gather (per-prototype cull, renderer region 520 m), timed at
`ScatterRenderer.GatherAndSwapAsync` around `GatherOffThread`.

| Build | Gather (Forest) | Notes |
|---|---|---|
| Baseline (per-candidate biome + eager normal) | **~21 s** | biome eval + normal every candidate |
| Tier 1b (lazy normal only) | **~21 s** | normal was never the cost — biome eval dominates (~50 us/candidate) |
| Tier 1a + 1b (coarse-cell biome memo) | **~6 s** (5.9-6.1 s, ~38 k instances) | ~3.5x vs baseline |

**Tier 1b alone did nothing** — the slope normal was a red herring; the live biome eval (climate
noise + Voronoi resolve) is the dominant per-candidate cost. Tier 1a (memoize the biome at a coarse
level-9 cell centre, reuse for the finer candidates inside) is what moved the number.

Determinism after Tier 1a: `scatter.verify PASS` — unique, order-independent, transform-stable,
region-independent, id+player round-trip (1117 instances, 16894 candidates, 740 ms at region 60).

**Region-independence regression found + root-caused (not a determinism bug).** First verify after
Tier 1a failed region-independence by 2 of ~365 instances. Both mismatches sat 0.2-0.65 m inside the
30 m test disc edge; order-independence and transform-stability still passed exactly (forward ==
reverse). Root cause is in the shared range builder, pre-existing and surfaced by the re-layout:
`halfExtent = ceil(discRadiusUV / cellUvWidth)` centres a cell-count-symmetric square on the floored
centre cell, but the camera sits at a fractional offset inside that cell, so the +U/+V world reach
falls short of the disc by up to one cell. Fix: **+1 cell margin** on `halfExtent` in both
`FaceSpaceCellRangeBuilder` overloads (also benefits grass; downstream culling still clips to the
exact radius). Verify PASS after.

Accepted-ID churn vs the pre-Tier-1a layout: biome is now sampled at ~19.5 m coarse cells (level 9 on
a 5293 m planet), so a candidate's membership can only differ from the exact per-candidate value
within ~19.5 m of a biome border. Biomes are Voronoi cells at km scale, so the affected fraction is
small and confined to border bands; interior placement is byte-identical. This is the approved
one-time re-layout.

### Where 6 s leaves us vs target

Capped surface speed is `0.006 x 5293 ~= 32 m/s` (95 m/s sprint). Tree fresh-margin is
region 520 - mesh cull 400 = **120 m**. A 6 s gather travels ~190 m at 32 m/s > 120 m, so trees still
lag the camera even at the capped base speed, and far worse at the ~100 m/s target. Tier 1a is a
large win but **not sufficient on its own.**

Remaining per-candidate cost after Tier 1a is the ground-radius sample (one multi-octave elevation
eval, ~5-7 us x ~370 k candidates). Next lever: **retain the baked per-face surface-radius atlas the
grass build already computes.** `GrassSurfaceAtlasBuilder` builds a 1009^2 RFloat/face atlas from
`chunk.CpuVertexRadii`, then discards the CPU copy and keeps only a `makeNoLongerReadable` GPU
texture. Retaining that float[6][] (~24 MiB) lets scatter `TrySampleRadius` do a bilinear array
lookup (~0.1 us) instead of live noise — projected ~6 s -> ~1-2 s, plus region tuning for the
100 m/s margin. Distinct from dropped Tier 1c (biome atlas, ~47 MiB, top-K != point sample): this is
a single-value radius field scatter would sample the same way grass already does, and it puts props
on the exact meshed surface. If ~1-2 s still misses the sprint budget, Tier 2 (GPU-driven gather).
