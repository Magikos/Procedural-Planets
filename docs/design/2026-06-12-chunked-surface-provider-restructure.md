# Chunked Surface Provider Restructure

**Date:** 2026-06-12
**Status:** Complete (landed 2026-06-12 on `code-refactor`). Provider went ~2,064 → 546 lines;
collaborators: `BiomeAtlasService`, `ChunkSurfaceGenerator`, `ChunkMeshCache`,
`ChunkVisibilitySelector` (+ stateless `GrassSurfaceAtlasBuilder` / `ChunkSurfaceQueries`).
**Supersedes for this file:** perf-maintainability plan slice 4 (this is the worked-out version of that split)

## Problem

`ChunkedSurfaceProvider` is ~1,740 lines owning six responsibilities that accreted as
features landed (generation → biome bake → chunked LOD → grass atlas → mesh pooling). Each
addition reached into the **same shared fields** (the per-face quadtree, the chunk list, the
atlas textures), so nothing has a boundary.

The coupling is **incidental** (everyone touches shared state), not a fundamental cycle. There
is exactly **one** runtime feedback that looks circular: a biome **rebake** changes the atlas,
and the render side must **rebind** the affected chunk.

Already extracted (true classes, stateless): `GrassSurfaceAtlasBuilder`, `ChunkSurfaceQueries`.

## Decision (agreed)

- **True separate classes**, not partials. Partials hurt followability and are for code
  generators, not hand-written decomposition.
- Each collaborator sits **behind an interface** (swappable, clear contract).
- The **orchestrator constructs the collaborators and injects the interface references** in
  dependency order, and owns disposal in **reverse** order. This is the project's existing
  pattern (CLAUDE.md: "one orchestrator drives many services… owns deterministic disposal in
  reverse init order").
- The internal pipeline uses **direct calls**, not EventBus — it is ordered and synchronous
  (bake → atlas → select → render) and runs on the per-frame hot path. ServiceLocator / EventBus
  stay reserved for the **cross-subsystem boundary** (other subsystems finding the surface
  provider; `PlanetGeneratedEvent`; the external `ChunkShown`/`ChunkHidden`), which already
  works that way.
- The **public `IChunkSurfaceProvider` contract stays stable** throughout.

### Why not ServiceLocator/EventBus for the internals
These are parts of one subsystem with a single per-planet lifecycle and one instance each, on
the hot path. ServiceLocator would hide the dependency graph, fight the lifecycle (regen churn,
multi-planet), and violate the project's "resolve once, never Get per frame" rule. EventBus
would turn an ordered pipeline into implicit control flow with per-frame dispatch cost and lost
call stacks. Explicit interface injection from one composition root gives the same swappability
with the control flow kept legible.

## Target structure

**Shared model:** the per-face `TerrainQuadtree[]` + the chunk list. `PlanetChunk` is already
passive data. The orchestrator owns it and passes it to collaborators. No new `ChunkSurfaceModel`
type for now — promote later only if it earns its keep.

**Collaborators** (each behind an interface where it has a real contract):

| Class | Owns | Depends on |
| --- | --- | --- |
| `ChunkSurfaceGenerator` | job scheduling, `_pendingJobs`, `_filters`, shape generator | model |
| `BiomeAtlasService` (`IBiomeAtlasService`) | face biome atlases + staging, per-chunk bake | model |
| `ChunkMeshCache` (`IChunkMeshCache`) | pooled render handles, visibility apply, biome bind | model, `IBiomeAtlasService` |
| `ChunkVisibilitySelector` (`IChunkVisibilitySelector`) | visible-leaf sets, LOD/frustum/horizon | model, `IChunkMeshCache` |
| `ChunkSurfaceQueries` (done) | raycast / radius sampling | model, visible set |

**Orchestrator** `ChunkedSurfaceProvider`: owns the model + collaborators, implements
`IChunkSurfaceProvider`, drives `Tick` (visibility → mesh cache) and the build pipeline, and
**mediates the one feedback**: `RebakeBiomeMapsAt` calls `biome.RebakeRegion(leaf)` then
`meshCache.Rebind(leaf)` — so `BiomeAtlasService` never references the mesh cache.

## Dependency direction (acyclic)

```
BiomeAtlasService ─┐                 (leaves: depend only on the model)
ChunkSurfaceGenerator ─┘
        ▲
ChunkMeshCache  ── reads ──► IBiomeAtlasService
        ▲
ChunkVisibilitySelector ── calls ──► IChunkMeshCache.SetVisible
        ▲
ChunkSurfaceQueries ── reads ──► visible set
```

## Interface sketches (refined during implementation)

- `IBiomeAtlasService`: `BuildAtlases(chunks)`, `bool TryGetFaceAtlases(face, out…)`,
  `bool HasCompleteAtlases()`, `RebakeRegion(leaf)`, `ReleasePerChunkBiomeTextures(chunks)`,
  per-chunk `BakeChunkMap` / `UploadChunkMap`, `Dispose()`.
- `IChunkMeshCache`: `SetVisible(chunk, bool)`, `Rebind(chunk)`, `bool IsActuallyVisible(chunk)`,
  `RetainColorSource(chunk)`, `Dispose()`; raises the external `ChunkShown`/`ChunkHidden`.
- `IChunkVisibilitySelector`: `bool UpdateForCamera(observerPos, camera)`,
  `IReadOnlyList<PlanetChunk> GetVisibleLeaves(face)`, `GetGrassResidencyChunks(…)`, `Dispose()`.

## Migration order (validate render + grass + raycast + F10 in Unity after each)

1. `BiomeAtlasService` (leaf) — move atlas state + bake/build/staging; orchestrator delegates;
   render bind reads it via the interface.
2. `ChunkSurfaceGenerator` (leaf) — move job scheduling/draining.
3. `ChunkMeshCache` — move render handles + visibility apply + biome bind (holds
   `IBiomeAtlasService`).
4. `ChunkVisibilitySelector` — move LOD selection (holds `IChunkMeshCache`).
5. Orchestrator shrinks to wiring + public interface + Tick/build + rebake mediation.

## Guards

- Public `IChunkSurfaceProvider` unchanged.
- Behaviour-neutral: no logic changes, only relocation + explicit wiring.
- Deterministic dispose in reverse construction order.
- Each step compiles and renders identically before the next.
