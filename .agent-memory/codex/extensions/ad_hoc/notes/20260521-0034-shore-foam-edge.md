# ProceduralPlanets Shore Foam Edge

[ad-hoc note] Bryan repeated above/below F10 captures and also captured closer to the artifact. The close-up set around `20260521-002432` showed the bright edge tracking `FoamParts` and `SurfaceAlpha` at the exact shoreline, not only volume diagnostics.

[ad-hoc note] Current diagnosis: above-water artifact is mainly a hard shoreline foam/surface band at the terrain-water intersection. Volume diagnostics still light up because they share shoreline water data, but the visible close-up edge is foam/surface driven.

[ad-hoc note] `Ocean.shader` `ComputeShoreFoam` now clears foam away from the exact terrain intersection with `edgeClear`, shifts the shoreline band slightly into the water, and reduces `lipFoam`. The goal is broken water-side wash instead of a continuous white/yellow terrain edge.

[ad-hoc note] `Planet.cs` reduced `WaterShoreFoamDepth` from 48 to 32 at radius 5000 scale to narrow the shore foam band. Regenerate the planet/water material for this constant to apply.
