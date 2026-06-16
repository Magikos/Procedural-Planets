---
name: project-current-focus
description: "As of 2026-06-15: code-refactor arc COMPLETE. All audit backlog items closed. Biome arc remains paused."
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

**Active branch:** `code-refactor`

**Arc status: COMPLETE as of 2026-06-15.** All audit backlog items are closed and committed. The biome-climate-overhaul arc remains paused.

## Completed this arc (all verified against source)

- **T3 boot-path:** LoadingManager is the only RuntimeInitializeOnLoadMethod. ✓
- **T5 dead code:** GrassMidField, GpuChunkSurfaceProvider, CombinedFaceMesh, self-tests, GrassPlacementClimateBinding — all deleted. ✓
- **T6 shader-globals:** All global names in ShaderGlobalIds partial files. ✓
- **T7 debug-module hygiene:** AtmosphereDebugModule, ScaleReferenceDebugModule, BiomeDebugModule, TerrainGeographyDebugModule all own their domain. ✓
- **Slice 4:** ChunkedSurfaceProvider 2146 → 546 lines. ✓
- **Slice 5:** FrameTimingCounters + FrameTimingModule, GrassBladeBufferPool, F6/F9 overlay reorganization. ✓
- **Slice 6 god-class splits:** Planet (1043→406), WeatherManager (898→388 via WeatherDiagnostics + WeatherEvolutionScheduler + WeatherQueryCache), DebugCaptureController, WaterDebugModule (878→243), GrassPlacementController (781→494), FreeCameraController (859→415). ✓
- **ConsoleController split:** 1083 → 265 + ConsoleInputController 491 + ConsoleAsyncRunner 321 + ConsoleInputLineFormatter 148. ✓
- **GRASS-1/4/5/6/7/8:** DTO path, Warning→Info, dead DTO deleted, blade constants consolidated, altitude consts to IGrassQualitySettings. ✓
- **TintDryShift/TintLushShift:** Fully wired 2026-06-14. ✓
- **Settings DTO pattern (WEATHER-1):** Fully realized for all hub SOs — Atmosphere, Cloud, Biome, Planet. ✓
- **WEATHER-2:** WeatherEvolutionScheduler + WeatherQueryCache extracted from WeatherManager into plain class collaborators. ✓
- **WEATHER-3 dirty flag:** PrecipitationController migrated to EnsureStaticPropertiesUploaded + UpdatePerFrameProperties pattern. ✓
- **WEATHER-6:** RainParticleController reads wind via IWeatherProvider (resolved at init). ✓
- **WEATHER-7 logger migration:** All non-backend Debug.Log* calls migrated to ILogger/LoggerProvider. ✓
- **WEATHER-8:** RainParticleController uses Destroy (not DestroyImmediate). ✓
- **WEATHER-9:** RainParticleCommands nested class deleted; commands moved onto controller with MonoTargetType.Single. ✓
- **WEATHER-10:** DumpAtmosphereDiagnostics added to DebugCommandType; raised from DebugInputRelay; AtmosphereDiagnostics listens. ✓
- **WEATHER-12:** All three render features (Atmosphere, Cloud, Precipitation) use ServiceLocator.TryGet instead of FindAnyObjectByType. ✓
- **WEATHER-13:** AtmosphereController field renamed to _seaLevelRadiusId. ✓
- **WEATHER-14:** WeatherManager uses bool _windDirty flag (NaN sentinels removed). ✓
- **WEATHER-15:** Lightning extracted to WeatherLightning.hlsl; WeatherSampling.hlsl #includes it. ✓
- **WEATHER-16:** Cube-face UV helpers extracted to WeatherCubeFace.hlsl; both WeatherSampling + CloudShadows #include it. ✓
- **WEATHER-18:** TryFindStrongestStorm deleted; TryFindStrongestPrecipitation reads from CalculateStats. ✓
- **PLANET-9:** GetVisibleChunksSnapshot changed to output-list pattern (no per-call allocation). ✓
- **CORE-10:** MonoTargetType.Single caches result in CommandData.CachedSingleTarget. ✓
- **CORE-11:** ConsoleRegistry.Scan filters to Assembly-CSharp*/Magikorp* assemblies. ✓
- **CORE-13:** IMemoryReporter pull model replaces MemoryDebugCounters push bag; ChunkMeshCache + BiomeAtlasService + ChunkedSurfaceProvider implement it. ✓
- **Audit open questions:** All 15 stamped. ✓
- **Persistence adapter:** Deferred by Bryan — will design after world development is further along.

## Deferred / future work

- **Rain curtain LOD bridge (WEATHER-4 re-classified):** Distant WeatherParticles.shader rain pass intended as LOD layer. Currently zero-forced in PrecipitationRenderFeature.Setup. Not yet implemented.
- **Debug.Log* migration:** Opportunistic — migrate when files are touched for other reasons.
- **VoronoiBiomeField.cs** (641 lines, new file): Not yet reviewed. Flag for split if responsibilities grow past ~400 lines.

## Biome arc (paused — context preserved)

The biome-climate-overhaul work (TemperatureProvider lat+alt model, Voronoi assignment, Synty texture work) is on hold. Remaining steps: 1b (climate model), 1c (Voronoi+domain-warp), grass Gaussian niche, props. TintDryShift/TintLushShift is SHIPPED — do not re-implement.
