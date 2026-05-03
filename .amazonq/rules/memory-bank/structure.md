# Project Structure — Procedural Planets

## Directory Layout
```
ProceduralPlanets/
├── Assets/
│   ├── Editor/
│   │   ├── PlanetEditor.cs              # Custom inspector for Planet component
│   │   └── BiomeRegistryEditor.cs       # Custom grid inspector for BiomeRegistry
│   ├── Graphics/
│   │   ├── Materials/
│   │   │   └── Planet.mat               # Planet material (URP, vertex color)
│   │   └── Shaders/
│   │       ├── PlanetVertexColor.shader # URP HLSL: vertex color as albedo, PBR lighting
│   │       ├── Atmosphere.shader        # Post-process: fullscreen scattering pass
│   │       ├── OpticalDepth.compute     # Compute shader: UNUSED in v3 (kept for future LUT optimization)
│   │       ├── BlueNoise.png            # Dither texture (future use)
│   │       ├── Planet.shadergraph       # OLD shader graph (unused, kept for reference)
│   │       └── Includes/
│   │           ├── Atmosphere.hlsl      # v3: brute-force Rayleigh+Mie scattering
│   │           ├── Common.hlsl          # Unity shader variable declarations
│   │           └── Math.hlsl            # Ray-sphere intersection, utility functions
│   ├── Scenes/
│   │   ├── Planet.unity                 # Main planet scene
│   │   └── Placement.unity              # Object placement test scene
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── Data/
│   │   │   │   ├── BiomeTypes.cs        # BiomeType enum + BiomeResult struct
│   │   │   │   └── ChunkCoord.cs        # Chunk coordinate struct
│   │   │   ├── Events/
│   │   │   │   ├── EventBus.cs          # Generic static event bus
│   │   │   │   ├── EventBusProcessor.cs # Deferred event processing
│   │   │   │   ├── IGameEvent.cs        # Marker interface for events
│   │   │   │   ├── IProgressEvent.cs    # Progress reporting interface
│   │   │   │   ├── PlanetGeneratedEvent.cs # Includes SeaLevelRadius, ElevationMin/Max
│   │   │   │   └── CelestialEvents.cs   # DayNightChangedEvent, MoonPhaseChangedEvent
│   │   │   ├── Interfaces/
│   │   │   │   ├── IBiomeProvider.cs    # Biome evaluation interface
│   │   │   │   ├── IBiomeRegistry.cs    # Temperature × moisture → biome lookup
│   │   │   │   ├── ILogger.cs           # Logging interface
│   │   │   │   ├── IMoistureProvider.cs # Moisture evaluation interface
│   │   │   │   ├── ISeedProvider.cs     # Deterministic seed interface
│   │   │   │   ├── ITemperatureProvider.cs # Temperature evaluation interface
│   │   │   │   ├── ITerrainProvider.cs  # Elevation evaluation interface
│   │   │   │   └── IWorldAction.cs      # Command pattern interface
│   │   │   ├── Services/
│   │   │   │   ├── FreeCameraController.cs # WASD+mouse camera, Shift+A/D=roll, Ctrl+Space=surface
│   │   │   │   ├── GameBootstrap.cs
│   │   │   │   ├── SeedProvider.cs
│   │   │   │   ├── ServiceLocator.cs
│   │   │   │   ├── UnityLogger.cs
│   │   │   │   └── WorldActionManager.cs
│   │   │   ├── Utilities/
│   │   │   │   ├── CoordinateConverter.cs
│   │   │   │   ├── CubeSphereMeshBuilder.cs # Shared cube-sphere mesh generation
│   │   │   │   └── ObjectPool.cs
│   │   │   └── ProceduralPlanets.Core.asmdef
│   │   ├── Planet/
│   │   │   ├── Atmosphere/
│   │   │   │   ├── AtmosphereController.cs    # Sets shader globals, uses sea level radius
│   │   │   │   ├── AtmosphereRenderFeature.cs # URP ScriptableRendererFeature
│   │   │   │   ├── AtmosphereRenderPass.cs    # RenderGraph pass: fullscreen scattering
│   │   │   │   ├── AtmosphereSettings.cs      # ScriptableObject: all tunable parameters
│   │   │   │   └── AtmosphereDiagnostics.cs   # F12 screen capture + shader global dump
│   │   │   ├── Biomes/
│   │   │   │   ├── BiomeDefinition.cs
│   │   │   │   ├── BiomeRegistry.cs
│   │   │   │   ├── BiomeSettings.cs
│   │   │   │   ├── TemperatureProvider.cs
│   │   │   │   └── MoistureProvider.cs
│   │   │   ├── NoiseFilters/
│   │   │   │   ├── INoiseFilter.cs
│   │   │   │   ├── NoiseFilterFactory.cs
│   │   │   │   ├── SimpleNoiseFilter.cs
│   │   │   │   └── RigidNoiseFilter.cs
│   │   │   ├── Planet.cs                # Main orchestrator (terrain, water, events)
│   │   │   ├── PlanetSettings.cs        # ScriptableObject: user-friendly generation params
│   │   │   ├── ShapeGenerator.cs        # Evaluates noise layers → elevation
│   │   │   ├── TerrainFace.cs           # Generates mesh for one cube face
│   │   │   ├── ColorGenerator.cs        # Biome colors via temp/moisture/registry
│   │   │   ├── CelestialManager.cs      # Sun/moon orbits, day/night, moon phases
│   │   │   ├── Noise.cs                 # Simplex noise (seed-based)
│   │   │   ├── MinMax.cs                # Thread-safe min/max tracking
│   │   │   ├── ShapeSettings.cs         # Runtime noise layer config (built by PlanetSettings)
│   │   │   ├── NoiseSettings.cs         # Serializable noise parameters
│   │   │   └── ProceduralPlanets.Planet.asmdef
│   │   ├── PoissonDiscSampling.cs
│   │   ├── PoissonDiscSphereSampling.cs
│   │   ├── Test.cs
│   │   ├── TestPoissonDiscSphereDraw.cs
│   │   └── ProceduralPlanets.Sampling.asmdef
│   └── Settings/
│       ├── Planet Settings/
│       │   ├── Planet.asset             # PlanetSettings instance
│       │   ├── Atmosphere Settings.asset # AtmosphereSettings instance (YAML, editable)
│       │   └── Biomes/                  # BiomeRegistry, BiomeSettings, 15 BiomeDefinitions
│       ├── PC_RPAsset.asset             # URP pipeline asset (has AtmosphereRenderFeature)
│       └── PC_Renderer.asset            # URP renderer asset
├── local-only/                          # Reference projects (not in main build)
└── ProceduralPlanets.sln
```

## Core Architecture

### Generation Pipeline
```
Planet (MonoBehaviour — orchestrator)
  ├── PlanetSettings (user-friendly ScriptableObject)
  │   └── BuildShapeSettings() → ShapeSettings (noise layers from friendly params)
  ├── ShapeGenerator : ITerrainProvider (elevation calculation, no clamp)
  │   ├── NoiseFilterFactory → INoiseFilter[]
  │   └── Noise (simplex noise, seed-based permutation)
  ├── TerrainFace[6] (one per cube face → mesh)
  ├── ColorGenerator : IBiomeProvider (vertex colors from biome system)
  │   ├── TemperatureProvider : ITemperatureProvider
  │   ├── MoistureProvider : IMoistureProvider
  │   └── BiomeRegistry : IBiomeRegistry
  ├── Water Mesh (CubeSphereMeshBuilder, transparent URP Lit material)
  └── PlanetGeneratedEvent → triggers all dependent systems
```

### Celestial System
```
CelestialManager (MonoBehaviour)
  ├── Sun orbit (directional light, tilted plane, configurable day length)
  ├── Moon orbit (separate speed/inclination, phase tracking)
  ├── IsDayAt(worldPosition) — position-based day/night check
  ├── MoonPhase / MoonFullness / MoonPhaseIndex
  └── Events: DayNightChangedEvent, MoonPhaseChangedEvent
```

### Atmosphere System v3 (Post-Process)
```
AtmosphereController (MonoBehaviour)
  ├── Receives PlanetGeneratedEvent → gets maxRadius + seaLevelRadius
  ├── Sets shader globals every frame (sun direction, all parameters)
  ├── Three key radii:
  │   ├── _PlanetRadius = seaLevelRadius (ocean sphere, ray intersection floor)
  │   ├── _DensityOriginRadius = seaLevelRadius (density height=0 at ocean)
  │   └── _AtmosphereRadius = maxRadius * AtmosphereScale (outer edge)
  └── Scale heights converted: fraction * atmosphereThickness → world units

AtmosphereRenderFeature → AtmosphereRenderPass
  ├── URP ScriptableRendererFeature + RenderGraph pass
  ├── Fullscreen triangle via SV_VertexID (DrawProcedural)
  ├── DIRECTIONAL_SUN keyword always enabled
  └── Works from both space and surface views

Atmosphere.hlsl (brute-force ray marching)
  ├── View ray: 16 steps through atmosphere
  ├── Sun ray: 8 steps per view sample (128 total per pixel)
  ├── Rayleigh phase: (3/16π)(1+cos²θ)
  ├── Mie phase: HG with anisotropy g=0.76
  ├── Density: exp(-height / scaleHeight) from DensityOriginRadius
  ├── Tone mapping: Reinhard on scatter only (preserves terrain color)
  ├── Sun disc: rendered even outside atmosphere, attenuated through it
  └── Debug modes: 0=final, 1=height, 2=Rayleigh, 3=Mie, 4=sunT, 5=mask
```

### Startup Flow
```
OnEnable (all listeners subscribe to EventBus)
  ↓
Planet.Start()
  ├── Has _lastGeneratedRadius? → Raise PlanetGeneratedEvent (with cached seaLevel/elevation)
  └── No data? → GeneratePlanetAsync() → Raise event when done
  ↓
All listeners receive PlanetGeneratedEvent:
  ├── FreeCameraController → reposition camera
  ├── CelestialManager → set planet radius, moon orbit
  └── AtmosphereController → set shader globals
```

### Key Patterns
- **EventBus**: Subscribe in OnEnable, unsubscribe in OnDisable. Initialization in Start.
- **Planet owns startup**: Raises event if data exists, otherwise auto-generates.
- **No OnValidate**: Generation only via button press or Start.
- **No serialized meshes**: All meshes generated at runtime.
- **Global shader properties**: Atmosphere uses Shader.SetGlobal* for all parameters.
- **Assembly separation**: Core (no Planet dependency) ← Planet (references Core + URP).
- **Settings as YAML**: AtmosphereSettings.asset is plain YAML, editable from code.
