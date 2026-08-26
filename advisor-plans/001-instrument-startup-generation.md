# Plan 001: Instrument startup and establish the current generation baseline

> **Executor instructions**: Follow this plan step by step. Run every verification and
> confirm its expected result before continuing. If a STOP condition occurs, stop and
> report; do not improvise. When complete, update this plan's row in
> `advisor-plans/README.md` unless a reviewer says they own the index.
>
> **Drift check (run first)**:
> `git diff --stat fdbc76c..HEAD -- Assets/Scripts/Core/Services/LoadingManager.cs Assets/Scripts/Planet/Planet.cs Assets/Scripts/Planet/ColorGenerator.cs Assets/Scripts/Planet/Biomes/VoronoiBiomeField.cs Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs Assets/Scripts/Planet/Surface/BiomeAtlasService.cs Assets/Scripts/Planet/PlanetWaterSurface.cs Assets/Scripts/Planet/WaterMeshBuilder.cs`
>
> If an in-scope file changed, compare the current-state excerpts below with live code.
> Any structural mismatch is a STOP condition.
>
> Baseline re-pinned from `fab754b` to `fdbc76c` on 2026-08-15. `Planet.cs` gained +73/-1
> lines of tree-generator work between those commits; that change is known and is not a
> STOP. Every other in-scope file is byte-identical to the authored state.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW
- **Depends on**: none
- **Category**: perf / diagnostics
- **Planned at**: commit `fab754b`, 2026-08-12

## Why this matters

The latest recorded successful generations took a 127.775-second median, but those logs
were written on 2026-08-03 and predate the current commit and lake-mask integration. The
existing top-level and Phase B timers already point at two hotspots, yet the `colors`
number currently includes lake-mask work and startup has no per-initializer timings.
This plan makes later changes measurable without introducing a telemetry framework or
changing generation behavior.

## Current state

- `Assets/Scripts/Core/Services/LoadingManager.cs:341-389` topologically orders and
  sequentially awaits every early and late initializer, but only logs initializer counts.
- `Assets/Scripts/Planet/Planet.cs:310-378` reports `initialize`, `terrain`, `colors`,
  `climate`, `water`, and `total`. The `colors` stopwatch begins before `LakeMask.Build`,
  so the label is inaccurate:

```csharp
phaseTimer.Restart();
// LakeMask.Build ...
await GenerateColorsAsync(...);
long colorsMs = phaseTimer.ElapsedMilliseconds;
```

- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:134-245` performs quadtree
  construction, chunk texture allocation, Burst mesh generation, water-sampler assembly,
  and grass surface-atlas generation under one terrain number.
- `Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs:36-88` already uses bounded
  Burst job batches. This plan measures its schedule/wait/drain wall time; it does not
  replace the job system.
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:1397-1536` already reports
  Phase B `vertex`, `mapBake`, `retainUpload`, and `atlas` timings.
- `Assets/Scripts/Planet/Biomes/VoronoiBiomeField.cs:75-113` times the full build only.
  Seed placement, climate assignment, five-iteration cleanup, KD tree construction, and
  the 512×512×6 primary atlas are not separated.
- `Assets/Scripts/Planet/PlanetWaterSurface.cs:164-213` awaits pure CPU water computation,
  then uploads/configures it, but no substage timing is logged.
- Historical evidence from `docs/audit/2026-08-11-startup-planet-generation-audit.md`:

| Phase | Historical median |
|---|---:|
| Initialize | 6.035 s |
| Terrain | 14.864 s |
| Colors including lake work | 92.258 s |
| Climate map | 0.572 s |
| Water | 12.836 s |
| Total | 127.775 s |
| Phase B vertex | 71.249 s |
| Phase B map bake | 17.396 s |

## Preservation contract

- Logging only: do not reorder initializers, change progress ranges, change `await`
  boundaries, add concurrency, modify job batch sizes, or alter generated data.
- Keep `Awaitable`, cancellation tokens, and the existing main/background-thread pattern.
- Do not add a generic metrics service. Local `Stopwatch` values and the existing logger
  are sufficient.
- The static climate assignment and Voronoi cleanup pass remain untouched. Record their
  times separately so later optimization cannot silently delete them.
- Diagnostic checksum work must be compiled only in `UNITY_EDITOR` or
  `DEVELOPMENT_BUILD`; release players must pay no hashing cost.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Confirm branch | `git symbolic-ref --short HEAD` | branched from `harvest-vertical-slice` unless Bryan selected a new implementation branch |
| Confirm clean start | `git status --short` | no unrelated dirty files; commit or stash the Trees/Scatter work first |
| Core build | `dotnet build ProceduralPlanets.Core.csproj --no-restore` | exit 0, 0 errors |
| Planet build | `dotnet build ProceduralPlanets.Planet.csproj --no-restore` | exit 0, 0 errors |
| Inspect timing lines | `$editorLog = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"; rg "Initializer timing|Voronoi timings|Terrain timings|Biome color timings|Biome atlas checksum|Water timings|Generation timings" $editorLog` | at least one complete set after a fresh successful play |
| Refresh graph | `graphify update .` | exit 0; if it hits the repository's known timeout, record that fact and continue |

Open/import with exactly Unity `6000.6.0a7` before trusting generated `.csproj` files or
runtime results. Core and Planet builds are serial; do not build them concurrently.

## Scope

**In scope (only these product files):**

- `Assets/Scripts/Core/Services/LoadingManager.cs`
- `Assets/Scripts/Planet/Planet.cs`
- `Assets/Scripts/Planet/ColorGenerator.cs`
- `Assets/Scripts/Planet/Biomes/VoronoiBiomeField.cs`
- `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs` (two timers only; see Step 5b)
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`
- `Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs`
- `Assets/Scripts/Planet/Surface/BiomeAtlasService.cs`
- `Assets/Scripts/Planet/PlanetWaterSurface.cs`
- `advisor-plans/README.md` (status only)

**Out of scope:**

- `WaterMeshBuilder.cs` internals. CPU-vs-upload separation is enough for this batch; only
  instrument its graph/classification internals if post-003 water evidence makes it the
  next hotspot.
- Any timing/metrics abstraction, persistent analytics, profiler package, or editor tool.
- Any algorithm, batch-size, resolution, visual, cleanup, weather, or cancellation change.
- The unrelated dirty worktree and the existing root `plans/` directory.

Unity may generate or update `.meta` files for new assets, but this plan creates no new
product asset and should therefore create none.

## Git workflow

- Suggested branch: `perf/startup-generation-instrumentation`.
- Use one logical commit, e.g. `Perf: instrument startup generation`.
- Do not push or open a PR unless instructed.
- Before committing, run `git status --short` and verify that unrelated pre-existing
  changes remain untouched.

## Steps

### Step 1: Capture an unmodified current-HEAD baseline

Before editing product code, open the project in Unity 6000.6.0a7 and let scripts import.
Use the current high-resolution settings unchanged. Record the planet seed printed by
`Planet`, quadtree depth, chunk resolution, chunk count, leaf count, vertex count, face
atlas mode, and whether Burst is enabled.

Perform three fresh Play-mode starts, fully leaving Play mode between runs. Copy all
existing `Generation timings` and `Biome color timings` lines after each successful load
to a local evidence file under:

`local-only/perf/startup-generation/001-before.txt`

Do not use same-session `planet.generate` runs as substitutes for the fresh-start median;
they have different JIT/cache conditions. They may be recorded separately as warm
regeneration evidence.

**Verify**:

```powershell
$editorLog = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"
rg "Generation timings|Biome color timings|Built global Voronoi field" $editorLog
```

Expected: three complete successful run groups and the same generated seed/configuration
for all three. If a deterministic seed cannot be held constant, STOP and report the three
seeds instead of comparing unlike worlds.

### Step 2: Time each startup initializer without changing its order — DEFERRED, do not implement

> **Deferred on review, 2026-08-15.** The whole initialize phase is a 6.035-second median,
> 4.7% of the load, and this series explicitly refuses to parallelize the initializer graph.
> Per-initializer timing therefore cannot change any decision in Plans 002/003, while it is
> the only step that touches `LoadingManager`. Skip it. Re-open it only if post-003 evidence
> ranks initialization as the next hotspot. `LoadingManager.cs` stays out of scope until then.
>
> The original text is kept below for that future re-open.

In `LoadingManager.InitializeAsync`, start a `Stopwatch` immediately before each
`EarlyInitialize` and `LateInitialize` await. In a `finally` local to each initializer,
emit one Debug log with exactly these stable fields:

```text
Initializer timing: phase=<early|late>, type=<full type name>, elapsed=<integer>ms,
scene=<scene name>, success=<True|False>
```

Also time the entire `InitializeAsync` body and log `Startup initialization timings` with
`earlyTotal`, `lateTotal`, and `total`. Do not create a helper class or change exception
wrapping. A duplicated three-line stopwatch around the two loops is smaller and clearer.

**Verify**: import in Unity, then run both serial builds. Expected: 0 errors. A fresh Play
must show every initializer exactly once in its appropriate phase and preserve the same
initializer order as before.

### Step 3: Correct the top-level planet phase boundaries

In `Planet.GeneratePlanetAsync`:

1. Stop and record `lakeMs` immediately after returning to the main thread from
   `LakeMask.Build` and assigning `LakeMask.Current`.
2. Restart the phase timer immediately before `GenerateColorsAsync` and record only that
   await in `colorsMs`.
3. Add `lake=<n>ms` to the existing `Generation timings` line.
4. Add stable configuration fields to the adjacent planet summary: planet seed,
   resolution mode, per-face resolution, and `Unity.Burst.BurstCompiler.IsEnabled`.

Do not move `LakeMask.Build`; it must remain between terrain/elevation commit and biome
map generation.

**Verify**: `rg -n "lakeMs|Generation timings" Assets/Scripts/Planet/Planet.cs` must show
a distinct lake duration and the final log must contain `lake=` before `colors=`.

### Step 4: Time terrain ownership boundaries

In `ChunkedSurfaceProvider.GenerateAsync`, record wall-clock durations for:

- `tree`: all six fixed-depth quadtree builds plus chunk gathering;
- `textureSetup`: current batched `PlanetChunkTextures.Allocate` work and its yields;
- `meshJobs`: the complete `_generator.GenerateMeshesAsync` await;
- `waterSamplers`: construction of the six root/aggregated samplers;
- `grassSurfaceAtlas`: `BuildGrassSurfaceAtlasesAsync`;
- `total`.

Log one `Terrain timings` line with those fields plus `depth`, `chunkResolution`, total
chunks, leaves, and total chunk vertices. Compute counts from `_allChunks` once; do not add
new state solely for logging.

The `ChunkSurfaceGenerator.GenerateMeshesAsync` schedule/wait/drain split is **deferred on
review, 2026-08-15**: terrain work is deferred until post-003 re-ranking, and the
provider-level `meshJobs` number is already enough to rank terrain against the other
phases. Leave `ChunkSurfaceGenerator.cs` untouched. If post-003 evidence promotes terrain,
add the split then:

- `schedule`: NativeArray allocation and scheduling;
- `wait`: wall time from `ScheduleBatchedJobs` until combined completion, including frame
  yields;
- `drain`: `DrainCompletedJobs` copying/disposal;
- batch count and total vertices.

**Verify**: on a successful generation, the sum of the logged substages must not exceed
`total` beyond normal stopwatch rounding. All substage values must be non-negative and
`chunks=2046`, `leaves=1536`, `chunkResolution=97` for the audited default unless settings
have intentionally changed.

### Step 5: Time Voronoi construction while preserving cleanup

In `VoronoiBiomeField.Build`, use the existing stopwatch and record boundaries for:

- Fibonacci seed placement;
- static-climate biome assignment;
- cleanup;
- distinct-count plus KD-tree build;
- primary-atlas build;
- total.

Expose those scalar durations as read-only properties on `VoronoiBiomeField` and include
them in `ColorGenerator.CommitBiomeAssignmentField`'s existing `Built global Voronoi
field` log as a separate `Voronoi timings` line. Include seed count, cleanup iteration
limit, cleanup changes, distinct biome count, atlas resolution, and assignment mode.

Do not edit `AssignClimateBiomes`, `CleanupBiomeAssignments`, `FindNearestEight`, the
six-vote threshold, tie ordering, early exit, or `BuildPrimaryAtlas` behavior.

**Verify**:

```powershell
rg -n "VoronoiCleanupIterations|CleanupBiomeAssignments|majorityCount >= 6|Voronoi timings" Assets/Scripts/Planet
```

Expected: the constant remains the active iteration argument; the six-vote rule remains;
the log reports nonzero static-climate and atlas times for normal Voronoi mode.

### Step 5b: Split the map-bake number into grid construction and smoothing

**Added on review, 2026-08-15. Plan 003 cannot start without this measurement.**

`ChunkedSurfaceProvider.cs:1461-1472` times the whole `BiomeAtlasService.BakeChunkMap`
`Parallel.For` as one `mapBake` number. That number covers two unrelated passes inside
`BiomeMapBaker.Bake` (`Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs:62-63`):

```csharp
BuildHighResIdGrid(chunk, lookup, assignmentField, vertRes, tempHighRes);
SampleTopKPerTexel(tempHighRes, lutColors, activeBiomeCount, blendedColors, ids, weights);
```

Plan 003's rolling histogram speeds up only `SampleTopKPerTexel`. The other pass is not
cheap: it evaluates 152×152 = 23,104 padded samples per leaf, each doing a domain-warped
`IBiomeAssignmentField.EvaluatePrimaryId`, a `LakeMask.Current.Sample`, two bilinear
climate/elevation samples, and a `BiomeLookupEvaluator.ResolveFromLandBiomes` — about 35.5
million such samples per generation across 1,536 leaves. Which of the two dominates the
17.396-second median is currently unknown, and Plan 003's entire 5.78× premise rests on
the answer.

In `BiomeMapBaker`, add two `static long` tick accumulators and bracket each call with
`Stopwatch.GetTimestamp()`, publishing with `System.Threading.Interlocked.Add` because the
caller runs this under `Parallel.For`. Expose a reset method and a read accessor pair, and
log the two values in the existing Phase B `Biome color timings` line as `hrGridCpu=` and
`topKCpu=`. Reset them where `mapBakeTicks` is initialized so a regeneration does not
accumulate.

The `Cpu` suffix is load-bearing: these are summed worker CPU times and are not comparable
to the wall-clock phase numbers beside them. Keep it in the field name so nobody adds them
to a wall-clock budget later.

Keep this cheap: `Stopwatch.GetTimestamp()` deltas rather than `Stopwatch` instances, so
the bake path allocates nothing new. No per-texel timing, no timing class.

Do not change `BuildHighResIdGrid`, `SampleTopKPerTexel`, `PickTopK`, or any constant.

**Verify**: both values must be nonzero, and `hrGridCpu + topKCpu` must **exceed**
`mapBake`. These two accumulate summed worker CPU time across the `Parallel.For`, while
`mapBake` is the wall clock around it, so on an 8-worker machine their sum should land
several times higher. A sum at or below `mapBake` means the accumulation is wrong.

Record `topKCpu / (hrGridCpu + topKCpu)` in the Step 8 table. That share is the input to
Plan 003's go/no-go, and being a ratio of two identically-measured quantities it is
unaffected by the CPU-vs-wall distinction.

### Step 6: Add development-only atlas parity checksums

In `BiomeAtlasService`, after the six face pixel arrays have been assembled but before
they are uploaded/released, compute a deterministic 64-bit FNV-1a checksum over the raw
RGBA bytes of:

- biome IDs;
- biome weights;
- blended colors.

Hash faces in index order and pixels in row-major order. Include resolution in the log.
Place both computation and log behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. Keep the
small hash method private to `BiomeAtlasService`; do not add a utility class. This checksum
is a regression oracle for Plans 002 and 003, not gameplay state.

**Verify**: two same-seed fresh generations on unchanged code must emit identical three
checksums. A different seed should change at least the ID checksum. If same-seed IDs or
weights differ before any optimization, STOP: the benchmark is nondeterministic and the
later byte-parity gates cannot be used as written.

### Step 7: Separate water CPU build, upload, and material setup

In `PlanetWaterSurface.GenerateAsync`, add a total stopwatch and measure:

- `compute`: the `BuildWaterMeshAsync` await;
- `upload`: `WaterMeshBuilder.Apply` and shader frozen-body global writes;
- `configure`: material creation/reuse, renderer binding, and `UpdateWaterMaterial`;
- `total`.

Log one `Water timings` line with current water mesh vertex/triangle/body counts. Do not
instrument inside `WaterMeshBuilder` yet and do not move Unity mesh/material APIs off the
main thread.

**Verify**: the water `compute` duration must be less than or equal to `total`; the final
top-level `water` duration must be greater than or equal to the water service total within
normal stopwatch rounding.

### Step 8: Build, run, and publish the baseline table

After Unity import, build Core and Planet serially. Run three fresh same-seed Play-mode
starts. Save the timing lines to
`local-only/perf/startup-generation/001-after-instrumentation.txt` and calculate medians
for all new stable fields. Append the table to the existing startup audit only if Bryan
has confirmed that untracked audit file is the intended record; otherwise leave the local
evidence file and paste the table into the implementation handoff.

Compare instrumented against unmodified **substage** medians, not the total. The observed
historical total range is 116.495–159.057 s — about ±17% — so a 5% gate on the total sits
inside the noise floor and proves nothing at n=3. The real checks are:

- development-only atlas hashing stays below 250 ms;
- `hrGrid + topK` reconciles with `mapBake`, and `mapBake` itself has not grown by more
  than 5% against the Step 1 baseline (this is the one substage whose new timers run inside
  a `Parallel.For` body, so it is where added cost would actually show);
- no other substage shows a new systematic cost that is not explained by a timer added to it.

If `mapBake` grew beyond 5%, rerun once; if it persists, STOP and report the overhead
before starting Plan 002.

Run `graphify update .` after the code change.

## Test plan

No new test framework or test fixture is needed because this plan changes only timing
observability. Required checks are:

- serial Core and Planet builds after Unity import;
- three same-seed fresh starts complete without exceptions;
- same-seed atlas checksums are identical across runs;
- all new timing fields exist and are non-negative;
- `hrGridCpu` and `topKCpu` are nonzero and sum above the wall-clock `mapBake`;
- `mapBake` median overhead is within the 5% gate.

## Done criteria

- [ ] Fresh unmodified current-HEAD baseline captured before edits.
- [ ] `colors` no longer includes lake-mask time; `lake` is separate.
- [ ] Terrain, Voronoi, Phase B, water, and top-level timing records appear.
- [ ] Phase B reports `hrGridCpu` and `topKCpu`, both nonzero and summing above `mapBake`.
- [ ] The `topKCpu / (hrGridCpu + topKCpu)` share is recorded for Plan 003's go/no-go.
- [ ] Same-seed ID, weight, and blended-color checksums are stable.
- [ ] Static climate assignment and cleanup iteration/rule code are unchanged.
- [ ] `LoadingManager.cs` and `ChunkSurfaceGenerator.cs` are untouched (Steps 2 and 4's
  mesh-job split are deferred).
- [ ] Core and Planet builds exit 0 with 0 errors after Unity import.
- [ ] Three fresh post-change runs complete with <=5% `mapBake` instrumentation overhead.
- [ ] `git status --short` shows no newly modified file outside the in-scope list, plan
  status, Unity-generated metadata, or pre-existing unrelated changes.
- [ ] `graphify update .` was run or its known timeout was recorded.
- [ ] `advisor-plans/README.md` row 001 is `DONE`.

## STOP conditions

- An in-scope source file structurally differs from the current-state excerpts.
- A same-seed pre-optimization run produces changing biome ID or weight checksums.
- A deterministic same-seed fresh-start baseline cannot be captured.
- Instrumentation requires changing generation ordering, progress/cancellation semantics,
  job batch sizes, or generated data.
- Development checksum cost remains above 250 ms or `mapBake` overhead remains above 5%
  after one rerun.
- `hrGridCpu` or `topKCpu` is zero, or their sum falls at or below the wall-clock
  `mapBake`, meaning the split is mismeasured and Plan 003 has no valid input.
- Either build fails twice after one reasonable correction.
- Work would overwrite or stage an unrelated dirty file.

## Maintenance notes

- Keep log field names stable until the full optimization series is complete; comparison
  scripts and human baseline tables depend on them.
- Do not treat summed parallel CPU time as wall time. All logged phase/substage fields in
  this plan are elapsed wall-clock durations around the awaited ownership boundary.
- Once startup performance is accepted, development checksums may remain because they are
  cheap regression evidence. Remove them only if measured Editor overhead becomes material.
- Re-rank deferred terrain and water work from post-003 evidence; do not assume their
  historical shares are still current.
