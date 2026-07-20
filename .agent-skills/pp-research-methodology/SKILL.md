---
name: pp-research-methodology
description: Use when deciding if a hypothesis or idea is worth pursuing, designing an experiment, interpreting inconclusive results, deciding to adopt or retire an experiment, or before retrying an old idea ("didn't we try this?"). Triggers: hypothesis, experiment, probe, should we adopt, results ambiguous, tuning not converging, revert or keep. Not for executing a settled plan — see pp-visual-migration-campaign; not for what counts as evidence — see pp-validation-and-evidence.
---

# pp-research-methodology

This skill is the discipline that turns a hunch into an accepted result in ProceduralPlanets.
It is distilled from what actually worked and actually failed here — the cloud cube-face
seam diagnosis, the water-artifact saga, the temporal-accumulation revert, the grass-blanket
retirement — not from a textbook. Follow it and you will not repeat the project's most
expensive mistakes.

Jargon used below, defined once:

- **F10 capture set** — a named list of debug view modes selected via
  `debug.capture-set "<Set Name>"` in the in-game console; pressing F10 captures each mode
  as a PNG plus a `.txt` metadata **sidecar** into `local-only/debug-screenshots`.
- **Binary isolation / extreme proof mode** — a probe that forces an unmistakable outcome
  (forced solid color, forced full opacity, a pass disabled entirely) so the answer is
  yes/no, never "slightly better".
- **Working tree** — the dirty uncommitted state on branch `code-refactor` is normal and
  sacred here; experiments live in it until adopted or retired.

---

## 1. The method at a glance

| Stage | Artifact produced | Gate to next stage |
|---|---|---|
| Hunch | one paragraph: mechanism + what it would explain | can you state a discriminating observation? |
| Sketch | design-doc section or `docs/agent-conversation/` entry | prediction written down (Section 4 template) |
| Probe | cheap binary-isolation test + capture/number | prediction confirmed or killed |
| Experiment | code in the working tree, before/after F10 captures | evidence bar met (Section 2) + refutation pass survived (Section 3) |
| Adopt | Bryan sign-off, plan-tracker checkbox, captures archived | — |
| Retire | revert (whole or partial) + written record of why | never silently retried (Section 6) |

An experiment without a stated expectation is knob-twiddling. A mechanism that explains
only the convenient observations is a guess. Both are banned by this method.

---

## 2. The evidence bar

A mechanism is **accepted** only when:

1. It explains **every** observation — including the negatives (the things that *didn't*
   change when they "should have").
2. It has survived an explicit adversarial-refutation pass (Section 3).
3. Its predicted fix, applied, removes the symptom in captures — and removes it in the
   debug mode where the symptom was first proven, not just in the final composed image.

**Worked example — the cloud cube-face seam (2026-05-31).** Sharp diagonal, cube-face-shaped
seams in the clouds. The winning route was to check the raw `CloudWeather` debug view
*first*. The seam appeared there — in the weather field itself, upstream of all lighting.
The UV-orientation hypothesis (cube-face UV orientation disagreed between weather
generation, shader sampling, cloud shadows, and the CPU weather query path) was accepted
because it explained **all** observations at once:

- why the seam showed in the raw `CloudWeather` field (the data itself was misaligned) — the positive;
- why every lighting tweak had failed to move it (lighting was downstream of the defect) — the negative;
- why the seam traced cube-face boundaries specifically (per-face UV convention mismatch).

The fix aligned cube-face UV orientation across all four consumers (`CubeFaceUv` now lives
in `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl` and is shared; see also
`Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`). The alternative hypothesis —
"it's a lighting/rendering artifact" — was already dead by evidence: it could not explain
the seam's presence in the pre-lighting weather field. That is the bar: one mechanism, all
observations, alternatives killed by evidence rather than by preference.

The general move is **stage ownership**: prove which pipeline stage owns the artifact
before touching anything. If the artifact appears in an upstream debug mode
(`CloudWeather`, `WaterData`), stop tuning downstream presentation. pp-debugging-playbook
owns the per-domain stage maps; this skill owns the acceptance standard.

---

## 3. Adversarial refutation protocol

Before declaring a mechanism accepted, run this devil's-advocate pass. If working with
subagents, assign it to an agent explicitly tasked to *break* the hypothesis — an author
defending their own mechanism is the wrong person to test it. Solo, do it in writing so
the attempt is honest.

Runbook:

1. **List every observation** the mechanism claims to explain. Include the negatives
   ("lighting tweaks never moved it") — negatives are usually the most discriminating.
2. **For each observation, attempt one alternative mechanism** that could also produce it.
   Write the alternative down even if it seems weak.
3. **For each surviving alternative, name the discriminating probe** — the single cheap
   observation whose outcome differs between your mechanism and the alternative. Prefer
   binary isolation: a debug mode, a forced constant, a disabled pass.
4. **Run only the probes whose alternatives are still alive.** An alternative is dead when
   an existing capture or measurement already contradicts it — cite which one.
5. **Verdict**: accepted only when every alternative is dead **by evidence**. "My
   mechanism is more elegant" is not a kill. "The seam is visible in `CloudWeather`, which
   renders before lighting runs" is a kill.

Refutation table to fill in (paste into the design doc or agent-conversation entry):

```
| Observation | My mechanism explains it because | Alternative mechanism | Discriminating probe | Alternative status |
|---|---|---|---|---|
```

---

## 4. Predict numbers before running

State the expected observation or number **before** the capture or measurement. This is
practiced in the project's migration plans — the gates are pre-registered predictions, as
of 2026-07-06:

- `docs/design/2026-07-04-grass-visual-migration-plan.md` Phase 0 exit check:
  "placement stats unchanged before/after (same emitted counts on the same seed)" —
  a refactor predicted to be a no-op, with the number that proves it named in advance.
- Same plan, Phase 1: move lighting to the vertex stage and "Measure NearGrass frame
  section before/after — expect the largest single grass GPU win available"; exit check
  "NearGrass ms measurably down" with A/B captures "identical-to-eye at blade scale".
- `docs/design/2026-07-04-cloud-visual-migration-plan.md` Phase 0 exit check: "Cloud
  darkening above and shadow darkening below must track the same cells" — a testable
  agreement prediction, checked at a rain cell found via `cloud.debug-mode 8`.

If you cannot write the prediction, you do not yet have a hypothesis — you have a knob.

**Cautionary tale — the water saga.** The costliest failure pattern in this project's
history: repeated F10 runs against a "washed transparent sheet" final image, tuning
constants without a stated expectation, while proof modes (`SurfaceRawOpaque`,
`SurfaceFxProof`) already showed convincing raw output. No run could fail, because no run
predicted anything. The recorded lesson (codex memory, restated here): *if repeated F10
runs show no visible progress, stop knob-twiddling and design an extreme/binary isolation
step before touching more constants* — and if the final `Off` view stays wrong while proof
modes look right, the problem is composition/layering, so pivot branches instead of
re-tuning the current one. The saga ended only when work switched to hard isolation modes
(`TerrainSourcePink`, `SeaRay`, forced opacity, disabled passes) and a layer-by-layer
rebuild — i.e., when every step became a yes/no question.

Related standing rule (Bryan, Phase-1 answers): **no visual-constant tuning without a
capture-diff, and Bryan's eyes lock a look** — an agent's eye alone never judges visual
success. See pp-change-control for the gate; see pp-validation-and-evidence for what a
valid capture-diff is.

---

## 5. The experiment template (paste and fill)

Paste this block into the relevant design doc or a `docs/agent-conversation/` entry
(file convention there: `YYYY-MM-DD-<phase>.md`, append under
`## YYYY-MM-DD — <author> — <topic>`) **before** running the experiment:

```
### Experiment: <short name> — <date>

Hypothesis (mechanism, one sentence):
Predicted observation (number/capture, stated BEFORE running):
Probe (exact steps: console commands, capture set, measurement source):
Refutation pass: <link/inline table from Section 3, or "trivial: <why>">
Result (what was actually observed):
Verdict: ADOPT | RETIRE | RETIRE-WITH-KEEPS (<list keeps>) | INCONCLUSIVE (<next probe>)
```

Rules of use:

- "Result" is filled in only after "Predicted observation" exists. Never backfill the
  prediction to match the result.
- INCONCLUSIVE must name the next discriminating probe or convert to RETIRE. An
  experiment may not idle in the working tree as "inconclusive" — dead-code rules apply
  (Section 6).
- For anything visual, "Result" cites capture timestamps (e.g. the cloud plan archives
  `20260704-1325xx` as its Phase 0 baseline) — see pp-run-and-operate for where captures
  land and pp-validation-and-evidence for before/after protocol.

---

## 6. The idea lifecycle: adopt or retire, never linger

Full path: hunch → design-doc sketch or agent-conversation entry → cheap discriminating
probe → experiment in the working tree → **adopted** or **retired**.

**Adopted** means all three of:
1. capture evidence archived (before/after set, timestamps recorded in the plan tracker);
2. Bryan sign-off (explicit for anything visual — plans encode this as named gates, e.g.
   "Gate after Phase 2 (Bryan review)");
3. tracker updated (the plan's Active Tracker checkboxes + "Current next action" line).

**Retired** means, per the CLAUDE.md dead-code rule: reverted at the superseding commit or
within one week — or, if genuinely parked, gated behind `#if PROJECT_X_EXPERIMENT` so it
stops shipping, with a written note of what's parked and why. (As of 2026-07-06 no code in
`Assets/` uses that define — every retired experiment so far chose revert over parking.)
Either way the retirement is **documented so it is never silently retried**.

Three verified retirements, showing the full range of outcomes:

| Experiment | Verdict | What was kept | Where the record lives |
|---|---|---|---|
| Cloud temporal accumulation (multi-frame march amortization) | Retired — reverted to single-pass march | **Partial keeps**: pass ordering + per-step jitter changes | Preamble of `docs/audit/2026-07-03-grass-cloud-line-audit.md`; `docs/research/2026-07-04-cloud-visual-research.md` states "no temporal accumulation (tried, reverted)" so no later plan re-proposes it blind |
| Grass terrain-paint blanket (far-field coverage) | Retired pending the biome-stripe / far-field decision — `_grassBlanketEnabled = false` (`Assets/Scripts/Planet/PlanetGrassCoordinator.cs:21`, as of 2026-07-06), `PlanetVertexColor.shader` reverted to HEAD | The **diagnosis** survived: the stripe root cause (linear coverage + toe cut) is recorded as known-good in grass plan Phase 3(a), with `run "Grass Edge Strip Probe"` (`Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt`) as the re-landing regression harness | Audit preamble + `docs/design/2026-07-04-grass-visual-migration-plan.md` Phase 3 |
| Water-volume lip prepass (underwater shoreline gap) | Retired **as a global pass** (global `ZTest Always` lip caused above-water/through-planet regressions) | Kept **as a conditional**: relaxed lip drawn only when `IsCameraInsideWaterMesh` (`Assets/Scripts/Planet/WaterVolumeRenderFeature.cs:87,188`) | Codex memory rule: "do not re-enable a global ZTest Always lip pass" |

The pattern to internalize: **retirement can salvage components.** A failed experiment's
verdict applies to the mechanism as proposed, not to every piece of it — the temporal
revert kept its jitter improvements; the lip revert kept the pass under a guard; the
blanket revert kept the root-cause fix on file for re-landing. When retiring, explicitly
list the keeps in the template's `RETIRE-WITH-KEEPS` verdict.

Also verified as of 2026-07-06: `_chunkGrassEnabled = false`
(`PlanetGrassCoordinator.cs:18`) — the chunk layer is another parked-by-flag decision
(audit G6, reserved for Bryan), not a silent death.

---

## 7. Negative results are results

A killed hypothesis or reverted experiment is recorded, never just deleted:

- **Audit preambles** state the post-review negative results as context ("the cloud
  temporal-accumulation experiment is reverted ... the grass blanket layer is disabled")
  so the next auditor doesn't re-litigate them.
- **Research docs** carry the scar tissue forward ("no temporal accumulation (tried,
  reverted)") so recommendations are ranked against what already failed here.
- **Memory** (`.agent-memory/`) holds the behavioral rules distilled from failures (the
  knob-twiddling stop rule, the global-lip ban) — background reference, restated in the
  skills where load-bearing.
- **pp-failure-archaeology is the ledger**: symptom → root cause → evidence → status for
  every major dead end. Before retrying any old idea, check it first — if the idea was
  retired, the retry must state what has changed since the retirement (new evidence, new
  constraint removed), or it is a silent retry and forbidden.

"We tried X and it didn't work because Y" is one of the highest-value artifacts this
project produces. Write it down at the moment of retirement, while Y is still sharp.

---

## 8. Where good ideas come from here

Verified intake channels, in rough order of historical yield:

1. **`docs/research/` digests of external references.** External projects and papers live
   in `local-only/` (Sebastian Lague's `Clouds-master` — our cloud shader's ancestor,
   `UnityURP-InfiniteGrass-main`, `GrassFlow`, the Ghost of Tsushima GDC 2021 PDF, Harris
   cloud guide, GPU Gems digests) and at `D:\Planet_Architect_v0.1.5_Windows` (analyzed in
   `docs/research/2026-06-05-planet-architect-biomes-vegetation.md`). The digest doc does
   the filtering: what the reference does, what we do, verdict — including an explicit
   "Explicitly not recommending" section so bad fits die on paper, not in the working tree.
2. **The R-numbered reference-recommendation pattern.**
   `docs/research/2026-07-04-grass-cloud-reference-recommendations.md` assigns stable IDs
   (R1-R10) to each portable idea, ranks them by leverage into top/second/third tier, and
   the migration plans then cite them by ID (cloud plan consumes R1, R2, R7; grass plan
   consumes R3, R4, R5, R6, R8). This makes idea→plan traceable and keeps rejected ideas
   (impostor clouds, per-frame buffer realloc) permanently on record. New reference
   analysis should follow this pattern: stable ID, what-they-do/what-we-do/verdict, tier.
3. **The audits.** Findings (A/B/C/D/E/G series) are hypotheses about defects, each with
   file:line evidence — plans sequence them. See pp-change-control for the findings-first
   workflow.
4. **Capture-evidence anomalies.** Things that light up in an F10 mode that shouldn't —
   the cube-face seam entered as a capture anomaly, not a plan item. The rule from memory:
   let the modes that still light up decide the next branch.
5. **Published research digested against OUR architecture.**
   `docs/research/2026-07-04-cloud-visual-research.md` ranks Schneider/Nubis, Frostbite,
   etc. "by expected visual payoff for our architecture", naming the exact integration
   point in `Cloud.shader` per item — a paper idea isn't an idea here until it has an
   integration point. Intake includes license discipline: the grass plan's clump-shape
   note credits a CC BY-NC-SA Shadertoy as "concept only, no code."

For which open problems deserve new research at all, see pp-research-frontier.

---

## When NOT to use this

- **Executing a settled, gated plan** (the cloud/grass visual migration phases) — see
  **pp-visual-migration-campaign**. This skill governs how ideas *enter* such plans.
- **Analysis recipes and worked math/measurement techniques** — see
  **pp-proof-and-analysis-toolkit**.
- **What counts as evidence, capture protocol, promotion of a result** — see
  **pp-validation-and-evidence**. This skill assumes that standard and adds the
  hypothesis discipline around it.
- **Looking up whether an idea already failed** — see **pp-failure-archaeology** (the
  ledger this skill writes into).
- **Triage of an active bug** — see **pp-debugging-playbook** (stage-ownership tables);
  come back here when you have a candidate mechanism to accept or reject.
- **How/where to write the record** (doc conventions, memory rules) — see
  **pp-docs-and-memory**.

---

## Provenance and maintenance

All claims verified 2026-07-06 against branch `code-refactor` (working tree dirty on
`ec0b1cd` — normal). Re-verify volatile facts with:

```
# Grass layer flags still off (lifecycle table, Section 6)
grep -n "_grassBlanketEnabled\|_chunkGrassEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs

# Lip prepass still camera-inside-water conditional
grep -n "IsCameraInsideWaterMesh" Assets/Scripts/Planet/WaterVolumeRenderFeature.cs

# Temporal-accumulation revert + blanket disable still stated in the audit preamble
head -12 docs/audit/2026-07-03-grass-cloud-line-audit.md

# Migration-plan predicted gates still current (tracker status drifts fastest)
head -25 docs/design/2026-07-04-cloud-visual-migration-plan.md
head -25 docs/design/2026-07-04-grass-visual-migration-plan.md

# R-numbered recommendations doc still the idea source of record
grep -n "^### R" docs/research/2026-07-04-grass-cloud-reference-recommendations.md

# Strip-probe regression harness still present
ls "Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt"

# PROJECT_X_EXPERIMENT still unused in code (Section 6 claim)
grep -rn "PROJECT_X_EXPERIMENT" Assets/ || echo "unused - claim holds"

# Cube-face seam fix still centralized in shared sampling include
grep -n "CubeFaceUv" Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl
```

Historical narratives (cube-face seam route, water saga, lip-prepass ownership) are
restated from `.agent-memory/codex/memory_summary.md` and
`.agent-memory/codex/skills/proceduralplanets-water-artifact-debug/SKILL.md` — additional
background only; the load-bearing facts are embedded above. If a retirement in Section 6's
table is re-landed (e.g. blanket Phase 3(a)), update the table's verdict and keep the
original retirement record intact in pp-failure-archaeology.
