# Performance and Maintainability Plan

**Date:** 2026-06-08  
**Status:** Active; slices 1–4 implemented and validated (slice 4 split landed 2026-06-12, see
[2026-06-12-chunked-surface-provider-restructure.md](2026-06-12-chunked-surface-provider-restructure.md)).
Slices 5–6 outstanding.

## Goals

1. Reduce retained managed, native, and graphics memory before adding more
   per-surface state.
2. Remove avoidable per-frame work and allocation churn.
3. Split large classes along existing ownership boundaries.
4. Preserve rendering behavior through F10 comparisons and focused build checks.

This is not a general cleanup pass. Each refactor must either remove measured
cost, make a high-risk subsystem independently testable, or eliminate duplicated
ownership that has already caused bugs.

## Baseline

Latest grass F10 capture:

- Total reported memory: 12.6 GB
- Mono heap: 2.7 GB
- GC-tracked memory: 2.4 GB
- Native reserved memory: 6.2 GB
- Graphics-driver memory: 3.8 GB
- Chunk texture sets: 2,046
- Grass chunk runtimes: 15
- Production frame rate: approximately 59 FPS

The high-resolution surface provider builds all 2,046 nodes in a six-face,
depth-4 quadtree. Each node has a 97 x 97 mesh and previously retained:

- vertices;
- unit-sphere points;
- elevations;
- vertex radii;
- terrain normals;
- colors;
- biome data.

Those managed arrays account for approximately 1,395 MiB before object and array
overhead. The provider also uploads and retains a Unity mesh for every node.

## Execution Order

### Slice 1: Release completed generation data

**Status:** Implemented and orbit-F10 validated.

After biome maps and mesh colors are uploaded:

- release `CpuColors` for every chunk;
- release `CpuUnitSpherePoints` when the water sampler owns an aggregate copy;
- release elevation, radius, and biome arrays from internal quadtree nodes;
- retain vertices and normals for visible-surface raycasts;
- retain leaf elevation, radius, and biome data for sampling and biome rebakes.

Expected retained chunk-array reduction:

```text
Before: approximately 1,395 MiB
After:  approximately   771 MiB
Saved:  approximately   624 MiB
```

The F10 memory sidecar now reports `Chunk CPU arrays retained`.

Validation gate:

- Unity imports without exceptions.
- Planet generation completes.
- Terrain, water, grass, and scale-marker raycasts behave identically.
- F10 reports approximately 771 MiB retained chunk CPU data.
- GC-tracked and Mono-used memory fall materially from the baseline.

### Slice 2: Correct texture ownership

**Status:** Implemented; awaiting Unity/F10 validation.

The face biome atlases are the production terrain and grass source, but every
quadtree node still owns local biome color, ID, and weight textures. Surface
state is also allocated for all nodes even though current grass consumers use
max-depth chunks.

Work:

- separate biome-bake staging data from persistent surface-state ownership;
- remove local biome textures after face-atlas construction;
- make single-region atlas rebakes update the atlas rather than an unused local
  texture;
- move surface state to a face atlas or allocate it lazily for active leaves;
- report bytes and counts separately for biome atlases and mutable state.

The implemented first ownership pass keeps the existing per-chunk surface-state
textures because grass placement currently consumes them directly. It releases
the three local biome textures from every chunk after all six face atlases are
successfully built and bound. Regional rebakes use three reusable 64 x 64 staging
textures plus `Graphics.CopyTexture` to update the production atlas without
making the full atlases CPU-readable.

Validation gate:

- all terrain texture and grass F10 modes match the pre-change captures;
- biome rebake modifies the production atlas;
- chunk texture count and graphics memory fall materially.

### Slice 3: Replace the all-node Unity mesh cache

The provider currently creates a GameObject, MeshRenderer, MeshFilter, and Mesh
for every quadtree node. This avoids fly-through generation hitches but shifts
the cost into large persistent native and graphics allocations.

Work:

- measure mesh vertex/index memory explicitly;
- keep generated compact source data separate from render objects;
- pool render handles for the visible set plus a small transition reserve;
- upload meshes incrementally before they become visible;
- preserve hysteresis so pooled uploads do not cause rapid churn.

Validation gate:

- no visible LOD holes or fly-through hitch regression;
- visible and reserve mesh counts remain bounded;
- graphics-driver and native memory fall substantially.

### Slice 4: Split `ChunkedSurfaceProvider`

At roughly 1,600 lines, the provider owns generation, mesh upload, visibility,
biome baking, atlas construction, surface queries, diagnostics, and disposal.
Split it after ownership is corrected so the new classes reflect the final data
lifecycle:

- `ChunkSurfaceGenerator`: job scheduling and generated CPU data;
- `ChunkRenderCache`: pooled Unity meshes and visibility application;
- `ChunkVisibilityResolver`: frustum, horizon, and screen-size LOD selection;
- `BiomeAtlasBuilder`: bake staging, atlas construction, and regional updates;
- `ChunkSurfaceQueries`: radius sampling and visible triangle raycasts.

`ChunkedSurfaceProvider` remains the small orchestrator implementing the public
surface and visibility interfaces.

### Slice 5: Profile update loops and grass rendering

Work:

- add CPU/GPU timing counters for surface visibility, terrain, water, clouds,
  near grass, and chunk grass;
- remove work that runs when camera pages, weather state, or visible chunks did
  not change;
- replace distant 54-vertex grass tufts with a cheaper card representation;
- evaluate shared or pooled chunk grass buffers after timing data exists.

### Slice 6: Secondary maintainability passes

Apply the same evidence-driven split to the remaining large classes:

- `Planet`: generation orchestration versus runtime feature control;
- `DebugCaptureController`: capture scheduling versus image encoding and file IO;
- `WaterDebugModule`: mode registration versus metadata collection;
- `FreeCameraController`: movement versus teleport/capture/debug commands;
- `ConsoleController`: command execution versus UI state.

Do not combine these splits with visual feature changes.

## Engineering Rules

- Build Core and Planet projects serially.
- Keep F10 before/after captures from the same camera location.
- Treat build success as code health, not visual validation.
- Add counters before optimizing opaque ownership.
- Avoid abstraction-only refactors without a measured or testability benefit.
- Keep public contracts stable while internals are split.
- Prefer `ILogger`/`LoggerProvider` over direct Unity logging in new code.
