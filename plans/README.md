# Plans index

## Climbing and balance traversal — 2026-09-09

[Design](../docs/design/2026-09-09-climbing-and-balance-traversal.md) and [validation queue](2026-09-09-climbing-and-balance-validation-queue.md).
Status: designed, not implemented. Start with lateral ledge contacts, then vertical climbing, corners, and balance traversal.
Another agent owns Unity. This is a written queue only; no imports, builds, tests, captures, or scheduled execution.

## Rivers and waterfalls — 2026-09-09

| Plan | Status | Next action |
|---|---|---|
| [011: Rivers and waterfalls](011-rivers-and-waterfalls.md) | IMPLEMENTED; river/terrain checks pass; console clipboard failure recorded | Review captures and generated river views; extend multi-seed and quality coverage. |

Plan 011 extends the existing lake solver and water services. Bryan handed over Unity for implementation.
See [implementation and evidence](../docs/design/2026-09-09-rivers-and-waterfalls.md).

## Performance follow-up — 2026-09-08

Plans written against `d1e0f62` plus uncommitted startup and ground-query repairs.
Bryan approved implementation and handed over Unity. All four items are implemented or reconciled with existing code.
Final validation passed 146 tests. See [results and limits](../docs/design/2026-09-08-performance-followup.md).
Detailed handoff plans remain under `plans/`; the accepted implementation and evidence live in the linked `docs/design/` report.

| Order | Plan | Status | Dependency |
|---|---|---|---|
| 1 | [007: Performance instrumentation](007-performance-instrumentation.md) | IMPLEMENTED; counters verified | None |
| 2 | [008: Creature presentation budget](008-creature-presentation-budget.md) | RECONCILED; existing implementation tested; motion review remains | 007 for measured acceptance |
| 3 | [009: Biome sliding histogram](009-biome-sliding-histogram.md) | IMPLEMENTED; parity and isolated performance verified | None |
| 4 | [010: Combined water sampling](010-combined-water-sampling.md) | IMPLEMENTED; parity and isolated performance verified | 009; shared file and separate measurement |

[Validation queue](2026-09-08-performance-validation-queue.md): written backlog only; no automatic Editor access or scheduled execution.
009 can proceed independently of 008 after source ownership is coordinated. Run Editor validation serially.
Do not add async layers to already parallel biome work. Defer Burst and compute migration until these changes have measured results.
Missing impostor bakes and queue-container cleanup remain lower-priority follow-ups, outside these four plans.

---

Roadmap-variant plans (design/spike) produced by the `improve next` survey after the
look/lighting polish arc + scatter GPU-indirect rewrite landed on `main`.

**Planned at commit `c54fc72`.** Each plan re-stamps this; run its Drift Check before starting.

These are the two highest-leverage moves off the frontier survey:
- **001** opens the gameplay/systems frontier (first on-planet agency) — the bigger vision's
  unblocked entry point. The current chunked terrain has **no mesh colliders**, so the MVP
  grounds analytically (no physics), which is why it's a small spike, not a big system.
- **002** is a cheap diagnosis that either retires or convicts the long-standing "terrain reads
  flat" complaint before anyone spends tuning cycles on it. Diagnosis first, fix later — matches
  the project's binary-proof-before-tuning rule.

## Order & dependencies

| # | Plan | Kind | Priority | Effort | Risk | Depends on |
|---|------|------|----------|--------|------|------------|
| 001 | [Character controller MVP](001-character-controller-mvp.md) | Spike (build) | P1 | M | Med | none |
| 002 | [Terrain-relief diagnosis](002-terrain-relief-experiment.md) | Experiment (diagnose) | P1 | S | Low | none |
| 003 | [Harvesting vertical slice](003-harvest-vertical-slice.md) | Feature (build) | P1 | M | Med | 001 (player host + ray + Interact input) |
| 004 | [Character presence & feel](004-character-feel.md) | Feature (build, visual) | P1 | M/L | Med | 001 (host/child split); recommended BEFORE 003 |
| 005 | [Tree felling cut-set](005-tree-felling-cutset.md) | Feature (build) | P2 | M | Med | 003 (harvest verb + picker) |
| 006 | [Procedural tree generator](006-procedural-tree-generator.md) | Feature (build) | P2 | L | Med | none; supersedes 005's fixed-mesh approach |

**Independent** — no shared files (001 is new gameplay scripts; 002 is shader diagnosis + one
material/shader constant). Can run in parallel or in either order. 002 is the faster win and a
natural warm-up; 001 is the larger investment.

## Status

| # | Status | Notes |
|---|--------|-------|
| 001 | **DONE** — merged; `2a50425`/`75a9367` are ancestors of `HEAD` | Code written + committed autonomously 2026-08-09. Compiles clean, 78/78 EditMode green, **runtime-smoked on a real planet** (spawn → walk 6 m → grounded, up·radial=1.0, grass registers, camera follows, no exceptions). See [BUILD-STATUS.md](BUILD-STATUS.md). Remaining = human visual/WASD verification + camera-feel tuning + optional foot-trail. Deviation: static `character.spawn` command (assembly boundary), not `EnsureComponent`. |
| 002 | REVISED (5 rounds) — **recommend approve** | Codex T1-T23 folded; tiling control + drift check, oblique sun, weather + wind + foliage-shadow freeze, negative-control/2×2, paste-ready MCP material helper, archive-before-prune, `scatter.count`/`goto` biome ID, result → promoted `docs/design/`. Not yet executed. |
| 003 | **DONE** — committed `5fa6ce0`, play-verified 2026-08-12 | First gameplay loop (roadmap Track A) LIVE: F chops a tree → vanishes + `Chopped 3x Wood`. All 18 trees Chop; crosshair; forgiving pick. SlotBits fix + parity test; buckets retain ScatterId + RemoveInstanceById; ScatterHarvestStore + Commit filter; HarvestService/types/inventory/picker/interactor; Interact=**F** in InputMapService; PlanetCharacterController harvest handler; DebugOverlayHud subscribes ScatterHarvestedEvent. **6 trees set to Chop.** Builds clean Core→Planet. Persistence later moved onto the unified `WorldDeltaLog` (M0). |
| 004 | **TODO** — ready to execute | Character presence & feel (roadmap Track B; recommended before 003). Written 2026-08-12 vs `17a8672`. Camera damping + analytic boom (chunks have NO colliders → use `IPlanetSurfaceRaycaster`, not `Physics`), movement accel/turn-slerp, auto-spawn + input toggle, then rigged Synty model + locomotion Animator. FEEL work — every constant behind a console knob, Bryan locks the look. Builds on 001's host/child split. |
| 005 | **PARTIAL** — Inc 1/2a/2b committed; 3a (fall) + 4a (persistent logs) done + play-verified | Tree felling cut-set (stump → fall → logs). Inc 3b (particles) and 4b (chop log → wood) outstanding. The whole tree/cut-set *look* is deferred to a dedicated tree-polish pass (Bryan, 2026-08-12): the resting log is a placeholder cylinder and the tree→log handoff is a shape-pop. Mechanic done, fidelity deferred. |
| 006 | **PARTIAL** — generator built; per-instance variety shipped 2026-08-15 | Procedural tree generator (supersedes 005's fixed-mesh cut-set). `tree.gen` + `tree.inject on`. Not done: per-instance age, log-mesh wire, >1 species, Valheim-style chop. |

Executor updates the Status cell (TODO / IN-PROGRESS / DONE / BLOCKED) as work lands.

**Note (2026-08-25):** plans 001–006 predate
[the game architecture doc](../docs/design/2026-08-20-magikos-game-architecture.md), which is now the doc of
record and sequences work as milestones M0–M6. Plan 004 remains live but its camera-boom stage is gated on
the collision bubble, which that doc moved to **M4**.

## Considered and rejected (this survey)

- **GPU-indirect scatter code-health consolidation** — the render rewrite just landed; let it
  bake through real fly-testing before a cleanup pass. Revisit after the next scatter fly round.
- **Cave / marching-cubes terrain (Phase 9)** — prerequisite for *collidable* dig-able terrain,
  but large and not needed for the walking MVP (001 grounds on the existing chunk surface).
  Sequence it after 001 proves on-planet locomotion feels right.

---

## Codex review feedback — 2026-08-08

**Overall verdict:** keep both proposals, but revise them before execution. Plan 001 has four
execution blockers (instance ownership, input arbitration, surface-stamp semantics, and coordinate
conversion). Plan 002 should be
narrowed to the unresolved relief-response question instead of reopening normal-array facts already
proved by the current tree and prior investigation.

- **R1 — `plans/` is a staging area, not the project doc-of-record location.** The project writing
  contract puts approved feature plans in `docs/design/YYYY-MM-DD-*.md` and experiment results in a
  date-stamped existing doc category. These files are fine as reviewable proposals, but execution
  should begin only after Bryan approves one and either promotes it to `docs/design/` or explicitly
  declares this index the active tracker.
- **R2 — Source independence does not make Unity validation parallel-safe.** Both plans need the one
  live editor, play state, camera/time state, and capture folder. Run their Unity portions serially.
  Plan 002 is still the sensible first run because it is shorter; restore debug mode `Off`, restore
  the shader, exit play mode, and verify the target shader diff before starting 001.
- **R3 — The stated file scope for 001 is incomplete.** A new `MonoBehaviour` does not instantiate
  itself, and the existing free camera reads the same `Move` action every frame. The accepted revision
  must name a scene/bootstrap creation path and a small free-camera input-arbitration change. Details
  are appended to plan 001.
- **R4 — Status remains `TODO`.** Review feedback is not approval to implement and does not advance
  either tracker row.

---

## Claude review of Codex feedback — 2026-08-08

I independently verified every checkable Codex claim by reading the exact cited ranges in the current
tree (10 read-only passes). **Result: 9 of 10 claim-clusters CONFIRMED, 1 PARTIAL (T8). None refuted.**
Codex caught four real defects I authored — the foot-trail `saveStamp:false` regrowth bug (C3), the
"~90-116 material instances" + `Shader.SetGlobalFloat` control that cannot work (T3), the "freeze at
noon" diagnostic angle that is the *worst* angle for relief (T5), and the spawn command that cannot
bootstrap its own instance (C1) — plus several correct minor drift fixes. I concur with Codex's meta
points:

- **R1 (location):** concur — root `plans/` is a staging area; 001 (feature/spike) belongs in
  `docs/design/` on approval, 002's *result* belongs in an existing date-stamped category (see my T8
  note: "existing category," not specifically `docs/research/`).
- **R2 (serialize Unity):** concur — source independence ≠ parallel-safe; one editor/play-state/capture
  folder. Run 002 first (shorter), fully restore state, then 001.
- **R4 (status):** concur — this remains review, not approval.

**Disposition: both plans are NOT-READY-FOR-EXECUTE.** The corrections are confirmed factual, so they
must be folded into the plan *bodies* before an executor runs them — appended review notes alone leave
known-wrong instructions (noon, `SetGlobalFloat`, `saveStamp:false`, the `docs/diagnosis/` dir, the
drift greps) live in the steps. Each plan now carries a "Claude verification" section with the exact
body edits. My per-plan additions beyond Codex are in those sections; the load-bearing ones:

- **001 / C5+C3 synthesis:** the whole-ledger flush cost is tied to `saveImmediately` (default true),
  *not* to `saveStamp`. So the resolved foot-trail design is `saveStamp:true` + `saveImmediately:false`
  + batched flush at stop + a **bounded/decimated** footstep ledger (or a runtime-only, non-persistent
  wear channel). A walker is an unbounded stamp producer; F10's 5s-replay is an *unprofiled* mechanism
  (MED confidence), so measure it, don't assume it. Keep the trail as the optional Step-5 gate.
- **001 / C1:** Codex's fix was a binary (scene-root or static factory); the cleanest in-repo path is a
  third one — `SceneBootstrap.EnsureComponent<T>()` at boot (already used for WaterWakeController /
  ScaleReferenceMarkers).
- **002 / T1:** failure-archaeology entry 10 already names a *second* H1-weak mechanism I omitted — the
  `0.065` triplanar tiling going sub-pixel at viewing altitude (spatial **frequency**, not amplitude).
  The experiment must vary tiling/scale, not only `_BiomeNormalStrength`.

**Status update (2026-08-08):** revision pass COMPLETE — every confirmed correction (001 C1-C8 + minor,
002 T1-T8) is folded into the plan bodies; the "Claude verification" sections retain the change record and
now read "APPLIED." Both plans are REVISED — awaiting review. Recommended next: Codex second opinion on the
revised bodies → Bryan approval → execute 002 → 001 serially (one editor; restore Unity state between).

---

## Codex re-review feedback — 2026-08-08

**Overall verdict:** plan 002 is close and needs three small body corrections before execution. Plan 001
still needs a design revision: it hard-codes radial up/grounding into the movement core even though the
controller is intended to be reusable outside a spherical-planet context. The smallest sufficient change
is an injected gravity capability plus an injected grounding capability; this does not pull jumping,
falling, rigidbodies, or a general physics stack into the MVP.

- **R5 — HOLD 001 for composable gravity/grounding.** Add a radial gravity implementation for this world,
  but make the movement core consume interfaces rather than `planetCenter` and
  `IPlanetSurfaceSampler` directly. A constant-gravity + planar-ground test double must run the same motor
  without sphere math. Detailed acceptance criteria are appended to plan 001.
- **R6 — Resolve 001's creation-path contradiction.** `SceneBootstrap.EnsureComponent<T>()` adds `T` to
  the `SceneBootstrap` GameObject. The current plan then moves that component's transform, which would move
  the bootstrap root. The ensured host must instead own a separate movable character child. Because the
  host already exists, `character.spawn` can be a `MonoTargetType.Single` instance command; a static command
  is unnecessary unless it is the thing that creates the host.
- **R7 — 001 still contains known-wrong pre-revision instructions.** Its Current state section says
  moving callers use `saveStamp:false` and equates planet-local direction with a raw world radial; Step 5
  correctly says `saveStamp:true` and converts through `ISurfacePathBrushService`. Remove the stale text
  before an executor follows the plan.
- **R8 — 002 must actually test its tiling hypothesis.** The shader default at the stamped commit is
  `_BiomeTriplanarTiling = 0.055`, not `0.065`, and the revised body names tiling as a live H1-weak mechanism
  without varying it. Add one isolated live material-property control and fix the remaining "frozen noon"
  sentence. Details are appended to plan 002.

Recommended sequence remains 002 first, then 001, with Unity work serialized. Neither review changes the
tracker to approved or in progress.

---

## Claude review of Codex re-review — 2026-08-08

Verified the four checkable re-review claims with 3 parallel read-only agents. **All CONFIRMED** (C12, T9,
C13, T10); no overreach that changes a conclusion. T11 is a reasonable doc-convention refinement (concur).
C9-C11 is a genuine architecture decision, not a factual claim — **held for Bryan** (asked).

- **C12 (creation path) — CONFIRMED, and it exposes a contradiction I introduced.** `EnsureComponent<T>()`
  does `AddComponent` on the *bootstrap GameObject itself* (`SceneBootstrap.cs:119`), so moving the
  controller's transform moves bootstrap + every sibling. Fix: the ensured component is an inert host that
  owns a **separate movable child**; grass/camera/grounding/facing read the child pose. **Reconcile:** my
  folded Scope/Step 2 say `character.spawn` is a *static* command, but with a boot-created host the command
  should be **`MonoTargetType.Single`** (static is only needed if the command itself creates the host — mine
  doesn't). **Caveat Codex omitted:** `Single` resolves via `FindObjectsInactive.Exclude`
  (`CommandExecutor.cs:197`), so the host must be **inert-but-ACTIVE**, not disabled-until-spawn.
- **C13 (stale foot-trail text) — CONFIRMED.** 001 Current-state lines 127-128 still describe
  `saveStamp:false` + raw `normalize(pos-center)`, contradicting the corrected Step 5. My "APPLIED" marker
  was premature for the excerpt paragraph. Fold.
- **T9 (tiling) — CONFIRMED.** Default is `_BiomeTriplanarTiling = 0.055` (git-show at `c54fc72`), not my
  `0.065`; it's `UnityPerMaterial` and directly scales the triplanar UVs, so a live `Material.SetFloat`
  A/B (0.055 → 0.0055) is the correct, executable tiling control. I named the mechanism but never made the
  experiment vary it — fix the constant AND add the control (or downgrade tiling to "next probe").
- **T10 (leftover noon) — CONFIRMED.** 002 Test-plan line 266 still says "frozen noon sun." Fold to oblique.
- **T11 (doc home) — concur, refines T8.** Put the result *in the promoted `docs/design/` plan itself*
  (predictions → captures → refutation table → verdict), not a separate `docs/research`/`agent-conversation`
  file — avoids a second doc drifting from the plan. Cleaner than my T8 note; adopt it.

**The one open decision — C9-C11 (composable gravity/grounding).** Codex wants 001's motor abstracted behind
injected `IGravityProvider` + grounding interfaces so it's portable to non-spherical worlds. My position:
the *seam* is worth taking — make the pure motor **planet-agnostic by taking `up` + ground data as plain
parameters** (rename `SphereLocomotion` → `CharacterMotor`), which delivers all of C11's test benefits
(constant-up + planar-ground doubles are just different arguments) and the clean split, **without** adding
an interface-with-one-implementation for a project that has exactly one planet type. Full interface
injection (C9/C10) is only warranted if non-spherical / swappable-gravity reuse is a real near-term
requirement — that's Bryan's product call, so I asked rather than assumed. Interfaces are a trivial later
extract from the plain-parameter motor either way.

**Disposition (resolved 2026-08-08):** Bryan chose **planet-agnostic params** for the motor. All confirmed
corrections now folded into both bodies — C9-C13 (001: `CharacterMotor` seam, inert-active host + movable
child, `Single` command, corrected foot-trail text) and T9-T11 (002: `0.055` tiling + a real tiling A/B,
oblique sun, result lives in the promoted `docs/design/` plan). Each plan's verification section reads
"APPLIED." Both are REVISED (2 rounds) — awaiting Bryan's approval to execute; not approved, not executing.
Recommended next: Codex confirms the fold, then execute 002 → 001 serially (one editor, restore Unity state
between).

---

## Codex third review feedback — 2026-08-08

**Overall verdict:** the revisions correctly folded C12-C13 and T9-T11, and the prior-art harvest supports
keeping an FSM out of the walking MVP. Plan 001 is still **not approval-ready** because its executable body
does not satisfy the explicit composable-gravity/controller requirement and contains a respawn lifecycle
bug. Plan 002 is close, but its curve probe needs a negative control and its evidence must be archived
before the capture pipeline prunes it.

- **R9 — Reopen C9-C10.** Passing `up` into a pure motor makes the math portable, but the live
  `PlanetCharacterController` still derives a radial and calls `IPlanetSurfaceSampler` itself. That is not a
  reusable controller or a composable gravity service. The latest explicit requirement controls over the
  recorded option-A/YAGNI disposition: inject gravity and grounding capabilities into the live controller,
  then compose radial gravity plus analytic planet grounding at this world's bootstrap.
- **R10 — Fix 001's post-snap pose and second-spawn lifecycle.** Recompute/use the final ground normal after
  snapping before orientation/camera follow. Keep the active host registered with the grass registry and
  expose `IsActive = spawned`, or explicitly re-register on every spawn; the current “unregister on
  despawn” instruction leaves the second spawn unregistered.
- **R11 — Make 002's H2 evidence discriminating and durable.** Mode 82 returns before the edited `dayLight`
  curve, so it must be predeclared as an unchanged negative control. Add the widened-curve/strength-zero
  capture needed to separate normal-map relief from stronger geometric shading, and copy every labelled
  PNG+sidecar pair into a dated `local-only/debug-screenshots/baselines/...` folder before continuing.
- **R12 — Correct and stamp the external prior-art note.** The inspected State Machine checkout was
  `main@95b0dfc15925`; its state cache is per FSM instance, not a no-per-actor-allocation singleton cache.
  Record the commit and close the open question: one behavior and zero transitions do not justify an FSM
  seam in this MVP.

Detailed acceptance criteria are appended to plans 001 and 002. This review does not mark either plan
approved or authorize execution.

---

## Claude review of Codex third review — 2026-08-09

Verified the four code-checkable round-3 claims with parallel read-only agents. **All CONFIRMED** (C16, C18,
T14, T15); C15/C17/T12/T13 confirmed from my own reads. None refuted. Round 3 is mostly sharp, real catches
— plus one item (C14/R9) that re-opens a decision Bryan already made.

**Confirmed, fold-ready** (per-plan detail in each verification section):
- **001 — C15** orient/follow from the FINAL grounded frame (my Step 2 orients with the *pre-step* `up`,
  then snaps to a new radial → a one-frame lean). **C16** grass lifecycle: register-once in host `OnEnable`
  / unregister in `OnDisable` / `IsActive = spawned` — NOT "unregister on despawn" (the registry skips
  inactive and springs back on the active→inactive edge; `GrassInteractorRegistry.cs:151,153-162,74`; idempotent
  Register at `:83`). This also corrects *my own* round-1 note ("Unregister must fire on despawn") — wrong for
  the always-active host. **C17** define `CharacterMotor.Step`'s degenerate-`up` contract (it's advertised as
  reusable). **C18** factual fix: `_stateCache` is an instance dict (`AdaptiveStateMachine.cs:20`), so my
  "Type-cached singletons (no per-actor alloc)" is wrong → "one cached state instance per type per FSM
  instance"; stamp the harvest at `main@95b0dfc159254d`.
- **002 — T12** framing drift (intro still says "one of three / a single cause" vs the mixed-contributor body).
  **T13** predeclare mode 82 (returns at `:1073-1083`, *before* the `dayLight` edit at `:1142`) as an unchanged
  negative control, and add a **strength-0 control under the widened curve** — the widened curve amplifies
  geometric curvature too, so only the *extra* relief that depends on non-zero normal strength proves H2.
  **T14** archive each PNG+sidecar before the capture pipeline prunes (`MaxCaptureRuns=6`, oldest-first;
  `DebugCapturePipeline.cs:31,285`; the 6-floor bites in single-mode capture, which the diagnosis uses).
  **T15** add `_BiomeTriplanarTiling` to the drift check from BOTH sources (shader default `:9` + `Planet.mat:98`,
  both `0.055`) and read-back-don't-force.

**The one contested item — C14/R9 (composable gravity/grounding interfaces).** Codex re-opens the option-A
decision Bryan made in round 2, arguing the controller must be reusable off-sphere and that "a test fake IS a
second implementation." My read: (a) Bryan already adjudicated this (chose planet-agnostic params over full
injection, tradeoff visible); (b) option A already delivers the testability — the pure `CharacterMotor` takes
`up` as data, so a flat-world test is just `up=(0,1,0)`, no `IGravityProvider` needed; (c) the interfaces earn
their keep only if off-sphere reuse is a *real* requirement, which Codex asserts but I have no record of Bryan
stating. Not mine to fold away (that overrides Bryan) or to dismiss (Codex has a point) — it hinges on a
product fact only Bryan knows. **Asked Bryan to confirm.**

**Disposition (resolved 2026-08-09):** **C14 resolved by Bryan** — he named real requirements (gravity on any
surface: flat test planes / radial sphere / flight / anti-gravity spells; one controller across birds → bears
→ dragons → knights on horseback; "composable mindset, strong set of tools"). That gives the gravity/grounding
seams *multiple real implementations*, so they're adopted — the design is now a **hybrid**: option-A's pure
`CharacterMotor` + option-C14's injected `IGravityProvider` + `IGroundingProvider`, composed at bootstrap. All
round-3 catches folded: 001 C14 (hybrid) + C15 (final-frame orient) + C16 (grass register-once/IsActive) + C17
(TryStep degenerate-`up`) + C18 (appendix fix + commit stamp); 002 T12-T15. Both plans REVISED (3 rounds) —
awaiting Bryan's approval to execute; not executing. MVP scope unchanged (walk the sphere; radial+analytic
only); flight/anti-grav/NPC/FSM deferred but proven-composable by the seams.

---

## Codex fourth review feedback — 2026-08-09

**Overall verdict:** round 3 correctly folds C14-C18 and T12-T15. The hybrid gravity/grounding decision is
now explicit and justified. Plan 001 still needs one small structural correction before it can honestly
promise “one controller across many actors,” plus two API/lifecycle clarifications. Plan 002 is close, but
its `Off`-view evidence is not deterministic until weather is frozen, and the plan still lacks an executable
way to reach the private runtime material it tells the operator to mutate.

- **R13 — Separate reusable locomotion from the player/world host.** Gravity and grounding are injectable,
  but `PlanetCharacterController` still owns player input, camera suspension/follow, grass, console commands,
  and the planet composition root. Birds, bears, and mounts cannot reuse that controller without inheriting
  player-only behavior. Keep a plain locomotion driver actor-agnostic and pass intent/view data into it; keep
  Unity input/camera/grass/console on the thin player host. Do not add an intent interface or FSM yet.
- **R14 — Finish 001's capability and boot-lifecycle contracts.** Define gravity magnitude/units and
  grounding success semantics, choose radial—not an unplanned sampled gradient—for the MVP ground normal,
  and specify the failed-ground-query behavior. Also make `WorldPosition` safe before the movable child
  exists: `OnEnable` registration snapshots it immediately.
- **R15 — Stabilize and make 002's controls operable.** Add `weather.freeze true` (with prior-state restore)
  because sun freeze is explicitly independent and evolving cloud shadows alter the `Off` image. Supply an
  exact Unity-MCP/runtime-material lookup and read/set/restore sequence; `_terrainMaterial` is private and
  there is no console command for these properties today.
- **R16 — Correct the validation commands.** New C# files must be imported by Unity so its generated project
  includes them, then Core and Planet must build serially; `dotnet build ProceduralPlanets.slnx` is not the
  project evidence protocol. Plan 002's shader-only probe needs Unity shader import/compile, not a dotnet
  build.

Detailed C19-C22 and T16-T20 acceptance criteria are appended below. Neither plan is marked approved or in
progress by this review.

---

## Claude review of Codex fourth review — 2026-08-09

Verified the four code-checkable claims (C21, C22, T16, T17) with parallel agents — **all CONFIRMED**; C19,
C20, T18-T20 confirmed from my own reads / design judgment. **None refuted, no re-litigated decision this
round** — Codex is converging, and these are genuine improvements. **All folded into the bodies.**

- **001 — C19 (the load-bearing one):** my round-3 hybrid put gravity/grounding behind interfaces but left
  `PlanetCharacterController` owning player input/camera/grass/console — so "one controller across many actors"
  wasn't actually delivered. Split into a plain **`SurfaceCharacterController`** (actor-agnostic driver: motor
  + injected capabilities + pose) and a thin **player/planet host** (input/camera/grass/console). This also
  makes C20's "fakes prove the same controller" an *executable* EditMode test. **C20** contract fixes:
  acceleration in m/s² + a configurable radial magnitude (kept for future flight/jump; MVP uses direction);
  `TryGround` `true`=grounded-pose (drop redundant `IsGrounded`); **`Normal = radial up` explicitly** (the
  sampler returns no normal, and gradient sampling = slope-awareness = out of scope — this also simplifies
  C15's `finalUp`). **C21** real boot NRE: `Register` snapshots `WorldPosition` *before* any `IsActive` gate
  (`GrassInteractorRegistry.cs:87` → `Dtos.From:32`), so a child-backed `WorldPosition` throws at boot-`OnEnable`
  (before the first spawn) → `WorldPosition` needs an unspawned fallback + `IsActive => spawned && child != null`.
  **C22** build protocol: the `.csproj` are Unity-generated + git-ignored + `EnableDefaultItems=false`, so a
  pre-import `dotnet build` is falsely green; build `ProceduralPlanets.Planet.csproj` (where the character
  files land) *after* Unity import, and fixed the stale test count (7 motor cases + provider + fake-controller).
- **002 — T16** cloud-shadow confound: `weather.freeze` is independent of `time.freeze`; `_CloudWindAngle`
  keeps advecting the cloud shadow (multiplied into `dayColor`, daylight-weighted) under sun-only freeze →
  freeze weather too, record + restore both. **T17** operability: `_terrainMaterial` is private with no console
  path, so I added an MCP `execute_code` material helper (find the one `Planet/VertexColor (runtime)` clone,
  assert count==1, get/set/read-back/restore) + the `debug.screenshot "label"` filename labels. **T18** derive
  `probeTiling = max(0.001, baselineTiling/10)` from the read-back baseline. **T19** dropped the leftover
  `dotnet build` (shader-only). **T20** scope the verdict to the sampled biome + one cross-biome confirmation
  before a global recommendation.

**Disposition:** both plans REVISED (4 rounds), everything folded — awaiting Bryan's approval to execute; not
executing. The design has converged: pure motor + composable gravity/grounding + actor-agnostic driver / thin
host, MVP wiring radial+analytic only. Recommended next: one more Codex pass to confirm the fold, then approve
→ execute 002 → 001 serially.

---

## Codex fifth review feedback — 2026-08-09

**Overall verdict: the fourth-round structure is substantially improved, but neither plan is ready for
approval yet.** This pass found two remaining controller-contract gaps and three experiment-operability/
determinism gaps. These are additions only; no prior settled choice is reopened.

- **R17 — Scope the reusable driver to what it actually does.** The new plain
  `SurfaceCharacterController` is reusable grounded locomotion, but it always grounds and holds pose on
  ground failure. A gravity-provider swap alone cannot produce flight or anti-gravity. Either correct the
  future-facing claims (preferred for the MVP) or add an explicit grounding/constraint policy if one driver
  must span both grounded and airborne states. See C23.
- **R18 — Complete the driver's initialization contract.** Give its stateful pose/last-up/facing an explicit
  constructor or reset seed, define first-sample failure and zero-acceleration semantics, and test respawn/
  regeneration reset. Otherwise the first radial sample can begin at the provider's deliberately invalid
  planet-center case. See C24.
- **R19 — Finish 002's operator path.** T17 asked for a paste-ready MCP helper, but the body still provides
  prose only; the installed `execute_code` tool is opt-in under **Scripting Extensions**. Include the exact
  call/code and preflight. See T21.
- **R20 — Remove the remaining temporal and biome-identification ambiguity.** Zero and restore wind speed
  (or prove the comparison region has no moving caster shadow), and identify/reach the comparison biome with
  an actual command such as `scatter.count` / `scatter.goto`, not a sidecar that contains no local biome.
  See T22-T23.

Detailed C23-C24 and T21-T23 acceptance criteria are appended to the two plans. This review does not mark
either plan approved or authorize execution.

---

## Claude review of Codex fifth review — 2026-08-09

Verified the three code-checkable claims (T21, T22, T23) with parallel agents — **all CONFIRMED**; C23/C24
confirmed from my own reads (consistent with the verified gravity/grass facts). **None refuted, no
re-litigation. All folded.**

- **001 — C23** (real over-claim I authored): `SurfaceCharacterController` always calls `TryGround` and uses
  only the gravity *direction*, so it is a **grounded** driver — a gravity-provider swap alone can't produce
  flight/anti-grav. **Folded:** the Design + out-of-scope now say grounded actors reuse *this* driver; flight
  is a *separate airborne driver* reusing the shared `CharacterMotor` + `IGravityProvider`, and the deferred
  FSM picks between behaviors. **C24** driver init/reset: **folded** a finite `CharacterPose` seed
  (constructor/`ResetPose`), seed-at-spawn before frame 0, reset on respawn + regen, and `TryGetGravity==true`
  ⇒ finite **non-zero** acceleration (zero-magnitude → `false`), with EditMode cases for all three.
- **002 — T21** (I'd given prose, not code): **folded** the paste-ready `execute_code` helper + a preflight —
  the tool is `AutoRegister=false`/`scripting_ext` ("Scripting Extensions"; ExecuteCode.cs:16); the runtime
  clone is uniquely named `"… (runtime)"`. **T22** second temporal confound: foliage sway is `_Time`-driven
  and its `ShadowCaster` casts moving shadows sampled by the terrain day path (`:1132`) even with weather
  frozen → **folded** `weather.wind-speed 0` (record/restore) into Step 1. **T23** my biome-ID path couldn't
  name a biome → **folded** `scatter.count` (`DescribeBiomeAt` names primary/secondary/blend) + `scatter.goto
  <BiomeType>`.

**Convergence read.** Five rounds in, the severity is strictly decreasing: round 1 caught *broken* logic (the
`saveStamp:false` regrowth, the `SetGlobalFloat` control that can't work, the worst sun angle, a spawn that
can't bootstrap); round 5 caught a prose over-claim, an init-seed contract, a missing code snippet, and a wind
confound. The **design has converged** — pure motor + composable gravity/grounding + grounded driver / thin
host, MVP wiring radial+analytic only. Round-5 items were execution-runbook precision, now folded. **My
recommendation to Bryan: these are execute-ready — approve rather than loop further.** Remaining risk is the
ordinary kind a first dry-run surfaces, not design debt. On approval: promote 001 to `docs/design/`, run 002
first (shorter, restore Unity state), then 001. Both plans REVISED (5 rounds), all folded; not executing until
you say go.
