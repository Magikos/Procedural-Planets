# Plan 008: Reduce distant creature pose work

Priority: P1. Effort: M. Risk: MED. Category: performance. Depends on: 007 for measured acceptance.

## Execution contract

Planned on 2026-09-08 against commit `d1e0f62`, with existing uncommitted performance changes.
Status: IMPLEMENTED / VALIDATED — see docs/design/2026-09-08-performance-followup.md for results and validation limits.
Current next action: review recorded evidence. Plan 008 reuses the implementation present at handoff; motion quality remains a human review.

Run from `C:/Users/Bryan/Source/Repos/Magikorp/ProceduralPlanets`.
Run `git status --short` and `git diff --stat d1e0f62..HEAD -- Assets/Scripts Assets/Tests`.
Also read the uncommitted diff for every scoped file. HEAD alone does not describe the planned tree.
Compare the excerpts below with live code. Reconcile overlapping work before editing.
Preserve unrelated changes. Do not stash, reset, commit, push, or switch the shared checkout.
If an isolated branch is needed, use the `codex/` prefix and preserve required uncommitted prerequisites.

Bryan initially queued tests, then explicitly approved implementation and handed over Unity.
The ownership restriction was released for this implementation run.
The queue in `plans/2026-09-08-performance-validation-queue.md` is a written backlog, not a scheduled job.
Do not mark runtime validation complete from a build.

Use existing NUnit EditMode fixtures under `Assets/Tests/EditMode`.
The older skill statement that this project has no test framework is stale; existing fixtures are the current evidence.
Match `ChunkSurfaceRaycastTests.cs`: namespace `ProceduralPlanets.Tests`, NUnit attributes, deterministic inputs, explicit cleanup.
Use existing services and Unity Awaitable if asynchronous work is necessary. Do not add dependencies or Task.Run.
Keep authority-owned simulation separate from presentation. Do not change seeds, biome appearance, density, or quality defaults.

## Verification commands

Planning used source reads and document checks. Implementation and validation results are recorded in the linked design report.
After implementation and Editor release, import new scripts before relying on generated project files.
Run `dotnet build ProceduralPlanets.Core.csproj`, then `dotnet build ProceduralPlanets.Planet.csproj`, serially.
Expected: exit 0, no new compiler warnings or errors. Record existing warnings separately.
Use Unity MCP `run_tests(mode="EditMode", test_names=[...])` with the fixtures listed below.
Poll the returned job with `get_test_job`; require completed results with zero failures and zero skipped selected tests.
Discover the installed tool schema before sending these tool arguments.
Run `git diff --check`: expect exit 0.
After source implementation, run `graphify update .`; do not run it for this document-only planning turn.

## Stop conditions

Stop the affected step and report if an excerpt no longer matches, another agent owns a scoped source file,
a required change exceeds scope, or a verification failure persists after two focused attempts.
Keep useful independent work moving. Do not replace failed parity with looser tolerances.
Do not claim a measured improvement until the queued measurements pass.

## Why this matters

The repaired build still spent 10.5 ms updating 25 creature views in one isolated sample.
This is a lead, not a deterministic baseline. Capture a fixed creature workload before comparing implementations.

## Scope and current state

Scope: `Assets/Scripts/Planet/Creatures/CreatureView.cs`,
`CreatureAnimationView.cs`, `BirdAnimationView.cs`, and `ProceduralPoseRig.cs` in that directory;
the observer argument at `Assets/Scripts/Planet/Planet.cs`;
one small presentation cadence helper in the creature directory, if needed;
`Assets/Tests/EditMode/CreaturePresentationCadenceTests.cs` and meta.
Do not change residency, AI, navigation, attack resolution, audio scheduling, persistence, fish, or ambient swarms.

CreatureView.Sync currently calls every live animation:
```csharp
animation.Tick(c.Velocity, c.Up, Time.deltaTime, _grounding);
```
The view updates root transforms and audio in the same loop.
CreatureAnimationView uses a manual PlayableGraph and AlwaysAnimate.
Its Tick updates temporal blends, evaluates the graph, and runs procedural pose work.

## Design

Separate elapsed presentation time from expensive pose evaluation.
Always update root transforms and authoritative state inputs.
Keep audio and PredatorVision.Sync at their current cadence.
Do not use Renderer.isVisible as the gameplay camera test; other cameras can affect visibility.
Reuse the observer camera already owned by Planet. Build frustum planes once per frame without allocations.

Use conservative body bounds and distance for pose cadence.
Proposed experimental tiers: within 30 m, every frame; visible beyond 30 m, 15 Hz; offscreen beyond 30 m, 5 Hz.
Add hysteresis and stable identity-based staggering. These are validation candidates, not approved final quality defaults.
Force refresh on first spawn, entering the near tier, becoming visible, teleport, and relevant action transitions.
Clear cadence state on removal, regeneration, and disposal.

A skipped evaluation must not freeze logical animation time or replay missed attacks.
Advance cheap animation state every frame; evaluate the current pose only when due.
Do not feed a large accumulated dt blindly into foot springs or integrate every missed frame during catch-up.
On resumption, reset stale foot anchors through the existing pose lifecycle or add one focused reset operation.
Preserve the current full-rate behavior for nearby creatures and action-critical transitions.

## Steps

1. Inspect Tick state updates and ProceduralPoseRig temporal state. Map which calculations advance time and which sample/apply poses.
   Verify using `rg -n "Tick|Evaluate|elapsed|AttackTime|anchor|Reset" Assets/Scripts/Planet/Creatures`.
   Stop if clip progression cannot be separated without altering gameplay state.
2. Add deterministic cadence tests before changing live evaluation.
   Cover stable staggering, no starvation, near bypass, absent camera fallback to full rate, transitions, teleport, removal, and large dt.
   Queue CreaturePresentationCadenceTests; expected: every case passes.
3. Split time advancement from pose evaluation inside existing view classes.
   Preserve full-rate callers, including carcass and ambient-bird uses.
   Add observer selection and lifecycle cleanup in CreatureView.
   Verify `git diff --check` and inspect every caller with `rg -n "animation.Tick|bird.Tick|CreatureAnimationView|BirdAnimationView" Assets/Scripts`.
4. Queue fixed-workload performance and visual checks before selecting final tier distances/rates.
   Compare three 120-frame windows per condition, using the same actors, trajectories, camera, and settings.
   Include offscreen-to-visible, slope walking, attack, rest, drinking, bird landing, and teleport.

## Done criteria

- Builds and cadence fixture pass; existing creature regression fixtures remain green.
- Identical authority state, residency counts, attacks, and audio events for the deterministic fixture.
- Near full-rate scenario has no more than 5% average or p95 CPU regression.
- Mixed-distance scenario reduces presentation average CPU by at least 25%, with p95 not worse.
- No queued catch-up spike, frozen action, stale foot placement, or removed-creature state remains.
- Bryan reviews motion captures before final quality defaults are accepted.
- Record an unmet performance target as inconclusive; do not hide it through reduced creature counts.

## Maintenance

Keep this policy in presentation. Network interpolation and future view systems must share elapsed-time semantics.
Burst ground-query batching remains a follow-up only if measured foot queries still dominate.


