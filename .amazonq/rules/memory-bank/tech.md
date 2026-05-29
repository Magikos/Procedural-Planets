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
- **PlanetVertexColor.shader** — URP HLSL vertex color as albedo, PBR lighting. Has DepthOnly/DepthNormals passes; supports debug terrain tints (face id, source-color isolation).
- **Ocean.shader** — Transparent ocean surface: depth color, shoreline foam, waves (triplanar phase), glint/whitecaps, terrain-contact fades. Many F10 debug modes.
- **WaterVolume.shader** / **WaterVolumePrepass.shader** — Full-screen underwater/long-path volume composite + the prepass that writes water interface coverage.
- **Atmosphere.shader** + **Includes/Atmosphere.hlsl** — Post-process Rayleigh+Mie ray marching; sea level as density origin, Reinhard on scatter only, light shafts, sun disc.
- **Cloud.shader** + **CloudNoise.compute** — Volumetric clouds driven by the weather grid; stratified per-pixel sampling.
- **Precipitation.shader** — Rain/storm curtains with jittered raymarch.
- **Star.shader** — Background stars + sun disc, clipped against the sea-level planet sphere.
- **SDFText.shader** — Signed-distance-field text (loading overlay, debug labels).
- **LoadingOverlay.shader** — Fullscreen loading overlay + progress bar.
- **WeatherEvolution.compute** — GPU ping-pong evolution of the cube-sphere weather grid.
- **Includes/** — `Common.hlsl`, `Math.hlsl`, `CloudShadows.hlsl`, `WeatherSampling.hlsl`, `DebugModes.hlsl`.
- **OpticalDepth.compute** — Sun-ray optical-depth LUT bake used by the atmosphere controller.
- **Planet.shadergraph** — OLD shader graph (unused, kept for reference).

## Runtime Systems & Bootstrap
- **LoadingManager** (`[RuntimeInitializeOnLoadMethod]`, DontDestroyOnLoad) — drives startup: collects MonoBehaviours, runs `IEarlyInitialize` (desc priority) then `ILateInitialize` (desc priority), shows a loading overlay with `ProgressTracker`/`IProgressReporter` progress.
- **GameBootstrap** (`IEarlyInitialize`, priority 100) — registers `ISeedProvider`, `IWorldActionManager`, `IDebugCommandProvider`; ensures `ShaderGlobalsController`, `QualityController`, `DebugInputRelay`, `DebugCaptureController`, `WaterWakeController`.
- **ServiceLocator** — interface-keyed service registry (no concrete-type registrations).
- **EventBus&lt;T&gt;** — weak-reference static event bus; compiled open-delegate dispatch (no per-event reflection); auto-registers into `EventBusRegistry` for one-call teardown.
- **URP ScriptableRendererFeatures** — Atmosphere, Cloud, Precipitation, WaterVolume, Star (registered on the PC renderer asset).
- **SDF Text** (`Core/Text/`) — runtime SDF font asset + mesh builder + renderer for overlay/debug text.

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
