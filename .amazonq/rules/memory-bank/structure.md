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
│   │   │   └── Planet.mat               # Planet material (URP)
│   │   └── Shaders/
│   │       └── Planet.shadergraph       # Shader Graph for planet rendering
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
│   │   │   │   └── PlanetGeneratedEvent.cs
│   │   │   ├── Interfaces/
│   │   │   │   ├── IBiomeProvider.cs    # Biome evaluation interface
│   │   │   │   ├── IBiomeRegistry.cs    # Temperature × moisture → biome lookup
│   │   │   │   ├── IColorProvider.cs    # Color/texture management interface
│   │   │   │   ├── ILogger.cs           # Logging interface
│   │   │   │   ├── IMeshBuilder.cs      # Mesh building interface
│   │   │   │   ├── IMoistureProvider.cs # Moisture evaluation interface
│   │   │   │   ├── ISeedProvider.cs     # Deterministic seed interface
│   │   │   │   ├── ITemperatureProvider.cs # Temperature evaluation interface
│   │   │   │   ├── ITerrainProvider.cs  # Elevation evaluation interface
│   │   │   │   └── IWorldAction.cs      # Command pattern interface
│   │   │   ├── Services/
│   │   │   │   ├── FreeCameraController.cs
│   │   │   │   ├── GameBootstrap.cs
│   │   │   │   ├── SeedProvider.cs
│   │   │   │   ├── ServiceLocator.cs
│   │   │   │   ├── UnityLogger.cs
│   │   │   │   └── WorldActionManager.cs
│   │   │   ├── Utilities/
│   │   │   │   ├── CoordinateConverter.cs
│   │   │   │   └── ObjectPool.cs
│   │   │   └── ProceduralPlanets.Core.asmdef
│   │   ├── Planet/
│   │   │   ├── Biomes/
│   │   │   │   ├── BiomeDefinition.cs   # ScriptableObject: per-biome color/tint
│   │   │   │   ├── BiomeRegistry.cs     # ScriptableObject: temp×moisture grid + elevation overrides
│   │   │   │   ├── BiomeSettings.cs     # ScriptableObject: noise config for temp/moisture
│   │   │   │   ├── TemperatureProvider.cs # Latitude-based + noise perturbation
│   │   │   │   └── MoistureProvider.cs  # Noise-based moisture evaluation
│   │   │   ├── NoiseFilters/
│   │   │   │   ├── INoiseFilter.cs      # Noise filter interface
│   │   │   │   ├── NoiseFilterFactory.cs # Factory for creating noise filters
│   │   │   │   ├── SimpleNoiseFilter.cs  # Standard layered simplex noise
│   │   │   │   └── RigidNoiseFilter.cs   # Ridge-style noise (extends Simple)
│   │   │   ├── Planet.cs                # Main planet MonoBehaviour (orchestrator + water mesh)
│   │   │   ├── PlanetSettings.cs        # ScriptableObject: user-friendly generation params
│   │   │   ├── ShapeGenerator.cs        # Evaluates noise layers → elevation (no clamp)
│   │   │   ├── TerrainFace.cs           # Generates mesh for one cube face
│   │   │   ├── ColorGenerator.cs        # Biome texture via temp/moisture/registry
│   │   │   ├── Noise.cs                 # Simplex noise implementation (seed-based)
│   │   │   ├── MinMax.cs                # Thread-safe min/max elevation tracking
│   │   │   ├── ShapeSettings.cs         # ScriptableObject: radius + noise layers (built by PlanetSettings)
│   │   │   ├── ColorSettings.cs         # ScriptableObject: material + BiomeSettings (built by PlanetSettings)
│   │   │   ├── NoiseSettings.cs         # Serializable noise parameters
│   │   │   └── ProceduralPlanets.Planet.asmdef
│   │   ├── PoissonDiscSampling.cs       # 2D Poisson-disc point generation
│   │   ├── PoissonDiscSphereSampling.cs # 3D sphere Poisson-disc with biome data
│   │   ├── Test.cs                      # 2D Poisson-disc visualization test (Gizmos)
│   │   ├── TestPoissonDiscSphereDraw.cs # 3D sphere placement visualization test (Gizmos)
│   │   └── ProceduralPlanets.Sampling.asmdef
│   ├── Settings/
│   │   ├── Planet Settings/
│   │   │   ├── Planet.asset             # PlanetSettings instance (user-friendly params)
│   │   │   ├── Shape.asset              # ShapeSettings instance (legacy, kept for reference)
│   │   │   ├── Color.asset              # ColorSettings instance (legacy, kept for reference)
│   │   │   └── Biomes/                  # All biome ScriptableObject assets
│   │   │       ├── BiomeRegistry.asset  # 4×3 temp×moisture grid + elevation overrides
│   │   │       ├── BiomeSettings.asset  # Temperature/moisture noise config
│   │   │       └── *.asset              # 15 BiomeDefinition assets (flat debug colors)
│   │   ├── PC_RPAsset.asset             # URP render pipeline asset (PC)
│   │   ├── Mobile_RPAsset.asset         # URP render pipeline asset (Mobile)
│   │   └── DefaultVolumeProfile.asset   # Post-processing volume
│   └── PlanetBiomeGradient.png          # Biome gradient texture reference
├── docs/
│   ├── PROJECT_PLAN.md                  # Master index of all phases
│   └── phases/                          # Individual phase documents
├── local-only/                          # Reference projects (not in main build)
│   ├── Fluid-Planet-main/               # Fluid planet reference project
│   └── Procedural Planet E01–E07/       # Sebastian Lague tutorial episodes
├── Packages/
│   └── manifest.json                    # Unity package dependencies
├── ProjectSettings/                     # Unity project configuration
└── ProceduralPlanets.sln                # Visual Studio solution
```

## Core Architecture

### Generation Pipeline
```
Planet (MonoBehaviour — orchestrator)
  ├── PlanetSettings (user-friendly ScriptableObject)
  │   ├── BuildShapeSettings() → ShapeSettings (noise layers from friendly params)
  │   └── BuildColorSettings() → ColorSettings (material + biome refs)
  ├── ShapeGenerator : ITerrainProvider (elevation calculation, no clamp)
  │   ├── NoiseFilterFactory → INoiseFilter[]
  │   │   ├── SimpleNoiseFilter (layered simplex)
  │   │   └── RigidNoiseFilter (ridge noise, extends Simple)
  │   └── Noise (simplex noise, seed-based permutation)
  ├── TerrainFace[6] (one per cube face → mesh, uses ITerrainProvider)
  ├── ColorGenerator : IBiomeProvider + IColorProvider
  │   ├── TemperatureProvider : ITemperatureProvider (latitude + noise → temperature)
  │   ├── MoistureProvider : IMoistureProvider (noise → moisture)
  │   └── BiomeRegistry : IBiomeRegistry (temp × moisture grid → BiomeResult)
  └── Water Mesh (cube-sphere at base radius, transparent URP Lit material)
```

### Unified Scale Model
```
Mountain peak:  radius = PlanetRadius * (1 + positiveElevation)  → above water
Sea level:      radius = PlanetRadius * (1 + OceanLevel)         → water sphere
Ocean floor:    radius = PlanetRadius * (1 + negativeElevation)  → below water
```
- Terrain goes both above AND below base radius (no elevation clamp)
- Water sphere sits at exactly PlanetRadius (+ OceanLevel offset)
- Land above water = visible continents; terrain below water = hidden ocean basins

### PlanetSettings Parameters
```
PlanetRadius:      1-5000 (Unity units)
ContinentSize:     0.1-1.0 (small islands → large landmasses)
OceanDepth:        0-1.0 (shallow → deep basins)
MountainHeight:    0-1.0 (flat → extreme peaks)
MountainDensity:   0-1.0 (few → many)
TerrainRoughness:  0-1.0 (smooth → jagged detail)
HasOceans:         bool (toggles water sphere)
OceanLevel:        -0.05 to 0.05 (raise/lower water)
WaterColor:        Color with alpha
```
These translate internally to 3 noise layers: continent shelf, mountains (rigid), surface detail.

### Data Flow
1. **Planet.Initialize()** — PlanetSettings builds ShapeSettings + ColorSettings, creates 6 TerrainFaces
2. **Planet.GenerateMeshAsync()** — Parallel.For across 6 faces on background thread via Awaitable
3. **ShapeGenerator** — Evaluates noise layers per vertex, tracks elevation MinMax (thread-safe)
4. **Planet.GenerateColors()** — ColorGenerator builds biome texture, TerrainFace updates UVs
5. **Planet.GenerateWater()** — Builds cube-sphere water mesh at base radius
6. **Shader** — Uses `_ElevationMinMax` and `_Texture` to render biome colors

### Biome Resolution Flow
```
pointOnUnitSphere
  → TemperatureProvider.Evaluate() → temperature (latitude + noise)
  → MoistureProvider.Evaluate() → moisture (noise)
  → BiomeRegistry.Resolve(temp, moisture, elevation)
    → elevation overrides (Ocean/Beach/Mountain/SnowyMountain) checked first
    → temp × moisture grid lookup with boundary blending
    → BiomeResult (primary, secondary, blend weight, temp, moisture)
```

### Biome Texture Layout
```
Row 0:  Ocean          (sand/brown — ocean floor visible through water)
Row 1:  Beach          (sandy yellow)
Rows 2-13: Grid biomes (temp × moisture: Tundra→Snow→IceBog→Steppe→Taiga→Swamp→Scrub→Grassland→Forest→Desert→Savanna→Tropical)
Row 14: Mountain       (grey rock — warm/hot high elevation)
Row 15: SnowyMountain  (white — cold high elevation)
```
Texture is 4×16 with Point filtering. Each row is a flat color.
Shader samples texture at (elevationNormalized, UV.x) where UV.x = biome row percent.

### Known Bugs
- **Coastline rainbow strip**: Multi-colored band at ocean-to-land transition. Caused by shader elevation sampling interacting with biome texture at tiny elevation values. Will resolve with water mesh (Phase 5) or planet scale increase.

### Key Patterns
- **ScriptableObject Settings**: PlanetSettings (user-friendly), ShapeSettings, ColorSettings, BiomeSettings, BiomeRegistry, BiomeDefinition
- **Factory Pattern**: NoiseFilterFactory creates appropriate INoiseFilter based on FilterType
- **Interface Abstraction**: ITerrainProvider, IBiomeProvider, IColorProvider, ITemperatureProvider, IMoistureProvider, IBiomeRegistry
- **Inheritance**: RigidNoiseFilter extends SimpleNoiseFilter, overriding Evaluate
- **Cube-Sphere**: 6 faces × resolution² vertices, projected to unit sphere, scaled by elevation
- **Deterministic Seed**: Planet → ShapeGenerator (seed+i per layer), TemperatureProvider (seed), MoistureProvider (seed+100)
- **Async Generation**: Single async path via GeneratePlanetAsync, Parallel.For for 6 faces, CancellationToken support
- **Temperature × Moisture Grid**: Biomes placed on continuous 2D gradient, preventing illogical adjacencies
