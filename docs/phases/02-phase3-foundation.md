# Phase 3: Project Foundation & Architecture
*Goal: Set up the infrastructure that all future systems depend on*

## 3.1 — Command/Action Pattern (Network-Ready)
- [ ] Create `IWorldAction` interface with `Execute()`, `Undo()`, `Serialize()`, `Deserialize()`
- [ ] Create `WorldActionManager` that processes actions (later becomes network authority)
- [ ] All world-modifying operations (terrain, building, spawning, harvesting) go through this system
- [ ] Action history for undo support and network replay

## 3.2 — Chunk System Foundation
- [ ] Define `ChunkCoord` struct for addressing chunks on the sphere surface
- [ ] Create `ChunkManager` with async chunk loading: `async Awaitable LoadChunkAsync(ChunkCoord coord, CancellationToken ct)`
- [ ] Implement chunk loading radius and unloading hysteresis
- [ ] **Pre-load chunks ahead of player movement direction** to minimize pop-in
- [ ] Create `IChunkDataProvider` interface (current noise system implements this for base terrain)
- [ ] Design chunk data format that supports both base terrain and modifications (delta layer)
- [ ] Octree spatial index for marching cubes chunks (efficient neighbor/collision queries)
- [ ] Chunk generation runs noise evaluation on background thread via `Awaitable.RunOnBackgroundThread()`

## 3.3 — Save/Load Foundation
- [ ] Create `WorldSaveData` class: seed, player modifications per chunk, building data, metadata
- [ ] Create `ChunkSaveData`: terrain deltas, placed objects, harvested entities, surface modifications
- [ ] Implement binary serialization for chunk data (compact, fast)
- [ ] Create `SaveManager` with async operations: `async Awaitable SaveWorldAsync()`, `async Awaitable LoadWorldAsync()`
- [ ] File format: one master file + per-chunk delta files (only modified chunks saved)
- [ ] Serialization runs on background thread, main thread only for final state application

## 3.4 — Debug Camera Controller
- [ ] Create `FreeCameraController` for development: WASD + mouse look, shift to speed up
- [ ] Add ability to teleport to coordinates on the sphere surface
- [ ] Display debug info overlay: chunk coords, biome, elevation, FPS, async task count
- [ ] This replaces the need for a character controller during early development
