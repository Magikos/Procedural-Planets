# ProceduralPlanets Terrain Source Pink Diagnostics

[ad-hoc note] Bryan's F10 sets around `20260521-124410`, `20260521-124437`, and `20260521-124456` included above-shore, high-altitude, and under-terrain/above-sea views. The shoreline-like contour is visible in source-like views at some positions, so the next diagnosis should explicitly distinguish terrain source color from foam and volume coverage.

[ad-hoc note] Added F10 debug modes `TerrainSourcePink` (31), `FoamPink` (32), and `VolumeSphere` (33). `TerrainSourcePink` paints terrain hot pink through `PlanetVertexColor.shader` while the ocean surface is transparent and the volume composite still runs. `FoamPink` paints only ocean foam hot pink and bypasses the volume. `VolumeSphere` shows RGB = analytic sea-sphere fallback, scene-behind-sea gate, and sea path length.

[ad-hoc note] `VolumeOcclusion` mode 30 now returns black for no-water pixels instead of falling back to `_Source`, so it can no longer hide missing coverage by showing normal scene color.

[ad-hoc note] `WaterVolume.shader` now has a guarded analytic sea-sphere fallback for above-water, near-surface, grazing rays with valid scene depth behind the sea sphere and weak rasterized water coverage. It also adds `sourcePathOcclusion` based on water-to-scene path length so distant terrain source color seen through water is suppressed even when low-shore contact risk is not high.

[ad-hoc note] Next F10 review should compare `Off`, `VolumeOnly`, `WaterOff`, `VolumeOcclusion`, `TerrainSourcePink`, `FoamPink`, and `VolumeSphere` first. If the contour turns hot pink in `TerrainSourcePink` but not `FoamPink`, keep debugging/fixing the full-screen volume/source occlusion path rather than foam.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. Scoped `git diff --check` passed for tracked touched files, and no trailing whitespace was found in `Ocean.shader`, `WaterVolume.shader`, `PlanetVertexColor.shader`, or `FreeCameraController.cs`.
