# Product Overview — Procedural Planets

## Purpose
A Unity 6 procedural planet generator that creates explorable, seed-based planetary worlds with terrain, biomes, and object placement. Inspired by Sebastian Lague's "Procedural Planets" series, modernized with Unity 6 features (URP, Jobs/Burst planned).

## Value Proposition
- Fully procedural, deterministic planet generation from a single seed
- Cube-sphere mesh with multi-layered simplex noise terrain
- Biome system with elevation-based color gradients and blending
- Poisson-disc sampling for natural object placement on the planet surface
- Designed for scalability (radius 1000–5000 units) with planned LOD and character controller

## Key Features
- **Cube-Sphere Mesh**: 6-face projected sphere with configurable resolution (2–256)
- **Noise System**: Layered simplex noise with Simple and Rigid filter types, first-layer masking support
- **Biome System**: Temperature × moisture registry with gradient-based coloring, tint, and boundary blending; elevation overrides for ocean/beach/mountain
- **Water**: Runtime-generated ocean mesh (`WaterMeshBuilder`, global cube-face seam sharing) plus a multi-pass `WaterVolume` renderer for depth absorption, shoreline/foam, waves, glint, and underwater volume
- **Atmosphere**: Brute-force Rayleigh+Mie scattering as a URP post-process (`AtmosphereRenderFeature`), with stars and night ambient
- **Celestial**: Sun/moon orbits, day/night, moon phases (`CelestialManager`)
- **Weather**: Planet-scale cube-sphere weather grid evolved on the GPU; drives wind, clouds, precipitation
- **Clouds**: Volumetric cloud rendering (`CloudRenderFeature`) coupled to the weather grid
- **Precipitation**: Rain/storm shafts and lightning (`PrecipitationController`, `WeatherLightningController`)
- **Deterministic Seed**: All procedural systems use a propagated seed (`ISeedProvider`) for reproducible results
- **ScriptableObject Settings**: PlanetSettings, ShapeSettings, BiomeSettings, AtmosphereSettings, CloudSettings as reusable assets
- **Bootstrap / Loading**: `LoadingManager` drives `IEarlyInitialize`/`ILateInitialize` ordering with a progress overlay (SDF text)
- **Debug Framework**: F6–F11 debug hotkeys via `DebugInputRelay` → EventBus; F10 water-artifact capture sets via `DebugCaptureController`
- **Custom Editors**: `PlanetEditor` (inline settings, regenerate), `BiomeRegistryEditor` (grid inspector)
- **Poisson-Disc Sampling**: 2D flat + 3D sphere variants for future object spawning
- **Shapes Plugin**: Integrated for debug/test visualization (Shapes library by Freya Holmér)

## Current Phase
Active branch: `phase4-biomes`. Implemented well beyond the original Phases 1–2:
- Phases 1–2: Core cube-sphere generation + biome system — **done**
- Phase 4 (Biomes), Phase 5 (Water), Phase 6 (Celestial), Phase 7 (Moons), plus Atmosphere, Weather, Clouds, and Precipitation are substantially implemented
- Current focus: water visual polish (see `water.md`) and ongoing code-health work (see `docs/audit/`)
- Not yet started: chunk/LOD streaming (Phase 13), marching cubes/caves (Phase 9), character controller (Phase 10), resources/crafting/building (Phases 11–12), multiplayer (Phase 14)

## Target Users
- Game developers building procedural world systems
- Hobbyists learning procedural generation in Unity
- Developers prototyping planetary exploration games

## Scenes
- **Planet.unity** — Main planet generation scene
- **Placement.unity** — Object placement testing scene
