# ProceduralPlanets Water Binary Isolation

[ad-hoc note] Bryan ran another F10 set around `20260521-093744` and reported the water-surface-to-shore artifact still looks unchanged. This should be treated as credible; prior value tuning has not meaningfully changed the final `Off` image.

[ad-hoc note] Current diagnosis is uncertain. Stop tuning water shader values until the rendering source is isolated.

[ad-hoc note] New F10 modes were added. `VolumeOnly` mode 24 makes `Ocean.shader` transparent but leaves `WaterVolume.shader` active. `SurfaceOnly` mode 25 renders the ocean surface normally but bypasses `WaterVolume.shader`. `WaterOff` mode 26 makes `Ocean.shader` transparent and bypasses `WaterVolume.shader`.

[ad-hoc note] Next F10 review should compare `Off`, `VolumeOnly`, `SurfaceOnly`, and `WaterOff` first. If the line remains in `VolumeOnly`, investigate volume composite/prepass. If it remains in `SurfaceOnly`, investigate transparent ocean surface. If it remains in `WaterOff`, investigate non-water rendering such as terrain, atmosphere, clouds, or depth ordering.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel Core build hit the known shared intermediate DLL lock, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.
