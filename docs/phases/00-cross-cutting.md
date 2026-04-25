# Cross-Cutting Concerns — Logging, Events, Pooling, Coordinates

## Centralized Logging
- `ILogger` interface — all systems log through this, never `Debug.Log` directly
- Log levels: Trace, Debug, Info, Warning, Error
- Single place to control verbosity, filter by system, pipe to file
- Critical for multiplayer debugging later

```csharp
public interface ILogger
{
    void Log(LogLevel level, string system, string message);
    void LogException(string system, Exception ex);
}

// Usage:
_logger.Log(LogLevel.Info, "ChunkManager", $"Loaded chunk {coord}");
```

### Async Error Handling
- All `async Awaitable` methods wrap body in try/catch
- Unhandled exceptions go to `ILogger.LogException` — never silently swallowed
- Pattern:
```csharp
private async Awaitable DoWorkAsync(CancellationToken ct)
{
    try
    {
        // ... async work
    }
    catch (OperationCanceledException) { /* expected on shutdown/unload */ }
    catch (Exception ex)
    {
        _logger.LogException("SystemName", ex);
    }
}
```

---

## Event Bus (Adapted from local-only/EventBus)

We adopt the existing EventBus pattern with minor enhancements:

### What We Keep
- **Generic static class per event type**: `EventBus<TEvent>` — zero boxing, no dictionary lookups
- **WeakReference subscribers**: auto-cleanup of dead listeners, no memory leaks
- **Deferred processing**: events queue and fire in LateUpdate via `EventBusRuntime`
- **Struct events**: `IGameEvent` constrained to struct — zero allocation per raise
- **Attribute binding**: `[HandleEventBus(typeof(SomeEvent))]` for MonoBehaviour auto-discovery
- **Filter support**: `Func<TEvent, bool>` predicate on subscribe

### What We Add
- **Explicit Unlisten**: `EventBus<TEvent>.Unlisten(handler)` — important for systems that get destroyed/recreated
- **Async event support**: `EventBus<TEvent>.RaiseAsync(context)` for events that need awaitable responses
- Ensure `EventBusRuntime` plays nice with service architecture (no conflicts with DI)

### Event Naming Convention
- Events are structs named `{Subject}{Verb}Event`
- Examples: `EntityDeathEvent`, `ChunkLoadedEvent`, `BiomeChangedEvent`, `ItemPickedUpEvent`, `TimeOfDayChangedEvent`

### Key Events (Planned)
```
ChunkLoadedEvent          — chunk finished loading, entities can spawn
ChunkUnloadingEvent       — chunk about to unload, save state
EntityDeathEvent          — entity died, trigger loot drops
EntityHarvestedEvent      — entity was harvested, update chunk delta
ItemPickedUpEvent         — pickup collected, update inventory
BuildingPlacedEvent       — building piece placed, recalculate structural integrity
TerrainDeformedEvent      — terrain modified, rebuild chunk mesh
TimeOfDayChangedEvent     — time phase changed (dawn/day/dusk/night)
BiomeEnteredEvent         — player entered new biome, update ambient audio/effects
WeatherChangedEvent       — weather state changed, update particles/lighting
```

---

## Object Pooling

### Interface
```csharp
public interface IObjectPool<T> where T : class
{
    T Get();
    void Return(T instance);
    void Prewarm(int count);
    int CountActive { get; }
    int CountInactive { get; }
}
```

### Usage
- All entity spawning goes through pools (trees, rocks, pickups, particles)
- Grass chunk buffers are pooled
- Mesh objects for marching cubes chunks are pooled
- Pool per prefab type, managed by a `PoolManager` service
- Pools auto-expand but log warnings if growing beyond expected size

---

## Coordinate Systems & Converter

### Coordinate Spaces
| Space | Description | Used By |
|-------|-------------|---------|
| **Unit Sphere** | Normalized point on sphere (magnitude 1) | Noise evaluation, biome calculation |
| **World Space** | Unity world coordinates (scaled by radius + elevation) | Rendering, physics, Unity API |
| **Chunk Coord** | Integer address of a chunk on the sphere surface | Chunk management, save/load |
| **Chunk Local** | Position within a chunk (0 to chunkSize) | Mesh building, entity placement |
| **Lat/Long** | Latitude (-π/2 to π/2), Longitude (-π to π) | Biome temperature, continental noise |
| **Cube Face UV** | Which face (0-5) + UV position on that face | Quadtree LOD, texture mapping |

### CoordinateConverter Utility
```csharp
public static class CoordinateConverter
{
    // Unit Sphere ↔ World
    public static Vector3 UnitSphereToWorld(Vector3 pointOnUnitSphere, float elevation);
    public static Vector3 WorldToUnitSphere(Vector3 worldPoint);

    // Unit Sphere ↔ Lat/Long
    public static (float latitude, float longitude) UnitSphereToLatLong(Vector3 point);
    public static Vector3 LatLongToUnitSphere(float latitude, float longitude);

    // Unit Sphere ↔ Chunk Coord
    public static ChunkCoord UnitSphereToChunkCoord(Vector3 point, float chunkArcSize);
    public static Vector3 ChunkCoordToUnitSphere(ChunkCoord coord, float chunkArcSize);

    // Unit Sphere ↔ Cube Face UV
    public static (int face, Vector2 uv) UnitSphereToCubeFace(Vector3 point);
    public static Vector3 CubeFaceToUnitSphere(int face, Vector2 uv);

    // World ↔ Chunk
    public static ChunkCoord WorldToChunkCoord(Vector3 worldPoint, float planetRadius, float chunkArcSize);
    public static Vector3 ChunkLocalToWorld(ChunkCoord coord, Vector3 localPos, float planetRadius, float chunkArcSize);

    // Utility
    public static float ArcDistance(Vector3 a, Vector3 b, float radius);
    public static Vector3 SurfaceNormal(Vector3 worldPoint, Vector3 planetCenter);
    public static Vector3 ProjectToSurface(Vector3 worldPoint, float planetRadius);
}
```

All coordinate conversion logic lives here. No ad-hoc math scattered through the codebase.

---

## Configuration Separation

### Design-Time Settings (ScriptableObjects — tuned in Unity Inspector)
- `BiomeDefinition` — color gradient, spawn rules, temperature/moisture range
- `SpawnRule` — prefab, density, biome filter, slope constraints
- `LootTable` — weighted drop list
- `CraftingRecipe` — input/output items
- `BuildingPiece` — mesh, snap points, structural strength
- `ShapeSettings`, `ColorSettings` — existing planet config

### Runtime Settings (Save File — changes during gameplay)
- World seed
- Time of day, day count
- Player position, inventory, stats
- Chunk deltas (terrain modifications, harvested entities, buildings)
- Surface modification maps (worn paths)

### Debug Settings (Editor-only toggles, not saved)
- Show chunk borders
- Show biome overlay
- Disable LOD
- Force specific biome everywhere
- Show structural integrity colors
- Log async task timing
- Stored in `EditorPrefs` or a debug ScriptableObject excluded from builds

---

## Testing Strategy

### Unit Tests (Unity Test Framework — Edit Mode)
- **Noise determinism**: same seed → same output, always
- **Biome assignment**: temperature × moisture → correct biome type
- **Biome adjacency**: verify no invalid neighbors possible on the gradient
- **Coordinate conversions**: round-trip accuracy (UnitSphere → LatLong → UnitSphere ≈ original)
- **Loot table probability**: statistical validation over N rolls
- **Serialization round-trip**: save → load → identical state
- **MinMax tracking**: verify correct min/max after N values

### Integration Tests (Play Mode)
- Chunk load/unload cycle doesn't leak memory
- Entity spawn → harvest → loot drop → pickup flow
- Building place → structural integrity calculation → collapse if unsupported
- Save → quit → load → world state matches

### Performance Tests
- Chunk generation time (target: <16ms on background thread)
- Grass rendering frame time with 1M+ blades
- Marching cubes mesh rebuild after deformation

---

## Git Workflow
- Feature branch per phase: `phase3-foundation`, `phase4-biomes`, etc.
- Short commit messages: "Implemented chunk loading system.", "Fixed biome blending at poles."
- Merge to main when phase is stable and tested
- Main branch always in a working state
