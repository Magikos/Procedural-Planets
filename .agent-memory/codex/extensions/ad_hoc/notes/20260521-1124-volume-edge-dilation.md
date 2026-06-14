# ProceduralPlanets Water Volume Edge Dilation

[ad-hoc note] Bryan's F10 set around `20260521-111938` still showed the line in `Off` and `VolumeOnly`. `WaterOff` did not show the same line.

[ad-hoc note] `VolumeContact` showed the mask near the contour, but fading contact away still left a bright source-color sliver. Updated diagnosis: the artifact is likely a narrow untreated source-color/terrain sliver at the edge of `_WaterVolumeData`, made visible by contrast with the tinted water volume.

[ad-hoc note] `WaterVolume.shader` now samples neighboring `_WaterVolumeData` pixels via `WaterExpandedData` and uses the best nearby water sample to expand volume coverage by about one screen pixel at the boundary.

[ad-hoc note] `dilationMask` fills pixels where center water coverage is low but nearby water coverage is high. It contributes to `waterMask`, `screenEdgeFade`, `waterVisible`, and `horizonOcclusion`, so edge pixels receive a light water-volume tint instead of preserving a white terrain/source line.

[ad-hoc note] Added F10 `VolumeDilation` mode 28. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = center water coverage, expanded coverage, dilation-only mask. Next review should compare `Off`, `VolumeOnly`, `WaterOff`, `VolumeContact`, and `VolumeDilation`.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Core.csproj` and serial `dotnet build ProceduralPlanets.Planet.csproj` passed. A parallel Planet build hit the known shared intermediate DLL lock, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.
