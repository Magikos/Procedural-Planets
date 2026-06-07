# 2026-06-06 - Grass Runtime Control and Allocation

**Status:** Implemented and Unity/F10 validated.

## Why this slice exists

The latest Terrain Geography F10 sidecars showed the legacy per-chunk grass
renderer holding 305 full-capacity buffers at orbit:

- 915.022 MB allocated
- 305 draw submissions
- 0 emitted instances

The near-field renderer also retained its fixed 45.8 MB buffer while the camera
was far above the surface and emitted no instances.

## Decisions

1. Keep the legacy chunk renderer enabled for the current mid-field band. The
   A/B capture proved that near grass plus the terrain blanket do not yet replace
   its visual contribution.
2. Do not delete the chunk renderer until F10 proves that near grass plus the
   terrain blanket cover its visual role.
3. When enabled, allocate chunk buffers only for visible eligible chunks whose
   world bounds intersect the 600 m render range.
4. Release chunk buffers outside a 650 m boundary. The 50 m hysteresis prevents
   allocation churn while moving near the render limit.
5. Allocate the fixed near-field buffer only below 350 m terrain altitude and
   release it above 500 m.
6. Keep the terrain blanket enabled by default because it has no per-chunk
   allocation and supplies the orbital/far-field grass read.

## Runtime commands

```text
grass.status
grass.enabled [true|false]
grass.layer Near [true|false]
grass.layer Chunk [true|false]
grass.layer Blanket [true|false]
```

`grass.enabled` is a master switch. Re-enabling it restores the requested layer
configuration.

## F10 metadata

The Grass capture now starts with:

```text
--- GrassRuntime ---
Master: enabled=True
Layers: near=True/False requested/active, chunk=False/False, blanket=True/True
```

Requested state is the configured layer state. Active state means its runtime
controller or shader contribution currently exists. Near grass is expected to
be requested but inactive at orbit.

The Grass capture no longer stops reporting when the legacy chunk controller is
absent. Near-field, atmosphere, and scale-reference metadata still follow.

## Validation

## Validation result

The 2026-06-06 F10 comparison established:

- Orbit: near field inactive, chunk layer disabled for the test, no grass
  controllers or grass buffers, graphics-driver memory 3.55 GB.
- Surface, chunk off: near field emitted 416,921 roots and held 45.8 MB.
- Same surface view, chunk on: only 15 of 111 visible chunks allocated buffers,
  adding 81,569 roots and 244,707 visual blades for 45.0 MB.
- Graphics-driver memory increased from 3.60 GB to 3.71 GB with chunk grass on,
  rather than the previous 915 MB reported legacy allocation.
- Comparable chunk-off/chunk-on captures were 53.6 and 55.8 FPS. This is not a
  benchmark, but it shows no immediate frame-rate regression from the 15 nearby
  chunk draws.

Visually, chunk-on clearly restored the missing grass across the hillside beyond
the 120 m near-field range. Therefore chunk grass is enabled by default until a
dedicated mid-field renderer replaces it. The runtime toggle remains available
for future A/B tests.

## Follow-up: distant blanket hidden by terrain overrides

The next orbit F10 validated the allocation gates with the chunk controller
enabled:

- visible chunks: 164
- tracked chunks: 0
- draw calls: 0
- chunk grass buffer: 0 MB
- near-field controller: missing

The accompanying surface F10 still showed a pale distant hill despite
`GrassLodCoverage` reporting strong vegetation coverage. The cause was shader
composition order:

1. `ApplyFarGrassOverlay` painted the biome surface green.
2. `ApplyTerrainOverrides` then repainted coast, slope, and snow textures over
   that grass blanket.
3. Real blade geometry did not receive the same repaint, so the near and far
   representations disagreed.

`PlanetVertexColor.shader` now applies the terrain material overrides first and
the grass blanket second. The blanket's existing biome-density, slope, and water
clearance gates still determine where it contributes. This is an ordering fix;
coverage strengths and biome grass authoring values were not tuned.

## Build verification

- `dotnet build ProceduralPlanets.Core.csproj` - passed
- `dotnet build ProceduralPlanets.Planet.csproj` - passed

Unity shader import and F10 visual/runtime validation are still required.
