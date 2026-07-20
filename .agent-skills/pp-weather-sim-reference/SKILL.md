---
name: pp-weather-sim-reference
description: Use when working with the weather simulation or anything it drives - the weather grid, condensation, storm intensity, rain rate, humidity, moisture source, _CloudWeatherMap, _WeatherDynamicsMap, gloom, cube-face UV sampling, wind direction/speed, snow vs rain gating, WeatherManager, SphericalWeatherGrid, WeatherEvolution.compute, CPU weather queries, or adding a new weather-driven visual. Not for raymarch/lighting theory (see pp-gpu-rendering-reference) or the cloud/grass look campaign (see pp-visual-migration-campaign).
---

# Weather Simulation Reference

The weather sim is the single source of truth for every sky, precipitation, and
surface-weather visual in ProceduralPlanets. This skill documents its data contract,
evolution loop, sampling rules, consumers, and how to extend it without breaking the
coupling. All file paths are repo-relative; all facts verified against the working tree
on 2026-07-06 (branch `code-refactor`, dirty tree is normal).

## The coupling contract (law)

From `docs/design/2026-07-04-cloud-visual-migration-plan.md` (the active plan), carried
through every phase:

1. **The weather grid stays the single source of truth.** Every visual change consumes
   `_CloudWeatherMap` and `_WeatherDynamicsMap`. No phase introduces cloud or rain state
   that the sim doesn't drive.
2. **Snow is a surface/particle concern** — a temperature gate in
   `Assets/Graphics/Shaders/WeatherParticles.shader`, using the *climate map's*
   temperature, not the weather grid. The cloud layer's job for snow weather is identical
   to rain weather: overcast gloom + correct shadows. There is no "snow channel" and no
   snow concept in `Cloud.shader`.
3. **Sky and ground must agree.** Any darkening/gloom term must be computed by the shared
   helpers in `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl` in both the view
   march and the ground-shadow path (see "Gloom" below for the incident that made this law).
4. **Cube-face UV orientation must agree** across weather generation, shader sampling,
   cloud shadows, and CPU queries (see "Sampling rules" for the seam incident).

If you are about to write a shader branch like `if (raining) { ... }` from anything other
than these two textures (or the CPU mirror of them), stop — you are forking the source of
truth.

## Data contract: the two weather textures

Both are cube-sphere `Texture2DArray`s: 6 slices (one per cube face), `ARGBHalf`,
bilinear/clamp, `enableRandomWrite`, resolution = `ClosestPowerOfTwo(clamp(WeatherResolution, 32, 512))`
— default `WeatherResolution = 256` (`Assets/Scripts/Planet/Clouds/CloudSettings.cs:7`).
Each exists as a ping-pong pair (active + scratch) created in
`SphericalWeatherGrid.GenerateComputeAsync` (`Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs:149-217`).

Channel meanings, verified against the kernel writes in
`Assets/Graphics/Shaders/WeatherEvolution.compute:186-187` (`CSEvolveWeather`) and
`:310-312` (`CSInitWeather`):

| Texture (global name) | Channel | Meaning | Evidence |
|---|---|---|---|
| `_CloudWeatherMap` | r | **condensation** — cloud coverage/density driver, 0-1 | `_WeatherWrite[pixel] = float4(nextCondensation, nextStorm, localWeather.b, deltaDebug)` |
| `_CloudWeatherMap` | g | **storm** intensity 0-1 | same write |
| `_CloudWeatherMap` | b | **moisture source** — the *stationary* seeded front-potential map; evolution relaxes condensation toward it. Never advected | comment at `WeatherEvolution.compute:152-154` |
| `_CloudWeatherMap` | a | **condensation-delta debug** — `saturate(0.5 + delta * 16)`; 0.5 = no change. Debug-only (`cloud.debug-mode 7`) | `WeatherEvolution.compute:184-186`; scale is `SphericalWeatherGrid.DeltaVisualizationScale = 16` |
| `_WeatherDynamicsMap` | r | **humidity** — advected air moisture, consumed by condensation growth and rain-out, recovers toward supply | `_DynamicsWrite[pixel] = float4(nextHumidity, nextPrecipitationWater, rainRate, moistureSupply)` |
| `_WeatherDynamicsMap` | g | **precipitation water** — accumulated rainable water in the cloud | same write |
| `_WeatherDynamicsMap` | b | **rain rate** — the actual "it is raining here right now" signal; what rain visuals consume | same write |
| `_WeatherDynamicsMap` | a | **moisture supply** — geographic (planet-fixed) humidity ceiling, seeded from latitude+climate noise; read at the local cell, never advected | `WeatherEvolution.compute:148-149` |

The migration plan's shorthand "r=condensation, g=storm, b=moisture-source / r=humidity,
g=precip water, b=rain rate" is accurate; the alpha channels above are the parts the plan
omits.

Binding: `CloudController` uploads both textures as shader globals
(`Assets/Scripts/Planet/Clouds/CloudController.cs:32-33`, names from
`Assets/Scripts/Core/Services/ShaderGlobalIds.Cloud.cs:6-7`). `WeatherManager` sets
`_CloudWeatherRotation` to identity in `Awake` (`WeatherManager.cs:165`) — as of
2026-07-06 it is *always* identity; `SampleWeather`/`SampleDynamics` apply it anyway, and
the CPU path passes `Quaternion.identity` to match.

## Seeding and evolution

### Who owns what

- `Assets/Scripts/Planet/WeatherManager.cs` — MonoBehaviour orchestrator, `[CommandPrefix("weather")]`,
  registers `IWeatherProvider` + `IWeatherConfigurator` as world services. Owns the grid
  lifecycle, wind state, and the dirty-flag wind global uploads.
- `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs` — the grid itself: GPU textures,
  CPU mirror arrays (six `float[res*res*6]`), compute dispatch, cell indexing.
- `Assets/Scripts/Planet/WeatherEvolutionScheduler.cs` + `Assets/Scripts/Planet/WeatherQueryCache.cs`
  — plain-class services extracted from WeatherManager in commit `7048c2c`.
- `Assets/Scripts/Planet/WeatherDiagnostics.cs` — overlay + JSON/CSV export.

### Seeding (commit `5c56e03`: Burst CPU job → `CSInitWeather` compute)

`WeatherManager.LateInitialize` (dependency: `IPlanet`) awaits
`SphericalWeatherGrid.GenerateComputeAsync`, which dispatches `CSInitWeather` directly
into the ping-pong textures — there is no CPU staging upload. Seed comes from
`ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("Weather")`. Three concatenated
512-entry simplex permutation tables (`seed`, `seed+7919`, `seed+104729`) drive front
noise, detail noise, and climate (latitude-wetness) noise; coverage threshold is
`lerp(0.84, 0.18, InitialCoverage)` (default `InitialCoverage = 0.48`). Regeneration
happens on `PlanetGeneratedEvent` (after init) and on `SettingsChangedEvent` when
`CloudDto.WeatherResolution` or `InitialCoverage` changed.

### Evolution loop (GPU)

`WeatherManager.Update` → `WeatherEvolutionScheduler.Tick` every frame:

- Gated by `CloudDto.EnableWeatherEvolution` (default true) and a non-null
  `WeatherCompute` (`Assets/Graphics/Shaders/WeatherEvolution.compute`, inspector-assigned).
- Fixed-step accumulator: step = `max(CloudDto.EvolutionInterval, 0.05)` seconds (default
  **0.1 s**, i.e. ~10 dispatches/sec), max **3** catch-up steps per frame, excess time clamped.
- Each step dispatches `CSEvolveWeather` with `ceil(res/8) x ceil(res/8) x 6` groups, then
  swaps active/scratch for both texture pairs (`SphericalWeatherGrid.Advance`).
- Advection is semi-Lagrangian: each cell reads its upwind parcel by rotating the cell
  direction about `cross(direction, _WindDirection)` by `-_StepAngle`, where
  `stepAngle = windSpeed / seaLevelRadius * FrontAdvectionSpeedMultiplier(=1) * interval`.
  The scheduler accumulates these into the `_CloudWindAngle` global so the cloud *shape
  noise*, cloud shadows, and grass all advect by the same angle.
- Border texels sample the exact cube edge (`EdgeSnappedUv`) so adjacent faces evolve from
  the same spherical direction — this is seam-prevention, do not "simplify" it away.

The sim's internal rates (storm growth/decay, rain formation, humidity recovery, etc.) are
compile-time constants in `Assets/Scripts/Planet/Clouds/CloudConstants.cs` — notably
`RainFormationThreshold = 0.88` (on storm) and `RainCloudThreshold = 0.90` (on
condensation): rain only forms in very stormy, very dense cells, multiplied by humidity.

### CPU mirror: the query cache

GPU textures are the truth; CPU code reads a *lagged mirror*. `WeatherQueryCache.Tick`
round-robins one face per `WeatherQueryCacheInterval` (default **0.5 s**, so a full
refresh takes ≥3 s) via `AsyncGPUReadback` into the grid's CPU arrays; the dynamics
readback for the same face is chained after the weather readback. Consequences:

- `WeatherManager.SampleWeather(worldPos)` returns `InitialCoverage` as fallback only
  while `_grid == null`. After generation but *before* a face's first readback, CPU
  queries on that face return **0** (arrays start zeroed — `SphericalWeatherGrid.cs:202-216`).
- CPU values lag GPU by up to `6 * interval` + readback latency. Never use a CPU sample to
  assert what a shader shows *this frame*.
- `weather.export-grid` forces a full 6-face readback of both textures first, then writes
  `summary.json` + `cells.csv` (all 6 channels per cell) to
  `Application.persistentDataPath/weather-grid-<timestamp>/`.

CPU consumers of `IWeatherProvider.SampleWeather`: lightning strike placement
(`WeatherLightningController`), capture metadata (`DebugCaptureMetadataBuilder`,
`WaterDebugModule`, `CloudDebugModule` overlays), and `weather.frame-storm` /
`FreeCameraController`. `WeatherSample.State` classifies Storm/Cloudy/Clear using
`WeatherManager.PrecipitationStormThreshold` / `CloudyThreshold` (inspector fields, 0.5 / 0.18).

## Sampling rules: cube-face UV alignment

Face layout everywhere: **0=+Y, 1=-Y, 2=-X, 3=+X, 4=+Z, 5=-Z**, with
`axisA = (up.y, up.z, up.x)`, `axisB = cross(up, axisA)`, `uv = (dot/major)*0.5+0.5`.
Four implementations must stay identical:

| Path | Function | File |
|---|---|---|
| Weather generation + evolution | `CubeFaceToUnitSphere` / `CubeFaceUv` | `Assets/Graphics/Shaders/WeatherEvolution.compute:38-83` |
| All weather-consuming shaders | `CubeFaceUv` | `Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl` (included by `WeatherSampling.hlsl`, which is included by `CloudShadows.hlsl`) |
| CPU cell lookup | `UnitSphereToWeatherCubeFace` | `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs:472-501` |
| Direction reconstruction (stats) | `CoordinateConverter.CubeFaceToUnitSphere` | used at `SphericalWeatherGrid.cs:345` |

**The seam incident (why this is law):** sharp cube-face-shaped seams appeared in the
clouds. F10 `Cloud Diagnostics` proved the wedge was already visible in the `CloudWeather`
debug view — i.e. in the weather field, not in lighting — and the root cause was that
shader-side `CubeFaceUv` was *not* the inverse of generation-side `CubeFaceToUnitSphere`
(several faces flipped/rotated). Border-texel snapping helped but couldn't fix it; the fix
was aligning UV orientation across generation, shader sampling, cloud shadows, and the CPU
query path. The warning comment at `SphericalWeatherGrid.cs:470-471` is load-bearing: do
**not** substitute `CoordinateConverter.UnitSphereToCubeFace` for the grid's own inverse —
its UV orientation differs. Full chronicle: **pp-failure-archaeology**. If a face-shaped
seam ever returns, check `CloudWeather` first (triage tables: **pp-debugging-playbook**).

## Consumers: how each visual reads the sim

All GPU consumers go through `SampleWeather` / `SampleDynamics` in `WeatherSampling.hlsl`
(which declares the textures and `_PrecipitationParams`). Verified sampling sites:

| Consumer | What it reads | Where |
|---|---|---|
| `Cloud.shader` (volumetric layer) | `weather.r` shapes density, `.g` storm, `.b` moisture debug, `.a` delta debug; `WeatherPrecipitationSignal` + gloom in the lit branch | `Cloud.shader:164-170, 361-391` |
| `Includes/CloudShadows.hlsl` (`CloudShadowFactor`) | `weather.r` for shadow density, `WeatherCloudGloom` for storm boost | `CloudShadows.hlsl:40-60` |
| Grass, Ocean, terrain (`PlanetVertexColor`), `WaterVolume` | cloud shadows + wind globals, via `#include "Includes/CloudShadows.hlsl"` | grep `CloudShadows.hlsl` in `Assets/Graphics/Shaders/*.shader` |
| `Precipitation.shader` (distant rain curtains + fog/haze) | `weather.g` storm gate, `dynamics.b` rain rate along ray and toward camera | `Precipitation.shader:140-141, 329, 375-378` |
| `RainParticles.shader` (local drops) | per-drop `dynamics.b` x storm gate for visibility/density | `RainParticles.shader:125-126`; design doc in `RainParticleController.cs` header |
| `WeatherParticles.shader` (dust + snow) | `rainSignal = saturate(dynamics.b) * stormGate`; snow additionally gated by climate-map temperature (`snowPhase`) | `WeatherParticles.shader:170-182, 216-219` |
| `WeatherLightning.hlsl` + `WeatherLightningController` | CPU `SampleWeather` picks storm cells (min precipitation 0.35, min storm 0.6); flash rendered in cloud/precip shaders via globals | `WeatherLightningController.cs:19-20` |

### The precipitation signal and gloom (current state, post-A2)

This section is the library's home for the gloom formula and its line citation. Exact
spans, verified 2026-07-06: `WeatherPrecipitationSignal` at `WeatherSampling.hlsl:40-45`,
`WeatherCloudGloomFromRain` at `:47-50`, `WeatherCloudGloom` at `:52-55` (the gloom
helpers proper span 47-55; the trio including the signal spans 40-55). Sibling skills
cite the helpers by name and point here.

As of 2026-07-06 the working tree has **unified** the gloom
formula that audit A2 (`docs/audit/2026-07-03-grass-cloud-line-audit.md:61`) found
diverged (sky used storm-gated rain + plain max; ground shadow used raw rain + a
steepening smoothstep, under a false "same formula" comment):

```hlsl
float WeatherPrecipitationSignal(float3 direction, float storm)
    // = saturate(dynamics.b) * smoothstep(stormThreshold, +softness, storm)
float WeatherCloudGloomFromRain(float storm, float precipitationSignal)
    // = max(saturate(storm), saturate(precipitationSignal))
float WeatherCloudGloom(float3 direction, float storm)   // convenience wrapper
```

Both paths now call the shared helper: `Cloud.shader:388`
(`WeatherCloudGloomFromRain(cloud.storm, rainRate)`) and `CloudShadows.hlsl:58`
(`WeatherCloudGloom(direction, weather.g)`). Note the resolution took the *storm-gated*
signal on both paths with a plain `max` — not the audit's suggested
`smoothstep(0.12, 0.6, rawRain)` variant. Gloom drives storm albedo, storm darkening,
silver-lining suppression (`Cloud.shader:390-407`) and the shadow storm boost. If you
change gloom, change it in the helper only, and expect a capture-diff (visual-tuning gate:
**pp-change-control**).

### Trap: three different "storm thresholds"

| Name | Default | Used for | Owner |
|---|---|---|---|
| `CloudDto.StormThreshold` | 0.86 | sim: condensation → storm smoothstep inside the kernels | `CloudSettings.cs:9` |
| `PrecipitationDto.StormThreshold` (`_PrecipitationParams.y`, softness `.z`) | 0.55 / 0.2 | shader gate: turns `dynamics.b` into the visible precipitation signal | `PrecipitationController.cs:30-31, 288-292` |
| `WeatherManager.PrecipitationStormThreshold` | 0.5 | CPU-only: `WeatherSample.State` classification + `CalculatePrecipitation` | `WeatherManager.cs:84` |

They are not interchangeable; renaming or merging them is a settings-design decision, not
a cleanup.

## Wind

`WeatherManager` owns wind (design doc: `docs/design/2026-06-09-climate-wind-weather-particles.md`).
Globals (names in `ShaderGlobalIds.Cloud.cs:42-46`, uploaded dirty-flagged in
`WeatherManager.Update`): `_WindDirection` (normalized, direction wind moves *toward*),
`_WindSpeedMps` (physical), `_WindStrength01` (`speed / 25 m/s` reference storm, for
stylized responses like grass bend), `_CloudWindAngle` (accumulated advection angle).
Presets (`weather.wind-preset`): Calm 1, Breeze 4, Windy 9, Gale 17, Storm 25 m/s.
Convention shared by the sim, cloud shape noise, shadows, grass, and particles: motion is
along the local `windTangent = wind - normal * dot(wind, normal)`; advected fields rotate
about `cross(direction, windDir)`. Legacy scenes stored wind as an abstract 0-5 value;
`MigrateWindUnits` converts once (x5) via `_windUnitsVersion`.

## Inspecting the sim live

Debug views (`cloud.debug-mode <0-9>`, enum `CloudDebugState.View`, registered in
`Assets/Scripts/Core/Services/CloudDebugModule.cs:42-51`): 0 Off, 1 CloudWeather
(condensation), 2 CloudStorm, 3 CloudDensity, 4 CloudOpticalDepth, 5 CloudSilverLining,
6 CloudMoistureSource, 7 CloudCondensationChange, 8 CloudPrecipitationSignal, 9
WeatherPrecipitationSignal. Modes 8 and 9 both visualize the storm-gated signal
(`Cloud.shader:362-366, 385-388, 451-458`); the difference is *where it's sampled*: 8
shows what the lit march actually consumed (masked by density), 9 samples every step and
masks by coverage — comparing them splits "sim wrong" from "renderer wrong".
`debug.capture-set "Cloud Diagnostics"` + F10 captures modes 0,1,3,4,2,6,7,8,9 with
metadata sidecars (workflow: **pp-run-and-operate** / **pp-diagnostics-and-tooling**).

Console commands (all verified by reading the attributed methods):

- `weather.*` (`WeatherManager.cs`): `diagnostics` (JSON to persistentDataPath),
  `export-grid` (full readback → summary.json + cells.csv), `frame-storm` (fly camera to
  strongest storm), `wind-speed`, `wind-preset`, `wind-direction`.
- `cloud.*` (`CloudController.cs`): `density`, `altitude`, `thickness`, `debug-mode`,
  `debug-threshold`, `debug-saturation`.
- `precipitation.*` (`PrecipitationController.cs`): `intensity`, `debug-mode`
  (RainMask/RainDots/StormDots), `fog`, `haze`, `particles-enabled`, `particle-proof`
  (force Dust/Rain/Snow/All), `particle-radius`, `dust-count`, `snow-count`, `dust-size`,
  `dust-length`.
- `rain-particles.*` (`RainParticleController.cs`): `count`, `near-radius`, `fall-speed`,
  `streak-length`, `streak-width`, `density-scale`.
- `lightning.*` (`WeatherLightningController.cs`): `enable`, `delay`, `intensity`.
- `climate.*` (`Assets/Scripts/Planet/Biomes/ClimateCommands.cs`, static class): `status`,
  `preset` (Legacy/Earthlike/StrongBands), `temperature-range`, `temperature-point`,
  `moisture-point`, `moisture-bands`, `moisture-noise`, `temperature-noise`,
  `altitude-lapse`, `lut-resolution`, `map-resolution`, `voronoi-*`, `temperature-unit`,
  and `apply` (regenerates the planet — required after most climate setters).

Temperature/climate: the weather grid does **not** own temperature. `ClimateProvider` is
the static climate model; `IClimateSampler.TrySampleClimate` gives `TemperatureCelsius`
(terrain-elevation-based — deliberately not camera altitude), and a 6-face `RGHalf`
climate map (r=temperature01, g=moisture01, same cube-face convention) serves shaders.
`WeatherManager.GetTemperature` just delegates to the climate sampler.

## Extension checklist: adding a new weather-driven visual

1. **Pick the signal from the existing channels.** Rain-ness = `WeatherPrecipitationSignal`
   (never raw `dynamics.b` unless you have a stated reason — the storm gate is the agreed
   look). Overcast-ness = `WeatherCloudGloom`. Coverage = `weather.r`. Snow = rain signal
   x a climate-temperature gate (copy the `snowPhase` pattern, `WeatherParticles.shader:178-182`).
   Do not add a channel unless the sim itself must evolve new state — that's a
   design-doc + Bryan decision, not a feature branch.
2. **Shader side:** `#include "Includes/WeatherSampling.hlsl"` (or `CloudShadows.hlsl` if
   you also need shadows/wind declarations — including both is fine, they're guarded).
   Never redeclare the textures or roll your own cube-face math.
3. **New shader globals:** add the name to the right `ShaderGlobalIds.*.cs` partial first,
   cache the `PropertyToID` locally, upload via the dirty-flag pattern (static/dynamic
   split, mark dirty on `PlanetGeneratedEvent` + every console setter). Material and
   compute-shader-scoped property names stay module-local (CLAUDE.md rule).
4. **CPU side:** resolve `IWeatherProvider` from the world context once at init; remember
   the query-cache lag and the zeroed-until-readback window. Don't `TryGet` per frame.
5. **Settings:** new tunables go SO → DTO (`From(SO)` factory, `EnsureRegistered`,
   console setters via `SettingsProvider.Update(dto with {...})`) — catalog and how-to in
   **pp-settings-and-flags**.
6. **Debug surface:** register a debug mode in your domain's `*DebugModule`; if it's
   cloud-adjacent, extend `CloudDebugState.View` *and* `CloudDebugModule` *and* the shader
   branch together (audit A3 was exactly these drifting apart).
7. **Evidence:** F10 before/after captures; a weather-driven visual must be demonstrated
   against a known cell (`weather.frame-storm`, `cloud.debug-mode 8`) — build success is
   not proof (**pp-validation-and-evidence**). Run `graphify update .` after code changes
   (set a timeout — known hang in this checkout, see pp-build-and-env Known traps).

## When NOT to use this

- **Raymarch mechanics, lighting theory, noise generation, cube-sphere math in general** —
  pp-gpu-rendering-reference. This skill covers what the marchers *consume*, not how they march.
- **The live cloud/grass visual migration plan and its gates** — pp-visual-migration-campaign.
- **Seam/artifact triage procedure** — pp-debugging-playbook; **full seam/gloom history** —
  pp-failure-archaeology.
- **SO→DTO mechanics, quality tiers, settings catalog** — pp-settings-and-flags.
- **Console/capture operation basics** — pp-run-and-operate.
- **Change gating (visual-tuning lock, audit workflow)** — pp-change-control.

## Provenance and maintenance

Written 2026-07-06 from the working tree (branch `code-refactor`, graph baseline
`ec0b1cd`). Re-verify before trusting:

- Channel writes: `grep -n "_WeatherWrite\[pixel\]\|_DynamicsWrite\[pixel\]" Assets/Graphics/Shaders/WeatherEvolution.compute`
- Gloom still unified via shared helper: `grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders`
- Precipitation signal formula: `grep -n "WeatherPrecipitationSignal" Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`
- Weather rotation still identity-only: `grep -rn "CloudWeatherRotation" Assets/Scripts`
- Evolution cadence + catch-up: `grep -n "EvolutionInterval\|maxSteps" Assets/Scripts/Planet/WeatherEvolutionScheduler.cs Assets/Scripts/Planet/Clouds/CloudDto.cs`
- Query-cache interval/round-robin: `grep -n "WeatherQueryCacheInterval\|_nextFace" Assets/Scripts/Planet/WeatherManager.cs Assets/Scripts/Planet/WeatherQueryCache.cs`
- CPU inverse warning intact: `grep -n "UnitSphereToWeatherCubeFace" Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`
- Debug modes 0-9: `grep -n "RegisterMode" Assets/Scripts/Core/Services/CloudDebugModule.cs`
- Console commands: `grep -rn "CommandPrefix(\"weather\"\|CommandPrefix(\"climate\"\|CommandPrefix(\"precipitation\"\|CommandPrefix(\"rain-particles\"\|CommandPrefix(\"lightning\"" Assets/Scripts`
- Defaults quoted here (resolution 256, coverage 0.48, interval 0.1, storm 0.86): `grep -n "=" Assets/Scripts/Planet/Clouds/CloudSettings.cs | head -20`
- Sim rate constants: `Assets/Scripts/Planet/Clouds/CloudConstants.cs` (weather section, lines 5-28)
- Coupling law text: `docs/design/2026-07-04-cloud-visual-migration-plan.md` (intro, "Hard requirement")

Additional background (not load-bearing): `.agent-memory/codex/MEMORY.md` task group
"cloud seam diagnosis and cube-face sampling fix".
