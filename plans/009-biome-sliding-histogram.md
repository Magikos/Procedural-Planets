# Plan 009: Reuse overlapping biome histograms

Priority: P1. Effort: M. Risk: LOW. Category: performance. Depends on: none; use existing bake timers.

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

The latest fresh generation spent 19.16 seconds in colors, within 42.26 seconds total.
The smoothing pass performs roughly 3.93 billion counter increments across 1,536 map chunks.
Most increments repeat samples from neighboring windows.

## Scope and current state

Modify `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs`.
Add `Assets/Tests/EditMode/BiomeMapBakerParityTests.cs` and meta.
Do not change sampling resolution, kernel radius, biome assignment, LUT values, chunk padding, upload, or shaders.

The current constants are:
```csharp
const int HighResolution = MapResolution * 2;
const int KernelRadius = 12;
```
SampleTopKPerTexel clears counts for each texel, then scans all 25 by 25 samples.
Adjacent centers differ by two cells.
PickTopK and the following normalization/color arithmetic define existing output.

## Steps

1. Preserve the current exhaustive count algorithm in the test fixture as a reference.
   Use the existing test reflection pattern or the assembly's existing internal access; do not expose a new public production API.
   Verify the fixture covers raw IDs, weights, and blended Color32 bytes, not only aggregate counts.
2. Clear and build counts once at the beginning of each output row.
   For each later texel, remove the two outgoing columns and add the two incoming columns.
   Derive bounds from existing center/stride constants. Preserve the id < activeBiomeCount guard.
   Keep PickTopK, tie order, weight remainder assignment, and floating color operation order unchanged.
   Do not carry counts across rows or replace the worker-local buffer with shared mutable storage.
   Verify `git diff --check`; queue BiomeMapBakerParityTests.
3. Compare every output byte against the exhaustive reference.
   Cover uniform IDs, stripes, checkerboards, randomized grids, equal-count ties, active counts 1 and 256,
   out-of-active-range IDs, short LUT fallback, and consecutive bakes with different active counts.
   Run simultaneous bakes to exercise thread-local isolation.
   Include real adjacent chunk edges and cube-face corners with the same seeds.
4. Queue before/after fresh generations and isolated bake measurements.
   Use three runs per variant; report medians and individual runs.
   Existing HighResGridTicks and TopKTicks sum worker CPU time; never label them elapsed seconds.

## Done criteria

- Builds and parity fixture pass with zero byte differences.
- Histogram sample operations fall from 2,560,000 to 443,200 per 64 by 64 chunk.
- Isolated smoothing median improves by at least 20%; no more than 5% median total-generation regression.
- Fresh-run images preserve biome boundaries and cube-face seams.
- No new per-texel allocations or retained per-chunk histogram storage.
- Report actual loading savings separately from the 5.8-fold counting-work reduction.

## Maintenance

If resolution or kernel stride changes, update operation-count expectations and edge fixtures.
The optimization preserves existing output, including current padding behavior.
Do not repair unrelated visual sampling issues in this change.


