# Phase 15: Polish & Stretch Goals

## 15.1 — Post-Processing
- [ ] Bloom for sun and bright surfaces
- [ ] Ambient occlusion for terrain detail
- [ ] Color grading per biome mood
- [ ] Lens flare on sun
- [ ] Fog (distance + height based)

## 15.2 — Clouds
- [ ] Cloud layer sphere above planet surface
- [ ] Noise-based cloud density, animated drift
- [ ] Lit by sun direction, cast shadows on terrain
- [ ] Configurable coverage and altitude

## 15.3 — Weather System
- [ ] Rain, snow, storms per biome
- [ ] Weather affects surface interaction map (wet ground, snow accumulation)
- [ ] Wind strength variation affects grass/trees
- [ ] Weather affects gameplay (cold damage in blizzard, etc.)

## 15.4 — Audio
- [ ] Ambient sounds per biome (birds in forest, wind in tundra, waves at beach)
- [ ] Day/night audio transitions
- [ ] Weather audio (rain, thunder, wind)
- [ ] Interaction audio (chopping, mining, building, footsteps per terrain type)

## 15.5 — Fast Travel & Map
- [ ] Teleportation system between discovered locations
- [ ] Map UI showing explored areas, biomes, placed markers
- [ ] Waypoint/beacon placement
- [ ] Minimap or compass HUD

## 15.6 — Boats & Ocean Travel
- [ ] Boat as a craftable vehicle entity (raft → karve → longship progression, Valheim-style)
- [ ] Water surface physics: buoyancy system for boat hull
- [ ] Sailing mechanics: wind direction from weather system affects speed/heading
- [ ] Player can board/exit boat, walk on deck while sailing
- [ ] Boat inventory for transporting resources across ocean
- [ ] Boat takes damage from storms, sea creatures (stretch), rocks
- [ ] Boat persistence: saved as entity in chunk delta, loads/unloads with chunks

## 15.7 — Magic System (Stretch)
- [ ] Mana pool as player stat (collected from mana orb pickups)
- [ ] Spell system: equippable spells with mana cost, cooldown, effects
- [ ] Spell types: combat (fireball, shield), utility (teleport, light), building (repair, reinforce)
- [ ] Spell discovery: found in caves, ruins, rare drops
- [ ] Mana regeneration: slow passive regen, faster near magical biome features

## 15.8 — Enemies & Combat (Stretch)
- [ ] Enemy spawning per biome (wolves in forest, skeletons in caves, etc.)
- [ ] Enemies as `WorldEntity` with loot tables (resources + mana orbs + rare items)
- [ ] Basic melee/ranged combat system
- [ ] Enemy AI: patrol, chase, attack
- [ ] Loot drops from enemies
- [ ] Base defense: enemies attack player structures
