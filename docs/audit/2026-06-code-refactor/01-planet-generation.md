# Audit — Planet generation, surface & chunk pipeline

**Date:** 2026-06-10
**Branch:** phase8-spawning-foundation (sub-branch of code-refactor arc)
**Auditor:** Claude (subagent)
**Scope:**
- `Assets/Scripts/Planet/Planet.cs`
- `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`
- `Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs`
- `Assets/Scripts/Planet/Surface/GpuChunkSurfaceProvider.cs`
- `Assets/Scripts/Planet/Surface/PlanetChunk.cs`
- `Assets/Scripts/Planet/Surface/PlanetChunkMeshJob.cs`
- `Assets/Scripts/Planet/Surface/TerrainQuadtree.cs`
- `Assets/Scripts/Planet/Surface/TerrainQuadtreeSelfTest.cs`
- `Assets/Scripts/Planet/Surface/PlanetChunkMeshJobSelfTest.cs`
- `Assets/Scripts/Planet/Surface/ChunkedFaceMeshSampler.cs`
- `Assets/Scripts/Planet/Surface/CombinedFaceMesh.cs`
- `Assets/Scripts/Planet/Surface/ChunkTriangleTemplate.cs`
- `Assets/Scripts/Planet/Surface/ChunkUvTemplate.cs`
- `Assets/Scripts/Planet/Surface/GrassSurfaceAtlasGpuData.cs`
- `Assets/Scripts/Planet/TerrainFace.cs`
- `Assets/Scripts/Planet/ShapeGenerator.cs`
- `Assets/Scripts/Planet/ColorGenerator.cs`
- `Assets/Scripts/Planet/Noise.cs`
- `Assets/Scripts/Planet/ShapeSettings.cs`
- `Assets/Scripts/Planet/PlanetSettings.cs`
- `Assets/Scripts/Core/Services/GameBootstrap.cs`
- `Assets/Scripts/Core/Services/LoadingManager.cs`
- `Assets/Scripts/Core/Interfaces/IPlanetSurfaceProvider.cs`
- `Assets/Editor/PlanetEditor.cs`

**Status:** Findings only — no code modified.

## Executive summary

Compared to the 2026-05-28 baseline the architectural backbone is intact — `IPlanetSurfaceProvider` is the right seam, `Awaitable` is used end-to-end, the Burst job paths in `PlanetChunkMeshJob` and `TerrainFaceMeshJob` are clean, and slice 1/2 of the perf-maintainability plan have visibly reduced retained CPU/GPU memory. The biggest *new* themes since the baseline are (1) `Planet.cs` has accumulated a second major responsibility — runtime grass-LOD orchestration with four controllers and altitude hysteresis — pushing it well past 1,000 lines and into clear god-class territory; (2) `ChunkedSurfaceProvider.cs` at 2,146 lines (with a ~150-line dead `#if false` boundary-normal-smoothing block) is the single largest file in the subsystem and owns generation, mesh upload, visibility, biome baking, atlas construction, raycasting, diagnostics, and disposal — slice 4 of the perf plan already calls this out; (3) the Settings DTO pattern is violated throughout: `Planet`, `PerFaceSurfaceProvider`, and `ChunkedSurfaceProvider` all read `PlanetSettings` / `ShapeGenerator.Settings` (the raw SO and SO-derived `ShapeSettings`) at runtime, not a snapshot DTO; and (4) three subsystem files inject themselves into the boot path via `RuntimeInitializeOnLoadMethod`, in direct violation of project rule #3. The biggest surprise was finding `CombinedFaceMesh.cs` and `GpuChunkSurfaceProvider.cs` both fully retained in the build but never instantiated by anything in `Planet.Initialize()` — they are dead code from earlier design pivots.

## Findings

### PLANET-1 🔴 Settings DTO pattern violated across the generation pipeline

- **Category:** Architectural drift
- **Severity:** 🟠 Architecture
- **Location:** [Planet.cs:271-298](../../Assets/Scripts/Planet/Planet.cs#L271-L298), [Planet.cs:437-446](../../Assets/Scripts/Planet/Planet.cs#L437-L446), [Planet.cs:527-548](../../Assets/Scripts/Planet/Planet.cs#L527-L548), [Planet.cs:806-849](../../Assets/Scripts/Planet/Planet.cs#L806-L849), [ChunkedSurfaceProvider.cs:1593](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1593), [ChunkedSurfaceProvider.cs:1868](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1868), [PerFaceSurfaceProvider.cs:57](../../Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs#L57)
- **Effort to fix:** L
- **Cross-ref:** Settings DTO pattern memory; baseline 2026-05-28 did not flag this — the rule emerged later.

`Planet.cs` holds a `[SerializeField] PlanetSettings _planetSettings` (a `ScriptableObject`) and dereferences it directly from runtime code in dozens of places — every water build (`_planetSettings.PlanetRadius`, `_planetSettings.OceanLevel`, `_planetSettings.WaterColor`, `_planetSettings.FrozenWater`, `_planetSettings.HasOceans`), every grass-controller configuration (`_planetSettings.PlanetRadius`, `_planetSettings.HasOceans`, `_planetSettings.PlanetMaterial`), every climate sample (`_planetSettings.PlanetRadius`), every surface-override push (`_planetSettings.SurfaceOverrides`, `_planetSettings.BiomeSettings.Registry`). The two `if (_planetSettings != null)` guards in `ApplyGrassBlanketState` show the runtime code is even prepared for the SO to be missing. `PerFaceSurfaceProvider` and `ChunkedSurfaceProvider` also reach into `ShapeGenerator.Settings.PlanetRadius` — the `ShapeSettings` they pass through is built fresh each generation via `_planetSettings.BuildShapeSettings()` and is itself a runtime `ScriptableObject` (`CreateInstance<ShapeSettings>()`), so the cross-coupling layers two SOs deep.

This is the single largest architectural debt in the subsystem. Once a save/load layer or a runtime planet-customisation path lands (Phase 9+), the lack of an immutable snapshot will be felt as cross-system mutation bugs.

**Proposed direction:** Add a `PlanetSnapshot` (or `PlanetRuntimeData`) record/struct built once at the top of `Planet.GeneratePlanetAsync` from the SO, and pass it (or the relevant slice) into `Initialize`, both surface providers, the grass controllers, the water builder, and the material/biome paths. Treat `_planetSettings` as editor-only state after that point. This naturally subsumes the `ShapeSettings.CreateInstance` allocation per regen.

### PLANET-2 🟠 `Planet.cs` has become a god-class spanning generation, water, and grass-LOD orchestration

- **Category:** Cross-coupling
- **Severity:** 🟠 Architecture
- **Location:** [Planet.cs:1-1049](../../Assets/Scripts/Planet/Planet.cs)
- **Effort to fix:** L
- **Cross-ref:** Perf plan slice 6 already names `Planet` as a split target — this finding sharpens what to split.

`Planet.cs` is 1,049 lines and implements eight interfaces (`IPlanet`, `IPlanetSurfaceSampler`, `IPlanetSurfaceRaycaster`, `IClimateSampler`, `IGrassRuntimeControl`, `IEarlyInitialize`, `ILateInitialize`, `IProgressReporter`). Its `Update()` ticks the surface provider AND drives four grass controllers' activation/deactivation hysteresis. Its `OnDestroy` disposes seven independently-owned subsystems. Beyond the generation orchestration that the baseline noted, the file now also owns:

- ~56 water-related shader-property ids + 27 named water constants ([lines 53-148](../../Assets/Scripts/Planet/Planet.cs#L53-L148));
- the entire `GenerateWaterAsync` pipeline including mesh build, material creation, and freeze-state global writes ([lines 800-983](../../Assets/Scripts/Planet/Planet.cs#L800-L983));
- the four-tier grass controller lifecycle (chunk + near + mid + blanket) with altitude-based hysteresis activation ([lines 515-719](../../Assets/Scripts/Planet/Planet.cs#L515-L719));
- a private nested `ProgressRangeHandle` for sub-progress reporting ([lines 993-1015](../../Assets/Scripts/Planet/Planet.cs#L993-L1015)).

The grass-orchestration block in particular is independent of generation and could move to a `PlanetGrassController` MonoBehaviour that listens for `PlanetGeneratedEvent`. The water block similarly has no shared state with planet generation beyond `_planetSettings`, the surface samplers, and the shape generator.

**Proposed direction:** Split into (1) `Planet` orchestrator that owns generation, raycasts, climate sampling, and the surface provider; (2) `PlanetWaterController` that owns water mesh build, material, and the water shader globals; (3) `PlanetGrassOrchestrator` that owns the four grass controllers and their altitude hysteresis. The latter two listen for planet events and resolve the surface provider via `ServiceLocator`. Sequence after slices 3-4 land per the plan.

### PLANET-3 🟠 `ChunkedSurfaceProvider.cs` owns nine responsibilities (slice 4 work)

- **Category:** Cross-coupling
- **Severity:** 🟠 Architecture
- **Location:** [ChunkedSurfaceProvider.cs:1-2146](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs)
- **Effort to fix:** L
- **Cross-ref:** **Perf plan slice 4** — exact split already prescribed. Listed here only to confirm it's still warranted; do not duplicate the work.

The file is 2,146 lines (1,998 if you exclude the dead `#if false` block — see PLANET-5) and implements `IPlanetSurfaceProvider`, `IChunkVisibilitySource`. The split prescribed in slice 4 (`ChunkSurfaceGenerator`, `ChunkRenderCache`, `ChunkVisibilityResolver`, `BiomeAtlasBuilder`, `ChunkSurfaceQueries`) maps cleanly onto the existing internal sections and would also pull `PlanetChunkTextures` (currently nested in `PlanetChunk.cs`) into the atlas builder where it belongs.

One observation worth carrying into slice 4: the `_tlsBakeHighResBuffer` `[ThreadStatic]` ([line 90](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L90)) crosses the future `BiomeAtlasBuilder` boundary — it'll need to live with the bake code, not the provider.

**Proposed direction:** Execute slice 4 as written. Suggest pulling `PlanetChunkTextures` into the new `BiomeAtlasBuilder` file at the same time so the texture lifecycle has a single owner. No new analysis needed.

### PLANET-4 🟠 Three out-of-band `RuntimeInitializeOnLoadMethod` self-tests inject themselves into the boot path

- **Category:** Architectural drift
- **Severity:** 🟠 Architecture
- **Location:** [TerrainQuadtreeSelfTest.cs:19-25](../../Assets/Scripts/Planet/Surface/TerrainQuadtreeSelfTest.cs#L19), [PlanetChunkMeshJobSelfTest.cs:21-26](../../Assets/Scripts/Planet/Surface/PlanetChunkMeshJobSelfTest.cs#L21), `Assets/Scripts/Planet/Biomes/BiomeLookupSelfTest.cs:20` (sibling subsystem)
- **Effort to fix:** S
- **Cross-ref:** Project rule #3 (boot path must go through `GameBootstrap`/`LoadingManager`).

Both `TerrainQuadtreeSelfTest` and `PlanetChunkMeshJobSelfTest` are gated behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` and run heavy Burst-job allocations + scheduling at `BeforeSceneLoad`. They report failures via `Debug.LogError` (also a 🔵 violation of project rule #5, demoted). This directly violates the project rule about `GameBootstrap`/`LoadingManager` owning the init phases.

There are two existing reasonable `RuntimeInitializeOnLoadMethod` entry points (`LoadingManager.CreateInstance` and `EventBusProcessor` infra) — the self-tests are *not* in that category; they're test fixtures. `CubeFaceTopology` also uses one for a self-test which is out-of-scope here.

`Test.cs` and `TestPoissonDiscSphereDraw.cs` (the editor-only fixtures the baseline flagged in CONV-01/02 + IMP-08) are still there in the working tree at the root of `Assets/Scripts/`. Bryan's testing-stance memory says not to push a test framework, but these fire-and-forget runtime self-tests are *not* a framework — they're stale fixtures that should be deleted or moved.

**Proposed direction:** Delete both `*SelfTest.cs` files. The invariants they protect (hash bits, triangle template, vertex midpoint snapping) are stable foundations that haven't shifted since Phase A and will not regress without an explicit job-code change. Per Bryan's testing stance, do not replace them with a test-framework alternative — just delete.

### PLANET-5 🟡 Dead `CombinedFaceMesh` class and `#if false` block in `ChunkedSurfaceProvider`

- **Category:** Style & dead code
- **Severity:** 🟡 Quality
- **Location:** [CombinedFaceMesh.cs](../../Assets/Scripts/Planet/Surface/CombinedFaceMesh.cs), [ChunkedSurfaceProvider.cs:1999-2144](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1999-L2144)
- **Effort to fix:** S
- **Cross-ref:** None (new since baseline; both predate the 2026-05-30 pre-cache pivot).

`CombinedFaceMesh.cs` is fully compiled but has zero callers in the codebase (`Grep CombinedFaceMesh` returns only the file itself). It belongs to the per-frame mesh-aggregation design that was abandoned when `ChunkedSurfaceProvider` switched to pre-cache + visibility-toggling on 2026-05-30. The `#if false` block at the bottom of `ChunkedSurfaceProvider.cs` ([lines 1999-2144](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1999-L2144)) is the boundary-normal-smoothing code that worked against `_combinedMeshes` — same provenance. The block references fields (`_combinedMeshes`, `_smoothFaceNormalsScratch`, `_smoothNeighborNormalsValid`, `_smoothNeighborNormalsBuffers`, `_smoothPosToNormalScratch`, `EdgeVertexIndices`, `SpatialHashScale`) that don't exist anywhere else.

Both also leak narrative cost: anyone reading the chunked path has to figure out whether `CombinedFaceMesh` is still part of the active design (it isn't) and what the `#if false` block was supposed to do.

**Proposed direction:** Delete `CombinedFaceMesh.cs` and the `#if false` block. If boundary-normal smoothing returns later, it will need a fresh design against the pre-cache + per-chunk-renderer architecture anyway.

### PLANET-6 🟡 `GpuChunkSurfaceProvider` is a fully-compiled experimental dead path

- **Category:** Style & dead code
- **Severity:** 🟡 Quality
- **Location:** [GpuChunkSurfaceProvider.cs](../../Assets/Scripts/Planet/Surface/GpuChunkSurfaceProvider.cs)
- **Effort to fix:** S–M (depends on Bryan's intent)
- **Cross-ref:** None.

The `PlanetResolution` switch in `Planet.Initialize` ([lines 281-298](../../Assets/Scripts/Planet/Planet.cs#L281-L298)) only constructs `PerFaceSurfaceProvider` (Low) or `ChunkedSurfaceProvider` (High); the `default` case throws. `GpuChunkSurfaceProvider` therefore has no live instantiation in the project. Both `UseFullNoiseKernel` and `UseGpuReadback` are `static readonly` `false`, meaning even if it were instantiated the `BuildProofSamplers` path (analytic placeholder elevation) would always run instead of real GPU readback. The file references a compute shader resource `Resources/GpuPlanetTerrain.compute` and a shader `Planet/GpuChunkPatch` that I did not separately verify exist.

This is either (a) a parking spot for the future GPU planet path that should stay, or (b) an abandoned experiment.

**Open question for Bryan** (also restated in the section below): keep, gate behind `#if PLANET_GPU_EXPERIMENT`, or delete? Recommend gating + flag rather than deletion.

**Proposed direction:** Wrap with `#if PLANET_GPU_EXPERIMENT` (or remove from the asmdef) so it doesn't compile into Player builds while the experiment is paused.

### PLANET-7 🟡 `ApplyChunkColors` calls `UploadMeshData(true)` per chunk on the main thread

- **Category:** Per-frame / generation hot path
- **Severity:** 🟡 Quality
- **Location:** [ChunkedSurfaceProvider.cs:1540-1550](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1540-L1550)
- **Effort to fix:** S
- **Cross-ref:** Perf-maintainability plan, slice 3 (mesh ownership) — adjacent but distinct concern.

`ApplyChunkColors` calls `handle.Mesh.UploadMeshData(true)` on every chunk in every color batch (96 chunks per batch, batched across ~2,046 chunks at depth 4). Passing `true` marks the mesh as non-readable after upload, which prevents any later CPU operation on it. Empirically this works because chunk meshes aren't re-read post-color-bake under the current design, but it (a) precludes future debug captures of chunk geometry, and (b) `UploadMeshData(true)` calls are themselves synchronous main-thread stalls and add up across 2,046 chunks.

The mesh data was already uploaded once in `EnsureChunkRenderer` (verts/normals/uvs/triangles). The color/biome upload here just adds two vertex streams (colors + UV2). Passing `false` keeps the option to debug-inspect later and skips a redundant fence.

**Proposed direction:** Change to `handle.Mesh.UploadMeshData(false)`. Alternatively skip `UploadMeshData` entirely and let Unity batch the upload at the next render — `SetColors` + `SetUVs` already enqueue a GPU sync.

### PLANET-8 🟡 `BiomeLookupData` allocator mismatch between bake and rebake paths

- **Category:** Style / consistency
- **Severity:** 🟡 Quality
- **Location:** [ChunkedSurfaceProvider.cs:419](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L419), [ChunkedSurfaceProvider.cs:983](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L983)
- **Effort to fix:** S
- **Cross-ref:** None.

`GenerateColorsAsync` builds the lookup with `Allocator.Persistent` and disposes it in `finally` after the full bake (potentially many seconds). `RebakeBiomeMapsAt` (Phase E entry point) builds the same lookup with `Allocator.TempJob` for a single-chunk rebake. The intent reads correctly — TempJob for short-lived, Persistent for long-lived — but `TempJob` has a 4-frame leak detector and is the wrong choice if the rebake is ever awaited across a frame boundary. Today it isn't (`BakeChunkBiomeMap` is synchronous), but a future Phase E expansion to a radius of chunks is likely to want either async semantics or batched scheduling, at which point the `TempJob` becomes a latent bug.

**Proposed direction:** Use `Allocator.Persistent` consistently and dispose in `finally`. The cost difference at lookup-data sizes is negligible vs the safety of not depending on the 4-frame rule.

### PLANET-9 🟡 `GetVisibleChunksSnapshot` allocates a fresh `List<PlanetChunk>(128)` per call

- **Category:** Per-frame hot path / allocations
- **Severity:** 🟡 Quality
- **Location:** [ChunkedSurfaceProvider.cs:270-289](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L270-L289)
- **Effort to fix:** S
- **Cross-ref:** Slice 5 of the perf plan touches the broader "remove no-op work" theme this fits in.

`GetVisibleChunksSnapshot()` is called by grass controllers and any other consumer that wants the current visible-leaf set. Each call allocates a new `List<PlanetChunk>(128)` and never reuses it. The signature returns `IReadOnlyList<PlanetChunk>`, so the caller can't see the underlying storage. With three grass controllers potentially calling this per frame (chunk + near + mid), we're allocating 3 lists/frame just for residency queries (this overlaps with `GetGrassResidencyChunks`, which correctly accepts an `output` list to reuse).

**Proposed direction:** Mirror the `GetGrassResidencyChunks` pattern — replace with `GetVisibleChunksSnapshot(List<PlanetChunk> output)` that clears+fills the caller's list. Alternatively, expose the (already-maintained) `_visibleLeavesPerFace` as a `IReadOnlyList<IReadOnlyList<PlanetChunk>>` view, since most callers can iterate per-face without flattening.

### PLANET-10 🟡 `GatherVisibleLeaves` recursion allocates no GC but performs redundant per-chunk world-bounds math per frame

- **Category:** Per-frame hot path
- **Severity:** 🟡 Quality
- **Location:** [ChunkedSurfaceProvider.cs:1748-1763](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1748-L1763), [ChunkedSurfaceProvider.cs:1881-1917](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1881-L1917), [ChunkedSurfaceProvider.cs:1892-1900](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L1892-L1900)
- **Effort to fix:** M
- **Cross-ref:** Perf plan slice 5 (timing counters); this finding scopes a measurable target.

Each Tick traverses all six quadtrees and, for every visited node, runs `EstimateWorldBounds` (TransformPoint + lossyScale read + bounds construction), `GeometryUtility.TestPlanesAABB`, `IsChunkAboveHorizon` (two normalised dots), and `ShouldSubdivide` (which calls `EstimateWorldBounds` again). With ~2,046 nodes at depth 4, much of which is descended into during early frames, the per-frame cost adds up.

Two specific points:

1. `EstimateWorldBounds` is called twice per visited node when both `IsChunkVisibleCandidate` and `ShouldSubdivide` are invoked. Same result both times.
2. The planet transform is static at runtime (`Planet` lives at world origin in normal scene setup), so `TransformPoint` / `lossyScale` results are constant within a frame.

`PrepareLodContext` already caches per-frame camera state; extending it to cache `_planetTransform.position` + a uniform-scale value would let `EstimateWorldBounds` be a cheaper local-bounds → world-bounds offset.

**Proposed direction:** In `PrepareLodContext`, cache `_planetTransform.position` and `_planetTransformUniformScale`. Refactor `IsChunkVisibleCandidate` + `ShouldSubdivide` to share a single `EstimateWorldBounds` result per node (e.g. compute once in `GatherVisibleLeaves` and pass down). Quantify before/after via slice 5 counters.

### PLANET-11 🟡 `TryGetLocalSurfaceRadius` walks up the parent chain when the leaf has no CPU data

- **Category:** Style & dead code
- **Severity:** 🟡 Quality
- **Location:** [ChunkedSurfaceProvider.cs:322-340](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L322-L340)
- **Effort to fix:** S
- **Cross-ref:** Slice 1 of perf plan (chunk CPU array release).

Slice 1 of the active perf plan retains leaf elevation/radius/biome arrays after generation and releases the internal-node arrays. After that change, `while (leaf != null && leaf.CpuVertices == null) leaf = leaf.Parent;` ([line 331](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L331)) should never iterate — leaves always have `CpuVertices` because slice 1 keeps them. The walk-up is left over from the design before slice 1.

Currently harmless, but it (a) reads as suspicious code at the leaf-radius sampler, and (b) hides a bug class where a non-leaf node intentionally has data discarded but the caller walks to it.

**Proposed direction:** Either delete the walk-up (matching slice 1's invariant) or replace with an explicit assert that fails loudly if the invariant is broken. Same applies to similar walks in `IsChunkAboveHorizon` callers (haven't found them, but a code-review with this finding in mind would catch any).

### PLANET-12 🔵 `_lastGeneratedRadius` / `_lastSeaLevelRadius` / `_lastElevationMin/Max` `[HideInInspector]` fields are write-only state cached on the SO-bound instance

- **Category:** Style & dead code
- **Severity:** 🔵 Convention
- **Location:** [Planet.cs:25-28](../../Assets/Scripts/Planet/Planet.cs#L25-L28), [Planet.cs:160-161](../../Assets/Scripts/Planet/Planet.cs#L160-L161), [Planet.cs:439-442](../../Assets/Scripts/Planet/Planet.cs#L439-L442)
- **Effort to fix:** S
- **Cross-ref:** None.

`_lastGeneratedRadius`, `_lastSeaLevelRadius`, `_lastElevationMin`, `_lastElevationMax` are private `[SerializeField, HideInInspector]` fields. `_lastGeneratedRadius` and `_lastSeaLevelRadius` are exposed via `LastGeneratedRadius`/`LastSeaLevelRadius` public properties. `_lastElevationMin`/`Max` are *not* exposed publicly. The PlanetEditor doesn't reference them either (`PlanetEditor.cs` only uses `_settingsFoldout`). They're written but never read — the same values are already raised on `PlanetGeneratedEvent` for downstream consumers, and `ShapeGenerator.ElevationMin/Max` is the live source.

**Proposed direction:** Delete `_lastElevationMin` / `_lastElevationMax` and their write lines. Verify nothing in editor scripts reads via `SerializedObject.FindProperty` (a quick grep should confirm).

### PLANET-13 🔵 `PlanetEditor.GeneratePlanetAsync()` is called without await or exception handling

- **Category:** Style & dead code
- **Severity:** 🔵 Convention
- **Location:** [PlanetEditor.cs:20](../../Assets/Editor/PlanetEditor.cs#L20)
- **Effort to fix:** S
- **Cross-ref:** Baseline BUG-02 lineage (editor-only fire-and-forget pattern).

`_planet.GeneratePlanetAsync()` returns `Awaitable` but the editor button drops the result. Cancellation works only because `GeneratePlanetAsync` internally creates its own CTS, but exceptions raised inside the async chain disappear into the void — Bryan only sees them if `Logger.LogException` happens to be on the path *before* the exception is rethrown. The editor doesn't even pass `serializedObject.FindProperty`-backed progress to the inspector progress bar (it just shows `1f`).

This is identical in shape to BUG-02 from the 2026-05-25 audit (editor-only fire-and-forget). The baseline demoted that to ⚪. Same severity applies here.

**Proposed direction:** `_ = _planet.GeneratePlanetAsync();` with a discard suffices to silence the warning. Or use `EditorApplication.delayCall` to await safely. Out of scope to fix on its own.

### PLANET-14 ⚪ `Planet.ConfigureMaterial` mutates a shared `_planetSettings.PlanetMaterial` asset

- **Category:** Cross-coupling
- **Severity:** ⚪ Suggestion
- **Location:** [Planet.cs:308-330](../../Assets/Scripts/Planet/Planet.cs#L308-L330)
- **Effort to fix:** M
- **Cross-ref:** Compounds PLANET-1.

`ConfigureMaterial` and `ConfigureTerrainSurfaceOverrides` directly call `mat.SetFloat`, `mat.SetInt`, `mat.shader = ...` on `_planetSettings.PlanetMaterial`. Since the SO references a Material asset, the writes mutate the project asset, *not* a runtime instance. This is visible after every generation as dirtied-but-unsaved state on the asset in the editor, and means two planets in the same scene with the same `PlanetSettings` will silently fight over the material's shader-keyword state and surface override floats. The `_BIOME_COLOR_MODE_TEXTURE` keyword toggle in `ChunkedSurfaceProvider.GenerateColorsAsync` has the same problem.

This is "by design" today (the project ships one planet) but will bite as soon as a multi-planet scene exists. Flagging as low-severity for now; the fix is part of the broader Settings DTO refactor.

**Proposed direction:** Have `Planet` clone the material on first use (`new Material(_planetSettings.PlanetMaterial) { hideFlags = HideFlags.DontSave }`) and dispose in `OnDestroy`. Pair with PLANET-1.

### PLANET-15 ⚪ `BuildShapeSettings` allocates a fresh `ScriptableObject` on every generation

- **Category:** Per-frame / generation hot path / allocations
- **Severity:** ⚪ Suggestion
- **Location:** [PlanetSettings.cs:83-89](../../Assets/Scripts/Planet/PlanetSettings.cs#L83-L89), [Planet.cs:271](../../Assets/Scripts/Planet/Planet.cs#L271)
- **Effort to fix:** S
- **Cross-ref:** Compounds PLANET-1 (a snapshot DTO removes the SO allocation entirely).

`PlanetSettings.BuildShapeSettings()` calls `CreateInstance<ShapeSettings>()` on every `Planet.Initialize`. `ScriptableObject.CreateInstance` is more expensive than a plain `new` (it goes through Unity's serialized-object factory), and the returned instance is never released — it leaks as an unowned ScriptableObject for the lifetime of the application. Multiple regenerations during a Bryan-driven inspector workflow accumulate leaked `ShapeSettings` instances.

`ShapeSettings` itself is a plain data container that doesn't need to be a `ScriptableObject` — nothing serializes it as an asset, only `PlanetSettings` builds it.

**Proposed direction:** Convert `ShapeSettings` to a plain `[System.Serializable] class` (or struct) and have `BuildShapeSettings` return a new instance. If the existing `.asset` files for `ShapeSettings` matter, audit which they are first (`Glob *ShapeSettings*.asset` under `Assets/`). Subsumed by PLANET-1's snapshot DTO.

### PLANET-16 ⚪ `Planet.Awake` shader cache reads from `Shader.Find` are still per-instance and run at scene load before bootstrap

- **Category:** Cross-coupling
- **Severity:** ⚪ Suggestion
- **Location:** [Planet.cs:173-185](../../Assets/Scripts/Planet/Planet.cs#L173-L185)
- **Effort to fix:** S
- **Cross-ref:** Baseline QUAL-02 was resolved; this is a different concern (timing, not redundancy).

The static shader caches (`_vcShader`, `_oceanShader`, `_urpLitShader`, `_standardShader`) are correctly static now (QUAL-02 fix), but `Shader.Find` is still called in `Awake()` of each Planet instance — and `Awake()` runs before `GameBootstrap.EarlyInitialize`. If a future scene moves shader provisioning to a service (e.g. `ShaderRegistry` resolved via ServiceLocator), this ordering would break silently.

Today this is fine (the shaders ship with the project). It's flagged only because it appears in `Awake()` rather than `EarlyInitialize`, which makes the dependency invisible to anyone tracing the boot path.

**Proposed direction:** Move into `EarlyInitialize` alongside the existing `GrassInteractorRegistry.Initialize()` so the boot path captures the dependency.

## Cross-cutting themes

- **The Settings DTO violation is everywhere**: `Planet`, both surface providers, `ColorGenerator.Configure`, `Initialize`, water build, grass controller construction, climate sampling — every layer of the generation pipeline reaches directly into `PlanetSettings` (or transitively through `ShapeGenerator.Settings`). This is the single highest-leverage refactor in the subsystem and a prerequisite for clean multi-planet support, save/load, and runtime customisation.
- **`Planet.cs` and `ChunkedSurfaceProvider.cs` are both ~halfway through a god-class trajectory**. The perf plan already targets `ChunkedSurfaceProvider` (slice 4) and `Planet` (slice 6). PLANET-2 and PLANET-3 just add empirical detail. The grass-orchestration block in `Planet.cs` is independent enough to be lifted now without waiting for slice 6.
- **Dead/experimental code is accumulating after design pivots.** `CombinedFaceMesh`, the `#if false` smoothing block, `GpuChunkSurfaceProvider`, and the two `*SelfTest.cs` files are all artefacts of earlier directions. The longer they sit, the harder it is for a new reader (or a future Bryan) to know what's load-bearing.
- **Per-frame work is bounded but full of redundant local computation.** `EstimateWorldBounds` is called twice per node, `GetVisibleChunksSnapshot` reallocates a list, and the visibility walk recomputes static transform state every frame. None of these are crises but they're the natural targets for slice 5's counter-driven cleanup.

## Open questions for Bryan

1. **`GpuChunkSurfaceProvider` (PLANET-6)** — keep as a parked experiment behind a `#if PLANET_GPU_EXPERIMENT` symbol, exclude from the asmdef, or delete? My suggestion is `#if` gate. Needs your call before the next pass touches it.
2. **`ShapeSettings` as a `ScriptableObject` (PLANET-15)** — are there `.asset` files for it on disk that ship with the project (Phase 0 legacy)? If yes, the conversion to plain class needs an asset migration step. If no, it's a trivial change.
3. **Settings DTO scope (PLANET-1)** — do you want a single mega-snapshot (`PlanetSnapshot`) holding everything, or several narrow records (`PlanetGenerationDTO`, `PlanetWaterDTO`, `PlanetGrassDTO`)? The narrow approach pairs better with the PLANET-2 split.
4. **Self-test removal (PLANET-4)** — confirm deletion is fine given your "no tests" stance, or do you want them kept as fire-and-forget invariants with the `RuntimeInitializeOnLoadMethod` violation accepted?

## Out-of-scope for this hotspot

These belong to the other parallel audits — flagging so the summariser can route:

- **Grass subsystem**: `GrassPlacementController`, `GrassNearFieldController`, `GrassMidFieldController` and the grass-orchestration block inside `Planet.cs` ([lines 515-719](../../Assets/Scripts/Planet/Planet.cs#L515-L719)) — splitting that out is named in PLANET-2, but the controllers themselves and the four-tier model belong to a grass audit.
- **Water subsystem**: `WaterMeshBuilder`, the water shader-property bank and `GenerateWaterAsync` block ([Planet.cs:800-983](../../Assets/Scripts/Planet/Planet.cs#L800-L983)). PLANET-2 names the split but the internals belong to a water audit.
- **Biome subsystem**: `BiomeRegistry`, `BiomeMapBaker`, `BiomeSettings`, `ColorGenerator.Configure` Settings DTO violation (touches biomes), `VoronoiBiomeField`, the `BiomeLookupSelfTest` `RuntimeInitializeOnLoadMethod` violation.
- **Climate subsystem**: `ClimateMapGpuData`, `ClimateProvider`, `IClimateSampler` (Planet implements it; the climate query path itself belongs elsewhere).
- **Editor subsystem**: `PlanetEditor` (PLANET-13). Editor tooling audit if there is one.
- **Boot infrastructure**: `LoadingManager`'s LINQ chain through `OfType` and `OrderByDescending` is per-scene, not per-frame, but it allocates several lists — out of scope here.
