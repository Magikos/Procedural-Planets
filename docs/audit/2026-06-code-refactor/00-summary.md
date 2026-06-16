# Code Refactor Audit — Summary

**Date:** 2026-06-10
**Branch:** code-refactor
**Scope:** All four hotspots (Planet generation, Weather / precipitation, Grass / vegetation, Debug / console / services) across all four motivations (architectural consistency, cross-coupling, perf / allocations, style).
**Status:** Historical findings plus live reconciliation. Implementation is ongoing; use the reconciliation below before acting on an older finding.

## Live reconciliation - 2026-06-15 (code-verified pass)

**This section supersedes the 2026-06-14 reconciliation.** All items below were
verified against the live tree on 2026-06-15. File paths and line numbers are
current. Do not schedule work from the 2026-06-10 finding docs without checking
here first — many findings are stale.

### Confirmed closed since 2026-06-10 audit

- **T1 Settings DTO** — PLANET-1/14/16, WEATHER-1, GRASS-1/6: All closed.
  `_planetSettings` in Planet.cs appears only in `RegisterWorldSettings` (boot
  path to create DTOs; 3 occurrences). Zero runtime SO reads in Planet.cs.
  `GrassBiomeTintConfig.From(def)` is called at
  [BiomeSurfaceTextureArrays.cs:145](../../../Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs#L145).
  `ConfigureMaterial` and `Shader.Find` are gone from Planet.cs.
- **T2 God-class splits**: Planet (1043→510), WeatherManager (898→389 after
  WEATHER-2 this session), DebugCaptureController (1006→347),
  WaterDebugModule (878→281), FreeCameraController (859→483), ConsoleController
  (1083→265 + ConsoleInputController 491 + ConsoleAsyncRunner 321 +
  ConsoleInputLineFormatter 148), ChunkedSurfaceProvider (2146→618).
- **T3 Boot-path**: LoadingManager is the ONLY RuntimeInitializeOnLoadMethod.
  All three self-tests deleted. EventBusProcessor under GameBootstrap.
- **T6 Shader globals**: ShaderGlobalIds partial-class hub fully implemented.
  Zero raw-string inline globals.
- **T7 Debug modules**: AtmosphereDebugModule, ScaleReferenceDebugModule,
  BiomeDebugModule, TerrainGeographyDebugModule all own their domains.
  IDebugModule + IDebugCaptureMetadataProvider pattern in place.
- **WEATHER-2**: WeatherEvolutionScheduler + WeatherQueryCache extracted from
  WeatherManager (537→389 lines). Both are plain sealed classes.
- **WEATHER-3**: PrecipitationController dirty-flag pattern implemented.
- **WEATHER-5**: Closed as side-effect of WEATHER-9 field extraction.
- **WEATHER-6**: RainParticleController reads wind from IWeatherProvider.
- **WEATHER-7**: RainParticleController already uses LoggerProvider — not Debug.LogError.
- **WEATHER-8**: DestroyImmediate replaced with Destroy.
- **WEATHER-9**: RainParticle commands on RainParticleController (MonoTargetType.Single).
- **WEATHER-10**: AtmosphereDiagnostics uses EventBus subscription; F12 polling removed.
- **WEATHER-12**: All three render features use ServiceLocator.TryGet<IInterface>.
- **WEATHER-13/14**: _planetRadiusId renamed; NaN sentinels replaced with _windDirty.
- **WEATHER-17**: MigrateLocalWeatherParticleSettings — leave as-is (stamped).
- **PLANET-12**: `_lastElevationMin`/`_lastElevationMax` deleted from Planet.cs.
- **PLANET-4/5/6/15**: Self-tests, CombinedFaceMesh, GpuChunkSurfaceProvider,
  ShapeSettings SO allocation — all closed.
- **GRASS-1**: GrassBiomeTintConfig.From(def) wired at BiomeSurfaceTextureArrays.cs:145.
- **GRASS-3/4/5/6**: GrassMidField deleted; per-frame TryGet removed; Warning→Info;
  GrassPlacementClimateBinding deleted.
- **GRASS-7**: NOT duplicated — GrassNearFieldController and GrassPlacementController
  both reference `GrassChunkRuntime.BladeStride` etc. No local copies.
- **CORE-1/4/7/8/9/15**: EventBusProcessor under GameBootstrap; ShaderGlobalIds hub;
  ShaderGlobalsController uses LateUpdate; GrassDebugModule hygiene; ConsoleController
  popup state now encapsulated in ConsoleInputController.
- **All 15 open questions**: Stamped 2026-06-15 in section below.
- **Slice 5 perf**: FrameTimingCounters + FrameTimingModule shipped. Clouds reduced
  from 72/8 to 48/4 steps (16.60→11.07 ms isolated). GrassBladeBufferPool kills
  driver/GC churn on fly-through.

### Closed 2026-06-15 (small-items pass)

PLANET-7: `UploadMeshData(false)` in [ChunkMeshCache.cs:239](../../../Assets/Scripts/Planet/Surface/ChunkMeshCache.cs#L239).
PLANET-8: `Allocator.Persistent` + existing `finally` dispose in [ChunkedSurfaceProvider.cs:465](../../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L465).
PLANET-11: Walk-up `while (leaf.CpuVertices == null)` deleted from [ChunkedSurfaceProvider.cs:262](../../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L262).
PLANET-13: `_ = _planet.GeneratePlanetAsync()` in [PlanetEditor.cs:20](../../../Assets/Editor/PlanetEditor.cs#L20).
CORE-5: `EnsureComponent` block documented in [GameBootstrap.cs](../../../Assets/Scripts/Core/Services/GameBootstrap.cs).
CORE-6: `ServiceLocator.Unregister<T>` now auto-disposes the evicted service if `IDisposable`. Manual pre-Unregister dispose calls removed from `GameBootstrap.OnDestroy`.
CORE-14: `DumpAtmosphereDiagnostics` (F12) and `ClearScaleMarkers` (Shift+M) added to `IInputMapService` / `InputMapService`. `DebugInputRelay` no longer reads `Keyboard.current`; `using UnityEngine.InputSystem` removed from that file.

### Closed (session following 2026-06-15 reconciliation)

GRASS-2: `GrassPlacementController` (572→354 lines) split into `GrassChunkResidencyResolver` (40 lines, frustum residency + chunk set) and `GrassChunkDispatcher` (263 lines, all 33 PropertyToID fields, compute dispatch, buffer management, redispatch). Orchestrator calls `_resolver.Refresh`, `_dispatcher.SetTickCamera`, `_dispatcher.RedispatchAll`; pre-computes `EstimateGrassWorldBounds` once and passes to `CreateAndDispatch`.
WEATHER-4: Dead rain plumbing (`_rainParticles`, `_rainParticlesDrawCount`, `_rainParticleMaterial`) removed from `PrecipitationRenderPass`. Altitude-fade handoff implemented in `RainParticleController`: `[LOD Fade] AltitudeFadeBand` inspector field, `UpdateAltitudeFade()` method (lazy-resolves `IPrecipitationDebugControl`, computes `_altitudeFadeAlpha`), alpha applied to `RainColor.a` in `UploadMaterialParams`. `LocalMaxCameraAltitude` property added to `IPrecipitationDebugControl` and implemented on `PrecipitationController`.
PLANET-10: `EstimateWorldBounds` cached per node in `GatherVisibleLeaves`; result passed down to `IsChunkVisibleCandidate`, `ShouldSubdivide`, and `ProjectedChunkDiameterPixels` — eliminating the second call per node. Planet transform cached per tick in `RefreshTransformCache()` (called from `PrepareLodContext` and `GetGrassResidencyChunks`). `_planetTransform` now accessed exactly 3× per tick.

### All remaining items closed (session following 2026-06-15 reconciliation)

WEATHER-15: Lightning uniforms + functions extracted from `WeatherSampling.hlsl` to new `WeatherLightning.hlsl`. `WeatherSampling.hlsl` `#include`s it; all existing consumers unchanged.
WEATHER-16: Cube-face UV helpers (`CubeFaceLocalUp` / `CubeFaceUv`) extracted to new `WeatherCubeFace.hlsl`. `WeatherSampling.hlsl` and `CloudShadows.hlsl` both `#include "WeatherCubeFace.hlsl"`; duplicate `CloudShadowCubeFaceLocalUp` / `CloudShadowCubeFaceUv` removed from `CloudShadows.hlsl`; call site updated to `CubeFaceUv`.
WEATHER-18: `TryFindStrongestStorm` deleted from `SphericalWeatherGrid`. `WeatherManager.TryFindStrongestPrecipitation` now calls `CalculateStats` and reads `stats.StrongestStorm*` fields; single cell scan instead of two.
PLANET-9: `IChunkVisibilitySource.GetVisibleChunksSnapshot()` changed to `void GetVisibleChunksSnapshot(List<PlanetChunk> output)`; implementation clears+fills caller's list; no heap allocation per call.
CORE-13: `MemoryDebugCounters` static god-bag deleted. `IMemoryReporter` interface added (`AppendMemoryReport(StringBuilder)`). `ChunkMeshCache`, `BiomeAtlasService`, `ChunkedSurfaceProvider` each implement `IMemoryReporter`, register with `MemoryDebugModule.Register` at construction, unregister at Dispose. `MemoryDebugModule` iterates registered reporters in `RefreshStrings`. `FormatBytes` made `public static`. `ReportRetainedChunkCpuMemory` push method deleted; CPU bytes computed on demand in `AppendMemoryReport`.
CORE-10: `CommandData` gains `internal UnityEngine.Object CachedSingleTarget`. `CommandExecutor.ResolveTarget` for `MonoTargetType.Single` checks the cache first; calls `FindAnyObjectByType` only on miss or after the cached object is destroyed.
CORE-11: `ConsoleRegistry.Scan` skips assemblies whose name does not start with `Assembly-CSharp` or `Magikorp`, cutting Unity engine, system, and third-party assemblies from the scan.
GRASS-8: `FarOverlayAltitudeStart/End`, `NearFieldActivationAltitude`, `NearFieldDeactivationAltitude` added to `IGrassQualitySettings` and `DefaultGrassQualitySettings`. Private consts removed from `PlanetGrassCoordinator`; replaced with `ServiceLocator.Get<IGrassQualitySettings>()` reads.
WEATHER-11: Accepted as-is (F12-only GetPixel allocations; not worth touching).

**Deferred / ongoing**

- **Persistence adapter**: New design work; deferred until world development is further along.
- **Debug.Log\* migration**: Opportunistic — migrate when files are touched for other reasons.
- **VoronoiBiomeField.cs** (641 lines, unaudited new file): Not yet reviewed. If it crosses the ~400-line mark with added responsibilities, flag for a split audit.

### What is NOT remaining (verified closed)

PLANET-1 (Settings DTO), PLANET-2/3 (god-class splits), PLANET-7 (UploadMeshData), PLANET-8 (TempJob allocator), PLANET-9 (GetVisibleChunksSnapshot output-list), PLANET-10 (EstimateWorldBounds + transform cache), PLANET-11 (walk-up dead code), PLANET-12 (write-only elevation fields), PLANET-13 (PlanetEditor discard), PLANET-14 (shared material mutation), PLANET-16 (Shader.Find in Awake), GRASS-2 (GrassPlacementController split), GRASS-7 (blade-format constants), GRASS-8 (altitude consts → IGrassQualitySettings), CORE-5 (EnsureComponent docs), CORE-6 (Unregister dispose), CORE-7 (ShaderGlobalsController LateUpdate), CORE-10 (FindAnyObjectByType cache), CORE-11 (assembly prefix filter), CORE-13 (IMemoryReporter), CORE-14 (Keyboard.current in DebugInputRelay), CORE-15 (ConsoleController popup state), WEATHER-4 (rain LOD fade handoff), WEATHER-7 (LogProvider in RainParticleController), WEATHER-11 (GetPixel — accepted as-is), WEATHER-15 (WeatherLightning.hlsl extract), WEATHER-16 (WeatherCubeFace.hlsl consolidation), WEATHER-18 (TryFindStrongestStorm fused into CalculateStats).

---

## Live reconciliation - 2026-06-14

The detailed finding documents remain useful evidence, but several cross-cutting
claims in the original summary are no longer current:

- World-scoped settings are implemented. Runtime planet, biome, cloud,
  atmosphere, weather, and precipitation consumers use typed DTOs from one
  `ISettingsService` per world. Runtime settings are keyed by DTO type; stable
  string keys remain a persistence-boundary concern.
- Fail-fast world construction, required service/DTO validation, registration
  freeze, teardown, and same-scene world replacement are implemented. See
  [the world lifecycle design](../../design/2026-06-13-world-lifecycle.md).
- T5 dead code removed: GrassMidField experiment (controller/shader/compute +
  Planet.cs wiring + GrassRenderLayer.Mid + stats types + debug blocks),
  GrassMidFieldController, SmokeRenderer flag, GrassPlacementClimateBinding DTO,
  WEATHER-4 procedural-rain pass/DistantRain fields, GpuChunkSurfaceProvider,
  CombinedFaceMesh, CubeFaceTopology self-test. The 2026-06-10 audit is stale
  on most of T5.
- T3 boot-path: LoadingManager is the ONLY RuntimeInitializeOnLoadMethod. The
  real remaining violation was CubeFaceTopology.RunSelfTestAtStartup (missed by
  audit) — now deleted. EventBusProcessor was already lazy/self-creating (CORE-1
  stale).
- T6 shader-globals: all shader-global names centralized in `ShaderGlobalIds`
  per-domain partial files (Core/Atmosphere/Water/Cloud/Grass/Precipitation/
  Terrain/Biome/Celestial.cs). Module-local `static readonly int _xId` caches
  remain; hub owns names, not IDs. Zero raw-string inline globals.
- T7 debug-module hygiene: AtmosphereDebugModule + ScaleReferenceDebugModule
  extracted from GrassDebugModule. BiomeDebugModule + TerrainGeographyDebugModule
  already existed. IDebugModule + IDebugCaptureMetadataProvider pattern.
- All originally-flagged god-classes split to under ~500 lines:
  Planet (1043→510), WeatherManager (898→535), DebugCaptureController (1006→347),
  WaterDebugModule (878→281), GrassPlacementController (781→577, grew from
  TintDry additions), FreeCameraController (859→483), ConsoleController
  (1083→265 + ConsoleInputController 491 + ConsoleAsyncRunner 321 +
  ConsoleInputLineFormatter 148). ChunkedSurfaceProvider (2146→546) was Slice 4.
- Slice 5 perf counters shipped: FrameTimingCounters + FrameTimingModule.
  First numbers: whole-frame CPU 14.6ms / GPU 17.0ms; chunk grass 1.17ms heaviest
  CPU section. Debug overlay reorganized F6 (Camera+Performance) vs F9 (detailed).
- GRASS-1 (DTO bypass): BiomeSurfaceTextureArrays now packs via
  GrassBiomePlacementConfig.From(def) + GrassBiomeTintConfig.From(def).
- GRASS-4 (per-frame TryGet): IGrassNearFieldStatsProvider injected at
  construction; no per-frame service lookup.
- GRASS-5 (Warning for feature-disabled): demoted to LogLevel.Info in
  GrassPlacementController and GrassNearFieldController.
- GRASS-6 (GrassPlacementClimateBinding unreferenced): deleted.
- TintDryShift/TintLushShift (GRASS-1 tail): now fully wired end-to-end.
  GPU struct grew 48→80 bytes (5 Vector4s). Both compute shaders sample
  _ClimateMap (Texture2DArray, G=moisture01) per lane/cell and apply
  `saturate(TintBase * lerp(TintDry, TintLush, moisture))`. Controllers bind
  climate map before each dispatch via GetClimateMap() helper (falls back to
  1×1×6 neutral Texture2DArray if global not yet set).
- ConsoleController split complete: 1083→265 (ConsoleController) + 491
  (ConsoleInputController) + 321 (ConsoleAsyncRunner) + 148
  (ConsoleInputLineFormatter). CONSOLE-6 polish confirmed landed.

Measured performance work completed:

- Packed biome retention and post-atlas release reduced retained chunk CPU
  arrays from 918.1 MB to 624.3 MB, saving 293.8 MB in the validated scene.
- Deterministic timed F10 captures now report rolling CPU/GPU averages and p95,
  reject malformed frame samples, freeze local sunlight, and isolate render
  stages.
- Water stages were not the suspected hotspot once weather was held constant.
- Clouds were the dominant measured render cost. Reducing the high/PC default
  from 72 view steps and 8 light steps to 48/4 reduced isolated average GPU
  frame time from 16.60 ms to 11.07 ms at the validated viewpoint, without an
  observed silhouette or lighting regression. The production `Off` control
  confirms the runtime uses 48/4.
- GrassBladeBufferPool: per-chunk blade GraphicsBuffer reuse on page in/out.
  Kills driver/GC churn on fly-through. Slice 5 closed.

Remaining live priorities:

1. Finish the saved-world persistence adapter and schema migration boundary.
2. GRASS-7 (🔵 style): blade-format constants (BladeStride, VerticesPerVisualBlade,
   ClusterCardsPerInstance, BladeVertexCount) duplicated between
   GrassNearFieldController and GrassChunkRuntime. Low-severity consolidation.
3. Migrate direct `UnityEngine.Debug.Log*` calls when their owning files are
   otherwise touched.
4. ~~Remaining audit open questions~~ — All 15 open questions stamped 2026-06-15.

Everything below this section is the original 2026-06-10 audit snapshot. Verify
each finding against the live tree before scheduling it.

## How to read this audit

Each hotspot has its own findings doc with stable IDs (e.g. `PLANET-1`, `WEATHER-3`). This summary is the cross-cutting view — it does not restate findings, it tracks themes and proposes sequencing.

- [01-planet-generation.md](01-planet-generation.md) — 16 findings (0 🔴 / 4 🟠 / 7 🟡 / 2 🔵 / 3 ⚪)
- [02-weather-precipitation.md](02-weather-precipitation.md) — 17 findings (counts in doc)
- [03-grass-vegetation.md](03-grass-vegetation.md) — 8 findings (0 🔴 / 2 🟠 / 2 🟡 / 3 🔵 / 1 ⚪)
- [04-debug-console-services.md](04-debug-console-services.md) — 15 findings (0 🔴 / 4 🟠 / 5 🟡 / 2 🔵 / 4 ⚪)

**Total:** ~56 findings. **Zero 🔴 Critical.** Ten 🟠 Architecture. The codebase is in good shape; what remains is leverage work, not damage control.

Baseline references (use, don't re-audit):
- [docs/audit/2026-05-28.md](../2026-05-28.md) — last comprehensive
- [docs/audit/2026-05-28-shaders.md](../2026-05-28-shaders.md)
- [docs/audit/2026-05-30-performance.md](../2026-05-30-performance.md)
- [docs/design/2026-06-08-performance-maintainability-plan.md](../../design/2026-06-08-performance-maintainability-plan.md) — **active**; slices 1+2 implemented, 3-6 outlined. This audit references those slices; it does not duplicate them.

## Top-line picture

Three things stand out compared to the 2026-05-28 baseline:

1. **The codebase is genuinely healthier on the basics.** No `async void`. No `DestroyImmediate` in runtime. ServiceLocator uses interfaces. EventBus reflection cache shipped. CONSOLE-6 polish landed. The 2026-05-28 open items are mostly closed or shrunk to convention-tier.
2. **The Settings DTO pattern that was supposed to be the project's hallmark new discipline is not actually shipped on a single hot path.** It compiles (`GrassPlacementDtos`, `GrassInteractorSnapshot`) but the GPU pack still reads `BiomeDefinition` directly; `WeatherManager`, `CloudController`, `AtmosphereController`, `PrecipitationController`, and `Planet` all hold and read the raw `ScriptableObject` at runtime. Three weather console commands explicitly *mutate* the SO at runtime. This is the single largest systemic finding.
3. **Six classes are between 780 and 2150 lines.** Five of those are already named for splits in the perf-maintainability plan (slices 4 and 6). The audit adds empirical detail — what they actually own — to those slices, plus one new file (`WeatherManager`) that wasn't in the plan but exhibits the same shape.

## Cross-cutting themes

### T1 — Settings DTO violations are systemic (drives PLANET-1, WEATHER-1, GRASS-1, GRASS-6, PLANET-14, PLANET-15)

The pattern memory says: SOs are editor-only authoring surfaces; runtime reads immutable snapshot DTOs. The audit finds:

- `Planet.cs` dereferences `_planetSettings.X` in dozens of places — water build, grass configuration, climate sampling, surface overrides — and even ships two `if (_planetSettings != null)` guards in `ApplyGrassBlanketState`. ([PLANET-1](01-planet-generation.md))
- `WeatherManager`, `CloudController`, `AtmosphereController`, `PrecipitationController` each hold a public `[SerializeField]` SO and read it every frame. `SphericalWeatherGrid.ScheduleGridJob` passes the SO through to a Burst job. Three console commands write to the SO at runtime (`cloud.density`, `cloud.altitude`, `cloud.thickness`), plus four `atmosphere.*` setters. ([WEATHER-1](02-weather-precipitation.md))
- `BiomeSurfaceTextureArrays.ResolveGrassParams` reads `def.GrassDensity`, `def.GrassHeight`, etc. straight off `BiomeDefinition` and packs `BiomeGrassParamsGpu`. `GrassBiomeTintConfig.From` has **zero callers**. `GrassPlacementClimateBinding` is completely unreferenced. ([GRASS-1](03-grass-vegetation.md), [GRASS-6](03-grass-vegetation.md))
- `ConfigureMaterial` writes shader keywords directly on `_planetSettings.PlanetMaterial` — mutating the project asset. ([PLANET-14](01-planet-generation.md))
- `BuildShapeSettings` allocates a fresh `ScriptableObject.CreateInstance<ShapeSettings>()` per generation and leaks it. ([PLANET-15](01-planet-generation.md))

**Why it matters:** save/load, multi-planet scenes, runtime customisation, and console-command undo all break the moment a second consumer of the SO exists. T1 must be addressed before T2 (the splits) because splits will memorialize the violation in more files.

### T2 — Six god-classes, mostly already in slice scope

| File | Lines | Responsibilities | Status |
| --- | --- | --- | --- |
| `ChunkedSurfaceProvider.cs` | 2,146 (1,998 if dead block removed) | 9 | Perf-plan **slice 4** |
| `Planet.cs` | 1,049 | Generation + water + grass-LOD + 8 interfaces | Perf-plan **slice 6** |
| `DebugCaptureController.cs` | 1,006 | Capture + IO + GUI + console + console UX | Perf-plan **slice 6** |
| `WeatherManager.cs` | 898 | Sim + diagnostics + 2 readback caches + OnGUI | **Not in plan** ([WEATHER-2](02-weather-precipitation.md)) |
| `WaterDebugModule.cs` | 878 | ~85 modes + biome + terrain + freeze metadata | Perf-plan **slice 6** |
| `GrassPlacementController.cs` | 781 | Quality + residency + dispatch + readback + stats + runtime + near-handshake | **Not in plan** ([GRASS-2](03-grass-vegetation.md)) |

**Why it matters:** the perf-maintainability plan already targets four of six. The audit confirms the targets are still warranted and adds two new candidates (`WeatherManager`, `GrassPlacementController`) with the same shape. Splits also expose new coupling that's currently hidden — e.g. `GrassPlacementController` reaches into `IGrassNearFieldStatsProvider` via `ServiceLocator.TryGet` inside Tick, which is a contract that needs to become explicit.

### T3 — Out-of-band boot path entries (drives PLANET-4, CORE-1, CORE-9)

Project rule #3 says everything boots through `GameBootstrap` / `LoadingManager`. The audit finds five `RuntimeInitializeOnLoadMethod` injectors:

- `LoadingManager.CreateInstance` — **justified** (must paint overlay before any `IEarlyInitialize`).
- `EventBusProcessor.Init` — **no justification**; could be `EnsureComponent`'d. ([CORE-1](04-debug-console-services.md))
- `TerrainQuadtreeSelfTest` — test fixture. Schedules Burst jobs at `BeforeSceneLoad`. ([PLANET-4](01-planet-generation.md))
- `PlanetChunkMeshJobSelfTest` — same shape. ([PLANET-4](01-planet-generation.md))
- `BiomeLookupSelfTest` — sibling subsystem, same shape.

Per Bryan's testing stance, the three self-tests should be deleted, not replaced. `EventBusProcessor` should move under `GameBootstrap.EnsureComponent`. After that, `LoadingManager` is the *one* sanctioned exception, with a comment explaining why.

### T4 — Per-frame churn / hot-path drift

The dirty-flag pattern that `CloudController` and `AtmosphereController` adopted in NEW-06 (2026-05-28) is the right standard. Four places still re-upload globals or do redundant work each frame:

- `PrecipitationController.Update()` unconditionally writes ~17 `Shader.SetGlobal*` per frame. ([WEATHER-3](02-weather-precipitation.md))
- `ShaderGlobalsController.Update()` writes `_GameTime` unconditionally — defensible, but mixes `Update` vs `LateUpdate` publish phases with `WaterWakeController`. ([CORE-7](04-debug-console-services.md))
- `GrassPlacementController.Tick()` calls `ServiceLocator.TryGet<IGrassNearFieldStatsProvider>` every frame (twice, in two methods). ([GRASS-4](03-grass-vegetation.md))
- `ChunkedSurfaceProvider.GatherVisibleLeaves` calls `EstimateWorldBounds` twice per node per Tick and recomputes static transform state every frame. `GetVisibleChunksSnapshot` allocates a fresh `List<PlanetChunk>(128)` per call. ([PLANET-9](01-planet-generation.md), [PLANET-10](01-planet-generation.md))

None are crises. They're the natural targets for perf-plan **slice 5**'s timing counters.

### T5 — Dead/experimental code from design pivots

Mechanically easy subtraction; high readability win.

- `CombinedFaceMesh.cs` — zero callers ([PLANET-5](01-planet-generation.md))
- `ChunkedSurfaceProvider.cs` `#if false` boundary-normal-smoothing block (~150 lines, references non-existent fields) ([PLANET-5](01-planet-generation.md))
- `GpuChunkSurfaceProvider.cs` — never instantiated by the resolution switch ([PLANET-6](01-planet-generation.md))
- `TerrainQuadtreeSelfTest.cs`, `PlanetChunkMeshJobSelfTest.cs` — boot-path violations ([PLANET-4](01-planet-generation.md))
- `GrassMidFieldController.cs` + `GrassMidField.shader` + `GrassMidFieldPlace.compute` + `IGrassMidFieldStatsProvider` + `GrassMidFieldStats` + `GrassRenderLayer.Mid` enum value + F10 mid-field metadata block. Both design docs declare it rejected. ([GRASS-3](03-grass-vegetation.md))
- Procedural rain pass in `WeatherParticles.shader` + `DistantRain` profile fields in `PrecipitationController` — kept but forced to 0 draw count after the compute-buffer rain refactor. ([WEATHER-4](02-weather-precipitation.md))
- `GrassPlacementDtos.GrassPlacementClimateBinding` — unreferenced. ([GRASS-6](03-grass-vegetation.md))
- `SmokeRenderer` flag in `IGrassDebugStatsProvider` — hard-coded false. ([GRASS-3](03-grass-vegetation.md))
- `Planet.cs` `_lastElevationMin/Max` — written, never read. ([PLANET-12](01-planet-generation.md))

Rough estimate: ~2,000 lines of compiled-but-unused code across the C# tree, plus three resource files.

### T6 — Shader globals are scattered with no central authority ([CORE-4](04-debug-console-services.md))

`ShaderGlobalIds` holds exactly four IDs. Every debug module then redeclares its own `static readonly int _xxxId` cache (~70 across the project). `_OceanDebugMode` is duplicated in at least three files. There is no contract preventing two modules from picking different names for the same global. This is also why `WaterDebugModule` ends up owning biome/terrain shader IDs (CORE-3) and `GrassDebugModule` ends up owning atmosphere IDs (CORE-8).

Two paths: (a) make `ShaderGlobalIds` a partial-class hub with per-domain files; (b) accept module-local caches as intentional and add a comment to that effect, plus a naming convention. **Decision is Bryan's** — listed in open questions below.

### T7 — Debug subsystem leaks domain knowledge ([CORE-2](04-debug-console-services.md), [CORE-3](04-debug-console-services.md), [CORE-8](04-debug-console-services.md))

`DebugCaptureController` owns metadata serialization for camera/runtime/weather/precipitation/sun. `WaterDebugModule` owns biome and terrain-geography metadata. `GrassDebugModule` owns atmosphere globals. Each domain should own its own `IDebugCaptureMetadataProvider`. Currently the debug surface is the path of least resistance.

## Candidate "rules going forward" — for our live discussion

These come straight out of the patterns above. **Not a manifesto** — a draft to argue with.

1. **Settings DTO is the only runtime read path.** No `Controller._settings.X` calls outside `Awake` / `OnPlanetGenerated`. Console-command setters update the runtime snapshot; they do not mutate the SO asset.
2. **`Material` assets are cloned on first use.** Runtime never writes shader properties or keywords on an SO-referenced material asset.
3. **`RuntimeInitializeOnLoadMethod` is reserved for `LoadingManager` only.** Anything new requires a documented justification. Self-test fixtures don't count.
4. **Class size budget: ~400 lines.** Beyond that, a split design note is required before adding new responsibilities. (Existing files above the budget grandfathered, but flagged for splits via perf-plan slices.)
5. **Per-frame globals upload uses the dirty-flag pattern.** Static vs dynamic split; mark dirty in `OnPlanetGenerated` and on each console-command setter. Atmosphere and clouds are the precedent; precipitation, shader-globals, and any new controller must follow.
6. **`ServiceLocator.Get<>` is reserved for "must exist."** Optional services use `TryGet<>` with a downstream null-check. Resolve once at init for hot-path consumers; don't `TryGet` per frame.
7. **Dead experiments are deleted at the same commit that supersedes them, or within one week.** If the experiment is being parked, gate it behind `#if PROJECT_X_EXPERIMENT` so it stops shipping in the build.
8. **Each domain owns its own debug surface.** Atmosphere globals belong on an `AtmosphereDebugModule`; biome metadata on a `BiomeDebugModule`; etc. `WaterDebugModule` audits water. `DebugCaptureController` orchestrates, doesn't enumerate fields.
9. **Shader-globals stance (TBD):** either centralized hub or documented "tiny shared hub + module-local with naming convention." Pick one and write it down.
10. **Logger preference upgraded from "in new code" to "everywhere":** new code uses `ILogger`/`LoggerProvider`; old `UnityEngine.Debug.Log*` migrates opportunistically when its file is touched for another reason.

## Proposed sequencing if you greenlight any fixing

This is the audit's recommendation, not a commitment.

**Wave 1 — Foundation (no risk, big leverage):**
- T5 dead-code subtraction pass. ~2,000 lines, single PR. Removes confusion before T2 splits memorialise the wrong structure.
- T3 boot-path: delete the three self-tests, promote `EventBusProcessor` under `GameBootstrap`. Closes rule #3 drift.

**Wave 2 — Settings DTO migration (T1):**
- Design note: shape of `PlanetSnapshot` (mega vs narrow). Open question for you below.
- Migrate one consumer end-to-end first as a precedent (suggest `GrassPlacementController` — smallest blast radius, highest visibility because of the DTO showcase narrative).
- Then `PrecipitationController`, then weather/cloud/atmosphere together, then `Planet.cs` (which is also a slice 6 split candidate).

**Wave 3 — Hot-path consolidation (T4):**
- Add perf-plan slice 5 counters first; let measurement drive priority. Likely targets: `PrecipitationController` dirty-flag, `_GameTime` LateUpdate move, `GrassPlacementController` injected near-field reference, `EstimateWorldBounds` cache.

**Wave 4 — God-class splits (T2):**
- Per the perf-maintainability plan. Audit findings add detail to slices 4 and 6; `WeatherManager` ([WEATHER-2](02-weather-precipitation.md)) and `GrassPlacementController` ([GRASS-2](03-grass-vegetation.md)) are new candidates not in the plan.

**Wave 5 — Shader globals + debug-module hygiene (T6, T7):**
- Pick the shader-globals stance, then propagate.
- Extract `AtmosphereDebugModule`, `BiomeDebugModule`, `TerrainGeographyDebugModule`, `ScaleReferenceDebugModule`. Pull metadata responsibilities out of the wrong owners.

## Open questions — stamped 2026-06-15

All questions resolved. Decisions recorded below.

1. **DTO shape** → **Narrow records.** Use focused per-domain DTOs (`PlanetGenerationDto`, `PlanetWaterDto`, `PlanetGrassDto`, `PlanetClimateDto`, etc.). No mega `PlanetSnapshot`. Shapes T1 Settings DTO wave.
2. **Shader globals** → **Closed.** Centralized partial-class hub already implemented.
3. **`GpuChunkSurfaceProvider`** → **Closed.** Already deleted in T5.
4. **`ShapeSettings`** → **Closed.** Resolved 2026-06-13.
5. **Self-test removal** → **Closed.** All three self-tests deleted in T5.
6. **Procedural rain fallback path** → **Keep the pass; fix the zero-forcing.** The distant rain curtain is the intended LOD bridge to `RainParticleController` particles up close. The zero-draw-count in Setup is the bug. WEATHER-4 re-classified: implement the LOD transition, do not delete.
7. **`GrassMidField` deletion** → **Closed.** Deleted in T5.
8. **`GrassRenderDiagnostics.GeometryMode`** → **Keep.** Still a useful diagnostic for isolating near-field vs cluster rendering artifacts.
9. **`GrassTintDryShift` / `GrassTintLushShift`** → **Closed.** Fully wired 2026-06-14 (GRASS-1 tail).
10. **`EventBusProcessor` ownership** → **Closed.** Already moved under `GameBootstrap.EnsureComponent`.
11. **`[DebugOnly]` release-strip** → **Attribute approach preferred.** Future work.
12. **Debug-metadata ownership** → **Closed.** Each domain already owns its `IDebugCaptureMetadataProvider` (T7 done).
13. **`WeatherManager` split granularity** → **Plain classes** owned by `WeatherManager` orchestrator. No new MonoBehaviours.
14. **`MigrateLocalWeatherParticleSettings` scope** → **Leave as-is.** Version is at 1 on current checkout; won't fire again unless a version 2 is added. Overwriting all 6 fields on version mismatch is acceptable.
15. **`RainStreakLength` / `RainFallSpeed` / `RainWidth` ranges** → **Closed.** Fields moved to `RainParticleController` during extraction; `[Range]` and console clamps already agree.

## What we already know is not in scope

- **Tests** — your stance is unchanged. The audit does not propose adding a framework.
- **Caustics** — flagged where they overlap (`WaterDebugModule` mode registration); no fix recommendations made.
- **Perf-maintainability plan slices 3-6** — referenced, not duplicated. This audit's findings sharpen the splits and add `WeatherManager` + `GrassPlacementController` as additional split candidates.
- **Existing 2026-05-28 baseline findings already closed** — not re-listed. The remaining baseline open items (ARCH-02, QUAL-03, QUAL-07, SUGG-01, SUGG-02) are referenced where they overlap.

## Review process from here

Per [feedback-audit-workflow](../../../../.claude/projects/c--Users-Bryan-Source-Repos-Magikorp-ProceduralPlanets/memory/feedback_audit_workflow.md), you review and mark each finding as keep / defer / fix before any code is touched. Suggested marker convention: edit each finding in place with `**Decision:** fix-wave-N` / `**Decision:** defer` / `**Decision:** wontfix`. The audit then becomes the work-list.
