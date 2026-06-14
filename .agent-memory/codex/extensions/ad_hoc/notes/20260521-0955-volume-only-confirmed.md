# ProceduralPlanets Water Volume Only Confirmed

[ad-hoc note] Bryan's F10 set around `20260521-095054` showed the line clearly in `Off` and `VolumeOnly`. It did not appear the same way in `SurfaceOnly` or `WaterOff`.

[ad-hoc note] Current confirmed source: the artifact is in the full-screen water volume composite/prepass path, not the transparent ocean surface shader.

[ad-hoc note] `WaterVolume.shader` now computes `grazingSceneContact` for above-water near-surface grazing views from water visibility, valid scene depth, surface proximity, grazing angle, and short `aboveScenePath`. It combines this with the prior low-shore `shoreContact` into `contactRisk`.

[ad-hoc note] `waterVisible` now fades by `terrainClearance` whenever `contactRisk` is high, regardless of `shore01Raw`. This targets open-water-looking contours that the low-shore-only contact fade missed.

[ad-hoc note] Added F10 `VolumeContact` mode 27. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = contact risk, terrain clearance, resulting water visibility. Next review should compare `Off`, `VolumeOnly`, `WaterOff`, and `VolumeContact` first.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Core.csproj` and serial `dotnet build ProceduralPlanets.Planet.csproj` passed. A parallel Planet build hit the known shared intermediate DLL lock, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.
