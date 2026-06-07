# 2026-06-06 - Biome slice 1e: terrain geography overrides

## Status

Implemented by Codex. Core and Planet builds pass. Unity shader import and the
first Terrain Geography F10 gate passed.

Checkpoint before this slice:

- `d2b28d6 Add climate-aware frozen water`

## Goal

Make the generated biome field read as geography rather than only as blended
colored regions. Coast, exposed rock, and snow are independent surface
conditions and must not depend on whichever biome ID happens to dominate a
texel.

## Implementation

- `PlanetSettings.SurfaceOverrides` owns the mask thresholds and tiling.
- Coast coverage uses terrain height relative to the generated sea-level
  radius. It spans shallow seabed through low beach terrain and fades out
  inland.
- Exposed rock coverage uses the angle between the terrain normal and the
  planet radial normal.
- Snow coverage uses the final climate temperature already stored in mesh UV2.
  This includes latitude, noise, and altitude cooling from slice 1b.
- The renderer reuses the existing texture-array slices:
  - coast: Beach
  - slope: Mountain
  - snow: Snow
- Each override samples albedo, normal, and ARM with its own triplanar tiling.
- Composition order is coast, then slope, then snow.
- The far grass terrain blanket is applied before the overrides, so rock and
  snow are not painted green by the distant-grass approximation.
- No new texture arrays or per-chunk textures were allocated.

## Initial settings

```text
Coast: -8m below sea through 2m above sea, fading out by 20m
Slope: begins at 28 degrees, full exposed rock at 48 degrees
Snow: full at temperature <= 0.28, faded out by temperature 0.42
```

These are first-light defaults. Validate signal ownership before art tuning.

## Diagnostics

New modes:

- `TerrainCoastMask`
- `TerrainSlopeMask`
- `TerrainSnowMask`
- `TerrainOverrideComposite`

The composite displays coast in red, slope in green, and snow in blue.

New capture set:

```text
debug.capture-set Terrain Geography
debug.capture
```

Water surface and water volume are suppressed only while the four new terrain
mask modes are active. This exposes the shallow coast band without changing
production rendering.

F10 sidecars include the enabled state, slice IDs, thresholds, and tiling.

## First validation

Take one capture from a view containing shoreline plus a steep hill. Take a
second capture in a cold region that includes lowlands and elevated terrain.

Pass conditions:

1. `TerrainCoastMask` follows sea-relative height, not biome borders.
2. `TerrainSlopeMask` follows cliffs and ridges without chunk or LOD seams.
3. `TerrainSnowMask` agrees with `BiomeTemperature` and changes smoothly.
4. `TerrainOverrideComposite` contains no cube-face-aligned or chunk-aligned
   discontinuity.
5. `TerrainSelectedAlbedo` proves the three texture layers reach the final
   terrain albedo.
6. Production `Off` has no hard rings at any threshold.

Do not tune widths or colors if a mask is attached to the wrong feature. Fix
the owning signal first.

## First F10 result

Four complete Terrain Geography sets were captured:

- `20260606-195544` through `195547`: cold polar shoreline, close view.
- `20260606-195602` through `195605`: temperate shoreline and hill.
- `20260606-195638` through `195641`: high-latitude terrain from altitude.
- `20260606-195659` through `195703`: warm orbit-scale view.

Findings:

- `TerrainCoastMask` follows the generated sea-level contour in all four views.
  Its width changes with terrain grade rather than biome ID, as intended.
- `TerrainSlopeMask` selects isolated ridges and steep faces. It does not form
  chunk-aligned blocks or latitude bands.
- `TerrainSnowMask` agrees with `BiomeTemperature`: the two cold views are
  broadly snow-covered, the temperate view retains only a colder/elevated
  region, and the warm orbit view is almost entirely clear.
- `TerrainOverrideComposite` combines the masks without a visible cube-face or
  chunk seam. Red is coast, green is slope, and blue is snow.
- `TerrainSelectedAlbedo` confirms Beach, Mountain, and Snow texture-array
  slices reach the final terrain surface.
- Production `Off` has no new threshold ring or LOD handoff artifact.

The architecture gate passes. Broad uninterrupted snow in deeply cold regions
is correct for the current temperature-only deposition model. Later art work
may add wind exposure, slope retention, and low-frequency breakup, but those
are presentation features rather than corrections to this slice.

## Known follow-up

Near-field grass geometry is still placed from biome suitability, not the snow
mask. The far terrain blanket is correctly covered by snow and rock in this
slice. If validation shows blades crossing fully snow-covered terrain, route
the same climate/surface mask into grass placement in the grass tuning slice.
