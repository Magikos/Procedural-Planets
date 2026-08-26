# Audit Summary

Audited branch `character-controller-mvp` at `f6bd37f` on 2026-08-11. The working tree
was already dirty; those unrelated user changes were preserved. Scope covers the loading
initializer graph, planet initialization, high-resolution chunk generation, biome/climate
generation, biome-map baking, water generation, and the handoff to weather/scatter. It
does not re-audit unrelated steady-state rendering or visual tuning.

**Findings only — no code changed.** Bryan must mark findings `fix`, `defer`, or
`wontfix` before product-source implementation. Categories used below are Performance,
Architecture, and Bug/Risk.

The latest available recorded timings are from `Editor-prev.log`, last written
2026-08-03. They predate the current HEAD and the later lake-mask integration, so they are
historical evidence rather than a fresh benchmark of this exact tree. They do, however,
match the reported two-minute load symptom and identify a stable source-level hotspot.
The last seven recorded successful generations were:

| Phase | Median | Range | Median share of total |
|---|---:|---:|---:|
| Initialize | 6.035 s | 5.316–7.768 s | 4.7% |
| Terrain | 14.864 s | 13.678–19.947 s | 11.6% |
| Colors (including any work between terrain and color completion) | 92.258 s | 82.575–110.924 s | 72.2% |
| Climate map | 0.572 s | 0.531–0.718 s | 0.4% |
| Water | 12.836 s | 11.840–15.415 s | 10.0% |
| **Planet total** | **127.775 s** | **116.495–159.057 s** | **100%** |

The matching Phase B sub-timings isolate the dominant work further:

| Phase B substage | Median | Range | Median share of planet total |
|---|---:|---:|---:|
| Per-vertex biome/climate evaluation | 71.249 s | 63.076–85.998 s | 55.8% |
| Leaf biome-map bake | 17.396 s | 15.826–20.245 s | 13.6% |
| Compact/retain/upload preparation | 2.842 s | 2.459–3.252 s | 2.2% |
| Face-atlas assembly/upload | 0.395 s | 0.284–0.532 s | 0.3% |

The current high-resolution configuration is depth 4 with 97×97 vertices per chunk:
2,046 total chunks, 1,536 leaves, and 19,250,814 generated chunk vertices. The two
highest-payoff findings are P1 and P2. Burst can materially reduce startup, but terrain is
already Burst-compiled; adding Burst indiscriminately is not the answer. A reasonable
first performance gate is to reduce the recorded median below 60 seconds. That is a target,
not a promise, until a fresh current-HEAD baseline and implementation measurements exist.

## What came back clean

- **Loading order:** `LoadingManager` discovers three early and two late initializers.
  It topologically orders them and awaits each in sequence
  (`Assets/Scripts/Core/Services/LoadingManager.cs:341-389`). `WeatherManager` correctly
  declares an `IPlanet` late dependency (`Assets/Scripts/Planet/WeatherManager.cs:203-214`).
  There is no meaningful independent late-startup branch to parallelize before the planet.
- **Terrain already uses Burst:** `PlanetChunkMeshJob` and `PlanetChunkNormalsJob` are
  Burst `IJobParallelFor` jobs, scheduled in bounded batches by
  `ChunkSurfaceGenerator` (`Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs:36-88,
  98-155`). The terrain median is still worth improving, but “turn on Burst for terrain”
  is already done.
- **Burst plumbing exists:** the Planet assembly already references Burst, Mathematics,
  and Collections (`Assets/Scripts/Planet/ProceduralPlanets.Planet.asmdef:4-12`), and the
  resolved package lock contains Burst 1.8.29. `NoiseData`, `NoiseFilterData`, and
  `BiomeLookupData` already provide suitable precedents for blittable evaluation snapshots.
- **Main thread responsiveness:** the expensive biome and water calculations move to a
  worker and yield frames while waiting. The problem is elapsed load time and allocation
  volume, not a single two-minute main-thread freeze.
- **Climate-map and face-atlas upload:** their recorded medians are about 0.6 and 0.4
  seconds. They are not useful first targets.
- **Scatter:** initial scatter configuration happens only after planet readiness
  (`Assets/Scripts/Planet/Planet.cs:366-371`), and the gather path is now a Burst
  `IJobParallelFor` (`Assets/Scripts/Planet/Scatter/ScatterGatherJob.cs:50-56`). It is not
  the source of the recorded planet-generation delay.
- **Async architecture sweep:** the only active `RuntimeInitializeOnLoadMethod` under
  `Assets/Scripts` is `LoadingManager`; no active `Task.Run`, `async void`, coroutine, or
  execution-order workaround was found in the startup/generation path.
- **Visual tuning:** no recommendation below reduces chunk depth, terrain resolution,
  biome-map resolution, water resolution, or biome smoothing radius. Those remain visual
  decisions.

# Findings

## P1 — The dominant face-atlas biome pass performs managed and partly redundant work for 19.25 million vertices

**Category:** Performance / Architecture  
**Severity:** High  
**Description:** The high-resolution face-atlas path still visits every vertex of every
internal and leaf chunk using `Parallel.For`. Each call enters the managed `IBiomeProvider`
interface, evaluates temperature and moisture noise, samples the lake mask, performs a full
Voronoi primary/secondary lookup, resolves DTO-backed biome definitions, and creates a
`Vector4`. The result is then converted to a separate `Color32` array on the main thread.

The exact Voronoi resolve is unnecessary for normal face-atlas rendering: the terrain's
visible biome IDs and weights already come from the face biome atlas. In the vertex payload,
the resolved primary biome is used by a diagnostic shader mode, while temperature is the
channel used by normal snow overrides. The diagnostic can read the already-authoritative
face ID atlas instead of forcing an exact Voronoi query at every chunk vertex.

**Evidence:**

- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:1397-1499` runs two managed
  `Parallel.For` passes per 96-chunk batch. Even when `vertexColorsRequired` is false in
  face-atlas mode, it calls `CalculateChunkBiomeData` for every chunk.
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:1714-1728` loops every vertex,
  allocates `Vector4[]`, and calls `IBiomeProvider.GetBiomeData`.
- `Assets/Scripts/Planet/ColorGenerator.cs:229-285,318-344` evaluates climate, the lake
  mask, the managed assignment field, and registry DTOs per vertex.
- `Assets/Graphics/Shaders/PlanetVertexColor.shader:78-87,1026-1043,1078-1081` shows that
  the primary-biome, moisture, and altitude-drop channels are diagnostic, while normal
  terrain override rendering consumes temperature.
- `Assets/Scripts/Planet/Biomes/BiomeLookupData.cs:4-49` and
  `Assets/Scripts/Planet/NoiseFilters/NoiseFilterData.cs:4-50` already establish Burst-safe
  snapshot/evaluator patterns.
- Recorded median: 71.249 seconds, 55.8% of total startup.

**Impact:** This one pass dominates startup and creates avoidable managed allocations and
virtual/DTO-heavy calls. It also repeats 4,798,590 internal-LOD vertex evaluations whose
locations align with the max-depth face grid.

**Effort:** L  
**Fix Risk:** MED  
**Confidence:** HIGH that this is the primary bottleneck; MED on the exact speedup before a
current-HEAD benchmark.  
**Recommendation:** Implement this as three measured, behavior-preserving steps:

1. In face-atlas mode, stop doing a full `ResolveBiome` solely to populate the diagnostic
   primary-biome vertex channel. Make the primary-biome debug view sample the same face ID
   atlas that normal terrain uses.
2. Add one concrete `ClimateBakeJobData` snapshot containing the existing Burst-safe noise
   state, native latitude-curve LUTs, scalar climate settings, and flattened read-only lake
   and Voronoi-primary lookup arrays. Run a deterministic Burst `IJobParallelFor` over the
   chunk batch and write native climate output.
3. Feed the leaf map bake from that native float data, then write the retained `Color32`
   payload directly. Do not create an intermediate managed `Vector4[]` and then loop over it
   again to compact it. Once parity is proven, derive aligned internal-LOD payloads from the
   canonical max-depth face grid instead of reevaluating them.

A 3× reduction would take this substage below about 24 seconds and save roughly 47 seconds
from the recorded median. That is an acceptance target, not an assumed Burst multiplier.

**Refactor Option:** One concrete climate snapshot/evaluator shared later with the water
classification pass. Do not add a generic generation-job framework.  
**Behavior note:** Preserving. Require same-seed normal-render captures plus primary biome,
temperature, moisture, altitude-cooling, and snow debug comparisons. Compare generated face
biome ID/weight pixels before accepting float-mode differences.

## P2 — Biome smoothing performs about 3.93 billion count updates per generation

**Category:** Performance  
**Severity:** High  
**Description:** Every one of the 1,536 leaf maps produces 64×64 output texels. Each texel
rescans a 25×25 high-resolution neighborhood from scratch, for
`1,536 × 64 × 64 × 625 = 3,932,160,000` sample-count updates. The radius was deliberately
increased to 12 for softer biome borders; lowering it would speed the pass by changing the
look and is not recommended.

**Evidence:** `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs:24-32,129-175` defines the
128-cell high-resolution grid, radius 12, and full nested window scan. Only leaves are map
targets in face-atlas mode (`Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:
1459-1472,1731-1732`). Recorded median: 17.396 seconds.

**Impact:** The algorithm discards almost all work between adjacent output texels even
though their windows overlap heavily.

**Effort:** M  
**Fix Risk:** LOW-MED  
**Confidence:** HIGH  
**Recommendation:** Preserve the exact 25×25 window and `PickTopK` tie ordering, but maintain
a rolling 256-bin histogram. For the first texel in a row, count the full window. Moving one
output texel advances two high-resolution cells, so subtract the two outgoing columns and
add the two incoming columns: 100 updates instead of 625. Reuse vertical row state as a
second step. First prove byte-identical IDs, weights, and blended colors in managed code;
then move the row/chunk work into a Burst job using the native inputs from P1.

The simple horizontal rolling window reduces the core scan count by about 5.8×. A practical
gate is map-bake median at or below 5 seconds, worth roughly 12 seconds from the recorded
median if achieved.

**Refactor Option:** None beyond a focused Burst biome-map job; the current baker remains the
right owner.  
**Behavior note:** Preserving and expected to be byte-identical. Do not change
`KernelRadius`, resolutions, normalization, or top-K tie behavior.

## P3 — Blank surface-edit resources are eagerly created and uploaded for every chunk

**Category:** Performance / Maintainability  
**Severity:** Medium  
**Description:** Before mesh generation, every one of the 2,046 chunks receives a 64×64
RGBA surface-state texture, a 256×256 R8 path-wear texture, and a managed 256×256 path-wear
byte array. Each texture is initialized and `Apply` is called. This creates 4,092 texture
objects and uploads blank data even though edit painting only targets leaf chunks and most
chunks normally have no saved edit.

At current settings, the managed blank path buffers alone contain 127.9 MiB of pixels. One
raw copy of the chunk-local surface/path textures adds about 160 MiB; readable texture CPU
backing and GPU allocation are additional runtime costs that must be measured rather than
assumed from raw pixel counts.

**Evidence:**

- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:168-189` allocates resources for
  all chunks before mesh jobs.
- `Assets/Scripts/Planet/Surface/PlanetChunk.cs:235-307` creates, clears, and uploads both
  textures and creates `PathWearPixels` for every chunk.
- Paint entry points skip non-leaves at
  `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:339-343,430-434`.
- Blank face-level surface and path atlases are also created for grass at
  `Assets/Scripts/Planet/Surface/BiomeAtlasService.cs:199-213`.

**Impact:** Large startup allocation/upload volume and persistent memory use. Its elapsed
cost is currently hidden inside the aggregate terrain phase, so the time saving is a
measured hypothesis rather than a claim.

**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH on allocation volume; MED on elapsed-time payoff.  
**Recommendation:** Add a substage timer first. Bind shared blank surface/path textures for
unedited chunks, allocate leaf-local texture/CPU state only when a saved stamp or live edit
first touches that leaf, and preserve the current blank behavior for internal LOD chunks.
Ensure lazy allocation rebinds resident render handles and updates the face atlas used by
grass. Keep the existing eager path as a rollback until edit replay and regeneration pass.

**Refactor Option:** A small `EnsureSurfaceEditResources(PlanetChunk)` helper in the existing
surface-edit owner; no new resource framework.  
**Behavior note:** Preserving if untouched chunks continue sampling zero and saved edits are
allocated before replay.

## P4 — Terrain is Burst-compiled, but main-thread finalization and all-LOD data volume remain unmeasured

**Category:** Performance  
**Severity:** Medium  
**Description:** Terrain jobs generate all 2,046 internal and leaf chunks. Their initial
managed CPU arrays contain about 807.8 MiB of element data before array/object overhead, and
the post-bake retained arrays contain about 624.3 MiB. After every batch completes, the main
thread copies four native arrays, calculates every vertex radius and bounds, and feeds every
one of the 19.25 million elevations through a min/max accumulator.

The source comment estimates about 135 KiB of transient native data per chunk, but the four
actual native arrays total 40 bytes per vertex, about 367.5 KiB per chunk and 45.9 MiB for a
128-chunk batch. The stale estimate does not cause the delay, but it can mislead future
batch-size tuning.

**Evidence:**

- `Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs:16-18,98-155` allocates vertices,
  unit directions, elevations, and normals for each scheduled chunk.
- `Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs:158-178,192-217` performs managed
  copy, radius, bounds, and per-elevation min/max loops after job completion.
- `Assets/Scripts/Planet/ShapeGenerator.cs:128-134` confirms that
  `RecordElevationSample` only updates min/max.
- Depth and resolution evidence:
  `Assets/Game Data/Planet Settings/Planet.asset:15-17` and
  `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:32,150-166`.
- Recorded terrain median: 14.864 seconds.

**Impact:** Burst accelerates the noise/normal math but not these copies, reductions, blank
texture setup, or duplicate all-LOD source generation.

**Effort:** M for reduction/finalization; L for canonical-grid or progressive generation.  
**Fix Risk:** MED for reduction; HIGH for changing generation/readiness semantics.  
**Confidence:** HIGH on work volume; LOW-MED on the split until substage timers exist.  
**Recommendation:** Time quadtree build, texture preparation, job wait, job drain/copy, water
sampler construction, and grass-atlas construction separately. Then move radius/bounds and
per-chunk elevation min/max into a Burst reduction so the main thread records two extrema
instead of every elevation. Correct the batch-memory comment. Consider canonical max-depth
face data for aligned internal LODs only after those smaller changes are measured.

Do not start with progressive/on-demand planet generation. It could reduce time-to-play
more dramatically, but water, grass surface atlases, biome atlases, and deterministic
readiness currently consume global data, so it is a behavior and architecture change.

**Refactor Option:** Keep `ChunkSurfaceGenerator` as the concrete owner; extend its existing
job state with finalization outputs rather than adding another orchestration layer.  
**Behavior note:** Burst reduction is preserving. Progressive generation changes readiness
and must be separately approved.

## P5 — Water spends about 13 seconds in a managed global graph over roughly 889,000 face samples

**Category:** Performance / Architecture  
**Severity:** Medium  
**Description:** At aggregate depth 2, each water face sampler is 385×385, or 889,350 face
samples before seam deduplication. `WaterMeshBuilder` maps those samples through a managed
direction-key dictionary, several growable lists, a `List<int>[]` adjacency graph, climate
evaluation for wet points, component classification, shore-distance traversal, and managed
mesh dictionaries/lists. The whole computation runs on one background continuation. It is
off the main thread but still on the loading critical path.

**Evidence:**

- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:201-223` constructs 385×385 face
  samplers.
- `Assets/Scripts/Planet/WaterMeshBuilder.cs:124-148,530-644` builds the global data and
  evaluates managed climate; `:646-690` allocates adjacency lists.
- `Assets/Scripts/Planet/PlanetWaterSurface.cs:164-189,216-224` waits for the complete managed
  result before mesh upload.
- Recorded water median: 12.836 seconds.

**Impact:** Water is the third-largest recorded phase. A monolithic Burst port is blocked by
managed interfaces, dictionaries, lists, and graph topology, but its pure sampling and
classification portions are viable once P1 supplies a climate snapshot.

**Effort:** L  
**Fix Risk:** MED  
**Confidence:** HIGH that water is material; MED on which internal stage dominates because
there are no substage timers.  
**Recommendation:** Add timers for seam indexing, wet/depth/climate classification,
adjacency/body BFS, shore distance, per-face mesh construction, and Unity mesh upload.
Replace direction hashing with deterministic cube-face seam indexing and flat arrays. Burst
the parallel wet/depth/climate pass first; keep component traversal and mesh assembly managed
until their counters justify a rewrite. Reuse P1's concrete climate snapshot.

**Refactor Option:** A flat `WaterGridData` scratch owner is justified if it replaces the
current dictionary/list graph; do not wrap the existing graph in jobs.  
**Behavior note:** Preserving. Compare same-seed water stats, body counts, shoreline/freeze
captures, and mesh checksums within the project's existing visual-evidence workflow.

## P6 — Startup timing is too coarse to validate the medium-priority work safely

**Category:** Architecture / Maintainability  
**Severity:** Medium  
**Description:** The top-level planet timer labels the entire interval after terrain and
through `GenerateColorsAsync` as `colors`; in the current tree that interval also includes
lake-mask construction. LoadingManager reports initializer counts but not initializer
durations. Terrain and water each report only one aggregate duration. Phase B is the useful
exception because it already has substage timers.

**Evidence:**

- `Assets/Scripts/Planet/Planet.cs:319-350,373-378` starts the color timer before lake-mask
  construction and reports only aggregate terrain/climate/water phases.
- `Assets/Scripts/Core/Services/LoadingManager.cs:347-389` logs initializer counts but not
  elapsed time or component names on success.
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs:1523-1532` demonstrates the useful
  Phase B timing pattern.

**Impact:** P3–P5 cannot be ranked or regression-checked precisely, and current available
logs predate HEAD.

**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Before optimization, add one debug timing record per initializer and
the bounded substages named in P3–P5. Split lake-mask time from color time. Include seed,
resolution mode, depth, chunk count, vertex count, water sampler resolution, and whether
Burst was enabled. Benchmark cold and warm Editor runs separately and use a development
player run as the shipping comparison. Keep logs aggregate; do not emit per-chunk noise.

**Refactor Option:** None; local stopwatches and one summary record per owner are sufficient.  
**Behavior note:** Preserving.

## R1 — Canceling a slow startup still does not cancel the water computation

**Category:** Bug / Risk  
**Severity:** Medium  
**Description:** The prior consolidated audit's F06 remains open. `GenerateAsync` accepts a
cancellation token, but the background water calculation and its polling loop do not observe
it until the entire result completes. This does not slow a successful startup, but it makes
the current 12–15 second water cost unavoidable after cancellation or teardown.

**Evidence:** `Assets/Scripts/Planet/PlanetWaterSurface.cs:98-103,164-178,216-224` accepts the
token but does not pass it into `WaterMeshBuilder.Compute`; the long loops under
`Assets/Scripts/Planet/WaterMeshBuilder.cs:530-690` have no cancellation checks.

**Impact:** Regeneration cancellation and world teardown continue consuming CPU and
allocations until water completes; the stale result is not applied.

**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Carry the token through the existing pure compute pipeline and check it
at bounded row/face/component intervals. Poll with cancellable `NextFrameAsync(ct)` and never
publish partial `MeshData`. This can land with P5 instrumentation before any data-oriented
rewrite.

**Refactor Option:** None.  
**Behavior note:** Preserving for successful generation; cancellation becomes prompt and
matches the existing API contract.

# Refactoring Plan

This is a proposal, not authorization.

1. **Measurement slice — P6 and R1.** Add per-initializer and generation-substage timing,
   split lake-mask/color attribution, and make water cancellation cooperative. Build Core
   then Planet, let Unity recompile, and capture three same-seed cold/warm baselines on the
   current HEAD. Product visuals are unchanged.
2. **Dominant-work slice A — P1 redundant resolve.** Remove the face-atlas path's exact
   per-vertex biome resolve and source primary-biome diagnostics from the authoritative face
   atlas. Validate normal terrain, snow, and all biome diagnostic modes before measuring.
3. **Dominant-work slice B — P1 Burst climate.** Introduce the focused climate/lake/primary
   lookup snapshot and deterministic job. Keep the managed evaluator as an equivalence
   reference/rollback until same-seed climate samples and face-atlas outputs pass. Write the
   retained compact format without a managed `Vector4[]` conversion pass.
4. **Exact algorithm slice — P2.** Land the rolling histogram with byte-for-byte output
   comparison, then Burst it using P1's native inputs. Do not alter smoothing or resolution.
5. **Allocation slice — P3.** After its new timer proves material cost, replace eager blank
   per-chunk edit resources with shared defaults and lazy touched-leaf allocation. Validate
   no-edit startup, saved stamp replay, live path/scorch edits, LOD changes, regeneration,
   and teardown.
6. **Terrain finalization slice — P4.** Move extrema/radius/bounds finalization into jobs and
   measure job wait versus drain. Only investigate canonical internal-LOD derivation if the
   terrain phase remains above budget.
7. **Water slice — P5.** Use the new counters to flatten the dominant global-grid stage and
   Burst only pure classification first. Preserve mesh topology and water-body statistics.

Suggested concrete collaborators are limited to `ClimateBakeJobData`/its evaluator and,
only if P5 counters justify it, flat `WaterGridData`. Existing `ChunkSurfaceGenerator`,
`BiomeMapBaker`, and `BiomeAtlasService` remain the correct owners for their work.

Expected payoff is deliberately staged. If P1 reaches its 3× gate and P2 reaches 5 seconds,
the historical median falls from about 128 seconds to roughly 68 seconds before any terrain,
texture, initialization, or water improvement. A stronger P1 result plus the measured P3–P5
work makes a 30–60 second startup plausible; only fresh runtime evidence can narrow that
range.

# Prior Audit Reconciliation

Only prior items overlapping startup/planet generation are restated here. Unrelated findings
retain their status in their source audits.

| Prior item | Status | Current evidence / destination |
|---|---|---|
| 2026-07-22 F06 water cancellation | OPEN | Revalidated against current `PlanetWaterSurface`/`WaterMeshBuilder`; carried as R1. |
| 2026-07-22 F11 surface-provider mixed responsibilities | OPEN, not duplicated | The provider remains broad, but this audit uses existing concrete generator/atlas seams and does not propose a broad split. |
| 2026-07-22 F14 loading-transition commit boundary | OPEN, outside duration cause | Still a separate cancellation-semantics decision; initializer parallelism is not recommended here. |
| 2026-07-25 F2 synchronous scatter gather | RESOLVED | Current scatter gather is scheduled as a Burst job and is outside the recorded planet critical path. |
| 2026-07-26 scatter addendum | OUT OF SCOPE | Findings concern steady-state rendering/material/LOD behavior, not startup planet generation. |

# Questions for the User

Before implementation, mark P1–P6 and R1 `fix`, `defer`, or `wontfix`. The recommended first
authorization is P6 + R1 + P1 + P2; defer P3–P5 until the added counters rank their internal
costs. Progressive/on-demand generation is intentionally not included in that first approval
because it changes readiness semantics.
