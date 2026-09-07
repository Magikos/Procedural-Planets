# Coral and Golden Forest LOD fixes

The eight branching coral variants and the sparse Golden Forest Tree now retain their original geometry through their full draw range. The larger Golden Forest variants retain their impostors.

## Decision and implementation

The old cards changed narrow branches and trunk coverage. Larger atlas cells and more viewing directions did not consistently correct this. Those experiments were removed.

`ScatterPrototype.MeshOnlyVertexLimit` sets an explicit budget for each source prototype. `ScatterPrototypeDto.ApplyMeshOnlyPolicy()` checks the combined vertex count of each generated variant. The policy only accepts parts with one mesh LOD. It preserves mesh and material references, clears unused atlas references, and retains the previous final draw distance. Repeated application does not extend the range again.

The Golden Forest source has a 200-vertex budget. Only its 163-vertex variant qualifies. Its 847-vertex and 2,104-vertex variants retain cards. The four branching coral sources have a 400-vertex budget. All eight generated variants qualify, at 142–343 vertices.

| Asset | Final draw distance |
|---|---:|
| Golden Forest Tree | 2,250 m |
| Ocean Coral and v1 | 540 m |
| Ocean Coral Shallow and v1 | 90 m |
| Ocean Coral Plate and v1 | 110 m |
| Ocean Coral Deep and v1 | 630 m |

The normal mesh shaders retain lighting, shadows, and weather response. The final mesh fade uses the existing mesh fade interval. It replaces the longer card fade interval; the cull endpoint stays unchanged.

The manifest now contains 175 card entries. Eighteen unused generated atlas PNGs and their metadata were retired. Trial textures were archived outside `Assets`. No shader experiment remains from this follow-up.

## Verification

- Core and Planet builds pass. Core reports no warnings. Planet reports 18 existing warnings.
- All 27 targeted rendering, shader, and LOD fade regression tests pass.
- The verification scene scanned all 185 prototypes at 36-pixel handover size from yaw 0°, 45°, 90°, 180°, and 270°.
- The targeted coral and Golden Forest cases pass those checks. Mesh-only rows explicitly report `MESH_ONLY`, not a missing-card failure.
- Golden Forest v1/v2 card coverage ranges from 0.964 to 1.023 of LOD0. Their luminance ratios range from 0.949 to 1.009.
- The graph was updated. Whitespace checks pass.

These checks establish the selected draw policy and sampled appearance. They do not prove every camera angle, weather state, or whole-planet frame rate.

## GPU comparison

The repeatable benchmark uses `ScatterGpuDraw`, the production indirect renderer, at 1920×1080 on an RTX 3090. Each case submits 10,000 candidate instances. Each pass warms up for 60 frames and measures the next 120 frames. Zero timings are excluded. Saved images confirm that the mesh and card cases render.

| Case | Card median GPU ms, two passes | Mesh median GPU ms, two passes |
|---|---|---|
| Golden Forest Tree | 4.09 / 4.08 | 3.78 / 3.87 |
| Ocean Coral v1 | 4.07 / 3.67 | 3.91 / 4.03 |

Golden Forest geometry was slightly cheaper in this fixture. Coral costs were comparable within run variation. These are whole-frame editor measurements, not isolated shader timings or a full-planet performance claim.

Use the `*-performance-final.csv` files. Earlier exploratory timings preceded a correction to temporary buffer disposal. The final benchmark waits for rendering to finish before disposing buffers.

## Other sweep findings

Update: these four findings were addressed in [Remaining LOD fixes](2026-09-05-remaining-lod-fixes.md). The list below records the earlier sweep.

Four other assets remain flagged by the wider sweep:

- Snow Rock v2: color distance 0.100 at yaw 0°.
- Beach Palm: silhouette overlap 0.54 at yaw 45°.
- Golden Meadow Tree v1: color distance 0.102 at yaw 45°.
- Ocean Kelp: card coverage falls below the sweep's minimum sample area at yaw 45°. Coverage ratio is 0.73; this requires closer inspection.

These findings were not changed in this coral/Golden Forest follow-up. A sweep pass remains a screening result, not a guarantee of exact mesh/card equality.

## Evidence

All local evidence is under `local-only/lod-finish/`:

- `final-{0,45,90,180,270}/`: CSV metrics, summaries, and images.
- `tests-final.json` and `fade-tests-final.json`: 19 rendering/shader tests plus eight fade tests.
- `core-build-final.txt` and `planet-build-final.txt`: build output.
- `benchmark.cs.txt`: Unity execute-code benchmark body.
- `golden-performance-final.csv`, `coral-performance-final.csv`, and `benchmark-*.png`: final performance evidence.
- `source-before/`, `retired-coral-atlases/`, `retired-golden-atlas/`, and `trial-atlases/`: prior assets and rejected trials.
