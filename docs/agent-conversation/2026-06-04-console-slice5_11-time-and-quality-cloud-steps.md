# 2026-06-04 — Debug Console Slice CONSOLE-5.11: time.set-local + time.moon-phase + quality.cloud-steps

**Status:** Shipped. 3 commands across CelestialManager + QualityController. Build clean.

## Files

**Modified (2):**

- [`Planet/CelestialManager.cs`](../../Assets/Scripts/Planet/CelestialManager.cs) — `time.set-local` and `time.moon-phase` commands
- [`Core/QualityController.cs`](../../Assets/Scripts/Core/QualityController.cs) — `quality.cloud-steps` command + relaxed `CloudStepMultiplier` setter

## Commands shipped (3)

### `time.set-local <float>` (`MonoTargetType.Single`)

Sets time-of-day **relative to the camera's position on the planet**. `0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset`. Wraps modulo 1 if you pass `1.5` or `-0.25`.

The math:

1. Compute `camDir = (cam.position - planetCenter).normalized`
2. Un-tilt via inverse of `Quaternion.Euler(AxialTilt, 0, 0)` to get the camera direction in the sun's orbital frame
3. Project onto the XY plane (sun orbits in untilted XY)
4. Inverse of `UpdateSun`: `tNoon = atan2(camDirInPlane.x, -camDirInPlane.y) / 2π` gives the global `_timeOfDay` when the sun is overhead the camera
5. `_timeOfDay = (tNoon + (fraction - 0.5)) % 1`
6. Call `UpdateSun(0)` to refresh the sun transform immediately

Edge case: camera at planet center → error; camera at a celestial pole → "local time undefined" (no sun rise/set at the pole).

### `time.moon-phase [int?]` (`MonoTargetType.Single`)

- No arg → prints current `MoonPhaseIndex` and `_moonCycleProgress`
- With int arg (0-7) → sets `_moonCycleProgress = (i + 0.5) / 8`, calls `UpdateMoon(0)` to refresh

Wraps modulo 8 (`time.moon-phase 8` = `time.moon-phase 0`, `time.moon-phase -1` = `time.moon-phase 7`). Center-of-bucket placement (`+0.5/8`) avoids the off-by-one where rounding errors might place the moon at the boundary between two phases.

### `quality.cloud-steps [float?]` (`MonoTargetType.Single`)

- No arg → prints current `CloudStepMultiplier`
- With float arg → clamps to `[0.33, 1]` and applies

Required relaxing `CloudStepMultiplier` from `{ get; private set; }` to `{ get; set; }`. The command then `Mathf.Clamp(value, 0.33f, 1f)` so the range Bryan specified is enforced — out-of-range input is silently clamped (the print confirms the actual applied value).

**Caveat documented in the description:** `quality.set <level>` calls `ApplyQualityLevel` which resets `CloudStepMultiplier` back to the tier value. So console overrides survive until the next `quality.set` (or scene reload). This is the right behavior — quality tier changes should reset all quality params.

## Categorization tracker

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.*`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Gameplay / world state** | `action.*` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- Both projects clean (only pre-existing warnings)
- 2 modified files, ~80 net lines

## Validation guidance

1. **`time.set-local 0.5`** — sun snaps to noon directly overhead the camera (assumes camera is somewhere over the planet surface, not in space).
2. **`time.set-local 0`** — sun goes to opposite side of planet from camera (midnight).
3. **`time.set-local 0.25`** — sunrise (sun on the horizon, eastern direction). The geometric definition: sun is 90° before noon relative to camera.
4. **`time.set-local 0.5*0.5`** — math eval works → 0.25 → sunrise.
5. **`time.moon-phase 4`** — moon snaps to full-moon (or whichever phase index 4 represents in the cycle).
6. **`time.moon-phase`** — reads current phase index.
7. **`quality.cloud-steps 0.5`** — cloud rendering becomes faster but coarser (fewer raymarch steps).
8. **`quality.cloud-steps 0.1`** — clamped to 0.33, prints "0.33".
9. **`quality.set 0` after `cloud-steps 0.5`** — `cloud-steps` reverts to whatever the tier dictates (caveat behavior).
10. **At the north pole**: `time.set-local 0.5` → "camera is at a celestial pole — local time undefined" (mathematically correct).

## Remaining inventory

**Phase 2c (heavyweight):**

- `planet.generate [seed] [radius]` — async cancellable, touches Planet's generation pipeline
- `planet.seed [int?]` — get; setting triggers regenerate?
- `planet.resolution [int?]` — same shape as seed
- `debug.module <name>` — switch active debug module (DebugRegistry)
- `debug.mode <id>` — switch visualization mode within active module
- `debug.capture <set>` — wraps F10 capture using the close-console / capture / reopen pattern

Then **code review / cleanup** pass per Bryan's earlier mention.

Picking `debug.module` + `debug.mode` + `debug.capture` next (all touch `DebugRegistry` + `DebugCaptureController`, cohesive batch). Then planet.* as the finale.
