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

## Atmosphere Rewrite v3 (In Progress)

### Why Previous Approaches Failed
- **URP-Atmosphere (v1)**: Raw coefficients tuned for specific small planet radius. At our radius (~5257), optical depth too large, all channels blown out or killed equally (gray/white).
- **Solar System hybrid (v2)**: `/planetRadius` normalization + normalized LUT. Scale mismatch between LUT (normalized) and shader (world-space). Hacky attenuation didn't generalize. Wavelength-based coefficients couldn't be tuned to work at our scale.
- **Core issue**: Both approaches used baked LUT for sun ray optical depth, adding complexity and scale-dependent bugs. Coefficients from reference projects don't transfer to different planet radii.

### New Approach: Step-by-Step from First Principles
Based on `local-only/atmospheric_scattering_shader_unity_guide.md` (from cpp-rendering.io article).

**Key principles:**
- Brute-force ray marching for BOTH view and sun rays (no LUT initially)
- Verify each step with debug modes before adding the next
- Use `exp(-height / scaleHeight)` density (no extra `* (1-height01)` term)
- Scale-independent by design — parameterize everything, no hardcoded Earth values
- Keep infrastructure: RenderGraph pass, ScriptableObject settings, diagnostics, DepthOnly pass

**Implementation roadmap (from guide Section 20):**
1. **Ray-sphere intersection** — verify atmosphere/planet hit detection, camera inside/outside
2. **Basic density visualization** — render density as grayscale, verify falloff
3. **View ray marching** — accumulate density, verify horizon thicker than zenith
4. **Sun ray optical depth** — add sun transmittance, verify day/night
5. **Rayleigh scattering** — wavelength-dependent, verify blue sky + red sunset
6. **Mie scattering** — sun glow/haze
7. **Artist controls + debug modes** — expose all parameters, debug views

**Debug modes (from guide Section 18):**
0=final, 1=height, 2=Rayleigh density, 3=Mie density, 4=sun transmittance, 5=view transmittance, 6=Rayleigh only, 7=Mie only, 8=optical depth

**Starting parameters (from guide Section 21, adapted for our scale):**
- planetRadius: ~5257 (from generation)
- atmosphereRadius: planetRadius * 1.05 to 1.1
- rayleighScaleHeight: 0.08 * atmosphereThickness
- mieScaleHeight: 0.02 * atmosphereThickness
- rayleighScattering: float3(5.8, 13.5, 33.1) * scale factor TBD
- mieScattering: 0.01
- mieAnisotropy: 0.76
- sunIntensity: 20
- viewSteps: 16, sunSteps: 8

### What We Keep
- AtmosphereRenderFeature.cs + AtmosphereRenderPass.cs (RenderGraph infrastructure)
- AtmosphereController.cs (EventBus, ScriptableObject settings, real-time update)
- AtmosphereSettings.cs (ScriptableObject)
- AtmosphereDiagnostics.cs (F12 capture)
- Atmosphere.shader (fullscreen pass structure)
- PlanetVertexColor.shader DepthOnly/DepthNormals passes
- Common.hlsl, Math.hlsl
- BlueNoise.png
- FreeCameraController improvements (backspace=face sun, surface positioning)

### What We Rewrite
- Atmosphere.hlsl — from scratch, step by step
- OpticalDepth.compute — removed initially, add back as optimization later
- AtmosphereSettings.cs — simplified parameters matching the guide

### Git Reference
- Tag: `atmosphere-v2-checkpoint` — last commit before v3 rewrite
- All previous atmosphere code is preserved in git history
