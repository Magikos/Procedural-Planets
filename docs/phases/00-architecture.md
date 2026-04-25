# Architecture Decisions

## Terrain: Hybrid System
- **Distant planet**: Cube-sphere with **quadtree LOD per face** (current system extended) — visible mountains/terrain from far away at low resolution
- **Local playable area**: **Marching cubes chunks** loaded around the player on the sphere surface — enables caves, terrain deformation, digging
- **Transition**: Geomorphing (smooth vertex interpolation between LOD levels) to minimize pop-in; pre-load chunks ahead of player movement direction
- **Spatial indexing**: Quadtree for cube-sphere face subdivision; octree for marching cubes volume chunks (efficient neighbor lookups, collision queries, object culling)

## Planet Scale
- **Target radius: ~5 km** (~314 km² surface area, roughly 8× Valheim's playable area)
- Feels flat locally at player scale, entire planet explorable over the course of the game
- Chunk size tuned for this scale (TBD during implementation, likely 32–64m per chunk)

### Scale Flexibility
- Radius is a configurable parameter that flows through all systems
- Chunk sizes, LOD distances, loading radii are all **relative to planet radius**, not hardcoded
- If we need to scale to 10km or 50km later, it's a settings change, not a rewrite

## Continental Generation
- **Continental noise layer** (very low frequency) defines large landmasses vs ocean basins
- Sea level cuts through this, creating distinct continents with coastlines
- Ocean areas between continents are deep enough for boat travel
- Feeds into biome system (deep ocean biome, coastal/beach biome at shorelines)
- Seed-deterministic: same seed always produces same continent layout

## Async & Multi-Core Architecture (Unity 6 Awaitable)
All heavy operations use Unity 6's `Awaitable` pattern and multi-threading where applicable:
- **Chunk loading/unloading**: `async Awaitable LoadChunkAsync(ChunkCoord coord)` — noise evaluation and mesh generation off main thread
- **Mesh generation**: Unity Jobs + Burst for vertex/triangle computation, awaited on main thread for mesh assignment
- **Save/Load**: `async Awaitable SaveWorldAsync()` / `LoadWorldAsync()` — binary serialization on background thread
- **Asset loading**: `Addressables.LoadAssetAsync` or `Resources.LoadAsync` wrapped in Awaitable
- **Terrain deformation**: density modification on background thread, mesh rebuild awaited
- **Poisson-disc sampling**: large point generation runs async, results applied on main thread
- **Scene transitions**: `await SceneManager.LoadSceneAsync()`

### Awaitable Pattern
```csharp
private async Awaitable GenerateChunkAsync(ChunkCoord coord, CancellationToken ct)
{
    // Heavy noise computation on background thread
    var densityData = await Awaitable.RunOnBackgroundThread(() =>
        ComputeDensityVolume(coord, _seed), ct);

    // Back to main thread for Unity API calls
    await Awaitable.MainThreadAsync();
    var mesh = BuildMeshFromDensity(densityData);
    chunk.AssignMesh(mesh);
}
```

### Threading Rules
- Unity API calls (mesh assignment, GameObject creation, material setting) **must** happen on main thread
- Noise evaluation, density computation, serialization, spatial queries can run on background threads
- Use `CancellationToken` for all async operations (cancel when chunk unloads, player disconnects, etc.)
- Jobs/Burst for tight loops (per-vertex noise, marching cubes triangle generation)

## Networking: Mirror (Planned)
- All world-modifying operations use command/action pattern (network-ready RPCs later)
- Authority/ownership patterns from the start
- Supports both host/client and dedicated server modes
- Not implemented until core systems are stable

## Persistence: Seed + Delta Saves
- Base world generated deterministically from seed
- Only player modifications saved (terrain deformation, buildings, placed objects, harvested resources)
- Chunk-based save format — only modified chunks are stored
- Harvested resources (chopped trees, mined rocks) saved as "removed entity" deltas

## Surface Interaction System
- Reusable render texture system around the player:
  - **Transient layer** (fades over time): grass displacement, single footsteps, blood splatter
  - **Persistent layer** (accumulates, saved with chunk): worn paths, permanent scorch marks
  - Persistent trampling: repeated walking on same path gradually blends terrain texture toward dirt/path material; saved as chunk delta
- Single `SurfaceInteractionMap` system, multiple visual consumers
- **Persistent layer storage**: only exists in chunks that have been modified (zero cost for untouched chunks)
  - Each chunk gets an optional `SurfaceModificationMap` (small texture, e.g., 64×64) created on first modification
  - Saved as part of chunk delta — scales with player activity, not planet size
  - Configurable decay rate (paths fade if not maintained, or stay permanent)

## Spawned Objects as Interactable Entities
- Every spawned tree, rock, bush, ore node is an **entity with state** (not just static mesh)
- Each entity has: health, loot table, respawn timer, biome association
- Harvesting (chop tree, mine rock) goes through `IWorldAction` for persistence + networking
- Destroyed entities saved in chunk delta; respawn after configurable time (or never, player choice)

## Loot Table System
- Each `WorldEntity` has a `LootTable` — weighted list of possible drops
- Drops can include: resources (wood, stone, ore), special items (mana orbs, rare materials), nothing
- Each entry has: item type, quantity range, drop chance (0–1), optional biome/condition modifiers
- Probability rolls use deterministic seed (chunk seed + entity index) so multiplayer clients agree on drops
- Example: Tree → Wood ×3 (100%), Mana Orb ×1 (15%), Rare Seed ×1 (2%)
- System extensible for future magic system, rare drops, biome-specific loot

## Pickup Entities
- Dropped items (resources, mana orbs) are `PickupEntity` instances in the world
- Physics-enabled: fall to ground, settle on terrain
- Mana orbs: float, glow, drift toward player within attraction radius
- Resource drops: sit on ground, manual pickup or auto-collect within radius
- Despawn timer if not collected (configurable, saved if player is nearby)

## Distant Object Rendering
- **LOD chain for all world objects**: full 3D mesh (near) → simplified mesh (medium) → billboard/impostor (far) → cluster billboard (very far, groups become single textured quad) → hidden (beyond horizon)
- Buildings follow the same LOD chain — player can see their base from far away
- Trees at extreme distance rendered as cluster billboards (a forest patch = one quad)
- System applies to all entity types: trees, rocks, buildings, structures
