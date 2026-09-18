# Plan 010: Share water coordinates during biome sampling

Priority: P2. Effort: S. Risk: LOW. Category: performance. Depends on: 009 for serial edits and isolated attribution.

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

Biome grid construction requests water state and level at the same direction.
Each request projects that direction to a water-map cell.
Combining the requests removes one repeated projection when level data exists.

## Scope and current state

Modify `Assets/Scripts/Planet/Biomes/WaterBodyMap.cs` and `BiomeMapBaker.cs`.
Add `Assets/Tests/EditMode/WaterBodyCombinedSampleTests.cs` and meta.
Do not change hydrology solving, ocean thresholds, NoWater semantics, or existing external callers.

WaterBodyMap currently uses:
```csharp
public byte Sample(Vector3 direction) => _mask[CellIndex(direction)];
```
LevelAt separately reads:
```csharp
if (_level == null) return fallbackLevel;
float level = _level[CellIndex(direction)];
return level == NoWater ? fallbackLevel : level;
```
BiomeMapBaker.BuildHighResIdGrid calls both for each padded-grid direction.

## Steps

1. Add a combined sampling method with state and level outputs.
   Compute CellIndex once; read the mask; return the existing fallback when levels are absent or NoWater.
   Keep Sample and LevelAt behavior and signatures unchanged.
   Verify `git diff --check` and inspect the combined body for one CellIndex call.
2. Capture WaterBodyMap.Current once for the bake operation and pass/use that reference throughout grid construction.
   If absent, retain state zero and OceanThreshold fallback.
   Do not add locks or cache values across generations. Confirm existing generation lifetime keeps the captured map immutable.
   Verify with `rg -n "WaterBodyMap.Current|CellIndex|LevelAt" Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs Assets/Scripts/Planet/Biomes/WaterBodyMap.cs`.
3. Queue WaterBodyCombinedSampleTests and BiomeMapBakerParityTests.
   Compare combined results with independent Sample and LevelAt results for all cell centers and representative edges/corners.
   Cover null level storage, NoWater, solved lakes, ocean fallback, arbitrary fallback values, and map replacement between bakes.
4. Queue isolated high-resolution-grid timing and three fresh runs before/after.
   Require identical baked IDs, weights, and colors. Keep plan 009's version constant during this comparison.

## Done criteria

- Builds and both fixtures pass.
- Combined path uses one cell projection per sample.
- Output remains identical, including fallback cases.
- No additional allocation per sample.
- No more than 5% median grid-time or total-generation regression across matched runs.
- Report measured savings even if too small to distinguish from noise.

## Maintenance

Keep combined sampling beside existing water accessors.
Any future change to level fallback semantics must cover both APIs in the parity fixture.
Do not expand this into a new water abstraction or unrelated caller migration.


