# Phase 12: Building System
*Goal: Valheim-style snap + freeform building with structural integrity*

## 12.1 — Building Pieces
- [ ] Define building piece types: wall, floor, roof, stairs, door, window, pillar, beam, foundation, fence
- [ ] Create `BuildingPiece` ScriptableObject: mesh, snap points, structural strength, crafting recipe
- [ ] Each piece has defined snap points (edges, corners, centers) with orientation rules
- [ ] Multiple material tiers: wood, stone, metal (different strength/appearance/recipe)

## 12.2 — Placement System
- [ ] `BuildingManager` handles placement mode toggle (activated via Hammer tool)
- [ ] Ghost preview: transparent version of piece follows cursor, snaps to valid positions
- [ ] Toggle between snap mode (pieces align to grid/snap points) and free mode (place anywhere)
- [ ] Rotation controls: rotate piece before placing
- [ ] Collision check: can't place inside terrain or other buildings
- [ ] `PlaceBuildingAction` (implements `IWorldAction`) for persistence and networking
- [ ] Material cost deducted from inventory on placement

## 12.3 — Structural Integrity (Valheim-Style)
- [ ] Each piece has a `MaxSupportDistance` from a grounded foundation
- [ ] Foundations are always stable (connected to terrain)
- [ ] Support propagates through connected pieces, weakening with distance
- [ ] Pieces beyond max support distance collapse (with physics)
- [ ] Visual indicator: color-coded stability (green = strong, yellow = weak, red = about to collapse)
- [ ] Different materials have different support ranges (wood < stone < metal)
- [ ] Pillars and beams provide extra structural support (reset support distance)

## 12.4 — Building Persistence & Interaction
- [ ] Buildings saved as list of pieces: type, position, rotation, material, chunk reference
- [ ] Saved per chunk in the delta save system
- [ ] Buildings load/unload with chunks
- [ ] Destruction: pieces can be dismantled (returns partial materials), triggers structural recalculation
- [ ] Damage: environmental damage (storms?), enemy damage (future), repair with hammer + materials
