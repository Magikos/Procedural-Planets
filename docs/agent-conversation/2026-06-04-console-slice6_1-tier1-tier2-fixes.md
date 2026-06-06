# 2026-06-04 — Debug Console Slice CONSOLE-6.1: Tier 1 + Tier 2 fixes

**Status:** Shipped. Build clean (2 pre-existing CS0162 warnings only). Addresses 8 of the 38 audit findings from [the CONSOLE-6 audit doc](2026-06-04-console-slice6-audit.md).

## Findings closed this slice

| # | What | Files |
| - | ---- | ----- |
| F1 + (partial) F15 | Extracted `CompletionRanker.Rank(IEnumerable<string>, string partial)` helper; collapsed 5 providers from ~20 lines each to ~2 lines each. `IntellisenseEngine.SuggestAliases` left as-is (its per-item matchStart tracking shape is different enough that forcing it through the helper would have been more code, not less). | `CompletionRanker.cs` (new), 5 provider files |
| F14 + F20 | Extracted `ParameterData.FormatCommandSignature(cmd, includeDefaults)`; replaced 3 separate impls. **Side effect: `help` no longer prints `Nullable\`1` for nullable parameters** — it now uses `DisplayTypeName` like the other surfaces. | `ParameterData.cs`, `CommandExecutor.cs`, `IntellisenseEngine.cs`, `ConsoleBuiltins.cs` |
| F13 | Extracted `ConsoleColors.Named` shared dict. ColorParser and ColorTagParser now share one source of truth (orange now resolves in both, previously only in markup). | `ConsoleColors.cs` (new), `ConsoleArgumentParsers.cs`, `ConsoleColorTagParser.cs` |
| F22 | `action.undo` / `action.redo` now throw `InvalidOperationException` if `IWorldActionManager` is missing — consistent with sibling `history`/`clear` that already printed an error. | `ActionCommands.cs` |
| F3 | `EnumCompletionProvider` now caches `Enum.GetNames` once in the ctor instead of re-calling per keystroke. | `EnumCompletionProvider.cs` |

## Findings resolved by verification (no code change)

| # | What | Verification |
| - | ---- | ------------ |
| F31 | Doc said design intent was "backtick toggles," but the controller has separate `OpenConsole` and `CloseConsole` actions checked against `_isOpen` — looked like a divergence. **Verified in `InputMapService`:** both actions bind to `<Keyboard>/backquote`, so toggle works correctly. Implementation is functionally identical to a single toggle action; the two-action shape is just a side-effect of action-map design. **Not a bug.** |

## Net diff

- **New files (3):** `CompletionRanker.cs`, `ConsoleColors.cs`, this slice log
- **Modified (9):** 5 providers, `ParameterData.cs`, `CommandExecutor.cs`, `IntellisenseEngine.cs`, `ConsoleBuiltins.cs`, `ConsoleArgumentParsers.cs`, `ConsoleColorTagParser.cs`, `ActionCommands.cs`, `ProceduralPlanets.Core.csproj` (added 2 Compile entries for new files)
- **Net lines:** roughly −110 (more deletion than addition; ~20-line provider blocks collapsed to ~3 lines each)

## Build verification

```
dotnet build ProceduralPlanets.Core.csproj
ProceduralPlanets.Core -> Temp\bin\Debug\ProceduralPlanets.Core.dll
Build succeeded.
    2 Warning(s)  (pre-existing CS0162 on DebugCaptureController)
    0 Error(s)
```

## Validation guidance

1. **`help camera.position`** (any command with a nullable parameter) → should print `int?` (or `float?`, etc.) instead of `Nullable\`1`. Bug fix.
2. **`echo <color=orange>hello</color>`** — orange now works in BOTH markup AND as a Color-typed argument (the shared dict fix). Previously only worked in markup.
3. **`debug.mode `** (with space) → enum-style popup still appears. Substring + prefix ranking still works.
4. **`action.undo` when no IWorldActionManager registered** → red error message instead of silent no-op.
5. **All existing test commands** (`test.console.colors`, `test.console.async-cancellable`, etc.) → unchanged behavior.

## What's deferred / still open

Still on the audit doc waiting for Bryan's direction:

- **Tier 2 leftovers** (F4 / F30 / F37 / F36 / Renderer cleanups): low-priority "fragile in theory, correct in practice." Could ship as a small CONSOLE-6.2 if Bryan wants.
- **Tier 3 (design infrastructure):**
  - **`ConsoleTheme` ScriptableObject** (F12 + F27 + F37): factor out ~30 colors. ~1-2 hours.
  - **`[DebugOnly]` attribute** infrastructure: tag debug commands, gate `ConsoleRegistry.Scan`, verify release-build strip. Needs Bryan input on the open question (do `console.*` commands count as "debug only"?).
  - **`ConsoleRenderState` struct + scrollbar/frame helpers** (F24 + F25 + F26): cosmetic renderer cleanup.
- **Tier 4: design-doc refresh** — `docs/design/2026-06-03-debug-console.md` has 11 documented divergences from current code (D1-D11). Either patch in place or replace with an "as-shipped" doc.
- **Tier 5 (deferred):** F16 (asm filter), F17 (capped-list indicator), F18 (Vector expr eval), D7 (PrintEscaped), D8 (timestamps + wrap), F36 (input-span caching).

## Recommended next

1. **Bryan validates** the changes via Unity recompile + a few of the test commands above.
2. If clean, **scope CONSOLE-6.2** to bundle the Renderer cosmetic work (Tier 3 item 3) + remaining low-priority stuff. Defer ConsoleTheme + DebugOnly to their own slices since each has design decisions.
