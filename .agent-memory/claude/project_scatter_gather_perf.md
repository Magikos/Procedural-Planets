---
name: project-scatter-gather-perf
description: 2026-07-29 — scatter IS deterministic (verify PASS); gather was ~21s, Tier 1a coarse-biome memo cut it to ~6s (committed 4354691); if scatter "looks missing" it's gather-speed/altitude, NOT placement
metadata:
  type: project
---

**Scatter placement is fully deterministic / seed-based (do not doubt this).** Every prop is
`ScatterHash.Node(worldSeed, face, level, x, y)` → `Slot(node, slotId)` — pure math, no RNG, no
session state. `ScatterId.Pack` is the stable u64 persistence key. `scatter.verify` PASSES (unique,
order-independent, region-independent, transform-stable, id+player round-trip). Golden-value unit tests
lock the hashes (see [[project-test-harness]]). A tree cannot move between loads or wander into a
player's build. If asked "is scatter seed-based / consistent" the answer is YES, proven.

**Why scatter can LOOK missing (both are NOT placement bugs):**
1. It is camera-centric + near-surface-only: gathers/draws within the region of the camera's surface
   anchor. Trees mesh-cull ~400m, impostors to the region cap, bushes/rocks ~120-250m, grass ~380m.
   In orbit (default spawn = 2.5× radius via FreeCameraController.AutoPositionOnGenerate) everything is
   beyond cull → bare sphere. **Spacebar (`ToggleOrbit`, bound in InputMapService.cs:97) drops to the
   surface.** `scatter.goto <Biome>` teleports onto a biome.
2. **The gather is SLOW (~9-14s):** per candidate it does a ground sample (AnalyticGroundSampler = 3
   elevation noise evals for radius + slope normal) + a LIVE biome eval (ColorGenerator.EvaluateBiome →
   climate + Voronoi resolve, not the baked atlas). ~370-600k candidates, re-run every 10m of camera
   move (ScatterRenderer double-buffer, one gather in flight). At the old surface fly speed (~106 m/s)
   the camera out-runs it — scatter stays gathered ~1km behind, beyond draw range → invisible while
   flying. Fix (4564b76): capped placement-only prototypes to 80m gather (redundant no-mesh scatter
   grass was scanning the full region at 2.5m spacing), impostor region mult 1.75→1.3 (~700→520m),
   surface fly speed 0.02→0.006 (~106→~32 m/s) so travel-per-gather < region. STOPGAP.

**Tier 1a landed (commit 4354691), gather ~21s → ~6s, verify PASS.** Measured the REAL production
gather at ~21s (worse than the ~9-14s guess). The dominant cost is the LIVE biome eval (~50µs/candidate),
NOT the slope normal — **Tier 1b (lazy normal) alone changed nothing** (still 21s). Tier 1a memoizes
the biome at a coarse level-9 cell centre (`sampleLevel=min(level,9)`, invocation-local single-entry
memo keyed `(face,sampleLevel,xb,yb)`, value = EvaluateBiome at the cell centre → order-independent by
construction) and reuses it for the finer candidates inside → ~6s. Also shipped: two-stage
`ISurfaceGroundSampler` (TrySampleRadius + SampleNormalAt), one shared `ScatterPlacementMath.PassesAltitudeWater`
predicate, and a **range-builder +1 cell margin fix** (`FaceSpaceCellRangeBuilder.halfExtent` — the
cell-count-symmetric square around the floored centre cell fell short of the disc by up to one cell from
sub-cell camera offset, failing verify region-independence by 2 disc-edge instances; the re-layout
surfaced a pre-existing gap). Tier 1a is an approved one-time re-layout (biome snaps to ~19.5m cells near
borders; interior byte-identical). Surface screenshot confirms lush trees/rocks/bushes.

**Still not enough for 100 m/s.** At capped 32 m/s the tree fresh-margin (region 520 − cull 400 = 120m)
is crossed in ~4s < 6s gather, so trees still pop in ~210m out while flying (fine slow, laggy fast).
**Next lever (design doc §Results): retain the baked per-face surface-radius atlas grass already builds**
(`GrassSurfaceAtlasBuilder` makes a 1009²/face RFloat atlas from `chunk.CpuVertexRadii`, then discards the
CPU copy). Retaining that float[6][] (~24 MiB) makes `TrySampleRadius` a bilinear lookup (~0.1µs) vs live
noise (~6µs) → projected ~6s → ~1-2s. Caveat: atlas is ~8m/texel (coarser than the mesh) → use a
fidelity-preserving hybrid (atlas for gates, analytic radius for the final accepted position) if trees
float on steep terrain. Distinct from dropped Tier 1c (biome atlas 47 MiB). Then region tuning / Tier 2
(GPU gather) for the sprint case. See [[project-planet-look-dev]].
