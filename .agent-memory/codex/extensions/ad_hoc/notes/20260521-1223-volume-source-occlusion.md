# ProceduralPlanets Water Volume Source Occlusion

[ad-hoc note] Bryan's F10 set around `20260521-121853` showed `VolumeOnly` and `VolumeNoRefraction` looking effectively the same, so refraction is not the cause.

[ad-hoc note] Current confirmed practical diagnosis: the full-screen water volume composite is leaving too much already-rendered bright shoreline/terrain source color visible through water. It looks like draw order because terrain renders before the volume composite.

[ad-hoc note] `WaterVolume.shader` now keeps contact pixels partially visible with `contactVisibilityFloor` instead of fading them almost away. This lets the volume cover/tint the offending source pixel.

[ad-hoc note] Added `sourceOcclusion` for above-water near-surface grazing rays with valid scene depth. It uses `contactRisk`, `horizonOcclusion`, and `edgeDilation` to suppress transmittance, raise `volumeBlend`, and increase `deepExtinction`.

[ad-hoc note] Added F10 `VolumeOcclusion` mode 30. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = source occlusion, final volume blend, transmittance suppression. Next F10 review should compare `Off`, `VolumeOnly`, `VolumeNoRefraction`, and `VolumeOcclusion`.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel Core build hit the known shared intermediate DLL lock, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.
