# Procedural Planets Project Description

## Overview

This project is a Unity 6 implementation of a procedural planet generator, inspired by Sebastian Lague's "Procedural Planets" tutorial series (GitHub: https://github.com/SebLague/Procedural-Planets, ~2018-2019). The goal is to create a scalable, performant planet with procedural terrain, biomes, and a character controller for a 1.8m-tall character to walk on the surface, feeling like a true planetary environment. The project modernizes the original with Unity 6 features (e.g., URP, Jobs) and adds spherical gravity, not present in the original.

## Project Plan

This project is a fresh start for a procedural planet generator in Unity 6, inspired by modern best practices and leveraging Unity's latest features (URP, Jobs, Burst, etc.). The goal is to create a scalable, performant, and visually appealing planet with procedural terrain, biomes, and a character controller for planetary exploration.

### Phase 1: Core Planet Generation

- Implement a cube-sphere mesh (6 faces projected to a sphere) for the planet base.
- Create a flexible noise system for terrain elevation (supporting multiple noise layers: base, ridged, detail).
- Expose planet radius and noise parameters for easy tuning.

### Phase 2: Terrain and Biomes

- Design a system for realistic terrain: broad hills, valleys/oceans, smooth mountains.
- Implement a biome system (e.g., ocean, plains, mountains) with color gradients based on elevation and position.
- Ensure proper normalization of elevation for accurate biome/color mapping.

### Phase 3: Level of Detail (LOD)

- Implement quadtree-based LOD for each planet face to optimize mesh detail based on camera distance.
- Ensure seamless transitions between LOD levels.
- Optimize mesh generation using Unity Jobs/Burst for performance.

### Phase 4: Gravity and Character Controller

- Add spherical gravity so objects/characters are attracted to the planet center.
- Implement a character controller that walks/jumps on the planet surface, aligned to the local normal.
- Ensure stable collision and smooth movement on procedural terrain.

### Phase 5: Polish and Optimization

- Refine noise and biome settings for visually appealing results at various planet scales (radius 1000-5000 units).
- Profile and optimize performance (Jobs, Burst, mesh updates).
- Add optional features: atmosphere, water, clouds, vegetation scattering.

### Stretch Goals

- Support for runtime planet generation and editing.
- Advanced biome blending and climate simulation.
- Integration with Unity's DOTS for massive scalability.

---

**Next Steps:**

1. Set up Unity 6 project with URP.
2. Implement basic cube-sphere mesh and noise-based terrain.
3. Add simple biome coloring and test elevation normalization.
4. Develop LOD system and test with large planet radii.
5. Add spherical gravity and character controller for surface exploration.
6. Iterate on noise, biomes, and performance optimizations.
   - Fix biome coloring (ColorSettings.cs, TerrainChunk.cs) to show blue oceans, green plains, brown/white mountains.
   - Ensure heightPercent spans 0-1, reflecting elevation range.
7. **Performant LOD**:
   - Ensure quadtree LOD works: high res (e.g., 241) near camera, low res (e.g., 10) far away.
   - Use Unity Jobs for async mesh generation.
   - Test with large radius (1000-5000) without lag.
8. **Character Walking**:
   - 1.8m character walks/jumps on surface, aligned to spherical gravity.
   - Stable collision with procedural terrain (using CharacterController or Rigidbody).
   - Target: Seamless exploration on a large planet.
9. **Future Enhancements**:
   - Add atmosphere, water, clouds (as in later tutorial episodes).
   - Optimize noise with compute shaders/Burst Compiler.
   - Basic vegetation scattering or simple biomes.

## Current Setup

- **Unity Version**: Unity 6 (latest stable, e.g., 6.0.11f1 or newer).
- **Render Pipeline**: Universal Render Pipeline (URP) for better lighting/performance.

## Project Plan & Goals

This project aims to create a scalable, performant procedural planet in Unity 6, featuring:

### 1. Core Planet Generation

- Cube-sphere mesh for the planet base
- Flexible, multi-layered noise system for terrain
- Exposed parameters for easy tuning

### 2. Biomes & Color Gradients

- Biome system (ocean, sand, grass, forest, mountain, etc.)
- Color gradients based on elevation and position
- Accurate normalization for biome/color mapping

### 3. Object Spawning (Vegetation, Rocks, etc.)

- Use Poisson-disc sampling for natural, non-overlapping placement
- For each spawn point:
  - Project to planet surface (from 2D to 3D)
  - Sample elevation/biome at that location
  - Select asset list based on biome (e.g., palm trees for sand, pines for mountains)
  - Instantiate and align prefab to surface normal

### 4. Level of Detail (LOD) & Culling

- Quadtree-based LOD for planet mesh and spawned objects
- High detail near player, low detail at distance
- Cull objects on the far side of the planet

### 5. Performance Optimization

- Profile and optimize Poisson-disc and mesh generation
- Convert to Unity Jobs/Burst for large-scale performance

### 6. Gravity & Character Controller

- Spherical gravity (attract to planet center)
- Character controller for walking/jumping on surface
- Stable collision and smooth movement

### 7. Stretch Goals

- Runtime planet generation/editing
- Advanced biome blending and climate simulation
- DOTS integration for massive scalability

---

**Next Steps:**

1. Implement object spawning on the planet surface with biome-based asset selection
2. Validate placement and biome logic visually
3. Profile and optimize Poisson-disc generation (convert to Burst/Jobs if needed)
4. Integrate with LOD/culling system for performance
5. Continue with polish, optimization, and stretch goals

## Deterministic Generation (Seed Support)

To ensure that the same planet, terrain, and object placements can be recreated every time, all procedural systems (terrain, biomes, object spawning, etc.) will use a seed-based random number generator. This seed will be stored in project settings or as a field in the main planet/manager script, and passed to all systems that require randomness. Using the same seed will always produce the same results, which is essential for sharing, debugging, and multiplayer consistency.

## Task List

- [ ] Add a seed field to the main planet/manager script and propagate it to all procedural systems
- [ ] Refactor Poisson-disc sampling and noise/biome generation to use a deterministic random number generator (e.g., System.Random or Unity.Mathematics.Random)
- [ ] Implement cube-sphere mesh generation for the planet
- [ ] Implement multi-layered noise system for terrain elevation
- [ ] Implement biome/color gradient system based on elevation and position
- [ ] Implement object spawning using Poisson-disc sampling, projected to the planet surface
- [ ] Sample biome/elevation at each spawn point and select assets accordingly
- [ ] Instantiate and align prefabs to the planet surface normal
- [ ] Implement quadtree-based LOD for planet mesh and spawned objects
- [ ] Implement culling for distant/hidden objects
- [ ] Add spherical gravity and character controller for surface exploration
- [ ] Profile and optimize performance; convert to Jobs/Burst as needed
- [ ] Add polish, optional features, and stretch goals (atmosphere, water, runtime editing, etc.)
