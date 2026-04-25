# Procedural Planets — Master Project Plan

## Vision
A planetary survival/exploration game built in Unity 6. Players explore a procedurally generated planet with diverse biomes, gather resources, craft items, build structures (Valheim-style with structural integrity), deform terrain, and experience a full day/night cycle with orbiting moons. Designed for co-op multiplayer with persistent world state. Future magic system with mana orbs as world drops.

## Current State (Phases 1–2 Complete)
- Cube-sphere mesh generation (6 faces, resolution 2–256)
- Layered simplex noise terrain (Simple + Rigid filters, first-layer masking)
- Biome system with elevation-based color gradients, tint, blend, ocean gradient
- Deterministic seed propagation (Planet → ShapeGenerator → NoiseFilterFactory → Noise)
- Poisson-disc sampling (2D flat + 3D sphere with biome data)
- Custom PlanetEditor with inline settings, auto-update
- ScriptableObject settings (ShapeSettings, ColorSettings)
- Shapes plugin for debug visualization

## Phase Documents
- [00 — Architecture Decisions](phases/00-architecture.md)
- [00 — Code Architecture: Interfaces, Services & Patterns](phases/00-code-architecture.md)
- [01 — Code Quality Fixes](phases/01-code-quality-fixes.md)
- [02 — Phase 3: Foundation & Architecture](phases/02-phase3-foundation.md)
- [03 — Phase 4: Enhanced Biome System](phases/03-phase4-biomes.md)
- [04 — Phase 5: Water System](phases/04-phase5-water.md)
- [05 — Phase 6: Celestial System](phases/05-phase6-celestial.md)
- [06 — Phase 7: Moon System](phases/06-phase7-moons.md)
- [07 — Phase 8: Object Spawning & Grass](phases/07-phase8-spawning.md)
- [08 — Phase 9: Marching Cubes & Caves](phases/08-phase9-marching-cubes.md)
- [09 — Phase 10: Character Controller](phases/09-phase10-character.md)
- [10 — Phase 11: Resources & Crafting](phases/10-phase11-resources.md)
- [11 — Phase 12: Building System](phases/11-phase12-building.md)
- [12 — Phase 13: LOD & Performance](phases/12-phase13-lod.md)
- [13 — Phase 14: Multiplayer](phases/13-phase14-multiplayer.md)
- [14 — Phase 15: Polish & Stretch Goals](phases/14-phase15-polish.md)

## Phase Dependencies
```
Phase 3:  Foundation & Architecture ──────────────────────────┐
Phase 4:  Enhanced Biomes ──────────────────────────────┐     │
Phase 5:  Water System ──────────────── (needs 4) ──────┤     │
Phase 6:  Celestial / Day-Night ──── (parallel) ────────┤     │
Phase 7:  Moon System ───────────── (needs 6) ──────────┤     │
Phase 8:  Object Spawning / Grass ── (needs 4) ─────────┤     │
Phase 9:  Marching Cubes / Caves ─── (needs 3) ─────────┘     │
Phase 10: Character Controller ────── (needs 9) ──────────────┤
Phase 11: Resources & Crafting ────── (needs 8,10) ───────────┤
Phase 12: Building System ─────────── (needs 3,10,11) ────────┤
Phase 13: LOD & Performance ───────── (parallel, any time)    │
Phase 14: Multiplayer ─────────────── (needs 3,10,11,12) ─────┘
Phase 15: Polish ──────────────────── (ongoing)
```

## Recommended Build Order
| Order | Phase | Rationale |
|-------|-------|-----------|
| 1 | Code Quality Fixes | Solid foundation before new work |
| 2 | Phase 3: Foundation | Command pattern, chunks, save/load, debug camera — everything depends on this |
| 3 | Phase 4: Biomes | Core visual identity, needed by spawning and water |
| 4 | Phase 6: Celestial | Day/night is high visual impact, independent of terrain work |
| 5 | Phase 5: Water | Oceans complete the planet's look |
| 6 | Phase 7: Moons | Depends on celestial, high visual payoff |
| 7 | Phase 8: Spawning & Grass | Brings the world to life — entities are harvestable from the start |
| 8 | Phase 9: Marching Cubes | Enables caves and terrain deformation |
| 9 | Phase 10: Character | Playable character on the finished world |
| 10 | Phase 11: Resources & Crafting | Harvest, inventory, crafting — core gameplay loop begins |
| 11 | Phase 12: Building | Valheim-style building with structural integrity |
| 12 | Phase 13: LOD | Performance optimization once features are stable |
| 13 | Phase 14: Multiplayer | Network layer on top of working systems |
| 14 | Phase 15: Polish | Ongoing throughout, major push at end |
