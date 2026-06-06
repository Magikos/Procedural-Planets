# 2026-06-04 — Debug Console Slice CONSOLE-4.5: Polish

**Status:** Shipped. Awaiting Bryan validation across cursor movement, async abandon/cancel split, confirm modal, animated dots, next-arg ghost, and the `test.console.enum` / `test.console.cancellable` test commands.

**Design doc:** [docs/design/2026-06-03-debug-console.md](../design/2026-06-03-debug-console.md) — out-of-scope polish triggered by Bryan's slice 4 validation feedback.

## Triggering feedback (recap)

From Bryan's slice 4 validation:

1. "Close console to abandon" warning is misleading — closing doesn't actually stop the awaitable
2. Arrow keys should move the cursor inside the input line
3. Error messages were inconsistent (some red with `[Error]` prefix, some without)
4. The `... ` prompt-cue prefix should animate while async is pending
5. Next required argument should show as ghost while typing args
6. Need an enum test command to exercise the enum-completion popup

Plus Bryan refined the abandon/cancel split:

- `console.abandon` — UI gives up tracking, awaitable runs to completion in background (no confirm)
- `console.cancel` — true cancellation via `CancellationTokenSource`, Y/N modal confirm

## Files

**Modified:**

- [`Registry/CommandData.cs`](../../Assets/Scripts/Core/Console/Registry/CommandData.cs) — added `HasCancellationToken` field
- [`Registry/ConsoleRegistry.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleRegistry.cs) — `Build` detects trailing `CancellationToken` parameter; hides it from user-typed `ParameterData[]`
- [`Registry/CommandExecutor.cs`](../../Assets/Scripts/Core/Console/Registry/CommandExecutor.cs) — `Execute` takes optional `CancellationToken`; injects it as the last argument when `HasCancellationToken`
- [`ConsoleInputBuffer.cs`](../../Assets/Scripts/Core/Console/ConsoleInputBuffer.cs) — cursor state + `Insert` / `Delete` / `MoveLeft/Right/Home/End` / `Set` (replace whole content + cursor to end)
- [`Core/Interfaces/IInputMapService.cs`](../../Assets/Scripts/Core/Interfaces/IInputMapService.cs) — 5 new actions: `ConsoleCursorLeft/Right/Home/End` + `ConsoleDelete`
- [`Core/Services/InputMapService.cs`](../../Assets/Scripts/Core/Services/InputMapService.cs) — bindings for the above
- [`IConsoleService.cs`](../../Assets/Scripts/Core/Console/IConsoleService.cs) — `AbandonPending()` + `RequestCancelPending()` interface methods
- [`ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — large update (see below)
- [`ConsoleRenderer.cs`](../../Assets/Scripts/Core/Console/ConsoleRenderer.cs) — `ConfirmRenderData` struct + `DrawConfirmModal`; confirm replaces suggestions popup when active
- [`Commands/ConsoleBuiltins.cs`](../../Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs) — added `console.abandon` and `console.cancel`
- [`Commands/TestConsoleCommands.cs`](../../Assets/Scripts/Core/Console/Commands/TestConsoleCommands.cs) — added `TestEnumOption` (Greek letters) + `test.console.enum` + `test.console.cancellable`

## Async lifecycle redesign

Four-state truth table for the pending async:

| State           | `_pending` | `_pendingCts` | UI                                    |
| --------------- | ---------- | ------------- | ------------------------------------- |
| Idle            | null       | null          | `> _`                                 |
| Running         | set        | set           | spinner line + animated `... > ` cue  |
| Abandoned       | null       | (orphaned)    | spinner line replaced with `abandoned`; awaitable runs invisibly until natural completion |
| Cancel requested | set       | Cancel'd      | confirm modal closes; observer sees `OperationCanceledException`; line replaced with `cancelled` |

### Key implementation details

**CTS ownership:** every `SubmitInputLine` allocates a `CancellationTokenSource`. If the command turns out sync, the CTS is disposed in the `finally` block. If async, ownership transfers to the `ObservePending` closure which disposes it when the awaitable finally completes — even after abandon (the observer can still run in background after `_pending` is cleared).

**Abandon semantics:** `_pending = null; _pendingCts = null;` immediately. The observer's continuation eventually fires, detects that `_pending` no longer holds its `LineId` (or is null), and skips the scrollback update. The CTS is still disposed via the captured `cts` closure variable. Awaitable runs to completion silently.

**Cancel semantics:** the modal's `OnYes` callback invokes `_pendingCts.Cancel()`. Awaitables that accept a `CancellationToken` parameter cooperatively `ct.ThrowIfCancellationRequested()` and bail with `OperationCanceledException`. The observer catches OCE and replaces the line with a `cancelled` message in amber (Warning color, not Error). Commands without a `CancellationToken` parameter cannot be cancelled — `console.cancel` will trigger the modal but `Cancel()` will have no effect on the awaitable (it'll just keep running until natural completion, and the `cancelled = false` observer path will run normally).

**`[Error]` prefix dropped:** the observer used to write `"[Error] {alias}: {message}"`; now it writes `"{alias}: {message}"`. The `ConsoleMessageType.Error` already colors it red — the prefix was redundant.

## Cursor implementation

`ConsoleInputBuffer` carries an `int _cursor` (0 to `Length`). All editing operations operate at the cursor:

- `Insert(char)` / `Insert(string)` — insert at cursor, advance
- `Backspace()` — delete glyph before cursor, cursor moves left
- `Delete()` — delete glyph at cursor, cursor stays
- `MoveLeft/Right/Home/End()` — clamp + move
- `Set(string)` — replace entire content, cursor to end (used by `AcceptSuggestion`, `HistoryPrevious`, etc.)
- `Clear()` — empty buffer, cursor to 0

Renderer uses `InsertCursorIntoSpans` to inject the cursor `|` span at the right offset within the syntax-highlighted typed spans. If the cursor falls mid-span, the span is split into before/after parts and the cursor is inserted between. Mid-quote cursor placement may produce slightly funky syntax coloring (a quoted string crossing the cursor splits into two `StrColor` halves with a cursor in the middle) — acceptable edge case.

## Confirmation modal

Single-line modal anchored where the suggestions popup would be. Layout:

```
Cancel 'test.console.cancellable'?   [ ] Yes   [*] No
```

The active option (`[*]`) is colored cyan; inactive (`[ ]`) is dim gray. Mode toggle via Tab, Left, or Right arrow. Enter accepts the active choice. Esc = No (cancel the cancel). Backtick = No + close console.

Default active is **No** — protects against an accidental Enter wiping out a long-running command.

While modal is up:
- Text input handler returns early (no typing into the prompt)
- All "normal" key handlers skipped
- Suggestions popup hidden (modal is exclusive)

Infrastructure is reusable — `ShowConfirm(question, onYes, onNo)` is a private helper, easy to wire into future destructive commands (e.g., `planet.regenerate` or `quit`).

## Next-arg ghost

Generalized the existing param hint logic that previously only fired when the typed text was *exactly* a command name. Now:

1. Tokenize the typed text
2. If `tokens[0]` is a known command and has parameters → compute next slot:
   - With trailing space → slot is `tokens.Count - 1` (about to start that arg)
   - With `tokens.Count == 1` and no trailing space (just alias) → slot 0
   - Else (mid-typing) → `tokens.Count - 1` (the slot AFTER the one being edited)
3. Show that slot's param name as `<name>` (required, orange) or `[name]` (optional, blue)

Suppressed when the cursor is mid-line (only shown when cursor is at end) — mid-line hints would visually overlap with the after-cursor text.

## Animated `... ` prompt cue

Uses the same dot-phase computation as the spinner (`SpinnerDotPeriod = 0.5s`, 4 phases). Same visual rhythm as the spinner line, so they feel related — when both are visible the dots animate in sync.

## Test commands added

| Alias                                   | New | Description                                            |
| --------------------------------------- | --- | ------------------------------------------------------ |
| `test.console.cancellable [s=5]`        | ✅  | Sleeps N seconds with `ct.ThrowIfCancellationRequested()` polling. Exercises BOTH `console.abandon` AND `console.cancel`. |
| `test.console.enum <option>`            | ✅  | Takes `TestEnumOption` (`Alpha`..`Zeta`). Exercises the enum completion popup. |

## Truthful warning message

Old: `"... wait or close console to abandon"` (misleading — closing doesn't abandon)

New: `"'{alias}' running ({s}s) — wait, or run 'console.abandon' / 'console.cancel'"` — both options are real commands now.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- ~370 net lines added across 11 files (vs. estimated ~355 — bang on)

## Validation guidance

1. **Cursor movement:** type `echo hello world`, press Home → cursor jumps to start. Type `X` → `Xecho hello world`. End → cursor jumps to end. Left/Right arrows step one char. Delete removes char at cursor (vs Backspace before).
2. **Animated `...` prompt cue:** start `test.console.async 5`. Watch the `...` before the `>` cycle through `   ` → `.  ` → `.. ` → `...` in sync with the spinner.
3. **Next-arg ghost — alias only:** type `test.console.types` (no space). Ghost should show ` <i>`.
4. **Next-arg ghost — mid-args:** type `test.console.types 42 ` (with space). Ghost should show `<f>`.
5. **Next-arg ghost — typing arg:** type `test.console.types 42 3` (no trailing space). Ghost should show ` <b>` (the SLOT AFTER the one being typed).
6. **Error consistency:** run `test.console.error` (sync exception), then `test.console.async-fail 1`. Both should produce red lines WITHOUT a `[Error]` prefix — color alone signals the type.
7. **Abandon path:** `test.console.async 10` (or `cancellable 10`), then `console.abandon`. Spinner line should be replaced with `abandoned (X.Xs)` in amber. Background work continues silently — type `echo hi` to confirm input still works.
8. **Cancel path (with cancellable command):** `test.console.cancellable 10`, then `console.cancel`. Modal appears: `Cancel 'test.console.cancellable'?   [ ] Yes   [*] No`. Tab to toggle Yes; Enter → modal dismisses, spinner line becomes `cancelled (X.Xs)` in amber. Try again with Esc → modal dismisses, command keeps running.
9. **Cancel against uncancellable command:** `test.console.async 10`, then `console.cancel`. Modal appears. Confirm Yes — modal dismisses but command keeps running (no `CancellationToken` parameter to honor). Eventually completes normally.
10. **Enum popup:** type `test.console.enum ` (with space). Popup shows `Alpha / Beta / Gamma / Delta / Epsilon / Zeta`. Up/Down arrows navigate, Tab or Enter accept. Type `B` after the space — popup filters to `Beta`. Tab accepts.
11. **Modal cancels on close:** open modal via `console.cancel`, then press backtick. Console closes; modal is dismissed with No (no cancellation occurs). Reopen — back to normal state.

## What's next

**CONSOLE-5** is unblocked. With abandon/cancel + cursor + confirm modal infra in place, the per-module commands can be added cleanly:

- `weather.wind-speed`, `weather.wind-direction`, `weather.state` (the latter benefits from enum completion already proven)
- `time.sun-elevation`, `time.freeze`
- `grass.density-mult`, `grass.show-coverage`
- `planet.regenerate` (async, cancellable) + `planet.seed` (could use the confirm modal: "Regenerate planet? Are you sure?")
- `debug.capture <set>` using the close/capture/reopen pattern
- `quit`, `console.anchor`, `console.scrollback-size`
- F10 `--- DebugConsole ---` sidecar block

Test-command files (`TestWeatherCommands.cs`, `TestGrassCommands.cs`) colocated with their systems as they're built.
