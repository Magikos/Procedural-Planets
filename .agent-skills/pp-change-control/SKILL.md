---
name: pp-change-control
description: Use before risky or gated edits in this repo - classifying refactor/behavior/visual/experiment/audit work, deciding whether a fix is approved, tuning any visual constant or shader value, responding to audit findings, reverting an experiment, committing, staging, or touching another agent's dirty worktree changes. Keywords - findings-only, fix/defer/wontfix, capture-diff, caustics don't-touch, hand-tuned values, revert discipline. Not for how to capture evidence itself - see pp-validation-and-evidence.
---

# pp-change-control — how changes are gated in ProceduralPlanets

The one-sentence model: **Bryan is the change authority.** Agents produce findings,
evidence, and candidate changes; Bryan's review approves fixes, and Bryan's eyes — never
an agent's judgment — decide whether a visual result is done. Current user instructions,
the working tree, `AGENTS.md`/`CLAUDE.md`, and `.agent-memory/` form the rule stack;
if code or skill text disagrees with that stack, the stale text/code is drift to correct.

## 1. Change classification — classify first, then apply the gate

| Class | Definition | Gate before merge-worthy |
|---|---|---|
| **Audit** | Read-only review producing a dated findings doc in `docs/audit/` | No code changes at all. Bryan marks each finding fix / defer / wontfix. |
| **Code-health refactor** | Behavior-preserving restructure (extraction, rename, dead-code delete) | Approved finding or explicit ask; `dotnet build` of `ProceduralPlanets.Core.csproj` then `ProceduralPlanets.Planet.csproj` **serially** (parallel builds collide on a shared intermediate DLL); Unity import. One extraction per commit, validate-then-commit. |
| **Behavior change** | Logic/bug fix that changes runtime behavior but not the look | Approved finding or Bryan's request; build + play-mode/runtime evidence that the changed path actually runs (build success is a code-health check only). |
| **Visual change** | Anything that changes what renders — shader code, tuning constants, colors, distances, fades | Everything above **plus the visual tuning gate (§3)**: before/after F10 capture-diff, and Bryan's eyes lock the look. Phased visual plans carry explicit Bryan-review gates (e.g. cloud plan "Gate after Phase 2"). |
| **Experiment** | Exploratory code whose value is unproven | Lives only in the dirty worktree. Deleted at the superseding commit or within one week, or parked behind `#if PROJECT_X_EXPERIMENT` with a note on what's parked and why. Salvage the proven parts on revert (§5). |

"F10 capture" = the debug-screenshot workflow: `debug.capture-set "<Set Name>"` in the
in-game console selects a named set of debug view modes; pressing F10 writes one PNG +
`.txt` metadata sidecar per mode to `local-only/debug-screenshots`. Details live in
pp-validation-and-evidence and pp-run-and-operate.

## 2. The audit workflow (findings-first, always)

- Audits are **read-only**. Every audit doc opens with the boundary line — literally
  "**Findings only — no code changed.**" (see
  `docs/audit/2026-07-22-consolidated-code-audit.md`). Do not roll from audit into edits.
- Deliverable: a dated file `docs/audit/YYYY-MM-DD-<topic>.md` with severity-tagged
  findings (`BUG`, `RISK`, `PERF`, `DEAD`, `STYLE/BP`, `SUGG`; the general audit adds
  `RULE`, `ARCH`, `META`). Cross-reference prior audit findings instead of re-listing
  them, and re-validate prior findings against current code (resolved / partial / open),
  stating your own agreement or disagreement — Bryan explicitly wants independent
  judgment, not a rubber stamp.
- Bryan reviews and marks each finding **fix / defer / wontfix**. Only then does
  implementation start. Real lifecycle example: the former grass-LOD findings reconciled
  in `docs/audit/2026-07-22-consolidated-code-audit.md`
  status line — "IMPLEMENTED 2026-07-01 (Bryan approved; Codex feedback amendments
  applied) ... G6 deliberately not executed — Bryan's call ... Needs in-Unity visual
  verification." Approval is per-finding, amendments from a second reviewer get folded
  in, some findings are explicitly declined, and implementation still isn't "done" until
  visual verification.
- Housekeeping note (as of 2026-07-06): `docs/audit/` is **untracked** in git — the
  older tracked audits (including `docs/audit/2026-06-code-refactor/`, which CLAUDE.md
  still links) were removed from tracking at commit `5e33fca`. Recover historical audit
  text with `git show 7048c2c:docs/audit/2026-06-code-refactor/00-summary.md`.

## 3. The visual tuning gate (Bryan's rule, stated 2026-07-06)

1. **No tuning a visual constant without a before/after capture-diff.** Capture the F10
   set for the domain before the change, again after, and compare. A change you cannot
   show in a capture pair did not happen.
2. **Never retune a value Bryan hand-picked.** If a constant was baked from a live tuning
   session, it is locked. Incident: the 2026-07-01 grass-LOD implementation brightened
   the far-overlay paint (finding G2); in-game it "glowed as a halo at partial coverage"
   and was **reverted to the hand-tuned absolute color (0.46 through the shared 0.76
   canopy scale)**. Same session, finding G3: a widened coverage smoothstep left "a
   bright wash along biome-blend borders" and was fully reverted — the original formula
   stands. If a look problem remains, expose a live console knob (`grass.surface-brightness`
   precedent) and let Bryan tune, then bake his number.
3. **Bryan's eyes lock a look.** Success is never judged by the agent's eye alone. A
   visual phase is complete when Bryan has seen the captures (or played it) and said so —
   e.g. cloud migration Phase 1 closed with "Bryan saw no odd behavior" against captures
   `20260705-051115/051118`.
4. **Isolate before tuning.** The costliest failure in project history (the water-artifact
   saga) was knob-twiddling constants before isolating the owning stage. If repeated
   captures show no progress, stop tuning and design a binary/extreme isolation step —
   see pp-debugging-playbook.

## 4. Non-negotiables — rule, rationale, incident

| Rule | Why | Incident behind it |
|---|---|---|
| **Caustics are untouchable** (`Assets/Graphics/Shaders/Ocean.shader` caustics code). Audit findings against caustics are flag-only. | They look correct now and are fragile: per CLAUDE.md, "every touch breaks them." | Repeated caustics breakage during the water arc is the stated origin of the rule (Bryan, 2026-07-06). Every recent audit and plan explicitly scopes them out ("Caustics untouched"). |
| **Audits are findings-only until Bryan marks decisions** | Bryan iterates over long arcs; findings get stale, and he wants independent agreement, not auto-fixes. Fixing mid-review destroys the review. | The whole code-refactor arc (2026-06-10 → 06-15) ran audit → Bryan review → fix, per finding. Memory records "do not roll directly from audit into edits without explicit approval." |
| **Visual tuning gate (§3)** | An agent cannot see the render; captures + Bryan's eyes are the only ground truth. | G2/G3 reverts (§3); water-saga knob-twiddling. |
| **No change-history or false comments; comments only for non-obvious WHY** | Comments drift into lies the compiler never checks. | Audit finding A2 (2026-07-03): `CloudShadows.hlsl` carried `// Same gloom term as Cloud.shader` while the two formulas had diverged (smoothstep steepening and storm gating differed) — sky and ground disagreed about the same storm. Fixed by extracting shared `WeatherCloudGloomFromRain` / `WeatherCloudGloom` into `WeatherSampling.hlsl` (lines 47–55 as of 2026-07-06; formula home: pp-weather-sim-reference) so both paths call one function instead of a comment promising parity. |
| **Experiments die on schedule (§5)** | Unproven code left live becomes load-bearing and fights the next change. | The biome-stripe / grass-blanket fight: the terrain-paint blanket layer produced biome stripes; the fight ended with `_grassBlanketEnabled = false`, `_chunkGrassEnabled = false` (`PlanetGrassCoordinator.cs:18,21`, still false as of 2026-07-06; current value: see pp-settings-and-flags) and `PlanetVertexColor.shader` reverted to HEAD. |
| **Dirty worktree is sacred; stage only your own files (§6)** | Multiple agents (Claude + Codex) work the same tree; active work lives uncommitted. | 2026-06-13: Codex's world-lifecycle refactor was live in the same hot files as Claude's Slice 5; commits `f16d296`/`a37390b` were bundled only on Bryan's explicit call. |
| **Keep incidental cleanup out of active fixes** | Mixed diffs make review and revert impossible. | Bryan's standing preference (Codex memory): no namespace/folder cleanup inside an active fix "unless there is an explicit rule-backed reason." Exception that IS the rule: prune change-history comments in files you touch for another reason (CLAUDE.md). |
| **Build success ≠ visual/runtime proof** | `dotnet build` checks code health only; Unity import + play mode decide. | Both 2026-07-01 audits shipped with "Needs in-Unity visual verification" because shaders can't compile outside the editor. |
| **Console setters take human units (0-1 or real units), never a raw shader/physics coefficient** | A raw coefficient (`0.002`, `0-0.08`) is unmemorable and non-linear; Bryan reasons in "50% fade / metres / %", not per-metre extinction rates. Convert to the internal coefficient inside the setter; the getter reports the human value; the shader/DTO stays physical. The physical **range lives once** as a `public const` on the settings SO (or state class that owns the field), referenced by both the SO `[Range(Min, Max)]` inspector attribute and the console mapping — so editing a range is a one-line edit on the authoring surface, and the slider + console never drift. Shader *globals* carry no range to read at runtime (only editor-only `ShaderUtil` sees `Properties{ Range() }`), so the SO is the correct single source, not the shader. | 2026-07-17 sweep converted `cloud.density`, `cloud.debug-threshold`, `cloud.debug-saturation`, `atmosphere.sun-disc-blend` to 0-1; 2026-07-18 single-sourced the ranges (`CloudSettings.DensityMin/Max`, `AtmosphereSettings.SunDiscBlendMin/Max`, `CloudDebugState.Condensation…Max`). Template: `AtmosphereController.MieCmd` / `CloudController.AerialFadeCmd` — `human * SO.Max` on set, `internal / SO.Max` on get (or `Mathf.Lerp`/`InverseLerp` when the range has a nonzero floor). The sweep also caught a pre-existing `sun-disc-blend` drift (SO slider capped 0.01, console allowed 0.05); single-sourcing to 0.05 removed it. |

## 5. Experiment lifecycle — worked example

CLAUDE.md rule: experiments are deleted at the same commit that supersedes them, or
within one week; genuinely parked experiments go behind `#if PROJECT_X_EXPERIMENT` with
documentation of what's parked and why. Dead fields, unused enums, `#if false` blocks,
unused DTOs: removed when discovered.

**The cloud temporal-accumulation revert (2026-07-01 → 07-03) is the template:**

1. The 2026-07-01 cloud-rain audit proposed jitter fixes and explicitly cautioned that
   temporal jitter without a resolve/accumulation path "turns static grain into crawling
   grain" (W1: "no-history jitter only").
2. A temporal-accumulation experiment was tried anyway, in the dirty worktree, judged by
   captures and Bryan's eyes.
3. It lost. The revert **salvaged the proven parts**: "the cloud temporal-accumulation
   experiment is reverted (single-pass march kept, pass ordering + per-step jitter
   changes retained)" — 2026-07-03 line-audit preamble.
4. A fresh line-by-line audit then re-validated the post-revert tree, so the next plan
   built on verified state, not on memory of the experiment.

Takeaways: experiment freely in the uncommitted tree; let captures decide; revert is not
all-or-nothing (keep what the evidence proved); re-audit after a messy revert. Deeper
history of this and other reverts: pp-failure-archaeology.

## 6. Dirty-worktree discipline

- Branch `code-refactor` (as of 2026-07-06); dirty on top of `ec0b1cd` is **normal and
  sacred** — active work lives uncommitted, sometimes from more than one agent.
- Never `git checkout -- .`, `git reset --hard`, `git stash` broadly, or reformat-on-save
  across the tree. Preserve every change you didn't make.
- Make focused edits: touch only files your task owns. Before staging anything, run
  `git status` and stage **only your own files**; another agent's work may be interleaved
  in the same directories.
- Reverting your own experiment means restoring exactly the files you changed (e.g.
  `git checkout -- <file>` per file, or hand-reverting hunks), never a tree-wide reset.

## 7. Commit conventions (derived from `git log`)

- **Commit only when Bryan asks.** He owns checkpoint timing; his own commits are terse
  milestones ("Grass paths working well", "Before grass and cloud changes",
  "CHECKPOINT: Before atmosphere rewrite v3 ...") — note the safety-checkpoint habit
  before risky arcs.
- Subject: imperative, capitalized, no trailing period, no conventional-commits prefixes.
  House patterns: `Extract X from Y (Slice 6)`, `Fix <symptom>: <cause/action>`,
  `Centralize shader-global names: <domain> (T6)`, `Close CORE-2: ...` — reference the
  audit/plan item ID when one exists.
- Body (when the why isn't obvious): short prose paragraphs explaining cause → fix, as in
  `143ceee` ("PrecipitationRenderFeature used ServiceLocator.Get<...> but the service is
  optional ... switching to TryGet stops the InvalidOperationException").
- One validated change per commit ("validate-then-commit"; the Slice-6 extractions are
  the precedent: one extraction, Unity validation, commit). Bundled multi-agent commits
  only on Bryan's explicit instruction.
- Agent-authored commits end with a `Co-Authored-By: Claude <model> <noreply@anthropic.com>`
  trailer (see `143ceee`, `7048c2c`).

## 8. Agent checklist — before / during / after a change

**Before**
- [ ] Classify the change (§1). If it's an audit, stop here: findings doc only.
- [ ] Confirm authorization: an approved finding, an active approved plan phase, or
      Bryan's direct ask. "I noticed it while in the file" authorizes nothing except
      deleting trivially dead code and pruning stale comments while touching the file
      for an approved reason (CLAUDE.md) — and NOT dead code that is already a numbered
      open audit finding (e.g. general audit G8, `EventBusAutoBinder.cs`): anything with
      a finding ID stays findings-first, Bryan-gated.
- [ ] Check the don't-touch list: any path near `Ocean.shader` caustics → flag, don't edit.
- [ ] If visual: capture the BEFORE state first (`debug.capture-set` + F10 — protocol in
      pp-validation-and-evidence).
- [ ] `git status` — know which dirty files are yours vs. another agent's.

**During**
- [ ] Edit only files your task owns; no incidental cleanup beyond comment-pruning in
      files you're already touching.
- [ ] Follow CLAUDE.md architecture rules (SO→DTO, Awaitable-only, ShaderGlobalIds,
      ILogger, dirty-flag uploads — see pp-architecture-contract).
- [ ] New visual constant an agent guessed? Prefer a console knob so Bryan can tune live.

**After**
- [ ] Build Core then Planet serially; fix warnings you introduced.
- [ ] `graphify update .` after code changes (AST-only, free; set a timeout — known hang
      in this checkout, see pp-build-and-env Known traps).
- [ ] Produce evidence matched to the class (§1): captures for visual, runtime proof for
      behavior. State plainly what is still unverified ("needs in-Unity visual
      verification" is the honest house phrase).
- [ ] Do not commit, and do not declare a visual change done — both are Bryan's calls.

## When NOT to use this

- **How to capture/compare evidence, what counts as proof, promotion of a result** →
  pp-validation-and-evidence.
- **Doc-of-record templates, audit-doc formatting details, .agent-memory update rules,
  comment doctrine deep-dive** → pp-docs-and-memory.
- **Which architecture rules a change must satisfy (DTOs, init phases, shader globals)** →
  pp-architecture-contract.
- **Past incidents in full detail** → pp-failure-archaeology.
- **Debugging a rendering artifact (isolation method)** → pp-debugging-playbook.

## Provenance and maintenance

Sources: `CLAUDE.md` (Audit workflow, Don't touch, Dead code, Comments sections);
`docs/audit/2026-07-22-consolidated-code-audit.md` preamble and prior-audit
reconciliation status lines; `docs/design/2026-07-04-cloud-visual-migration-plan.md`
(gates) and
`...-grass-visual-migration-plan.md` (biome-stripe aftermath); git history; the
2026-07-06 authoring brief (Bryan's Phase-1 answers: visual-tuning rule, caustics-origin,
costliest failures). UNVERIFIED: the specific commits/sessions where caustics were
broken — the rule's text is in CLAUDE.md but the incident details are not in the repo.

Re-verify volatile facts:

- Grass layers still disabled: `grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs`
- Caustics rule still in force: `grep -n "Caustics" CLAUDE.md`
- Gloom parity still via shared helper: `grep -n "WeatherCloudGloom" Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`
- Audit boundary phrasing: `grep -rn "Findings only" docs/audit/`
- Current branch/base: `git status -b --short` and `git log --oneline -3`
- docs/audit still untracked: `git ls-files docs/audit` (empty = untracked)
- Commit trailer style: `git log -1 --format=%B 143ceee`
