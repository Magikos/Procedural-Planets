# ProceduralPlanets Horizon Contact Matte

[ad-hoc note] Bryan's F10 set around `20260521-182729` still showed the far low-horizon shoreline contour in `Off` and `VolumeOnly`. `SeaMatte` continued to show that the hard diagnostic matte can suppress the low-horizon source edge more strongly than production.

[ad-hoc note] Latest interpretation: the long sea-source matte reduced the terrain/source bleed, but the remaining visible contour is also a bright contact/classification edge. `VolumeContact`, `VolumeOcclusion`, `SeaVsMesh`, and `SeaPath` still light the shoreline contour, so the next pass should target the bright contact edge rather than only increasing long-path opacity.

[ad-hoc note] `WaterVolume.shader` now separates `longSeaSourceMatte` from a new `horizonContactMatte`. `horizonContactMatte` is gated by above-water near-surface camera, valid source depth, sea-sphere intersection, grazing sea ray, sea path length, contact/edge dilation signal, and source luma.

[ad-hoc note] Added F10 mode 39, `SeaSourceMatte`: red = `longSeaSourceMatte`, green = `horizonContactMatte`, blue = final `sourceMatte`. `Ocean.shader` renders transparent in this mode and `FreeCameraController` includes it in the targeted WaterArtifact set.

[ad-hoc note] Expected next F10: if the visible contour lights green/blue in `SeaSourceMatte`, the new contact-edge matte is covering the correct pixels. If the line remains in `Off` while `SeaSourceMatte` is bright there, strengthen final production color application. If `SeaSourceMatte` is dark at the line, pivot to prepass coverage or geometry overlap.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel `ProceduralPlanets.Planet.csproj` build first hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed with only line-ending warnings.
