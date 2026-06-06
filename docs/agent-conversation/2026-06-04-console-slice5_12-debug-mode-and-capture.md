# 2026-06-04 — Debug Console Slice CONSOLE-5.12: debug.mode / debug.capture-set / debug.capture

**Status:** Shipped. 3 commands + 2 completion providers + DebugRegistry/Controller wiring.

## Files

**New (2):**

- [`Console/Intellisense/DebugModeNamesProvider.cs`](../../Assets/Scripts/Core/Console/Intellisense/DebugModeNamesProvider.cs) — popup of all registered debug mode names
- [`Console/Intellisense/DebugCaptureSetNamesProvider.cs`](../../Assets/Scripts/Core/Console/Intellisense/DebugCaptureSetNamesProvider.cs) — popup of all registered capture set names

**Modified (2):**

- [`Core/Services/DebugRegistry.cs`](../../Assets/Scripts/Core/Services/DebugRegistry.cs) — added `ModeIds`, `CaptureSetCount`, `TryFindModeByName`, `TryFindCaptureSetByName`
- [`Core/Services/DebugCaptureController.cs`](../../Assets/Scripts/Core/Services/DebugCaptureController.cs) — registers `DebugRegistry` in `ServiceLocator` so providers can enumerate; new `CaptureCurrentSetAsync` public method; 3 new console commands

## Design call — unified `debug.mode <name>` instead of `debug.module` + `debug.mode`

Bryan's original inventory had **`debug.module <name>`** and **`debug.mode <id>`** as separate commands. After reading the registry I went with a single **`debug.mode <name>`** — modes already have unique descriptive names (`CloudDensity`, `WaterMask`, etc.), so the user can just name what they want directly. Subsumes both.

| Want to look at... | Old (two-step) | New (one command) |
| ------------------ | -------------- | ----------------- |
| Cloud density       | `debug.module cloud` then `debug.mode 3` | `debug.mode CloudDensity` |
| Water mask          | `debug.module water` then `debug.mode 1` | `debug.mode WaterMask`    |

The popup shows all mode names across all modules in registration order. If a module name conflict ever arose (two modes with the same name), the provider picks the first match — easy to fix by renaming.

If we ever genuinely need module-level grouping (e.g., "show me only cloud modes"), we can add `debug.module` later. For now, single command.

## Commands shipped (3)

### `debug.mode [name?]` (`MonoTargetType.Single`)

- No arg → prints current mode name + id
- With name → resolves via `TryFindModeByName` (case-insensitive substring popup), applies via `ApplyDebugMode(id)`
- Unknown name → "unknown debug mode: 'foo'"

### `debug.capture-set [name?]` (`MonoTargetType.Single`)

- No arg → prints current capture set name
- With name → resolves via `TryFindCaptureSetByName`, sets `_f10CaptureSetIndex` to the matched index
- Coexists with the existing `debug.cycle-capture-set` (slice 5.2) which advances to the next set

### `debug.capture` (`MonoTargetType.Single`, async)

The big one — uses the close-console / capture / reopen-console pattern from the original design doc:

```csharp
async Awaitable CaptureCmd(CancellationToken ct)
{
    bool reopenConsole = false;
    if (ServiceLocator.TryGet<IConsoleService>(out var console) && console.IsOpen)
    {
        reopenConsole = true;
        console.Close();
        // Wait for fade-out (FadeDuration=0.12s) so the screenshot isn't mid-alpha
        await Awaitable.WaitForSecondsAsync(0.2f);
    }
    try { await CaptureCurrentSetAsync(ct); }
    finally { if (reopenConsole && console != null) console.Open(); }
}
```

This appears as an async-cancellable command in the console spinner UI. Submitting `console.cancel` while it's running will cancel — though most capture work is fire-and-forget per-step, so cancellation cleanly stops further screenshots without breaking the in-flight one.

## New public method `CaptureCurrentSetAsync`

The existing `TriggerDebugCapture` was a sync method that fire-and-forgot the awaitable. Wrapped it as a proper `async Awaitable` method so callers (the new console command) can `await` completion before doing follow-up (reopening console).

```csharp
public async Awaitable CaptureCurrentSetAsync(CancellationToken ct)
{
    DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();

    if (captureSet.Behavior == DebugCaptureSetBehavior.CurrentModeOnly)
    {
        CycleDebugMode();
        await CaptureDebugScreenshotAsync(...);
        return;
    }
    if (!SaveF10DebugScreenshots) { CycleDebugMode(); return; }
    await CaptureDebugSequenceAsync(GetDebugCaptureModes(), captureScreenshots: true);
}
```

The F10 keypath (`TriggerDebugCapture`) is unchanged — it still fires-and-forgets. Both paths now exist for their respective use cases.

## DebugRegistry registered in ServiceLocator

`DebugCaptureController.Awake` now does `ServiceLocator.Register<DebugRegistry>(_debugRegistry)`. This is how the completion providers (which are plain classes, not MonoBehaviours) get access to the registry to enumerate modes and capture sets.

Unregister in `OnDestroy` for clean teardown.

## Categorization tracker

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*` (now 8 commands), `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.*`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Gameplay / world state** | `action.*` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean. Updated 2026-06-06: `SaveF10DebugScreenshots` is runtime readonly, so the disabled branches remain valid without `CS0162` warnings.
- 4 files (2 new, 2 modified), ~180 net lines

## Validation guidance

1. **`debug.mode`** (no arg) → prints current mode like `"debug mode: Off (.00)"`.
2. **`debug.mode `** (with trailing space) → popup of all registered mode names. Greek-style list — should include `Off`, `CloudWeather`, `CloudDensity`, `CloudStorm`, `WaterMask`, etc.
3. **`debug.mode CloudDensity`** → switches to cloud density visualization. The cloud shader should render the density debug view.
4. **`debug.mode cloud`** (substring) → popup filters to cloud-* modes; Tab to accept the first.
5. **`debug.mode garbage`** → "unknown debug mode: 'garbage'" error.
6. **`debug.capture-set `** (with space) → popup of all capture set names (`Current Mode Only`, `Full Loop`, `Cloud Diagnostics`, etc.).
7. **`debug.capture-set Cloud Diagnostics`** — wait, this has a space in the name. Need to quote: `debug.capture-set "Cloud Diagnostics"`. Test this works (tokenizer should handle quoted strings).
8. **`debug.capture`** with console open → console fades out, F10 capture runs, console fades back in. Screenshot in `local-only/debug-screenshots/` should NOT contain the console overlay.
9. **`console.cancel` during `debug.capture`** → modal appears (it's cancellable), Yes interrupts mid-sequence cleanly.
10. **`help debug`** → lists all 8 `debug.*` commands.

## Remaining inventory

Just **`planet.*`** left:

- `planet.generate [seed] [radius]` — async cancellable, threads `CancellationToken` through Planet's existing generation pipeline
- `planet.seed [int?]` — get; setting probably auto-triggers regenerate (or just stores for next regen)
- `planet.resolution [int?]` — same shape as seed

Then the **code review / cleanup** pass Bryan mentioned earlier. That's the close-out of the console arc.
