# 2026-06-04 — Debug Console Slice CONSOLE-5.8: Enter Accepts Any Visible Suggestion

**Status:** Shipped. Single-rule simplification of Enter behavior triggered by Bryan's slice 5.7 feedback.

## The rule (Bryan's model)

> "If a suggestion is showing, the user clearly wanted it — Enter should accept. If it's not in the list, it's not a valid command."

Translation:

| State | Enter |
| ----- | ----- |
| No suggestions (engine returned nothing) | **Submit** (gets `unknown command` if invalid) |
| Active suggestion's completion **equals** typed text | **Submit** (typed is already complete — no work to do) |
| Active suggestion's completion **differs** from typed text | **Accept** |

Tab unchanged — always accepts.

This collapses the previous "ghost mode submits raw / popup mode accepts" inconsistency into one predicate: **if any suggestion exists and isn't already what you typed, Enter accepts it.**

## Files

**Modified (1):**

- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — replaced inline Enter logic with `HandleSubmitKey()`

## Implementation

```csharp
void HandleSubmitKey()
{
    if (_suggestions.Count == 0)
    {
        SubmitInputLine();
        return;
    }

    int idx = Mathf.Clamp(_activeSuggestionIdx, 0, _suggestions.Count - 1);
    string completion = _suggestions[idx].CompletionText;
    if (completion.Equals(_inputBuffer.Text, StringComparison.OrdinalIgnoreCase))
        SubmitInputLine();
    else
        AcceptSuggestion();
}
```

Three branches, one predicate. `PopupVisible` is no longer read by the Enter handler — only by Escape (which still uses it to decide between "dismiss popup" and "clear input"). The renderer also still uses `ShouldShowGhost` directly. Three separate decisions that happen to coincide cleanly with this one.

## Behavior trace

| Typed | Suggestions | Active completion | Old Enter | New Enter |
| ----- | ----------- | ----------------- | --------- | --------- |
| `sun` | `[atmosphere.sun-intensity]` (substring) | differs | submit raw → "unknown command" (slice 5.7 fixed this case) | **accept** → `atmosphere.sun-intensity ` |
| `atm` | `[atmosphere.sun-intensity]` (prefix → ghost) | differs | submit raw → "unknown command" | **accept** → `atmosphere.sun-intensity ` (was: Tab only) |
| `clear` | `[clear]` (exact match) | equals | submit (correct) | **submit** (same) |
| `cle` | `[clear]` (prefix → ghost) | differs | submit raw → "unknown command" | **accept** → `clear ` |
| `bogus` | (empty) | n/a | submit → "unknown command" | **submit** → "unknown command" (same) |
| `test.console` | many | differs | accept active | **accept** active (same) |

The big behavior change: **ghost mode no longer submits raw on Enter**. Users get the completion instead. Tab still accepts in either mode (popup or ghost) — unchanged for muscle memory.

If you genuinely want to submit invalid text for testing, type something with zero matches (`zzz_invalid`) and press Enter — that path is preserved.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- 1 modified file, ~25 net lines

## Validation

1. **Substring match Enter:** type `sun` → popup shows `atmosphere.sun-intensity` → Enter → buffer becomes `atmosphere.sun-intensity ` (NOT "unknown command").
2. **Prefix match (ghost) Enter:** type `atm` → ghost preview shows the completion → Enter → buffer becomes `atmosphere.sun-intensity ` (this is the new behavior).
3. **Exact match Enter still submits:** type `clear` → suggestion is also `clear` → Enter submits, scrollback clears.
4. **Zero-match Enter still errors:** type `definitely_not_a_command` → no suggestions → Enter → "unknown command".
5. **Tab still works:** type `atm` → Tab → accepts (same as Enter now).
6. **Two-step flow works:** type `sun` → Enter → buffer = `atmosphere.sun-intensity ` → type `30` → Enter → runs the command. (Bryan's flow from slice 5.6.)

## What's next

Back to Phase 2b — precipitation + lightning subsystem batch.
