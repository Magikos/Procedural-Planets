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
│   │       ├── Atmosphere.shader        # Post-process: wavelength-based scattering, sun disc
│   │       ├── OpticalDepth.compute     # Compute shader: bakes optical depth LUT (normalized radius)
│   │       ├── BlueNoise.png            # Dither texture for atmosphere
│   │       ├── Planet.shadergraph       # OLD shader graph (unused, kept for reference)
│   │       └── Includes/
│   │           ├── Atmosphere.hlsl      # Scattering math (Rayleigh, Mie, optical depth)
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
│   │   │   │   ├── PlanetGeneratedEvent.cs # + PlanetGenerationProgressEvent
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
│   │   │   │   ├── FreeCameraController.cs # WASD + mouse camera, spacebar reset
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
│   │   │   │   ├── AtmosphereController.cs    # Drives atmosphere: bakes LUT, sets globals
│   │   │   │   ├── AtmosphereRenderFeature.cs # URP ScriptableRendererFeature
│   │   │   │   └── AtmosphereRenderPass.cs    # RenderGraph pass: fullscreen scattering
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
│       │   └── Biomes/                  # BiomeRegistry, BiomeSettings, 15 BiomeDefinitions
│       ├── PC_RPAsset.asset             # URP pipeline asset (has AtmosphereRenderFeature)
│       └── PC_Renderer.asset            # URP renderer asset
├── local-only/                          # Reference projects (not in main build)
│   ├── Fluid-Planet-main/               # Sebastian Lague fluid planet
│   ├── Geographical-Adventures-main/    # Full planet game reference
│   ├── URP-Atmosphere-main/             # URP atmosphere reference (basis for our implementation)
│   ├── Clouds-master/                   # Volumetric clouds reference
│   └── Procedural Planet E01–E07/       # Tutorial episodes
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

### Atmosphere System (Post-Process)
```
AtmosphereController (MonoBehaviour)
  ├── Bakes optical depth LUT via compute shader (once per generation)
  ├── Sets global shader properties each frame (sun direction)
  └── Listens to PlanetGeneratedEvent for radius/position

AtmosphereRenderFeature → AtmosphereRenderPass
  ├── URP ScriptableRendererFeature + RenderGraph pass
  ├── Fullscreen triangle via SV_VertexID (DrawProcedural)
  ├── Ray-marched Rayleigh + Mie scattering
  ├── Reads _CameraDepthTexture for depth-aware rendering
  └── Works from both space and surface views
```

### Star System
```
Stars rendered inside atmosphere post-process shader (no mesh).
StarSphere.cs and Stars.shader are DELETED.
Planet.EnsureStarSphere() is DELETED.
Future: constellation system will query star directions from C# seed-based generation.
```

### Startup Flow
```
OnEnable (all listeners subscribe to EventBus)
  ↓
Planet.Start()
  ├── Has _lastGeneratedRadius? → Raise PlanetGeneratedEvent
  └── No data? → GeneratePlanetAsync() → Raise event when done
  ↓
All listeners receive PlanetGeneratedEvent:
  ├── FreeCameraController → reposition camera
  ├── CelestialManager → set planet radius, moon orbit
  ├── AtmosphereController → bake LUT, set shader globals
  └── StarSphere → set sphere radius, regenerate mesh
```

### Key Patterns
- **EventBus**: Subscribe in OnEnable, unsubscribe in OnDisable. Initialization in Start.
- **Planet owns startup**: Raises event if data exists, otherwise auto-generates.
- **No OnValidate**: Generation only via button press or Start.
- **No serialized meshes**: All meshes generated at runtime, _lastGeneratedRadius persisted for startup.
- **Global shader properties**: Atmosphere uses Shader.SetGlobal* for all parameters.
- **Assembly separation**: Core (no Planet dependency) ← Planet (references Core + URP).

## Atmosphere Rewrite (In Progress)

### Problem
Current atmosphere uses raw Rayleigh/Mie/Absorption coefficients from URP-Atmosphere reference.
These are scale-dependent — tuned for a specific planet radius. At our radius (~5257),
the sky is white/foggy or black at zenith. Stars as mesh quads don't render (far clip, atmosphere overwrite).

### Solution: Solar System Project Model
Wavelength-based scattering with `/planetRadius` normalization for scale independence.

### What Changes
1. **Atmosphere.hlsl** → Rewrite: single `densityFalloff`, wavelength-based `scatteringCoefficients`,
   `opticalDepthBaked2` for bidirectional view-ray sampling, `/planetRadius` normalization.
   Add sun disc rendering. Add procedural star rendering (seed-based, no mesh).
2. **Atmosphere.shader** → Update: pass UV to fragment for blue noise dithering.
3. **OpticalDepth.compute** → Simplify: single-channel density (no separate Rayleigh/Mie/Ozone),
   single `densityFalloff`, normalized radius (planetRadius=1).
4. **AtmosphereController.cs** → Rewrite fields: remove Rayleigh/Mie/Absorption vectors,
   add wavelengths (700,530,460), scatteringStrength, single densityFalloff.
   Compute scatteringCoefficients from wavelengths: `(400/λ)^4 * strength`.
5. **DELETE**: StarSphere.cs, Stars.shader, Planet.EnsureStarSphere()

### What Stays
- AtmosphereRenderFeature.cs (RenderGraph infrastructure) — unchanged
- AtmosphereRenderPass.cs (RenderGraph pass) — unchanged
- Common.hlsl, Math.hlsl — unchanged
- BlueNoise.png — now used for dithering in atmosphere shader
