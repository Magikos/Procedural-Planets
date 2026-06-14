# ProceduralPlanets Global Water Graph Pass

[ad-hoc note] Bryan's latest F10 set around `20260521-142601` and `20260521-142628` still showed the same shoreline/line artifact. The lack of visible change after source-matte, overlap, and shader tuning is a strong signal that constants are not touching the root cause.

[ad-hoc note] The square-ish shore/coverage shape is still strongest in `VolumeMask` and faintly present in `Off`/`WaterOff`. `TerrainFaceId` helps keep the next diagnosis focused on cube-sphere face/grid boundaries and water data continuity rather than foam.

[ad-hoc note] `WaterMeshBuilder` was changed to build a global direction-space water graph across all six terrain faces. Wet body classification, shore-distance BFS, original water vertices, and clipped shoreline edge vertices now share global direction keys across cube-face borders instead of being computed independently per `TerrainFace`.

[ad-hoc note] Expected next test: start a fresh play session so the planet and water mesh regenerate, then run the normal F10 `WaterArtifact` set. The mesh vertex count should change from the previous `219813` if the global vertex sharing path is active. Compare `Off`, `WaterOff`, `VolumeMask`, `VolumeBoundary`, and `TerrainFaceId` first.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed after the global water graph patch. No trailing whitespace was found in `WaterMeshBuilder.cs`.
