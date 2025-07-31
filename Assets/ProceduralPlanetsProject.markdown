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
- **Scripts**:
  - Planet.cs: Manages 6 root chunks, initializes ShapeGenerator, ColorGenerator.
  - TerrainChunk.cs: Handles quadtree LOD, mesh generation per chunk.
  - ShapeGenerator.cs, NoiseFilterFactory.cs: Compute elevations via noise.
  - ColorGenerator.cs: Stub for biome colors (needs fixing).
  - GravityAttractor.cs, GravityBody.cs, PlanetWalker.cs: Spherical gravity and character movement.
  - ShapeSettings.cs, ColorSettings.cs, LODSettings.cs: Config assets.
- **Settings** (example, to be tuned):
  - ShapeSettings: radius=1000, noise layers (simple: strength=0.08, numLayers=6, baseRoughness=0.5; ridged: strength=0.15, baseRoughness=1.5).
  - ColorSettings: 3 biomes (ocean: startHeight=0, plains: 0.2, mountains: 0.5).
  - LODSettings: maxLOD=5, lodDistances={0,0.8,0.4,0.2,0.1,0.05}.

## Next Steps

1. **Fix LOD Visibility**:
   - Ensure root chunks and subchunks display (TerrainChunk.cs: SetVisible(true) for leaves).
   - Debug bounds and subdivision logic (UpdateChunk).
2. **Test Gravity/Character**:
   - Verify player falls to surface, walks/jumps, aligns to normals.
   - Fix PlanetWalker.cs planet reference (via FindObjectOfType or public field).
3. **Refine Noise**:
   - Adjust noise layers for realistic terrain (broad hills, smooth mountains, deep valleys).
   - Add debug logs for elevationMin/Max, heightPercent.
4. **Fix Colors**:
   - Ensure biome gradients apply correctly (blue lows, green mids, brown/white highs).
   - Fix normalization in TerrainChunk.UpdateColors.
5. **Optimize**:
   - Use Jobs/Burst for noise/mesh generation.
   - Test performance at radius=1000-5000.

## Notes for Collaborators

- Check console for errors or debug logs (e.g., elevation ranges, chunk visibility).
- Share screenshots (e.g., via Imgur) of Inspector settings or planet visuals.
- Current planet radius (1000) is for testing; may scale to 5000 for more "planetary" feel.
- Focus on LOD and gravity functionality before noise/color polish.
- Use American English spelling (e.g., "color", not "colour").
