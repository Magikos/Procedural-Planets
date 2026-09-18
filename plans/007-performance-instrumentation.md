# Plan 007: Measure creature and scatter frame costs

Priority: P1. Effort: S. Risk: LOW. Category: performance diagnostics. Depends on: none.

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

The earlier regression raised Planet.Update to roughly 579 ms per frame.
After repair, the sampled whole frame still averaged 26.9 ms, with 31.7 ms p95.
Counters should identify the remaining work and detect recurrence.

## Scope and current state

Modify `Assets/Scripts/Core/Services/FrameTimingModule.cs`, `Assets/Scripts/Planet/Planet.cs`,
`Assets/Scripts/Planet/Creatures/CreatureView.cs`, `Assets/Scripts/Planet/Scatter/ScatterTileCache.cs`,
and `Assets/Scripts/Planet/Scatter/ScatterRenderer.cs` only where measured work occurs.
Add `Assets/Tests/EditMode/FrameTimingSectionTests.cs` and its meta if needed.

FrameTimingModule currently declares:
```csharp
const int SectionCount = 4;
const int RollingWindowSize = 120;
```
Its existing scope is `using (FrameTimingCounters.Measure(section))`.
The four sections are SurfaceVisibility, Clouds, NearGrass, and ChunkGrass.
GetUninstrumentedCpuStats subtracts every section. Nested sections would double-count elapsed time.

## Steps

1. Map synchronous main-thread ownership of creature simulation, presentation, scatter planning, and commits.
   Use `rg -n "FrameTimingCounters|ReplanPublish|Commit|Sync" Assets/Scripts/Planet Assets/Scripts/Core/Services/FrameTimingModule.cs`.
   Verify every proposed section has one non-overlapping owner.
2. Extend the existing enum, storage sizing, reset, overlay, and capture text together.
   Measure exclusive main-thread sections only. Never hold a scope across an await or send it to a worker.
   Use profiler markers for nested animation and foot-query detail; do not subtract nested markers from frame totals.
   Verify with `git diff --check` and inspect all enum consumers with `rg -n "FrameTimingSection" Assets`.
3. Add regression coverage for enum/storage coverage, reset, and section reporting.
   Keep worker CPU aggregates separate from wall-clock and main-thread values.
   Queue builds and FrameTimingSectionTests. Expected: all selected tests pass.
4. Queue a 120-frame idle, moving-camera, and dense-creature sample.
   Record avg, p95, sample count, creature count, scatter queue size, and whole-frame CPU/GPU.
   Expected: populated sections, no missing enum entries, no nested subtraction.

## Done criteria

- Queued build and fixture checks pass.
- All new sections appear in overlay and capture output.
- Measurements contain 120 completed frames per scenario.
- No new per-frame logging or steady-state allocation from measurement scopes.
- Instrumentation overhead stays below 0.5 ms average in matched repeated samples.
- Update this status and the index only with linked evidence.

## Maintenance

This plan adds diagnosis, not a promised FPS gain.
Append section IDs without changing existing values. Review all fixed-size storage when sections change.
If nested timing is needed later, explicitly model inclusive versus exclusive totals.


