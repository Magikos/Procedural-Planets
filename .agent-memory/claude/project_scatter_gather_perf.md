---
name: project-scatter-gather-perf
description: 2026-07-29 — scatter IS deterministic (verify PASS) but the camera-centric gather is slow (~9-14s); if scatter "looks missing" it's a gather-speed/altitude issue, NOT placement
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

**Real fix (follow-up) for full-speed flight:** faster/incremental gather — defer the slope normal (2
of 3 ground samples) until AFTER the biome-membership gate (most candidates fail biome, so the normal
is wasted); cache/cheapen the biome eval; or gather only the leading edge instead of re-scanning the
whole region each move. The per-candidate biome eval is the dominant cost. See [[project-planet-look-dev]].
