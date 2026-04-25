# Phase 9: Marching Cubes Terrain & Caves
*Goal: Deformable terrain with cave systems*

## 9.1 — Marching Cubes Chunk System
- [ ] Implement marching cubes algorithm (reference: Fluid-Planet `MarchingCubes.cs`)
- [ ] Create `TerrainChunk` class: density volume → marching cubes mesh
- [ ] Chunks map to sphere surface coordinates, load around player
- [ ] Base density generated from same noise as cube-sphere (consistent terrain shape)
- [ ] Chunk resolution tuned for walkable-scale detail
- [ ] Octree spatial index for efficient chunk management and neighbor queries
- [ ] Mesh generation runs async: `async Awaitable BuildChunkMeshAsync(TerrainChunk chunk, CancellationToken ct)`
- [ ] Density computation on background thread via Jobs/Burst, mesh assignment on main thread

## 9.2 — Cave Generation
- [ ] Add cave noise layer: 3D worm-like noise (Perlin worms or simplex tunnels)
- [ ] Cave noise subtracts from terrain density, creating hollow spaces
- [ ] Cave entrances where worm noise intersects surface
- [ ] Configurable: cave frequency, size, depth range, biome restrictions
- [ ] Cave biome with unique spawning rules (stalactites, crystals, mushrooms, ore deposits)

## 9.3 — Terrain Deformation
- [ ] `DeformTerrainAction` (implements `IWorldAction`): modify density values in a radius
- [ ] Dig: subtract density (remove terrain)
- [ ] Fill: add density (add terrain)
- [ ] Tool system: different dig shapes (sphere, cube, cylinder)
- [ ] Modified density values saved as chunk deltas (persistence)
- [ ] Mesh regeneration after deformation (localized, not full chunk rebuild)
- [ ] Deformation mesh rebuild runs async to avoid frame hitches

## 9.4 — Hybrid System Transition
- [ ] Define transition distance: beyond X meters, use cube-sphere quadtree LOD; within X, use marching cubes
- [ ] **Geomorphing**: smooth vertex interpolation between LOD levels to eliminate pop-in
- [ ] Marching cubes chunks generate from same noise seed as cube-sphere (terrain matches)
- [ ] Only chunks near player are active marching cubes; distant terrain stays as cube-sphere
- [ ] Mountains and major terrain features visible at all distances via quadtree LOD
