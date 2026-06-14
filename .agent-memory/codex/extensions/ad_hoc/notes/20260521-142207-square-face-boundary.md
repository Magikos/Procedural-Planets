# ProceduralPlanets Square Shore Face Boundary

[ad-hoc note] Bryan noticed an odd square-like shore geometry in the latest F10 sets. It is visible in post-regeneration captures around `20260521-141525` and `20260521-141543`, especially in `VolumeMask`, where a large straight-edged/square-ish boundary appears.

[ad-hoc note] The square-ish shape appears faintly in `Off`/`WaterOff` and strongly in water-data/volume modes. This likely plays a role in the shoreline artifact and points toward cube-sphere face/grid boundary data or per-face water classification rather than foam or only source-color matte.

[ad-hoc note] Code review supports this: `WaterMeshBuilder` processes each `TerrainFace` independently. `ClassifyWaterBodies` and `ComputeShoreDistance` do not propagate across cube-face edges, so shoreline/water data can show straight face-local boundaries even when terrain elevation is direction-continuous.

[ad-hoc note] Added F10 `TerrainFaceId` mode 34. `PlanetVertexColor.shader` colors terrain by dominant cube-sphere face, `Ocean.shader` hides the water surface, and `WaterVolume.shader` bypasses. If the square edge aligns with a color boundary in `TerrainFaceId`, the next fix should connect or derive water classification across face boundaries or compute shore distance in a global direction-space pass.

[ad-hoc note] Next F10 review should compare `Off`, `WaterOff`, `VolumeMask`, `VolumeBoundary`, and `TerrainFaceId` first. If `TerrainFaceId` lines match the square/shore artifact, no more broad screenshots are needed before changing `WaterMeshBuilder` topology.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel build attempt hit the known shared intermediate DLL write collision. No trailing whitespace was found in `FreeCameraController.cs`, `PlanetVertexColor.shader`, `Ocean.shader`, `WaterVolume.shader`, or `WaterMeshBuilder.cs`.
