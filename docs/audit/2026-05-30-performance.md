# Performance Audit — 2026-05-30

## Scope and method

This audit identifies CPU hotspot candidates (Burst / Jobs targets), LOD opportunities, and GPU costs incurred by the recent water/cloud/atmosphere work. It is a **static code-reading audit** — no profiler captures yet. The capture-target section at the end recommends what to measure to confirm or reject these hypotheses before committing implementation time.

The pattern follows the 2026-05-28 water audit: read code → identify candidates → prioritize → user approves → implement biggest-win first → measure → next.

---

## Already optimized (don't redo)

These systems are already on a reasonable performance path. Listing them so we don't accidentally re-optimize:

| System                                           | Current state                                                                                                                    |
| ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| Planet generation (`Planet.GeneratePlanetAsync`) | `Awaitable.BackgroundThreadAsync` for all heavy phases, `Parallel.For` across the 6 cube faces                                   |
| Color generation                                 | Background thread + `Parallel.For` across 6 faces                                                                                |
| Water mesh build                                 | `Task.Run` for background compute, per-frame progress poll                                                                       |
| Cloud weather grid generation                    | Background thread, single one-shot                                                                                               |
| Cloud weather **evolution**                      | GPU compute shader (`WeatherEvolution.compute`) — ping-pong RT, dispatched per frame                                             |
| Cloud raymarch view step count                   | Already scales with altitude (`StepScaleNearAltitude`/`StepScaleFarAltitude`) and global `QualityController.CloudStepMultiplier` |
| Cloud noise textures                             | Generated on GPU via `CloudNoiseGenerator` compute shader                                                                        |

So the _scaffolding_ for async/parallel work exists. The remaining wins are inside the per-face loops and per-vertex evaluations.

---

## Prioritized findings

### PERF-01 🔴 Critical — `Noise.Evaluate` is the central hot loop, called from every system

**File:** [Assets/Scripts/Planet/Noise.cs](../../Assets/Scripts/Planet/Noise.cs)

Pure-CPU 3D simplex noise. Called from:

- `SimpleNoiseFilter.Evaluate` (N layers per call)
- `RigidNoiseFilter.Evaluate` (N layers per call)
- `TemperatureProvider`, `MoistureProvider` (via filters)
- `SphericalWeatherGrid.Fbm` (5 octaves × 3 noise fields × every cell)
- `CloudShadows.hlsl` via `SampleCloudShadowDensity` (separate GPU path — not relevant here)

Estimated call volume during planet generation:

- 6 faces × Resolution² vertices × N noise layers
- At default Resolution=10: 600 × ~4 layers = ~2,400 noise evals → trivial
- At Resolution=128 (common for visible detail): 98,304 × ~4 = ~400,000 noise evals
- At Resolution=256: 393K × ~4 = **1.5M noise evals just for terrain shape**

Plus weather grid initial: ~98K cells × 3 noise fields × 5 octaves = **~1.5M more**.

The function uses `double` math, accesses a managed `int[]` (`_random`), and a managed `int[][]` (`Grad3` jagged array). Burst-compiling this is the single highest-leverage optimization — every system that calls it benefits.

**Refactor required:**

- Convert `Noise` from class to `struct` or `IBurstCompile`-able static class
- `Grad3` jagged array → flat `int4` or `float3` static array (Burst can't access managed `int[][]`)
- `_random` int array → NativeArray or static readonly cached `int[]` (Burst can't dereference managed arrays directly)
- Switch `double` → `float` where precision allows (Burst SIMD prefers `float`)
- Wrap in `[BurstCompile]` static helper

**Risk:** Medium. The function is a self-contained algorithm with no Unity API calls. Refactoring is well-bounded. The seed initialization (`Randomize`) can stay managed (one-shot).

**Win:** Probably 5–10× per call (Burst-compiled simplex noise is famously much faster than managed C#). With ~3M calls in planet gen, that's seconds of generation time saved.

**Feedback:** I agree with this, proceed.

---

### PERF-02 🔴 Critical — `TerrainFace.CalculateMeshData` is single-threaded per face

**File:** [Assets/Scripts/Planet/TerrainFace.cs:55-92](../../Assets/Scripts/Planet/TerrainFace.cs#L55-L92)

The outer 6-face loop in `Planet.GenerateMeshAsync` uses `Parallel.For`, so faces run in parallel. But each face's inner `for(y) for(x)` loop is sequential. Each iteration:

1. Computes `pointOnUnitCube` and `pointOnUnitSphere` (cheap math)
2. Calls `_terrainProvider.EvaluateElevation(pointOnUnitSphere)` — the noise hot path
3. Writes to `_pendingVertices`, `_pendingTriangles`, etc.

At Resolution=128, each face does 16,384 iterations sequentially. With 6 faces in parallel, that's 6 cores doing 16K iterations each. A modern desktop has 12–16 cores — most are idle.

**Refactor:**

- Convert per-face vertex loop to `IJobParallelFor` with Burst
- Output goes to `NativeArray<float3>` for vertices, `NativeArray<int>` for triangles, etc.
- Triangle writes have a fixed pattern (no dependency between iterations) — fully parallelizable
- Vertex writes also independent
- `ShapeGenerator._workingMinMax` accumulation needs synchronization → use a per-job local min/max then reduce, or atomic min/max (Burst supports `InterlockedMin`/`Max`)
- After all faces+all vertices: `_pendingVertices` etc. get assigned to the mesh on main thread (already the case)

**Depends on PERF-01:** This only pays off if `EvaluateElevation` (which calls `Noise.Evaluate`) is also Burst-compatible.

**Win:** With 12+ cores, single-face parallelization unlocks ~4–8× speedup _on top of_ whatever PERF-01 gives. So combined with PERF-01: potentially 20–80× faster terrain mesh generation.

**Feedback:** I agree with this, proceed.

---

### PERF-03 🟡 High — `SphericalWeatherGrid.ComputeGridData` is single-threaded

**File:** [Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs:149-237](../../Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs#L149-L237)

Triple-nested loop: `for(face=0..6) for(y=0..res) for(x=0..res)`. Default `WeatherResolution` clamped to 32–512, typically 64–128. At resolution 128, that's 98,304 iterations. Each does ~3 FBM noise calls (`largeFronts`, `smallFronts`, `climate`) plus simple math.

Runs once per planet generation. Currently single-threaded on a background thread (better than blocking the main thread, but worse than parallel + Burst).

**Refactor:**

- Convert outer loop to `IJobParallelFor` with index = face × res² + y × res + x
- Output to `NativeArray<float>` for each channel (condensation, storm, moistureSource, etc.) — already flat indexed in current code
- The `PixelsByFace[face][pixelIndex]` Color buffer is for the upload-to-texture step — can stay as `NativeArray<Color>` or `NativeArray<half4>` per face
- Three `Noise` instances (`frontNoise`, `detailNoise`, `climateNoise`) need to be Burst-compatible (PERF-01)

**Depends on PERF-01.**

**Win:** Probably 5–10× from parallelization alone, multiplied by PERF-01's noise speedup. The whole weather grid initial gen could go from hundreds of milliseconds to tens.

**Feedback:** I agree with this, proceed.

---

### PERF-04 🟡 High — Water mesh is rendered at one fixed resolution from any distance

**Files:** [Assets/Scripts/Planet/WaterMeshBuilder.cs](../../Assets/Scripts/Planet/WaterMeshBuilder.cs), [Assets/Scripts/Planet/Planet.cs:324-403](../../Assets/Scripts/Planet/Planet.cs#L324-L403) (water generation), no LOD anywhere

The water mesh is a single `Mesh` per planet built at the planet's `Resolution`. From the F10 captures: **217,960 verts / 419,257 tris**. Every frame, this entire mesh is rendered.

Problem:

- **From orbit:** the whole planet is visible. The far-side water is back-face culled by URP automatically — fine. But the front-hemisphere ~210K verts still go through the vertex shader (including the swell-displacement work we shipped). That's all wasted detail because at orbital distance, one mesh triangle is sub-pixel.
- **From surface:** only the foreground ~5% of the mesh is visible at meaningful size. The rest is far-distance triangles being rasterized at sub-pixel sizes — also wasted, and also paying the full fragment shader cost (foam, caustics composite, atmosphere bypass, etc.).

**Refactor options (pick one):**

A. **Distance-based mesh swap.** Generate 2–3 LODs at planet-gen time (full / half / quarter resolution). Switch by camera distance. Cheapest implementation, biggest win because it directly cuts vertex _and_ fragment count for distant water.

B. **Geometric tessellation in vertex shader.** Use SV_TessFactor + hull shader. Higher quality but requires geometry-shader-friendly hardware and more shader complexity. Probably overkill.

C. **Per-face mesh splitting.** Split the global water mesh into 6 face submeshes; cull individual faces by frustum / facing test. Doesn't reduce visible triangle count per se but lets URP cull whole faces early. Easy win on top of A.

**Recommended:** A + C. A gives the big win; C piles on small wins for free.

**Win:** Hard to estimate without profiling, but for orbital view this could realistically be 5–10× fewer vertices submitted. Fragment cost for distant water also drops because the mesh is smaller in screen space.

**Feedback:** I worry with option C here, we could get some edge case scenarios where the water crosses faces and then we have odd wave artifacts. I prefer option B, the correct way to do this - especially because we plan to add boats later and large ocean wave swells that move with weather. Think Valhiem. I am open to talk about this in more details.

### PERF-05 🟡 High — Terrain mesh is rendered at one fixed resolution from any distance

**File:** [Assets/Scripts/Planet/TerrainFace.cs](../../Assets/Scripts/Planet/TerrainFace.cs), [Assets/Scripts/Planet/Planet.cs](../../Assets/Scripts/Planet/Planet.cs)

Same shape as PERF-04 but for terrain. 6 face meshes at fixed `Resolution`, no LOD. Resolution 128 per face × 6 faces = 98K verts × 6 = **589K terrain verts** per planet, rendered every frame.

The reference projects in `local-only/` (LOD-Planets-in-Unity-master, Geographical-Adventures-main) implement quad-tree LOD for cube-sphere planets — this is the standard approach. Big project to implement properly (chunk loading/unloading, seam handling, async chunk generation), but the gains are huge.

**Two implementation paths:**

A. **Simple distance LOD** — same as PERF-04A: generate 2–3 fixed LODs, switch by camera distance. Quick win.

B. **Quadtree chunking** — proper cube-sphere LOD with chunks generated on demand by distance + screen-space size. Big architectural change. The reference projects show the pattern.

**Recommended:** Start with A. B is a multi-week project that should be its own dedicated effort, not part of this audit's scope.

**Win:** With A, ~3–5× fewer terrain verts in orbital views. With B, an order of magnitude beyond that.

**Feedback:** B has always been the plan here. And the terrain generation needs a bit more work to handle cliffs and overhangs correctly - also marching-cubes and SDF for terrain manipulation. Pluse grass and trees, rocks, buildings, lots of other assets need to be placed on the terrain - so this system needs to be solid. We can discuss more as needed.

---

### PERF-06 🟢 Medium — `CloudController.SetGlobalProperties` runs every frame

**File:** [Assets/Scripts/Planet/Clouds/CloudController.cs:113-190](../../Assets/Scripts/Planet/Clouds/CloudController.cs#L113-L190)

~40 `Shader.SetGlobal*` calls every `Update()`. Most of these values _don't change between frames_ — only the view-step count adapts to camera altitude. The rest of the settings are static unless the user edits the inspector.

**Refactor:**

- Cache `Settings` values into a struct
- Only re-upload when `Settings` reference changes or values are dirtied
- Always update the altitude-dependent `viewSteps` (it does change per frame)

**Win:** Small per-frame CPU win (~40 Shader.SetGlobal calls collapsed to a single dirty check + 1 SetGlobal per frame for viewSteps). Worth doing for cleanliness but not a major win.

**Feedback:** We don't need to edit these values in the inspector at all, we can definitely clean this up.

---

### PERF-07 🟢 Medium — Caustics fragment cost is high and unconditional

**File:** [Assets/Graphics/Shaders/WaterVolume.shader CausticChromaticPattern](../../Assets/Graphics/Shaders/WaterVolume.shader)

The rework I shipped this session does **~27 voronoi evaluations per fragment** (3 layers × 3 chromatic samples × 3 triplanar axes). Caustics are mask-gated (only computed where `caustics.mask > 0`), so they don't fire for fragments where there's no water-over-terrain. But where they _do_ fire, it's expensive.

**Refactor options:**

A. **Distance-LOD the chromatic.** At long view distances, drop from 9 samples-per-axis to 3 (just the green channel). Hand-bake chromatic shift to a fast approximation at distance.

B. **Distance-LOD the voronoi layer count.** At long view distances, drop layer 3 (the finest detail). 2 layers × 3 chromatic × 3 axes = 18 → 33% reduction.

C. **Quality knob.** Expose `_CausticsQuality` (0–1) that fades the third layer at LOW; QualityController hooks it.

**Win:** Mostly benefits shallow-water orbital views. Smaller win than PERF-04/05 because caustics fragments are usually a small fraction of screen.

**Feedback:** I am good with A and B, so long as there is a fade transition and no visible "pop" in detail

---

### PERF-08 🟢 Medium — `WaterDebugModule.RefreshWaterDebugStats` iterates the entire water mesh

**File:** [Assets/Scripts/Core/Services/WaterDebugModule.cs:533-622](../../Assets/Scripts/Core/Services/WaterDebugModule.cs#L533-L622)

Iterates all 217K water mesh vertices to compute average/min/max stats per F6 overlay refresh. Throttled to once per 0.75 seconds — so amortized it's not a huge cost, but each _burst_ is a noticeable CPU spike.

**Refactor:**

- Compute the stats once per mesh build (in `WaterMeshBuilder`), cache results, refresh only when the mesh changes
- The overlay code then just reads cached values

**Win:** Eliminates a 217K-iteration loop from every ~0.75s frame when the debug overlay is on. Negligible when overlay is off (overlay is debug-only).

**Feedback:** I am fine with this, it's debug data - not game related. Eventually we should dial it further back or remove it all together.

---

### PERF-09 🟢 Low — `Planet.TryGetSurfaceRadius` does linear search through every vertex of every face

**File:** [Assets/Scripts/Planet/Planet.cs:290-322](../../Assets/Scripts/Planet/Planet.cs#L290-L322) calling [TerrainFace.TryGetNearestSurfaceRadius:23-42](../../Assets/Scripts/Planet/TerrainFace.cs#L23-L42)

For each call: 6 faces × Resolution² vertices = brute-force nearest-direction search. At Resolution=128 that's 98K dot products per query.

Used by camera surface-clamping in `FreeCameraController` — called _per frame_ when in surface view mode.

**Refactor:**

- Spatial acceleration: cube-face lookup based on direction's dominant axis cuts the search by 6×
- Hierarchical: store coarse-grid bounding spheres, refine into the matching cell
- Or precompute a low-res sampling grid that returns approximate surface radius for any direction

**Win:** Eliminates a 98K-iteration loop from every camera frame in surface view. Solid per-frame win.

**Feedback:** What is this used for? Can we use SDF here? If a low-res sampling grid would work, maybe SDF will also? We can talk about what this is for and what is the best move.

---

## Profiler capture recommendations

Before committing to implementation, capture profiler data from these scenarios so we know whether the audit's guesses about call volume + cost are right. **What to capture:** Unity Profiler `.data` files (Window → Analysis → Profiler → File → Save) AND a GPU profiler frame from RenderDoc or Unity Frame Debugger for the per-frame scenarios.

1. **Cold planet generation** — from game start to `PlanetGeneratedEvent`. Confirms cost split between `TerrainFace.CalculateMeshData`, `WaterMeshBuilder.Compute`, `SphericalWeatherGrid.ComputeGridData`. Tells us whether PERF-01/02/03 are actually the big costs or if I missed something.
2. **Orbital view per-frame** — camera in space, full planet centered. Tests PERF-04 (water mesh LOD) and PERF-05 (terrain LOD) hypotheses. Should show heavy vertex throughput.
3. **Surface view per-frame** — camera at sea level, looking forward across water and terrain. The most-played view. Mixed CPU/GPU. Tests PERF-09 (camera surface-clamping) and the water/cloud fragment cost.
4. **Underwater per-frame** — camera below sea level, looking around. The volume shader is dominant here; caustics fire on terrain bottom. Tests PERF-07.
5. **Sunset surface view** — like the captures we did this session. Atmosphere is heaviest here.

For each: ~10 seconds of profiler data so we can see frame-time variance.

**Feedback:** I do think the profiler would give some good information, but most of these are no-brainers that we had planned to do anyway.

---

## Agreed sprint plan (2026-05-30)

After review, the sprint scope is **Burst/Jobs/cleanup**. LOD-rendering work (PERF-04, PERF-05, PERF-07) is pulled out into dedicated future projects so it can be done deliberately alongside related systems (boats, weather swells, SDF terrain).

### In this sprint

1. **PERF-01 — Noise Burst-ification.** Highest leverage. Sets up 02 and 03 to pay off.
2. **PERF-03 — Weather grid Jobs+Burst.** Smaller piece, validates the Job pattern.
3. **PERF-02 — Terrain Jobs+Burst.** Bigger change, but pattern is proven by this point.
4. **Cold-gen profiler capture** — single before/after to put a number on the win.
5. **PERF-06 — Cloud `SetGlobalProperties` dirty cleanup.** No inspector editing needed; safe to make static.
6. **PERF-08 — Water debug stats cached at mesh build.** Debug-only path; trivial.
7. **PERF-09 — Coarse direction→radius lookup grid (interim).** Replaces the 98K-vert linear search with a bilinear lookup against a precomputed coarse grid. This is a temporary solution that gets replaced by SDF terrain queries when PERF-05 (terrain SDF/quadtree) lands.

### Deferred to dedicated future projects

- **PERF-04 → "Ocean tessellation + boat-ready wave query" project.** Hull/domain shader rewrite of Ocean.shader with screen-space tessellation factor, paired with a CPU-side wave-height query function that boats can use for buoyancy. Continuous LOD, no chunk seams. Done alongside or just before boats/weather-swells work.
- **PERF-10 → bundled with the ocean tessellation project.** `WaterMeshBuilder.Compute` takes ~10s at Resolution=128 (measured after PERF-01/02/03 made terrain gen sub-second). The cost is structural: phase 5 (`ProcessFace`) does marching-cubes-style edge clipping with **6 shared dictionaries** for cross-face vertex deduplication and a sequential output `List<Vector3>` appended across all 6 faces. Can't be cleanly Burst-ified or parallel-for'd without becoming a NativeHashMap + per-face NativeList + reduce rewrite. Since the ocean tessellation project replaces this whole pipeline (the mesh becomes a coarse base + GPU-tessellated detail), the right move is to wait rather than rewrite something we're about to throw away. Tolerable cost during planet load until then.
- **PERF-05 → "Terrain SDF/quadtree + placement system" project.** Multi-week effort. Quadtree LOD + marching cubes/SDF for cliffs/overhangs + asset placement (grass/trees/rocks/buildings). PERF-09 collapses into this — SDF query naturally replaces the radius lookup.
- **PERF-07 → tag along with the ocean tessellation project.** Distance-fade the caustic chromatic and layer count with smooth `smoothstep` transitions so there's no visible pop.

### Profiler captures

Most findings in this sprint are provably hot from static analysis alone, so we skip the full 5-scenario capture matrix. We do capture **one cold-gen Unity Profiler trace** before starting PERF-01 and **one after** PERF-01/02/03 are done, so we can prove the win with a number rather than vibes.

---

## Original recommended order (superseded by sprint plan above)

1. PERF-01 (Noise Burst)
2. PERF-03 (Weather grid Burst+Jobs)
3. PERF-02 (Terrain Burst+Jobs)
4. Measure
5. PERF-04 (Water LOD) — _now deferred to ocean tessellation project_
6. PERF-05 (Terrain LOD) — _now deferred to terrain SDF/quadtree project_
7. Measure again
8. PERF-06, PERF-07, PERF-08, PERF-09 — _07 deferred; 06/08/09 in this sprint_

---

## Risks / things to watch

- **Burst gotchas.** Managed types (class, string, jagged arrays) don't work. NativeContainers leak if Dispose is skipped. Burst version of a function can subtly diverge from C# version due to floating-point semantics — keep the C# version as a reference and unit-test that both produce the same noise output for the same input.
- **NativeContainer lifetime.** Job results live in NativeArrays that need disposal after the job completes. Pattern: schedule job → complete → copy out → dispose. Easy to forget.
- **LOD seam artifacts.** Different LOD resolutions for adjacent water/terrain produce visible seams unless handled. Standard approach: skirts, morphing, or chunk-boundary handling. The reference projects show the patterns.
- **Profile then optimize.** Most important — don't skip the profile step. The recommendations above are educated guesses; the profiler tells the truth.

---

## What this audit is NOT

- Not a list of bugs (the bug audit is `2026-05-28.md`).
- Not GPU shader optimization (the shader audit is `2026-05-28-shaders.md`; PERF-07 references it).
- Not architecture refactoring beyond what's needed for Burst/Jobs.
- Not a quadtree LOD project (PERF-05's option B is called out as out-of-scope here).

The scope is: identify where to spend Burst/Jobs/LOD effort for the biggest wins, in priority order. Implementation is per-finding, one at a time, measured before/after.
