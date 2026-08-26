# Plan 002: Move the high-resolution climate vertex bake to Burst without changing biome generation

> **Executor instructions**: Follow this plan step by step and run every verification.
> Preserve full-precision climate inputs until leaf biome maps are classified. Stop and
> report on any STOP condition; do not paper over parity failures with tolerances, lower
> resolutions, or disabled diagnostics. Update row 002 in `advisor-plans/README.md` when
> complete unless a reviewer owns the index.
>
> **Drift check (run first)** (baseline re-pinned from `fab754b` to `fdbc76c` on
> 2026-08-15; no in-scope file for this plan changed between those commits):
> `git diff --stat fdbc76c..HEAD -- Assets/Scripts/Planet/Biomes/ClimateCurveLut.cs Assets/Scripts/Planet/Biomes/TemperatureProvider.cs Assets/Scripts/Planet/Biomes/MoistureProvider.cs Assets/Scripts/Planet/Biomes/ClimateProvider.cs Assets/Scripts/Planet/ColorGenerator.cs Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs Assets/Scripts/Planet/Surface/ChunkMeshCache.cs Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs Assets/Graphics/Shaders/PlanetVertexColor.shader Assets/Graphics/Shaders/Includes/DebugModes.hlsl Assets/Tests/EditMode`
>
> Plan 001 is expected to change some of these files. Compare its completed implementation
> with the contracts below. STOP only on a semantic/structural mismatch, not on the known
> Plan 001 timing additions.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MED
- **Depends on**: `advisor-plans/001-instrument-startup-generation.md`
- **Category**: perf / architecture / tests
- **Planned at**: commit `fab754b`, 2026-08-12

## Why this matters

At the audited depth-4, 97×97 configuration, the face-atlas path evaluates 19,250,814
chunk vertices. The historical median for that pass is 71.249 seconds—55.8% of the full
load. Each managed call currently evaluates climate, samples the lake mask, performs an
exact primary/secondary Voronoi lookup, resolves DTO-backed biome definitions, allocates a
`Vector4[]`, and later loops again to compact it. Normal rendering gets biome IDs/weights
from the face atlas, so most of that work is redundant. The required per-vertex payload is
static climate (temperature, moisture/precipitation, altitude cooling), which is a good
fit for the existing Burst job pattern.

## Current state

- `Assets/Scripts/Planet/ColorGenerator.cs:229-285` takes the expensive route even when
  callers only ask for diagnostics:

```csharp
ClimateSample climate = _climateProvider.Evaluate(pointOnUnitSphere, elevation);
temperature = climate.Temperature01;
moisture = climate.Moisture01;
altitudeTemperatureDrop = climate.AltitudeTemperatureDrop;
BiomeResult result = ResolveBiome(pointOnUnitSphere, climate);
```

- `ResolveBiome` samples `LakeMask.Current`, calls
  `_biomeAssignmentField.Evaluate` (the exact domain-warped KD-tree path), converts slice
  IDs through DTO definitions, then applies elevation/lake overrides.
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:1397-1500` enters a managed
  `Parallel.For` for every 96-chunk batch and calls `CalculateChunkBiomeData` on every
  internal and leaf chunk when face atlases are enabled.
- `Assets/Scripts/Planet/Surface/ChunkMeshCache.cs:267-299` later converts each float
  `Vector4` to `Color32` using:

```csharp
(byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f)
```

- The leaf map reads full-precision x/y climate from `CpuBiomeData` before compaction in
  `BiomeMapBaker.GetTemperatureMoisture`; if only `CpuBiomeData32` exists it reads
  quantized values. Quantizing before map classification would be a behavior change.
- The production texture path samples `_BiomeIds` and `_BiomeWeights`. Shader diagnostic
  mode 73 alone reads `input.biomeData.z`; diagnostic mode 78 already reads the dominant
  production map ID.
- Existing Burst precedents:
  - `NoiseData` / `NoiseFilterData` / `NoiseFilterEvaluator` are blittable and already
    covered by `Assets/Tests/EditMode/NoiseFilterEvaluatorGoldenTests.cs`.
  - `PlanetChunkMeshJob` is a Burst `IJobParallelFor` scheduled in bounded batches and
    polled with `Awaitable.NextFrameAsync`.
  - `ProceduralPlanets.Planet.asmdef` already references Burst, Mathematics, and
    Collections; no package change is needed.

## Required generation behavior

This plan optimizes a consumer of biome generation; it does not remove or replace biome
generation.

- `ClimateProvider` remains the single static climate authority. Temperature remains
  latitude curve + seeded noise - elevation lapse. Moisture/precipitation remains the
  latitude moisture curve blended with seeded noise.
- `VoronoiBiomeField.Build` still uses that static climate to assign every seed before
  cleanup.
- `BiomeConstants.VoronoiCleanupIterations` remains five, with eight nearest neighbors,
  the six-vote replacement threshold, lower-ID tie behavior, and early exit unchanged.
- The cleaned 512×512×6 primary atlas remains the land-biome source for leaf map
  classification.
- Lake/ocean/shore and elevation overrides remain in `BiomeMapBaker.BuildHighResIdGrid`.
  The Burst climate job deliberately does not sample the lake or Voronoi field because
  neither affects temperature/moisture/altitude-cooling values.
- The leaf map receives full-precision Burst output before any byte packing.
- The high-resolution face-atlas path is optimized. The low/per-face and no-atlas fallback
  paths continue using the current managed color/biome methods.
- `WeatherManager` continues to generate its evolving weather grid after `IPlanet`. Do
  not feed live weather back into static biome assignment.
- Normal-render screenshots and final biome atlas ID/weight checksums must be unchanged.
  Mode 73 is allowed to change only as documented below: in texture mode it becomes an
  authoritative production-map diagnostic rather than forcing exact per-vertex Voronoi
  resolution.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Planet build | `dotnet build ProceduralPlanets.Planet.csproj --no-restore` | exit 0, 0 errors after Unity import |
| Relevant EditMode tests | Unity Test Runner / MCP `run_tests`, EditMode filter `ClimateBakeJobTests` | all new tests pass |
| Full EditMode suite | Unity Test Runner / MCP `run_tests`, EditMode | 0 failures; report total count rather than assuming it |
| Confirm obsolete high-res call is gone | `rg -n "CalculateChunkBiomeData|GetBiomeData\(" Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs` | no high face-atlas call remains; fallback uses may remain elsewhere |
| Inspect performance | `$editorLog = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"; rg "Biome color timings|Biome atlas checksum|Generation timings" $editorLog` | complete same-seed before/after records |
| Refresh graph | `graphify update .` | exit 0, or record the known timeout |

## Scope

**In scope (only these product/test files):**

- `Assets/Scripts/Planet/Biomes/ClimateCurveLut.cs`
- `Assets/Scripts/Planet/Biomes/TemperatureProvider.cs`
- `Assets/Scripts/Planet/Biomes/MoistureProvider.cs`
- `Assets/Scripts/Planet/Biomes/ClimateProvider.cs`
- `Assets/Scripts/Planet/Biomes/ClimateBakeJob.cs` (create)
- `Assets/Scripts/Planet/ColorGenerator.cs`
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`
- `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs`
- `Assets/Graphics/Shaders/PlanetVertexColor.shader`
- `Assets/Graphics/Shaders/Includes/DebugModes.hlsl`
- `Assets/Scripts/Planet/AssemblyInfo.cs` (create only if test internals are not already visible)
- `Assets/Tests/EditMode/ClimateBakeJobTests.cs` (create)
- Unity-generated `.meta` files for new files
- `advisor-plans/README.md` (status only)

**Out of scope:**

- `VoronoiBiomeField` algorithms, seed count, cleanup count/rules, domain warp, or primary
  atlas resolution.
- `BiomeMapBaker.SampleTopKPerTexel`; Plan 003 owns smoothing.
- A generic job framework, generic snapshot framework, ECS/DOTS conversion, compute shader,
  `Task.Run`, package additions, or unsafe pinning of managed arrays.
- Retaining terrain mesh `NativeArray`s across the terrain/color phase. That may remove a
  later input copy, but it expands lifecycle/cancellation risk and is not needed to reach
  this plan's target.
- Canonical max-depth face-grid reuse for internal LODs. Measure this direct Burst pass
  first; only pursue reuse if the target is missed and profiling proves input/output copy
  is dominant.
- Climate-map or water conversion to Burst.

## Git workflow

- Suggested branch from completed 001: `perf/burst-climate-vertex-bake`.
- Prefer two reviewable commits: (1) evaluator/tests, (2) batch integration/shader debug.
- Example messages: `Perf: add Burst climate evaluator`; `Perf: Burst the chunk climate bake`.
- Do not push or open a PR unless instructed.

## Target design

Add one concrete data/evaluator/job path—no hierarchy:

```text
ClimateProvider initialized state
  -> ClimateBakeJobData (2 NoiseFilterData + 2 NativeArray<float> LUTs + scalars)
  -> ClimateBakeJob : IJobParallelFor
       input: flattened float3 directions + float elevations for one <=96 chunk batch
       output A: full-precision float4 climate
       output B: packed Color32 retained vertex payload
  -> leaf BiomeMapBaker consumes output A slices
  -> each chunk retains output B; no managed Vector4[] and no second compaction loop
```

`float4` channel meaning remains x temperature, y moisture, z unused in the texture path,
w clamped altitude cooling. `Color32.b` is zero/reserved for the texture path. Low/fallback
paths keep the old z channel. In `_BIOME_COLOR_MODE_TEXTURE`, diagnostic mode 73 samples
the dominant `_BiomeIds` map just like mode 78; outside texture mode it retains the old
per-vertex z behavior.

## Steps

### Step 1: Pin the current output and performance baseline

From Plan 001 evidence, select the three same-seed fresh-start runs and record median:

- Phase B `vertex`, `mapBake`, `retainUpload`, `atlas`, total;
- top-level `initialize`, `terrain`, `lake`, `colors`, `climate`, `water`, total;
- biome atlas ID, weight, and blended checksums;
- seed, assignment mode, cleanup changes, distinct biome count, depth, resolution, chunk
  counts, total vertices, face-atlas flag, Burst enabled.

Capture normal render and debug modes 73, 74, 75, 78, 79, and 87 from the same camera.
The normal render, temperature, moisture, map primary, map blend, and altitude cooling are
parity references. Mode 73 is a semantic migration reference.

**Verify**: same-seed ID and weight checksums match across all three baseline runs. If not,
STOP before modifying code.

### Step 2: Create an immutable Burst climate snapshot

In `ClimateCurveLut`, add the minimum internal operation needed to copy its baked sample
array into a caller-owned `NativeArray<float>` with a requested allocator. Do not expose
the mutable managed array.

In `TemperatureProvider` and `MoistureProvider`, store the initialized seed and expose
internal snapshot construction data: the existing `NoiseSettings`, exact baked LUT, the
computed `_maxValue`, and existing scalar strengths/influences/water threshold. Do not
re-evaluate the authoring `AnimationCurve`; copy the already-baked LUT used by the managed
provider.

Create `ClimateBakeJobData` in `ClimateBakeJob.cs` containing exactly:

- temperature and moisture `NoiseFilterData` created with the initialized seeds (moisture
  remains seed + 100 because `ClimateProvider.Initialize` already makes that choice);
- temperature and moisture latitude LUT `NativeArray<float>` values;
- temperature maximum amplitude, noise strength, altitude lapse, water threshold;
- moisture maximum amplitude, latitude influence, noise strength.

Implement `IDisposable` only to dispose the two LUT arrays. `NoiseFilterData` owns no
native allocation. Add `ClimateProvider.CreateBakeJobData(Allocator)` and, if useful, one
thin `ColorGenerator` forwarding method; do not let `ChunkedSurfaceProvider` reach into
provider internals.

**Verify**: a test constructs/initializes `ClimateProvider`, creates/disposes the snapshot
twice, and asserts both LUT arrays are created at the configured resolution and disposed.

### Step 3: Implement a formula-equivalent evaluator and job

In `ClimateBakeJob.cs`, implement a Burst-compatible evaluator with the exact managed
formula order from `TemperatureProvider.EvaluateClimate` and
`MoistureProvider.EvaluateClimate`:

1. normalized latitude = `abs(asin(clamp(direction.y, -1, 1))) / (PI/2)`;
2. linear sample the baked latitude LUT with clamped input, floor lower index, min upper
   index, and unclamped interpolation;
3. evaluate existing `NoiseFilterEvaluator` and divide by the same maximum amplitude;
4. temperature noise contribution = `(normalized - 0.5) * TemperatureNoiseStrength`;
5. altitude drop = `max(0, elevation - OceanThreshold) * AltitudeTemperatureDrop`;
6. final temperature = clamp01(latitude temperature + noise contribution - drop);
7. legacy moisture = clamp01(`(normalized - 0.5) * 2.5 + 0.5`);
8. band moisture = clamp01(latitude moisture +
   `(normalized - 0.5) * 2 * MoistureNoiseStrength`);
9. final moisture = lerp(legacy, band, `MoistureLatitudeInfluence`).

Add `[BurstCompile(FloatMode = FloatMode.Deterministic)]` to `ClimateBakeJob :
IJobParallelFor`. Inputs are `[ReadOnly] NativeArray<float3> Directions` and
`[ReadOnly] NativeArray<float> Elevations`. Outputs are
`NativeArray<float4> FullPrecision` and `NativeArray<Color32> Packed`.

Pack x/y/w using a Burst-compatible equivalent of
`Mathf.RoundToInt(Mathf.Clamp01(value) * 255f)`; set b to zero. Do not use a fast float mode
until parity is proven. Avoid copying the ~2 KiB `NoiseFilterData` per element: invoke the
evaluator by reference on the job's temperature/moisture fields or otherwise prove in the
Burst Inspector that the permutation tables are not copied inside every `Execute`.

**Verify**: `ClimateBakeJobTests` schedules the real job and compares it to
`ClimateProvider.Evaluate` for:

- at least two seeds;
- pole, equator, mid-latitude, cube-face edge, and negative-axis directions;
- below-water, sea-level, low-land, and mountain elevations;
- latitude influence 0 and 1, noise strength 0 and configured nonzero values;
- altitude lapse 0 and configured nonzero values;
- byte rounding probes immediately below, at, and above half-byte boundaries.

Float climate fields must be exactly equal if Burst and managed execution produce exact
results. If the compiler differs within <=1e-6, document the exact probes and additionally
prove final map IDs/weights remain byte-identical. Any larger difference is a STOP.

### Step 4: Replace only the high face-atlas managed vertex pass

In `ChunkedSurfaceProvider.GenerateColorsAsync`, keep the current <=96 chunk bound. For
each batch on the main thread:

1. Compute per-chunk vertex counts and offsets.
2. Allocate bounded `Allocator.Persistent` native input/output arrays **once, before the
   batch loop**, sized for the fixed 96-chunk bound, and reuse them across all batches.
   Use Persistent because frame polling can exceed TempJob's four-frame lifetime. At 96 ×
   97² vertices the four arrays are roughly 33 MB; allocating and freeing that per batch
   churns it about 22 times per generation for no benefit. Dispose once in the outer
   `finally`.
3. Copy existing `CpuUnitSpherePoints` and `CpuElevations` into flat input arrays. Do not
   pin managed arrays. `Vector3` and `float3` are layout-identical, so copy into a
   `NativeArray<Vector3>` with `CopyFrom` and take a `.Reinterpret<float3>()` view rather
   than converting element by element.
4. Schedule one `ClimateBakeJob` over the flat vertex count and call
   `JobHandle.ScheduleBatchedJobs()`.
5. Poll completion with `Awaitable.NextFrameAsync(ct)`. On cancellation, complete the job,
   dispose every created array, then rethrow, matching `ChunkSurfaceGenerator`'s proven
   ownership pattern.
6. Complete the handle, move to the background thread for leaf-map work/copies, then
   return to main for the existing release/progress steps.

Run this path only when `bakeLookupBuilt && _usesFaceBiomeAtlases`. Keep the existing
managed `CalculateChunkColors`/`CalculateChunkBiomeData` behavior for fallback modes.
Remove the `Parallel.For` exact-biome vertex pass from the high face-atlas branch.

Add timing fields to the existing Phase B line:

- `climatePrepare` (flatten/copy);
- `climateJob` (schedule through completion);
- `climateFinalize` (output slice copies);
- keep aggregate `vertex` as their wall-clock sum for historical comparison.

Always dispose job data and batch NativeArrays in `finally`. Never dispose while a handle
is incomplete.

**Verify**: intentionally cancel one `planet.generate` during this phase, then regenerate
successfully. Unity must report no leaked NativeArray, job safety, or disposed-container
errors. This is a smoke check, not authorization to expand into the separate cancellation
finding. Cancel during Phase B colors specifically — water generation has not started yet
at that point, so audit finding R1 (water computation ignores cancellation) cannot
contaminate the result. R1 stays open and out of scope for this series.

### Step 5: Feed full precision directly to the current map baker

Add the smallest `BiomeMapBaker.Bake` overload needed to accept a
`NativeSlice<float4>` (or array + offset/count) for temperature/moisture instead of reading
`PlanetChunk.CpuBiomeData`. Keep `PlanetChunk.CpuElevations`, chunk face/UV geometry,
`BiomeLookupData`, cleaned `IBiomeAssignmentField.EvaluatePrimaryId`, `LakeMask`, and all
existing high-res classification and smoothing logic unchanged.

For each target leaf in the completed batch:

- pass that chunk's full-precision Burst output slice to this overload;
- produce the same three pending `Color32[]` map buffers through the existing
  `BiomeAtlasService` ownership path;
- only after map classification, copy the corresponding packed output slice to the
  chunk's `CpuBiomeData32`;
- leave `CpuBiomeData` null, so `_meshCache.RetainBiomeSource` takes its existing
  already-packed fast path.

Internal chunks need only the packed output. Do not allocate a managed `Vector4[]` for
any high face-atlas chunk.

**Verify**:

```powershell
rg -n "new Vector4\[|CpuBiomeData =" Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs
```

Expected: no allocation/assignment remains in the high face-atlas batch path. Existing
fallback code may still allocate it.

### Step 6: Move the primary-biome diagnostic to its authoritative source

> **Approval gate, added on review 2026-08-15.** This step changes observable debug
> behavior — mode 73 stops rendering per-vertex Voronoi in texture mode — which makes it a
> behavior change riding inside a performance plan. Per `CLAUDE.md` change control it needs
> Bryan's explicit `fix` / `defer` mark before it is implemented. It is not covered by
> approval of the plan series as a whole. If it is deferred, Steps 1-5 still stand: the
> per-vertex `z` channel simply keeps being written from the packed output, at the cost of
> mode 73 no longer matching the production map.
>
> Note for the executor: `input.biomeData.x` is **not** debug-only. It feeds
> `EvaluateTerrainOverrideMasks` on the production path
> (`Assets/Graphics/Shaders/PlanetVertexColor.shader:1094`), so per-vertex temperature is
> load-bearing for every rendered chunk including internal LOD chunks. Do not "optimize" by
> skipping the climate bake for non-leaf chunks.

In `PlanetVertexColor.shader`, change only the mode-73 branch:

```hlsl
#if defined(_BIOME_COLOR_MODE_TEXTURE)
    // normalize DominantBiomeId(input.chunkUv) by _BiomeCount and call BiomeIdColor
#else
    // retain BiomeIdColor(input.biomeData.z)
#endif
```

Update the comment in `DebugModes.hlsl` so mode 73 is documented as map-backed in texture
mode and per-vertex in fallback mode. Do not alias/delete mode 78; it remains the explicit
top-K map diagnostic used by existing workflows.

**Verify**: Unity shader import/compile has no errors. In high face-atlas mode, captures of
73 and 78 should now show the same dominant biome boundaries. In low/fallback mode, 73
must still render from vertex data.

### Step 7: Validate parity and performance before considering further work

After Unity import:

1. Build `ProceduralPlanets.Planet.csproj`.
2. Run `ClimateBakeJobTests`, then the full EditMode suite.
3. Run three fresh same-seed Play-mode starts with the same settings/camera as baseline.
4. Compare atlas checksums. ID and weight checksums must be exactly identical. Blended
   checksum must also be identical; if only it differs, STOP and report the first pixel
   mismatch rather than accepting visual similarity.
5. Compare normal, modes 74/75/78/79/87, lake shores, snowline, coast, and at least one
   multi-biome junction. These should be unchanged. Confirm mode 73 now matches mode 78 in
   texture mode as designed.
6. Record medians and percentage deltas in the implementation handoff and local evidence
   file `local-only/perf/startup-generation/002-after-burst-climate.txt`.

Acceptance target: median Phase B `vertex <= 8,000 ms` without increasing `mapBake` by
more than 5%. Also report the total generation median; `<60,000 ms` is the series goal.

The gate was tightened from 24 s to 8 s on review, 2026-08-15, for two reasons. First,
arithmetic: everything outside the two hotspots totals 39.2 s historically, so a 24-second
climate result plus Plan 003's 5-second map bake lands at 68 s and misses the series goal
while nominally passing. Second, the target work: this plan removes the per-vertex
KD-tree/DTO `ResolveBiome` path entirely and replaces the remainder with deterministic
Burst over 19,250,814 vertices of two noise-filter evaluations. That should complete in low
single-digit seconds on the existing `IJobParallelFor` pattern. A 24-second result would be
roughly an order of magnitude off the achievable number and should be profiled, not banked.

If `vertex <=8 s`, stop. Do not implement canonical-grid/internal-LOD reuse. If it lands
between 8 s and 24 s, that is a profiling task and not an automatic STOP: report the split
below, then ask before either accepting it or opening follow-up work. Use Plan 001's
`climatePrepare`, `climateJob`, and `climateFinalize` values:

- prepare/finalize dominant: report canonical grid reuse as a new plan;
- job dominant: inspect Burst Inspector and worker utilization before proposing changes;
- map bake dominant: proceed to Plan 003.

Run `graphify update .` after the source change.

## Test plan

Create `Assets/Tests/EditMode/ClimateBakeJobTests.cs`, modeled after
`NoiseFilterEvaluatorGoldenTests.cs`. Use the existing EditMode asmdef and NUnit; add no
test package. If internal members are inaccessible, add one assembly-level
`InternalsVisibleTo("ProceduralPlanets.Tests.EditMode")` declaration in
`Assets/Scripts/Planet/AssemblyInfo.cs` and keep implementation APIs internal.

Required automated cases are listed in Step 3. Runtime checks additionally cover:

- high face-atlas normal render;
- low/per-face fallback render;
- lake and lake-shore overrides;
- ocean/beach, mountain/snowy-mountain overrides;
- two biome seeds/cleanup results unchanged;
- cancellation followed by successful regeneration;
- atlas checksum parity and three-run timings.

## Done criteria

- [ ] Static climate formulas, seeds, LUT samples, and full-precision leaf-map inputs are
  preserved.
- [ ] Voronoi climate assignment and five-iteration cleanup remain active and unchanged.
- [ ] High face-atlas per-vertex climate runs in a deterministic Burst job.
- [ ] High face-atlas path no longer calls full `ResolveBiome` per vertex.
- [ ] No high face-atlas managed `Vector4[]` climate payload or second compaction loop
  remains.
- [ ] Low/no-atlas fallback behavior remains intact.
- [ ] Climate evaluator/job EditMode tests and full suite pass with 0 failures.
- [ ] Same-seed biome atlas ID, weight, and blended checksums are byte-identical.
- [ ] Normal and specified climate/map debug captures match; mode 73's intended source
  migration is verified, or Step 6 was explicitly deferred by Bryan.
- [ ] Batch native arrays are allocated once outside the batch loop and disposed once.
- [ ] Median Phase B vertex time is <=8 seconds over three fresh same-seed runs.
- [ ] Unity reports no job/container leaks after cancellation smoke test.
- [ ] Planet build exits 0 after Unity import.
- [ ] `graphify update .` was run or its known timeout recorded.
- [ ] No unrelated dirty file was modified/staged; row 002 is `DONE`.

## STOP conditions

- Plan 001 same-seed baseline checksums are unstable.
- Full-precision climate cannot reach the leaf baker without early byte quantization.
- Implementing the job appears to require changing cleanup, seed assignment, smoothing,
  live weather dependency, resolution, or fallback rendering.
- Burst-vs-managed climate differs by more than 1e-6 at any probe.
- Atlas ID or weight checksum changes for the same seed/settings.
- Blended checksum changes and the first differing pixel cannot be explained as a bug in
  Plan 001's checksum instrumentation.
- A job/container leak or safety exception persists after one reasonable ownership fix.
- A step requires unsafe managed-array pinning, retained terrain-job arrays, a generic job
  framework, or a file outside Scope.
- Build or tests fail twice after one reasonable correction.

## Maintenance notes

- The climate snapshot is an initialized-generation snapshot. If climate settings change,
  rebuild it through the existing planet regeneration path; never mutate it while a job
  is running.
- The static moisture field is the biome precipitation driver. Do not substitute the live
  evolving weather texture in future refactors.
- Keep float data until classification and byte data for retained mesh upload. Collapsing
  those two roles into one early packed buffer risks biome-boundary drift.
- If a future renderer stops reading per-vertex temperature for terrain overrides, profile
  whether the entire internal-chunk climate payload can be removed. That is outside this
  plan and requires shader/LOD evidence.
