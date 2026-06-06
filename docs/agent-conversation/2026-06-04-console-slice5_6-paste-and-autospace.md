# 2026-06-04 — Debug Console Slice CONSOLE-5.6: Ctrl+V Paste + Auto-Space After Accept

**Status:** Shipped. Two tiny UX polish items from Bryan's slice 5.5 feedback.

## Files

**Modified (1):**

- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs)

## Fix 1 — Ctrl+V paste

Polled directly in `UpdateNormalMode` (no new Input Action — the modifier+key combo is simpler to detect via `Keyboard.current.ctrlKey.isPressed && Keyboard.current.vKey.wasPressedThisFrame`).

Clipboard text comes from `GUIUtility.systemCopyBuffer` (Unity's cross-platform clipboard API). Control characters are filtered out — newlines / tabs / DEL get stripped so multi-line clipboard content collapses to a single line of printable text. The cleaned string is inserted at the cursor in one operation, then `OnInputMutated()` resets suggestion state.

```csharp
void PasteClipboard()
{
    string clipboard = GUIUtility.systemCopyBuffer;
    if (string.IsNullOrEmpty(clipboard)) return;

    var sb = new StringBuilder(clipboard.Length);
    foreach (char c in clipboard)
        if (c >= 0x20 && c != 0x7F) sb.Append(c);
    if (sb.Length == 0) return;

    _inputBuffer.Insert(sb.ToString());
    OnInputMutated();
}
```

Did NOT add Ctrl+C / Ctrl+X / Ctrl+A — kept scope to exactly what Bryan asked for. Easy to add as a symmetric set later if needed.

## Fix 2 — auto-space after suggestion accept

Single-character change in `AcceptSuggestion`:

```csharp
_inputBuffer.Set(_suggestions[_activeSuggestionIdx].CompletionText + " ");
//                                                                ^^^ new
```

Now Bryan's flow works as expected:

1. Type `sun-i`
2. Popup shows `atmosphere.sun-intensity`
3. Enter (or Tab) accepts → buffer becomes `atmosphere.sun-intensity ` (with trailing space)
4. Type `30`
5. Enter submits → runs `atmosphere.sun-intensity 30`

Trailing space is safe for zero-arg commands too — the submit-line tokenizer ignores whitespace, and the `IsNullOrWhiteSpace` early-out doesn't fire because there's still alias text.

## Categorization tracker (unchanged)

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- ~25 net lines

## Validation

1. **Ctrl+V from external source:** Copy "cloud.density 0.05" from outside Unity. Open console. Ctrl+V. Should appear in input. Enter to submit.
2. **Ctrl+V from messy source:** Copy "  hello\nworld\t!  " (with newlines/tabs). Ctrl+V → should appear as "  helloworld!  " (control chars stripped, spaces preserved).
3. **Auto-space:** Type `sun-i`. Popup. Tab (or Enter). Buffer → `atmosphere.sun-intensity `. Type `30`. Enter. Command runs cleanly.
4. **Auto-space + zero-arg:** Type `clea` (or whatever partial matches `clear`). Tab. Buffer → `clear `. Enter. Submits as `clear` — trailing space doesn't break.

## What's next

Phase 2b continues with precipitation + lightning (then grass, action, planet, debug.module/mode/capture).
