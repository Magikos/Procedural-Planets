# ProceduralPlanets Water Volume Edge And Fresnel

[ad-hoc note] Bryan's F10 set around `20260521-005059`, `20260521-005110`, and `20260521-005120` still showed the artifact. The `005110` sea-level set was most useful: `FoamParts` was basically clean, while `SurfaceAlpha`, `VolumeBoundary`, and `VolumeOptical` showed the contour.

[ad-hoc note] Diagnosis moved away from shore foam. The likely remaining sources are the rasterized water-volume prepass edge and grazing-angle surface reflection/alpha at the water silhouette, especially where the water data looks open-water-like instead of low-shore.

[ad-hoc note] `WaterVolume.shader` now has `WaterScreenEdgeFade`, based on neighboring `_WaterVolumeData` coverage samples. It multiplies this into `volumeWaterMask` so the full-screen volume composite fades at the water prepass edge instead of producing a hard line.

[ad-hoc note] F10 `VolumeMask` mode 14 now shows RGB = raw water coverage, effective volume coverage, screen-space edge fade.

[ad-hoc note] `Ocean.shader` now reduces grazing reflection strength and fresnel alpha when `_WaterVolumeEnabled` is active. F10 `SurfaceBlend` mode 23 was added: RGB = final surface alpha, base surface alpha, boosted fresnel alpha. `WaterVolume.shader` bypasses mode 23 so the view isolates the surface shader.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed when run serially. A parallel build attempt hit the known shared intermediate DLL write collision, but the serial rerun passed. Scoped `git diff --check` passed for the touched shader/controller files.
