# Project Structure — Procedural Planets

## Directory Layout
```
ProceduralPlanets/
├── Assets/
│   ├── Editor/
│   │   ├── PlanetEditor.cs              # Custom inspector for Planet (inline settings, regenerate)
│   │   ├── BiomeRegistryEditor.cs       # Grid inspector for BiomeRegistry
│   │   └── SDFFontAssetImporter.cs      # Builds SDF font assets
│   ├── Graphics/
│   │   ├── Materials/
│   │   ├── Fonts/                       # SDF font asset(s) (DefaultFont)
│   │   └── Shaders/
│   │       ├── PlanetVertexColor.shader # Vertex-color terrain, PBR, depth passes, debug tints
│   │       ├── Ocean.shader             # Transparent ocean surface (foam/waves/glint/depth)
│   │       ├── WaterVolume.shader        # Full-screen underwater/long-path volume composite
│   │       ├── WaterVolumePrepass.shader # Writes water interface coverage texture
│   │       ├── Atmosphere.shader        # Post-process Rayleigh+Mie scattering
│   │       ├── Cloud.shader             # Volumetric clouds (weather-driven)
│   │       ├── Precipitation.shader     # Rain/storm curtains
│   │       ├── Star.shader              # Stars + sun disc, horizon-clipped
│   │       ├── SDFText.shader           # SDF text (overlay/debug)
│   │       ├── LoadingOverlay.shader    # Loading overlay + progress bar
│   │       ├── *.compute                # OpticalDepth, CloudNoise, WeatherEvolution
│   │       └── Includes/                # Atmosphere, Common, Math, CloudShadows, WeatherSampling, DebugModes (.hlsl)
│   ├── Resources/
│   │   └── DefaultFont (SDF font asset, loaded by LoadingManager)
│   ├── Scenes/
│   │   ├── Planet.unity                 # Main planet scene
│   │   └── Placement.unity              # Object placement test scene (hosts Test.cs)
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── Data/                     # BiomeTypes, ChunkCoord
│   │   │   ├── Events/                   # EventBus, EventBusRegistry, EventBusAutoBinder, EventBusProcessor,
│   │   │   │                             #   IGameEvent, PlanetGeneratedEvent, CelestialEvents, WeatherEvents,
│   │   │   │                             #   ProgressEvent, DebugActionEvents, DebugCommandRequestedEvent
│   │   │   ├── Interfaces/               # IPlanet, IPlanetSurfaceSampler, ITerrainProvider, IBiomeProvider,
│   │   │   │                             #   IBiomeRegistry, ITemperatureProvider, IMoistureProvider, ISeedProvider,
│   │   │   │                             #   ICelestialTimeController, IWeatherProvider, IPrecipitationDebugControl,
│   │   │   │                             #   ILoadingManager, IWorldAction(Manager), ILogger,
│   │   │   │                             #   IEarlyInitialize, ILateInitialize, IProgressReporter/Handle
│   │   │   ├── Services/                 # ServiceLocator, GameBootstrap, LoadingManager, SeedProvider, UnityLogger,
│   │   │   │                             #   WorldActionManager, ProgressTracker, ProgressHandle,
│   │   │   │                             #   FreeCameraController, ShaderGlobalsController,
│   │   │   │                             #   DebugInputRelay, DebugCommandProvider, DebugCaptureController,
│   │   │   │                             #   DebugRegistry, DebugModeConstants, WaterDebugModule,
│   │   │   │                             #   WaterWakeController, WaterWakeEmitter
│   │   │   ├── Text/                      # SDFFontAsset, SDFGlyph, SDFTextMeshBuilder, SDFTextRenderer
│   │   │   ├── Utilities/                 # CoordinateConverter, CubeSphereMeshBuilder, ObjectPool
│   │   │   ├── QualityController.cs
│   │   │   └── ProceduralPlanets.Core.asmdef   (refs: Unity.InputSystem)
│   │   ├── Planet/
│   │   │   ├── Atmosphere/                # AtmosphereController, AtmosphereDiagnostics, AtmosphereSettings,
│   │   │   │                             #   AtmosphereRenderFeature, AtmosphereRenderPass, StarRenderFeature
│   │   │   ├── Biomes/                    # BiomeDefinition, BiomeRegistry, BiomeSettings, Temperature/MoistureProvider
│   │   │   ├── Clouds/                    # CloudController, CloudSettings, CloudRenderFeature,
│   │   │   │                             #   CloudNoiseGenerator, SphericalWeatherGrid
│   │   │   ├── NoiseFilters/              # INoiseFilter, NoiseFilterFactory, Simple/RigidNoiseFilter
│   │   │   ├── Planet.cs                  # Orchestrator (ILateInitialize, IProgressReporter, IPlanet)
│   │   │   ├── PlanetSettings.cs / ShapeSettings.cs / NoiseSettings.cs
│   │   │   ├── ShapeGenerator.cs / Noise.cs / MinMax.cs / ColorGenerator.cs / TerrainFace.cs
│   │   │   ├── CelestialManager.cs        # Sun/moon orbits, day/night, phases
│   │   │   ├── WeatherManager.cs          # Cube-sphere weather grid (GPU evolution), wind
│   │   │   ├── WaterMeshBuilder.cs        # Builds clipped ocean mesh (+ volume lip)
│   │   │   ├── WaterVolumeRenderFeature.cs
│   │   │   ├── PrecipitationController.cs / PrecipitationRenderFeature.cs / WeatherLightningController.cs
│   │   │   ├── ICloudController.cs / IWeatherConfigurator.cs
│   │   │   └── ProceduralPlanets.Planet.asmdef (refs: Core, URP, InputSystem)
│   │   ├── PoissonDiscSampling.cs / PoissonDiscSphereSampling.cs
│   │   ├── Test.cs                        # Editor-only Gizmo fixture (in Placement.unity)
│   │   └── ProceduralPlanets.Sampling.asmdef (refs: Core, Planet)
│   └── Settings/                          # PlanetSettings, AtmosphereSettings, CloudSettings, Biomes/, URP assets
├── local-only/                            # Reference projects & papers (not in build) — see reference_local_only memory
├── docs/                                  # PROJECT_PLAN, phases/, audit/
└── ProceduralPlanets.sln
```

## Assembly Boundaries (important)
- **Core** (`ProceduralPlanets.Core`) references only Unity.InputSystem. It holds interfaces, events, services, utilities. It **cannot** reference Planet types.
- **Planet** (`ProceduralPlanets.Planet`) references Core + URP. Holds generation, water, atmosphere, weather, clouds, celestial.
- **Sampling** references Core + Planet.
- Consequence: `ITerrainProvider`/`IBiomeProvider` in Core are pure **evaluation** contracts — setup (`Configure`/`Initialize`) lives on the concrete classes in Planet because the settings types (ShapeSettings/BiomeSettings) are not visible to Core.

## Core Architecture

### Generation Pipeline
```
Planet (MonoBehaviour, ILateInitialize, IProgressReporter, IPlanet)
  ├── PlanetSettings (user-friendly ScriptableObject)
  │   └── BuildShapeSettings() → ShapeSettings
  ├── ShapeGenerator : ITerrainProvider (elevation; range published via CommitElevationRange after parallel pass)
  │   ├── NoiseFilterFactory → INoiseFilter[]
  │   └── Noise (simplex, seed-based)
  ├── TerrainFace[6] (one per cube face → mesh, async via Parallel.For)
  ├── ColorGenerator : IBiomeProvider (Temperature × Moisture → BiomeRegistry)
  ├── WaterMeshBuilder (global cube-face seam sharing → ocean mesh + volume lip)
  └── PlanetGeneratedEvent → celestial, atmosphere, weather, camera
```

### Startup Flow (LoadingManager-driven)
```
[RuntimeInitializeOnLoadMethod] creates LoadingManager + EventBusProcessor (DontDestroyOnLoad)
  ↓
LoadingManager.Start() → InitializeAsync(activeScene):
  ├── Collect all MonoBehaviours (incl. DontDestroyOnLoad GameBootstrap)
  ├── Register IProgressReporter into ProgressTracker
  ├── Run IEarlyInitialize ordered by descending EarlyPriority
  │     └── GameBootstrap (priority 100): registers ISeedProvider, IWorldActionManager,
  │         IDebugCommandProvider; ensures ShaderGlobals/Quality/DebugInputRelay/DebugCapture/WaterWake
  ├── Run ILateInitialize ordered by descending LatePriority
  │     ├── Planet (priority 0): GeneratePlanetAsync → raises PlanetGeneratedEvent
  │     └── WeatherManager (priority -10): generates weather grid (planet already generated)
  └── Fade out loading overlay (SDF text progress)
```

### Celestial System
```
CelestialManager (ICelestialTimeController)
  ├── Sun orbit (directional light, configurable day length)
  ├── Moon orbit (separate speed/inclination, phase tracking)
  ├── IsDayAt(worldPosition), MoonPhase/Fullness/Index
  └── Events: DayNightChangedEvent, MoonPhaseChangedEvent
```

### Atmosphere System v3 (Post-Process)
See `atmosphere-reference.md` for full detail. Brute-force Rayleigh+Mie ray marching as a URP
fullscreen post-process. Three radii (`_SeaLevelRadius` = ocean sphere, `_DensityOriginRadius` =
same, `_AtmosphereRadius` = maxRadius × scale) set by AtmosphereController from PlanetGeneratedEvent.

### Water System
See `water.md` (authoritative, kept current). Ocean surface (`Ocean.shader`) + full-screen volume
(`WaterVolume.shader`) + prepass coverage. Vertex colors encode depth/shore/body. F10 capture sets
drive artifact debugging.

## Key Patterns
- **ServiceLocator**: interface-keyed only (no concrete-type registrations). Providers register in Awake, unregister in OnDestroy.
- **EventBus**: Subscribe in OnEnable, unsubscribe in OnDisable. Weak references; compiled open-delegate dispatch (no per-event reflection). Each bus auto-registers into `EventBusRegistry`, so `EventBusRegistry.ClearAll()` in `GameBootstrap.OnDestroy` clears every event type in one call.
- **Async**: `async Awaitable` everywhere (no coroutines, no `async void`). Background work via `Awaitable.BackgroundThreadAsync` / `MainThreadAsync`, cancellation via linked `CancellationToken`.
- **Init ordering**: `IEarlyInitialize`/`ILateInitialize` priorities drive deterministic startup through LoadingManager (not Awake/Start races).
- **Debug input**: hotkeys polled only in `DebugInputRelay` → `DebugCommandRequestedEvent` → `DebugCommandProvider` → typed debug events. Simulation classes listen for events; they do not poll input.
- **No serialized meshes**: all meshes generated at runtime.
- **Global shader properties**: atmosphere/water/weather set `Shader.SetGlobal*`; IDs cached via `Shader.PropertyToID`.
- **Settings as ScriptableObjects / YAML**: AtmosphereSettings.asset is plain YAML, editable from code.
