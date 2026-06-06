# 2026-06-04 — Debug Console Slice CONSOLE-4: History, Async UX, Test Commands

**Status:** Shipped. Awaiting Bryan validation (history Up/Down outside popup, async spinner + completion, `test.console.*` proof commands, `help test` prefix listing).

**Design doc:** [docs/design/2026-06-03-debug-console.md](../design/2026-06-03-debug-console.md) — slice CONSOLE-4 (reduced scope after CONSOLE-3 absorbed page scroll + scrollbar).

**Goal:** Close out the console proper — persistent input history, Up/Down recall outside the popup, in-place spinner UX for async commands, plus a `test.console.*` test command suite (and a convention for adding `test.<system>.*` packs later).

## Files

**New (2):**

- [`Console/ConsoleHistory.cs`](../../Assets/Scripts/Core/Console/ConsoleHistory.cs) — 100-line ring, PlayerPrefs JSON serialization, cursor + draft preservation
- [`Console/Commands/TestConsoleCommands.cs`](../../Assets/Scripts/Core/Console/Commands/TestConsoleCommands.cs) — 7 commands under `test.console.*` prefix

**Modified (4):**

- [`Console/ConsoleScrollback.cs`](../../Assets/Scripts/Core/Console/ConsoleScrollback.cs) — added `Id` to `ConsoleMessage`, `Append` now returns `long` id, new `Replace(long id, ...)` for stable line targeting through ring trim
- [`Console/IConsoleService.cs`](../../Assets/Scripts/Core/Console/IConsoleService.cs) — added `BeginAsync(string alias, object awaitable)`
- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — history navigation, pending-async state machine, spinner update, prompt cue, reject-second-submit
- [`Console/Registry/CommandExecutor.cs`](../../Assets/Scripts/Core/Console/Registry/CommandExecutor.cs) — async returns now route to `console.BeginAsync(alias, result)` instead of the placeholder message
- [`Console/Commands/ConsoleBuiltins.cs`](../../Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs) — `help` now falls back to prefix-match if no exact alias matches

## Async UX implementation

The spec from chat (recap):

> Inline scrollback line with elapsed time + cycling dots, replaced in-place on completion.
> `... > ` prompt cue while pending. Submit-while-pending rejected with a warning.

Implementation details:

### Stable line identity

The async line lives in scrollback but must survive ring trim if the user runs `test.console.spam 1000` while async is pending. Solution: every `ConsoleMessage` now carries a `long Id`, allocated monotonically by `Append`. `Replace(id, ...)` does a linear scan for the id (O(N) but N ≤ 1000 and called at ~5Hz — trivially cheap). Silent no-op if the id was trimmed out.

### Spinner update

A throttled per-frame update in `ConsoleController.Update`: every 0.2s while `_pending.HasValue`, rewrite the pending line with `running {alias} {dots} ({elapsed:F1}s)`. Dot phase advances every 0.125s (4 phases per 0.5s cycle: `"   "`, `".  "`, `".. "`, `"..."`). Throttle keeps mesh rebuilds to ~5Hz instead of per-frame.

### Awaitable<T> reflection

`Awaitable` (no return) can just be `await`'d. `Awaitable<T>` requires `T` at compile time, which the executor doesn't have. Solution: polling reflection helper `AwaitGeneric`:

```csharp
var awaiter = result.GetType().GetMethod("GetAwaiter").Invoke(result, null);
while (!(bool)awaiterType.GetProperty("IsCompleted").GetValue(awaiter))
    await Awaitable.NextFrameAsync();
return getResult.Invoke(awaiter, null);  // T-typed via reflection
```

Polling once per frame is fine for debug — async commands won't be tight loops. `TargetInvocationException` is unwrapped so the underlying exception message surfaces.

### Lifecycle safety

`ObservePending` is fire-and-forget (`_ = ObservePending(...)`). If the controller is destroyed mid-await, the continuation does `if (this == null || scrollback == null) return;` and silently drops the update. The awaitable itself still runs to completion in the background — no leak. Unity's destroyed-object null comparison makes this safe.

### Concurrency policy

Per design: **one pending at a time, reject subsequent.** Both `SubmitInputLine` and `BeginAsync` short-circuit with a warning if `_pending.HasValue`. The input buffer is preserved on rejected submit so the user can wait + resubmit without retyping.

Closing the console does NOT cancel — the awaitable keeps running and the completion line gets appended when the user next opens. Cancellation (Ctrl+C) is deferred per design doc.

## History UX implementation

### Cursor semantics

- `Cursor == -1` → live input (not navigating)
- `Cursor == 0` → newest entry
- `Cursor == Count - 1` → oldest entry

`Previous()` moves toward older; `Next()` moves toward newer / live input.

### Draft preservation

If the user has typed `"echo bo"` and presses Up to look at history, that draft is saved to `_draftBeforeHistory`. Pressing Down past the newest entry restores the draft. If the user types or backspaces while navigating history, the draft is invalidated (they've taken over the recalled entry) — `OnInputMutated` handles this.

### Popup-vs-history disambiguation

When the suggestion popup is visible, Up/Down navigate the popup (existing behavior). When the popup is hidden, Up/Down navigate history. The disambiguation is the existing `PopupVisible` predicate — no new flags needed.

### Persistence

`PlayerPrefs.SetString("ConsoleHistory", JsonUtility.ToJson(...))`. Saved on `Close()` and `OnDestroy`. Loaded lazily on first `Open()` (so consoles in release builds with `--allowDebug` off don't touch PlayerPrefs). 100-entry cap (~20KB worst case).

Dedupe: identical-to-previous entries don't get re-added (avoids history bloat from repeated Up→Enter).

## Help prefix-match

When `help <name>` has no exact match, falls back to listing all aliases starting with `<name>.`. So:

- `help test` → lists all `test.*` (or `test.console.*` if that's all there is yet)
- `help test.console` → lists all `test.console.*`
- `help bogus` → still errors with "unknown command"

Three lines of behavior change, but discoverability for free.

## Test command convention

`test.<system>.<scenario>` prefix pattern, no new attributes. The console registry already handles `[CommandPrefix("test.console")]` correctly — test commands are just commands.

**Filesystem convention:**

- Console-specific tests: `Assets/Scripts/Core/Console/Commands/TestConsoleCommands.cs` (shipped this slice)
- Subsystem tests: **colocated with the system** — e.g. `Assets/Scripts/Planet/TestWeatherCommands.cs` next to `WeatherManager.cs` when we get there. Same assembly means tests can poke at `internal` state without visibility gymnastics.

**Filter for release:** if it becomes desirable to strip test commands from a non-debug build, a one-line check at registry scan time (`alias.StartsWith("test.")`) handles it. Not implemented yet — `--allowDebug` already gates the entire console.

### Shipped test commands

| Alias                         | Description / Tests                                              |
| ----------------------------- | ---------------------------------------------------------------- |
| `test.console.colors`         | All 7 message types + inline `<color=...>` markup + hex + escape |
| `test.console.async [s=2]`    | Awaitable that completes after N seconds                         |
| `test.console.async-result [s=2]` | Awaitable<string> — return value prints after completion     |
| `test.console.async-fail [s=2]`   | Awaitable that throws — error UX                              |
| `test.console.error`          | Sync exception — game doesn't crash, error formatted             |
| `test.console.types <i> <f> <b> <vec3> <color>` | Round-trip every built-in parser                |
| `test.console.spam [n=200]`   | Fast Print loop — ring trim, scrollbar, output rebuild           |

`DelayUnscaledAsync` is a small helper that polls `Time.unscaledTime` so tests work even with `Time.timeScale == 0`.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- 2 new files, 5 modified files
- Net ~340 lines (vs. estimated ~295 — slightly over because the async observer + reflection helper + line-id refactor stacked up)

## Acceptance criteria refresh

| # | Criterion                                                          | Status   |
| - | ------------------------------------------------------------------ | -------- |
| 1 | Backtick toggles, Esc closes, gameplay input gated                 | ✅       |
| 2 | EventBus events fire on open/close                                 | ✅       |
| 3 | Bad commands print red error without crashing                      | ✅       |
| 4 | Intellisense suggests as you type, Tab accepts, substring + prefix | ✅       |
| 5 | Color tags render correctly                                        | ✅       |
| 6 | PageUp/Down scrolls; auto-scroll re-engages on submit              | ✅       |
| 7 | History persists across game restart                               | ✅ (NEW) |
| 8 | `debug.capture grass` captures cleanly                             | ❌ CONSOLE-5 |
| 9 | Release build without `--allowDebug` has no console                | ✅       |
| 10 | F10 sidecar `--- DebugConsole ---` block                          | ❌ CONSOLE-5 |
| BONUS | Async commands display spinner + complete in-place              | ✅ (NEW) |

## Validation guidance for Bryan

In Play mode after Unity recompile:

1. **History persistence:** open console, run `echo a`, `echo b`, `echo c`, close console. Reopen. Press Up — should recall `echo c`. Up again → `echo b`. Up again → `echo a`. Down → `echo b`. Down → `echo c`. Down → empty line.
2. **History across restart:** stop Play, start Play again, open console, Up → `echo c` recalled from previous session.
3. **Draft preservation:** type `echo bo` (don't submit). Press Up — `echo c` appears. Press Down — `echo bo` reappears.
4. **History dedupe:** run `echo a` three times. Up arrow should only show one `echo a`, not three.
5. **Async spinner:** `test.console.async 3` — should see `running test.console.async ... (X.Xs)` with cycling dots, then replaced with `test.console.async completed in 3.0Xs`.
6. **Async result:** `test.console.async-result 2` — same spinner, then completion line, then a separate line printing the return string.
7. **Async failure:** `test.console.async-fail 2` — spinner, then `[Error] test.console.async-fail: test async failure (this is expected) (2.0Xs)` in red.
8. **Reject-second-submit:** start `test.console.async 5`. While running, type `echo hello` + Enter — should warn "still running" without executing. Input buffer should stay populated so you can wait and resubmit.
9. **Prompt cue:** while async is pending, prompt should show `... > ` instead of `> `.
10. **Sync error:** `test.console.error` — red error, game keeps running.
11. **All parsers:** `test.console.types 42 3.14 true 1,2,3 #ff8800` — should echo back parsed values.
12. **Spam + scrollbar:** `test.console.spam 500` — 500 lines fly by; scrollbar appears; PageUp scrolls through history; PageDown returns to tail.
13. **Color test:** `test.console.colors` — see all 7 message types in different colors, plus inline `<color=...>` markup working.
14. **Help prefix:** `help test` — lists all `test.*` commands. `help test.console` — lists all `test.console.*`. `help test.console.async` — describes that single command.

If any of these misbehave, the slice log captures the design decisions for diagnosis.

## What's next

**CONSOLE-5** is the payoff slice — per-module commands so the console becomes a real debugging tool:

| Item                                                                | Lines |
| ------------------------------------------------------------------- | ----- |
| `weather.*` commands (wind-speed, wind-direction, state)            | ~30   |
| `time.*` commands (sun-elevation, freeze)                           | ~25   |
| `grass.*` commands (density-mult, show-coverage)                    | ~30   |
| `planet.*` commands (regenerate, seed)                              | ~30   |
| `debug.capture <set>` using close/capture/reopen pattern            | ~25   |
| `quit`, `console.anchor`, `console.scrollback-size` built-ins       | ~30   |
| F10 `--- DebugConsole ---` sidecar block                            | ~40   |

Total ~210 lines, lower-risk (mostly attribute decoration on existing classes). Real payoff: type `weather.wind-speed 8` and see the storm pick up live.

Per Bryan's preference, `Test<System>Commands.cs` files for these subsystems get added organically when the first test for each system is needed — not upfront.
