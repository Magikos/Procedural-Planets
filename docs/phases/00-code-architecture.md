# Code Architecture — Interfaces, Services & Patterns

## Core Principles
1. **Single Responsibility** — Each class does one thing. The planet generator orchestrates; it doesn't compute noise, assign biomes, or build meshes.
2. **Interface-Driven** — Every system is behind an interface. Swap implementations without touching consumers.
3. **Dependency Injection** — Services receive their dependencies via constructor or method injection. No singletons, no static god-classes.
4. **DRY** — Shared logic lives in utility classes or base classes. Noise evaluation, sphere math, coordinate conversion — written once, used everywhere.
5. **Async by Default** — Any operation that could take >1ms uses `async Awaitable` with `CancellationToken`. Chunk loading, mesh generation, save/load, spawning.
6. **Open/Closed** — Adding a new biome, noise filter, or spawn rule should require zero changes to existing code. Register it and the system picks it up.

---

## Service Architecture

### Planet Generation Pipeline
```
IPlanetGenerator              — orchestrates full planet generation
  ├── ITerrainProvider        — evaluates elevation at any point on unit sphere
  │     ├── IContinentalProvider   — low-frequency landmass vs ocean basin
  │     └── INoiseProvider         — layered noise evaluation (Simple, Rigid, etc.)
  ├── IBiomeProvider          — determines biome at any point
  │     ├── ITemperatureProvider   — latitude + noise → temperature value
  │     ├── IMoistureProvider      — noise → moisture value
  │     └── IBiomeRegistry         — temperature × moisture × elevation → BiomeDefinition
  ├── IColorProvider          — biome + elevation → color/texture mapping
  ├── IMeshBuilder            — builds mesh geometry from terrain data
  └── ISeedProvider           — deterministic seed propagation for all systems
```

### Chunk & World Management
```
IChunkManager                 — manages chunk lifecycle around focus point
  ├── IChunkDataProvider      — generates base chunk data (terrain, biome, spawn points)
  ├── IChunkDeltaStore        — reads/writes chunk modifications (terrain edits, harvested entities)
  └── IChunkMeshBuilder       — builds visual mesh from chunk data (async)

IWorldActionManager           — processes all world-modifying commands
  └── IWorldAction            — individual command (Execute, Undo, Serialize, Deserialize)

ISaveManager                  — async save/load of world state
  └── IChunkSerializer        — binary serialization of chunk deltas
```

### Entity & Spawning
```
IEntitySpawner                — places entities in chunks based on rules
  ├── ISpawnRuleEvaluator     — evaluates which entities spawn at a given point
  ├── ILootTableResolver      — resolves loot drops from entity death (deterministic)
  └── IPickupFactory          — creates pickup entities (resources, mana orbs)

IWorldEntity                  — base interface for all interactable world objects
  ├── IHarvestable            — can be damaged/harvested (trees, rocks, ore)
  ├── IBuildable              — can be placed/removed (building pieces)
  └── IPickupable             — can be collected (dropped items, mana orbs)
```

### Player Systems
```
IInventoryProvider            — abstracts inventory (limitless now, limited later)
IToolProvider                 — current equipped tool and its capabilities
ICraftingService              — recipe lookup, crafting execution
IBuildingService              — placement validation, structural integrity calculation
```

### Celestial
```
ICelestialManager             — manages sun, moons, time of day
  ├── ITimeProvider           — exposes TimeOfDay (0–1), day length, current phase
  ├── ISkyRenderer            — procedural sky based on sun angle
  └── IMoonProvider           — moon positions, phases, moonlight intensity
```

---

## Biome System — Temperature × Moisture Grid

Biomes exist on a continuous 2D gradient. Adjacent biomes on the planet surface are always
adjacent on the gradient because temperature and moisture change smoothly (noise-based).
Snow can NEVER be next to Desert — you'd cross through intermediate biomes first.

```
                    Dry ←————————————————→ Wet
         ┌───────────┬───────────┬───────────┐
   Hot   │  Desert   │  Savanna  │ Tropical  │
         ├───────────┼───────────┼───────────┤
   Warm  │  Scrub    │ Grassland │  Forest   │
         ├───────────┼───────────┼───────────┤
   Cool  │  Steppe   │  Taiga    │  Swamp    │
         ├───────────┼───────────┼───────────┤
   Cold  │  Tundra   │  Snow     │  Ice Bog  │
         └───────────┴───────────┴───────────┘

Elevation overrides (take priority regardless of temperature/moisture):
  - Ocean:    elevation < sea level
  - Beach:    elevation near sea level on coastline
  - Mountain: elevation > mountain threshold
  - Cave:     inside marching cubes hollow (determined by density, not surface)
```

### Adding a New Biome (e.g., Swamp)
1. Add `Swamp` to the `BiomeType` enum
2. Create a `SwampBiomeDefinition` ScriptableObject asset (color gradient, spawn rules, etc.)
3. Register it in the `BiomeMap` at the correct temperature × moisture cell (Cool + Wet)
4. **Zero code changes** to any existing system — the `IBiomeRegistry` picks it up automatically

### Biome Blending
- Temperature and moisture are continuous float values with noise perturbation
- At biome boundaries, the values are between two cells → blend between both biomes
- Blend width is configurable per biome pair
- Noise perturbation makes boundaries organic and wavy, never straight lines
- The shader receives blend weights and interpolates between biome textures/colors

---

## Interface Examples

### ITerrainProvider
```csharp
public interface ITerrainProvider
{
    void Initialize(ShapeSettings settings, int seed);
    float EvaluateElevation(Vector3 pointOnUnitSphere);
    float GetScaledElevation(float unscaledElevation);
    MinMax ElevationRange { get; }
}
```

### IBiomeProvider
```csharp
public interface IBiomeProvider
{
    void Initialize(ColorSettings settings, int seed);
    BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation);
}

public struct BiomeResult
{
    public BiomeType PrimaryBiome;
    public BiomeType SecondaryBiome;  // for blending at boundaries
    public float BlendWeight;          // 0 = fully primary, 1 = fully secondary
    public float Temperature;
    public float Moisture;
}
```

### IWorldAction
```csharp
public interface IWorldAction
{
    Awaitable ExecuteAsync(CancellationToken ct);
    Awaitable UndoAsync(CancellationToken ct);
    byte[] Serialize();
    void Deserialize(byte[] data);
    WorldActionType ActionType { get; }
}
```

### IChunkManager
```csharp
public interface IChunkManager
{
    Awaitable LoadChunksAroundAsync(Vector3 focusPoint, float loadRadius, CancellationToken ct);
    void UnloadDistantChunks(Vector3 focusPoint, float unloadRadius);
    TerrainChunk GetChunkAt(ChunkCoord coord);
    bool IsChunkLoaded(ChunkCoord coord);
    event Action<ChunkCoord> ChunkLoaded;
    event Action<ChunkCoord> ChunkUnloading;
}
```

### IWorldEntity
```csharp
public interface IWorldEntity
{
    int EntityId { get; }
    Vector3 Position { get; }
    float Health { get; }
    BiomeType Biome { get; }
    bool IsAlive { get; }
    void TakeDamage(float amount, DamageType type);
    event Action<IWorldEntity> OnDeath;
}

public interface IHarvestable : IWorldEntity
{
    LootTable LootTable { get; }
    float HarvestDifficulty { get; }  // minimum tool tier required
    void Harvest(IToolProvider tool);
}
```

### IInventoryProvider
```csharp
public interface IInventoryProvider
{
    bool TryAddItem(ItemDefinition item, int quantity);
    bool TryRemoveItem(ItemDefinition item, int quantity);
    int GetItemCount(ItemDefinition item);
    bool HasItems(ItemDefinition item, int quantity);
    IReadOnlyList<ItemStack> GetAllItems();
}
```

---

## Async Patterns Used Throughout

### Chunk Loading
```csharp
public async Awaitable LoadChunkAsync(ChunkCoord coord, CancellationToken ct)
{
    // Check for saved modifications
    var delta = await _deltaStore.LoadDeltaAsync(coord, ct);

    // Heavy computation on background thread
    var chunkData = await Awaitable.RunOnBackgroundThread(() =>
    {
        var data = _dataProvider.GenerateChunkData(coord);
        if (delta != null) data.ApplyDelta(delta);
        return data;
    }, ct);

    // Back to main thread for Unity API
    await Awaitable.MainThreadAsync();
    var chunk = _meshBuilder.BuildChunk(chunkData);
    _loadedChunks[coord] = chunk;
    ChunkLoaded?.Invoke(coord);
}
```

### Save/Load
```csharp
public async Awaitable SaveWorldAsync(CancellationToken ct)
{
    var modifiedChunks = _chunkManager.GetModifiedChunks();

    await Awaitable.RunOnBackgroundThread(() =>
    {
        foreach (var chunk in modifiedChunks)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = _serializer.Serialize(chunk.GetDelta());
            File.WriteAllBytes(GetChunkPath(chunk.Coord), bytes);
        }
    }, ct);
}
```

### Entity Spawning
```csharp
public async Awaitable SpawnEntitiesForChunkAsync(ChunkCoord coord, CancellationToken ct)
{
    // Compute spawn points on background thread
    var spawnPoints = await Awaitable.RunOnBackgroundThread(() =>
        _ruleEvaluator.EvaluateSpawnPoints(coord, _seed), ct);

    // Instantiate on main thread, spread across frames to avoid hitches
    await Awaitable.MainThreadAsync();
    foreach (var batch in spawnPoints.Batch(50))
    {
        ct.ThrowIfCancellationRequested();
        foreach (var point in batch)
            InstantiateEntity(point);
        await Awaitable.NextFrameAsync(ct);
    }
}
```

---

## Directory Structure (Planned)
```
Assets/Scripts/
├── Core/                          # Shared utilities, interfaces, base classes
│   ├── Interfaces/                # All interfaces (ITerrainProvider, IBiomeProvider, etc.)
│   ├── Data/                      # Shared data structures (ChunkCoord, BiomeResult, etc.)
│   ├── Services/                  # Service locator or DI container
│   └── Utilities/                 # Math helpers, extensions, noise base class
├── Planet/                        # Planet generation (existing, refactored)
│   ├── Terrain/                   # ITerrainProvider implementations
│   ├── Biomes/                    # IBiomeProvider, BiomeDefinitions, BiomeMap
│   ├── Color/                     # IColorProvider implementations
│   ├── Mesh/                      # IMeshBuilder implementations (TerrainFace, MarchingCubes)
│   ├── Noise/                     # INoiseProvider, noise filters (Simple, Rigid, etc.)
│   └── Chunks/                    # IChunkManager, chunk data, delta storage
├── Celestial/                     # Sun, moons, sky, time of day
├── Entities/                      # WorldEntity, Harvestable, Pickup, spawning
├── Player/                        # Character controller, camera, inventory
├── Building/                      # Building system, structural integrity
├── Crafting/                      # Recipes, crafting stations
├── Persistence/                   # Save/load, serialization
├── Interaction/                   # Surface interaction map, trample system
└── Networking/                    # Mirror integration (future)
```

---

## Key DRY Patterns

### Noise Evaluation (Used by Terrain, Biomes, Moisture, Caves, Grass Placement)
```csharp
public interface INoiseProvider
{
    float Evaluate(Vector3 point);
}

// One implementation, configured differently per use case
public class LayeredNoiseProvider : INoiseProvider
{
    private readonly INoiseFilter[] _filters;
    private readonly NoiseSettings[] _settings;
    // ... same noise evaluation logic, never duplicated
}
```

### Sphere Math Utilities (Used Everywhere)
```csharp
public static class SphereMath
{
    public static float Latitude(Vector3 pointOnUnitSphere);
    public static float Longitude(Vector3 pointOnUnitSphere);
    public static Vector3 PointFromLatLong(float lat, float lon);
    public static Vector3 SurfaceNormal(Vector3 pointOnSphere, Vector3 planetCenter);
    public static float ArcDistance(Vector3 a, Vector3 b, float radius);
    public static ChunkCoord PointToChunkCoord(Vector3 point, float chunkSize);
}
```

### Deterministic Seed Propagation
```csharp
public interface ISeedProvider
{
    int WorldSeed { get; }
    int GetSeedForSystem(string systemName);      // Hash(WorldSeed + systemName)
    int GetSeedForChunk(ChunkCoord coord);         // Hash(WorldSeed + coord)
    int GetSeedForEntity(ChunkCoord coord, int entityIndex); // For loot rolls
}
```
