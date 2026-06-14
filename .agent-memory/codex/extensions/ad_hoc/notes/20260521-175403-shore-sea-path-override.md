# ProceduralPlanets Shore Sea Path Override

[ad-hoc note] Bryan shared another agent's theory after the F10 set around `20260521-174913`: `VolumeContact` shows waterVisible/contact at the bright shoreline line, but `VolumeOptical` remains weak because the path falls back to a short near-shore value and `opticalGate` does not open enough.

[ad-hoc note] Current evaluation: the theory is directionally correct, but the exact old snippet should not be restored blindly. `SeaRay` proved the camera ray is physically behind the sea-level sphere at the problem band. `SeaVsMesh` showed the analytic curved contribution was still too gated at the shoreline/contact contour, because the existing `curvedSeaRay` path used an open-water gate.

[ad-hoc note] `WaterVolume.shader` now adds `shoreSeaPathCoverage`: above water, near the surface, scene behind sea sphere, valid raster volume mask, and very short `aboveScenePath`. `curvedSeaCoverage` is the max of the existing open-water curved ray and this shore/contact coverage.

[ad-hoc note] The important fix detail is that `curvedSeaPath = seaPathMeters * curvedSeaCoverage` feeds `abovePath` outside the `waterVisible * ...` multiply. This prevents the exact shoreline contour from collapsing to the shallow fallback path and should make `viewPath01`, `longViewGate`, source occlusion, and deep extinction engage for the low-angle far-shore line.

[ad-hoc note] Expected next F10: compare `Off`, `VolumeOnly`, `VolumeContact`, `VolumeOptical`, `SeaVsMesh`, and `SeaPath`. If the fix is active, `SeaVsMesh` should show blue/magenta `curvedSeaCoverage` on the line and `SeaPath` should show stronger green/blue curved/final path there.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed. No trailing spaces/tabs were found in the touched files. `WaterVolumePrepass.shader` has no content diff but may still show modified due line-ending churn.
