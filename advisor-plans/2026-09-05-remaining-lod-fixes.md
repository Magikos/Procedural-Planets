# Remaining LOD fixes

The four reported assets now retain their original geometry through the former card range. This removes the mismatched representation instead of hiding it with a wider fade.

## Changes

The existing `MeshOnlyVertexLimit` policy applies separately to each generated variant. The source settings now select these inexpensive meshes:

| Source asset | Vertex budget | Selected variants | Final draw range |
|---|---:|---|---:|
| Beach Palm | 150 | Base: 142 vertices | 2,250 m |
| Golden Meadow Tree | 350 | v1: 343 vertices | 2,250 m |
| Ocean Kelp | 120 | Base: 115 vertices | 630 m |
| Snow Rock | 240 | All three: 240 vertices each | 1,125 m |

The larger palm, meadow tree, and kelp variants keep their cards. The policy preserves original mesh/material references and final draw distances. It uses the existing final mesh fade. Lighting, shadows, and weather continue through the original mesh shaders.

Six card entries and their twelve atlas PNGs were retired. The manifest now contains 169 cards. The library contains 185 prototypes, including 16 mesh-only prototypes. No remaining asset, prefab, or scene references the retired texture GUIDs.

`ScatterLodSweep.PrototypeNames` now supports exact-name filtering. Empty still scans the full library. Screening thresholds remain unchanged.

## Verification

- Fresh captures reproduced the four original warnings before the change.
- All 185 prototypes were scanned at 36-pixel handover size from yaw 0°, 45°, 90°, 180°, and 270°.
- Every final sweep reports 169 `ok` card rows and 16 `MESH_ONLY` rows, with no warnings.
- All 27 rendering, shader, and LOD fade regression tests pass.
- Core and Planet builds pass. Planet retains 18 existing compiler warnings.
- Whitespace and retired-reference checks pass. The graph was updated.
- Unity is stopped with the Planet scene restored.

The affected prototypes have one mesh representation across their former handover interval. The sweep therefore verifies visible mesh-only rows, not a new card approximation. No separate moving-camera recording was made. These sampled checks do not guarantee every other asset at every angle or weather state.

## Performance tradeoff

The benchmark submits 10,000 candidates through the production GPU indirect renderer at 1920×1080 on an RTX 3090. Each of four alternating passes warms up for 60 frames and samples the following 120 frames. Zero timings are excluded. Images confirm that the geometry renders.

| Asset | Card median GPU ms, two passes | Mesh median GPU ms, two passes |
|---|---|---|
| Snow Rock v2 | 3.51 / 3.86 | 3.38 / 4.30 |
| Beach Palm | 3.95 / 3.70 | 3.51 / 3.72 |
| Golden Meadow Tree v1 | 3.90 / 3.73 | 4.35 / 4.26 |
| Ocean Kelp | 4.91 / 4.10 | 4.34 / 3.78 |

The meadow tree adds about 0.5 ms in this dense fixture. Snow Rock measurements vary between passes. Palm and kelp do not show a consistent increase. These are whole-frame editor measurements, not a claim of unchanged full-planet frame rate. The budgets prevent more expensive variants from switching to geometry.

## Evidence and deferred experiment

Evidence lives under `local-only/lod-remaining/`: `before-*`, `final-*`, `benchmark.cs.txt`, `*-performance-final.csv`, `benchmark-*.png`, `tests-final.json`, and build logs. Retired textures are backed up in `retired-atlases/`.

The staged `lighting.patch` was not applied. The opaque card lighting peak differs slightly from the mesh peak, but that correction alone cannot explain the observed rock mismatch. The selected geometry policy resolves these targets without changing lighting across all remaining cards.
