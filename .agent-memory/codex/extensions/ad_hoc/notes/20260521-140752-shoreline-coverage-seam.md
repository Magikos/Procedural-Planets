# ProceduralPlanets Shoreline Coverage Seam

[ad-hoc note] Bryan's F10 set around `20260521-135717` showed the missing clue: the visible shoreline-like line is not only bright terrain source color. It also tracks the water/shore boundary in `Absorption`, `VolumeMask`, `VolumeBoundary`, `VolumeOptical`, `VolumeContact`, and `VolumeDilation`.

[ad-hoc note] `TerrainSourcePink` still proves the underlying color is terrain and `FoamPink` still does not mark the contour, but the consistent volume-mask correlation means the clipped shoreline/prepass edge is exposing terrain source.

[ad-hoc note] `WaterMeshBuilder` now pushes clipped shoreline vertices farther under dry terrain with overlap `shoreRange * 0.22`, clamped by planet scale. Boundary vertices now encode small non-zero depth and shore values instead of exact `0,0`, so edge pixels that survive terrain depth do not produce a hard water-data line.

[ad-hoc note] This mesh change requires planet/water regeneration. Bryan's scene regenerates the planet at game start after deleting the baked planet object, so a fresh play session should pick it up automatically.

[ad-hoc note] Next F10 review should compare `Off`, `VolumeOnly`, `VolumeMask`, `VolumeBoundary`, `VolumeOptical`, `VolumeDilation`, `TerrainSourcePink`, and `FoamPink`. If the line remains identical, the next target is water-volume prepass depth/coverage rather than source matte or foam.

[ad-hoc note] Verification: `dotnet build ProceduralPlanets.Planet.csproj` passed and no trailing whitespace was found in `WaterMeshBuilder.cs` or `WaterVolume.shader`.
