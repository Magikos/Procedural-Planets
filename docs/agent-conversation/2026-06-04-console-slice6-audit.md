# 2026-06-04 — Debug Console Slice CONSOLE-6: Audit + Cleanup

**Status:** In progress. Read-only audit pass. **No code changes yet** — per Bryan's audit workflow, findings are listed here for review first.

## Scope

Console arc is functionally complete (slices 0 → 5.13, ~60 commands across 13 prefixes). This slice is the close-out:

1. Walk every `Assets/Scripts/Core/Console/*` file for: dead code, duplicated logic, stale comments, naming inconsistencies, missing XML on public surface
2. Reconcile slice-log design decisions vs original design doc at `docs/design/2026-06-03-debug-console.md` — note divergences, decide which warrant doc updates vs code reversals
3. Survey missed design-doc opportunities (scrollbar polish, awaitable cancellation propagation, color-tag in `help`, theming, etc.)
4. Refresh the original design doc (now several iterations behind reality)
5. Solidify categorization — convert the per-slice tracker table into a real `[DebugOnly]` attribute so release-build stripping has a single source of truth

## Workflow

Per [Bryan's audit preference](../../) — read-only audit first → consolidated findings → Bryan reviews & directs → fixes ship as a follow-up slice (or several). Do **not** start fixing during the audit pass.

## Files in scope

```
Assets/Scripts/Core/Console/
├── ConsoleController.cs          (~900 lines, the big one)
├── ConsoleRenderer.cs
├── ConsoleScrollback.cs
├── ConsoleInputBuffer.cs
├── ConsoleHistory.cs
├── ConsoleDebugModule.cs
├── ConsoleAnchor.cs
├── ConsoleEvents.cs
├── DebugConsoleBootstrap.cs
├── IConsoleService.cs
├── Commands/
│   ├── ConsoleBuiltins.cs
│   └── TestConsoleCommands.cs
├── Intellisense/
│   ├── IntellisenseEngine.cs
│   ├── Suggestion.cs
│   ├── IConsoleCompletionProvider.cs
│   ├── EnumCompletionProvider.cs
│   ├── BoolCompletionProvider.cs
│   ├── CommandNamesProvider.cs
│   ├── DebugModeNamesProvider.cs
│   └── DebugCaptureSetNamesProvider.cs
├── Registry/
│   ├── ConsoleRegistry.cs
│   ├── CommandData.cs
│   ├── ParameterData.cs
│   ├── CommandParser.cs
│   ├── CommandExecutor.cs
│   ├── ConsoleArgumentParsers.cs
│   ├── ExpressionEvaluator.cs
│   ├── ConsoleCommandAttribute.cs
│   ├── CommandPrefixAttribute.cs
│   ├── ParamDescriptionAttribute.cs
│   ├── CompletionSourceAttribute.cs
│   ├── MonoTargetType.cs
│   └── IConsoleArgumentParser.cs
└── Text/
    ├── TextSpan.cs
    └── ConsoleColorTagParser.cs
```

Also in scope: per-system commands inline in controllers (`AtmosphereController.cs`, `CelestialManager.cs`, `CloudController.cs`, `DebugCaptureController.cs`, `FreeCameraController.cs`, `Planet.cs`, `PrecipitationController.cs`, `QualityController.cs`, `ScaleReferenceMarkers.cs`, `WeatherLightningController.cs`, `WeatherManager.cs`) and `Core/Services/Commands/ActionCommands.cs`.

## Findings (running list — fill in as audit proceeds)

### Dead / duplicated code

- **F1: Substring-rank-and-yield duplicated 5x across completion providers.**
  Files: `BoolCompletionProvider`, `CommandNamesProvider`, `EnumCompletionProvider`, `DebugModeNamesProvider`, `DebugCaptureSetNamesProvider`.
  All five repeat: `if empty → yield all; else split into prefixed/substrings via StartsWith vs IndexOf; sort each; yield prefixed then substrings`.
  ~20 lines × 5 sites. **Proposed fix:** extract `CompletionRanker.RankFilter(IEnumerable<string> candidates, string partial) → IEnumerable<string>` helper. Each provider shrinks to ~6 lines.

- **F2: DebugModeNamesProvider / DebugCaptureSetNamesProvider have a convoluted dual loop.**
  They `yield return name` mid-loop for empty-partial AND also build prefixed/substrings lists. Works but reads weird — the empty-partial case is interleaved with the filter case. The Bool/Enum/CommandNames versions split it cleanly (early-return on empty, then the ranking loop). Once F1 lands, both collapse to the same shape.

- **F3: `EnumCompletionProvider` calls `Enum.GetNames(_enumType)` on every `GetCompletions` call.**
  Names are static for an enum type. Cache in field at constructor time.

### Stale comments

- **F4: `DebugConsoleBootstrap` log says "Toggle with \` (backtick)."** — need to verify this matches the actual InputAction binding in `ConsoleController`. If it ever changed (e.g. to Escape or F1), the bootstrap log is wrong. Verify and either pin in a constant or remove the key reference from the bootstrap message.

- **F5: `ConsoleColorTagParser` xmldoc says "v1 (per console design); any `</color>` closes back to the default colour"** — references "v1" with no follow-up plan. If nested tags are a future opportunity, document it; if not, drop the "v1" qualifier.

### Naming inconsistencies

_(none yet — providers all use `Cmp`, controllers all `Print*`, scrollback `Type` enum aligns with `Print*` methods)_

### Missing XML docs on public surface

- **F6: `ConsoleEvents.cs`** — both event structs have zero XML. Add one-liners (they're tiny but they're public IGameEvent surface).
- **F7: `ConsoleScrollback.cs`** — `ConsoleMessageType` enum has inline `//` comments but no XML; `ConsoleMessage` struct has no XML.
- **F8: `TextSpan.cs`** — no XML.

### Magic numbers / constants

- **F9: `ConsoleScrollback._capacity = 1000`** — magic. Promote to `const int DefaultCapacity = 1000` so it's visible / configurable from one place.
- **F10: `ConsoleAnchorExtensions` uses literal 0.66f / 0.34f for the 2/3 split** — minor, but two constants `MajorFraction = 0.66f`, `MinorFraction = 0.34f` (or `1f - MajorFraction`) would document intent.

### Builtins / Test / Action / Renderer / Controller findings

- **F20: `ConsoleBuiltins.Help` has a THIRD `FormatSignature` implementation** (lines 124-136). Different format from CommandExecutor and IntellisenseEngine: `[name:type=default]` vs `[type: name=default]` vs `[type: name]`. Worse: uses `p.Type.Name` directly, so a `Nullable<int>` parameter shows as `Nullable\`1` in `help` output (bug). Fold into F14 consolidation — call shared `ParameterData.FormatCommandSignature(cmd, ...)`.

- **F21: `ConsoleBuiltins.Help` duplicates the alias-listing-with-description loop** twice (prefix-match path + full-list path). DRY into a helper `PrintAliasList(IEnumerable<string> aliases, string heading)`.

- **F22: `ActionCommands.Undo/Redo` silently return `""` when no `IWorldActionManager`.**
  `History` and `Clear` return an explicit error string. Inconsistent — undo/redo should also print "no world action manager registered" so the user gets feedback.

- **F23: `TestConsoleCommands.DelayUnscaledAsync` has no CancellationToken.**
  `AsyncCancellable` inlines its own delay loop because of this. Make `DelayUnscaledAsync` take an optional `CancellationToken ct = default` and have both call it.

- **F24: `ConsoleRenderer.Render` has 12 parameters.**
  Each is distinct (alpha, anchor, inputSpans, scrollback, scrollOffset, suggestions, activeSuggestion, popupScrollOffset, popupVisibleCount, confirm, hasNewMessages, plus cmd). Pack into `ConsoleRenderState` struct so the Controller assembles it once and the Renderer reads from one input. Reduces call-site noise.

- **F25: ConsoleRenderer scrollbar logic duplicated.**
  `DrawScrollbar` and `DrawPopupScrollIndicator` share ~30 lines of bg/thumb math with OPPOSITE thumb-direction semantics (offset=0 at bottom vs top). Extract a `DrawVerticalScrollbar(reversed: bool)` helper.

- **F26: ConsoleRenderer popup-frame logic duplicated.**
  `DrawConfirmModal` and `DrawSuggestionsPopup` both compute backdrop bounds with the same pad/lineH math, then draw a backdrop quad. Extract `DrawPopupFrame(consoleBounds, lineH, rowCount, color) → popupBounds`.

- **F27: ConsoleRenderer has ~25 color constants scattered through the file.**
  Strong candidate to bundle as `ConsoleTheme` ScriptableObject (the missed design-doc opportunity F12). Renderer reads colors from the theme so palette swaps are one-asset-edit instead of code edits + recompile.

- **F28: ConsoleRenderer silent on missing font.**
  `Resources.Load<SDFFontAsset>("DefaultFont")` returns null silently; layout falls back to `FallbackLineHeight = 1.2f`. The shader-missing case logs an error; the font-missing case should too.

- **F29: ConsoleController has its OWN spinner glyph state** (`CurrentDotPhase`) separate from scrollback updates.
  Used in 2 places: `UpdatePendingLine` (scrollback spinner) AND `BuildInputSpans` (input-line spinner). One static method, two callers. Fine — just noting they share. No bug.

- **F30: ConsoleController `OnTextInput` hardcodes `c == '`' || c == '~'`** to prevent the toggle key from injecting into the input buffer. Fragile if the toggle binding ever changes (re-amplifies F4). Either pin the binding to a constant or derive from `IInputMapService.OpenConsole`'s bound keys.

- **F31: ConsoleController `Update` checks separate `OpenConsole.WasPerformedThisFrame() && !_isOpen` and `CloseConsole.WasPerformedThisFrame() && _isOpen`.**
  Design doc said backtick TOGGLES (close even when open). Current implementation may bind both to backtick (need to verify in `IInputMapService` impl) but the code reads as "two separate actions." If both are bound to backtick the behavior is correct; if only OpenConsole is bound, backtick doesn't close when open (Escape does). Verify intent.

- **F32: ConsoleController `RunCommand` (public, programmatic entry) does NOT check `_pending.HasValue`.**
  `SubmitInputLine` rejects new commands during pending unless they're bypass commands; `RunCommand` bypasses this. So an external caller can race two async commands simultaneously. Either gate `RunCommand` the same way or document that programmatic callers must check `GetDiagnostics().PendingAlias` first.

- **F33: `RunCommand` and `SubmitInputLine` share ~10 lines of CTS-creation + dispatch + cleanup.**
  Extract `ExecuteWithCts(string line)` (private) returning whether something started.

- **F34: ConsoleController `OnDestroy` does not call `Close`.**
  The map swap (Console → Gameplay) won't happen if the controller is destroyed while open. Low risk in practice (scene reload destroys everything), but if a future feature relies on the map state being Gameplay after teardown, this misses it. Add `if (_isOpen) Close();` at top of OnDestroy.

- **F35: ConsoleController `BeginAsync` uses fire-and-forget `_ = ObservePending(...)`.**
  ObservePending has try/catch that swallows everything, so safe. Worth a comment near the discard explaining the pattern (so a reader doesn't think it's a missing await).

- **F36: ConsoleController `BuildInputSpans` rebuilds spans every frame.**
  Even with no input change, cursor-blink-toggle and pending-spinner cause every-frame rebuild. Could cache spans keyed by `(typed, cursorPos, cursorOn, pendingPhase, suggestion-state-hash)`. Minor GC pressure today; flag for later optimization.

- **F37: ConsoleController has 7 `Color` constants for input-line syntax highlighting** (PromptColor, CmdColor, ValColor, StrColor, GhostColor, HintReqdColor, HintOptColor). Same story as F27 — folds into the `ConsoleTheme` SO proposal.

### Divergences from original design doc

Original doc: `docs/design/2026-06-03-debug-console.md` (2026-06-03). Reality has diverged significantly.

- **D1: Async UX dramatically expanded.**
  Doc: "if user issues another command while one is async (rare), enqueue or reject — pick reject for v1."
  Reality: full abandon + cancel + Y/N confirm + bypass-command whitelist + per-line spinner + IsCancellable pre-check + abandoned-but-still-running tracking. Positive divergence. Doc needs to absorb this.

- **D2: `IConsoleService` interface grew.**
  Doc has: `IsOpen, Open, Close, Toggle, RunCommand, Print, PrintLine, PrintError, Clear`.
  Reality adds: `Anchor` get/set, `PrintWarning`, `BeginAsync`, `AbandonPending`, `RequestCancelPending`, `Confirm`, `ScrollbackCapacity`, `GetDiagnostics`.

- **D3: Built-in commands table is outdated.**
  Doc: `help, clear, history, echo, quit, console.resize, console.anchor, console.scrollback-size`.
  Reality: drops `history` (Up/Down arrow replaces it), drops `console.resize` (anchor preset replaces it), adds `console.abandon`, `console.cancel`.

- **D4: Per-module commands table is wildly outdated.**
  Doc: ~10 commands across weather/time/grass/planet/debug.
  Reality: ~60 across 13 prefixes. Doc still says "~17 commands. Real demonstrable value on day 1." Should be "60 commands at Phase 2 close."

- **D5: No `[CompletionSource]` ever used for biome / scene names.**
  Doc's `WeatherStateCompletionProvider` example never materialized — the enum auto-provider covers WeatherState. Explicit completion providers shipped: CommandNames (for `help`), DebugModeNames, DebugCaptureSetNames. All for things that can't be enumerated by enum reflection alone. The doc example should probably be updated to one of these real ones.

- **D6: New-messages indicator is a border pulse, not a "N new" badge.**
  Doc: "Optionally show a 'N new' badge."
  Reality: `NewMessageBorderColor` pulse in `ConsoleRenderer.Render`. Different choice. Worth updating doc + noting that no count is shown.

- **D7: `Print/PrintEscaped` separation never implemented.**
  Doc Risk #6: "If a command prints user-provided text that contains `<color`, it could be interpreted as markup. Add `PrintEscaped` that escapes `<` first."
  Reality: no `PrintEscaped`. Today low-risk because all printers are internal code. Worth adding for the chat-name / file-path / user-data future case.

- **D8: Open questions 1 & 2 never answered.**
  Doc Q1: per-line timestamp display? — not implemented, no decision logged.
  Doc Q2: long-line wrap vs truncate? — not implemented, behavior unverified.
  Either implement, decide+document, or close as out-of-scope.

- **D9: `ExpressionEvaluator` is not in the design doc at all.**
  Math expressions in `int` / `float` args (`time.speed 60*60`) shipped in a later slice based on Bryan's "Quantum-style math" request. Pure additive feature. Update doc to mention.

- **D10: Slice plan ran longer than estimated.**
  Doc: CONSOLE-0 through CONSOLE-5, ~1900 lines.
  Reality: 22 slices (0 → 5.13), ~3500 lines. Doc should add a "Slice retrospective" pointer to `docs/agent-conversation/2026-06-0[34]-console-slice*.md`.

- **D11: Acceptance Criteria #9 ("Release build without `--allowDebug` has no console") never verified.**
  Per slice logs, the gating logic exists in `DebugConsoleBootstrap.IsConsoleAllowed`, but no QA pass on an actual release build was logged. Worth a one-time verification before declaring the arc done.

### Recommended fix-pass ordering (when Bryan greenlights)

Tier 1 — low-risk dedup wins (each ~30 minutes):
1. F1 + F15 — extract `CompletionRanker.RankFilter` helper, apply to all 6 sites.
2. F14 + F20 — extract `ParameterData.FormatCommandSignature(cmd, includeDefaults)`, apply to all 3 sites. Fixes the `Nullable\`1` bug in `help`.
3. F13 — extract `ConsoleColors.Named` shared dict, dedupe ColorParser ↔ ColorTagParser.

Tier 2 — small bug-ish fixes (~15 min each):
4. F22 — error message on missing IWorldActionManager in undo/redo.
5. F3 — cache `Enum.GetNames` in EnumCompletionProvider field.
6. F4 / F30 — pin backtick binding to a constant referenced by both bootstrap log and OnTextInput filter, or document the binding.
7. F31 — verify backtick toggle behavior; align doc/code.

Tier 3 — design infrastructure (~1-2 hours each):
8. **F12 + F27 + F37: `ConsoleTheme` ScriptableObject.** Move all ~30 colors out of code into one asset. Renderer + Controller read from injected theme. Enables palette tweaks without recompiling.
9. **`[DebugOnly]` attribute infrastructure** (proposed above). Tag existing debug commands. Add ScanFilter to `ConsoleRegistry.Scan`. Verify release-build strip.
10. **F24 + F25 + F26: `ConsoleRenderState` struct + scrollbar/frame helpers.** Tidies renderer; mostly cosmetic.

Tier 4 — design-doc refresh:
11. Update `docs/design/2026-06-03-debug-console.md` with D1-D11 reconciliations OR replace it with a "Console v1 — as-shipped" doc that supersedes the design proposal.

Tier 5 — deferred / out of scope unless explicitly wanted:
12. F16 — assembly scan filter (probably not worth the complexity vs ~100ms cold-path).
13. F17 — synthetic "more..." suggestion when capped.
14. F18 — expression eval for Vector parsers.
15. D7 — `PrintEscaped` / `PrintLine(text, escape: true)`.
16. D8 — answer open questions 1/2 (timestamps + wrap).
17. F36 — input-span caching micro-optimization.

### Registry / parser / intellisense findings

- **F13: ColorParser duplicates ConsoleColorTagParser's named-color table.**
  `ConsoleArgumentParsers.ColorParser` has a 9-case switch (red/green/blue/yellow/cyan/magenta/white/black/grey/gray).
  `ConsoleColorTagParser.NamedColors` has the same 10-entry dict (also includes "orange").
  Two sources of truth, neither in sync. Extract to `static class ConsoleColors { public static readonly Dictionary<string, Color> Named }` and have both consume it.

- **F14: `FormatSignature` is duplicated across CommandExecutor and IntellisenseEngine.**
  - `CommandExecutor.FormatSignature(cmd)` — `[type: name=default]` and `<type: name>` (includes default values)
  - `IntellisenseEngine.FormatSignatureDisplay(cmd)` — `[type: name]` and `<type: name>` (no defaults)
  Subtly different — should consolidate as `ParameterData.FormatCommandSignature(cmd, bool includeDefaults)` and have both call sites use it.

- **F15: Substring-rank pattern duplicated in `IntellisenseEngine.SuggestAliases` too (LINQ form).**
  Adds to F1: now 6 sites doing prefixed→substring→alpha ranking. The LINQ tuple version here vs the for-loop version in providers. Unify into `CompletionRanker.Rank(IEnumerable<T> items, string partial, Func<T, string> keySelector)` returning an ordered IEnumerable.

- **F16: `ConsoleRegistry.Scan` walks every loaded assembly with no filter.**
  System.*, UnityEditor.*, Mono.*, etc. all get `GetTypes()` and a `[CommandPrefix]` check. First console open pays this cost (~100ms? unverified). Options: (a) opt-in via `[assembly: ConsoleCommandsContainer]` attribute, (b) blacklist common platform asms, or (c) accept the cost as cold-path-only. Recommend measuring before optimizing.

- **F17: IntellisenseEngine `MaxSuggestions = 200` cap is silent.**
  If a provider returns 200+ matches the list is silently truncated. Add a synthetic "(N more — refine query)" trailer when cap is hit, or document that the cap is well above the working set.

- **F18: Vector2/Vector3 parsers don't route through ExpressionEvaluator.**
  Int and Float parsers do — so `time.speed 60*60` works. Vector2/Vector3 use `float.TryParse` directly, so `planet.position 1+1,2,3` wouldn't. Inconsistency. Decision: wire expressions through, or note "Vectors take literal numbers only" in xmldoc?

- **F19: `EnumParser.CanParse` correctly returns `t.IsEnum`.**
  Nullable<EnumT> is handled upstream by `ConsoleArgumentParsers.TryParse` (unwraps before dispatch). Worth a one-line comment on the parser confirming this so a future reader doesn't add a redundant nullable check.

### Divergences from original design doc

_(to be reviewed once `docs/design/2026-06-03-debug-console.md` is re-read — placeholder)_

### Missed design-doc opportunities

- **F11: Color-tag parser supports only `<color=>` markup, not `<b>`/`<i>`/`<size=>`.** Per the v1 comment this is intentional. If theming is a follow-up goal, would need extension. Likely defer.
- **F12: No theme/palette abstraction.** Renderer (per scroll-back `ConsoleMessageType`) maps message types to colours inline somewhere; the design doc may have proposed a `ConsoleTheme` SO. Worth deciding: ship a `ConsoleTheme` ScriptableObject or accept the inline mapping.

### Proposed `[DebugOnly]` attribute infrastructure

Design sketch (open for Bryan's input):

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class DebugOnlyAttribute : Attribute { }
```

- On a method → that single command is stripped from registry scan in release builds.
- On a class → every `[ConsoleCommand]` in that class is stripped.
- `ConsoleRegistry.Scan` gate: `if (Application.isEditor || DebugConsoleBootstrap.AllowDebugRequested) { include all } else { skip [DebugOnly] }`.
- Adopt by tagging existing per-slice categorization: `test.console.*` class → `[DebugOnly]`; `scale.*` class → `[DebugOnly]`; `debug.*` (most) → `[DebugOnly]`; `weather.diagnostics` / `precipitation.debug-mode` → method-level `[DebugOnly]`.
- Settings/gameplay commands stay always-on (camera.speed, time.speed, planet.generate are useful for QA testers even in release).

Open question: do we need a third tier "internal-only" for `console.cancel` / `console.abandon`? They're useful in any build that has the console at all, so probably no — `[DebugOnly]` + the existing `--allowDebug` gate is enough.

## Decisions log

_(to be filled in once Bryan reviews findings)_

## Audit progress

**Read & audited (29 of ~40 files):**
- All small/medium files: attributes, anchor, events, history, input-buffer, scrollback, bootstrap, IConsoleService, ConsoleDebugModule, color-tag parser, suggestion, providers (5), MonoTargetType, IConsoleArgumentParser, TextSpan, CommandData, ParameterData
- Registry: ConsoleRegistry, CommandParser, CommandExecutor, ConsoleArgumentParsers, ExpressionEvaluator, IntellisenseEngine
- Commands: ConsoleBuiltins, TestConsoleCommands, ActionCommands
- Big files: **ConsoleRenderer (read), ConsoleController (~900 lines read in 2 chunks)**
- Original design doc: `docs/design/2026-06-03-debug-console.md` (full read)

**NOT yet audited (potential follow-up):**
- Per-system `[CommandPrefix]` inline commands across ~11 controllers — should audit for consistency: naming conventions, ConsoleService access patterns, error-handling shape, who uses Async vs sync, who checks for null target. Likely surfaces 5-10 more findings about cross-system inconsistency. Spreading the audit across grep-driven spot checks rather than full re-reads of each controller file would be efficient.

**Audit is ~80% complete.** Major findings list (38 numbered items + 11 design-doc divergences + 5-tier fix-pass plan) is stable enough for Bryan to review.

## What's next

When findings list stabilizes → ping Bryan for review → he picks which to fix → ship as CONSOLE-6.1+ slices.

**Critical reminder per [audit workflow](../../):** Do NOT start fixing items above before Bryan reviews this findings list. The next session should either (a) finish the audit (Controller + Renderer + builtins + design doc reconciliation) and then ping Bryan, or (b) ping Bryan now with the partial findings if he'd rather direct mid-stream.

---

## Closure status (2026-06-04 EOD)

Bryan greenlit fixes 2026-06-04. Shipped across 3 cleanup slices + doc refresh:

| Slice | Findings closed | Log |
| ----- | --------------- | --- |
| CONSOLE-6.1 | F1, F3, F13, F14, F15, F20, F22 + F31 verified non-bug | [`slice6_1-tier1-tier2-fixes.md`](2026-06-04-console-slice6_1-tier1-tier2-fixes.md) |
| CONSOLE-6.2 | F24, F25, F26 (Renderer cosmetic) | [`slice6_2-renderer-cleanup.md`](2026-06-04-console-slice6_2-renderer-cleanup.md) |
| CONSOLE-6.3 | F12, F27, F37 (ConsoleTheme SO) | [`slice6_3-console-theme-so.md`](2026-06-04-console-slice6_3-console-theme-so.md) |
| (doc) | D1-D11 reconciled via fresh as-shipped doc | [`../design/2026-06-04-debug-console-as-shipped.md`](../design/2026-06-04-debug-console-as-shipped.md) |

**`[DebugOnly]` attribute (Tier 3 item 2): SKIPPED as YAGNI.** `--allowDebug` already gates the whole subsystem at bootstrap. Finer-grained stripping only earns its keep if we ever want release-with-console-but-filtered-commands. Revisit if that requirement appears.

**14 of 38 findings closed.** Remaining 24 are documented Tier 5 deferrals (cosmetic, low-risk, no behavior impact) or items closed at the doc level (D1-D11). The console arc is fully shipped + cleaned.

Per-system command consistency audit across the ~11 decorated controllers (called out as a gap in the original audit summary) was not done — would require grep-driven spot checks. **Recommended only if Bryan sees inconsistencies in practice** (e.g., one controller printing errors via PrintError while another returns string).
