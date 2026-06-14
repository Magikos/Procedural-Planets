# ProceduralPlanets Water Volume Refraction Isolation

[ad-hoc note] Bryan's F10 set around `20260521-114748` still showed the line in `Off` and `VolumeOnly`. `VolumeDilation` did not strongly mark the same contour as missing coverage, so dilation is not the full explanation.

[ad-hoc note] Current likely culprit is refraction in `WaterVolume.shader`: the composite may be sampling a bright terrain/shore source pixel across the volume boundary and pulling it into the water.

[ad-hoc note] `WaterVolume.shader` now suppresses refraction near contact/horizon/dilation pixels with `contactRefractionFade`, using `contactRisk`, `horizonOcclusion`, and `edgeDilation`.

[ad-hoc note] Added F10 `VolumeNoRefraction` mode 29. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` still runs the volume composite but forces `debugRefractionEnabled = 0`. The next F10 should compare `VolumeOnly` and `VolumeNoRefraction` first. If the line disappears in mode 29, refraction is confirmed as the cause.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel Core build hit the known shared intermediate DLL lock, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.
