# 2026-06-04 — Debug Console Slice CONSOLE-5.2: Phase 2a Pure-Decoration Commands

**Status:** Shipped. Awaiting Bryan validation on 18 commands across 6 existing classes — no controller API changes.

**Goal:** Land every command Bryan's inventory listed that can be wired to existing public methods / fields with zero impact on the target subsystem's API. Each one is inline on the target class (Quantum convention) using `[CommandPrefix(...)]` + `[ConsoleCommand(...)]`.

## Commands shipped (18)

### `camera.*` — [FreeCameraController](../../Assets/Scripts/Core/Services/FreeCameraController.cs) (`MonoTargetType.Single`)

| Command                       | Behavior                                                   |
| ----------------------------- | ---------------------------------------------------------- |
| `camera.speed [float?]`       | Get / set `MoveSpeed` (`>= 0` clamp)                       |
| `camera.sensitivity [float?]` | Get / set `LookSensitivity` (`>= 0` clamp)                 |
| `camera.fast-multiplier [float?]` | Get / set `FastMultiplier` (`>= 1` clamp)              |
| `camera.position`             | Print current world position                               |
| `camera.surface-view [bool?]` | Get / set surface-following view (calls `ToggleOrbitSurfaceView` if state differs) |

### `time.*` — [CelestialManager](../../Assets/Scripts/Planet/CelestialManager.cs) (`MonoTargetType.Single`)

| Command                | Behavior                                       |
| ---------------------- | ---------------------------------------------- |
| `time.freeze [bool?]`  | Get / set sun freeze (idempotent; just sets `FreezeTime`) |

### `quality.*` — [QualityController](../../Assets/Scripts/Core/QualityController.cs) (`MonoTargetType.Single`)

| Command                | Behavior                                              |
| ---------------------- | ----------------------------------------------------- |
| `quality.get`          | Print tier, level, name, cloud step multiplier        |
| `quality.list`         | Enumerate `QualitySettings.names` with `*` on current |
| `quality.set <int>`    | Call `SetQualityLevel(int)` (clamps internally)       |

### `scale.*` — [ScaleReferenceMarkers](../../Assets/Scripts/Core/Services/ScaleReferenceMarkers.cs) (`MonoTargetType.Static`, just raises events)

| Command          | Behavior                                                       |
| ---------------- | -------------------------------------------------------------- |
| `scale.drop`     | Raise `DropScaleMarkers` event (M-key equivalent)              |
| `scale.clear`    | Raise `ClearScaleMarkers` event (Shift+M equivalent)           |
| `scale.teleport` | Raise `TeleportToScaleMarkers` event (T-key equivalent)        |

### `debug.*` — [DebugCaptureController](../../Assets/Scripts/Core/Services/DebugCaptureController.cs) (`MonoTargetType.Single`)

| Command                     | Behavior                                                     |
| --------------------------- | ------------------------------------------------------------ |
| `debug.overlay [bool?]`     | Get / set `ShowDebugOverlay` field (F6 equivalent)           |
| `debug.water-details [bool?]` | Get / set `ShowWaterDebugDetails`                          |
| `debug.profiling`           | Toggle high-FPS profiling mode (F11 equivalent)              |
| `debug.precipitation`       | Toggle precipitation rendering (P-key equivalent)            |
| `debug.cycle-capture-set`   | Advance to next F10 capture set (F7 equivalent)              |

### `weather.*` — [WeatherManager](../../Assets/Scripts/Planet/WeatherManager.cs) (`MonoTargetType.Static`, raises event)

| Command                | Behavior                                                     |
| ---------------------- | ------------------------------------------------------------ |
| `weather.diagnostics`  | Raise `DumpWeatherDiagnostics` event (F9 equivalent)         |

## Design decisions

### Inline decoration, not wrapper classes

Per the original design doc and Quantum convention, commands live on the class they target. Each existing class gets `[CommandPrefix("system")]` at the class level and `[ConsoleCommand("verb")]` on small private command methods. The class's public API is undisturbed — the command methods are private and only the reflection scanner sees them.

Trade-off accepted: existing classes acquire a small "console-aware" footprint. Mitigated by the small size of each method (typically 3-5 lines).

### `MonoTargetType.Static` for event wrappers

Where the command's only job is to raise an `EventBus<DebugCommandRequestedEvent>` event, the method is `static` and uses `MonoTargetType.Static`. No instance lookup needed. Applies to `scale.*` and `weather.diagnostics`.

### `MonoTargetType.Single` for stateful commands

When the command needs to read or mutate instance state (camera speed, ShowDebugOverlay, etc.), uses `MonoTargetType.Single` which calls `Object.FindAnyObjectByType` to find the live instance. Works for all the controller classes since they're each singleton MonoBehaviours.

### Nullable params drive get/set pattern

Every `<T?>` parameter with a `null` default enables the "bare alias prints, with-arg sets" UX:

```csharp
[ConsoleCommand("speed", ...)]
string SpeedCmd(float? value = null) {
    if (value == null) return $"camera speed: {MoveSpeed:F2}";
    MoveSpeed = Mathf.Max(0f, value.Value);
    return $"camera speed: {MoveSpeed:F2}";
}
```

`camera.speed` → prints, `camera.speed 25` → sets, both print the resulting value.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (pre-existing `CS0162` warning only)
- `dotnet build ProceduralPlanets.Planet.csproj` — clean (pre-existing `CS0414` warning only)
- ~250 net lines across 7 files

## Validation guidance

1. **camera get/set roundtrip:** `camera.speed` → some value. `camera.speed 25` → 25.00. Camera should move noticeably faster. `camera.speed` → 25.00 confirms.
2. **camera position:** `camera.position` → reasonable coordinates.
3. **time.freeze:** `time.freeze` → current state. `time.freeze true` → freezes (visible sun stops rotating). `time.freeze false` → resumes.
4. **quality.list:** Greek-letter-style enumeration with `*` on current. `quality.set 0` → switches to lowest tier (or whatever index 0 is). Cloud rendering should change visibly. `quality.set <old>` to restore.
5. **scale.drop / clear / teleport:** same as M / Shift+M / T keys.
6. **debug.overlay:** `debug.overlay` → current state. `debug.overlay true` → F6 HUD appears. `debug.overlay false` → hides.
7. **debug.profiling:** Run, see Application.targetFrameRate change.
8. **debug.precipitation:** Toggle and watch precipitation render on/off.
9. **debug.cycle-capture-set:** Each invocation advances to next F10 capture set.
10. **weather.diagnostics:** A `weather_diagnostics_*.txt` (or similar) appears in `local-only/`.
11. **`help camera`** → lists all `camera.*` commands. **`help debug.precipitation`** → describes signature + behavior.

## What's next

**Phase 2b — needs new controller mutators (~10-15 commands):**

| Command pack                          | Mutators needed                                          |
| ------------------------------------- | -------------------------------------------------------- |
| `time.set-local <0-1>`                | CelestialManager: `SetTimeOfDayFromCameraLongitude(...)` |
| `time.speed <multiplier>`             | CelestialManager: `DayLengthSeconds` (already public!)   |
| `time.moon-phase <0-7>`               | CelestialManager: moon-phase override                    |
| `weather.wind-speed [float?]`         | WeatherManager: `Speed` (already public!)                |
| `weather.wind-direction [Vector3?]`   | WeatherManager: `WindDir` (already public!)              |
| `weather.precipitation <0-1>`         | WeatherManager: needs precipitation override             |
| `atmosphere.sun-intensity`, `atmosphere.rayleigh`, `atmosphere.mie` | AtmosphereController API survey |
| `cloud.density`, `cloud.altitude`, `cloud.thickness` | CloudController API survey                |
| `precipitation.intensity`, `precipitation.debug-mode` | PrecipitationController API                |
| `lightning.enable`, `lightning.delay`, `lightning.intensity` | WeatherLightningController API       |
| `grass.density`, `grass.draw-distance` | Grass controller surface                                |
| `quality.cloud-steps <0.33-1>`        | Expose `CloudStepMultiplier` setter + Refresh            |
| `action.undo`, `action.redo`, `action.history` | WorldActionManager API                          |

Some are pleasant surprises — `weather.wind-direction` for instance just needs `WindDir` (already public), so it's actually Phase 2a territory I missed. I'll do a quick scan before Phase 2b proper.

**Phase 2c — async / heavyweight (~7 commands):**

- `planet.generate [seed] [radius]`, `planet.seed`, `planet.resolution`
- `debug.module <name>`, `debug.mode <id>`, `debug.capture <set>`

**Code review / cleanup pass:** As we touch each subsystem for Phase 2b, surface refactor opportunities. End of arc.

Once Bryan validates Phase 2a, I'll do a 5-min API scan to grab anything that was actually trivially decoratable (like `weather.wind-direction`), then propose the Phase 2b first batch.
