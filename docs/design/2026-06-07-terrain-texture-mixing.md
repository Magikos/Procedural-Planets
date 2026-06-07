# Terrain Texture Mixing

**Date:** 2026-06-07  
**Status:** Phase 1 implementation

## Problem

The terrain shader already blends the top four biome weights and uses triplanar
albedo, normal, and ARM textures. Its anti-tiling layer previously sampled the
same albedo texture at a second scale. That changed the apparent frequency, but
it could not create meaningful material variation and left repeated source
features recognizable.

## Phase 1: Authored Secondary Albedo

Each `BiomeDefinition` may provide `SurfaceSecondaryAlbedo`.

- Primary albedo slices occupy `[0, BiomeCount)` in `_BiomeAlbedoArray`.
- Secondary albedo slices occupy `[BiomeCount, BiomeCount * 2)`.
- A missing secondary texture copies the primary texture into the secondary bank.
- The shader's existing macro-noise blend selects between the two banks.
- This replaces the old same-texture second-scale sample and does not add
  fragment texture taps.

The second bank does increase albedo-array memory. The current Standalone
import ceiling is 2048 pixels with block compression, so the expected increase
is approximately 40-80 MB depending on the selected platform format. Record
graphics memory in the first Unity validation before accepting this phase.

Secondary normal and ARM textures are intentionally deferred. Sampling a second
complete triplanar PBR material in every active top-K biome would materially
increase the terrain fragment cost. Add those channels only after Unity GPU
profiling demonstrates that the added samples fit the frame budget.

`BiomeDefinition.SurfaceTiling` remains reserved for per-biome scale authoring.
It is not part of this slice because reading per-biome parameters in every
corner and top-K contribution needs its own performance validation.

## Authored Pairing

The initial pairings use the existing texture library:

| Biome | Primary | Secondary |
|---|---|---|
| Beach | Sand | Wet Sand |
| Desert | Sand | Cracked Dirt |
| Forest | Grass | Dirt |
| Grassland | Grass | Dirt |
| Ice Bog | Ice | Snow |
| Mountain | Rock | Snowy Rock |
| Ocean | Wet Sand | Sand |
| Savanna | Cracked Dirt | Grass |
| Scrub | Rocky Dirt | Dirt |
| Snow | Snow | Snowy Rock |
| Steppe | Cracked Dirt | Dirt |
| Swamp | Wet Mud | Grass |
| Taiga | Grass | Snowy Rock |
| Tropical | Grass | Wet Mud |
| Tundra | Dirt | Snowy Rock |

These are structural defaults, not final art direction.

## Validation

Use the `Terrain Textures` F10 capture set from both ground level and a low
oblique flight view.

1. `BiomeMapBlend` verifies that any hard region edge originates in biome data.
2. `TerrainPrimaryAlbedo` shows the primary texture bank before geography and
   grass overlays.
3. `TerrainMixedAlbedo` shows the same terrain with the secondary bank enabled.
4. `TerrainSelectedAlbedo` verifies the final selected albedo after downstream
   terrain and grass overlays.
5. `TerrainSurfaceNormal`, `TerrainSurfaceAO`, and
   `TerrainSurfaceRoughness` verify that the primary PBR channels remain intact.
6. `TerrainFaceId` checks that no cube-face discontinuity was introduced.
7. `Off` is the production appearance and performance comparison.
8. Compare graphics-driver memory and terrain GPU time against the preceding
   checkpoint. Reject the bank if the measured cost is disproportionate to the
   visible improvement.

Do not tune mix strength until the capture proves whether the remaining
repetition comes from source textures, biome boundaries, geography overrides,
or the material-mix layer itself.
