# 2026-06-04 — Debug Console Slice CONSOLE-5.9: precipitation.* + lightning.*

**Status:** Shipped. 5 commands across two controllers. Build clean.

## Files

**Modified (2):**

- [`Planet/PrecipitationController.cs`](../../Assets/Scripts/Planet/PrecipitationController.cs) — `[CommandPrefix("precipitation")]` + 2 commands
- [`Planet/WeatherLightningController.cs`](../../Assets/Scripts/Planet/WeatherLightningController.cs) — `[CommandPrefix("lightning")]` + 3 commands

## Commands shipped (5)

### `precipitation.*` (`MonoTargetType.Single`)

| Command                              | Backing field                              | Range |
| ------------------------------------ | ------------------------------------------ | ----- |
| `precipitation.intensity [float?]`   | `Intensity`                                | 0-2   |
| `precipitation.debug-mode [DebugView?]` | `DebugMode` (enum: Off / RainMask / RainDots / StormDots) | enum |

### `lightning.*` (`MonoTargetType.Single`)

| Command                                       | Backing fields                              | Range            |
| --------------------------------------------- | ------------------------------------------- | ---------------- |
| `lightning.enable [bool?]`                    | `EnableLightning`                           | bool             |
| `lightning.delay [float? min] [float? max]`   | `MinDelay`, `MaxDelay`                      | min≥0.5, max≥min |
| `lightning.intensity [float? cloud] [float? rain]` | `CloudFlashIntensity`, `RainFlashIntensity` | cloud 0-8, rain 0-2 |

## Multi-arg get/set pattern

`lightning.delay` and `lightning.intensity` both take two related numbers. Settled on a "all-or-nothing set" pattern:

- Zero args → print both current values
- Both args → set both, print result
- Exactly one arg → error: "needs BOTH values"

```csharp
[ConsoleCommand("delay", "...")]
string DelayCmd(float? min = null, float? max = null)
{
    if (min == null && max == null) return $"delay: {MinDelay}-{MaxDelay}";
    if (min == null || max == null) return "needs BOTH min and max";
    MinDelay = Mathf.Max(0.5f, min.Value);
    MaxDelay = Mathf.Max(MinDelay, max.Value);
    return $"delay: {MinDelay}-{MaxDelay}";
}
```

The intellisense popup will show `<float?: min> <float?: max>` as the ghost hint while typing — clear what the second arg expects.

## Controllers without Settings SO

Both controllers store config directly as public fields on the MonoBehaviour (no separate `ScriptableObject Settings`). They upload to shader globals per-frame in `Update()`. So mutations take effect on the next frame with zero plumbing — no `_staticPropertiesDirty` flag like atmosphere/cloud needed.

## Categorization tracker

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- `dotnet build ProceduralPlanets.Planet.csproj` — clean (pre-existing `CS0414` warning only)
- 2 modified files, ~80 net lines

## Validation guidance

1. **`precipitation.intensity 2`** → much heavier rain shafts. `precipitation.intensity 0` → no precipitation. Restore around 1.15.
2. **`precipitation.debug-mode `** (with space) → popup shows `Off / RainMask / RainDots / StormDots`. Tab/Enter accepts; the rendering changes to that debug view.
3. **`lightning.enable false`** → lightning stops. `lightning.enable true` → resumes.
4. **`lightning.delay 1 3`** → strikes become frequent. `lightning.delay 10 30` → rare. `lightning.delay` (no args) → prints current values.
5. **`lightning.intensity 8 2`** → maximum flash brightness. `lightning.intensity 1 0` → dim cloud flash, no rain flash.
6. **Math eval works here too:** `precipitation.intensity 2/3` → `0.67`.
7. **Two-arg ghost hint:** type `lightning.delay ` (with space) → next-arg ghost should show `<float?: min> <float?: max>`. After typing `5 ` → ghost shrinks to `<float?: max>`.
8. **Wrong arity:** `lightning.delay 5` → "needs BOTH min and max" error.

## What's next

Phase 2b continues. Remaining batches:

- **grass.*** (density, draw-distance) — survey grass controllers
- **action.*** (undo, redo, history) — `WorldActionManager` API
- **time.set-local + time.moon-phase** — new CelestialManager methods
- **quality.cloud-steps** — expose private setter
- **planet.*** (generate async cancellable, seed, resolution) — Phase 2c
- **debug.module / debug.mode / debug.capture** — DebugRegistry providers — Phase 2c

Next slice: probably **grass + action** (small batch, both relatively isolated subsystems).
