# ProceduralPlanets Analytic Sea Occlusion Gate

[ad-hoc note] Bryan's F10 set around `20260521-175701` still showed the shoreline line with no observable change after the shore sea-path override. `SeaPath` was active, but `VolumeOptical` and `VolumeOcclusion` still showed weak final optical/source response at the line.

[ad-hoc note] Diagnosis pivot: increasing path length alone is not enough because final opacity/extinction is still gated by depth/shore/open-water terms. The analytic sea path has to open the optical/source gates directly for low-camera rays where the source scene is behind the sea sphere.

[ad-hoc note] `WaterVolume.shader` now adds `curvedSeaOcclusion`, independent of raster water coverage and open-water shore gates. It is based on above-water camera, near sea level, scene behind sea sphere, sea grazing, and sea path length.

[ad-hoc note] `curvedSeaCoverage` is now the max of open-water curved ray, shore/contact coverage, and `curvedSeaOcclusion`. It feeds `abovePath` and also opens a new `curvedSeaGate` inside `opticalGate`. Source and horizon occlusion now also get stronger direct `curvedSeaCoverage` contributions.

[ad-hoc note] Expected next F10: `SeaVsMesh` should show stronger blue on the sea-occluded band even when mesh/contact gates are weak. `VolumeOptical` should show red/blue response on the shoreline contour; if it does not, the next diagnosis should inspect final color blend/transmittance rather than path/optical gate.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed. No trailing spaces/tabs were found in the touched files.
