---
name: project-scatter-gather-perf
description: 2026-07-29 — scatter deterministic (verify PASS). Tier 1a coarse-biome memo cut gather 21s→6s (4354691); then Lever B incremental TILE CACHE replaced whole-disc gather entirely (frontier-only per move) — fly cap raised 0.006→0.02 (~106 m/s); if scatter "looks missing" it's altitude/orbit, NOT placement or speed
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

**LEVER B SHIPPED — incremental tile cache replaced the whole-disc gather (Valheim ZoneSystem model).**
Design + Codex review: [docs/design/2026-07-29-scatter-incremental-gather.md]. `ScatterTileCache`
partitions the surface into fixed cube-face tiles at `Lt = min(7, minLevel)` (~82m). Each
`(tile, prototype)` payload is gathered ONCE via `ScatterField.GatherTilePrototype` (pure fn of tile+seed,
enumerates the prototype cells whose `ScatterQuadtree.ParentTile` is that tile) and cached; readiness is
per-`(tile,proto)` (`ulong ReadyMask`) so a far tile that entered "trees only" re-fills bushes as the
camera closes. One sequential background worker (batch 48/excursion), epoch-guarded commits, in-flight set
released in `finally` (retryable), evict beyond `maxRadius + 1 tile`, append-on-commit draw buckets +
affected-only rebuild on evict. A camera move gathers ONLY the frontier ring — validated: 200m move =
417→519 tiles in fast 6-9ms batches, no whole-disc re-scan. Commits: slice1 7f34bc0 (shared
`TryGatherCandidate` + `GatherTilePrototype` + `ParentTile`, verify PASS), slice2+3 9da9e34, hardening
b5433c4, partition guard + foliage tone 81f3dfd. **`scatter.tilecheck` PASS: tile union == whole disc,
1650 inst** (partition proof). New commands: `scatter.tiles` (cache counters), `scatter.tilecheck`
(partition proof, heavy → small region). 53 EditMode tests (added ParentTile partition tests).

**Fly cap raised 0.006→0.02 (~32→~106 m/s)** in FreeCameraController.cs + Planet.unity (2588b62) — the
stopgap is lifted because the gather is now frontier-only. Sprint (3×=~318 m/s) untested; lower the mult
if it out-runs the frontier. **ImpostorRangeMultiplier 1.3→2.0** (ScatterDtos) pushes the tree line to
~878m (was ~520) — horizon cutoff much softer. **Foliage lit shade 1.18→1.0** (FoliageLit/Scatter/
ScatterImpostor) — the 1.18 over-brightened above albedo ("too bright, doesn't fit"); clamped to full
albedo, settles into the terrain palette.

**Remaining (not blocking):** (1) true-horizon far-field = bake low-res tree-density tint into terrain
beyond impostor range (AAA method, cheaper than more billboards); (2) initial fill still "chunky" (~5300
mostly-empty wrong-biome pairs) — biome pre-filter per tile would cut it ~16× but risks the partition
(tile-center biome ≠ per-candidate) so needs a conservative multi-sample design; editor low-FPS
exaggerates it, a build fills far faster; (3) user to fly-test the raised cap (I can't drive input).
The baked surface-radius atlas idea (Lever A) is now OPTIONAL — the incremental cache made the per-move
gather cheap enough without it; revisit only if the frontier cost bites. See [[project-planet-look-dev]].
