# 2026-06-04 — Debug Console Slice CONSOLE-5.7: Popup/Enter Sync + Type Display

**Status:** Shipped. Two coupled fixes from Bryan's slice 5.6 feedback.

## Files

**Modified (4):**

- [`Console/Registry/ParameterData.cs`](../../Assets/Scripts/Core/Console/Registry/ParameterData.cs) — new `DisplayTypeName` property + static `FormatTypeName(Type)` helper
- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — `PopupVisible` aligned with renderer; local `FormatTypeName` removed
- [`Console/Intellisense/IntellisenseEngine.cs`](../../Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs) — signature formatter uses helper
- [`Console/Registry/CommandExecutor.cs`](../../Assets/Scripts/Core/Console/Registry/CommandExecutor.cs) — usage string uses helper

## Fix 1 — Enter-vs-popup sync

The bug from Bryan's screenshot: typing `sun` shows the popup with `atmosphere.sun-intensity` (substring match, single result), but pressing Enter submitted raw `sun` instead of accepting the popup.

Root cause: two predicates disagreed about whether the popup was "visible":

| Caller | Old logic | Behavior for single substring match |
| ------ | --------- | ------------------------------------ |
| Renderer (`popupSuggestions` in `OnEndCameraRendering`) | `!ShouldShowGhost(typed)` — show popup whenever ghost isn't taking over | **Shows popup** ✓ |
| Enter handler (`PopupVisible` property) | `count > 1 \|\| (count == 1 && _suggestionsFrozen)` | **Returns false** — submits raw text ✗ |

`sun` is a substring (not prefix) of `atmosphere.sun-intensity`, so `ShouldShowGhost` correctly returned false (single match but completion doesn't start with what's typed → no ghost). The renderer drew the popup. The Enter handler thought there was no popup.

Fix is a single-line predicate change:

```csharp
bool PopupVisible => _suggestions.Count > 0 && !ShouldShowGhost(_inputBuffer.Text);
```

Same condition the renderer uses. Now Enter on `sun` accepts `atmosphere.sun-intensity ` (with the slice 5.6 auto-space).

This also implicitly preserves the existing ghost-mode behavior:

- Ghost showing (single prefix match): `ShouldShowGhost = true` → `PopupVisible = false` → Enter submits typed text (Tab accepts ghost — unchanged)
- Multi-match popup: `count > 1` → `ShouldShowGhost = false` → `PopupVisible = true` → Enter accepts active row (unchanged)
- Single substring match popup: `ShouldShowGhost = false` → `PopupVisible = true` → Enter accepts (**now fixed**)

## Fix 2 — `Nullable\`1` → `float?` in signatures and hints

Bryan's screenshot showed `[value:Nullable\`1]` because `Type.Name` returns the CLR generic name. The controller had a private `FormatTypeName` for inline ghost hints, but the engine's popup signatures and the executor's usage strings used `Type.Name` directly. Inconsistent.

Consolidated into one static method on `ParameterData`:

```csharp
public string DisplayTypeName => FormatTypeName(Type);

public static string FormatTypeName(Type t)
{
    if (t == null) return "?";
    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
        return FormatTypeName(t.GetGenericArguments()[0]) + "?";
    if (t == typeof(int)) return "int";
    if (t == typeof(float)) return "float";
    if (t == typeof(double)) return "double";
    if (t == typeof(bool)) return "bool";
    if (t == typeof(string)) return "string";
    return t.Name;
}
```

Now everywhere displays:

- `Nullable<float>` → `float?`
- `Nullable<int>` → `int?`
- `Vector3` → `Vector3` (no mapping, falls through to `t.Name`)
- `TestEnumOption` → `TestEnumOption` (same)

Also normalized signature format across the three call sites — all now use `<type: name>` / `[type: name=default]`, matching the inline next-arg ghost format Bryan asked for in slice 4.6:

| Before                       | After                          |
| ---------------------------- | ------------------------------ |
| `[value:Nullable\`1]`        | `[float?: value]`              |
| `<seed:Int32>`               | `<int: seed>`                  |
| `[count:Int32=200]`          | `[int: count=200]`             |

## Categorization tracker (unchanged)

Same as 5.6.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (pre-existing `CS0162` warning only)
- 4 modified files, ~30 net lines

## Validation

1. **The bug:** Type `sun`. Popup shows `atmosphere.sun-intensity [float?: value]`. Press Enter. Buffer should become `atmosphere.sun-intensity ` (NOT submit raw `sun`).
2. **Multi-match still works:** Type `test.console`. Popup shows multiple. Arrow-pick. Enter → accepts.
3. **Ghost still submits raw:** Type `atm`. Ghost completion of `osphere.sun-intensity` appears inline. Enter → submits `atm` → "unknown command" (Tab is how you accept ghost — unchanged from slice 4.6).
4. **Type display clean:** Popup signatures show `float?`, `int?`, `Vector3` etc. — not `Nullable\`1` or `Int32`. Same for `help <command>` output and bind-error usage strings.

## What's next

Back to Phase 2b — `precipitation + lightning` is the next subsystem batch (deferred from slice 5.6).
