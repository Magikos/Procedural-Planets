# ProceduralPlanets F10 Targeted Water Debug

[ad-hoc note] Bryan clarified that he cannot choose individual debug tests during play; pressing F10 through every water debug mode is the practical workflow but creates too many screenshots.

[ad-hoc note] `FreeCameraController.F10CaptureSet` now defaults to `WaterArtifact`, so one F10 press captures a targeted water-artifact set and restores ocean debug mode Off. `CurrentModeOnly` keeps the old one-mode-at-a-time behavior, and `FullLoop` captures every mode.

[ad-hoc note] Targeted artifact modes are Off, Shore, Foam, WaterData, Absorption, VolumeMask, VolumePath, FoamParts, SurfaceAlpha, VolumeBoundary, and VolumeOptical.

[ad-hoc note] F10 pruning now sizes retention by active capture set. With default `WaterArtifact`, `DebugScreenshotMaxRuns = 6` keeps about 132 PNG/TXT files after the next F10 capture instead of keeping six full old-style loops.

[ad-hoc note] New debug modes: 18 `FoamParts` RGB=shore/runup/crest foam; 19 `SurfaceAlpha` RGB=final alpha/optical alpha/scene path; 20 `VolumeBoundary` RGB=waterVisible/sceneDepthValid/sceneBehindWater; 21 `VolumeOptical` RGB=optical/volumeBlend/deepExtinction.

[ad-hoc note] `WaterVolume.shader` now bypasses the post-volume pass for surface debug modes 1-11 plus 18/19 so underwater tint does not hide surface diagnostics. `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed after the workflow/debug pass; Unity shader reimport is still needed for `Ocean.shader` and `WaterVolume.shader`.
