# Phase 13: LOD & Performance
*Goal: Smooth performance at planetary scale*

## 13.1 — Quadtree LOD for Distant Planet
- [ ] Replace fixed-resolution TerrainFace with quadtree subdivision per cube face
- [ ] Subdivide based on camera distance, max depth configurable
- [ ] **Geomorphing** between LOD levels (smooth vertex interpolation, no pop-in)
- [ ] Seamless stitching between LOD levels (T-junction fix)
- [ ] Distant mountains and terrain features always visible

## 13.2 — Chunk LOD for Marching Cubes
- [ ] Multiple resolution levels per chunk based on distance
- [ ] High-res chunks near player, low-res at distance
- [ ] Async mesh generation: Unity Jobs + Burst for noise and marching cubes, awaited via `Awaitable`
- [ ] Priority queue: generate closest chunks first
- [ ] Pre-load chunks in player's movement direction
- [ ] Use `CancellationToken` to abort generation for chunks that unload before completing

## 13.3 — Object LOD & Culling
- [ ] Distance-based LOD chain for trees/buildings: full mesh → simplified → billboard → cluster billboard → hidden
- [ ] GPU instancing for grass, small rocks, flowers
- [ ] Hybrid frustum + sphere-horizon culling (hide objects behind planet curve)
- [ ] Object pooling for spawned entities
- [ ] Billboard/impostor generation: render object to texture for far-distance display

## 13.4 — Compute Shader Optimization
- [ ] Move noise evaluation to compute shader
- [ ] Marching cubes on GPU (reference: Fluid-Planet `MarchingCubes.cs`)
- [ ] Grass generation fully on GPU
- [ ] Profile and optimize hot paths
