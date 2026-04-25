# Phase 8: Object Spawning System
*Goal: Biome-appropriate vegetation, rocks, and details — all harvestable with loot tables*

## 8.1 — Spawn System Architecture
- [ ] Create `WorldEntity` base class: health, loot table, respawn timer, biome ID
- [ ] Create `LootTable` ScriptableObject: weighted list of drops (item, quantity range, drop chance, conditions)
- [ ] Create `PickupEntity` class: dropped item in world, physics, attraction radius, despawn timer, glow/float for special items (mana orbs)
- [ ] Create `SpawnRule` ScriptableObject: prefab list, density, scale range, biome filter, elevation range, slope constraints, loot table reference
- [ ] Create `BiomeSpawnProfile` ScriptableObject: list of SpawnRules per biome type
- [ ] Create `ObjectSpawner` that uses Poisson-disc sampling + biome data to place entities
- [ ] Spawning runs async: `async Awaitable SpawnChunkEntitiesAsync(ChunkCoord coord, CancellationToken ct)`
- [ ] Align to surface normal, random rotation around normal for variation
- [ ] Spawn data stored per chunk, regenerated deterministically from seed
- [ ] Harvested/destroyed entities tracked in chunk delta save
- [ ] Loot probability rolls use deterministic seed (chunk seed + entity index) for multiplayer agreement

## 8.2 — Procedural Grass (GPU Compute)
- [ ] Compute shader generates blade positions from chunk terrain data
- [ ] Simple triangle geometry per blade (3–5 vertices)
- [ ] Render via `DrawProceduralIndirect` — target millions of blades
- [ ] Wind system: global noise-based vector field affects blade lean/sway
- [ ] `SurfaceInteractionMap` integration: grass bends when player walks through
- [ ] Distance-based density falloff (full density near camera, sparse at distance, none beyond threshold)
- [ ] Per-biome grass color, height, and density variation
- [ ] No grass in ocean, desert, snow, or on steep slopes

## 8.3 — Trees & Large Vegetation
- [ ] Tree spawning with biome-appropriate prefabs (palm → tropical, pine → taiga, oak → forest)
- [ ] Trees as `WorldEntity`: choppable, loot table (wood + chance of mana orbs + rare drops), falls with physics on death
- [ ] Tree falling: physics-enabled fall → breaks into log segments → player chops logs for final resource yield (Valheim-style)
- [ ] LOD chain: full 3D mesh (near) → simplified mesh (medium) → billboard (far) → cluster billboard (very far) → hidden
- [ ] Scale variation based on elevation and biome
- [ ] Wind: tree trunk sway and leaf rustle via vertex shader
- [ ] Density falloff near biome edges for natural transitions

## 8.4 — Rocks, Ore & Details
- [ ] Rock spawning with biome-appropriate meshes/materials
- [ ] Rocks as `WorldEntity`: mineable, loot table (stone/ore + chance of mana orbs)
- [ ] Ore variants per biome (iron in mountains, crystals in caves, etc.)
- [ ] Small details: pebbles, flowers, mushrooms, bushes per biome (decorative, some harvestable)
- [ ] Slope-based rules: rocks on steep slopes, grass/flowers on flat areas
- [ ] GPU instancing for small repeated objects

## 8.5 — Wildlife (Stretch)
- [ ] Wildlife prefabs per biome, low density
- [ ] Wildlife as `WorldEntity`: huntable, loot table (food/hide + chance of mana orbs)
- [ ] Basic idle/wander AI on planet surface
- [ ] Align to surface normal and spherical gravity
