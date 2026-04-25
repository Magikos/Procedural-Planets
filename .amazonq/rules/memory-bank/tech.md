# Technology Stack — Procedural Planets

## Engine & Runtime
- **Unity 6** (6000.2.0b11 beta)
- **Render Pipeline**: Universal Render Pipeline (URP) 17.2.0
- **Language**: C# (Unity scripting)
- **IDE**: Visual Studio (solution: ProceduralPlanets.sln)

## Key Unity Packages
| Package | Version | Purpose |
|---------|---------|---------|
| com.unity.render-pipelines.universal | 17.2.0 | URP rendering |
| com.unity.inputsystem | 1.14.1 | New Input System |
| com.unity.ai.navigation | 2.0.8 | AI navigation (future use) |
| com.unity.timeline | 1.8.7 | Timeline (future use) |
| com.unity.visualscripting | 1.9.7 | Visual scripting |
| com.unity.test-framework | 1.5.1 | Testing |

## Third-Party Plugins
- **Shapes** (Freya Holmér) — Immediate-mode drawing library for debug visualization (Assets/Plugins/Shapes/)

## Project Assemblies
- **Assembly-CSharp** — Main game scripts (Planet/, Poisson sampling, tests)
- **Assembly-CSharp-Editor** — Editor scripts (PlanetEditor.cs)
- **ShapesRuntime / ShapesEditor / ShapesSamples** — Shapes plugin assemblies

## Shader Stack
- **Shader Graph** — Planet.shadergraph for planet surface rendering
- Custom properties: `_ElevationMinMax` (Vector4), `_Texture` (Texture2D biome map)

## Rendering Configuration
- PC and Mobile URP renderer assets (PC_RPAsset, Mobile_RPAsset)
- DefaultVolumeProfile for post-processing
- SampleSceneProfile for scene-specific settings

## Planned Technologies (from project roadmap)
- **Unity Jobs System** — Async mesh generation for LOD
- **Burst Compiler** — Performance optimization for noise/mesh
- **Compute Shaders** — Potential noise optimization
- **DOTS** — Stretch goal for massive scalability

## Development Workflow
- Open in Unity 6 (6000.2.0b11+)
- Main scene: Assets/Scenes/Planet.unity
- Planet settings configured via ScriptableObjects in Assets/Settings/Planet Settings/
- Custom editor auto-updates planet on parameter changes
- local-only/ contains reference projects (excluded from build)

## Build Notes
- No custom build scripts detected
- Standard Unity build pipeline
- URP configured for both PC and Mobile targets
