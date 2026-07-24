---
name: pp-docs-and-memory
description: Use when writing or updating any ProceduralPlanets doc of record (design plan, audit findings doc, research digest, agent-conversation entry, phase doc), when updating an Active Tracker checkbox, when Bryan says "remember" or "forget" something (.agent-memory update), when deciding where a fact belongs (docs vs memory vs nowhere), or when applying house code-writing style — comment doctrine, ILogger usage, dead-code rules. Not for how changes are classified, gated, or approved — see pp-change-control.
---

# pp-docs-and-memory — docs of record, cross-agent memory, house style

## What this covers

The project's written record lives in `docs/` (committed docs of record), `.agent-memory/`
(committed cross-agent memory), and the agent rule files (`AGENTS.md`, `CLAUDE.md`,
`GEMINI.md`). Never edit a rule file without Bryan's explicit instruction. This skill
tells you which doc type to write, in what format, and what belongs where. It also
carries the house code-writing doctrine (comments, logging, dead code) because doc drift
and comment drift are the same failure mode.

## Doc-type table

| Type | Directory | Naming | When to write one | Gate |
|---|---|---|---|---|
| Design doc / migration plan | `docs/design/` | `YYYY-MM-DD-topic.md` | Before implementing any non-trivial feature or multi-phase change. Design first, code after Bryan reads it. | Bryan reads/approves the plan; plans embed their own per-phase review gates ("Gate after Phase 2 (Bryan review)") |
| Audit | `docs/audit/` | `YYYY-MM-DD-scope-audit.md` | When asked to audit. **Findings only — no code changed** until Bryan marks each finding fix / defer / wontfix | Absolute. Never fix during or after an audit until Bryan reviews (see pp-change-control) |
| Research digest | `docs/research/` | `YYYY-MM-DD-topic.md` | After a literature/reference-project survey. Ranked by expected payoff *for this architecture*, each item naming its exact integration point | None, but keep it findings/recommendations — implementation goes through a design doc |
| Agent-conversation entry | `docs/agent-conversation/` | `YYYY-MM-DD-<phase>.md`, one file per phase, append-only | Cross-agent (Claude↔Codex) review, questions, or handoff on shared work. Read `docs/agent-conversation/README.md` first | Entries end with "what I'm asking the next agent to do" |
| Phase doc | `docs/phases/` | `NN-phaseN-topic.md` | Rarely — these are the master-plan chapters (00–14), indexed by `docs/PROJECT_PLAN.md`. Update only when the master plan itself changes | Bryan owns the roadmap |
| Memory | `.agent-memory/` | see runbook below | When Bryan says remember/forget, or a stable cross-agent fact emerges | Rules below |

Notes:

- One legacy undated file exists (`docs/sdf-implementation-plan.md`). It predates the convention. All new docs are date-stamped.
- Docs get **retired by deletion** when their arc closes: `docs/audit/2026-06-code-refactor/` was deleted in commit `5e33fca`; its findings live in git history. As of 2026-07-06 CLAUDE.md still links that directory — a known stale reference, not something to recreate.
- Design docs come in two header styles. Older docs (e.g. `2026-06-08-performance-maintainability-plan.md`, `2026-06-13-world-lifecycle.md`) use `**Date:** / **Status:** / **Branch:**` lines. The **current pattern** (the 2026-07-04 migration plans) is the Active Tracker block — use that for new plans.

## Templates (in this skill dir)

| File | Use for |
|---|---|
| `templates/design-doc.md` | New design/migration plan with Active Tracker |
| `templates/audit.md` | New findings-only audit |
| `templates/agent-conversation-entry.md` | Appending a cross-agent entry |

## Active Tracker maintenance

The Active Tracker is the top block of a live migration plan: a `Status:` sentence, a `Current next action:` sentence (concrete, with the exact command when one exists), and a checkbox list. Live examples: `docs/design/2026-07-04-cloud-visual-migration-plan.md` and `...-grass-visual-migration-plan.md`.

Rules:

1. **Update the tracker in the same session that lands a phase** — rewrite `Status:` and `Current next action:` so the tracker never lags the code (a lagging tracker is worse than none; the next agent acts on it). But **ask Bryan before checking any box** on the live migration plans: checkboxes flip only after his sign-off (pp-visual-migration-campaign owns that protocol). If sign-off hasn't happened yet, update `Status:` to say so instead of checking the box.
2. Checked items carry their evidence pointer inline where evidence exists (e.g. `[x] Phase 1 capture comparison: 20260705-051115/051118, Bryan saw no odd behavior`).
3. Capture-comparison and Bryan-review boxes are checked only after the capture/review actually happened — never on "code compiled" (see pp-validation-and-evidence).
4. Don't restructure the plan body below the `---` when updating the tracker; the body is the agreed plan, the tracker is the live state.

## House writing style

- **Date-stamped filenames** (`YYYY-MM-DD-`) for everything in `docs/design`, `docs/audit`, `docs/research`, `docs/agent-conversation`.
- **Imperative, concrete, numeric.** "Run `debug.capture-set "Cloud Diagnostics"` and F10", not "captures should be taken". Exact file paths, exact console commands, exact commit hashes.
- **Findings are numbered with a severity prefix** (`A2. BUG — …`, `G19. META — …`) so later docs can cite them by ID. Design docs cite source findings by ID (`(audit A2, B1)`).
- **Cross-reference instead of re-listing.** CLAUDE.md rule: "Cross-reference baseline audit findings instead of re-listing them." Same for design docs: "the detailed code sketches live there" + a link, not a copy.
- Docs state the exact tree they describe: branch, base commit, dirty-tree notes (e.g. "working tree, dirty on top of `ec0b1cd`"). Date-stamp volatile facts inside the doc body too.
- Record what came back **clean** in audits ("What came back clean" section) — negative results prevent the next auditor re-sweeping.
- Disagreement is recorded, not overwritten: audits get an appended "Codex feedback" / "Claude feedback" section; agent-conversation entries append below earlier entries, never rewrite them.

## Comment doctrine (and the incident behind it)

From CLAUDE.md, operative:

- **Default to no comments.** Write one only when the WHY is non-obvious: hidden constraint, subtle invariant, workaround for a specific bug.
- **Never** change-history commentary (`// was X, now Y`, `// added for Z issue`, `// see PR #123`).
- **Never** explain what code does — names do that.
- **Prune** existing change-history comments whenever you touch a file for another reason.

Why this is a correctness rule, not taste — the **former audit-A2 incident** (reconciled in
`docs/audit/2026-07-22-consolidated-code-audit.md`): `CloudShadows.hlsl` carried the comment
`// Same gloom term as Cloud.shader` above a gloom formula that had drifted to be
*different* from `Cloud.shader` (smoothstep steepening + ungated rain rate vs gated
linear). The false comment actively hid a sky-vs-ground visual inconsistency. A comment
that describes another file's code is a synchronization promise no compiler checks.
Resolution (verified 2026-07-06): the formula was unified into shared helpers
`WeatherCloudGloomFromRain` / `WeatherCloudGloom` in
`Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl:47-55` (formula home:
pp-weather-sim-reference), now called from `Cloud.shader:388` and
`CloudShadows.hlsl:58` — **a shared function is the correct fix for "keep these in sync"
comments; the comment was deleted.**

## Logger doctrine

- New code uses `ILogger` (`Assets/Scripts/Core/Interfaces/ILogger.cs`) via the static `LoggerProvider` (`Assets/Scripts/Core/Services/UnityLogger.cs:40`). Usage pattern in the codebase: `LoggerProvider.Log(LogLevel.Warning, "ChannelName", $"message");`.
- Direct `UnityEngine.Debug.Log*` migrates as files are touched. As of the 2026-07-03 general audit, migration is complete except the `UnityLogger` sink itself and one line in `ConsoleScrollback`.
- **`Warning` means "developer probably wants to fix this."** "Feature disabled, continuing silently" is `Info` (one-time) or a debug channel. Don't cry wolf in the log.

## Dead-code doctrine

- Experiments are deleted in the commit that supersedes them, or within one week.
- Genuinely parked experiments go behind `#if PROJECT_X_EXPERIMENT` with a note on what's parked and why.
- Dead fields, unused enum values, `#if false` blocks, unused DTOs: delete when discovered. In audits, tag them `DEAD` — deletion still waits for Bryan's finding review (findings-only rule wins).
- Editor-time self-tests via `RuntimeInitializeOnLoadMethod` are dead fixtures — delete, don't preserve as "tests".

## Memory runbook (.agent-memory/)

Structure (all committed):

- `.agent-memory/MEMORY.md` — canonical cross-agent index. Loaded into every Claude session via `CLAUDE.md` `@`-import; Codex reads it via `AGENTS.md`.
- `.agent-memory/claude/` — Claude's auto-memory: `MEMORY.md` index + topic files (`feedback_*.md` = doctrine Bryan gave, `project_*.md` = work-arc state, `reference_*.md` = pointers).
- `.agent-memory/codex/` — Codex's imported memory: `memory_summary.md`, `MEMORY.md` registry, rollout summaries, skills.

**Precedence** (from `.agent-memory/MEMORY.md`, binding): 1. Bryan's current explicit instructions → 2. current code, captures, sidecars, project docs → 3. shared memory → 4. agent-specific historical memory. Memory can be stale or checkout-specific — revalidate dates, branches, and implementation status before acting on it.

Update rules (from `AGENTS.md` + `.agent-memory/README.md`):

- When Bryan explicitly asks to **remember or forget** project information → update `.agent-memory/` (the shared index or a linked topic file), not any user-level memory store.
- Stable cross-agent facts → `MEMORY.md` or a linked topic file. Agent-specific detail stays under `claude/` or `codex/`.
- **Never** store credentials, tokens, private keys, or sensitive captures.
- **When two memories conflict, record the conflict and the resolving evidence — don't silently delete history.**
- Date checkout-specific state so the next reader can tell it's stale.

### Where does this fact go?

| Fact | Home |
|---|---|
| Agreed plan / phase sequence / decision gates for a feature | `docs/design/` plan (+ its Active Tracker) |
| Defect found while reading code (not fixing it) | `docs/audit/` finding, numbered with severity |
| "We tried X, it failed because Y" (investigation outcome) | Design-doc body or pp-failure-archaeology territory; a one-line pointer in memory if agents keep re-trying X |
| Bryan's standing preference / doctrine ("always do X") | `CLAUDE.md` only if Bryan says to add a rule; otherwise `.agent-memory/` topic file |
| Current work focus / what arc is live | `.agent-memory/claude/project_current_focus.md` + shared `MEMORY.md` "Current Direction" |
| Question/handoff for the other agent on shared work | `docs/agent-conversation/` entry |
| Transient debugging notes, chat-sized nits | **Nowhere** — chat with Bryan. Don't sediment noise into committed files |
| Design proposals | `docs/design/`, never agent-conversation (its README forbids it) |

## Graphify upkeep

After modifying code, run `graphify update .` (AST-only, no API cost) so `graphify-out/`
stays current. Dirty `graphify-out/` files are expected and are not a reason to skip
graphify (AGENTS.md). Caveat, date-stamped: F05 in
`docs/audit/2026-07-22-consolidated-code-audit.md` records the generated-content and stale
output problem — if a graphify command hangs, see pp-build-and-env for environment traps.

## When NOT to use this

- **Whether a change is allowed, how it's classified/gated, why the non-negotiables exist** → pp-change-control. This skill formats the record; that one governs the action.
- **What counts as evidence, capture protocol, promotion of results** → pp-validation-and-evidence.
- **Running captures / console commands** → pp-run-and-operate; **measuring** → pp-diagnostics-and-tooling.
- **Graphify/build environment problems** → pp-build-and-env.
- **History of past investigations themselves** (the content, not the format) → pp-failure-archaeology.
- **Executing the live migration plans** → pp-visual-migration-campaign.

## Provenance and maintenance

All claims verified against the working tree on 2026-07-06, branch `code-refactor`. Re-verify with (git-bash, repo root):

- Doc tree + naming: `ls docs/design docs/audit docs/research docs/agent-conversation docs/phases`
- Active Tracker pattern: `head -25 docs/design/2026-07-04-cloud-visual-migration-plan.md`
- Audit preamble + severities: `grep -n "Findings only" docs/audit/2026-07-03-*.md` and `grep -n "Severity:" docs/audit/2026-07-03-*.md`
- Agent-conversation protocol: `cat docs/agent-conversation/README.md`
- Gloom unification (A2 resolution): `grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders/`
- Logger surfaces: `grep -n "static class LoggerProvider" Assets/Scripts/Core/Services/UnityLogger.cs` and `ls Assets/Scripts/Core/Interfaces/ILogger.cs`
- Retired audit dir: `git show --stat 5e33fca -- docs/audit/2026-06-code-refactor | head`
- Memory rules: `cat .agent-memory/MEMORY.md .agent-memory/README.md` and the "Shared project memory" section of `AGENTS.md`
- Comment/logger/dead-code doctrine full text: `CLAUDE.md` "Code style" section
