# Procedural Planets Project Description

## Overview
This project is a Unity 6 implementation of a procedural planet generator, inspired by Sebastian Lague's "Procedural Planets" tutorial series (GitHub: https://github.com/SebLague/Procedural-Planets, ~2018-2019). The goal is to create a scalable, performant planet with procedural terrain, biomes, and a character controller for a 1.8m-tall character to walk on the surface, feeling like a true planetary environment. The project modernizes the original with Unity 6 features (e.g., URP, Jobs) and adds spherical gravity, not present in the original.

## Current State
- **Base Planet**: Implemented a cube-sphere (6 faces projected to a sphere) using C# scripts (Planet.cs, TerrainFace.cs, ShapeSettings.cs, Noise.cs, ColorSettings.cs).
  - Planet radius: ~1000 units (1km), adjustable for "planetary" scale.
  - Terrain: Generated with simplex and ridged noise, but currently produces spiky, unrealistic features (needs tuning for hills, valleys, mountains, oceans).
  - Colors: Basic biome system (3 biomes: ocean, plains, mountains), but stuck on blue, likely due to narrow elevation range or normalization issues in heightPercent.
- **LOD (Level of Detail)**: Implemented quadtree-based LOD for each face (TerrainChunk.cs, LODSettings.cs).
  - Chunks subdivide based on camera distance (high res near, low far).
  - Issue: All chunks initially disabled (SetVisible(true) fixed, but needs testing).
- **Gravity and Character**: Added spherical gravity (GravityAttractor.cs, GravityBody.cs) and a basic walking controller (PlanetWalker.cs).
  - Gravity pulls to planet center, aligns character to surface normal.
  - Character (1.8m) can walk/jump, but needs testing with fixed LOD/terrain.
- **Assets**: ShapeSettings (noise config), ColorSettings (biomes), LODSettings (LOD config).
- **Issues**:
  - Noise produces spiky terrain, not realistic hills/valleys/mountains.
  - Colors stuck on blue (likely elevation normalization in UpdateColors).
  - LOD chunks may not display correctly (visibility bug).
  - Performance not optimized yet (no Jobs/Burst for noise/mesh).

## Project Goals
1. **Realistic Terrain**:
   - Refine noise (Noise.cs, ShapeSettings.cs) for broad hills (~50-100m), valleys/oceans (-50m), and smooth mountains (~100-200m).
   - Use multiple layers: simple for base, ridged for peaks, optional for details.
   - Target: Visually appealing, varied landscape at radius=1000-5000 units.
2. **Biome Colors**:
   - Fix biome coloring (ColorSettings.cs, TerrainChunk.cs) to show blue oceans, green plains, brown/white mountains.
   - Ensure heightPercent spans 0-1, reflecting elevation range.
3. **Performant LOD**:
   - Ensure quadtree LOD works: high res (e.g., 241) near camera, low res (e.g., 10) far away.
   - Use Unity Jobs for async mesh generation.
   - Test with large radius (1000-5000) without lag.
4. **Character Walking**:
   - 1.8m character walks/jumps on surface, aligned to spherical gravity.
   - Stable collision with procedural terrain (using CharacterController or Rigidbody).
   - Target: Seamless exploration on a large planet.
5. **Future Enhancements**:
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