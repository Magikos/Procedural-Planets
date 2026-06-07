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

## Follow-up: dedicated mid-field card layer

Commit `f04972d` preserves the validated runtime-allocation checkpoint. The next
working-tree slice adds a dedicated GPU mid-field path without removing or
suppressing the legacy chunk path.

Implemented architecture:

- `GrassMidFieldController` owns one shared 200,000-instance card buffer
  (approximately 9.2 MB), one indirect-argument buffer, and one stats buffer.
- The controller is camera-centered and uses stable face-space cells at 2 m
  spacing. It dispatches every cube-face range returned by
  `FaceSpaceCellRangeBuilder`, rather than only the primary face.
- The intended band is 75-450 m: cards fade in over 75-125 m, remain dense
  through 260 m, and fade into the terrain blanket over the final 100 m.
- `GrassMidFieldPlace.compute` samples the same face biome, radius, and normal
  atlases as the near-field path. Placement remains seed-stable and applies
  biome density, water, slope, distance, and cube-face area gates.
- `GrassMidField.shader` draws one cylindrical camera-facing procedural clump
  card per accepted cell. It uses shared grass dither, planet day/night
  lighting, fog, wind globals, and the reserved interactor hook.
- Mid-field allocation activates below 650 m terrain altitude and releases
  above 850 m. Orbit should therefore report the mid controller as requested
  but inactive with no mid buffer.
- The current surface-state mask is still chunk-local. Mid-field path/scorch
  rejection is intentionally deferred until a face-space state atlas exists;
  the implementation does not pretend that data is available.

Runtime control now includes:

```text
grass.layer Mid [true|false]
```

The Grass F10 sidecar now includes `--- GrassMidField ---` with quality, face
range, dispatch, draw, buffer, rejection, and overflow counters.

### First Unity test

1. Let Unity finish importing and confirm there are no
   `GrassMidField.shader` or `GrassMidFieldPlace.compute` errors.
2. Go to a grassy surface view with a visible hill beyond the near blades.
3. Run `grass.layer Chunk false` so near + mid + blanket are isolated.
4. Take a Grass F10 while looking across the hill.
5. Run `grass.layer Mid false` from the same camera position and take another
   Grass F10.
6. Restore `grass.layer Mid true` and `grass.layer Chunk true`.

The first acceptance gate is structural, not art tuning:

- Mid cards remain fixed to the terrain while moving.
- `facesActive` rises above one near cube-face edges without a bare seam.
- `emitted` is non-zero, `overflow=0`, and buffer is about 9.2 MB.
- The 120-450 m hillside has visible geometric coverage when chunk grass is
  disabled.
- No hard empty band appears at either the near/mid or mid/blanket handoff.

Managed build verification passed for Core and Planet. Unity shader import and
the F10 A/B remain required.

### F10 result

The 2026-06-06 22:41 A/B validated the isolated mid-field layer:

- Mid on: 51,006 emitted cards from a 200,000-card capacity.
- Buffer: 9.2 MB.
- Overflow: 0.
- Candidate cells: 360,000.
- The `GrassLodCoverage` pair clearly shows the added mid band between the near
  geometry and the red far-only region.
- The production `Off` pair shows additional geometric hillside texture with
  mid enabled and no hard empty ring at either handoff.
- The legacy chunk path was disabled in both captures, so the result belongs to
  near + mid + blanket rather than hidden chunk fallback.

This is sufficient to make the legacy chunk layer disabled by default. It
remains available through `grass.layer Chunk true` for regression comparison.

The production capture is hazier than `AtmosphereBypass`, proving the wash is
owned by atmospheric aerial perspective rather than the grass layers. No
atmosphere values were changed because the atmosphere remains visually correct
in the broader scene and this grass slice should not retune it.
