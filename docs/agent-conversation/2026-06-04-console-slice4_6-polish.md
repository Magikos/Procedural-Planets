# 2026-06-04 — Debug Console Slice CONSOLE-4.6: Polish (round 2)

**Status:** Shipped. Awaiting Bryan validation across the cancel-bypass bug fix, generalized key repeat, ghost-vs-popup decision, hint format, and the paginated suggestion popup.

**Triggering feedback:** Bryan's slice 4.5 validation surfaced five distinct issues — see the comments inline below for each one.

## Files

**Modified (3):**

- [`ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — `KeyRepeat` helper class, `IsBypassPendingCommand` check, `ShouldShowGhost` predicate, rewritten param-slot hint logic, popup scroll offset tracking
- [`ConsoleRenderer.cs`](../../Assets/Scripts/Core/Console/ConsoleRenderer.cs) — paginated suggestions popup with mini scroll indicator
- [`IntellisenseEngine.cs`](../../Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs) — raised `MaxSuggestions` cap to 200 (pagination handles long lists now)

No new files. ~150 net lines.

## Fix 1 — `console.cancel` rejected while pending (the real bug)

`SubmitInputLine` rejected every submit when `_pending.HasValue`, including the very commands meant to handle pending state. Bryan tried `console.cancel` three times against `test.console.async 60` and got the warning each time.

Fix: peek at the first token before the pending check. If it's `console.abandon` or `console.cancel`, let it through.

```csharp
static bool IsBypassPendingCommand(string line) { ... }

if (_pending.HasValue && !IsBypassPendingCommand(line))
{
    // warning + return
}
```

These two commands are special by design — they exist precisely because the user wants out of the pending state. Adding new bypass commands later means extending this single function; no general attribute infrastructure needed (YAGNI).

## Fix 2 — Generalize key repeat

The slice 4.5 Backspace had inline initial-delay-then-repeat logic; Delete and the arrows didn't have it. Bryan wanted holding Delete (and other keys) to repeat like Backspace does.

Solution: a nested `KeyRepeat` helper class with `Update(pressed, held) → ticks-this-frame`. Each frame the controller calls `Update(...)` and runs the action that many times:

```csharp
int delTicks = _delRepeat.Update(
    _input.ConsoleDelete.WasPerformedThisFrame(),
    _input.ConsoleDelete.IsPressed());
for (int i = 0; i < delTicks; i++) { _inputBuffer.Delete(); OnInputMutated(); }
```

Applied to: Backspace, Delete, Left, Right, Up, Down. Six trackers in the controller, each owned by its key.

Home and End don't get repeat (they're discrete jumps; holding doesn't do anything sensible). PageUp/PageDown also don't get repeat (the existing 5-line step already moves fast enough).

Initial delay 0.4s, repeat interval 0.05s (≈20Hz) — same as the previous Backspace numbers.

## Fix 3 — Ghost-vs-popup decision (the `enum` case)

Slice 4.5 had ghost completion fire for ANY single-match suggestion, regardless of whether the completion was a prefix of what's typed. The substring-only path (`enum` → `test.console.enum`) hit a dead branch: `completion.StartsWith(typed)` returned false, no ghost appeared, AND the popup was suppressed because `_suggestions.Count == 1`. Net result: no visual indication that the system was suggesting anything.

Fix: factored out `ShouldShowGhost(typed)` predicate that requires BOTH single-match AND prefix-match. Used to decide popup-vs-ghost in `OnEndCameraRendering` AND inside `BuildInputSpans` (consistent logic in one place).

```csharp
bool ShouldShowGhost(string typed)
{
    if (_suggestions.Count != 1) return false;
    if (_suggestionsFrozen || _suggestionsSuppressed) return false;
    string completion = _suggestions[0].CompletionText;
    return completion.Length > typed.Length
        && completion.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
}
```

Now:
- `test.cons` + single match `test.console.enum` (prefix match) → ghost
- `enum` + single match `test.console.enum` (substring not prefix) → popup
- `ec` + multiple matches → popup (unchanged)

## Fix 4 — Hint format: `<type: name>`, show all remaining slots

Old behavior: only the next slot was shown, as `<i>` / `[i]` (just the name).

New behavior matches Bryan's progression example exactly:

```
type <int: i> <float: f> <bool: b> <Vector3: v> <Color: c>
type 5 <float: f> <bool: b> <Vector3: v> <Color: c>
type 5 12.3 <bool: b> <Vector3: v> <Color: c>
type 5 12.3 tru <Vector3: v> <Color: c>
```

Type names use C# keyword aliases for primitives (`int`, `float`, `double`, `bool`, `string`) via a tiny `FormatTypeName` static helper. Falls back to `Type.Name` for anything else (`Vector3`, `Color`, enum types).

Slot-from-which-to-show:
- Alias-only typed (no args yet) → start at slot 0
- Mid-typing slot N (no trailing space) → start at slot N+1 (the slot AFTER the in-progress one)
- Trailing space, N args completed → start at slot N (next slot about to begin)

Unified formula: `showFrom = max(0, tokens.Count - 1)` — works for all three cases.

Required slots are colored orange (`HintReqdColor`), optional slots blue (`HintOptColor`). Same colors as before, just applied per-slot now.

## Fix 5 — Paginated suggestion popup with centered active row

Old: popup grew to N rows tall, no upper bound, no scroll. Bryan asked for fixed-size (8 rows) with active row centered as it moves.

Implementation:

- **`IntellisenseEngine.MaxSuggestions` raised to 200** — pagination handles long lists; the cap is now mostly a sanity guard against pathological inputs (e.g. a 1000-entry enum)
- **Controller tracks `_popupScrollOffset`** — recomputed every time `_activeSuggestionIdx` changes or `_suggestions` regenerate
- **Centering math:** with `halfWindow = 4` and `visible = 8`:
  - `active < halfWindow` → `scrollOffset = 0`
  - `active >= total - (visible - halfWindow)` → `scrollOffset = total - visible`
  - else → `scrollOffset = active - halfWindow`
- **Renderer renders only `[scrollOffset, scrollOffset + visibleCount)`** — popup height is fixed at 8 rows × lineH (or fewer if `suggestions.Count < 8`)
- **Mini scroll indicator** appears at the right edge of the popup when `total > visible`. Proportional thumb. Reuses the existing scrollbar materials.

UX as Bryan specified:
- Arrow Down from top-of-list moves active down without scrolling, until active hits row 4 (the center)
- Beyond that, the list scrolls as active stays centered
- At the bottom, active sticks at row 7 (bottom of visible window); scrollOffset stops growing
- Round-robin wrap (modulo arithmetic in `_activeSuggestionIdx`): from row N-1, Down goes to row 0; `UpdatePopupScroll` recomputes offset to 0

The scroll indicator visually confirms there's more to scroll. As active moves from top to bottom, the thumb travels with it.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- 3 files modified, ~150 net lines

## Validation guidance

1. **`console.cancel` works now:** `test.console.cancellable 60`, then `console.cancel` → modal appears, Tab to Yes, Enter → spinner replaced with `cancelled (X.Xs)`. The submit-warning lines should no longer appear when typing `console.cancel`.
2. **`console.abandon` works now too:** `test.console.async 60`, then `console.abandon` → spinner replaced with `abandoned (X.Xs)`.
3. **Key repeat on Delete:** type a long line, position cursor at start (Home), hold Delete → characters delete left-to-right at ~20Hz with a 0.4s initial pause.
4. **Key repeat on arrows:** hold Left → cursor walks back through the line. Same for Right, Up, Down (history nav / popup nav).
5. **`enum` popup:** type `enum` (just the four letters, no prefix). A popup should appear showing `test.console.enum` (substring match, no longer a dead branch). Tab to accept.
6. **`test.cons` ghost:** type `test.cons`. The remainder (`ole.enum` etc.) should appear inline as gray ghost (prefix match, single result). Tab accepts.
7. **Type hint format:** type `test.console.types` (no space). Ghost shows ` <int: i> <float: f> <bool: b> <Vector3: v> <Color: c>` — all 5 slots, with type:name format.
8. **Type hint progression:** type `test.console.types 5`. Ghost shows ` <float: f> <bool: b> <Vector3: v> <Color: c>` (slot 0 omitted; you're typing it). Add ` 12.3` → ghost shrinks to `<bool: b> <Vector3: v> <Color: c>`. Etc.
9. **Popup pagination — Greek enum:** `test.console.enum ` (space). The 6-entry Greek enum fits without paging. Now imagine a 20-entry enum: type a partial that matches many — popup caps at 8 visible with scroll indicator at the right edge.
10. **Popup centering:** with a long list, arrow Down repeatedly. Active stays at top until it reaches row 4, then the list scrolls as active stays centered. At end, active reaches row 7 (bottom). Wrap-around Up from row 0 jumps to last row.

## What's next

CONSOLE-5 is still next:

- Per-module commands (`weather.*`, `time.*`, `grass.*`, `planet.*`)
- `debug.capture <set>` via close/capture/reopen pattern
- `quit`, `console.anchor`, `console.scrollback-size` built-ins
- F10 `--- DebugConsole ---` sidecar block

The infra is solid now — cancel/abandon, cursor, modal confirm, paginated popup, formatted hints. Adding real game-affecting commands should be mostly attribute decoration on existing classes.
