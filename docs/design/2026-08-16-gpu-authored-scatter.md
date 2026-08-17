# GPU-authored scatter placement

Status: design, not approved. Written 2026-08-16 after the scatter perf work in `764a0fd`.

## Why

Scatter placement runs on the CPU. Every instance is computed in a Burst job, stored in managed
lists, and uploaded to GPU buffers. The GPU already owns culling and drawing; it does not own
placement.

That split costs CPU time proportional to **draw** range, which is large, when the gameplay need is
proportional to **interaction** range, which is small. Flying makes this worst: the further and
faster you travel, the more the CPU plans, even though nothing in the air can be interacted with.

Measured on seed `1691104419`, 30 s flight at 30 m/s, 1.1M live instances, after `764a0fd`:

| Stage | Cost per 30 s |
|---|---:|
| `Reeval` tile plan | 627 ms |
| Master buffer upload | 222 ms |

Both are now small enough not to hitch, but both scale with draw range and prototype count, and both
disappear entirely under this design.

## Goals

- No main-thread cost proportional to draw distance.
- Full tree density at ground level with no hitching, at any travel speed.
- Interaction (chop, mine, pick), projectile hits, and collision keep working exactly as now.
- Harvested instances never reappear.

## Non-goals

- Replacing grass. Grass is already GPU-generated and is not part of this.
- Frustum culling. The current distance-only cull is deliberate so one culled list stays
  shadow-correct. Unchanged here.
- Changing placement *results*. The world must look the same.

## Architecture

**The GPU authors placement.** A compute shader generates instances for a region from the world
seed, the same pure function of (tile, prototype, seed) the CPU uses today. Output feeds the
existing `ScatterGpuDraw` master buffers. No CPU list, no `SetData` of instance transforms.

**The CPU queries on demand.** Placement is deterministic, so "what is in this tile" is a function
call, not stored state. Interactors ask for small local regions and discard the answer afterwards.

An interactor is anything that needs to know about solid things. Each has a role that sets its
radius and payload:

| Role | Radius | Payload | Owner |
|---|---|---|---|
| Harvest | arm's length, grounded | full record with instance id | player |
| Collision | bubble, biased forward by velocity | position, radius, height | player, mounts, later NPCs |
| Projectile | a few metres ahead of itself | position, radius, height | each live projectile |

Altitude gating falls out of the geometry. At 500 m up the collision bubble does not reach the
surface, so it returns nothing and costs nothing. Descend and it starts intersecting terrain, which
is exactly when the data is needed. No speed heuristic, and a fast low-flyer stays protected.

## Removed instances

The one piece of genuinely shared state.

`ScatterHarvestStore` stays the authority. The generation shader takes a buffer of removed instance
ids and skips any instance whose id is present.

- Ids sorted; the shader binary-searches. A few thousand entries is about twelve steps.
- A per-tile "has any removals" flag lets the vast majority of tiles skip the search entirely.
- The buffer changes only when the player harvests, which is discrete and rare, so it uploads
  synchronously at that moment. There is no frame where the GPU still draws a chopped tree.
- Removal is append-only within a session. Regrowth later needs a version counter, nothing more.

## The main risk

If the GPU generates placement and the CPU reproduces it for hit tests, the two must agree exactly.
Disagreement means an arrow passing through a visible tree, or a tree that cannot be chopped.

This is the same dual-implementation hazard that produced two bugs in this codebase already: the
`HashCode.Combine` variant seed (randomised per process, silent for a session) and the 64-bit
`ReadyMask` (silently suppressed 45 prototypes). Neither was visible in play-testing.

Mitigation, and there is precedent: `Assets/Tests/EditMode/ScatterGatherParityTests.cs` already
compares the Burst gather against a managed reference and asserts the instance sets match. Add a GPU
arm to that same test. Placement parity must be a test that fails loudly, not something verified by
looking at the world.

## Staging

Do not migrate trees first. Prove the architecture where a parity bug is harmless.

1. **One decoration prototype end to end.** Pick a non-interactable rock or fern. GPU generation,
   removed-set buffer, parity test, drawn through the existing indirect path. If placement is wrong,
   a rock sits in the wrong spot and nothing else breaks.
2. **All non-interactable prototypes.** Same path, no new mechanisms.
3. **Interactable prototypes plus the query API.** Harvest, collision, projectile roles. This is
   where `ScatterPicker`, `HarvestService`, `TreeFallSystem`, `StumpRenderer` and `LogRenderer` get
   rewired to the query instead of the resident cache.
4. **Delete the CPU streaming path.** `Reeval`, `_work`, `_inFlight`, readiness bitsets, eviction
   LRU, `ScatterDrawBuckets`.

Each step leaves a working game.

## What this deletes from the current code

Step 4 removes most of `ScatterTileCache`, including the tile-major `Reeval` optimised in `764a0fd`.
That work was still worth doing: it fixes the stutter now, in a small diff, and this design is a
multi-week project that may be deferred behind gameplay work.

## Measure before starting

Two numbers should be collected first, because they can change the plan:

1. **Frame time in a standalone build.** Every measurement so far is from the Editor, which carries
   real overhead. At 1.1M instances the Editor shows CPU main 23.7 ms and GPU 24.7 ms. If a build is
   comfortably inside budget, the remaining pressure is smaller than it looks and this project drops
   in priority.
2. **The split between CPU main and GPU at target density.** The two are currently near-balanced. If
   the GPU is the ceiling, moving placement to the GPU will not raise frame rate, and the right work
   is instead LOD, impostor distance, or density tuning.

This design removes CPU cost. It does not make the GPU faster. If the GPU is the limit, this is the
wrong project.

## Open questions

- Does the GPU generation reproduce `ScatterId` exactly? Removal is keyed by id, so the id
  derivation must be identical, not just the transform.
- Does per-tile generation stay stable when the region boundary moves? Instances must not shift as
  the camera crosses a tile edge.
- Collision proxies: derived from the same generation pass, or a separate cheaper pass that only
  emits position, radius and height?
- Interaction with the streamed `MeshCollider` bubble already described in the collision strategy
  note. These should be the same bubble, not two.
