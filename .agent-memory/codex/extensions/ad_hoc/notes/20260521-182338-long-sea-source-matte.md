# ProceduralPlanets Long Sea Source Matte

[ad-hoc note] Bryan's F10 sets around `20260521-181719` and `20260521-181754` included the new `SeaMatte` mode 38. The low horizon view mostly removed the thin far shoreline-through-water contour in `SeaMatte`, while the closer shoreline view still showed jagged contact-edge pixels.

[ad-hoc note] Interpretation: the far grazing artifact is terrain/source color leaking through the water-volume composite and can be suppressed by a sea/source matte. The close shoreline rim is a contact coverage/classification edge and should be treated separately if it remains after the far-line fix.

[ad-hoc note] `WaterVolume.shader` now adds `seaSourceMatte`, guarded by above-water camera, near sea level, scene behind the sea sphere, sea grazing angle, sea path length, and existing curved/source path coverage. It strengthens source matte, lowers source transmittance, and pushes deep extinction/deep-water color only for long sea-occluded source pixels.

[ad-hoc note] Expected next F10: compare `Off`, `VolumeOnly`, `VolumeOptical`, `VolumeOcclusion`, `SeaPath`, and `SeaMatte`. If the far horizon line is gone but the close shore rim remains, pivot to contact-edge coverage/shoreline overlap instead of more long-path source matte tuning.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed. Scoped `git diff --check` passed with only line-ending warnings.
