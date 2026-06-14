# ProceduralPlanets Water Surface Contact Debug

[ad-hoc note] Bryan's latest F10 captures still showed the shoreline artifact in `FoamParts`, `SurfaceAlpha`, and `VolumeBoundary`. The current working diagnosis is that transparent spherical water can sit in front of shore terrain on the camera ray, so shoreline surface/volume contribution reads as drawn on top at certain viewing angles.

[ad-hoc note] `Ocean.shader` now has `WaterSceneGapMeters`, `WaterSceneContactClearance01`, and a revised `ShoreContactVisibility(terrainClearance01, sceneValid, shore01)`. The fade uses the raw water-surface-to-opaque-scene gap instead of the previously gated water path.

[ad-hoc note] Surface fresnel alpha and focus-mode sunset shimmer alpha now respect the terrain-contact fade. This is meant to remove remaining shoreline overlay even after foam/base alpha were already suppressed.

[ad-hoc note] F10 `WaterArtifact` includes `SurfaceContact` mode 22. RGB means red = low-shore contact pressure, green = terrain clearance from raw scene gap, blue = raw water-to-scene gap scaled for inspection. `WaterVolume.shader` bypasses the volume composite for mode 22.

[ad-hoc note] `WaterVolume.shader` widened its above-water shoreline contact fade using low `shore01Raw`, valid scene depth, and a broader `aboveScenePath` terrain-clearance range.

[ad-hoc note] Verification after this pass: `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. `dotnet build Assembly-CSharp.csproj` still fails because generated Shapes project files reference missing `Assets/Plugins/Shapes/...` sources. Unity still needs to reimport/compile `Ocean.shader` and `WaterVolume.shader`.
