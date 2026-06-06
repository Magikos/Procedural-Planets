# 2026-06-06 - Biome overhaul slice 1b: latitude climate

**Status:** Implemented and visually validated with StrongBands and Earthlike F10 sets.
**Design source:** [`docs/design/2026-06-05-biome-climate-overhaul.md`](../design/2026-06-05-biome-climate-overhaul.md) section 3.4.1 / Step 1b.
**Previous slice:** [`2026-06-06-biome-1a-biome-offset.md`](2026-06-06-biome-1a-biome-offset.md).

## What landed

- `TemperatureLatitudeCurve` and `MoistureLatitudeCurve` are serialized on `BiomeSettings`.
- Curves are baked into immutable 256-sample LUTs before chunk generation enters worker threads.
- Temperature now supports normalized altitude lapse above `BiomeRegistry.OceanThreshold`.
- Moisture can blend from the legacy noise-only field into latitude bands plus centered noise.
- `ClimateSample` carries the final climate plus latitude, curve, noise, and altitude contributions.
- `BiomeAltitudeCooling` was added to the default Biome F10 capture set.
- Latitude debug no longer consumes a mesh channel; it derives angular latitude from world position.
- Runtime `climate.*` commands can inspect, tune, preset, and regenerate the climate model.

## Compatibility

The checked-in asset intentionally preserves the tested planet:

- Temperature curve is the old linear equator-to-pole function.
- `AltitudeTemperatureDrop = 0`.
- `MoistureLatitudeInfluence = 0`, which uses the previous moisture calculation exactly.

The first play after this change should therefore look materially unchanged until a preset is applied.

## Presets

`Legacy`

- Linear temperature latitude curve.
- Legacy noise-only moisture.
- No altitude cooling.

`Earthlike`

- Authored equator-to-pole temperature curve.
- Wet equator, dry subtropics, wetter temperate band, drier poles.
- 70% latitude-band moisture influence.
- Altitude lapse of 2.5 normalized temperature units per elevation unit.

`StrongBands`

- Diagnostic preset with full latitude moisture ownership and stronger altitude cooling.
- Intended to make signal ownership obvious, not as the final art setting.

## Console commands

```text
climate.status
climate.preset Earthlike
climate.preset StrongBands
climate.preset Legacy
climate.altitude-lapse [value]
climate.moisture-bands [value]
climate.moisture-noise [value]
climate.temperature-noise [value]
climate.lut-resolution [value]
climate.temperature-point <latitude01> <value01>
climate.moisture-point <latitude01> <value01>
climate.apply
```

Setting commands do not regenerate automatically. `climate.apply` performs the normal cancellable planet generation. Mutations are rejected while generation is already running.

## First validation

1. Enter play mode and run `climate.status`.
2. Confirm the default planet still resembles the pre-1b baseline.
3. Run `climate.preset StrongBands`, then `climate.apply`.
4. Take the default Biome F10 capture.
5. Verify:
   - `BiomeLatitude` is continuous across cube faces and symmetric north/south.
   - `BiomeTemperature` broadly cools toward the poles.
   - `BiomeMoisture` shows latitude bands broken up by noise rather than pure horizontal stripes.
   - `BiomeAltitudeCooling` is black/blue at sea level and increases only on elevated land.
6. Run `climate.preset Earthlike`, then `climate.apply` for the first art-facing result.

## Diagnostic interpretation

- Altitude cooling appears at sea level: the elevation threshold/domain is wrong.
- Latitude or moisture has cube-face seams: the problem is coordinate sampling, not biome lookup.
- StrongBands changes climate debug but not biome IDs: inspect registry thresholds/assignment next.
- Default output changes before a preset: compatibility was broken; compare LUT temperature against the old linear formula and verify moisture influence is zero.

## Verification completed

```text
dotnet build ProceduralPlanets.Core.csproj --no-restore
dotnet build ProceduralPlanets.Planet.csproj --no-restore
```

Both pass with zero warnings and zero errors. Unity must still reimport the HLSL/shader edits and perform the F10 validation above.

## Next slice after validation

Slice 1c is implemented in [`2026-06-06-biome-1c-voronoi.md`](2026-06-06-biome-1c-voronoi.md). It consumes the validated climate inputs behind a DirectClimateGrid/Voronoi A/B flag.

## F10 validation result - 2026-06-06

Bryan captured two complete Biome sets:

- `20260606-135911` through `135918`: StrongBands.
- `20260606-140118` through `140125`: Earthlike.

Both were surface views near 66 degrees north on the same generated seed.

Findings:

- `BiomeLatitude` is smooth and continuous across the visible terrain. No cube-face or chunk seam appears in the latitude signal.
- `BiomeTemperature` follows the high-latitude input and changes between the StrongBands and Earthlike curves.
- `BiomeMoisture` responds to the preset change. StrongBands is more latitude-owned; Earthlike retains more local noise variation.
- `BiomeAltitudeCooling` follows terrain elevation and is stronger in StrongBands than Earthlike, matching lapse values 4.0 versus 2.5.
- `BiomeMapPrimaryId`, `BiomeMapBlend`, and `BiomeMapFlatColor` all respond coherently to the changed climate field. The existing blend path remains smooth even where the dominant biome ID changes sharply.
- No evidence was found of altitude cooling below the ocean threshold or a climate coordinate seam.

Slice 1b passes its visual gate. One coverage limitation remains: both captures are high-latitude local views. Slice 1c validation should include an orbit-scale capture showing equator, subtropics, temperate bands, and pole in one frame.
