# ProceduralPlanets Water Near Surface Silhouette

[ad-hoc note] Bryan reported the artifact is still visible near the water surface while looking toward shore. F10 sets around `20260521-072953` and `20260521-073005` show the remaining issue.

[ad-hoc note] `SurfaceBlend` showed a broad blue/purple grazing-alpha band and `VolumeOptical` showed a yellow contour. `FoamParts` was not the primary source. Current diagnosis: near-surface grazing silhouette, not shore foam.

[ad-hoc note] `Ocean.shader` now reduces the transparent surface contribution more aggressively when `_WaterVolumeEnabled` is active: lower `WaterFinalAlpha` volume alpha, lower grazing reflection fresnel, lower fresnel alpha, and an added grazing alpha fade.

[ad-hoc note] `WaterVolume.shader` now computes `horizonOcclusion` for above-water, near-surface, grazing, open-water view paths. It increases density/extinction, lowers scatter light/strength, increases volume blend, and contributes to `deepExtinction` so bright shore/terrain pixels behind the water horizon get darkened/tinted.

[ad-hoc note] Existing F10 modes validate this pass: `SurfaceBlend` should show much less blue grazing alpha, and `VolumeOptical` should show more blue/deep-extinction contribution at the problematic contour.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`. Unity still needs to reimport/compile the shader edits.
