# 2026-06-06 - Biome overhaul slice 1c: global Voronoi assignment

**Status:** Implemented behind an A/B feature flag; orbit and surface F10 validation passed.
**Design source:** [`docs/design/2026-06-05-biome-climate-overhaul.md`](../design/2026-06-05-biome-climate-overhaul.md) section 3.4 / Step 1c.
**Previous slice:** [`2026-06-06-biome-1b-climate-latitude.md`](2026-06-06-biome-1b-climate-latitude.md).

## What landed

- One deterministic global Fibonacci seed field is built per planet.
- Seed positions receive tangent-space jitter from the `BiomeVoronoi` system seed.
- Each seed samples the trusted slice 1b climate model and selects the nearest registry-grid climate target.
- Five simultaneous majority-cleanup iterations remove isolated thin biome assignments.
- Three independent simplex fields produce tangent-projected spherical domain warp.
- An exact immutable 3D KD-tree resolves terrain-vertex primary and secondary land biomes.
- Ocean, beach, mountain, and snowy-mountain overrides still use the existing centralized elevation rules.
- Chunk biome maps sample one immutable 512x512x6 primary-ID atlas instead of performing roughly forty million exact KD-tree searches.
- Atlas samples near cube-face borders fall back to the exact KD-tree to prevent face quantization seams.
- Padded chunk-map neighborhoods sample the global field beyond chunk boundaries.
- The old direct climate-grid resolver remains available for visual A/B comparison.

## Performance model

The expensive work happens during planet generation:

1. Build and climate-classify about 2,000 seeds.
2. Run cleanup and build the exact KD-tree.
3. Build a 1.5 MiB global primary-biome atlas in parallel.
4. Bake the existing per-chunk top-K blend textures from fast atlas lookups.

Steady-state rendering still uses the existing per-chunk textures. There is no per-frame Voronoi search.

## Defaults

After orbit and surface validation, the checked-in asset uses:

```text
AssignmentMode = Voronoi
VoronoiSeedCount = 2048
VoronoiSeedJitter = 0.55
VoronoiTemperatureWeight = 4.26
VoronoiDomainWarpStrength = 0.08
VoronoiDomainWarpScale = 2.5
VoronoiDomainWarpOctaves = 4
VoronoiCleanupIterations = 5
```

Direct mode remains available only as the legacy regression baseline.

## Console commands

```text
climate.assignment DirectClimateGrid
climate.assignment Voronoi
climate.voronoi-seeds [128..8192]
climate.voronoi-jitter [0..1]
climate.voronoi-warp [0..0.25]
climate.status
climate.apply
```

Setting commands do not regenerate automatically.

## First validation

1. Enter play mode with the same planet seed used for slice 1b.
2. Run `climate.assignment DirectClimateGrid`, then `climate.apply`.
3. Take one orbit-scale Biome F10 set as the baseline.
4. Run `climate.assignment Voronoi`, then `climate.apply`.
5. Take one orbit-scale Biome F10 set and one surface set crossing a visible biome boundary.
6. Verify:
   - `BiomeTemperature`, `BiomeMoisture`, `BiomeLatitude`, and `BiomeAltitudeCooling` are unchanged between assignment modes.
   - `BiomeMapPrimaryId` changes from threshold bands into larger organic regions.
   - `BiomeMapBlend` remains smooth at region boundaries and chunk boundaries.
   - No cube-face seam appears in primary ID, blend, or flat color.
   - Ocean, beach, mountain, and snow overrides still follow elevation.
   - Reapplying Voronoi with the same world seed reproduces the same regions.
   - Planet generation does not regress into a long map-bake stall.

## F10 metadata

Every capture now appends:

```text
--- Biome Assignment ---
Mode: DirectClimateGrid|Voronoi
Voronoi: seeds=..., distinct=..., cleanupChanges=..., atlas=512x512x6, buildMs=...
```

Use `buildMs` to separate global-field construction cost from the remaining planet generation phases.

## Diagnostic interpretation

- Climate modes change between A/B runs: the assignment feature flag is leaking into climate generation.
- Primary IDs are organic but final color is banded: inspect map bake or shader selection, not seed generation.
- A line appears only at a cube-face edge: inspect atlas seam fallback and face-coordinate conversion.
- A line appears at ordinary chunk edges: inspect padded elevation overrides and neighboring chunk inputs.
- Regions are dominated by one biome: inspect distinct count and climate-target coverage before tuning warp.
- Many tiny islands remain: inspect cleanup changes and neighbor voting before increasing blend width.
- Build time is acceptable but planet generation still stalls: profile per-chunk texture baking; the KD-tree is no longer its hot path.

## Verification completed

```text
dotnet build ProceduralPlanets.Core.csproj --no-restore
dotnet build ProceduralPlanets.Planet.csproj --no-restore
```

Both pass with zero warnings and zero errors. Unity shader import and the F10 comparison remain required.

## Orbit F10 result - 2026-06-06

Capture set `20260606-145905` through `145911` validates the first half of the
slice 1c visual gate.

- Metadata confirms the Voronoi path was active: 2,048 seeds, 12 distinct land
  biomes, 112 cleanup changes, a 512x512x6 lookup atlas, and 5,231 ms global
  field build time.
- Temperature, moisture, latitude, elevation, and altitude-cooling diagnostics
  remain coherent at orbit scale.
- Primary biome regions are broad and organically warped rather than direct
  climate-grid bands.
- The baked primary map follows the vertex result. Small boundary displacement
  is the existing distinction between interpolated vertex data and the
  neighborhood-filtered top-K map, also visible in the direct-lookup baseline.
- The blend diagnostic shows expected lower dominant weights along Voronoi
  boundaries, and the flat-color result turns those boundaries into smooth
  transitions.
- No cube-face seam or ordinary chunk seam is visible in primary ID, blend, or
  flat color.
- Runtime memory did not regress in this comparison; the global byte atlas is
  approximately 1.5 MiB.

Remaining gate: take a surface-level Biome F10 set while looking across a
clearly visible biome boundary. This is required to judge transition width,
texture handoff, and any local atlas quantization that an orbit view can hide.

## Surface F10 diagnosis and fix - 2026-06-06

Capture set `20260606-155704` through `155707` exposed a straight hard strip at
a cube-face boundary. This was not a blend-width or lighting issue:

- `BiomeMapPrimaryId` showed a narrow third-biome strip.
- `BiomeMapBlend` showed the same straight boundary structure.
- `TerrainFaceId` confirmed alignment with a cube-face edge.
- Surface normals did not originate the color transition.

Root cause: the atlas was generated with
`CoordinateConverter.CubeFaceToUnitSphere`, but sampled with
`CoordinateConverter.UnitSphereToCubeFace`. Those methods use different UV
orientations and are not inverses. The temporary exact-KD seam fallback
therefore produced a correct strip between incorrectly addressed atlas faces.

Correction:

- Voronoi atlas lookup now uses an explicit inverse of the exact cube-face basis
  used to generate the atlas.
- The exact-strip fallback was removed.
- Atlas edge texels are sampled at UV 0 or 1 so adjacent faces generate their
  shared edge from identical unit-sphere directions.

No biome density, blend radius, seed count, or domain-warp tuning changed.
Core and Planet builds pass with zero warnings and zero errors after the fix.
Retest the same surface viewpoint; the straight strip should disappear while
the organic biome boundary and smooth top-K transition remain.

## Surface retest result - 2026-06-06

Capture set `20260606-162054` through `162058` passes the close-range gate.

- The narrow straight cube-face strip from the previous capture is gone.
- `TerrainFaceId` is uniform across the visible land, confirming the view does
  not contain an unresolved face-boundary artifact.
- `BiomeMapPrimaryId` still shows hard categorical borders by design; it
  displays only the dominant biome ID.
- `BiomeMapBlend` shows continuous lower-weight bands around those borders
  without the former straight face-aligned strip.
- `WaterOff` and the production view show broad, smooth biome transitions.
- Surface normals do not introduce a corresponding hard edge.

Slice 1c passes its orbit and surface visual gates. The feature flag can now be
promoted to Voronoi by default; retain DirectClimateGrid temporarily only as a
regression comparison until the next checkpoint commit.

## Default promotion - 2026-06-06

Voronoi is now the default in both the `BiomeSettings` C# initializer and the
checked-in `BiomeSettings.asset`. `DirectClimateGrid` remains selectable through
`climate.assignment DirectClimateGrid` for controlled regression comparisons.

## Follow-up captured from validation

Surface F10 `20260606-164137` shows a liquid inland lake surrounded by polar
snow terrain near 73 degrees south. The climate output is now trustworthy, so
the next consumer slice is climate-aware frozen water.

This is tracked as slice 1d in
[`docs/design/2026-06-06-climate-frozen-water.md`](../design/2026-06-06-climate-frozen-water.md).
It intentionally follows the 1b/1c checkpoint commit rather than expanding the
already validated Voronoi change set.
