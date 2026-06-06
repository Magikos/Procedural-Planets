# 2026-06-04 — Debug Console Slice CONSOLE-5.10: action.* + grass.* deferred

**Status:** Shipped action.*. Deferred grass.* pending Bryan's call.

## Files

**New (1):**

- [`Core/Services/Commands/ActionCommands.cs`](../../Assets/Scripts/Core/Services/Commands/ActionCommands.cs) — `[CommandPrefix("action")]` static class with 4 commands

**Modified (2):**

- [`Core/Interfaces/IWorldActionManager.cs`](../../Assets/Scripts/Core/Interfaces/IWorldActionManager.cs) — added `History` and `HistoryIndex` for inspection
- [`Core/Services/WorldActionManager.cs`](../../Assets/Scripts/Core/Services/WorldActionManager.cs) — implements the two new members (trivial getters)

## Commands shipped (4)

### `action.*` (`MonoTargetType.Static`, resolves via `ServiceLocator.TryGet<IWorldActionManager>`)

| Command          | Behavior                                                                |
| ---------------- | ----------------------------------------------------------------------- |
| `action.undo`    | Async + cancellable. Calls `mgr.UndoAsync(ct)`.                         |
| `action.redo`    | Async + cancellable. Calls `mgr.RedoAsync(ct)`.                         |
| `action.history` | Prints all actions oldest→newest with `>` marker on the current cursor. |
| `action.clear`   | Wipes the entire history (no confirm — history isn't reversible anyway).|

Async commands accept a trailing `CancellationToken` parameter so the console's `console.cancel` modal works against them. This is the first non-test use of the CancellationToken plumbing from CONSOLE-4.5.

## Why static class (not inline on WorldActionManager)

`WorldActionManager` is a plain class (NOT a `MonoBehaviour`), registered via `ServiceLocator` rather than scene-found. Three options:

1. `MonoTargetType.Single` — fails, requires `UnityEngine.Object`
2. `MonoTargetType.Registry` — works but requires wiring `ConsoleRegistry.RegisterInstance` at bootstrap
3. **Static wrapper that resolves via `ServiceLocator`** ✓

Option 3 keeps the commands self-contained — no bootstrap wiring needed, the commands just look up the service at invocation time. Same pattern would work for any future non-MonoBehaviour service.

## Note — `action.history` will look empty for a while

Per the `// FUTURE:` comment in `WorldActionManager.cs`, this command pattern is for terrain deformation / building place / remove — features that don't exist yet. Running `action.history` today will always print "no actions in history" because nothing is calling `ExecuteAsync` yet. The commands are wired and ready for whenever player-interaction work begins.

## grass.* deferred — design question for Bryan

Surveyed `IGrassQualitySettings` + `GrassPlacementController` + `GrassNearFieldController`:

- `IGrassQualitySettings` is a read-only interface (no setters)
- `GrassPlacementController` reads from it **once at init** (line 139-142) and caches into local fields
- Quality is conceptually tier-driven via `QualityController`, not per-parameter

To make `grass.density [0-2]` and `grass.draw-distance <m>` actually take effect at runtime, one of:

- **A** — make `IGrassQualitySettings` mutable + re-read on each dispatch (~80 lines, touches grass dispatch hot path)
- **B** — add a "console override" layer in front of the cached values (~50 lines, adds indirection)
- **C** — defer entirely; revisit if/when runtime grass tuning becomes a real need

Recommended **C**. Reasoning in chat: grass quality is intentionally tier-driven, runtime per-param tuning cuts against that architecture. Skip unless Bryan signals otherwise.

## Categorization tracker

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Gameplay / world state** | `action.*` (NEW) |
| **Console internal** | `console.abandon`, `console.cancel` |

Added a third "Gameplay / world state" tier — these are user-affecting commands that should stay in release builds (unlike debug). `action.*` is the first.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- 3 files (1 new, 2 modified), ~80 net lines

## Validation guidance

1. `action.history` → "no actions in history" (expected — nothing's executing actions yet).
2. `action.undo` → silently does nothing (history index is -1).
3. `action.redo` → silently does nothing.
4. `action.clear` → "action history cleared".
5. **When world actions DO ship later** (terrain deform, building, etc.): they'll register via `ExecuteAsync` and `action.history` will show them. Until then this is plumbing-only.
6. `help action` → lists all 4 `action.*` commands.

## What's next

Remaining Phase 2b items:

- **time.set-local + time.moon-phase** — need new CelestialManager methods
- **quality.cloud-steps** — expose private setter on `CloudStepMultiplier`

Then Phase 2c (heavyweight):

- **planet.generate + planet.seed + planet.resolution** — async cancellable; touches generation pipeline
- **debug.module + debug.mode + debug.capture** — DebugRegistry providers, close/capture/reopen

Next slice: probably `time.set-local + time.moon-phase + quality.cloud-steps` together — all small extensions to existing classes.
