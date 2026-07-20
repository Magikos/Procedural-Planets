---
name: pp-settings-and-flags
description: Use when you need to find, read, change, or add any configuration value — planet/biome/cloud/atmosphere/precipitation settings, quality tiers, grass layer flags, wind/lightning toggles, console-tunable values, or saved-world overrides. Also when a setting "doesn't take effect", a DTO throws "no DTO registered", registration is frozen, or you must know whether a flag is production or experimental and what its current default is. Not for the WHY behind the SO/DTO architecture — see pp-architecture-contract. Not for measuring a toggle's visual/perf effect — see pp-diagnostics-and-tooling.
---

# pp-settings-and-flags

Catalog of every configuration axis in ProceduralPlanets: which knob lives where, who owns it, what mutates it at runtime, and how to add a new one without violating the settings contract.

**Jargon, defined once:**
- **SO** = `ScriptableObject` settings asset. Editor-only authoring surface; runtime never reads it outside boot.
- **DTO** = immutable snapshot record (C# `record`) built from an SO (or, in one flagged exception, from a MonoBehaviour's serialized fields) via a static `From(...)` factory. Runtime consumers read DTOs only.
- **Registrar** = a MonoBehaviour implementing `IWorldSettingsRegistrar` (declared in `Assets/Scripts/Core/Services/ServiceLocator.cs:29`). `SceneBootstrap` calls `RegisterWorldSettings(ISettingsService)` on every scene registrar, validates every `RequiredSettingsTypes` entry, then **freezes** registration.
- **Freeze** = after `SettingsService.Freeze()` no new DTO types can be registered (`Register` throws) and construction-time `Override` throws; only `Update<TDto>` (replace an existing DTO) is allowed. See `Assets/Scripts/Core/Services/SettingsService.cs`.
- **MB-field axis** = a config axis whose state lives in serialized MonoBehaviour fields with no DTO (wind, lightning, rain particles). These predate the DTO pattern and are flagged drift — open audit finding G11 (general audit, 2026-07-03). Do **not** migrate them as drive-by work; migration is Bryan-gated findings-first (CLAUDE.md's migrate-as-touched clause is scoped to the logger, not settings architecture). New axes use SO→DTO from the start.

## The pipeline in six steps (verified 2026-07-06)

1. `SceneBootstrap.Awake` runs `RegisterSceneServices()`, `RegisterSceneSettings()`, then `ServiceLocator.GetWorld().ApplySettingsOverrides()` (`Assets/Scripts/Core/Services/SceneBootstrap.cs:67-72`).
2. `SceneBootstrap.EarlyInitialize` re-runs both registrations, applies overrides again (idempotent — `_settingsOverridesApplied` guard in `ServiceLocator.cs:146-164`), calls `settings.ValidateRequired(_requiredSettings)`, then `settings.Freeze()` (`SceneBootstrap.cs:89-95`).
3. Each registrar's `RegisterWorldSettings` uses an ensure-once pattern: `if (settings.IsRegistered<TDto>()) return; settings.Register(TDto.From(so));` — safe when several registrars require the same DTO (CloudDto has three).
4. Consumers cache the DTO at init: `_settings = SettingsProvider.GetSettings<TDto>()`. `GetSettings` throws `InvalidOperationException("no DTO registered for ...")` if nothing registered it — that error means a registrar is missing or the scene lacks the owning MonoBehaviour.
5. Runtime changes go through `SettingsProvider.Update(_settings with { Field = value })`. `SettingsService.Update` swaps the stored record and raises `EventBus<SettingsChangedEvent>` with the DTO's `Type` (`SettingsService.cs:19-29`).
6. Consumers listen for `SettingsChangedEvent`, filter on `evt.DtoType`, re-fetch, and mark their shader-global dirty flag (`_staticPropertiesDirty = true`). Precedent: `AtmosphereController.OnSettingsChanged` (`Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:114-119`), `CloudController.OnSettingsChanged` (`Assets/Scripts/Planet/Clouds/CloudController.cs:113-123`).

**Saved-world overrides:** persistence code builds `WorldSettingsOverride<TDto>` values (`Assets/Scripts/Core/Interfaces/ISettingsService.cs:23-37`) and passes them into `WorldLoadRequest.ForScene(...)` / `ForBuildIndex(...)` (`Assets/Scripts/Core/Interfaces/ILoadingManager.cs`). `WorldContext.ApplySettingsOverrides` applies each via `settings.Override(value)` — pre-freeze only, no event raised. This is the only sanctioned way to load a world with non-asset settings.

**Two iron rules** (CLAUDE.md, enforced by pattern):
- **No console command mutates the SO asset.** Every setter does `SettingsProvider.Update(_settings with { ... })` or writes a plain runtime field. If you see a command writing to a `ScriptableObject`, it is a bug.
- **Materials are cloned on first use.** Precedent: `PlanetTerrainMaterial.EnsureRuntime` clones the authored `PlanetMaterial` (`Assets/Scripts/Planet/PlanetTerrainMaterial.cs:41-50`); grass/water/rain controllers build `new Material(shader)` runtime materials. Runtime never writes properties or keywords on an SO-referenced material asset.

## (a) Master catalog — every config axis (verified 2026-07-06)

| Axis | SO asset (repo path) | DTO | Registrar (registers) | Main consumers | Runtime-mutable? (console) | Status |
|---|---|---|---|---|---|---|
| Planet shape/water | `Assets/Game Data/Planet Settings/Planet.asset` (`PlanetSettings`), assigned on `Planet` MB or via `PlanetRecipe` | `PlanetDto` (`Assets/Scripts/Planet/PlanetDto.cs`) | `Planet` (`Planet.cs:111-126`, registers PlanetDto + BiomeDto) | `Planet`, `PlanetWaterSurface`, `PlanetGrassCoordinator`, `PlanetTerrainMaterial`, `AtmosphereController` | Yes: `planet.generate [seed] [radius]` updates `PlanetRadius` via `Update` (`Planet.cs:590`); `planet.seed` / `planet.resolution` stage values, apply on `planet.generate` | Production |
| Biome/climate | `Assets/Game Data/Planet Settings/Biomes/BiomeSettings.asset` (`BiomeSettings`) + `BiomeRegistry.asset` composed as `BiomeRegistryDto` | `BiomeDto` (`Assets/Scripts/Planet/Biomes/BiomeDto.cs`) | `Planet` (same as above) | climate map pipeline, `PlanetTerrainMaterial`, grass configs (`GrassBiomeTintConfig`/`GrassBiomePlacementConfig` derive `From(BiomeDefinitionDto)`) | Yes: `climate.*` (~15 setters in `Assets/Scripts/Planet/Biomes/ClimateCommands.cs`, all `SettingsProvider.Update`; most need `climate.apply` to regenerate) | Production |
| Atmosphere | `Assets/Resources/Settings/AtmosphereSettings.asset` (`Resources.Load` in `AtmosphereController.EnsureSettingsRegistered`, `AtmosphereController.cs:64-72`) | `AtmosphereDto` | `AtmosphereController` | `AtmosphereController` (dirty-flag shader globals) | Yes: `atmosphere.sun-intensity`, `.rayleigh`, `.mie`, `.scale` | Production |
| Clouds + weather grid | `Assets/Resources/Settings/CloudSettings.asset` (`CloudDto.EnsureRegistered`, `CloudDto.cs:28-36`) | `CloudDto` | `CloudController`, `WeatherManager`, `PrecipitationController` (all ensure-once) | `CloudController`, `WeatherManager` (grid regen on `WeatherResolution`/`InitialCoverage` change, `WeatherManager.cs:202-215`), `PrecipitationController`, `RainParticleController` | Yes: `cloud.density` (0-0.08), `cloud.altitude` (20-1000 m), `cloud.thickness` (50-1000 m) | Production. NOTE: CLAUDE.md's example split (`CloudRenderSettings`/`CloudEvolutionSettings`/`WeatherGridSeedSettings`/`RainFormationSettings`) has NOT landed — one god-ish `CloudSettings`/`CloudDto` still ships. Rule-vs-code drift; the split is the target, not the state. |
| Precipitation | **No SO.** DTO snapshots the controller's serialized fields: `PrecipitationDto.From(PrecipitationController)` (`Assets/Scripts/Planet/PrecipitationDto.cs:51`). Inspector edits re-snapshot via `OnValidate` in play mode (`PrecipitationController.cs:220-221`). | `PrecipitationDto` | `PrecipitationController` (`PrecipitationController.cs:199-210`) | `PrecipitationController`, `RainParticleController` | Yes: `precipitation.intensity`, `.fog`, `.haze`, `.debug-mode`, `.particles-enabled`, `.particle-proof`, `.particle-radius`, `.dust-count`, `.snow-count`, `.dust-size`, `.dust-length`; plus `debug.precipitation` (P-key toggle → `RenderPrecipitation`) | Production, but the SO-less shape is a flagged exception to "DTOs live next to the SO they snapshot" |
| Grass quality profile | **No SO.** `DefaultGrassQualitySettings` plain class (`Assets/Scripts/Core/QualityController.cs:22-48`), registered app-scope as `IGrassQualitySettings` by `GameBootstrap.EarlyInitialize` (`GameBootstrap.cs:57-62`) | interface, not DTO | n/a (ServiceLocator app scope, not settings service) | `PlanetGrassCoordinator` (lazy `ServiceLocator.Get`, `PlanetGrassCoordinator.cs:53-54`), grass controllers, `QualityController` | No console setter — values are compile-time constants | Production (single profile; per-tier grass profiles do not exist yet) |
| Unity quality tier | Unity `QualitySettings` (ProjectSettings) | none — static properties on `QualityController` | n/a | `CloudController`, `PrecipitationController` read `QualityController.CloudStepMultiplier`; all cloud/precip shaders read `CLOUD_QUALITY_LOW` | Yes: `quality.get`, `quality.list`, `quality.set <index>`, `quality.cloud-steps [0.33-1]` (reset by `quality.set`) | Production |
| Grass runtime layers | none — plain bool fields on `PlanetGrassCoordinator` | none (exposed as `GrassRuntimeState` struct) | n/a | `PlanetGrassCoordinator` creates/disposes layer controllers per flag | Yes: `grass.enabled`, `grass.layer <Near|Chunk|Blanket> <bool>`, `grass.status` (in `Assets/Scripts/Core/Services/GrassDebugModule.cs:242+`) | See feature-flag table — Chunk and Blanket are OFF |
| Grass overlay tuning | none — fields on `PlanetGrassCoordinator` (`_farOverlayStrength = 1.0`, `_grassSurfaceBrightness = 0.35`, `PlanetGrassCoordinator.cs:42-43`) | none | n/a | terrain material far-overlay properties | Yes: `grass.overlay-strength`, `grass.surface-brightness`, `grass.overlay-status` | Live-tuning surface; winning values get baked into field defaults (visual change ⇒ pp-change-control gate) |
| Grass geometry mode | none — statics on `GrassRenderDiagnostics` (`GrassDebugModule.cs:18-35`) | none | n/a | grass shaders via `_GrassGeometryMode` etc. globals | Yes: `grass.render-mode <Physical|Hybrid|Cluster>`, `grass.render-cluster-range`, `grass.render-reset` | Diagnostic/experimental. Default `Physical`; cluster range default 12-24 m. DRIFT: `grass.render-reset` help text says "default Hybrid" but `Reset()` sets `Physical` (`GrassDebugModule.cs:30,39`). |
| Wind | none — serialized fields on `WeatherManager`: `WindDir = (1, 0, 0.3)`, `WindSpeedMetersPerSecond = 2.5` (`WeatherManager.cs:69-77`) | none (MB-field axis) | n/a | weather advection, cloud/precip shaders | Yes: `weather.wind-speed`, `weather.wind-preset`, `weather.wind-direction` (mutate MB fields directly) | Production but un-migrated MB-field axis |
| Lightning | none — serialized fields on `WeatherLightningController`: `EnableLightning = true`, `MinDelay = 4`, `MaxDelay = 12` (`WeatherLightningController.cs:11-13`) | none (MB-field axis) | n/a | lightning flash rendering | Yes: `lightning.enable`, `lightning.delay`, `lightning.intensity` | Production, MB-field axis |
| Rain particles (near-camera) | none — fields on `RainParticleController` | none (reads `CloudDto`/`PrecipitationDto` for cloud base + rates) | n/a | rain streak indirect draw | Yes: `rain-particles.count`, `.near-radius`, `.fall-speed`, `.streak-length`, `.streak-width`, `.density-scale` | Production, MB-field axis |
| Diagnostic layouts | `DiagnosticTerrainLayout` / `DiagnosticGridBiomeLayout` SOs under `Assets/Game Data/Planet Settings/Tests/`, composed by `PlanetRecipe.ToPlanetDto()/ToBiomeDto()` (`Assets/Scripts/Planet/PlanetRecipe.cs`) | folded into `PlanetDto.DiagnosticTerrainLayout` / `BiomeDto.DiagnosticGridLayout` | `Planet` (recipe path) | terrain/biome generators | No | Diagnostic fixtures (grass/terrain test recipes), not shipping content |
| Console/UI assets | `ConsoleTheme`, `SDFFontAsset` SOs | none | n/a | console renderer | Partially: `console.anchor`, `console.scrollback-size` (runtime state, not the SO) | Production tooling, outside the world-settings system |

World seed is its own mini-axis: `SceneBootstrap.WorldSeed = 12345` (serialized, `SceneBootstrap.cs:55`), overridable per load via `WorldLoadRequest.WorldSeed`, exposed as `ISeedProvider`; `planet.seed` stages a new one for the next `planet.generate`.

## (b) Quality tiers — what actually changes (verified 2026-07-06)

Tier classification is **name-token based**, not index based (`QualityController.ApplyQualityLevel`, `Assets/Scripts/Core/QualityController.cs:110-141`): a Unity quality level whose name contains `mobile|low|fastest` ⇒ Low; `medium|balanced` ⇒ Medium; anything else ⇒ High. Index fallback exists but is disabled (`FallbackLowQualityMaxLevel = -1`). In Standalone this project exposes PC as runtime index 0.

| Tier | `CLOUD_QUALITY_LOW` keyword | `CloudStepMultiplier` | Effect |
|---|---|---|---|
| Low | enabled | 0.33 | Keyword caps raymarch steps and disables detail noise in `Cloud.shader` (`#pragma multi_compile _ CLOUD_QUALITY_LOW` at `Assets/Graphics/Shaders/Cloud.shader:290`) and `Precipitation.shader` (line 221). Multiplier scales view steps in `CloudController`/`PrecipitationController`. |
| Medium | disabled | 0.65 | Step multiplier only. |
| High (default) | disabled | 1.0 | Full quality. `QualityController.CloudStepMultiplier` defaults to 1.0 before any controller exists. |

`quality.cloud-steps <0.33-1>` overrides the multiplier live; `quality.set` resets it.

**Grass is NOT tiered.** One profile, `DefaultGrassQualitySettings` (`QualityController.cs:22-48`), all values compile-time:

| Value | Number | Meaning |
|---|---|---|
| `NearFieldFullDensityDistance` | **144 m** | full blade density out to here; blade shader perceptual ramps derive from this |
| `NearFieldDrawDistance` | **200 m** | near-field blades stop drawing |
| `MaxRenderDistance` / `LowLodDistance` | 240 m / 200 m | chunk-grass layer distances |
| `MaxBladesPerLane` / `DensityMultiplier` | 24 / 1.0 | authored biome density, no diagnostic boost |
| `NearFieldFadeAltitudeStart` / `ActivationAltitude` / `DeactivationAltitude` | 350 / 500 / 550 m | camera-altitude gates; blades alpha-fade over 350→500 m so create/dispose never pops |
| `FarOverlayAltitudeStart` / `End` | 750 / 2600 m | terrain grass-paint overlay altitude window |
| `MaxCoarseLodOffsetForBlades` | 1 | blades persist across first terrain LOD transition |

## (c) Feature flags — current values as of 2026-07-06

This table is the library's canonical home for current flag values — sibling skills that mention these flags point here. When a flag flips (e.g. the far-field decision re-enables chunk/blanket), update this table first.

| Flag | Location | Value | Why / notes |
|---|---|---|---|
| `_grassEnabled` | `Assets/Scripts/Planet/PlanetGrassCoordinator.cs:16` | `true` | Master grass switch (`grass.enabled`). |
| `_nearFieldGrassEnabled` | `PlanetGrassCoordinator.cs:17` | `true` | Near-field blade layer, the only grass layer currently rendering (`grass.layer Near`). |
| `_chunkGrassEnabled` | `PlanetGrassCoordinator.cs:18` | **`false`** | Chunk-following blade layer parked pending the grass visual migration (`grass.layer Chunk true` re-enables live). |
| `_grassBlanketEnabled` | `PlanetGrassCoordinator.cs:21` | **`false`** | Far terrain-paint blanket. In-code comment: off "until it shares the same blend ownership as the biome material path" — the biome-stripe fight ended with these layers disabled and the shader reverted. Full story: pp-failure-archaeology. |
| `EnableSurfaceOverrides` | `PlanetSettings.cs:33` (SO field → `PlanetDto`) | `true` | Gates coast/biome surface override slices in `PlanetTerrainMaterial.ConfigureSurfaceOverrides`. |
| `HasOceans` / `EnableFrozenWater` | `PlanetSettings.cs:36,39` | `true` / `true` | Water + ice tinting. |
| `EnableWeatherEvolution` | `CloudSettings.cs:12` (SO field → `CloudDto`) | `true` | GPU weather-grid evolution (interval 0.1 s). |
| `EnableLightShafts` | `AtmosphereSettings.cs:39` (SO field → `AtmosphereDto`) | `true` | Light-shaft pass. |
| `RenderPrecipitation` | `PrecipitationController.cs:28` (MB field → `PrecipitationDto`) | `true` | Distant rain curtains; toggled by `debug.precipitation` (P key). |
| `RenderLocalParticles` | `PrecipitationController.cs:57` | `true` | Near-camera dust/snow (`precipitation.particles-enabled`). |
| `EnableLightning` | `WeatherLightningController.cs:11` | `true` | `lightning.enable`. |
| `GrassRenderDiagnostics.GeometryMode` | `GrassDebugModule.cs:30` | `Physical` | A/B geometry experiment (`grass.render-mode`). Help text for `grass.layer` mentions a deprecated "Mid" layer that no longer exists in `GrassRenderLayer` (Near/Chunk/Blanket only) — stale help text, not a real flag. |

SO **field** defaults above (e.g. `InitialCoverage = 0.48`, `DensityMultiplier = 0.018`, `BaseAltitude = 330`) are the C# class defaults; the committed `.asset` files may hold Bryan's hand-tuned values that differ. The asset wins at runtime. Never "fix" an asset value back to the class default — hand-picked values are locked (pp-change-control).

## (d) Checklist: add a new setting

Example: add `float FooStrength` to clouds.

1. **SO field**: add to `Assets/Scripts/Planet/Clouds/CloudSettings.cs` with `[Range]`/`[Tooltip]`. (For precipitation, the "SO" is the controller's serialized field section.)
2. **DTO field**: add a positional parameter to the `CloudDto` record — keep parameter order matched with the SO reading order.
3. **`From()`**: add `src.FooStrength` at the same position in `CloudDto.From`.
4. **Registrar**: nothing to do if the DTO already exists. For a brand-new DTO: give the owning MonoBehaviour `IWorldSettingsRegistrar`, a `static readonly Type[] RequiredSettings = { typeof(NewDto) }`, `RequiredSettingsTypes => RequiredSettings`, and an ensure-once `RegisterWorldSettings`. Missing registration fails fast at boot: `SceneBootstrap.EarlyInitialize` → `ValidateRequired` throws "required DTOs are not registered".
5. **Consumer re-fetch**: consumer caches DTO at init and re-fetches in `OnSettingsChanged` filtered by `evt.DtoType == typeof(CloudDto)`.
6. **Dirty flag**: if the value feeds a shader global, set `_staticPropertiesDirty = true` in `OnSettingsChanged` AND add the global's string name to the right `ShaderGlobalIds.*.cs` partial first (globals only — per-material and compute properties stay module-local). Cache `Shader.PropertyToID` locally.
7. **Console setter** on the service that owns the state (same class, under its existing `[CommandPrefix]`): clamp, then `SettingsProvider.Update(_settings with { FooStrength = clamped });` — never write the SO, never `Shader.SetGlobal` directly from the command (let the dirty-flag publish path do it).
8. **Saved worlds**: if the value must persist per-world, persistence translates its save key into `WorldSettingsOverride<CloudDto>` before load. New DTOs may require bumping `WorldLoadRequest.CurrentSettingsSchemaVersion` (`Assets/Scripts/Core/Interfaces/ILoadingManager.cs`) — check how existing keys map before inventing a new mechanism.
9. **Build check** (code health only, not visual proof): `dotnet build ProceduralPlanets.Planet.csproj` then Core, serially. Then `graphify update .` (set a timeout — known hang in this checkout, see pp-build-and-env Known traps).
10. If the new value changes the look, stop: capture-diff + Bryan's review before tuning (pp-change-control).

## (e) Drift warning + re-verification

Flag defaults and console command sets change frequently on this branch. **Re-verify any row you are about to rely on** — commands below are git-bash from repo root:

```bash
# grass layer flags (the most volatile rows)
grep -n "_chunkGrassEnabled\|_grassBlanketEnabled\|_nearFieldGrassEnabled\|_grassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
# grass quality numbers (144/200/240/350/500/550)
grep -n "NearField\|MaxRenderDistance\|FarOverlay" Assets/Scripts/Core/QualityController.cs
# quality tier multipliers + keyword
grep -n "StepMultiplier\|CLOUD_QUALITY" Assets/Scripts/Core/QualityController.cs Assets/Graphics/Shaders/Cloud.shader Assets/Graphics/Shaders/Precipitation.shader
# full console command inventory (names + help text)
grep -rn "\[ConsoleCommand(" Assets/Scripts --include=*.cs
# who registers which DTO
grep -rn "RegisterWorldSettings\|RequiredSettings =" Assets/Scripts --include=*.cs
# all Update<TDto> setters (proves no command touches an SO)
grep -rn "SettingsProvider.Update" Assets/Scripts --include=*.cs
# has the CloudSettings split landed yet?
grep -rln "CloudRenderSettings\|CloudEvolutionSettings\|WeatherGridSeedSettings\|RainFormationSettings" Assets/Scripts
# geometry-mode default vs render-reset help drift
grep -n "GeometryMode { get" Assets/Scripts/Core/Services/GrassDebugModule.cs
```

If a grep result contradicts this file, the repo wins — update this skill.

## When NOT to use this

- **Why the SO/DTO split, ServiceLocator scopes, boot phases, or dirty-flag pattern exist** → pp-architecture-contract. This file tells you *what and where*; that one tells you *why and what breaks otherwise*.
- **Measuring what a toggle does** (F10 capture sets, debug modes, counters, frame timing) → pp-diagnostics-and-tooling.
- **The history of why chunk grass / blanket got disabled** (biome-stripe fight, reverts) → pp-failure-archaeology.
- **Whether you are allowed to change a value** (visual-tuning gate, Bryan's hand-picked constants, caustics) → pp-change-control.
- **Launching the game / opening the console to run these commands** → pp-run-and-operate.
- **Weather grid channels and evolution semantics** (what `InitialCoverage`, `StormThreshold` mean physically) → pp-weather-sim-reference.

## Provenance and maintenance

Authored 2026-07-06 against branch `code-refactor` (dirty working tree — values reflect the working tree, not the last commit). Every path, default, line number, and command name above was read from source on that date. Known open items, labeled honestly:

- **UNVERIFIED**: the committed `.asset` YAML values for `Planet.asset`, `BiomeSettings.asset`, `AtmosphereSettings.asset`, `CloudSettings.asset` were not diffed against the C# defaults; the tables give class defaults and asset paths only.
- **Verified absence** (2026-07-06): `grep -rn "new WorldSettingsOverride" Assets/Scripts` returns nothing — the override mechanism is fully wired in `LoadingManager`/`WorldContext` but no production code constructs one yet. It is plumbing awaiting the save system.

Re-verify one-liners:

```bash
grep -n "bool _" Assets/Scripts/Planet/PlanetGrassCoordinator.cs                    # layer flags
grep -n "=> 1" Assets/Scripts/Core/QualityController.cs                             # grass profile numbers
grep -n "QualityStepMultiplier" Assets/Scripts/Core/QualityController.cs            # 0.33/0.65/1.0
grep -rn "class .*Settings : ScriptableObject" Assets/Scripts --include=*.cs        # SO inventory
grep -rn "static .* From(" Assets/Scripts --include=*.cs                            # DTO factories
grep -n "Freeze()" Assets/Scripts/Core/Services/SceneBootstrap.cs                   # freeze point
ls "Assets/Game Data/Planet Settings" Assets/Resources/Settings                     # asset locations
```
