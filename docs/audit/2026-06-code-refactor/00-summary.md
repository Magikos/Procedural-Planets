# Code Refactor Audit — Summary

**Date:** 2026-06-10
**Branch:** code-refactor
**Scope:** All four hotspots (Planet generation, Weather / precipitation, Grass / vegetation, Debug / console / services) across all four motivations (architectural consistency, cross-coupling, perf / allocations, style).
**Status:** Findings only. **No code modified. Do not start fixing until Bryan reviews and marks decisions on each finding.**

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

## Open questions you need to answer before fixing starts

Pulled together from the four hotspot docs.

1. **DTO shape:** one `PlanetSnapshot` carrying everything, or narrow records (`PlanetGenerationDTO`, `PlanetWaterDTO`, `PlanetGrassDTO`, `PlanetClimateDTO`)? Narrow records pair better with the Planet split (Wave 4). ([PLANET-1](01-planet-generation.md) Q3, [GRASS-1](03-grass-vegetation.md) Q1, [WEATHER-1](02-weather-precipitation.md) Q1)
2. **Shader globals:** centralized partial-class hub, or documented "tiny shared hub" + per-module caches with naming convention? ([CORE-4](04-debug-console-services.md))
3. **`GpuChunkSurfaceProvider`:** keep behind `#if PLANET_GPU_EXPERIMENT`, exclude from asmdef, or delete? ([PLANET-6](01-planet-generation.md))
4. **`ShapeSettings`:** any `.asset` files for it on disk? If yes, conversion to plain class needs migration. ([PLANET-15](01-planet-generation.md))
5. **Self-test removal:** confirm deletion is fine per your testing stance. ([PLANET-4](01-planet-generation.md))
6. **Procedural rain fallback path:** is the zero-forced `WeatherParticles.shader` Rain pass intentional for low-spec LOD where `RainParticleController` is disabled, or safe to delete? ([WEATHER-4](02-weather-precipitation.md))
7. **`GrassMidField` deletion timing:** A/B done? If yes, delete in T5. If keeping warm, can we at least remove the dead F10 block + enum value? ([GRASS-3](03-grass-vegetation.md))
8. **`GrassRenderDiagnostics.GeometryMode`:** still A/B-relevant or retired by the near/chunk/blanket stack? ([GRASS-3](03-grass-vegetation.md))
9. **`GrassTintDryShift` / `GrassTintLushShift`:** authored on both SO and DTO but never packed into the GPU struct. Shipped on a path the audit missed, or never wired? ([GRASS-1](03-grass-vegetation.md))
10. **`EventBusProcessor` ownership:** move under `GameBootstrap.EnsureComponent`, or keep the static singleton for resilience against scene-reload bugs? ([CORE-1](04-debug-console-services.md), [CORE-9](04-debug-console-services.md))
11. **`[DebugOnly]` release-strip:** still on roadmap, or replaced by the simpler `Debug.isDebugBuild` gate? ([CORE-12](04-debug-console-services.md))
12. **Debug-metadata ownership:** should each domain own its own `IDebugCaptureMetadataProvider`? Audit recommends yes; it's a convention call. ([CORE-8](04-debug-console-services.md))
13. **`WeatherManager` split granularity:** sub-`MonoBehaviour`s or plain classes owned by `WeatherManager`? First place that question gets forced. ([WEATHER-2](02-weather-precipitation.md))
14. **`MigrateLocalWeatherParticleSettings` scope:** overwrite all nine fields or only legacy-incompatible ones? ([WEATHER-17](02-weather-precipitation.md))
15. **`RainStreakLength` / `RainFallSpeed` / `RainWidth` ranges:** inspector slider vs OnValidate clamp inconsistency. ([WEATHER-5](02-weather-precipitation.md))

## What we already know is not in scope

- **Tests** — your stance is unchanged. The audit does not propose adding a framework.
- **Caustics** — flagged where they overlap (`WaterDebugModule` mode registration); no fix recommendations made.
- **Perf-maintainability plan slices 3-6** — referenced, not duplicated. This audit's findings sharpen the splits and add `WeatherManager` + `GrassPlacementController` as additional split candidates.
- **Existing 2026-05-28 baseline findings already closed** — not re-listed. The remaining baseline open items (ARCH-02, QUAL-03, QUAL-07, SUGG-01, SUGG-02) are referenced where they overlap.

## Review process from here

Per [feedback-audit-workflow](../../../../.claude/projects/c--Users-Bryan-Source-Repos-Magikorp-ProceduralPlanets/memory/feedback_audit_workflow.md), you review and mark each finding as keep / defer / fix before any code is touched. Suggested marker convention: edit each finding in place with `**Decision:** fix-wave-N` / `**Decision:** defer` / `**Decision:** wontfix`. The audit then becomes the work-list.
