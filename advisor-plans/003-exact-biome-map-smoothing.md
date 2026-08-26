# Plan 003: Replace the biome-map rescan with exact rolling smoothing

> **Executor instructions**: Follow this plan in order. The 25×25 smoothing window,
> top-four ordering, and byte output are a preservation contract, not tuning knobs. First
> implement the simple managed rolling histogram and measure it. Execute the optional
> Burst step only if the measured map-bake median remains above five seconds. Stop on any
> STOP condition and update row 003 in `advisor-plans/README.md` when done.
>
> **Go/no-go (run before anything else)**: this plan speeds up `SampleTopKPerTexel` and
> nothing else. Read the `hrGridCpu` and `topKCpu` medians produced by Plan 001 Step 5b and
> compute `topKCpu / (hrGridCpu + topKCpu)`. Use that share, not a comparison against
> `mapBake` — those two are summed worker CPU time while `mapBake` is wall clock. If the
> share is under 0.5, **STOP and re-rank**: even a perfect 5.78×
> reduction of `topK` cannot reach the 5-second gate, and the byte-parity test corpus in
> Step 2 would be built for a minority win. In that case the dominant cost is
> `BuildHighResIdGrid`'s ~35.5 million domain-warped `EvaluatePrimaryId` + `LakeMask`
> samples, which needs a different plan than this one.
>
> **Drift check (run first)** (baseline re-pinned from `fab754b` to `fdbc76c` on
> 2026-08-15; no in-scope file for this plan changed between those commits, other than the
> two timers Plan 001 Step 5b adds to `BiomeMapBaker.cs`):
> `git diff --stat fdbc76c..HEAD -- Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs Assets/Scripts/Planet/Surface/BiomeAtlasService.cs Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs Assets/Scripts/Planet/Surface/PlanetChunk.cs Assets/Tests/EditMode`
>
> Plans 001 and 002 are expected to have changed several files. Reconcile those known
> changes against the current-state and input contracts below. A semantic mismatch is a
> STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW-MED
- **Depends on**: `advisor-plans/001-instrument-startup-generation.md` (specifically Step
  5b's `hrGrid` / `topK` split — this plan has no valid gate without it),
  `advisor-plans/002-burst-climate-vertex-bake.md`
- **Category**: perf / tests
- **Planned at**: commit `fab754b`, 2026-08-12

## Why this matters

The audited face-atlas configuration bakes 1,536 leaf maps. Each 64×64 output map texel
currently rescans a 25×25 neighborhood from a padded high-resolution ID grid. That is:

`1,536 × 64 × 64 × 625 = 3,932,160,000` count updates per generation.

Adjacent output texels advance by only two high-resolution cells and share 23 of their 25
columns. Maintaining a rolling 256-bin histogram reduces a row from
`64 × 625 = 40,000` count updates to `625 + 63 × 100 = 6,925`, about 5.8× less core
work, without changing a single sampled cell or output byte.

## Current state

- `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs` constants are load-bearing:

```csharp
const int HighResolution = MapResolution * 2; // 128
const int KernelRadius = 12;                  // 25x25
const int KernelSamples = 625;
const int PaddedResolution = 152;
public const int TopK = 4;
```

- `BuildHighResIdGrid` samples beyond the chunk boundary in face space, reads the cleaned
  assignment field with `EvaluatePrimaryId`, samples `LakeMask`, and applies water,
  beach, lake, mountain, and snowy-mountain overrides through
  `BiomeLookupEvaluator.ResolveFromLandBiomes`.
- `SampleTopKPerTexel` currently clears a 256-entry thread-local count buffer for every
  output texel and scans all 625 cells from scratch.
- `PickTopK` scans IDs in ascending order and only replaces slots on strict `>` counts.
  That makes lower IDs win equal-count ties. Preserve this exact ordering.
- Weights use integer division by the selected top-four total, then assign the remainder
  to the first participating slot. Empty top-K slots remain zero.
- Blended color is computed from the normalized integer weights and the existing gamma-
  space `Color[]` LUT, then converted to `Color32`.
- Plan 002 is required to deliver full-precision climate slices to
  `BuildHighResIdGrid`; smoothing must not force early climate quantization.

## Preservation contract

- Keep `MapResolution=64`, `HighResolution=128`, `KernelRadius=12`, padded size 152,
  `TopK=4`, center mapping, and all padding samples unchanged.
- Keep static climate, cleaned Voronoi primary atlas, five cleanup iterations, lake mask,
  and elevation overrides unchanged.
- Keep invalid-ID behavior: count an ID only when `id < activeBiomeCount`; use 256 slots
  when lookup/LUT snapshots are inconsistent.
- Keep strict-`>` top-K tie ordering, integer normalization, remainder target, and LUT
  fallback exactly.
- Managed rolling output must be byte-identical to an independent reference rescan for
  IDs, weights, and blended colors.
- Stop once the three-run map-bake median is <=5,000 ms. Burst is conditional, not a
  deliverable for its own sake.
- If Burst is needed, Burst output must also be byte-identical. Do not accept a visual-only
  comparison for categorical IDs/weights.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Planet build | `dotnet build ProceduralPlanets.Planet.csproj --no-restore` | exit 0, 0 errors after Unity import |
| Focused tests | Unity Test Runner / MCP `run_tests`, EditMode filter `BiomeMapBakerTests` | all focused tests pass |
| Full suite | Unity Test Runner / MCP `run_tests`, EditMode | 0 failures; report actual count |
| Confirm constants | `rg -n "HighResolution|KernelRadius|KernelSamples|PaddedResolution|TopK" Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs` | values remain 128, 12, 625, 152, 4 |
| Inspect runtime proof | `$editorLog = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"; rg "Biome color timings|Biome atlas checksum|Generation timings" $editorLog` | complete same-seed records |
| Refresh graph | `graphify update .` | exit 0, or record known timeout |

## Scope

**Always in scope:**

- `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs`
- `Assets/Tests/EditMode/BiomeMapBakerTests.cs` (create)
- Unity-generated `.meta` for the test file
- `advisor-plans/README.md` (status only)

**Conditionally in scope only if the managed result misses the 5-second gate:**

- `Assets/Scripts/Planet/Biomes/BiomeTopKJob.cs` (create)
- `Assets/Scripts/Planet/Surface/BiomeAtlasService.cs`
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`
- Unity-generated `.meta` for the job file

**Out of scope:**

- Vertical rolling reuse, integral histograms, GPU compute, texture resolution changes,
  kernel/radius changes, fewer leaves, fewer biome seeds, fewer cleanup iterations, or a
  different blend algorithm.
- Porting `BuildHighResIdGrid` to Burst. It is not the audited 3.93-billion-update hotspot;
  measure the rolling pass first.
- Reworking atlas stitching/upload, runtime rebakes, climate formulas, Voronoi generation,
  lake mask generation, or weather.
- A general pooling/job framework or package addition.

## Git workflow

- Suggested branch from completed 002: `perf/exact-biome-map-smoothing`.
- Commit the managed exact optimization and tests first. Add a second commit only if the
  Burst gate is reached.
- Example messages: `Perf: roll biome smoothing histograms`; conditional
  `Perf: Burst biome top-k smoothing`.
- Do not push/open a PR unless instructed.

## Steps

### Step 1: Record the Plan 002 baseline and preservation oracle

From three fresh same-seed Plan 002 runs, record:

- Phase B `hrGridCpu` and `topKCpu` medians and the `topKCpu` share. Re-check the go/no-go
  at the top of this plan against post-002 numbers, not just Plan 001's — Plan 002 changes
  what feeds the baker, and the high-res grid reads climate through it;
- Phase B `mapBake` and total `colors` median;
- full planet median;
- atlas ID, weight, and blended checksums;
- seed, cleanup changes, distinct biome count, map target count, atlas resolution;
- normal render plus debug modes 78 (map primary), 79 (blend), and 80 (flat color) at a
  multi-biome junction and at a lake/shore.

Save lines to `local-only/perf/startup-generation/003-before.txt`.

**Verify**: all three same-seed checksums match. If not, STOP.

### Step 2: Write an independent byte-parity test before changing production code

Create `Assets/Tests/EditMode/BiomeMapBakerTests.cs`. Plan 002's
`InternalsVisibleTo("ProceduralPlanets.Tests.EditMode")` should allow direct testing of an
internal smoothing entry point. If it does not exist, add it in the already-approved
`AssemblyInfo.cs`; do not make smoothing APIs public.

In the test file, implement a small independent reference rescan matching the current
algorithm. Keep it in the test assembly, not production. It must:

- scan the same 625 cells for each output texel;
- ignore IDs outside `activeBiomeCount`;
- reproduce ascending-ID strict-`>` top-K selection;
- reproduce integer weights/remainder;
- reproduce blended `Color32` output.

Add cases for:

1. all-zero grid, active count 1;
2. one uniform nonzero biome;
3. checkerboard/tie-heavy grid, proving lower-ID tie behavior;
4. vertical and horizontal stripes at window boundaries;
5. IDs equal to and above `activeBiomeCount`;
6. deterministic pseudorandom grids for active counts 2, 14, and 256;
7. a color LUT containing nontrivial alpha/RGB fractions so byte conversion is checked.

Each case compares every one of the 4,096 ID, weight, and blended output pixels, not just
a checksum.

**Verify**: run the tests against the unmodified production rescan first. They must pass.
If the test reference does not match current production, fix the test before optimizing.

### Step 3: Implement the horizontal rolling histogram

Keep `BiomeMapBaker` as the owner. Make the production smoothing entry point internal for
tests, then replace only its histogram construction:

For each output row `ty`:

1. Clear the active range of the 256-entry count buffer once.
2. Compute the same `hrCenterY` as today.
3. For `tx=0`, compute the same `hrCenterX` and add the complete 25×25 window.
4. Emit output using the unchanged top-K/weight/color code.
5. For every subsequent `tx`, advance the center by two high-resolution cells:
   - subtract the two outgoing columns, 25 rows each;
   - add the two incoming columns, 25 rows each;
   - perform the same `id < activeBiomeCount` check for both removal/addition;
   - emit output using the same helper.

Derive column indices from previous/new window boundaries rather than magic constants.
For radius `r=12` and step `s=2`, moving center from C to C+2 removes columns `C-r` and
`C-r+1`, and adds columns `C+r+1` and `C+r+2`.

Extract only one focused helper if needed: `WriteTopKPixel(counts, activeBiomeCount,
lutColors, outputIndex, ...)`. Reuse existing `PickTopK` and `SafeLut`; do not introduce a
histogram class.

Expected work per leaf:

- old: `64 * 64 * 625 = 2,560,000` count additions;
- new: `64 * (625 + 63 * 100) = 443,200` add/subtract updates;
- about 5.78× fewer core updates.

**Verify**: run `BiomeMapBakerTests`. Every output byte must match the independent
reference for every case.

### Step 4: Build and measure the managed optimization

After Unity import, build Planet, run the focused test and full EditMode suite, then run
three fresh same-seed Play-mode starts. Save logs to
`local-only/perf/startup-generation/003-after-managed-rolling.txt`.

Required correctness:

- ID, weight, and blended atlas checksums exactly equal the Plan 002 baseline;
- normal and modes 78/79/80 unchanged at the same camera positions;
- seed assignment, cleanup changes, distinct biome count, target count, and atlas
  resolution unchanged.

Performance gate: median Phase B `mapBake <= 5,000 ms` and no regression >5% in the
Phase B vertex or atlas stages. Report `topK` before and after separately: `mapBake` is the
gate, but `topK` is the only number this plan can move, and its speedup is what proves the
rolling histogram worked.

If the gate passes, stop here, run `graphify update .`, and complete the plan. Do not
implement Step 5.

### Step 5 (conditional): Burst only the top-K smoothing pass

Execute this step only if Step 4 remains above 5,000 ms and Plan 001 timings show
smoothing—not high-res ID construction—is still dominant. Record that evidence in the
implementation handoff before editing.

Create `BiomeTopKJob.cs` with a deterministic Burst `IJobParallelFor` where each job index
owns one complete leaf map. Do not schedule one job per texel; each map item must process
its 64 rows sequentially so it can reuse one histogram slice.

Batch data for at most the current 96 chunks:

- managed flat high-res `byte[]` built in the existing background `Parallel.For`, one
  contiguous 23,104-byte slice per target leaf;
- one `NativeArray<byte>` copied from that flat input before scheduling;
- one `NativeArray<int>` histogram scratch sized `targetLeafCount * 256`, so each job
  index writes a disjoint slice;
- three flat `NativeArray<Color32>` outputs sized `targetLeafCount * 4,096`;
- one read-only native color LUT in exact `Color`/`float4` values.

Refactor `BiomeMapBaker` only enough to split `BuildHighResIdGrid` from the smoothing call
and allow an output offset. The high-res builder must keep using Plan 002's full-precision
climate slice, cleaned primary atlas, lake mask, and lookup overrides.

Schedule the job on the main thread, call `JobHandle.ScheduleBatchedJobs`, poll with
`Awaitable.NextFrameAsync(ct)`, complete, then copy each output slice into the existing
`PlanetChunk.PendingBiome*Pixels` arrays on the background thread. Reuse current
`BiomeAtlasService`/atlas ownership; do not change texture upload.

All native arrays use bounded Persistent lifetime and are disposed in `finally` after the
handle completes. Cancellation completes the job before disposal and rethrows.

The Burst job must use the same rolling algorithm and top-K ordering. Avoid fast float
mode because blended `Color32` conversion is a byte-parity requirement.

**Verify**:

- Extend `BiomeMapBakerTests` to schedule the real Burst job for every Step 2 corpus and
  compare all output bytes with the independent reference.
- Run cancellation-then-regeneration smoke check with no NativeArray/job safety errors.
- Repeat three same-seed runtime runs. All three atlas checksums must exactly match the
  Plan 002 baseline.
- Median map bake must now be <=5,000 ms. If Burst still misses it, stop and report the
  split timing; do not add vertical rolling or port more stages under this plan.

### Step 6: Final evidence and handoff

Report a compact before/after table containing:

- Plan 002 baseline map-bake median;
- managed rolling median and speedup;
- conditional Burst median/speedup if Step 5 ran;
- Phase B vertex/retain/atlas medians;
- whole-generation median;
- identical checksum values;
- test counts and build result.

Run `graphify update .`. Update row 003 to `DONE` and explicitly state whether the Burst
step was skipped because the managed algorithm met the gate.

## Test plan

Automated `BiomeMapBakerTests` must prove exact pixels for the seven data families in Step
2. If conditional Burst runs, the same corpus must compare three implementations:

1. independent full-rescan test oracle;
2. production managed rolling method;
3. scheduled Burst job.

Runtime validation covers stitched face atlases (not just individual map buffers), normal
render, map debug modes, a multi-biome junction, a lake shore, and cancellation cleanup.
Use the existing NUnit/EditMode infrastructure; add no package or test framework.

## Done criteria

- [ ] Constants remain 64 / 128 / radius 12 / 625 / padded 152 / top K 4.
- [ ] Full-precision static climate, cleaned Voronoi assignment, lake mask, and elevation
  override inputs are unchanged.
- [ ] Independent reference tests passed against the pre-change rescan.
- [ ] Managed rolling outputs are byte-identical for every 4,096-pixel test result.
- [ ] Same-seed stitched atlas ID, weight, and blended checksums match Plan 002 exactly.
- [ ] Normal render and modes 78/79/80 are unchanged at required locations.
- [ ] Median map-bake time is <=5 seconds over three fresh same-seed starts.
- [ ] Conditional Burst was skipped if managed rolling met the gate; if used, its tests,
  cancellation check, checksum parity, and target all pass.
- [ ] Planet build exits 0 and full EditMode suite has 0 failures.
- [ ] `graphify update .` was run or known timeout recorded.
- [ ] No unrelated dirty file was modified/staged; row 003 is `DONE`.

## STOP conditions

- Plan 001 Step 5b was not implemented, so no `hrGridCpu` / `topKCpu` split exists.
- The `topKCpu` share is under 0.5, making this plan's ceiling lower than its own gate.
- Plan 002 is incomplete, feeds byte-quantized climate into the leaf map, or has unstable
  same-seed checksums.
- Any requested change would reduce smoothing radius/resolution, seed count, cleanup
  iterations, leaf count, or top K.
- The independent reference does not reproduce pre-change output.
- Managed rolling produces any differing output byte.
- Runtime stitched atlas checksum changes for the same seed/settings.
- Managed map bake meets the <=5-second gate (STOP adding Burst; complete the plan).
- Conditional Burst produces any differing byte, leaks a native container, or requires
  porting high-res classification/atlas upload to meet the target.
- A step requires vertical rolling, integral histograms, compute shaders, a generic
  framework, or a file outside conditional Scope.
- Build/tests fail twice after one reasonable correction.

## Maintenance notes

- The horizontal update assumes output centers advance by
  `HighResolution / MapResolution`, currently two. Keep it derived from those constants so
  a future resolution change either remains correct or fails an explicit assertion.
- Lower-ID tie behavior is observable generated data. A seemingly cleaner sort/comparer can
  change maps and must not replace the current strict insertion rules casually.
- Runtime single-chunk rebakes use the same baker. Confirm any conditional batch-only Burst
  path leaves a correct managed rolling fallback for `RebakeBiomeMapsAt`.
- Vertical histogram reuse is deliberately deferred. Consider it only if a future larger
  map/kernel again misses a measured target.
