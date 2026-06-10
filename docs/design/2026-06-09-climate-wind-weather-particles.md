# Climate, Wind, and Local Weather Particles

## Goals

- Give weather and gameplay a world-space climate query that agrees with biome generation.
- Use physical wind speed in meters per second throughout the runtime.
- Keep Celsius as the canonical simulation temperature while allowing Celsius or Fahrenheit display.
- Bake the static planet climate into a small GPU texture for spatial weather rendering.
- Share one procedural local-particle renderer across dust, nearby rain, and later snow.

## Temperature Ownership

`ClimateProvider` remains the authoritative static climate model. It evaluates normalized
temperature and moisture from latitude, seeded noise, and normalized terrain elevation.

`Planet` exposes that model through `IClimateSampler.TrySampleClimate(worldPosition, ...)`.
The world-space adapter samples the underlying terrain radius and converts it back to the
normalized elevation used during biome generation. Camera altitude must not be used for this
query because it would make the climate colder as the camera rises.

Climate samples carry both:

- `Temperature01` for existing biome thresholds and normalized masks.
- `TemperatureCelsius` for weather, freezing, precipitation, and gameplay.

Each planet authors the Celsius values represented by normalized 0 and 1. Fahrenheit is a
player presentation preference and is converted only when values are formatted for display.

## GPU Climate Map

The CPU climate provider remains the source of truth. Planet generation also builds a
six-slice `RGHalf` `Texture2DArray`:

- R: normalized temperature.
- G: normalized moisture.
- Resolution: 256 pixels per cube-sphere face by default.
- Filtering: bilinear with mipmaps.

The texture uses the project's established `CubeFaceToUnitSphere` and `CubeFaceUv`
conventions. This avoids adding Unity cubemap face orientation as a second mapping convention.
It is rebuilt after terrain and climate generation and disposed on regeneration or teardown.

At 256 resolution the map uses about 2 MiB including mipmaps. CPU and GPU debug samples must
agree before precipitation type is allowed to depend on the texture.

## Wind Units

`WeatherManager` owns `WindSpeedMetersPerSecond`. Shader consumers receive:

- `_WindSpeedMps`: physical speed.
- `_WindStrength01`: normalized visual response derived from a 25 m/s reference storm.

Physical translation uses `_WindSpeedMps`. Stylized responses such as grass bend, ocean
energy, and opacity use `_WindStrength01` or a documented response curve.

Runtime presets:

| Preset | Speed |
| --- | ---: |
| Calm | 1 m/s |
| Breeze | 4 m/s |
| Windy | 9 m/s |
| Gale | 17 m/s |
| Storm | 25 m/s |

## Local Weather Particles

`PrecipitationController` owns the shared local-weather budgets and profile parameters.
`PrecipitationRenderFeature` schedules both the existing distant precipitation composite and
the local weather-particle draw so no additional renderer-asset feature is required.

The local renderer uses deterministic procedural slots rather than particle GameObjects:

- slots are allocated implicitly by `SV_InstanceID`;
- particles are anchored to a camera-centered world-space grid;
- motion wraps and reuses slots instead of spawning and destroying objects;
- materials are allocated once by the render feature;
- no C# object pool is involved.

Profiles share placement, depth occlusion, edge fading, wind advection, quality scaling, and
weather sampling while retaining separate motion and appearance:

- Dust: floating motes at low wind, tapered ribbons as wind increases, denser turbulence in
  dry storms.
- Rain: fast gravity-driven ribbons with wind drift, enabled by local precipitation and warm
  temperature.
- Snow: slower tumbling flakes with stronger wind drift, enabled by local precipitation and
  cold temperature.

The distant rain curtain remains a separate volumetric precipitation effect.

Runtime validation commands avoid rebuilds:

- `weather.wind-speed <metersPerSecond>`
- `weather.wind-preset <Calm|Breeze|Windy|Gale|Storm>`
- `precipitation.particle-proof <Off|Dust|Rain|Snow|All>`
- `precipitation.particle-radius <meters>`
- `precipitation.dust-count <count>`
- `precipitation.rain-count <count>`
- `precipitation.snow-count <count>`
- `precipitation.rain-length <meters>`
- `precipitation.rain-speed <metersPerSecond>`
- `precipitation.rain-width <halfWidthMeters>`
- `precipitation.dust-size <halfWidthMeters>`
- `precipitation.dust-length <meters>`

## Validation

1. Compare CPU and GPU temperature at equator, pole, mountain, and coast positions.
2. Validate wind at 0, 4, 12, and 25 m/s with a forced proof mode.
3. Confirm particles remain stable while rotating the camera.
4. Confirm terrain and grass occlude local particles through scene depth.
5. Capture particle counts, active profile, climate-map memory, sampled temperature, wind,
   FPS, and frame time in the F10 sidecar.
