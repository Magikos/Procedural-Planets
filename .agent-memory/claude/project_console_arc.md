---
name: project-console-arc
description: "Quantum-style debug console arc — slices 0-5.13 shipped (~60 commands, 13 prefixes), CONSOLE-6 audit/cleanup pass in progress"
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

The in-game debug console (gated by `--allowDebug` CLI flag) is functionally complete as of 2026-06-04.

**Why:** Bryan asked for a Quantum-style console as the canonical dev tool for poking at every subsystem. ~60 commands across 13 prefixes (camera, time, quality, scale, debug, weather, atmosphere, cloud, precipitation, lightning, action, planet, console) decorate existing controllers via `[CommandPrefix]` + `[ConsoleCommand]` attribute reflection.

**How to apply:** All console code lives in `Assets/Scripts/Core/Console/`. Per-system commands are added inline on the existing controller class (not in a separate Commands/ folder) — see `Planet.cs`, `AtmosphereController.cs`, etc. Test commands live in `Console/Commands/TestConsoleCommands.cs`. Slice logs at `docs/agent-conversation/2026-06-0[34]-console-slice*.md` (22 logs, slices 0 → 5.13).

**Cleanup arc: COMPLETE as of 2026-06-04.** Shipped 3 cleanup slices + design-doc refresh:
- CONSOLE-6 audit: 38 numbered findings + 11 design-doc divergences. Log: `docs/agent-conversation/2026-06-04-console-slice6-audit.md`.
- CONSOLE-6.1: Tier 1+2 dedup (CompletionRanker, FormatCommandSignature, ConsoleColors, action errors, Enum.GetNames cache, Nullable display bug fix in help).
- CONSOLE-6.2: Renderer cosmetic (ConsoleRenderState struct, DrawVerticalScrollbar helper, DrawPopupFrame helper).
- CONSOLE-6.3: ConsoleTheme ScriptableObject. ~30 colors out of code; `Resources.Load<ConsoleTheme>("ConsoleTheme")` with `CreateDefault()` fallback for zero-config out-of-box.
- Doc refresh: fresh as-shipped doc at `docs/design/2026-06-04-debug-console-as-shipped.md` supersedes the original `2026-06-03-debug-console.md` (marked HISTORICAL).

**`[DebugOnly]` attribute: SKIPPED as YAGNI.** `--allowDebug` already gates the whole console subsystem at bootstrap. Finer-grained per-command stripping only earns its keep if we ever ship release-with-console-but-filtered-commands. Revisit if that requirement appears.

**Tier 5 deferrals** (low-risk cosmetic, fine as-is): F16 (asm scan filter), F17 (capped-list indicator), F18 (Vector expr eval), D7 (PrintEscaped for user-supplied strings), D8 (timestamps + wrap-mode), F36 (input-span caching micro-opt).

**Open audit gap:** per-system command consistency audit (~11 controllers) — would require grep-driven spot checks. Skip unless Bryan sees inconsistencies in practice.

Old plan kept for reference:
1. Walk every `Console/*` file for dead code, dup logic, stale comments, naming inconsistencies
2. Reconcile slice-log design calls vs original design doc at `docs/design/2026-06-03-debug-console.md`
3. Identify missed design-doc items (color theming, awaitable cancellation propagation, etc.)
4. Refresh the original design doc (now several iterations behind)
5. Convert the categorization tracker into `[DebugOnly]` attribute infrastructure for release-build stripping

Per [[feedback-audit-workflow]]: read-only audit first → findings doc → Bryan reviews → fixes ship as follow-up. **Do not start fixing before Bryan reviews findings.**

Scope doc + running findings: `docs/agent-conversation/2026-06-04-console-slice6-audit.md`.

Categorization tracker (at last update):
| Category | Commands |
| -------- | -------- |
| Debug-only | `scale.*`, `debug.*` (8), `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| Settings | `camera.*`, `quality.*`, `time.*`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| Gameplay / world state | `action.*`, `planet.*` |
| Console internal | `console.abandon`, `console.cancel` |
