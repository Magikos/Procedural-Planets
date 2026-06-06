# 2026-06-04 — Debug Console Slice CONSOLE-5.5: atmosphere.* + cloud.*

**Status:** Shipped. Awaiting Bryan validation on atmosphere/cloud parameter tweaking.

**Goal:** Phase 2b first batch — visual atmosphere/cloud controls. Both `AtmosphereController` and `CloudController` use the same `ScriptableObject Settings` + `_staticPropertiesDirty` pattern, so this is one consistent slice.

## Files

**Modified (2):**

- [`Planet/Atmosphere/AtmosphereController.cs`](../../Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs) — added `[CommandPrefix("atmosphere")]` + 4 commands
- [`Planet/Clouds/CloudController.cs`](../../Assets/Scripts/Planet/Clouds/CloudController.cs) — added `[CommandPrefix("cloud")]` + 3 commands

## Commands shipped (7)

### `atmosphere.*` (`MonoTargetType.Single`)

| Command                                       | Backing field                | Range       |
| --------------------------------------------- | ---------------------------- | ----------- |
| `atmosphere.sun-intensity [float?]`           | `Settings.SunIntensity`      | 1-100       |
| `atmosphere.rayleigh [Vector3?]`              | `Settings.RayleighScattering` | none        |
| `atmosphere.mie [float?]`                     | `Settings.MieScattering`     | 0-0.1       |
| `atmosphere.scale [float?]`                   | `Settings.AtmosphereScale`   | 1.01-1.5    |

### `cloud.*` (`MonoTargetType.Single`)

| Command                          | Backing field                  | Range (m / unit) |
| -------------------------------- | ------------------------------ | ---------------- |
| `cloud.density [float?]`         | `Settings.DensityMultiplier`   | 0-0.08           |
| `cloud.altitude [float?]`        | `Settings.BaseAltitude`        | 20-1000 m        |
| `cloud.thickness [float?]`       | `Settings.LayerThickness`      | 50-1000 m        |

All commands `Mathf.Clamp` to the `[Range(...)]` attribute on the SO field. Out-of-range input is silently clamped (the print confirms the actual applied value).

## Key design call — `_staticPropertiesDirty = true` after mutation

Both controllers cache uploaded shader globals and only re-push when `_staticPropertiesDirty` is set. Editing `Settings.X` at runtime doesn't trigger re-upload by itself. Every set-command flips the flag, so the next `Update()` re-uploads cleanly.

This is also why these commands are `MonoTargetType.Single` — they need to mutate the live `_staticPropertiesDirty` field on the same instance the planet generation pipeline talks to. (`MonoTargetType.Static` wouldn't work; would have to find the instance manually.)

## ScriptableObject safety

These commands mutate fields on `AtmosphereSettings` / `CloudSettings` (ScriptableObject assets). At runtime in Editor Play mode, changes affect the in-memory instance and revert when Play stops (Unity doesn't write the asset back unless explicitly told to). In a built player, the SO is loaded from a serialized blob — mutations are pure in-memory.

So `atmosphere.sun-intensity 50` is safe — it doesn't corrupt your `.asset` file. Verified by reading Unity SO docs; let me know if you observe persistence across Play sessions.

## Categorization tracker

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- `dotnet build ProceduralPlanets.Planet.csproj` — clean (pre-existing `CS0414` warning only)
- `dotnet build ProceduralPlanets.Core.csproj` — clean (pre-existing `CS0162` warning only)
- ~110 net lines

## Validation guidance

1. **Sunset color:** `atmosphere.rayleigh 0.001,0.001,0.005` → much redder sky. Restore to defaults via `atmosphere.rayleigh 0.0012,0.0028,0.0069`.
2. **Sun intensity:** `atmosphere.sun-intensity 5` → much dimmer scene. `atmosphere.sun-intensity 50` → brighter. Try math: `atmosphere.sun-intensity 17*2`.
3. **Atmosphere scale:** `atmosphere.scale 1.5` → atmosphere extends further out (more visible from space).
4. **Cloud density:** `cloud.density 0.05` → much thicker clouds. `cloud.density 0.005` → wispy.
5. **Cloud altitude:** `cloud.altitude 800` → high clouds. `cloud.altitude 100` → ground-hugging clouds.
6. **Cloud thickness:** `cloud.thickness 600` → fat puffy clouds.
7. **Bool popup hint:** Type `cloud.` (with trailing period) — should NOT show a popup yet (popup is per-param, not per-prefix). Then `cloud.density ` → next-arg hint shows `<float: value>`. Type `0.05`. Submit.
8. **Math eval:** `atmosphere.sun-intensity (3+4)*2` → 14.00.
9. **Round-trip:** `cloud.density` (no arg) → current value. Memorize it. Set to something else. Restore via the value you noted.

## What's next

Phase 2b continues with the next subsystem batch. Per Bryan's inventory:

- **precipitation.* + lightning.*** — related weather effects. Survey PrecipitationController + WeatherLightningController.
- **grass.*** — `grass.density`, `grass.draw-distance` (skipping `grass.stats` per Bryan).
- **action.*** — `WorldActionManager` undo/redo/history.
- **time.set-local / time.moon-phase** — need new CelestialManager methods.
- **quality.cloud-steps** — expose private setter on `QualityController.CloudStepMultiplier`.

Picking precipitation+lightning next (related visual systems, like atmosphere+cloud was).
