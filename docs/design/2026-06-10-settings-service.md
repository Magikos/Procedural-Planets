# Settings Service + smaller SO breakup design

**Date:** 2026-06-10
**Status:** World scoping, duplicate protection, construction freeze, and required-DTO validation implemented
**Branch:** code-refactor
**Closes:** Audit findings PLANET-1, WEATHER-1, GRASS-1 (Settings DTO violations)

## Goal

End all direct `ScriptableObject` reads from runtime code. Replace with an `ISettingsService` that aggregates SOs at boot, builds immutable per-domain DTOs, and notifies subscribers when settings change. Break god-SOs (`CloudSettings`, `PlanetSettings`) into narrow targeted SOs that compose into DTOs.

Per [CLAUDE.md](../../CLAUDE.md): SOs are editor-only authoring surfaces; runtime reads DTOs.

## Locked decisions

- **SOs are narrow.** One SO per coherent concern. No god-SOs.
- **DTOs are immutable records.** Use a sealed record class for composed settings snapshots; reserve readonly record structs for genuinely small value types.
- **Each DTO has `From(SO[, …])` static factory.** Composition root for that DTO. Rename a field on the SO, only the factory changes.
- **Service surface is `ISettingsService.GetSettings<TDto>()`** + `EventBus<SettingsChangedEvent>` for change notification.
- **Consumers fetch once and cache.** Re-fetch on `SettingsChangedEvent`. Never `GetSettings<>` per frame.
- **Console-command setters update the runtime DTO via the service.** They never write to the SO asset.
- **`Material` assets are cloned on first use.** Runtime never mutates an SO-referenced material asset.

## Interfaces

```csharp
public interface ISettingsService
{
    TDto GetSettings<TDto>();
    void Update<TDto>(TDto next);
}

public readonly struct SettingsChangedEvent
{
    public readonly Type DtoType;
    public SettingsChangedEvent(Type dtoType) { DtoType = dtoType; }
}
```

- `GetSettings<TDto>()` returns the current cached DTO. Throws if the service hasn't built one for that type.
- `Update<TDto>(next)` replaces the cached DTO and raises `SettingsChangedEvent(typeof(TDto))`. Used by console commands and any other runtime mutator.
- Consumers subscribe via `EventBus<SettingsChangedEvent>.Listen(OnChanged)`, filter on `evt.DtoType == typeof(MyDto)`, and call `_settings = _service.GetSettings<MyDto>()` on match.

## DTO authoring shape

```csharp
public sealed record CloudRenderDto(
    Color Color,
    float Density,
    float AltitudeKm,
    float ThicknessKm,
    float Scattering)
{
    public static CloudRenderDto From(CloudRenderSettings so) => new(
        so.Color, so.Density, so.AltitudeKm, so.ThicknessKm, so.Scattering);
}
```

Records give us `with` for partial updates: `_service.Update(current with { Density = 0.7f });`.

DTOs that compose multiple SOs declare them in the factory:

```csharp
public sealed record PrecipitationDto(
    bool RenderPrecipitation,
    float Intensity,
    float RainParticleSize,
    Color RainColor)
{
    public static PrecipitationDto From(PrecipitationRenderSettings render, RainParticleSettings rain)
        => new(render.RenderPrecipitation, render.Intensity, rain.ParticleSize, render.RainColor);
}
```

## Target service shape

The initial implementation mirrored `LoggerProvider` with a cached fallback. That fallback is transitional. The target is one registry constructed, populated, validated, and frozen by the active world context; see [2026-06-13-world-lifecycle.md](2026-06-13-world-lifecycle.md).

```csharp
public static class SettingsProvider
{
    public static ISettingsService Get() =>
        ServiceLocator.Get<IWorldContext>().Settings;

    public static TDto GetSettings<TDto>() => Get().GetSettings<TDto>();
    public static void Update<TDto>(TDto next) => Get().Update(next);
}

public sealed class SettingsService : ISettingsService
{
    readonly Dictionary<Type, object> _dtos = new();
    bool _frozen;

    public void Register<TDto>(TDto initial)
    {
        if (_frozen) throw new InvalidOperationException("Settings registration is frozen.");
        if (!_dtos.TryAdd(typeof(TDto), initial))
            throw new InvalidOperationException($"{typeof(TDto).Name} is already registered.");
    }

    public TDto GetSettings<TDto>() => (TDto)_dtos[typeof(TDto)];

    public void Update<TDto>(TDto next)
    {
        _dtos[typeof(TDto)] = next;
        EventBus<SettingsChangedEvent>.Raise(new SettingsChangedEvent(typeof(TDto)));
    }

    public void ValidateRequired(IReadOnlyCollection<Type> required) { /* throw for each missing type */ }
    public void Freeze() => _frozen = true;
}
```

- **No lazy fallback.** Access without an active world is an error.
- **Composition is explicit.** The world bootstrap loads authoring assets and registers each DTO before initialization.
- **Registration is one-time.** Duplicate registration or registration after `Freeze()` throws.
- **Validation is eager.** Missing required DTOs fail before any world initializer runs.
- **Save loading happens at the boundary.** Stable persisted keys are translated into typed DTO registrations during world construction.

## Console-command setter shape

Before:

```csharp
[ConsoleCommand("cloud.density")]
string CloudDensityCmd(float? value = null)
{
    if (value.HasValue) Settings.Density = value.Value;  // mutates SO asset
    return $"cloud density: {Settings.Density:F2}";
}
```

After:

```csharp
[ConsoleCommand("cloud.density")]
string CloudDensityCmd(float? value = null)
{
    var current = _service.GetSettings<CloudRenderDto>();
    if (value.HasValue)
        _service.Update(current with { Density = value.Value });
    return $"cloud density: {current.Density:F2}";
}
```

Console command lives on the `CloudController` service that consumes the DTO — the same place that knows what the density means. Per [CLAUDE.md](../../CLAUDE.md), commands stay on the service that owns the state.

## CloudSettings breakup (first concrete target)

Current `CloudSettings` is the audit's canonical god-SO. Six candidate splits:

| New SO                                              | Fields it owns                                                             | DTO it feeds                                                    |
| --------------------------------------------------- | -------------------------------------------------------------------------- | --------------------------------------------------------------- |
| `CloudRenderSettings`                               | Color, density, altitude, thickness, scattering, lit color, ambient        | `CloudRenderDto`                                                |
| `CloudEvolutionSettings`                            | Advection speed, dissipation rate, evolution timestep, ping-pong RT format | `CloudEvolutionDto`                                             |
| `WeatherGridSettings`                               | Grid resolution, noise octaves, seed bias, weather field scales            | `WeatherGridDto`                                                |
| `RainFormationSettings`                             | Rain threshold, storm threshold, lightning threshold                       | `RainFormationDto`                                              |
| `CloudValidationSettings` (`#if UNITY_EDITOR`-only) | UseValidation*, Validation*Multiplier                                      | `CloudValidationDto` (or stays editor-only — see open question) |
| `CloudDebugViewSettings`                            | DebugView mode, debug overlay knobs                                        | `CloudDebugViewDto`                                             |

Five of six are clear. `CloudValidationSettings` is a question — see open questions.

`PlanetSettings` gets a similar pass in Wave 2 follow-up: at minimum `PlanetGenerationSettings` (radius, ocean level, resolution), `PlanetWaterSettings` (water color, frozen-water knobs, mesh build), `PlanetGrassSettings` (the grass-blanket and four-tier altitude thresholds — currently magic numbers in `Planet.cs`), and `PlanetMaterialSettings` (the planet material reference, the shader keywords).

## Migration plan

Land in narrow slices, one consumer end-to-end per commit. Each slice is small enough to validate in Unity before the next.

1. **Skeleton.** `ISettingsService`, `SettingsService`, `SettingsProvider`, and `SettingsChangedEvent`. The currently shipped lazy provider is transitional and is replaced by world-owned construction in the lifecycle pass.
2. **First consumer: `GrassPlacementController`** — closes the GRASS-1 showcase finding. Build `GrassBiomeTintConfig` (already exists) through the service; route `BiomeSurfaceTextureArrays.ResolveGrassParams` to consume it. Smallest blast radius; highest visibility.
3. **`PrecipitationController`.** Single SO, simpler than the cloud breakup. Land before the cloud work to exercise the console-command pattern.
4. **`CloudSettings` breakup.** Most work, biggest payoff. Six SOs, six DTOs, three controllers refactored (`CloudController`, `SphericalWeatherGrid`, `WeatherManager` — the last partial, since `WeatherManager` is also a split target). Ship in two commits: (a) create the new SOs, populate them from the existing god-SO via an editor migration script (or inspector copy-paste — TBD), build the DTOs at boot; (b) point consumers at the DTOs, remove the god-SO references.
5. **`AtmosphereController`.** Single SO. Pattern is established by now.
6. **`PlanetSettings` breakup.** Largest scope because it touches the grass-LOD altitudes, the water build, and `Planet.cs` itself. Likely sequenced with the `Planet.cs` split (perf-plan slice 6) rather than standalone.

After slice 6, all remaining direct SO reads should be in editor scripts only. Audit grep confirms.

## Open questions

1. **Validation SO.** `CloudValidationSettings` holds editor-only validation knobs (`UseValidationEvolutionRates`, `Validation*Multiplier`). Three options:
   - Real SO with `#if UNITY_EDITOR`-only fields. Ships nothing at runtime but the asset still exists.
   - Stay on the parent `CloudEvolutionSettings` SO under an `[#if UNITY_EDITOR]` block. Less file churn.
   - Drop entirely — replace with console-command overrides at runtime. Editor workflow changes.
     Recommended: keep on `CloudEvolutionSettings` under an `#if UNITY_EDITOR` block. Don't proliferate SOs for editor-only knobs.
2. **Discovery convention.** Current design: explicit `Resources.Load<TSettings>("Settings/Name")` per SO inside the `SettingsService` constructor. At ~20 SOs this becomes tedious. Two upgrade paths if/when the list gets long: (a) `[BuildsDto(typeof(TDto))]` attribute on each SO + `Resources.LoadAll<ScriptableObject>("Settings")` + reflection dispatch in the constructor; (b) `IRegistersSettingsDto` interface on each SO that knows how to register itself. Recommendation: defer until we hit ~15 SOs. Explicit Resources.Load wins on debuggability today.
3. **`with` expression performance for big DTOs.** Records do struct-copy on `with`. For a 40-field DTO that's not free. Mitigations: keep DTOs narrow (the breakup encourages this naturally), or use `class record` for large DTOs (heap alloc, but the alloc happens only on change). Recommended: narrow DTOs by default; switch to class record per-DTO if profiling shows the copy is hot.
4. **Migration tool for the god-SO breakup.** Two options for migrating existing `CloudSettings.asset` to the six new SOs: editor script that creates the six SOs from the one, or manual inspector copy-paste. Recommendation: editor script — it's a one-off and the copy-paste is error-prone across 30+ fields. Script lives in `Assets/Editor/Migration/CloudSettingsMigration.cs`, runs once, gets deleted after the migration commit.
5. **OnValidate forwarding.** When the inspector edits an SO in editor play mode, should the service automatically rebuild the DTO and raise the event? Recommendation: yes — `OnValidate` on each SO calls the service's `Refresh<TDto>()` method (editor-only path). Keeps the live-tuning workflow honest.
6. **Per-DTO event vs single event.** Single `SettingsChangedEvent(Type)` is simpler. Per-DTO `EventBus<SettingsChangedEvent<CloudRenderDto>>` is compile-time filtering. Recommendation: single event with type token. Less ceremony, no generic event class proliferation.

## Out of scope

- A generic SO discovery / `Resources.LoadAll` + attribute-dispatch service. Explicit per-SO `Resources.Load` is enough for now (see open question 2).
- Save/load of runtime DTOs to disk. The service builds from SOs each boot; runtime mutations are not persisted. Add later only if a real consumer needs it.
- Cross-DTO atomic updates ("change A and B in one event"). Single-DTO updates only.
- Editor inspector for the DTO snapshots themselves. SOs are the editor view; DTOs are runtime-only.
