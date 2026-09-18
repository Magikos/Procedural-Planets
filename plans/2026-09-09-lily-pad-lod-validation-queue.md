# Lily pad LOD validation queue

Status: Premature range cutoff corrected and imported in Unity. Visual acceptance and performance measurement remain open.
This file is a deferred work list, not an automatic scheduler or Editor reservation.
Bryan released Unity for this task on 2026-09-09.

## Confirmed source findings

- `Assets/Resources/Settings/Scatter/Lake Lily.asset` has one mesh and an authored end distance of 140 metres.
- `Assets/Scripts/Planet/Props/PlantInjection.cs` generates the lily through RockGenerator, retains only Lod0, and creates a single-mesh ScatterPartDto.
- `Assets/Scripts/Planet/Scatter/ScatterDtos.cs` disables impostors for bounds whose height is less than 10% of their horizontal size. This preserves the grazing silhouette of flat water plants.
- The generated lily definition uses Subdivisions = 1. Actual runtime mesh cost and effective distances remain unmeasured.

There is no intermediate lily mesh LOD in this path. The cause of the reported visible disappearance remains unverified. No rendering code or assets changed during this review.

## Tests after Unity handoff

1. Record scene, seed, camera pose, quality, water state, and current overrides. Preserve the handed-off state.
2. Locate the actual runtime lily prototypes. Record vertex and triangle counts, mesh bounds, material, effective draw distance, HasImpostor, and LOD bands.
3. Record a slow approach and retreat across the effective cull boundary, at shore height and looking downward. Repeat on CPU and GPU scatter paths. Restore the original path afterward.
4. Separate disappearance caused by distance fade, tile residency, frustum culling, water occlusion, and mesh LOD. Record the responsible stage before editing.
5. Use the existing LOD gallery to compare the generated rock LOD meshes, if available. Check whether a coarser mesh preserves the pad outline, surface height, and shading. Do not restore the known unsuitable upright impostor path.
6. If a lower mesh tier is justified, implement it in the existing generated-prop path. Compare matching camera recordings before and after. Require continuous coverage and no visible shape or lighting jump.
7. Run focused ScatterRenderingRegressionTests after an approved implementation. Record discovered, passed, failed, and skipped counts. Measure frame cost before claiming a performance improvement.

Archive recordings, captures, and runtime measurements under `local-only/lily-pad-lod/`. Record results here. Bryan approves the final appearance.

Reference: user attachment `C:/Users/Bryan/AppData/Local/Temp/codex-clipboard-bd1cce98-97cd-4955-a06f-318d4d033a9f.gif`. The attachment path is temporary; archive it with the evidence when available.

## Executed evidence

- Live Lake Lily: one mesh, 240 vertices, 80 triangles, no impostor, mesh cull distance 140 m.
- Runtime shared band functions return fade start 119 m and fade end 140 m.
- Actual ScatterCull compute dispatch accepted distances 118, 119, 130, and 139.9 m. It rejected 140 and 141 m, as configured.
- Captured approach/retreat at camera offsets 0, 20, 40, 20, 0 m on both CPU and GPU paths. The return captures overwrite the corresponding approach files; these are snapshots, not a continuous recording.
- Inspected `local-only/lily-pad-lod/gpu-40.png` and `cpu-40.png`. Lily placement and appearance match in these views. Broader rendering differs between paths, outside this lily investigation.
- Starting camera position was `(4316.23, -2448.18, -1208.83)`, quality index 0, GPU path enabled. The sweep restored the exact camera transform, controller state, draw path, and time scale through a finally block.
- `local-only/lily-pad-lod/sweep-complete.txt` confirms restoration. Unity remains in Play mode.

No source or asset changes were made. No NUnit suite or build was run because this pass changed only the evidence document. These checks confirm the one-mesh setup and distance boundary; they do not prove continuous pixel coverage or reproduce the exact submitted GIF viewpoint. A first inspection expression failed because Lod0Bounds is private; the corrected expression read the mesh bounds directly.

## Range correction

Bryan requested a fix after the inspection. Changed Lake Lily.asset end distance from 140 to 630 metres. The original far-reaching impostor rule extends 140 metres by 4.5; excluding flat pads from impostors also excluded that extension. The asset now explicitly retains that reach using its existing 80-triangle mesh. No new LOD shape or billboard was introduced.

Imported the asset and updated the active library's lily distance while retaining its meshes and other prototype settings. Reconfigured the existing scatter field and renderer. Unity reports 630 metres, HasImpostor false, 240 vertices, and fade start 535.5 metres. The shader reaches zero coverage at 630 metres.

Inspected `local-only/lily-pad-lod/range-after.png`: pads remain visible across the lake beyond the former empty cutoff. Compare with `current.png` at the same camera pose. Lighting/time advanced between these captures, so use them for coverage evidence only.

This fixes the premature range loss. It does not establish that water occlusion never affects pads. More distant instances are drawn; GPU cost has not been measured. No C# or shader edits, builds, or NUnit runs were needed for this asset-only correction. Unity remains in Play mode with the correction active.
