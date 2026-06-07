# 2026-06-06 - Biome slice 1d: climate-aware frozen water

## Status

Implemented by Codex and awaiting the first Unity capture.

Checkpoint before this slice:

- `f4e716f Add climate curves and Voronoi biome assignment`

## Trigger

F10 `20260606-164137` showed a liquid inland lake at latitude `-72.89` inside
a snow-covered polar region. The camera sample reported `body=0`, confirming
that the existing water graph classifies this as a small inland body rather
than part of the global ocean.

## Implementation

- `PlanetSettings` now owns serialized frozen-water thresholds and ice
  appearance settings.
- `ColorGenerator` exposes its initialized immutable climate provider to the
  planet-side water builder.
- `WaterMeshBuilder` samples climate once per unique wet graph vertex.
- Small connected water bodies receive one component-average temperature.
- Ocean water retains local climate temperature for regional polar sea ice.
- Intermediate body sizes blend component and local temperature by the
  existing body factor.
- Vertex color alpha stores effective water temperature. RGB remain depth,
  shore distance, and body factor.
- `Ocean.shader` derives freeze factor from temperature plus lake/ocean
  thresholds.
- Ice suppresses swell, ripple normals, wave energy, foam, and liquid glint.
- The water-volume prepass carries freeze factor to the composite, which
  suppresses liquid caustic and refraction distortion while preserving the
  underlying water volume.
- Ice receives static breakup, roughness, normal, opacity, tint, sunlight, and
  cloud-shadow treatment.
- The water volume remains beneath the frozen surface.

## Diagnostics

New modes:

- `WaterTemperature`
- `WaterFreeze`
- `WaterIceContribution`

New capture set:

- `Frozen Water`

Run:

```text
debug.capture-set "Frozen Water"
debug.capture
```

The sidecars include:

- mesh temperature min/max/average;
- camera-sampled water temperature;
- lake and ocean threshold pairs;
- frozen, partial, and liquid connected-body counts.

## First validation

Return to the same polar lake and capture `Frozen Water`.

Expected:

1. `WaterBody` identifies the lake as a small body.
2. `WaterTemperature` is cold and coherent across the lake.
3. `WaterFreeze` is bright across the complete connected lake.
4. `WaterIceContribution` is coherent with only broad irregular breakup near
   a partial-freeze boundary.
5. Production `Off` shows static ice with no liquid swell, foam, or glint.
6. A warm ocean remains liquid.

Do not tune ice color or thresholds until those gates identify whether any
failure starts in climate, component classification, freeze derivation, or
presentation.

## First capture result

F10 `20260606-184523` through `20260606-184527` validates the static slice:

- production ice reads correctly;
- body counts are `1 frozen / 0 partial / 9 liquid`;
- generated water temperature spans `0.049-1.000`;
- the cold polar region is coherent in `WaterTemperature`;
- `WaterFreeze` and `WaterIceContribution` agree spatially;
- the frozen region has no foam contribution.

The capture also exposed a console usability bug: popup completion inserted
`Frozen Water` without quotes, so the binder treated `Water` as an extra
argument. The console now quotes multi-word string completions and accepts the
remaining command tail for a final string parameter. Both of these are valid:

```text
debug.capture-set "Frozen Water"
debug.capture-set Frozen Water
```

## Verification completed

- `ProceduralPlanets.Core.csproj`: build succeeds with zero warnings/errors.
- `ProceduralPlanets.Planet.csproj`: build succeeds with zero warnings/errors.
- Unity DX12 shader compiler log: modified `Ocean.shader`,
  `WaterVolumePrepass.shader`, and `WaterVolume.shader` compile successfully.

## Deferred

Seasonal thawing, runtime state atlases, cracking, footsteps, breakable ice,
and walkable collision remain outside static slice 1d.
