# Project Structure — Procedural Planets

## Directory Layout
```
ProceduralPlanets/
├── Assets/
│   ├── Editor/
│   │   └── PlanetEditor.cs              # Custom inspector for Planet component
│   ├── Graphics/
│   │   ├── Materials/
│   │   │   └── Planet.mat               # Planet material (URP)
│   │   └── Shaders/
│   │       └── Planet.shadergraph       # Shader Graph for planet rendering
│   ├── Plugins/
│   │   └── Shapes/                      # Shapes library (Freya Holmér) for debug drawing
│   ├── Scenes/
│   │   ├── Planet.unity                 # Main planet scene
│   │   └── Placement.unity              # Object placement test scene
│   ├── Scripts/
│   │   ├── Planet/
│   │   │   ├── NoiseFilters/
│   │   │   │   ├── INoiseFilter.cs      # Noise filter interface
│   │   │   │   ├── NoiseFilterFactory.cs # Factory for creating noise filters
│   │   │   │   ├── SimpleNoiseFilter.cs  # Standard layered simplex noise
│   │   │   │   └── RigidNoiseFilter.cs   # Ridge-style noise (extends Simple)
│   │   │   ├── Planet.cs                # Main planet MonoBehaviour (orchestrator)
│   │   │   ├── ShapeGenerator.cs        # Evaluates noise layers → elevation
│   │   │   ├── TerrainFace.cs           # Generates mesh for one cube face
│   │   │   ├── ColorGenerator.cs        # Biome texture + elevation coloring
│   │   │   ├── Noise.cs                 # Simplex noise implementation (seed-based)
│   │   │   ├── MinMax.cs                # Tracks min/max elevation values
│   │   │   ├── ShapeSettings.cs         # ScriptableObject: radius + noise layers
│   │   │   ├── ColorSettings.cs         # ScriptableObject: biomes + ocean gradient
│   │   │   └── NoiseSettings.cs         # Serializable noise parameters
│   │   ├── PoissonDiscSampling.cs       # 2D Poisson-disc point generation
│   │   ├── PoissonDiscSphereSampling.cs # 3D sphere Poisson-disc with biome data
│   │   ├── Test.cs                      # 2D Poisson-disc visualization test
│   │   └── TestPoissonDiscSphereDraw.cs # 3D sphere placement visualization test
│   ├── Settings/
│   │   ├── Planet Settings/             # ScriptableObject instances for planet config
│   │   ├── PC_RPAsset.asset             # URP render pipeline asset (PC)
│   │   ├── Mobile_RPAsset.asset         # URP render pipeline asset (Mobile)
│   │   └── DefaultVolumeProfile.asset   # Post-processing volume
│   └── PlanetBiomeGradient.png          # Biome gradient texture reference
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
  ├── ShapeGenerator (elevation calculation)
  │   ├── NoiseFilterFactory → INoiseFilter[]
  │   │   ├── SimpleNoiseFilter (layered simplex)
  │   │   └── RigidNoiseFilter (ridge noise, extends Simple)
  │   └── Noise (simplex noise, seed-based permutation)
  ├── TerrainFace[6] (one per cube face → mesh)
  └── ColorGenerator (biome texture + elevation mapping)
```

### Data Flow
1. **Planet.Initialize()** — Creates 6 TerrainFaces, configures generators with settings + seed
2. **Planet.GenerateMesh()** — Each TerrainFace constructs mesh vertices via ShapeGenerator
3. **ShapeGenerator** — Evaluates noise layers per vertex, tracks elevation MinMax
4. **Planet.GenerateColors()** — ColorGenerator builds biome texture, TerrainFace updates UVs
5. **Shader** — Uses `_ElevationMinMax` and `_Texture` to render biome colors

### Key Patterns
- **ScriptableObject Settings**: ShapeSettings and ColorSettings are asset-based, reusable across planets
- **Factory Pattern**: NoiseFilterFactory creates appropriate INoiseFilter based on FilterType
- **Interface Abstraction**: INoiseFilter allows swappable noise algorithms
- **Inheritance**: RigidNoiseFilter extends SimpleNoiseFilter, overriding Evaluate
- **Cube-Sphere**: 6 faces × resolution² vertices, projected to unit sphere, scaled by elevation
- **Deterministic Seed**: Propagated from Planet → ShapeGenerator → NoiseFilterFactory → Noise
