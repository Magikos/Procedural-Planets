# ProceduralPlanets Sea Matte Diagnostic

[ad-hoc note] Bryan's F10 set around `20260521-180610` showed the shoreline line became darker after the analytic sea occlusion gate, but it was still visible from the low above-water camera.

[ad-hoc note] Latest interpretation: `TerrainSourcePink` marks the contour, `FoamPink` does not, `SeaVsMesh` and `SeaPath` show analytic sea coverage/path at the line, and `VolumeOcclusion` still lets a thin source edge remain. This points to terrain/source color leaking through the `WaterVolume.shader` full-screen composite rather than foam, ocean surface mesh order, or missing water coverage.

[ad-hoc note] Added F10 mode 38, `SeaMatte`, to force sea-occluded/source-occluded pixels toward dark deep-water color in `WaterVolume.shader`. `Ocean.shader` renders transparent in this mode, and `FreeCameraController` includes it in the targeted `WaterArtifact` capture set.

[ad-hoc note] Expected next F10: if the line disappears in `SeaMatte`, the final fix should strengthen production volume blend/transmittance/deep extinction for the sea-occluded source path. If the line survives in `SeaMatte`, suspect something drawn after `WaterVolume.shader` or a source pixel not covered by the analytic sea matte.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed after adding the mode. No trailing spaces/tabs were found in the touched shader/controller files.
