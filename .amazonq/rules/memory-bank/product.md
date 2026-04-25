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
- **Biome Coloring**: Gradient-based biome system with tint, blend, and ocean color support via shader texture
- **Deterministic Seed**: All procedural systems use a propagated seed for reproducible results
- **ScriptableObject Settings**: ShapeSettings and ColorSettings as reusable asset configurations
- **Custom Editor**: PlanetEditor with inline settings editing, auto-update, and "Generate Planet" button
- **Poisson-Disc Sampling**: 2D flat and 3D sphere variants for natural point distribution
- **Object Spawning**: SpawnLocation struct with position, elevation, normal, and biome index
- **Shapes Plugin**: Integrated for debug/test visualization (Shapes library by Freya Holmér)

## Current Phase
Phase 1–2 (Core Generation + Biomes) are implemented. The project is working toward:
- Phase 3: Quadtree LOD
- Phase 4: Spherical gravity + character controller
- Phase 5: Polish, atmosphere, water, clouds, vegetation

## Target Users
- Game developers building procedural world systems
- Hobbyists learning procedural generation in Unity
- Developers prototyping planetary exploration games

## Scenes
- **Planet.unity** — Main planet generation scene
- **Placement.unity** — Object placement testing scene
