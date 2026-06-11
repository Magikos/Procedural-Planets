# Audit — Weather, Precipitation, Clouds & Atmosphere

**Date:** 2026-06-10
**Branch:** code-refactor
**Auditor:** Claude (subagent)
**Scope:** `Assets/Scripts/Planet/WeatherManager.cs`, `Assets/Scripts/Planet/PrecipitationController.cs`, `Assets/Scripts/Planet/PrecipitationRenderFeature.cs`, `Assets/Scripts/Planet/WeatherLightningController.cs`, `Assets/Scripts/Planet/Precipitation/RainParticleController.cs`, `Assets/Scripts/Planet/Clouds/*`, `Assets/Scripts/Planet/Atmosphere/*`, `Assets/Graphics/Shaders/Cloud.shader`, `Atmosphere.shader`, `Precipitation.shader`, `WeatherParticles.shader`, `RainParticles.shader`, `Assets/Graphics/Shaders/Includes/{CloudShadows,WeatherSampling,ClimateSampling}.hlsl`, `Assets/Resources/RainParticleUpdate.compute`, `Assets/Graphics/Shaders/WeatherEvolution.compute` (referenced), `Assets/Scripts/Core/Services/ShaderGlobalsController.cs`.
**Status:** Findings only — no code modified.

## Executive summary

Weather/precipitation/clouds are largely well-factored — most hot paths now batch globals behind a dirty flag, the GPU weather grid uses a Burst job, and the new compute-buffer `RainParticleController` is a clean separation. The dominant remaining issues are (1) wholesale **Settings DTO** violations in every controller and the SphericalWeatherGrid (runtime reads the `ScriptableObject` directly, including via console commands that mutate it), (2) dead code from the rain refactor (the `DistantRain` and procedural `Rain` profiles in `WeatherParticles.shader` are draw-count-forced to zero but the C# fields, console commands, shader passes, and shader uniforms still ship), and (3) hub-like growth of `WeatherManager.cs` (898 lines, 8 responsibilities). The render-feature scan throttling already addresses NEW-03 from the 2026-05-28 audit but still misses `ServiceLocator` (the controllers all self-register). Atmosphere globals are properly slotted into a static/dynamic split (NEW-06 closed). `PrecipitationController.Update()` unconditionally re-uploads ~17 globals every frame even when nothing changed — the only weather/cloud controller that has not adopted the per-frame elision pattern.

## Findings

### WEATHER-1 🟠 Settings ScriptableObjects read directly at runtime across every controller

- **Category:** Architectural drift
- **Severity:** 🟠
- **Location:** [WeatherManager.cs:45-48,239-253,438-501](../../../Assets/Scripts/Planet/WeatherManager.cs#L45), [CloudController.cs:11-14,121-247](../../../Assets/Scripts/Planet/Clouds/CloudController.cs#L11), [AtmosphereController.cs:7,98-145](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs#L7), [PrecipitationController.cs:29-30,266-360](../../../Assets/Scripts/Planet/PrecipitationController.cs#L29), [SphericalWeatherGrid.cs:150-201,731-766](../../../Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs#L731)
- **Effort to fix:** L
- **Cross-ref:** Memory note `feedback_settings_dto_pattern.md`

Every controller in this subsystem holds a public `ScriptableObject` field and reads it on every frame, every console command, and every dispatch — including from a Burst job (`SphericalWeatherGrid.ScheduleGridJob` passes `CloudSettings`; the job copies primitive fields out, but the public surface still leaks the SO). Three console commands explicitly *mutate* the SO at runtime: `cloud.density`, `cloud.altitude`, `cloud.thickness`, plus all four `atmosphere.*` setters (`Settings.SunIntensity = …`, `Settings.RayleighScattering = …`, etc.). Per the project rule, runtime must consume an immutable snapshot DTO, not the SO. The current shape (CloudSettings binds Weather + Cloud + Evolution + RainFormation + Validation + DebugView together) is also exactly the god-object cross-coupling the DTO pattern exists to prevent.

**Proposed direction:** Add `CloudSettingsSnapshot` / `AtmosphereSettingsSnapshot` / `PrecipitationSettingsSnapshot` (or split into per-feature snapshots: `WeatherEvolutionParams`, `CloudRenderParams`, `WeatherGridSeedParams`, etc.). Build the snapshot once in each controller's `Awake`/`OnPlanetGenerated`; rebuild from console commands by calling `Settings.With…(value)` on a wither method or by going through a dedicated edit-only mutation API; pass the snapshot to `SphericalWeatherGrid.Generate` and `Advance` rather than `CloudSettings`. The `cloud.*` and `atmosphere.*` commands stop mutating the asset and instead update only the runtime snapshot.

### WEATHER-2 🟡 `WeatherManager` has grown into a god-class hub

- **Category:** Cross-coupling
- **Severity:** 🟡
- **Location:** [WeatherManager.cs (entire file, ~898 lines)](../../../Assets/Scripts/Planet/WeatherManager.cs)
- **Effort to fix:** L
- **Cross-ref:** Perf-plan slice 6 (`Planet` split pattern)

`WeatherManager` is now responsible for: grid generation orchestration, advection rotation upload, evolution compute dispatch with multi-step accumulator, three console commands, wind-globals dirty-checked upload, F9-equivalent JSON dump (StringBuilder writer with 6 helper methods), two distinct AsyncGPUReadback caches (query cache + diagnostics readback), aggregate-stats throttle, `OnGUI()` diagnostic overlay, and a Quaternion normalization utility. That mixes simulation, IO, diagnostic rendering, and console UI in one MonoBehaviour and consequently makes any future change (e.g. raising weather events to gameplay, swapping the evolution scheduler) reach across all of them.

**Proposed direction:** Split into `WeatherManager` (LateInitialize + grid ownership + IWeatherProvider surface), `WeatherEvolutionScheduler` (Update accumulator + Advance dispatch), `WeatherDiagnosticsModule` (the F9 dump, the OnGUI overlay, JSON builder), and `WeatherQueryCache` (the two async readback loops). Mirror the slice-4 / slice-6 split pattern already in the perf-maintainability plan.

### WEATHER-3 🟡 `PrecipitationController.Update()` re-uploads ~17 `Shader.SetGlobal*` calls every frame unconditionally

- **Category:** Per-frame hot path / allocations
- **Severity:** 🟡
- **Location:** [PrecipitationController.cs:241-360](../../../Assets/Scripts/Planet/PrecipitationController.cs#L241)
- **Effort to fix:** S
- **Cross-ref:** NEW-06 (2026-05-28) — atmosphere and clouds adopted this pattern; precipitation did not

`UploadGlobals()` runs every `Update()` and unconditionally pushes 17+ `SetGlobalInt/Vector/Color` calls, plus `Camera.main` lookup, plus the LOD lerp, plus `Vector3.Distance`. The two altitude/quality values (`viewSteps`, camera altitude) genuinely change per frame; the other 15 parameters change only when the inspector or a console command flips them. `CloudController` ([CloudController.cs:23-32,136-247](../../../Assets/Scripts/Planet/Clouds/CloudController.cs#L23)) and `AtmosphereController` ([AtmosphereController.cs:18-22,61-145](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs#L18)) already implement the static-vs-dynamic dirty-flag split; precipitation lags.

**Proposed direction:** Mirror the `_staticPropertiesDirty` + `_lastUploadedViewSteps` pattern. Mark dirty on `OnPlanetGenerated`, on each console-command setter, and after `MigrateLocalWeatherParticleSettings`. Push view-step + altitude lerp on every frame; push the rest only when dirty.

### WEATHER-4 🟡 Distant-rain and procedural close-rain shipped but rendered at zero — dead pipeline, surface still wired

- **Category:** Style & dead code
- **Severity:** 🟡
- **Location:** [PrecipitationRenderFeature.cs:142-149,233-247](../../../Assets/Scripts/Planet/PrecipitationRenderFeature.cs#L142), [PrecipitationController.cs:93-103,440-454](../../../Assets/Scripts/Planet/PrecipitationController.cs#L93), [WeatherParticles.shader:239-289,416-429,487-516](../../../Assets/Graphics/Shaders/WeatherParticles.shader#L239)
- **Effort to fix:** M

The new compute-buffer `RainParticleController` replaced the procedural close-rain pass, and `PrecipitationRenderPass.Setup` explicitly forces `_rainParticleCount = 0` and `_distantRainParticleCount = 0`. But the rest of the pipeline is intact: `PrecipitationController` still ships six `DistantRain*` fields with full `[Range]` + tooltips, six matching `[ConsoleCommand]` setters (`distant-rain-radius`, `distant-rain-count`, `distant-rain-length`), two shader globals (`_WeatherParticleDistantRainParams`, `_WeatherParticleDistantRainExtended`), the `Rain` and `DistantRain` passes inside `WeatherParticles.shader`, the `VertRain`/`VertDistantRain` entry points, and the entire profile-1/profile-3 branches in `WeatherParticleVertex`. Same applies to close-rain `Range`/console commands. Risk: a future contributor sets `distant-rain-count 5000` from the console and gets silence, because the field is plumbed but the draw count is zero.

**Proposed direction:** Remove the `Rain`/`DistantRain` passes from `WeatherParticles.shader` (keep `Dust`/`Snow`); delete the `DistantRain*` and close-`Rain*` profile fields, their migrations, and their console commands from `PrecipitationController`; drop the now-unused `_WeatherParticleDistantRain*` and `_WeatherParticleRainExtended` globals; remove the corresponding zero-forcing in `PrecipitationRenderPass.Setup`. Keep `RainColor`/`StormRainColor` and `LocalParticleRadius` (still consumed by `RainParticleController` indirectly via `_PrecipitationRadii`).

### WEATHER-5 🟡 `PrecipitationController.OnValidate` clamps narrower than the `[Range]` attributes

- **Category:** Architectural drift
- **Severity:** 🟡
- **Location:** [PrecipitationController.cs:84,88,91,205-215](../../../Assets/Scripts/Planet/PrecipitationController.cs#L84)
- **Effort to fix:** S

The inspector ranges and the `OnValidate` clamps disagree:

| Field | `[Range]` | `OnValidate` clamp |
| ----- | --------- | ------------------ |
| `RainStreakLength` | `0.05f, 8f` | `0.05f, 2f` |
| `RainFallSpeed`    | `1f, 120f`  | `1f, 25f`   |
| `RainWidth`        | `0.003f, 0.2f` | `0.003f, 0.05f` |

Setting `RainFallSpeed = 60` (the default visible in the inspector and described in the tooltip as *"50-70 m/s looks like fast rain"*) triggers `OnValidate` to snap it back to `25f` whenever Unity re-validates the asset. Console commands `precipitation.rain-length`, `precipitation.rain-speed`, `precipitation.rain-width` use the *narrower* OnValidate ranges and silently ignore the inspector-advertised maximums.

**Proposed direction:** Either widen `OnValidate` clamps and console-command clamps to match the `[Range]` attributes, or narrow the `[Range]` attributes to match the OnValidate intent. Whatever the answer, all three (inspector range, OnValidate, console clamp) need to agree on the same number.

### WEATHER-6 🟠 `RainParticleController` reads `_WindDirection` / `_WindSpeedMps` from `Shader.GetGlobalVector/Float` every frame instead of through `IWeatherProvider`

- **Category:** Architectural drift
- **Severity:** 🟠
- **Location:** [RainParticleController.cs:240-245](../../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L240)
- **Effort to fix:** S

`DispatchUpdate` reads three weather globals back from the shader to feed the compute shader: `_WindDirection`, `_WindSpeedMps`, and `_PrecipitationRadii` (line 139). `IWeatherProvider` exposes `WindDirection` and `WindSpeedMetersPerSecond` directly; the cloud bottom radius could come from `ICloudController.Settings.BaseAltitude` (or via the upcoming snapshot DTO, see WEATHER-1). Using `Shader.GetGlobalVector` as a side-channel for C#-to-C# data flow is fragile (precision conversion through float4, no compile-time guarantee the global was set this frame), and it ties the simulation order to renderer-update order rather than to component init order.

**Proposed direction:** Resolve `IWeatherProvider` once via `ServiceLocator.Get<IWeatherProvider>()`; read wind from the provider. Resolve the cloud-bottom radius via `ICloudController` (or `ICloudController.Settings` → snapshot under WEATHER-1). The shader globals stay as the GPU-side input; the C# side never reads them back.

### WEATHER-7 🔵 `RainParticleController` uses `Debug.LogError` instead of `LoggerProvider`

- **Category:** Style & dead code
- **Severity:** 🔵
- **Location:** [RainParticleController.cs:152,163](../../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L152)
- **Effort to fix:** S

The new file added two `Debug.LogError(...)` calls during shader/compute load. Project rule prefers `LoggerProvider.Get().Log(...)` / `LoggerProvider.LogException` in new code; the rest of the weather/cloud/atmosphere controllers route through `LoggerProvider` already.

**Proposed direction:** Replace with `LoggerProvider.Log(LogLevel.Error, "Rain", "...")`. Trivial.

### WEATHER-8 🔵 `RainParticleController` uses `DestroyImmediate` in `ReleaseResources()`

- **Category:** Architectural drift
- **Severity:** 🔵
- **Location:** [RainParticleController.cs:194-202](../../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L194)
- **Effort to fix:** S
- **Cross-ref:** BUG-01 (2026-05-25), NEW-04 (2026-05-28)

`ReleaseResources` calls `DestroyImmediate(_runtimeMaterial)` from `OnDestroy`. `Planet.cs` was specifically reworked to avoid `DestroyImmediate` in runtime paths; this new file re-introduces the pattern. Materials created via `new Material(shader)` with `HideFlags.HideAndDontSave` are fine to dispose with `Destroy()` in play mode (and `DestroyImmediate` is required only when the call site might be invoked during shutdown editor-mode reload — not the case here).

**Proposed direction:** Use `Destroy(_runtimeMaterial)` unconditionally, matching the `Planet.DestroyChildrenImmediate()` resolution.

### WEATHER-9 🟡 `RainParticleController` uses `FindAnyObjectByType` as the console-command target lookup

- **Category:** Cross-coupling
- **Severity:** 🟡
- **Location:** [RainParticleController.cs:269-321](../../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L269)
- **Effort to fix:** S

The nested static `RainParticleCommands` class does `Object.FindAnyObjectByType<RainParticleController>()` on **every console command invocation**. The controller already registers itself as `IRainParticleRenderer` in `ServiceLocator`. Either widen the interface to expose the few mutable knobs (`ParticleCountSetting`, `CameraNearRadius`, `FallSpeedMps`, `StreakLength`/`Width`, `DensityScale`) or use `MonoTargetType.Single` on the methods (as every other prefix in the project does) and let `CommandExecutor` resolve the target — that path already caches per-target.

**Proposed direction:** Move the commands onto `RainParticleController` directly (the way `PrecipitationController` does it) with `MonoTargetType.Single`, deleting the `RainParticleCommands` nested type.

### WEATHER-10 🔵 `AtmosphereDiagnostics` still polls F12 directly through `Keyboard.current`

- **Category:** Architectural drift
- **Severity:** 🔵
- **Location:** [AtmosphereDiagnostics.cs:31-39](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereDiagnostics.cs#L31)
- **Effort to fix:** S
- **Cross-ref:** QUAL-07 (resolved for F9 since 2026-05-28), DebugInputRelay design

QUAL-07 was closed by moving F9 weather-diagnostics from raw input to `DebugCommandRequestedEvent`. `AtmosphereDiagnostics` is now the only remaining MonoBehaviour that polls `Keyboard.current.f12Key` to fire a debug capture. Same fix shape — add `DebugCommandType.DumpAtmosphereDiagnostics` and route through `DebugInputRelay`.

**Proposed direction:** Add a `DumpAtmosphereDiagnostics` enum, raise from `DebugInputRelay`, listen in `AtmosphereDiagnostics.OnEnable`. Mirror the `OnWeatherDiagnosticsRequested` shape exactly.

### WEATHER-11 🔵 `AtmosphereDiagnostics` uses `Tex2D.GetPixel` per-pixel (allocates RGBA32 internally)

- **Category:** Per-frame hot path / allocations
- **Severity:** 🔵
- **Location:** [AtmosphereDiagnostics.cs:65-189](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereDiagnostics.cs#L65)
- **Effort to fix:** S

`DumpDiagnostics` calls `tex.GetPixel` over ~50 sample points and three region loops. Not a hot path (F12 only), but the call is one of the rare per-pixel managed paths that always allocates a `Color`. Cheap to swap to a single `tex.GetPixels32()` call or grab `GetPixelData<Color32>()` once. Lowest priority — flag only.

**Proposed direction:** Replace with one `GetPixelData<Color32>` followed by index math. Or accept as-is — it runs once per F12 press.

### WEATHER-12 🟡 Render features still call `Object.FindAnyObjectByType` instead of `ServiceLocator.Get`

- **Category:** Architectural drift
- **Severity:** 🟡
- **Location:** [AtmosphereRenderFeature.cs:34-44](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereRenderFeature.cs#L34), [CloudRenderFeature.cs:43-50](../../../Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs#L43), [PrecipitationRenderFeature.cs:47-54](../../../Assets/Scripts/Planet/PrecipitationRenderFeature.cs#L47)
- **Effort to fix:** S
- **Cross-ref:** NEW-03 (2026-05-28) — partially addressed via 1 Hz throttle

NEW-03 was partially resolved by adding a 1-second scan throttle. But the scan is still `Object.FindAnyObjectByType<…>()`, and the cloud / atmosphere / precipitation controllers all self-register with `ServiceLocator`. Using `ServiceLocator.TryGet<ICloudController>(out var _)` is O(1) and survives scene reloads identically (services are unregistered in `OnDestroy`). Render features can still keep the controller reference (or the relevant data through the interface), but the lookup itself should not be a scene scan.

**Proposed direction:** Replace each `FindAnyObjectByType` with `ServiceLocator.TryGet<I…>()` and a re-scan on null. Cache the interface, not the concrete controller, since (a) `PrecipitationRenderFeature` already needs `IRainParticleRenderer` from the locator (and uses it correctly) and (b) the render feature only reads `IsRenderingEnabled`, `ShouldRenderLocalParticles`, the particle counts, and `isActiveAndEnabled`, which can live behind an interface.

### WEATHER-13 ⚪ Atmosphere `_SeaLevelRadius` is set by an id called `_planetRadiusId`

- **Category:** Style & dead code
- **Severity:** ⚪
- **Location:** [AtmosphereController.cs:26,112-113](../../../Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs#L26)
- **Effort to fix:** S

```cs
static readonly int _planetRadiusId = Shader.PropertyToID("_SeaLevelRadius");
…
Shader.SetGlobalFloat(_planetRadiusId, _seaLevelRadius);
Shader.SetGlobalFloat(_densityOriginRadiusId, _seaLevelRadius);
```

The variable name says "planet radius" but the shader key is `_SeaLevelRadius` and the value being uploaded is `_seaLevelRadius`. Either three names disagree by accident, or `_planetRadiusId` should be renamed to `_seaLevelRadiusId`. The audit confirmed `AtmosphereDiagnostics` reads `_SeaLevelRadius` and `_DensityOriginRadius` separately, so the *shader* uniform layout is correct; only the C# name is misleading.

**Proposed direction:** Rename the field to `_seaLevelRadiusId`.

### WEATHER-14 ⚪ `WeatherManager` reverse-equality check on `float` to detect dirty wind

- **Category:** Style & dead code
- **Severity:** ⚪
- **Location:** [WeatherManager.cs:205-219](../../../Assets/Scripts/Planet/WeatherManager.cs#L205)
- **Effort to fix:** S

The dirty-uploaded wind values are compared with `!=` on `Vector3` and `float`. For `float`, this is functionally correct (NaN sentinel != anything, including itself), and the sentinel comments document the intent. But this style breaks the static analyzer's float-equality warning and is fragile against future tweaks. A `bool _windDirty` flag (set in the four setters and `OnValidate`) would be more explicit and avoid the NaN-sentinel trick.

**Proposed direction:** Flag-based dirty tracking, matching the `_staticPropertiesDirty` shape used in `CloudController` / `AtmosphereController`.

### WEATHER-15 🟡 `WeatherSampling.hlsl` is a multi-responsibility include

- **Category:** Style & dead code
- **Severity:** 🟡
- **Location:** [Includes/WeatherSampling.hlsl](../../../Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl)
- **Effort to fix:** S

The header is named "WeatherSampling" but declares `_WeatherLightningParams` + four `_WeatherLightningCellN` uniforms and the `WeatherLightning(...)` evaluator. Lightning is a distinct system (`WeatherLightningController` is a separate MonoBehaviour) and only one of the three consumers (`Cloud.shader` + `WeatherParticles.shader` + `Precipitation.shader`) treats it as part of weather sampling — `RainParticles.shader` includes this file purely for `SampleDynamics` and ends up with the lightning uniforms in its constant buffer for no benefit.

**Proposed direction:** Split into `WeatherSampling.hlsl` (cube-face math + `SampleWeather` + `SampleDynamics`) and `WeatherLightning.hlsl` (the four cell uniforms + `WeatherLightning` evaluator). Each consumer includes only what it needs.

### WEATHER-16 ⚪ `_CloudWeatherResolution` redeclared in both `Precipitation.shader` and `CloudShadows.hlsl`

- **Category:** Style & dead code
- **Severity:** ⚪
- **Location:** [Precipitation.shader:15](../../../Assets/Graphics/Shaders/Precipitation.shader#L15), [Includes/CloudShadows.hlsl:14](../../../Assets/Graphics/Shaders/Includes/CloudShadows.hlsl#L14)
- **Effort to fix:** S

`_CloudWeatherResolution` is declared in `Precipitation.shader` and again in `CloudShadows.hlsl`. URP allows duplicate declarations of the same name+type, but it splits ownership: a future renamer must touch every redundant declaration. The cube-face transform `CloudShadowCubeFaceLocalUp` / `CloudShadowCubeFaceUv` in `CloudShadows.hlsl` is also identical to `WeatherCubeFaceLocalUp` / `CubeFaceUv` in `WeatherSampling.hlsl` (with a different prefix). The `Cloud.shader` was originally the only consumer of `CloudShadows.hlsl`; `WeatherSampling.hlsl` is the newer common path.

**Proposed direction:** Have `CloudShadows.hlsl` `#include "WeatherSampling.hlsl"` (after the split in WEATHER-15) and delete the duplicated cube-face helpers + the duplicate `_CloudWeatherResolution`/`_CloudWeatherRotation` declarations.

### WEATHER-17 ⚪ `PrecipitationController.MigrateLocalWeatherParticleSettings` silently overwrites user-tuned values

- **Category:** Open question
- **Severity:** ⚪
- **Location:** [PrecipitationController.cs:380-398](../../../Assets/Scripts/Planet/PrecipitationController.cs#L380)
- **Effort to fix:** S

The migration unconditionally reassigns nine fields with the new defaults whenever `_localWeatherParticleSettingsVersion < CurrentLocalWeatherParticleSettingsVersion`. The justification (legacy 95 m / 520 arbitrary-units numbers were inscale-incompatible) is correct *for the values that previously existed*; but the migration also stamps fresh defaults on top of values like `DustOpacity`, `DustSize`, `DustTurbulence` that were never legacy and would now silently revert a designer's per-asset tuning the first time the asset is opened on a fresh checkout. Once `_localWeatherParticleSettingsVersion` is bumped, the migration is a one-shot — but if the version field is in `[HideInInspector]` on a `MonoBehaviour` (which it is — this is a scene-component field, not a SO), a fresh prefab instance starts at version 0 and gets stamped.

**Proposed direction:** Migration should only overwrite fields that *had* a known legacy meaning. Newly-introduced fields should keep whatever was authored on the prefab. Alternatively, document that this migration is intentional and one-time and add a comment to that effect. Bryan: is this intentional?

### WEATHER-18 ⚪ `SphericalWeatherGrid.CalculateStats` iterates the whole grid twice (once main, once for max storm)

- **Category:** Per-frame hot path / allocations
- **Severity:** ⚪
- **Location:** [SphericalWeatherGrid.cs:604-645,647-729](../../../Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs#L604)
- **Effort to fix:** S

`TryFindStrongestStorm` and `CalculateStats` both iterate every cell of every face. The diagnostic dump (`DumpWeatherDiagnostics`) calls both. At 256x256x6 = ~393 K cells, two passes is ~786 K compares — not a frame budget, but worth flagging because they're invoked from the F9 dump and the 2-second aggregate stats throttle. `CalculateStats` already computes the strongest-score; `TryFindStrongestStorm` repeats the same scan to populate the direction. Could be fused.

**Proposed direction:** `CalculateStats` returns the strongest storm direction it already computes. Drop the separate pass.

## Cross-cutting themes

- **Settings DTO not adopted in this subsystem.** Every controller violates the rule, sometimes with runtime mutation through console commands. This is the single largest architectural drift in the hotspot and the right place to do a focused snapshot pass before splitting `WeatherManager` (WEATHER-2). It also subsumes part of NEW-06 from the 2026-05-28 audit because the dirty-flag pattern is naturally re-stated as "settings snapshot changed".
- **Rain refactor left two intentional zero-forced code paths plus their full public surface.** WEATHER-4 is dead pipeline pruning; doing it now removes ~120 lines from `PrecipitationController`, the entire procedural-rain path of `WeatherParticles.shader`, and several globals — making the next round of changes substantially easier to read.
- **Render-feature controller lookup is the last `FindAnyObjectByType` hold-out among the weather/cloud/precipitation passes.** Switching to `ServiceLocator` lookup (WEATHER-12) lines up cleanly with the broader move toward interface-driven feature wiring already done elsewhere.
- **Two newish shader includes (`WeatherSampling.hlsl`, `CloudShadows.hlsl`) duplicate cube-face math and uniform declarations.** Worth one tidy pass (WEATHER-15, WEATHER-16) before any further header is added.

## Open questions for Bryan

- WEATHER-1: Is the snapshot DTO meant to also cover the editor-time-only validation fields on `CloudSettings` (`UseValidationEvolutionRates`, `Validation*Multiplier`), or do those stay as a dev-only side channel?
- WEATHER-4: Is the close-rain procedural fallback (`Hidden/WeatherParticles` Rain pass) deliberately kept for an LOD scenario where `RainParticleController` is disabled (e.g. low-spec)? If so, the zero-forcing in `Setup` is the bug, not the surface; if not, the code is safe to delete.
- WEATHER-5: Which range is correct for `RainStreakLength` / `RainFallSpeed` / `RainWidth` — inspector slider or OnValidate clamp? The tooltip on `RainFallSpeed` recommends 50-70 m/s which only the inspector range allows; the OnValidate clamp cuts it to 25 m/s.
- WEATHER-17: Should `MigrateLocalWeatherParticleSettings` overwrite *all* nine fields, or only the legacy-incompatible ones? Right now it silently resets per-prefab tuning to global defaults on a fresh checkout.
- WEATHER-2 split granularity: do you want `WeatherDiagnosticsModule` + `WeatherEvolutionScheduler` as separate `MonoBehaviour`s, or as plain classes owned by `WeatherManager`? The existing project convention so far is `MonoBehaviour` for everything; this is the first place where that starts to feel forced.

## Out-of-scope for this hotspot

- `Ocean.shader` caustics — flagged **DO-NOT-TOUCH**; no audit performed against caustics code paths in this hotspot.
- Slice 3-6 of `docs/design/2026-06-08-performance-maintainability-plan.md` (chunked surface provider split, grass profiling). Referenced as the template for WEATHER-2 only.
- `Planet.cs` split (perf-plan slice 6).
- `ClimateProvider` and `BiomeSettings` ownership — `ClimateProvider` is read by `WeatherManager.GetTemperature` and by `ClimateMapGpuData.Build`, but biome-side ownership lives with the biome hotspot.
- `WeatherEvolution.compute` correctness review (only structural use was checked: kernel binding via `_weatherRead/Write/_dynamics*` ids; the `Advance` dispatch path is clean).
- `RainParticleUpdate.compute` Burst-vs-compute selection — the compute path is brand new and intentional; no audit against alternative implementations.
- `WeatherLightningController` content — only its global-upload pattern was checked; the strike-selection logic is out of scope.
