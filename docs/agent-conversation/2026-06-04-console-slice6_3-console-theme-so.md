# 2026-06-04 — Debug Console Slice CONSOLE-6.3: ConsoleTheme ScriptableObject

**Status:** Shipped. Build clean. Tier 3 item 1 from [the CONSOLE-6 audit](2026-06-04-console-slice6-audit.md). Closes F12, F27, F37.

## What

All ~30 color/styling constants previously hardcoded across `ConsoleRenderer.cs` and `ConsoleController.cs` now live on a single `ConsoleTheme` ScriptableObject. Bryan can right-click in the Project window → *Create → ProceduralPlanets → Console → Theme*, save as `Assets/Resources/ConsoleTheme.asset`, and tune colors live in the inspector without recompiling.

## Files

**New (1):**
- [`Console/ConsoleTheme.cs`](../../Assets/Scripts/Core/Console/ConsoleTheme.cs) — SO with grouped headers for Backdrop/Border, Scrollback messages, Scrollbar, Suggestion popup, Confirm modal, Input-line syntax. Static `CreateDefault()` factory returns an in-memory instance with values matching the prior hardcoded defaults.

**Modified (3):**
- [`Console/ConsoleRenderer.cs`](../../Assets/Scripts/Core/Console/ConsoleRenderer.cs) — Removed 19 `static readonly Color` fields + the four public `BackdropColor`/`BorderColor`/`BorderThickness`/`ScanlineStrength` properties. Added `public ConsoleTheme Theme { get; private set; }`. Constructor: `Theme = Resources.Load<ConsoleTheme>("ConsoleTheme") ?? ConsoleTheme.CreateDefault();`. All inline color refs now go via `Theme.X`.
- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — Removed 7 static color fields. `AppendTypedSpans` and `AppendSyntaxSpans` flipped from `static` to instance methods so they can read `_renderer.Theme`. `BuildInputSpans` reads theme locally once per call. `InsertCursorIntoSpans` stays static (cursor color is passed in).
- `ProceduralPlanets.Core.csproj` — `<Compile Include>` entry for the new file.

## Out-of-box behavior

Identical to before — the default values on the SO match the previously-hardcoded values. **No asset file needs to exist for the console to look the same as it did yesterday.** The asset is purely an opt-in customization hook.

## To customize

1. In Project window, right-click → *Create → ProceduralPlanets → Console → Theme*.
2. Save as `Assets/Resources/ConsoleTheme.asset` (the filename + Resources/ location matters — that's what `Resources.Load<ConsoleTheme>("ConsoleTheme")` resolves).
3. Open the asset in the Inspector. Tweak colors. Restart Play mode (theme loaded once at renderer construction).

If the asset is deleted later, the renderer transparently falls back to `CreateDefault()`.

## Why this isn't hot-reload

`Theme` is loaded once in `ConsoleRenderer`'s constructor. To pick up edits during Play, restart Play mode. We could add a "Reload Theme" command later if it becomes annoying — for now, the assumption is theme tweaks are infrequent.

## Build verification

```
dotnet build ProceduralPlanets.Core.csproj
Build succeeded. 2 Warning(s)  (pre-existing CS0162). 0 Error(s)
```

## Validation guidance

Pure refactor at the default-color level — no visible difference expected.

1. **Open console** (backtick) → backdrop, border, scrollback, popup, modal all look identical to before.
2. **Print all message types**: `test.console.colors` should show normal/warning/error/exception colors unchanged.
3. **Active suggestion**: type partial command, hit Tab a few times → cyan accent on match, white-on-blue active row look identical.
4. **Create the asset**: Project window → Create → ProceduralPlanets → Console → Theme → save as `Assets/Resources/ConsoleTheme.asset`. Open it. Inspector should show all ~30 grouped color fields with the same defaults.
5. **Customize**: change `Backdrop` to bright red, restart Play, open console → backdrop is now bright red. Confirms loading path.

## Tally

- **CONSOLE-6** (audit): 38 findings + 11 design-doc divergences
- **CONSOLE-6.1**: 8 findings + 1 verified non-bug
- **CONSOLE-6.2**: 3 findings (Renderer cosmetic)
- **CONSOLE-6.3**: 3 findings (F12, F27, F37 — ConsoleTheme SO)
- **14 of 38 findings closed.**

## What's left

Still on the audit doc waiting for direction:

- **`[DebugOnly]` attribute** (Tier 3 item 2) — needs Bryan's call on whether `console.*` / `help` / `quit` count as debug-only.
- **Design-doc refresh** (Tier 4, D1-D11) — patch in place or write fresh "as-shipped" doc?
- **Tier 5 deferrals** (F16, F17, F18, D7, D8, F36) — most are "fine as-is unless we want them."
- **Per-system command consistency audit** — would surface the last ~5-10 cross-controller findings (error-handling shape, null-target patterns).
