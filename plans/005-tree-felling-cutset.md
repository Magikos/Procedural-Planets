# Plan 005 — Tree felling: the cut-set (stump → fall → logs)

**Status:** Inc 1 committed (`140cb80`). Inc 2a done + play-verified (per-tree stumps render).
Inc 2b (shovel) next. Builds on plan 003 (harvest POC, committed `5fa6ce0`).
**Decided with Bryan 2026-08-12.**

## Vision
A chop is not a delete — it's a **cut**. Each tree prototype is authored as a matching **cut-set** so
the pieces line up and the seam is invisible:
- **Standing tree** (exists) → chop →
- **Stump** (planted bottom, stays; later dug up with a shovel) + **Fallen log** (the top, physics/
  scripted fall) + **chop VFX** (leaf + bark-dust particles to mask the swap frame).

Approach **#1 (per-prototype authored)** was chosen over derived materials: the stump / standing tree /
fallen log must be authored pieces that share the silhouette, so the cut reads as one continuous tree and
the fall matches up. The harvest override record keys off `protoIndex`, so a chopped birch always yields the
birch cut-set — never an oak stump.

## The data model (recap — see plan 003 §"how objects are stored")
Scatter is **not a stored object list**; it's derived from the seed. A chop persists as an **override**:
a seed-keyed record layered on the deterministic base. This plan grows that override from a bare
"harvested id" into a small state machine with the data a stump needs to render:

```
HarvestNode { ulong Id; Vector3 Position; int ProtoIndex; HarvestState State }
HarvestState { Stump, Dug }   // Standing = absent from the store
```
The tile-cache Commit filter drops any recorded id (standing tree gone in both states). The stump renderer
draws a stump only for `Stump`-state records. `Dug` = fully gone.

## Increments (each play-verifiable)

### Inc 1 — chop leaves a persistent placeholder stump  *(building now)*
- Grow `ScatterHarvestStore`: `HashSet<ulong>` → `Dictionary<ulong, HarvestNode>` (state + pos + proto),
  persisted (SaveVersion 2 — old v1 harvest files reset once). `Contains(id)` still gates Commit.
- Chop records `State = Stump` (with pos + proto) instead of a bare id; still `RemoveInstance`s the tree.
- New `StumpRenderer`: instanced-draws a **placeholder** stump mesh (a short generated cylinder,
  PropLit material) at each `Stump` record near the camera. Driven per-frame.
- **Verify:** chop a tree → a stump appears where it stood → reload → stump still there.
- Placeholder only — proves the record→renderer path. Real meshes are Inc 2.

### Inc 2 — per-prototype cut-set stump + shovel
- Add optional `StumpMesh` + `StumpMaterial` to `ScatterPrototype` + DTO (the authored cut-set piece).
  `StumpRenderer` uses them per `protoIndex`; falls back to the placeholder when unauthored.
- Import stump assets (`SM_Env_Tree_Stump_*`, `SM_Env_Pine_Stump_*`); author per tree prototype (editor).
- Add a `Shovel` `ToolTier`; `HarvestService` gates by state: axe fells `Standing → Stump`, shovel digs
  `Stump → Dug`. The picker also finds `Stump` records (dig target), not just standing trees.
- **Verify:** each tree leaves a matching stump; shovel removes the stump; both persist.

### Inc 3 — falling log + VFX
- Add `FallenLogMesh` to the cut-set. On chop, spawn the log and play a **scripted tip-over** (lerp
  rotation onto the analytic ground over ~1s) — not real physics yet (terrain has no colliders; a
  rigidbody has nothing to land on). Chop particles (leaves + bark dust) fire on the swap.
- Later: the fallen log is itself harvestable → wood (tree → logs → wood); real rigidbody fall once a
  ground collider exists under it.

## Save-system tie-in
The `HarvestNode` record (`{id, position, protoIndex, state}`) is the first real **world-edit record** —
the currency the eventual world-save owns. When the save system lands (plan-worthy at the crafting/building
pillar), `ScatterHarvestStore` + `SurfaceEditController` register as `IPersistable` under one orchestrator.

## Files (Inc 1)
- **Modify** `Assets/Scripts/Planet/Scatter/ScatterHarvestStore.cs` — HarvestNode dict + state + save v2.
- **Modify** `Assets/Scripts/Planet/Scatter/HarvestService.cs` — record Stump (pos+proto) on fell.
- **Modify** `Assets/Scripts/Planet/Planet.cs` — construct + drive the StumpRenderer; pass pos/proto to persist.
- **New** `Assets/Scripts/Planet/Scatter/StumpRenderer.cs` — instanced placeholder-stump draw from records.
- **Update** the EditMode `ScatterHarvestStoreTests` for the new record shape.
