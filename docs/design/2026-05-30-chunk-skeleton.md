# Chunk skeleton design — Phase A

**Date:** 2026-05-30
**Status:** **PIVOTED to pre-cache 2026-05-30.** See the "Pre-cache pivot" section at the bottom — the dynamic-subdivision design above is the original plan; the pivot section is what's actually being built.
**Source-of-truth this implements:** [docs/research/2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md) — "Locked-in Design (post-feedback 2026-05-30)" section.

---

## 1. Purpose and scope

Deliver the **chunked quadtree LOD planet** as a clean addition to the existing planet code, without ripping out the working per-face path.

**In scope (this phase):**

- New `IPlanetSurfaceProvider` abstraction with two implementations: existing per-face path (kept) and new chunked path.
- `PlanetChunk` data class + lifecycle state machine.
- Hash-bit quadtree encoding with cross-face neighbor lookup.
- Half-chunk face-seam overlap math.
- Per-chunk Burst mesh job + edge-fan templates + border-vertex normal smoothing.
- Awaitable generation loop (no coroutines, no raw `Thread`).
- `PlanetResolution` enum threaded through `Planet.GeneratePlanetAsync`.

**Out of scope (later phases):**

- Biome textures (Phase B) — chunks expose hooks but biome map population happens later.
- Surface state stack (Phase C) — chunks expose hooks but textures aren't allocated yet.
- Grass renderer (Phase D), modifications (Phase E), snow (Phase F).

**Non-goals:**

- Replacing `ShapeGenerator`, `ColorGenerator`, `IBiomeProvider`, or `Planet.cs` public surface (`IPlanet`, `IPlanetSurfaceSampler`).
- Adding any new dependencies (no Burst Collections v2 features, no com.unity.collections extras beyond what's already imported).

---

## 2. Existing code we build on

Read straight from current `master`:

| File                                                                                                                     | Role                                                                                                                                                                     | How chunked path uses it                                                                                                                                                  |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Assets/Scripts/Planet/Planet.cs](../../Assets/Scripts/Planet/Planet.cs)                                                 | MonoBehaviour facade; implements `IPlanet`, `IPlanetSurfaceSampler`, `ILateInitialize`, `IProgressReporter`. Calls `GeneratePlanetAsync(ct)`.                            | Acquire `IPlanetSurfaceProvider` based on `PlanetResolution`, delegate mesh gen. Existing public surface unchanged.                                                       |
| [Assets/Scripts/Planet/TerrainFace.cs](../../Assets/Scripts/Planet/TerrainFace.cs)                                       | Per-face Burst mesh job + state + color compute.                                                                                                                         | Per-face path uses as today. Chunked path wraps it as the `Low` resolution provider; **does not modify it**.                                                              |
| [Assets/Scripts/Planet/ShapeGenerator.cs](../../Assets/Scripts/Planet/ShapeGenerator.cs)                                 | Wraps noise filters + radius + elevation tracking.                                                                                                                       | Reused unchanged by both providers; chunked path calls `ShapeGenerator.BuildNoiseFilterData` once per `GenerateMeshAsync` and shares the `NativeArray` across chunk jobs. |
| [Assets/Scripts/Core/Interfaces/IBiomeProvider.cs](../../Assets/Scripts/Core/Interfaces/IBiomeProvider.cs)               | `EvaluateBiome`, `GetBiomeColor`, `GetBiomeColorAndData`.                                                                                                                | Reused unchanged. Phase B will extend with a chunk-map sampler; this phase only feeds the existing color path.                                                            |
| [Assets/Scripts/Core/Utilities/CoordinateConverter.cs](../../Assets/Scripts/Core/Utilities/CoordinateConverter.cs)       | `UnitSphereToCubeFace`, `CubeFaceToUnitSphere`, `UnitSphereToChunkCoord`, `ChunkCoordToUnitSphere`. Face indexing: **0=up, 1=down, 2=left, 3=right, 4=forward, 5=back.** | Reused unchanged. Cross-face neighbor lookup builds the `CubeFaceTopology` table on top of this convention.                                                               |
| [Assets/Scripts/Core/Data/ChunkCoord.cs](../../Assets/Scripts/Core/Data/ChunkCoord.cs)                                   | `(Face, X, Y)` struct, already used by `SphericalWeatherGrid`.                                                                                                           | Reused as the **coarse grid coord** for top-level chunk seeding; the hash-bit ID handles deeper levels.                                                                   |
| [Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs](../../Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs) | `TryGetSurfaceRadius(worldUnitDirection, out surfaceRadius)`.                                                                                                            | Chunked path implements with bilinear sample from the visible leaf chunk that owns the direction.                                                                         |
| [Assets/Scripts/Core/Events/PlanetGeneratedEvent.cs](../../Assets/Scripts/Core/Events/PlanetGeneratedEvent.cs)           | Raised when gen completes.                                                                                                                                               | Both providers raise as today, after full first-frame chunk pass settles.                                                                                                 |

**What we deliberately don't touch in Phase A:**

- Water mesh building (`WaterMeshBuilder` reads from `_terrainFaces`). The chunked path needs to expose face-mesh-equivalent data for water; covered in §10.
- `ColorGenerator` — unchanged.
- Atmosphere, clouds, weather — orthogonal.

---

## 3. The `IPlanetSurfaceProvider` abstraction

The single new seam between `Planet` and the two surface implementations.

```csharp
public interface IPlanetSurfaceProvider
{
    // Schedules + awaits the initial surface generation. Reports progress through `progress`.
    // Throws OperationCanceledException on cancel.
    Awaitable GenerateAsync(IPlanet planet, IProgressHandle progress, CancellationToken ct);

    // Per-frame update (chunked path only — per-face path is a no-op). Called from Planet.Update
    // when IsGenerating == false. Drives quadtree split/merge based on observer position.
    void Tick(Vector3 observerWorldPosition);

    // Surface radius sampler — implements the contract IPlanetSurfaceSampler exposes on Planet.
    bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius);

    // Visible meshes — needed by WaterMeshBuilder and any system that wants to walk per-face vertices.
    // For per-face: returns 6 meshes, one per face. For chunked: returns currently-visible leaf meshes.
    IReadOnlyList<IPlanetSurfaceMesh> GetVisibleMeshes();

    // Surface state hooks — empty in Phase A, wired in Phase C.
    void RegisterStateConsumer(IChunkSurfaceStateConsumer consumer);
    void UnregisterStateConsumer(IChunkSurfaceStateConsumer consumer);

    // Lifecycle.
    void Dispose();
}

public interface IPlanetSurfaceMesh
{
    Mesh Mesh { get; }
    int FaceIndex { get; }                // 0-5
    uint ChunkHash { get; }               // 1 for full-face mesh (per-face provider), real hash for chunked leaves
    Vector3 ChunkCenterUnitSphere { get; } // for distance/visibility queries
    float ChunkRadiusOnUnitSphere { get; } // angular radius
    Bounds WorldBounds { get; }
}
```

**Provider selection** lives in `PlanetSettings`:

```csharp
public enum PlanetResolution
{
    Low,    // existing per-face path
    High    // new chunked path
}

// in PlanetSettings:
public PlanetResolution Resolution = PlanetResolution.High;
```

`Planet.Initialize` does:

```csharp
_surfaceProvider = _planetSettings.Resolution switch
{
    PlanetResolution.Low  => new PerFaceSurfaceProvider(_shapeGenerator, _planetSettings),
    PlanetResolution.High => new ChunkedSurfaceProvider(_shapeGenerator, _planetSettings),
    _ => throw new ArgumentOutOfRangeException()
};
```

`Planet.GenerateMeshAsync` collapses to `await _surfaceProvider.GenerateAsync(this, _progressHandle, ct)`.
`Planet.Update` adds `if (!_isGenerating) _surfaceProvider.Tick(observerPos)`.
`Planet.TryGetSurfaceRadius` delegates to `_surfaceProvider.TryGetSurfaceRadius`.

`PerFaceSurfaceProvider` is a thin wrapper: its `GenerateAsync` runs the existing per-face job code lifted verbatim from `Planet.GenerateMeshAsync`. `Tick` no-ops. `GetVisibleMeshes` returns the six face meshes.

---

## 4. `PlanetChunk` — the data class

```csharp
public sealed class PlanetChunk
{
    // Identity ----------------------------------------------------------------
    public readonly uint HashValue;        // 1 = face root; child = (parent << 2) | quadrant
    public readonly int DetailLevel;       // tree depth (face root = 0, max = 15)
    public readonly int FaceIndex;         // 0-5
    public readonly byte Corner;           // 0..3, which corner of parent this child is

    // Geometry on the unit cube (face-local) ----------------------------------
    public readonly float2 UvCenter;       // chunk center in face UV space, [0,1]²
    public readonly float UvHalfExtent;    // half-extent in face UV space (root = 0.5)
    public readonly float3 UnitSphereCenter; // for fast distance queries

    // Tree links --------------------------------------------------------------
    public PlanetChunk Parent;             // null for face root
    public PlanetChunk[] Children;         // length 0 (leaf) or 4 (subdivided)

    // Mesh data ---------------------------------------------------------------
    public Mesh Mesh;                      // owned; never null for Active leaves
    public TerrainFaceJobState? PendingJob; // not null while Subdividing
    public NeighborLodMask EdgeNeighbors;  // 4 bits, see §6

    // Lifecycle ---------------------------------------------------------------
    public ChunkLifecycle State;
    public uint Generation;                // bumped on every gen complete; used to detect stale callbacks

    // Phase B/C hooks (not allocated this phase) ------------------------------
    public ChunkBiomeMap BiomeMap;         // 64×64 RGBA8, populated Phase B
    public ChunkSurfaceState SurfaceState; // 4-texture stack, populated Phase C
    public ChunkGrassHandle GrassHandle;   // registration with grass system, Phase D

    // Memory ------------------------------------------------------------------
    public Vector3[] CpuVertices;          // kept for water mesh, sample radius queries
    public float[] VertexRadii;            // bilinear sample target

    // ... constructors + factory methods omitted from skeleton ...
}

public enum ChunkLifecycle
{
    Pending,         // created, not yet generated
    Generating,      // job in flight
    Active,          // leaf with mesh, no children
    ActiveWithChildren, // children fully Active; this chunk's mesh is hidden
    Subdividing,     // children Generating; this chunk's mesh still shown
    Merging,         // children fading out; this chunk re-shown
    Unloading        // mesh being released
}

[Flags]
public enum NeighborLodMask : byte
{
    None  = 0,
    East  = 1 << 0,
    West  = 1 << 1,
    North = 1 << 2,
    South = 1 << 3
}
```

**Why a class, not a struct:** chunks form a tree with mutable state and per-chunk Unity objects (`Mesh`). Allocation cost is dwarfed by mesh upload. Class allows clean equality by reference + nullable.

**Mesh ownership:** each `PlanetChunk` owns one `Mesh`. **Not** one `GameObject + MeshFilter` per chunk — that's too many draw calls. Instead, per-face `MeshRenderer + MeshFilter` swaps in a **combined mesh** built from all visible leaves on that face every time the leaf set changes (§7).

---

## 5. Hash-bit quadtree encoding

Each chunk has a `uint HashValue`:

```
bit layout, root → leaf:
  31           ...           2 1 0
  ┌───────────────────────────┬─┐
  │ ancestor quadrants × N    │1│   <- leading 1 sentinel preserves leading zeros
  └───────────────────────────┴─┘
```

- Face root = `1` (binary `…001`).
- Child append: `child.HashValue = (parent.HashValue << 2) | quadrant`.
- Quadrants:
  - `0` = NW
  - `1` = NE
  - `2` = SE
  - `3` = SW
- `DetailLevel` = (number of leading bits before the sentinel) / 2 = `(BitOperations.Log2(HashValue) >> 1)`.

**Max depth:** 15 levels. `1 + 2·15 = 31` bits, safely inside 32. Matches `detailLevelDistances.Length == 16` on the reference.

**Face-root disambiguation:** the bare `HashValue == 1` is identical across all six faces. The owning face is stored in `FaceIndex` and is **not** encoded in the hash. This is fine for in-memory work (all neighbor walks happen within a `TerrainQuadtree` instance that owns one face); cross-face lookup goes through `CubeFaceTopology` (§6) and uses face-relative hashes.

**Persistence keys** (for Phase C save layer): the full key is `(FaceIndex, HashValue)`. The persistence provider concatenates them.

---

## 6. Cross-face neighbor lookup

Two halves: **same-face** (lifted from LOD-Planets bitmask trick) and **cross-face** (the new bit that fixes the LOD-Planets TODO).

### 6.1 `CubeFaceTopology`

Static immutable table; built once at startup. For each face × edge, records:

```csharp
public readonly struct CubeFaceEdgeNeighbor
{
    public readonly int NeighborFace;
    public readonly CubeEdge NeighborEdge;        // which of N/S/E/W on the neighbor is shared
    public readonly bool FlipUAxis;               // axis remap when projecting UV from us to neighbor
    public readonly bool FlipVAxis;
    public readonly bool SwapUV;
}

public enum CubeEdge { East, West, North, South }
```

`CubeFaceTopology.GetNeighbor(int face, CubeEdge edge) → CubeFaceEdgeNeighbor`.

Six faces × four edges = 24 entries. Hand-derived from the existing `CoordinateConverter` face convention (0=up, 1=down, 2=left, 3=right, 4=forward, 5=back) and asserted with a startup self-test that:

- every neighbor's neighbor-edge points back at us,
- UV remap is involutive.

The derivation is tedious but mechanical; the design doc commits to producing it. Full 24-entry table will live in `CubeFaceTopology.cs` with a comment showing how each row was derived.

### 6.2 Same-face neighbor lookup

Direct port of the LOD-Planets bitmask trick, in pseudocode:

```
QueryNeighborLod(chunk, direction):
    hash = chunk.HashValue
    mask = 0
    count = 0
    while count < chunk.DetailLevel * 2:
        count += 2
        quadrant = hash & 3
        mask <<= 2
        mask |= (direction == North || direction == South) ? 0b11 : 0b01

        if quadrant_is_on_query_side(quadrant, direction):
            hash >>= 2
            continue
        else:
            break

    targetHash = chunk.HashValue ^ mask
    return tree.FindChunkLod(targetHash, chunk.DetailLevel)
```

`FindChunkLod` walks down from the face root by reading 2-bit quadrant indices off the target hash; returns the detail level of the first chunk that matches (or doesn't subdivide further).

### 6.3 Cross-face neighbor lookup

```
QueryNeighborLod(chunk, direction):
    if chunk_edge_is_face_border(chunk, direction):
        neighbor = CubeFaceTopology.GetNeighbor(chunk.FaceIndex, direction)
        mirroredHash = MirrorHashAcrossFaceEdge(chunk.HashValue, chunk.DetailLevel, neighbor)
        return neighborFaceTree.FindChunkLod(mirroredHash, chunk.DetailLevel)
    else:
        // same-face path from §6.2
        ...
```

`MirrorHashAcrossFaceEdge` flips the quadrant bits along the shared edge per the topology entry's `FlipU/FlipV/SwapUV` flags. **This is the trickiest piece** of the cross-face support; design commits to producing a closed-form bit transformation table:

For each `CubeFaceEdgeNeighbor`, given a child quadrant on our side touching this edge, return the corresponding quadrant on the neighbor's side. The 24×2 lookup table will be expressed as a `static readonly byte[24, 2] QuadrantMirror`.

If no neighbor chunk exists at the queried hash (because the neighbor face is at a coarser LOD), `FindChunkLod` returns the highest-LOD ancestor that contains the queried position. That ancestor's level is the "neighbor LOD," and the calling chunk marks that edge as needing an edge fan.

---

## 7. Per-face combined mesh

We keep one `MeshFilter + MeshRenderer` per cube face (six in total, like today). What changes is the contents of that mesh.

**Today:** one Burst job writes ~`Resolution²` verts into the face mesh.
**Chunked:** every active leaf chunk on a face contributes its own per-chunk vertex/triangle data; on a leaf-set change, the face's `CombinedMeshBuilder` walks the visible leaves and concatenates them.

```csharp
sealed class CombinedFaceMesh
{
    readonly Mesh _mesh;
    readonly List<PlanetChunk> _currentLeaves = new();
    bool _dirty;

    public void MarkDirty() => _dirty = true;
    public void MarkLeavesChanged(IReadOnlyList<PlanetChunk> leaves) { _currentLeaves.Clear(); _currentLeaves.AddRange(leaves); _dirty = true; }

    public void RebuildIfDirty()
    {
        if (!_dirty) return;
        // Concatenate per-chunk vertex/triangle/normal/color arrays into pooled buffers,
        // then mesh.SetVertices / SetTriangles / SetNormals / SetColors.
        // Triangles offset per chunk.
        _dirty = false;
    }
}
```

**Rebuild cost:** dominated by `Mesh.SetVertices`/`SetTriangles` uploads. Triggered only on leaf-set change, not per frame. With ~256 active chunks × 65² verts = ~1M verts/face worst case — comfortably inside `IndexFormat.UInt32`.

**Avoiding GC:** vertex/triangle/normal/color list buffers pooled per face; cleared and refilled in-place, not re-allocated.

---

## 8. Half-chunk face-seam overlap

Per Bryan's Q5: no visible chunk lines at cube-face boundaries. Implementation strategy:

### 8.1 Geometric overlap

Each face's quadtree covers UV ∈ `[-Overlap, 1 + Overlap]²` instead of the strict `[0, 1]²`, where `Overlap = 0.5 / ChunksPerFaceAtMaxLod` ≈ half a chunk's UV extent at max LOD. Chunks whose UV center is outside `[0, 1]²` are **ghost chunks** — they have geometry but are owned by the neighboring face.

In practice:

- Root chunk UV center stays at `(0.5, 0.5)` with extent `0.5`.
- Subdivision in the overlap zone is permitted **only as deep as the corresponding chunk on the neighbor face goes**, queried via §6.3.
- A ghost chunk's mesh is included in the **neighbor face's** combined mesh, not its origin face's. Each face's combined mesh therefore renders its native chunks + half-chunk strip of ghost chunks owned by all 4 adjacent faces.

This handles the visual seam: vertex positions blend smoothly because the ghost chunk and its true counterpart on the neighbor face evaluate elevation from the same `ShapeGenerator` at the same unit-sphere points, producing identical world positions.

### 8.2 Density blending (Phase D-relevant, design here for completeness)

For grass, biome state, and surface state textures, **values in the overlap zone are blended** between the two owning faces. In the half-chunk-wide seam strip:

```
blend_weight = smoothstep(0, overlap_extent, distance_from_face_edge)
```

Both faces sample their state at the same world position; their values are linearly interpolated by `blend_weight`. Result: no visible discontinuity in grass density, biome color, snow depth, etc. across face seams.

Phase C will detail the seam-blending compute pass; Phase A only commits to _enabling_ the overlap geometrically.

### 8.3 Why this is acceptable cost

- Extra geometry: each face renders ~`(1 + 2·Overlap)²` of the area it would render strictly = ~1.05× worst case. Negligible.
- Extra subdivision: ghost chunks subdivide only as deep as their neighbor, so subdivision work doesn't double.
- No texture-bandwidth cost in Phase A — that's a Phase C concern.

---

## 9. Lifecycle state machine

```
        ┌──────────┐
        │ Pending  │  (created by parent.Subdivide(), no mesh yet)
        └────┬─────┘
             │ schedule mesh job
             ▼
        ┌──────────┐
        │Generating│  (Burst job in flight)
        └────┬─────┘
             │ job complete + mesh uploaded
             ▼
        ┌──────────┐
        │  Active  │◄────────────┐  (leaf, mesh shown in face combined mesh)
        └────┬─────┘             │
             │ subdivide trigger │ merge trigger
             ▼                   │
       ┌─────────────┐           │
       │ Subdividing │           │
       │ children    │           │
       │ Generating  │           │
       └────┬────────┘           │
            │ all 4 children Active
            ▼                   │
   ┌────────────────────┐       │
   │ ActiveWithChildren │       │
   └────────┬───────────┘       │
            │ merge trigger     │
            ▼                   │
       ┌─────────┐              │
       │ Merging │──────────────┘
       │ (children Unloading)
       └─────────┘
            │ all children Unloaded
            ▼
       ┌─────────┐
       │Unloading│  (mesh disposed; chunk released)
       └─────────┘
```

**Triggers:**

- **Subdivide:** observer enters the chunk's `detailLevelDistances[DetailLevel]` threshold (sqr-distance test against world bounds).
- **Merge:** observer leaves a hysteresis band wider than the subdivide threshold (e.g. 1.2× threshold) so that crossing back and forth doesn't thrash.
- **Stale-callback guard:** every state transition checks `chunk.Generation == capturedGeneration`. If a chunk's parent merged it away while its job was in flight, the job's completion handler exits without uploading.

**No coroutines, no `Thread`, no `ActionQueue`:**

- Quadtree traversal happens synchronously inside `IPlanetSurfaceProvider.Tick`.
- Mesh jobs scheduled via Burst `IJobParallelFor`, polled with `await Awaitable.NextFrameAsync(ct)` until `Handle.IsCompleted`.
- Mesh upload (`Mesh.SetVertices` etc.) happens on the main thread inside the same Awaitable that polled completion.
- Pattern lifted from existing `Planet.GenerateMeshAsync` — proven to work.

---

## 10. Generation flow (initial + steady-state)

### 10.1 Initial generation (`GenerateAsync`)

```
GenerateAsync(planet, progress, ct):
    progress.Report(0, "Building quadtree seeds")
    foreach face in 6 faces:
        create face root chunk (DetailLevel = 0, HashValue = 1)
    progress.Report(0.05, "Generating root chunks")

    // Schedule mesh jobs for all 6 roots in parallel.
    var filters = shapeGenerator.BuildNoiseFilterData(Allocator.Persistent)
    var jobs = []
    foreach root in faceRoots:
        jobs.add(ScheduleChunkMeshJob(root, filters))
    var combined = JobHandle.CombineDependencies(jobs)

    while !combined.IsCompleted:
        if ct.IsCancellationRequested: cleanup + throw
        await Awaitable.NextFrameAsync()
    combined.Complete()

    foreach root in faceRoots:
        CompleteChunkMeshJob(root)
        root.State = Active
        faceCombinedMesh[root.FaceIndex].MarkLeavesChanged([root])
        faceCombinedMesh[root.FaceIndex].RebuildIfDirty()

    filters.Dispose()
    progress.Report(1, "Planet ready")
```

After `GenerateAsync` returns, `Planet.Update` starts calling `provider.Tick(observerPos)` every frame.

### 10.2 Steady-state (`Tick`)

```
Tick(observerWorldPosition):
    if isGenerating: return            // initial gen still running
    foreach face in 6 faces:
        TraverseAndSchedule(face.Root, observerWorldPosition)

    // Drain completed jobs (non-blocking):
    foreach inflight in _pendingJobs.ToArray():
        if inflight.Handle.IsCompleted:
            CompleteAndPromote(inflight)
            _pendingJobs.Remove(inflight)
            facesDirty.add(inflight.chunk.FaceIndex)

    foreach faceIdx in facesDirty:
        faceCombinedMesh[faceIdx].MarkLeavesChanged(GatherVisibleLeaves(faceIdx))
        faceCombinedMesh[faceIdx].RebuildIfDirty()
```

`TraverseAndSchedule(chunk, obs)`:

- If `chunk.State == Active` and `ShouldSubdivide(chunk, obs)`: spawn 4 children, mark `Subdividing`, schedule jobs.
- If `chunk.State == ActiveWithChildren` and `ShouldMerge(chunk, obs)`: mark `Merging`, queue children for unload.
- If subdivided: recurse into children.
- Otherwise: no-op.

**Job throttling:** `_pendingJobs` capped at e.g. 8 concurrent. New subdivisions queue if cap hit. Prevents frame stalls when the observer warps long distances.

### 10.3 Cancellation

`ct` cancellation during initial gen: in-flight jobs completed (necessary to safely Dispose), `NativeArray`s disposed, exception thrown to caller. Same pattern as existing `Planet.GenerateMeshAsync`.

`ct` cancellation during steady-state: provider disposed, all in-flight jobs completed + disposed, all chunk meshes destroyed.

---

## 11. Per-chunk mesh job (Burst)

Mirrors `TerrainFaceMeshJob` but parameterized on chunk UV extent and edge-fan template:

```csharp
[BurstCompile]
public struct PlanetChunkMeshJob : IJobParallelFor
{
    public int Resolution;                 // 65
    public float3 FaceLocalUp;
    public float3 FaceAxisA;
    public float3 FaceAxisB;
    public float2 UvOrigin;                // chunk's bottom-left UV on the face
    public float UvExtent;                 // chunk's UV size (= 2 * UvHalfExtent)
    public float PlanetRadius;
    public byte EdgeFanMask;               // 4-bit ESNW lower-LOD flags

    [ReadOnly] public NativeArray<NoiseFilterData> Filters;

    [WriteOnly] public NativeArray<float3> Vertices;
    [WriteOnly] public NativeArray<float3> UnitSpherePoints;
    [WriteOnly] public NativeArray<float> Elevations;
    [WriteOnly] public NativeArray<float3> BorderVertices;  // for normal smoothing across chunk seams
    [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Triangles;

    public void Execute(int index)
    {
        // 1) Map (x, y) → face UV → cube point → unit sphere → elevation → world position.
        //    Reuses ShapeGenerator's filters via NoiseFilterEvaluator (existing code).
        // 2) If on an edge that has EdgeFanMask bit set, snap to the coarser neighbor's vertex spacing
        //    by collapsing every other vertex along that edge to its neighbor on the same edge.
        //    (Achieved by writing the snapped position to both the regular vertex slot AND treating
        //    the in-between vertex as a degenerate that overlaps its neighbor — no triangle change needed.)
        // 3) Write triangles using a precomputed template for the EdgeFanMask value (16 templates).
        // 4) Border vertices (one ring beyond UV bounds) written to BorderVertices for normal smoothing.
    }
}
```

**Edge-fan templates:** 16 precomputed `int[]` triangle arrays (one per `EdgeFanMask` combination), built statically at startup. Same approach as LOD-Planets' `Presets.quadTemplateTriangles` but generated programmatically (we don't ship a 16-asset preset bundle).

**Normal smoothing across seams:** border vertices extend one cell width beyond the chunk UV in all 4 directions. Per-vertex normals are averaged using triangles that include the border ring. Border vertices are **never** included in the final mesh — only in normal calculation. (Matches LOD-Planets pattern.)

---

## 12. `TryGetSurfaceRadius` for chunked path

Existing `Planet.TryGetSurfaceRadius` maps direction → `(face, uv)` → bilinear sample of face's `_vertexRadii[]`. Chunked path:

```
TryGetSurfaceRadius(worldUnitDir, out radius):
    localDir = transform.InverseTransformDirection(worldUnitDir)
    (face, uv) = CoordinateConverter.UnitSphereToCubeFace(localDir)
    chunk = face.Quadtree.FindLeafContaining(uv)
    if chunk == null: return false
    return chunk.TrySampleRadiusBilinear(uv, out radius)
```

`FindLeafContaining(uv)` walks the quadtree from face root, picking the child whose UV bounds contain the query, until a leaf is reached. O(log₂(detail level)) = up to ~15 hops.

`PlanetChunk.TrySampleRadiusBilinear` is a chunk-local version of the existing `TerrainFace.TrySampleSurfaceRadius` — same math, smaller grid.

---

## 13. Water mesh integration

`WaterMeshBuilder.Compute(terrainFaces, ...)` currently consumes `TerrainFace[]` directly. Phase A keeps this working:

- `IPlanetSurfaceProvider` exposes `GetVisibleMeshes()` returning a flat list of `IPlanetSurfaceMesh`.
- A small adapter (`PlanetSurfaceMeshSetAdapter`) collects all leaf meshes for a face and exposes them as a single face-equivalent vertex stream.
- Or alternatively: chunked path also writes per-face combined CPU vertex arrays (already required for `TryGetSurfaceRadius`'s bilinear sample), and `WaterMeshBuilder` consumes those as if from a single-face mesh.

**Recommended approach** (committed in this design): chunked path's `CombinedFaceMesh` keeps a CPU shadow copy of vertices + per-face elevation array. `PerFaceWaterAdapter` exposes these to `WaterMeshBuilder` with the same shape `TerrainFace` does today. No `WaterMeshBuilder` changes needed.

**Open question for review:** should we instead refactor `WaterMeshBuilder` to consume `IPlanetSurfaceMesh` directly? Cleaner long-term but expands Phase A scope. **Default: keep the adapter for Phase A, refactor in a later cleanup phase.**

---

## 14. Memory budget

For a planet with:

- 6 faces
- Up to 256 active leaf chunks total across all faces
- 65×65 vertex chunks
- IndexFormat.UInt32

| Item                                   | Per chunk           | Total (256 chunks) |
| -------------------------------------- | ------------------- | ------------------ |
| Mesh vertices (float3)                 | 65² × 12 B = ~50 KB | 12.8 MB            |
| Triangles (uint, ~2× verts)            | ~50 KB              | 12.8 MB            |
| Normals (float3)                       | ~50 KB              | 12.8 MB            |
| CPU vertex shadow                      | 65² × 12 B = ~50 KB | 12.8 MB            |
| Vertex radii (float)                   | 65² × 4 B = ~17 KB  | 4.4 MB             |
| Border vertices (transient during job) | ~3 KB               | 0.8 MB             |
| **Phase A chunk-mesh total**           | **~170 KB**         | **~56 MB**         |

Phase B–F additions (state textures, biome map, grass force map): an additional ~50 KB per chunk × 256 = ~12 MB. **Phase A delivers in ~56 MB**, well within budget. Worst-case 1024-chunk planet still under 250 MB.

---

## 15. Public API additions

New files:

| File                                                       | Contents                                                                                    |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `Assets/Scripts/Core/Interfaces/IPlanetSurfaceProvider.cs` | `IPlanetSurfaceProvider`, `IPlanetSurfaceMesh`, `IChunkSurfaceStateConsumer` (empty marker) |
| `Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs`  | Wraps existing per-face path                                                                |
| `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`  | New chunked path orchestrator                                                               |
| `Assets/Scripts/Planet/Surface/PlanetChunk.cs`             | Chunk data class + lifecycle                                                                |
| `Assets/Scripts/Planet/Surface/TerrainQuadtree.cs`         | Per-face tree, neighbor lookup, leaf gathering                                              |
| `Assets/Scripts/Planet/Surface/CubeFaceTopology.cs`        | Static 24-entry edge-adjacency table + mirror helpers                                       |
| `Assets/Scripts/Planet/Surface/CombinedFaceMesh.cs`        | Per-face combined-leaf mesh builder                                                         |
| `Assets/Scripts/Planet/Surface/PlanetChunkMeshJob.cs`      | Burst job, mirrors `TerrainFaceMeshJob`                                                     |
| `Assets/Scripts/Planet/Surface/EdgeFanTemplates.cs`        | 16 precomputed triangle templates, generated at startup                                     |
| `Assets/Scripts/Core/Data/PlanetResolution.cs`             | `enum PlanetResolution { Low, High }`                                                       |

Modifications to existing:

| File                                      | Change                                                                                                                                                          |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assets/Scripts/Planet/Planet.cs`         | Acquire `_surfaceProvider`; delegate `GenerateMeshAsync` → `provider.GenerateAsync`; `TryGetSurfaceRadius` → `provider.TryGetSurfaceRadius`; add `Update` tick. |
| `Assets/Scripts/Planet/PlanetSettings.cs` | Add `PlanetResolution Resolution` field with `Low` default for safety; opt-in to `High` per-planet.                                                             |

Public surface preserved:

- `IPlanet`, `IPlanetSurfaceSampler`, `IProgressReporter`, `ILateInitialize` — unchanged.
- `PlanetGeneratedEvent` — fired identically by both providers.
- `Planet.Resolution` (per-face vertex resolution field) renamed to `PerFaceResolution` to disambiguate from `PlanetResolution.Low/High` enum. Migration: ctrl-F replace across project; small blast radius (Planet.cs + PlanetEditor.cs).

**Scene-asset change at implementation step 5:** the main scene planet's `PlanetSettings.Resolution` flipped from `Low` (default) to `High` so the dev environment exercises the chunked path every Play session. New planet assets remain `Low` by default. See §17 Q1 for rationale.

---

## 16. Verification plan (no test framework — manual checks)

Following [[project_testing_stance]], no automated test harness. The signals that confirm Phase A works:

1. **Round-trip from a fresh load**: editor scene loads with `PlanetResolution.High`; loading bar progresses 0→1; planet renders fully; no console errors.
2. **Visual seam check**: rotate camera around a cube face boundary; no visible chunk line, no normal-shading crease, no LOD-popping seam.
3. **LOD subdivision visual**: walk camera toward and away from a chunk; subdivision and merge happen in background without frame hitches > 16 ms.
4. **F-key debug overlays** (we already have `DebugCaptureController`): add a `Chunk` debug capture that renders chunk wireframe + state color (Active = green, Subdividing = yellow, Generating = orange).
5. **Existing per-face path still works**: switch `PlanetResolution` to `Low`, regenerate; should be byte-identical to current behavior.
6. **Water still renders correctly** on both resolution paths.
7. **Memory profiling**: GPU memory delta between `Low` and `High` should match the §14 budget within ~20%.

If any of these fail, fix before declaring Phase A done.

---

## 17. Risks and open questions

### Open questions — RESOLVED 2026-05-30

1. **Default resolution per planet:** should `PlanetResolution.Low` or `High` be the default for newly-created planets? My recommendation: **`Low`** while Phase A is being validated; flip to `High` as default once water/seams/perf are confirmed. Acceptable?

**Feedback:** Yes, the default can be low. But the main planet in the scene now should be high, don't you think so we can test the new features? Or do you have another plan for that? I will defer to your judgment here.

**Resolved (judgment call):** **Yes — flip the main scene planet to `High` at implementation step 5**, the moment `ChunkedSurfaceProvider` is wired up. Rationale: the default `Low` keeps existing demos / future planet assets safe, while the active dev scene gets exercised by the new code path every time we hit Play. If `High` regresses something (water seams, perf, etc.), we can flip the scene asset back to `Low` in seconds without touching code — the data/code separation is the whole point of the enum. We will also keep `Low` as the asset default in `PlanetSettings` constructor so any newly-created planet assets are safe by default.

2. **`Planet.Resolution` → `Planet.PerFaceResolution` rename:** any downstream tools / save data referencing it by name? `git grep` is clean inside the repo, but flagging in case it's surfaced anywhere I can't see.

**Feedback:** This project is in active development and no need for backwards compatibility. If we rename something, we can just make sure all previous references are now pointing to the new name. You are good to go on the rename.

**Resolved:** Proceeding with the rename in step 1. Blast radius is just `Planet.cs` + `PlanetEditor.cs` per current grep.

3. **`PlanetSettings.Resolution` as a ScriptableObject field:** OK for it to be per-planet asset, or do you want a global game setting (e.g. graphics quality determines it)? Default plan: per-planet asset, can be overridden by quality settings later.

**Feedback:** Per planet asset is fine for now. The settings should handle overall features, grass quality low/mid/high for example.

**Resolved:** Per-planet asset is the only home for `PlanetResolution`. The future quality-settings system stays orthogonal — it'll handle grass/snow/atmosphere quality tiers (Low/Mid/High) without touching the chunk-vs-face decision. The chunk-vs-face decision is a content authoring choice (this planet is detailed because the player goes there; that planet is a skybox decoration), not a graphics-fidelity choice.

4. **Job throttle cap (default 8 concurrent chunk jobs):** OK, or do you want this exposed in quality settings now?

**Feedback:** This seems fine to me.

**Resolved:** Hardcoded `const int MaxConcurrentChunkJobs = 8;` in `ChunkedSurfaceProvider`. Trivial to surface later as an `IGrassQualitySettings` field if perf measurements warrant it.

**All open questions resolved. This doc is ready for implementation sign-off.**

### Implementation risks

| Risk                                                                              | Mitigation                                                                                                                                  |
| --------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Cross-face neighbor mirror table is hand-derived and easy to get wrong.           | Startup self-test asserts "neighbor's neighbor's neighbor walk lands me back at myself"; CI-able later.                                     |
| Edge-fan template generation has 16 cases × subtle corner handling.               | Generate templates once at startup, dump them to a debug log on first run for visual inspection.                                            |
| Half-chunk overlap doubles ghost-chunk responsibility (which face owns the mesh). | Spec is explicit: ghost chunk's mesh lives in the **neighbor face's** combined mesh, not its origin face. Single source of truth per ghost. |
| Stale-job callbacks when chunks are merged mid-generation.                        | Per-chunk `Generation` counter; bumped on every transition; job completion compares before promoting.                                       |
| `WaterMeshBuilder` adapter complexity.                                            | Phase A keeps CPU vertex shadow per face exactly mirroring per-face provider's layout; water sees no difference.                            |
| Frame hitches on large LOD changes (e.g. camera warp).                            | Job throttle cap + only one combined-mesh rebuild per face per frame.                                                                       |

---

## 18. Phase A delivery checklist

When the chunk skeleton is "done":

- [ ] `IPlanetSurfaceProvider` interface defined, both implementations compile.
- [ ] `PlanetResolution` enum threaded through settings and selector logic.
- [ ] `CubeFaceTopology` table + self-test passes at startup.
- [ ] `TerrainQuadtree` produces correct neighbor LODs for same-face and cross-face queries (smoke test by walking a known surface point).
- [ ] `PlanetChunk` lifecycle state machine drives all subdivide/merge transitions without stale callbacks.
- [ ] Per-chunk Burst mesh job produces correct geometry + normals.
- [ ] Edge-fan templates eliminate visible cracks at all 16 neighbor-LOD combinations.
- [ ] Half-chunk overlap eliminates visible cube-face seams.
- [ ] `CombinedFaceMesh` rebuilds incrementally on leaf-set changes.
- [ ] `TryGetSurfaceRadius` works identically in both `Low` and `High` modes.
- [ ] Water mesh renders identically in both modes.
- [ ] Debug overlay (`F`-key chunk wireframe) implemented.
- [ ] All 7 verification-plan checks pass.

---

## Next step after this doc is approved

Implementation in this order:

1. New files compile-only first (no behavior change): `IPlanetSurfaceProvider`, `PerFaceSurfaceProvider` wrapping existing path. Planet swaps to provider with `Low` default. Confirms no regression.
2. `CubeFaceTopology` + self-test.
3. `PlanetChunk` + `TerrainQuadtree` (no rendering yet; tree just builds).
4. `PlanetChunkMeshJob` + `EdgeFanTemplates`.
5. `ChunkedSurfaceProvider` + `CombinedFaceMesh`. Switch `High` mode on for testing.
6. Half-chunk overlap.
7. `TryGetSurfaceRadius` + water adapter.
8. Debug overlay.
9. Verification pass.

Each step is independently committable. Bryan reviews after step 1 (provider seam), step 5 (chunked mode working without seams), step 8 (full Phase A). Three review checkpoints inside the phase.

---

## Pre-cache pivot (decided 2026-05-30 after step 7 perf testing)

### Why we pivoted

Steps 5–7 shipped a dynamic LOD-subdivision pipeline (subdivide chunks as the camera approaches, merge them as it pulls back). It rendered correctly but had severe perf issues that compounded as we built it out:

- **Subdivision jobs run while camera flies past** — Unity Burst jobs aren't cancellable; we'd pay for work the camera no longer needed.
- **GC churn from temporary List/Dictionary allocations** in the smoothing pass — pooling helped but the architecture kept exposing the next bottleneck.
- **Main-thread mesh upload is unavoidable** (Unity's `Mesh.SetVertices` is main-thread-only). Dynamic rebuilds during gameplay always cost a hitch.
- **Each fix uncovered the next bottleneck.** Whack-a-mole.

Bryan flagged this as the right time to step back rather than keep patching. After weighing pre-cache vs GPU compute, we picked **pre-cache** as the right next step:

- Removes the entire dynamic-rebuild failure mode (no runtime subdivision = no hitches).
- Reuses 100% of the data structures we already built (`PlanetChunk`, `TerrainQuadtree`, `CombinedFaceMesh`, `PlanetChunkMeshJob`, `CubeFaceTopology`).
- Aligns with Bryan's own intuition: **build all surface state during loading-bar time, when the player is already waiting.** Future marching-cubes / SDF work fits the same pattern.
- GPU compute is the right long-term answer for terrain (Phase D grass is already locked in as GPU) but is a 3-4 week rewrite. Pre-cache buys a working planet now and doesn't preclude GPU later.

### What pre-cache means

> **All chunks at all depths from 0 to `MaxChunkDepth` are generated once, in parallel, during `Planet.GeneratePlanetAsync`. Runtime `Tick` is a cheap visibility filter — for each leaf, decide "render this chunk OR recurse into children" based on camera distance. No mesh jobs scheduled after initial gen.**

### Architecture

```
GenerateAsync:
  1. For each face, build the full quadtree to MaxChunkDepth via
     TerrainQuadtree.BuildToFixedDepth.
  2. Collect ALL chunks at ALL depths (including internal nodes — they're
     rendering candidates at coarser camera distances, not just leaves).
  3. Schedule mesh jobs for every chunk in batches of N (~64), awaiting each
     batch before scheduling the next. Bounds transient NativeArray memory at
     ~10 MB per batch (vs ~300 MB if we schedule all at once).
  4. After each batch, drain completed jobs to populate chunks' CPU arrays.
  5. Compute initial visibility from Camera.main (or default to root chunks
     if no camera yet).
  6. Build each face's combined mesh once from the visible leaf set.
  7. Run SmoothFaceNormals once to fix cross-face + within-face seams.

Tick (per frame):
  1. For each face, walk the quadtree and gather the current visible leaf set
     by comparing each non-leaf's camera distance against its subdivide threshold.
  2. If the visible set for a face changed since last Tick, mark it dirty.
  3. Rebuild at most one dirty face per Tick (round-robin) — cheap since CPU
     vertex data is already in chunk.CpuVertices; just concatenate + upload.
```

### What gets removed

The following code (added in steps 5–7) goes away:

- `BeginSubdivide`, `BeginMerge` runtime calls
- `TraverseAndUpdate` subdivide-decision logic
- `_pendingJobs` queue + per-Tick `DrainCompletedJobs` (drain only runs during initial gen)
- Stale-callback guard (`Generation` check on completion — no in-flight jobs at runtime)
- `PromoteSubdividedParents` (no Subdividing→ActiveWithChildren transitions at runtime)
- Round-robin scheduling for subdivision
- Per-Tick `SmoothFaceNormals` (runs once at initial gen instead)
- Pooling concerns for the smoothing scratch buffers (only one call ever)

### What stays unchanged

- `PlanetChunk`, `TerrainQuadtree`, `CombinedFaceMesh`, `PlanetChunkMeshJob`, `ChunkTriangleTemplate`, `CubeFaceTopology`, `ChunkedFaceMeshSampler`, `IFaceMeshSampler` — all reusable as-is.
- `PerFaceSurfaceProvider` (Low mode) — completely unaffected.
- `Planet.cs` — unaffected (still calls `_surfaceProvider.GenerateAsync` / `Tick` / `TryGetLocalSurfaceRadius` / `GetFaceMeshSamplers`).
- Water + surface-radius integration (step 7) — works identically.

### Memory cost

For depth N, 6 × Σ 4^i chunks × ~150 KB/chunk (vertex + sphere + elevation + radii arrays):

| MaxChunkDepth | Chunks | Approx memory |
|---|---|---|
| 3 | 510 | ~75 MB |
| 4 | 2,046 | ~300 MB |
| 5 | 8,190 | ~1.2 GB |

**Default: 4.** Per-planet override via `PlanetSettings.MaxChunkDepth`. Smaller skybox-planets can use 2; large explorable planets can ramp to 5+ if memory permits.

### Limitations carried forward

- **Max LOD is fixed at `MaxChunkDepth`.** For "player walking on planet surface" zoom levels deeper than the cap, the geometry will look low-detail.
- **Edge-fan masks are skipped** — every chunk built with `EdgeFanMask = 0` (no vertex snapping). Small cracks may appear at LOD transition boundaries. Fix options for later: pre-build chunks with all 16 mask variants (16× memory), or regenerate affected chunks on visibility transitions (small dynamic work).
- **Visibility-driven combined-mesh rebuilds still happen at runtime.** Capped at 1 face per Tick (matches step 5.5). Cost is much lower than dynamic subdivision since vertex data is already in CPU arrays — no waiting for jobs.

### Future paths (deferred)

- **Layered dynamic subdivision** for chunks within close range of the player (only the deepest LODs are dynamic; coarse levels stay pre-cached). Adds back some of the dynamic complexity but only where it matters.
- **GPU compute terrain** as the eventual replacement once grass (Phase D) ships its GPU pipeline. By then we'll know what data shapes the GPU side needs.
- **Edge-fan mesh variants** if LOD-transition cracks turn out to be visible enough to matter.
