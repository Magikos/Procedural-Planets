# Periodic code audit

Status: design, **not approved**. Revision 2, 2026-08-18, after Codex review.

## Provenance of this revision

| | |
|---|---|
| Branch | `harvest-vertical-slice` |
| HEAD | `abb1123` |
| Tree at review | **100 tracked-dirty files, 19 untracked** |

That dirty count is not incidental — it is the evidence for the staleness design below. A commit hash
identifies almost nothing about what was actually audited in this repo.

**Rule changes are already applied, not proposed.** The CLAUDE.md dead-code exception, the
`ponytail:` / `planned:` marker conventions, the interface-seam/modding rule, and the caustics
correction across nine skills were each approved individually by Bryan and are live in the working
tree. They are recorded here as done. Nothing else in this document is approved.

## Problem

`docs/audit/` holds four dated audits. Each reconciles only the prior findings overlapping its own
scope — the 2026-08-11 doc says so explicitly. So nothing answers **"what is open right now, across
the project"**.

There is also no signal for when an audit is due, no deterministic pass for rules a script could
check, and no way to notice that new code reimplements something that already exists.

## What already exists

- `.agent-skills/pp-code-audit/` defines the method: category sweep, architecture-contract checks, a
  five-state reconciliation ledger, and a five-point bar each finding must clear.
- `graphify-out/graph.json` indexes the repo for retrieval.
- CLAUDE.md states the project's rules, many of which are mechanically checkable.
- `ponytail:` and `planned:` markers are the project's only self-declared debt and intent channels.

This design adds the loop around that method. It does not replace it.

## Scope manifest — the single definition of "the repo"

Every count, lint, hash, and clone comparison reads **one versioned manifest**. Nothing types a count
into prose; counts are generated output.

**Included:**

- `Assets/Scripts/**/*.cs`
- `Assets/Graphics/**/*.{shader,compute,hlsl}`
- `Assets/Resources/**/*.compute` — **five files, 1,358 lines.** Missed by revision 1; includes
  `ScatterCull.compute`, which is central to the scatter draw path.
- `Assets/Editor/**/*.cs`
- `Assets/Tests/**/*.cs`
- `ProjectSettings/*.asset`, `Packages/manifest.json`

**Excluded by named rule:** `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, generated
`*.csproj`/`*.sln`, `Assets/Plugins/Wingman/`, `Packages/com.singularitygroup.hotreload/`,
`local-only/`, `graphify-out/`.

The manifest carries a `scopeId`. Changing the manifest changes the id, which invalidates cached
fingerprints rather than silently comparing different repositories.

Revision 1 stated repo-wide facts from ad-hoc greps scoped to `Assets/Scripts/**/*.cs`. Two were
wrong: the compute files above, and "zero TODO markers" — `Assets/Graphics/Shaders/SDFText.shader:13`
contains one. That is the failure this manifest exists to prevent.

## Identity: what was audited

A commit hash is insufficient. The audit records:

| Field | Purpose |
|---|---|
| `auditedHead` | Commit at audit time |
| `scopeId` | Version of the scope manifest |
| `treeFingerprint` | Hash over sorted (path, content-hash) for **every file in scope**, including modified, deleted, and untracked |

Staleness compares all three. Rules:

- Fingerprint differs → **dirty staleness**, reported even with no new commit.
- HEAD is not a descendant of `auditedHead` → report **`diverged`**, never a negative commit count.
- Restoring a tree restores its fingerprint, so staleness is reversible and not monotonic.

## Architecture

Three tiers. Cost and determinism fall as you go down; judgement rises.

### Tier 1 — deterministic, whole scope, every run, no LLM

Runs in seconds, costs nothing, identical every time. Output is **input** to Tier 3.

**Runtime and ownership:** one standalone CLI entry point, `tools/audit/`, implemented in PowerShell 5.1
(already required by this project's environment; no new dependency). Host adapters — pre-commit hook,
SessionStart hook, skill invocation — are thin wrappers that shell out to it. The CLI is the owner;
adapters carry no logic.

**Output contract:** versioned JSON with a declared schema version, stable sort order (rule id, then
path, then line), and stable rule ids (`PP-R001` …). Every rule declares id, severity, scope glob, and
its exact matcher. Adding a rule bumps the schema minor version.

Functions:

1. **Rule lint** — the mechanically checkable CLAUDE.md rules: `RuntimeInitializeOnLoadMethod` outside
   `LoadingManager.CreateInstance`, `DefaultExecutionOrder`, coroutines / `async void` / `Task.Run`,
   shader-global string literals not sourced from `ShaderGlobalIds`, files over 400 lines (trend, not
   defect), change-history comments, `#if false`, new direct `Debug.Log*`.
2. **Clone detector** — normalised token shingles, ranked cross-file matches over a threshold.
3. **Capability index** — public **and internal** helper types with signatures. Internal is included
   because most reusable helpers here are internal to the Planet assembly.
4. **Marker harvest** — every `ponytail:` and `planned:` with file, line, text, and anchor fingerprint.

**Latency ceiling:** the pre-commit adapter runs lint only, and must complete in under 2 seconds on
this scope or it is not wired to the hook. Clone detection and the capability index are audit-time
only.

### Tier 2 — the ledger

`docs/audit/current.md` is the single live answer to "what is open". Dated audits remain as history.

Markdown with a **stable grammar**: one `###` block per finding, fields as a fixed-order definition
list, so it is parseable without being a second artifact. The grammar is versioned alongside the
schema.

Every finding carries the full audit record required by `pp-code-audit`, not a reduced set:

| Field | Notes |
|---|---|
| `id` | Canonical slug, never reused |
| `aliases` | Prior ids with their source doc — e.g. `F01@2026-07-22-consolidated`, `F1@2026-07-25-scatter` |
| `category` | Correctness / Resources / Performance / Architecture / Simplicity / Operations |
| `severity`, `impact`, `effort`, `fixRisk`, `confidence` | As the existing audits carry them |
| `recommendation`, `behaviorNote` | Required by the existing format |
| `status` | Technical state (below) |
| `evidenceState` | `CURRENT` / `STALE` / `PENDING_REVALIDATION` |
| `grade` | CONFIRMED / UNPROVEN / OPINION |
| `anchor` | File plus symbol plus anchor fingerprint. Never a line number |
| `evidence` | Observation, with file:line at time of writing |
| `validation` | The re-runnable procedure, plus `validatedAt` fingerprint |
| `decisionHistory` | Append-only list of (decision, date, reason) |

### Status and decision are separate, and both are closed

**Technical status:** `OPEN`, `PARTIAL`, `RESOLVED`, `REJECTED`, `SUPERSEDED`.

**Owner decision:** `fix`, `defer`, `wontfix` — appended to `decisionHistory`, never overwritten.

| Decision | Status becomes | Revalidated each run? | Surfaces when |
|---|---|---|---|
| none yet | `OPEN` | Yes | Always |
| `fix` | `OPEN` | Yes | Until evidence shows it resolved → `RESOLVED` |
| `defer` | `OPEN` | Yes, cheaply | Anchor fingerprint changes, or on request |
| `wontfix` | `REJECTED` | **Yes — anchor only** | Anchor fingerprint changes (see below) |

`wontfix` does not mean "never look again". It means "do not surface while the thing I judged is
unchanged".

### Tier 3 — orchestrated LLM audit

Runs the three-phase pipeline. Scoped, not whole-repo.

## The three-phase pipeline

### Phase 1 — Question: "is this right?"

Generous; optimise for recall. Capped per category to bound cost.

Inputs: Tier 1 output, the diff since `auditedHead`, the open ledger, the capability index, and the
harvested markers.

**Markers are evidence, never an exclusion filter.** A marked site is still scanned. The marker is
supplied to phases 2 and 3 as context, so a suspicion that contradicts the marker can still be raised.
Revision 1 excluded marked sites before scanning; that is how a reached ceiling becomes invisible.

### Phase 2 — Validation: "test that question"

Every candidate states how it could be proven wrong; that test is then run.

| Class | Validation | Strength |
|---|---|---|
| Dead code | Search whole scope including editor, tests, scenes, prefabs, reflection strings | Definitive |
| Rule violation | Grep plus read the containing method | Definitive |
| **Architecture contract conformance** | Search, call-path read, lifecycle trace, build evidence — CLAUDE.md's contract is checkable | **Definitive** |
| Duplication | Normalised comparison of both implementations | Strong |
| Resource lifetime | Trace every acquire to a release, including cancellation | Semi-definitive |
| Correctness | Construct the concrete failing input | Often definitive |
| Performance | Measure. Batch; only measure survivors of a plausibility screen | Definitive when run |
| Speculative structure | Intent search | Definitive when intent found |
| **Design quality** (cohesion, seam choice, SOLID taste) | Not testable | None |

Revision 1 collapsed the last two rows into one and graded all architecture as opinion. That discarded
the strongest project-specific checks: init ordering, DTO-vs-SO reads, `ShaderGlobalIds` ownership,
banned async APIs are all mechanically verifiable.

**Evidence reuse and invalidation.** Expensive validation (measurement especially) is not re-run every
audit. Each finding stores `validatedAt` — the anchor fingerprint plus the fingerprints of declared
dependency paths. Stored evidence is reused while those are unchanged; otherwise `evidenceState`
becomes `STALE` and the finding is re-validated or reported as `UNPROVEN`. Evidence older than the
manifest's `scopeId` is always stale.

### Phase 3 — Judgement: "is this worth reporting?"

Dedupe, apply the five-point bar, assign the grade, write the ledger.

**Validation evidence is a required input.** A finding with no validation record cannot be graded
`CONFIRMED` — judgement may not upgrade a suspicion on plausibility alone. This is the rule revision 1
stated in prose and then contradicted by allowing judgement to run on candidates directly.

Grades: `CONFIRMED` (validated, evidence attached), `UNPROVEN` (attempted, inconclusive — reported with
the measurement that would settle it), `OPINION` (not falsifiable; separate section).

## Suppression, and why it expires

Suppression binds to a tuple: canonical finding id, marker text, **anchor fingerprint**, and decision
record. It lapses when any of them changes.

- Editing a suppressed symbol re-validates it.
- A `ponytail:` ceiling that has been reached surfaces again, because the code around it moved.
- A `planned:` whose target shipped or was cancelled reports **stale intent**.
- A `wontfix` reason that fits neither marker stays in the ledger only. Not every dismissal is a
  ceiling or a plan.

**The audit never edits source.** Revision 1 said audits change no code *and* that `wontfix` reasons
become source markers. Those contradict. Writing a marker is a separate, Bryan-approved edit, proposed
by the audit and applied outside it.

## Intent validation, and the YAGNI problem

A speculative-structure finding may not be produced from code alone. Mandatory phase 2: search
`docs/design/`, `plans/`, `advisor-plans/`, `.agent-memory/`, CLAUDE.md's architecture section, and git
log.

| Phase 2 result | Outcome |
|---|---|
| Intent documented | Not a finding. Optional micro-finding: no pointer from code to doc |
| No doc, structure coherent | **Undocumented intent** — fix is to record it, not delete |
| No doc, no second use, no story | Genuine YAGNI finding |

**Single-implementation interfaces are excluded entirely.** 35 of 69 have one implementor because
CLAUDE.md mandates interface seams for future modding. That heuristic is off in this repo, not merely
gated.

Applied examples, already in the tree: `BiomeType.Cave`/`Underwater`, `IGravityProvider`,
`IWorldAction`/`WorldActionType`, `WorldActionManager`, both `CelestialEvents` structs.

`IDebugDiagnosticProvider` remains open: nothing implements it, `RegisterDiagnostic` is never called,
and no intent exists anywhere. Awaiting Bryan's delete-or-mark decision.

## Scoping and the recall claim

Duplication matters most when one side is new, and the new side is in the diff. But **diff scoping is
not lossless** — it cannot see a duplicate pair where neither side changed, nor drift in untouched code
under a rule added later.

Honest claim: diff scoping plus whole-scope Tier 1 catches **new-versus-existing duplication and all
mechanically checkable rules across the whole scope**. It does not catch latent old-versus-old
duplication of the semantic kind. The full sweep exists for that.

- **Every run:** revalidate open findings (anchor-cheap), run all of Tier 1 whole-scope.
- **Deep audit:** files changed since `auditedHead`, plus files Tier 1 flagged.
- **Full sweep:** **on explicit request only.** Revision 1 gave two different triggers; this is the one.

## Trigger

A SessionStart hook that **only reports**: compares HEAD and tree fingerprint against the ledger, prints
one line, runs nothing. No session open, no cost.

On-commit LLM auditing is rejected — commits are frequent, audits are slow, nobody is present to answer
questions. The lint-only adapter may be wired pre-commit subject to the 2-second ceiling.

## Model tiers

| Phase | Model | Why |
|---|---|---|
| Question | Sonnet | High volume, wide net, false positives acceptable |
| Validation | Sonnet with tools | Bounded, checkable, evidence-producing |
| Judgement | Opus | Low volume, decides what reaches Bryan |

Roles are defined by **capability requirement**, not model name: judgement requires reading validation
evidence and refusing to upgrade unvalidated suspicions. Any model meeting that bar may hold the role.

## Cost

Costs are **generated, not asserted**. The audit records actual token spend per run in the ledger
header, so the estimate improves from measurement rather than from prose. Revision 1's "20-50k routine,
800k full sweep" figures were derived from a line count, not observed, and are withdrawn until a run
produces real numbers.

## Guard rails

- **Findings only. The audit never changes code.** Every audit opens with that line.
- Bryan decides `fix` / `defer` / `wontfix`; decisions are appended, never overwritten.
- Do not propose a test framework.
- Caustics are editable since 2026-08-11 but fragile — a caustics recommendation must require visual
  verification of caustics, shoreline, and depth blend.
- Never stage, stash, or clean unrelated work; this tree is habitually dirty.

## Rule changes — already applied

Recorded as done, each approved individually before this revision:

1. CLAUDE.md dead-code rule carries the intent exception.
2. CLAUDE.md names `ponytail:` and `planned:` as the only debt/intent markers.
3. CLAUDE.md states the interface-seam/modding rule.
4. Caustics corrected across nine skills to match CLAUDE.md's 2026-08-11 lift.
5. `planned:` / `ponytail:` markers retrofitted to the six known sites.

## Build order

1. **Scope manifest plus Tier 1 lint and marker harvest.** No LLM. Validates the rule set and produces
   the first real counts.
2. **Ledger seeding** — revalidate each historical finding against the current tree *before* carrying
   it in; never copy a stale conclusion. Assign canonical ids, preserve old ids as aliases. Needs
   Bryan's review, since this is where existing findings get their first decision.
3. **Staleness hook** — fingerprint plus HEAD, report-only.
4. **Clone detector and capability index.**
5. **Orchestrator skill and category sub-agents.**

## Open questions

- Clone-detector similarity threshold. Start strict; a noisy list is worse than a short one.
- Whether `SUPERSEDED` needs a pointer to the superseding decision as a required field.
- `IDebugDiagnosticProvider`: delete, or keep with a `planned:` naming mods as the implementor source.

## Response to the Codex review

Every item below was checked against the tree before being accepted. Verified claims are marked; I did
not take the review on faith, and one item is only partly accepted.

### Blockers

| Item | Response | Where |
|---|---|---|
| **B1** commit hash cannot identify a dirty tree | **Accepted.** Verified: 100 tracked-dirty + 19 untracked files against `abb1123`. Commit-count staleness would report zero after a day of work here. | "Identity: what was audited" — `auditedHead` + `scopeId` + `treeFingerprint`, `diverged` for non-ancestors |
| **B2** no state transition for decisions | **Accepted.** Real hole: revision 1 never said what status follows `defer` or `wontfix`. | "Status and decision are separate" — transition table, `decisionHistory` append-only, `evidenceState` added |
| **B3** ledger cannot preserve the required audit record | **Accepted.** Verified against the 2026-08-11 audit's field set; revision 1 dropped category, severity, impact, effort, fix risk, confidence, recommendation, behaviour note. Alias point also correct — `F01` and `F1` collide across documents. | Tier 2 table — full field set plus `aliases` |
| **B4** permanent suppression can hide a changed defect | **Accepted, and it caught a self-contradiction.** Revision 1 said audits never edit code *and* that `wontfix` becomes a source marker. | "Suppression, and why it expires" — markers are evidence not filters; suppression binds to anchor fingerprint; source edits are separate and approved |

### Majors

| Item | Response | Where |
|---|---|---|
| **M1** rules changed before the approval gate | **Accepted with a nuance.** Each edit was approved by Bryan individually, so they were not smuggled — but the document did read as though they were still proposals, and the caustics line contradicted current policy. | Provenance header; "Rule changes — already applied" |
| **M2** missing scope manifest | **Accepted — the most useful catch.** Verified: 5 compute files in `Assets/Resources` (1,358 lines) were outside scope, including `ScatterCull.compute`. Verified: `SDFText.shader:13` holds a live `TODO`, so the "zero TODO" claim was false at repo scope. Marker counts were 13/6, not 11. | "Scope manifest"; all counts now generated |
| **M3** testable contracts graded as opinion | **Accepted.** Architecture splits into contract conformance (checkable) and design quality (taste). | Phase 2 table — two separate rows |
| **M4** no deterministic Tier 1 output contract | **Accepted.** | Tier 1 — versioned JSON, stable rule ids, stable sort |
| **M5** diff scoping's zero-loss claim | **Accepted.** "Loses nothing" was overstated; it cannot see old-versus-old semantic duplication. | "Scoping and the recall claim" — honest bounded claim |
| **M6** seeding can copy stale conclusions | **Accepted.** | Build order step 2 — revalidate before carrying forward |
| **M7** expensive validation needs an invalidation rule | **Accepted.** | Phase 2 — `validatedAt` fingerprints, `evidenceState`, scope-id invalidation |
| **M8** no named runtime or owner | **Accepted.** | Tier 1 — one PowerShell CLI owns the logic, adapters are thin |
| **M9** model pipeline contradicts its evidence gate | **Accepted.** | Phase 3 — validation evidence is a required input; roles defined by capability, not model name |

### Minors

| Item | Response |
|---|---|
| **N1** ledger needs a stable grammar | Accepted — fixed-order definition list, versioned with the schema |
| **N2** cost estimates not reproducible | Accepted, and gone further: the figures are **withdrawn** and will be recorded from real runs |
| **N3** full sweep has two triggers | Accepted — on explicit request only |
| **N4** pre-commit needs a latency ceiling | Accepted — lint-only, under 2 seconds, or it is not wired |

### Finding the review implied but did not name

Nine skills asserted the retired "don't touch caustics" rule that CLAUDE.md lifted on 2026-08-11.
That was live policy conflict, not a documentation nit — an audit run before this fix would have
enforced a withdrawn restriction. Corrected across `pp-code-audit`, `pp-change-control`,
`pp-architecture-contract`, `pp-debugging-playbook`, `pp-diagnostics-and-tooling`,
`pp-failure-archaeology`, `pp-gpu-rendering-reference` (2 files), and `pp-visual-migration-campaign`
(2 files). Historical records of what past campaigns did were left as history.

It argues for a standing Tier 1 rule: **skills must agree with CLAUDE.md.** Drift between them is
mechanically detectable and was worth more than several of the findings above.

---

*The original review follows, preserved as written.*

## Codex feedback — 2026-08-18

**Findings only — no code changed.**

I reviewed branch `harvest-vertical-slice` at `abb1123`. I used the dirty working tree as the source of truth.
This append changes only this design document. I did not run Unity because this review is read-only.

The three-tier direction is sound. The plan is not ready for approval because four blockers can make the
ledger stale, incomplete, or silently wrong.

### Blockers

#### B1 — A commit hash cannot identify the audited dirty tree

- **Category:** Architecture / Correctness
- **Severity:** High
- **Evidence:** The finding stores only `lastAuditedCommit` at line 80. The run stores only
  `lastAuditCommit` at line 82. The hook compares only commits at lines 221-225.
- **Current-tree check:** `git status --short` reported tracked and untracked source changes at review time.
  Examples include `Assets/Scripts/Planet/LakePlanarReflection.cs:14` and
  `Assets/Scripts/Planet/Scatter/ChopFxSystem.cs:7`. Both files are outside commit `abb1123`.
- **Impact:** Two different audited trees can have the same HEAD. The hook can report zero staleness after
  source changes. It also misses untracked files and deleted files.
- **Recommendation:** Store `auditedHead`, a versioned scope ID, and a deterministic tree fingerprint.
  Hash sorted path and content identities for every file in the audit scope. Include modified, deleted, and
  untracked files. Report a non-ancestor commit as `diverged`, not as a negative commit count.
- **Pass conditions:** A tracked edit makes the hook report dirty staleness without a commit. An untracked
  C# file does the same. Restoring either tree restores the prior fingerprint. A non-descendant HEAD reports
  `diverged` and exits successfully.
- **Effort:** M
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes audit metadata and reporting only.

#### B2 — The ledger has no complete state transition for Bryan's decisions

- **Category:** Architecture
- **Severity:** High
- **Evidence:** The lifecycle states at line 74 exclude `WONTFIX` and `DEFERRED`. The separate decision field
  appears at line 79. Every run revalidates every open finding at line 211. A `wontfix` decision permanently
  suppresses the finding at lines 257-259.
- **Impact:** The plan does not define the status after `defer` or `wontfix`. Different agents can leave the
  finding open, mark it rejected, or remove it. Those choices produce different audit results.
- **Recommendation:** Add an explicit transition table. Keep technical status separate from owner decision.
  Add an evidence state such as `CURRENT`, `STALE`, or `PENDING_REVALIDATION`. Record decision history instead
  of overwriting the last decision.
- **Pass conditions:** Each `fix`, `defer`, and `wontfix` choice has one defined transition. Reopening a decision
  preserves its prior date and reason. The routine-run query selects one unambiguous set of findings.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes the ledger schema only.

#### B3 — The proposed ledger cannot preserve the required audit record

- **Category:** Maintainability / Operations
- **Severity:** High
- **Evidence:** The fields at lines 69-80 omit category, severity, impact, effort, fix risk, confidence,
  recommendation, and behavior note. The required audit format lists those fields in
  `.agent-skills/pp-code-audit/SKILL.md:123-136`.
- **Evidence:** Historical IDs are source-local and ambiguous across documents. Examples include
  `docs/audit/2026-07-22-consolidated-code-audit.md:83` (`F01`) and
  `docs/audit/2026-07-25-scatter-audit.md:11` (`F1`). The plan assigns new slugs but defines no alias map.
- **Impact:** `current.md` cannot stand alone as the current answer. Existing links and decisions lose their
  historical identity during seeding.
- **Recommendation:** Add every required audit field. Give each finding a canonical ID and a source-alias list.
  Each alias must contain the source document and prior ID. Keep grade and confidence separate.
- **Pass conditions:** Every `OPEN` or `PARTIAL` prior finding has all required fields. Every old ID resolves to
  one canonical ID. No canonical ID is reused. Existing source links remain traceable.
- **Effort:** M
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes documentation and audit data only.

#### B4 — Permanent marker suppression can hide a changed defect

- **Category:** Correctness / Architecture
- **Severity:** High
- **Evidence:** Markers suppress questions before validation at lines 97-99. Lines 162-170 make that suppression
  permanent. Lines 258-259 require each `wontfix` reason to become either `ponytail:` or `planned:`.
- **Impact:** A `ponytail:` ceiling can become true. A `planned:` target can disappear or ship. A changed method
  can invalidate an old decision while the marker still suppresses inspection. Many `wontfix` reasons fit
  neither marker meaning.
- **Recommendation:** Harvest markers as evidence, but never exclude their sites from scanning. Bind a
  suppression to the canonical finding, marker text, anchor fingerprint, and decision record. Invalidate it
  when those inputs change. Keep general `wontfix` reasons in the ledger. Add source markers only through a
  separate, approved edit.
- **Pass conditions:** Editing a suppressed symbol sends it through validation again. Removing a `planned:`
  target reports stale intent. An unchanged `wontfix` finding stays quiet. The audit run never edits source.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Behavior note:** Preserving. The change prevents stale audit suppression.

### Majors

#### M1 — The plan has already changed rules before its approval gate

- **Category:** Operations / Change control
- **Severity:** High
- **Evidence:** The design says `not approved` at line 3. It lists required `CLAUDE.md` amendments at
  lines 264-269. The dirty tree already contains the marker and dead-code rules at `CLAUDE.md:121-142`.
- **Evidence:** The design keeps caustics flag-only at line 261. The current rule lifts that restriction at
  `CLAUDE.md:153-155`.
- **Impact:** Reviewers cannot tell whether the rule edits are proposals or active policy. The caustics rules
  also give opposite instructions.
- **Recommendation:** Add branch, HEAD, and dirty-tree notes to the document header. Schedule rule changes as
  a separate, post-approval step. Mark already-applied dirty edits as pending Bryan's approval. Use the current
  caustics decision in one place, then update stale skills through their maintenance process.
- **Pass conditions:** The header names the reviewed tree. The build order includes the rule gate. The design,
  `CLAUDE.md`, and project skills contain one caustics policy.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This aligns project instructions.

#### M2 — One missing scope manifest makes the whole-repository claims false

- **Category:** Operations / Maintainability
- **Severity:** Medium
- **Evidence:** Lines 28-30 count 336 C# files, 50,853 C# lines, and 41 shader files with 9,790 lines.
  The current tree has 336 C# files with 50,949 physical lines.
- **Evidence:** The shader subtotal is correct only for `Assets/Graphics/Shaders`. Five project-owned compute
  files also exist at `Assets/Resources/BiomeGrassPlace.compute:1`,
  `Assets/Resources/GpuPlanetTerrain.compute:6`, `Assets/Resources/GrassNearFieldPlace.compute:1`,
  `Assets/Resources/RainParticleUpdate.compute:1`, and `Assets/Resources/ScatterCull.compute:1`.
- **Evidence:** Those files add 1,358 lines. The audit scope therefore contains 46 shader or compute files with
  11,148 lines. `Assets/Graphics/Shaders/SDFText.shader:13` also contains the live `TODO` that line 21 denies.
  The tree has 13 `ponytail:` markers, not 11, plus six `planned:` markers.
- **Impact:** Lint, clone detection, counts, and fingerprints can inspect different repositories. The plan's
  first baseline would be incomplete.
- **Recommendation:** Define one versioned scope manifest. Reuse it for every Tier 1 function and tree hash.
  Include project-owned shaders and computes. Exclude generated, benchmark, and third-party content by named
  rules. Generate volatile counts instead of copying them into prose.
- **Pass conditions:** One inventory command reports 336 C# files and 46 owned shader or compute files on this
  snapshot. Every Tier 1 stage consumes that same inventory. The marker harvest reports 13 and six.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes audit coverage only.

#### M3 — The validation table treats testable contracts as opinion

- **Category:** Architecture
- **Severity:** Medium
- **Evidence:** Line 115 classifies all architecture and SOLID findings as untestable. The project contract
  defines checkable rules at `CLAUDE.md:48-69` and `CLAUDE.md:84-105`.
- **Impact:** Service scope, init ordering, async APIs, shader-global ownership, and file responsibility can
  reach `OPINION` without contract validation. This weakens the strongest project-specific part of the audit.
- **Recommendation:** Split `Architecture contract conformance` from `Design quality`. Validate contract
  conformance with searches, call-path reads, lifecycle traces, and build evidence. Keep subjective SOLID
  advice under `OPINION`.
- **Pass conditions:** Every architecture rule names a validation method. A direct banned API can become
  `CONFIRMED`. A cohesion suggestion without a contract breach remains `OPINION`.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes audit grading only.

#### M4 — Tier 1 lacks a deterministic rule and output contract

- **Category:** Correctness / Operations
- **Severity:** Medium
- **Evidence:** Lines 41-49 list rules but do not define exclusions, baselines, exit codes, output schema, or
  ordering. `new direct UnityEngine.Debug.Log*` and line-count trends both require a comparison baseline.
- **Evidence:** Line 45 does not specify an integer ID created from a raw `Shader.PropertyToID` literal and later
  passed to a global call. Live examples appear at `Assets/Scripts/Showcase/TreeShowcaseSpawner.cs:27-45`.
  The contract covers this flow at `CLAUDE.md:102-105`.
- **Impact:** Two runs can classify the same code differently. A regex can also confuse material properties
  with shader globals. Hook integration cannot distinguish findings from tool failure.
- **Recommendation:** Define a versioned machine output with stable sort order. Define rule IDs, allowed
  exceptions, baseline selection, and separate exit codes for findings and execution failure. Trace raw
  `PropertyToID` values into global calls. Keep material and compute properties local.
- **Pass conditions:** Fixture files cover direct strings, integer-ID flow, approved exceptions, comments, and
  local material properties. Two unchanged runs produce byte-identical output. Tool failure has a unique exit
  code.
- **Effort:** L
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes static analysis only.

#### M5 — Diff scoping does not support the stated zero-loss guarantee

- **Category:** Architecture / Maintainability
- **Severity:** Medium
- **Evidence:** Lines 207-209 say diff scoping loses nothing. The capability index includes only public helpers
  at lines 53-56. The open question at lines 286-287 confirms that internal helpers are initially absent.
- **Impact:** A new semantic duplicate can reuse neither text nor a public symbol name. Old duplicate paths can
  also become harmful after a rule or consumer changes. The clone detector and public index do not guarantee
  discovery.
- **Recommendation:** Replace `loses nothing` with a measured recall claim. Index reusable public and internal
  helpers, extension methods, and project-owned shader functions. Keep full sweeps for drift in unchanged code.
  Tune against a repository fixture set with known semantic and textual duplicates.
- **Pass conditions:** The fixture set includes an internal-helper semantic duplicate and a shader-helper
  duplicate. The routine audit finds both. The plan records measured recall and false-positive counts.
- **Effort:** M
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This widens audit retrieval.

#### M6 — Ledger seeding can copy stale conclusions into the current answer

- **Category:** Correctness / Operations
- **Severity:** Medium
- **Evidence:** Lines 276-277 say to consolidate the four audits. The startup audit describes tree `f6bd37f` at
  `docs/audit/2026-08-11-startup-planet-generation-audit.md:3-16`. Commit `764a0fd` later changed the measured
  startup path.
- **Evidence:** The consolidated audit says wake and action batch 2 was untouched at
  `docs/audit/2026-07-22-consolidated-code-audit.md:361-375`. The dirty tree now deletes both wake classes and
  edits `Assets/Scripts/Core/Services/WorldActionManager.cs:6-9`.
- **Impact:** A mechanical merge can publish obsolete statuses, evidence, and recommendations as current fact.
- **Recommendation:** Revalidate each historical finding before seeding it. Preserve old IDs through aliases.
  Store current evidence separately from historical evidence. Ask Bryan only about missing or ambiguous
  decisions; do not erase recorded decisions.
- **Pass conditions:** Each prior finding has a live-tree status and current evidence. The seed report lists all
  changed conclusions. Bryan receives a separate list containing only unresolved decisions.
- **Effort:** L
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes audit migration only.

#### M7 — Expensive validation cannot run on every routine audit without an invalidation rule

- **Category:** Performance / Operations
- **Severity:** Medium
- **Evidence:** Lines 117-118 require the next audit to rerun stored validation. Line 211 calls every open
  revalidation cheap. Performance validation requires measurement at line 113.
- **Impact:** F10 captures, profiler runs, cancellation exercises, and large builds are not cheap. Repeating all
  evidence on every routine run breaks the cost target at line 217. Skipping them without a rule makes evidence
  silently stale.
- **Recommendation:** Fingerprint each finding's anchor and declared dependency paths. Reuse evidence only while
  those fingerprints and the validation contract remain unchanged. Mark changed evidence
  `PENDING_REVALIDATION`. Reserve required runtime captures for affected findings and full sweeps.
- **Pass conditions:** An unrelated edit does not rerun an expensive capture. An anchor or dependency edit marks
  the evidence stale. The ledger shows the last successful validation and the reason for reuse.
- **Effort:** M
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This controls audit work only.

#### M8 — The tool and hook have no named runtime or owner

- **Category:** Operations / Maintainability
- **Severity:** Medium
- **Evidence:** Line 59 names a new `tools/audit/` location, but that directory does not exist. Lines 221-229
  name a `SessionStart` hook without naming its host, settings file, command, timeout, or failure behavior.
- **Evidence:** `.claude/settings.json:7-27` and `.codex/hooks.json:2-14` contain only `PreToolUse` hooks.
- **Impact:** Implementers can build incompatible runners for Claude, Codex, or a shell. A missing ledger,
  detached HEAD, shallow clone, or path with spaces can break session startup.
- **Recommendation:** Name one standalone CLI entry point and its supported runtime. Keep host adapters thin.
  Specify `.claude/settings.json` and `.codex/hooks.json` behavior separately if both are supported. The report
  hook must time out, print one line, and exit zero on every recoverable state.
- **Pass conditions:** Tests cover missing ledger, clean tree, dirty tree, detached HEAD, divergence, shallow
  history, and a repository path with spaces. Each supported host shows one equivalent status line.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Behavior note:** Preserving. This affects developer tooling only.

#### M9 — The model pipeline contradicts its own evidence gate

- **Category:** Architecture / Operations
- **Severity:** Medium
- **Evidence:** Phase 2 runs tests and measurements at lines 101-118. Lines 241-243 then say the audit has no
  measurement step and judgement alone accepts or rejects findings. Lines 235-239 also hard-code Sonnet and
  Opus in a repository used by several agent hosts.
- **Impact:** An orchestrator cannot determine whether validation evidence or model judgement controls entry.
  A host without those model names cannot execute the stated design.
- **Recommendation:** Make validation evidence a required input to judgement. Define role capabilities instead
  of vendor model names. Put host-specific model mappings in optional local configuration.
- **Pass conditions:** A candidate without a validation result cannot enter the ledger as `CONFIRMED`. The same
  workflow runs with two host configurations. The ledger records the validating command or artifact.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving. This changes orchestration policy only.

### Minors

#### N1 — The live Markdown ledger needs a stable grammar

- **Category:** Maintainability
- **Severity:** Low
- **Evidence:** Lines 63-67 select `docs/audit/current.md` and reject a generated view. The plan does not define
  headings, field delimiters, escaping, atomic updates, or schema versioning.
- **Impact:** Human edits can break the hook or orchestrator parser. A partial write can damage the only current
  ledger.
- **Recommendation:** Publish one Markdown template with a schema version and fixed field order. Parse and
  rewrite through one module. Write to a sibling temporary file, validate it, then replace the ledger.
- **Pass conditions:** Round-trip tests preserve free text, links, and code spans. A failed validation leaves the
  prior ledger unchanged. The project documents `current.md` as the standing-ledger naming exception.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving.

#### N2 — The cost estimates are not reproducible

- **Category:** Operations
- **Severity:** Low
- **Evidence:** Lines 28-30 and 217 give token totals without a tokenizer, included file list, prompt overhead,
  output allowance, or model context policy.
- **Impact:** The estimates cannot support model selection or a budget regression check.
- **Recommendation:** Label them as rough estimates, or generate them from the scope manifest. Record the
  tokenizer, prompt version, input bytes, and output allowance.
- **Pass conditions:** Another operator can reproduce the estimate within the documented rounding rule.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving.

#### N3 — The full-sweep trigger has two different decisions

- **Category:** Operations
- **Severity:** Low
- **Evidence:** Lines 213-215 schedule a full sweep roughly quarterly. Lines 287-288 recommend only on request
  until the routine loop proves itself.
- **Impact:** Two operators can follow the document and choose different schedules.
- **Recommendation:** Keep one rule. A safe initial rule is explicit request or major refactor. Add a quarterly
  trigger only after the routine loop records cost and value.
- **Pass conditions:** The scoping section and open-question resolution name the same trigger.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Behavior note:** Preserving.

#### N4 — Pre-commit use needs a measured latency ceiling

- **Category:** Performance / Operations
- **Severity:** Low
- **Evidence:** Lines 38-39 say all Tier 1 work runs in seconds. Lines 227-229 allow Tier 1 in pre-commit.
  Clone detection and capability indexing have no benchmark or cache design.
- **Impact:** A whole-repository scan can delay every commit and cause users to bypass the hook.
- **Recommendation:** Keep `lint` separate from `scan`. Permit pre-commit integration only for measured commands.
  Record a warm and cold latency ceiling on this repository.
- **Pass conditions:** The selected pre-commit command meets its documented ceiling in three cold and three warm
  runs. Tool failure never destroys or changes the index.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** MED
- **Behavior note:** Preserving.

### Checks that matched the live tree

- `docs/audit/` contains the four dated audits named by the plan.
- `graphify-out/graph.json` is 7,613,062 bytes, which supports the rounded 7.6 MB value at line 19.
- The 336 C# file count is correct.
- The 41-file and 9,790-line shader subtotal is correct for `Assets/Graphics/Shaders` only.
- `ScatterHash`, `CoordinateConverter`, `FaceSpaceCellRangeBuilder`, and `ChunkUvTemplate` are public helpers.
- No existing periodic-audit runner or `SessionStart` audit hook needs replacement.

### Required build order after revision

1. Obtain Bryan's approval for the design and proposed rule changes.
2. Define the scope manifest, tree identity, ledger schema, and runner contract.
3. Build rule lint and marker harvest against the shared manifest.
4. Revalidate all four audits, then seed the canonical ledger and alias map.
5. Add report-only host hooks after the tree identity passes dirty-tree tests.
6. Build and tune clone detection and the capability index with a fixture corpus.
7. Add the orchestrator after every earlier artifact has a stable contract.

This order keeps each cut verifiable. Steps 4-7 depend on the contracts from step 2.
