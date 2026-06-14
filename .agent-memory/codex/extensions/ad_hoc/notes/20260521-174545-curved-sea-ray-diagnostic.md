# ProceduralPlanets Curved Sea Ray Diagnostic

[ad-hoc note] Bryan clarified the persistent line is only visible from a low camera near the water surface while looking along the curved planet, effectively seeing the far shoreline through the body of water. This makes the most likely remaining issue the water-volume path/depth model at grazing angles, not foam, backface culling, or cube-face mesh continuity.

[ad-hoc note] Latest F10 set around `20260521-173527` confirmed the global water graph regenerated (`Mesh: verts=217960`, down from `219813`) but the line remained. That means the cube-face water-data continuity patch activated but did not solve this artifact.

[ad-hoc note] Replaced the previous one-value prepass shore-floor experiment with a spherical sea-ray diagnostic and guarded curved-path contribution in `WaterVolume.shader`. The volume now computes a `curvedSeaRay`, `curvedSeaCoverage`, and `curvedSeaPath` from analytic sea-level sphere intersection, scene depth, low camera proximity, grazing angle, and existing water coverage.

[ad-hoc note] Added F10 modes 35-37: `SeaRay` outputs RGB = scene behind sea sphere, analytic sea path, sea grazing; `SeaVsMesh` outputs RGB = raster volume mask, curved sea ray, curved sea coverage; `SeaPath` outputs RGB = old above-scene path, curved sea path, final path. `Ocean.shader` hides the water surface in these modes.

[ad-hoc note] Expected next test: same low near-surface view. If `SeaRay` lights the visible shoreline but `SeaVsMesh` does not, the curved-path guard is too strict or raster water coverage is missing. If both light and normal `Off` improves, the fix direction is correct. If `SeaRay` does not light the line, the issue is outside the sea-level sphere/depth model and should pivot again.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. The first parallel Core build hit the known shared intermediate DLL lock. No trailing spaces/tabs were found in the touched files using `[ \t]+$`.
