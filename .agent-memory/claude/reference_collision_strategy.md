---
name: reference-collision-strategy
description: "Decided 2026-08-09: terrain/dynamic-object collision. Analytic raycast for cheap ground queries (character/spawn/placement, no colliders); STREAMED per-chunk MeshColliders (cook-once, cache, async BakeMesh) in a bubble around player+active objects for real physics (rocks/trees/ragdolls); ONE ground-truth surface under both. Doc: docs/design/2026-08-09-collision-strategy.md"
metadata:
  type: reference
---

Collision architecture decision (Bryan + Claude, 2026-08-09). Full doc:
`docs/design/2026-08-09-collision-strategy.md`.

- **Cheap ubiquitous ground queries** (character grounding, spawn, prop placement, AI height, simple
  projectiles) → **analytic raycast** against the visible surface. NO colliders. (Character already does this.)
- **Real dynamic physics** (felled trees, thrown rocks that roll/rest, ragdolls, loot) → **Unity Rigidbody +
  streamed per-chunk `MeshCollider`s** in a bubble around player + active objects. Cook ONCE per chunk (reuse
  the existing render mesh), cache while near, drop when far, re-cook only on geometry change (dig). Cook
  OFF-MAIN-THREAD via `Physics.BakeMesh` (Burst job) → no hitch. Do NOT build a custom analytic collision solver.
- **ONE ground-truth surface** under analytic queries AND colliders, or objects rest above/below the player's
  feet. The character fall-through bug WAS the analytic sampler (`TryGetSurfaceRadius`) differing from the
  render mesh by 3-24 units — see [[project-character-terrain-plans]]. Character now grounds on the render-mesh
  raycast; physics colliders come from the render mesh too. Analytic sampler = approximate queries only.

**REJECTED:** (1) single vert-updated collision patch that follows the player — coverage can't follow objects
thrown/felled away from the player, and a MeshCollider re-cooks on vert change so the cost win is illusory. The
heightfield (`TerrainCollider`) version is only right for a future physics `CharacterController`, which is moot
(character is kinematic-analytic). (2) whole-planet colliders (prohibitive). (3) custom analytic solver
(reinvents physics).

**SDF/Phase-9:** collision is a byproduct of the meshing pass — generate render + collision mesh from the same
SDF for near+changed chunks; dig → re-mesh + re-cook that chunk. "Build/update collision at gen/modification."
