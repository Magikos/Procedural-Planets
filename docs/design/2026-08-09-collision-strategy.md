# Collision strategy — terrain & dynamic objects

**Status:** decided 2026-08-09 (Bryan + Claude). Direction note, no code. Pin before Phase 9 (SDF /
marching-cubes) and the building/gameplay-physics systems build on it.

## Context

The planet is a chunked cube-sphere. Chunk meshes are **render-only — no colliders** (a deliberate perf
choice: colliders on a whole planet of chunks are prohibitive to cook and hold). The character controller
grounds **analytically** via `IPlanetSurfaceRaycaster` (a raycast against the visible chunk surface), not
physics — no collider needed for walking.

The open question: once gameplay adds **dynamic objects** — a chopped tree that falls, a thrown rock that
bounces/rolls/rests, ragdolls, dropped loot, explosion-shoved debris — those need real contact against the
terrain. Do we (a) query the surface with a custom checker, (b) keep one collision "patch" that follows the
player and rewrite its verts, or (c) build/stream real collision meshes? And when SDF/marching-cubes lands,
where does collision come from?

## Decision

**Two surfaces of need, two mechanisms — and one ground-truth surface under both.**

1. **Ubiquitous cheap ground queries** (character grounding, spawn, prop placement, AI walk-height, camera,
   simple projectiles that only need "did I hit the ground") → **analytic raycast** against the visible
   surface. No colliders. Planet-wide, cheap. *(Already how the character works.)*

2. **Real dynamic physics** (felled trees, thrown rocks that roll/rest/stack, ragdolls, loot settling,
   debris) → **Unity Rigidbody physics against real `MeshCollider`s**, **streamed per-chunk in a bubble**
   around the player and any active dynamic objects. Do **not** reimplement contact resolution analytically —
   that is a physics engine.

3. **One surface for both.** The analytic queries (1) and the collision meshes (2) must read the **same**
   surface, or a rock rests visibly above/below the player's feet. This already bit us: the analytic sampler
   (`TryGetSurfaceRadius`) and the rendered mesh differed by 3–24 units, which dropped the character through
   the terrain. The character now grounds on the **render-mesh raycast**; physics colliders come from the
   **render mesh** too. The analytic sampler is relegated to approximate queries where exact agreement
   doesn't matter.

### Collision streaming shape
- Attach a `MeshCollider` to each chunk **within a physics radius** of the player + each active dynamic
  object, pointing at that chunk's **existing render mesh** (reuse — don't build new geometry).
- **Cook once per chunk** the first time it enters the bubble; **cache** while near; **drop** when far. This
  is not "rebuild repeatedly": walking only cooks *newly entered* chunks (a few per boundary crossing). The
  only re-cook is when a chunk's geometry **changes** (a dig) — which we want.
- **Cook off the main thread** with `Physics.BakeMesh(meshId, convex)` in a Burst job, then assign the
  pre-baked mesh on the main thread with **zero cook hitch**. This is the standard large-world pattern and
  removes the only real cost (PhysX cook stutter).

## Options considered and rejected

- **Single collision "patch" that follows the player, verts updated.** Rejected as the general solution:
  - **Coverage.** It covers the player, but colliders must follow the *objects* — a rock thrown 30 m, a tree
    falling sideways, scattered loot leave the patch and fall through.
  - **Cost is illusory for a MeshCollider.** A `MeshCollider` **re-cooks whenever its verts change**, so a
    vert-updated patch is baking constantly anyway (just a smaller mesh), plus you must sample the surface
    height for every patch vert — work the chunk mesh already did.
  - **Where it *is* right:** a moving **`TerrainCollider` (heightfield)** patch updates heights **without a
    full cook** — the correct version of this idea — but its natural use is **player-local ground for a
    physics `CharacterController`**, which we've already solved analytically (kinematic character, no
    collider). So the need it serves is gone. Keep it in the back pocket if we ever want a physics character.

- **Whole-planet colliders.** Prohibitive cook cost + memory. Nothing dynamic exists far from the player, so
  it's wasted everywhere but the bubble.

- **Custom analytic collision solver** for dynamic objects. Rejected: resting, stacking, sliding, restitution,
  ragdolls = a physics engine. Unity's is free and robust *given colliders*. Analytic checks stay for the
  cheap case + simple projectiles only.

## Use-case matrix

| Need | Mechanism | Collider? |
|------|-----------|-----------|
| Character grounding / spawn / placement / AI height | analytic raycast (render surface) | no |
| Simple projectile "did I hit ground" (no roll/rest) | analytic trajectory-vs-surface raycast | no |
| Thrown rock that rolls / rests / stacks | Unity Rigidbody + streamed chunk MeshColliders | yes (bubble) |
| Felled tree falling + landing | dynamic Rigidbody trunk vs bubble colliders | yes (bubble) |
| Ragdolls / loot settling / debris | Unity physics vs bubble colliders | yes (bubble) |

## SDF / marching-cubes (Phase 9) alignment

Collision becomes a **byproduct of the meshing pass**: generate the render mesh **and** the collision mesh
from the same SDF, for chunks that are **near active physics AND changed**. A dig modifies the SDF → re-mesh
+ re-cook that one chunk (async). One source of truth; render, collision, and analytic queries stay
consistent as long as they all derive from the SDF surface. This is exactly the "build/update collision at
generation and modification" model — it just falls out of the SDF pipeline cleanly.

Chopping a tree fits the same frame: the instanced billboard is swapped for a real Rigidbody trunk that
falls, collides with the local bubble colliders, and rests.

## Follow-ups (when the time comes, not now)
- Define the physics-bubble radius + the collider add/evict policy (by player distance + per active object).
- Wire the async `BakeMesh` job + a cooked-mesh cache keyed by chunk + geometry version (so a dig invalidates).
- Decide the felled-tree / thrown-object spawn path (instanced → real Rigidbody handoff).
- Keep every surface consumer (analytic raycaster, colliders, water) reading one surface definition to avoid
  the analytic-vs-mesh drift that caused the character fall-through.
