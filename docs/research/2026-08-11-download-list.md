# Download list — for the scratch project

**Target:** `D:\Unity\Explore Assets`
**Verified 2026-08-11** against the 214 cached packages in `%APPDATA%\Unity\Asset Store-5.x\` — nothing below is already on disk.

⚠️ **Do not import any of these into ProceduralPlanets.** They go in the scratch project so the code can be read. Only harvested reimplementations and explicitly-approved art cross over. See [the adoption map](2026-08-11-asset-adoption-map.md) for what to take from each.

---

## Tier 1 — the three zero-code subsystems (16)

Survival/crafting/building, portals, and audio have **no code at all** today. This tier changes the most.

- [?] Survival Engine — Crafting, Building, Farming - D:\Unity\Survival Engine - Crafting, Building, Farming
- [x] Ultimate Crafting System
- [?] RPG Farming Kit - D:\Unity\RPG Farming Kit
- [x] Dynamic Portals
- [x] Fluid Seamless Portals — Basic
- [x] Dynamic Water Physics 2
- [x] Altos — Volumetric Clouds, Skybox, and Weather for Unity URP
- [x] UniStorm — Volumetric Clouds, Sky, Modular Weather, and Cloud Shadows
- [x] Ambient Sounds — Interactive Soundscapes for Unity 6
- [x] SurfaceData: Effects System for Surface Interaction
- [x] Blaze AI Engine
- [x] Dialogue System for Unity
- [x] Voxelica — Voxel Engine
- [x] **The Vegetation Engine** ← the _base_ asset. The Polygonal Shaders **module** is already cached but won't compile without this.
- [x] Skill & Attack Indicators
- [x] Crates & Barrels — Stylized Destructible Props

## Tier 2 — comparanda and second opinions (25)

- [x] Character Controller Pro
- [x] Character Movement Fundamentals
- [x] Easy Character Movement 2
- [x] Magica Cloth
- [x] Ragdoll Animator 2
- [x] Tail Animator
- [x] Look Animator
- [x] Legs Animator
- [x] UMotion Pro — Animation Editor
- [x] Magic Arsenal
- [x] Combat Magic Spells — Bundle
- [x] Epic Toon VFX 2
- [x] Stylized VFX Bundle
- [x] All In 1 Vfx Toolkit
- [x] Highlight Plus
- [x] Easy Grid Builder Pro
- [x] Inventory Pro
- [x] FS Swimming System
- [x] Weatherade: Snow and Rain System
- [x] Snowify
- [x] Brute Force — Snow & Ice Shader
- [x] SECTR World Streaming for Unity 6
- [x] Mesh Combine Studio 2
- [x] Clothing Culler
- [x] Love/Hate

## Tier 3 — art and content (23)

- [x] POLYGON — Farm Pack — Art by Synty
- [x] POLYGON — Construction Pack — Art by Synty
- [x] POLYGON — Western Frontier Pack — Art by Synty
- [x] KayKit — Adventurers Character Pack
- [x] KayKit — Dungeon Remastered Pack
- [x] Quirky Series — Animals Mega Pack Vol 1
- [x] Quirky Series — Animals Mega Pack Vol 2
- [x] Quirky Series — Animals Mega Pack Vol 3
- [x] Merchants Enemies And Townsfolk
- [x] Stylized Fantasy Dragons Pack
- [x] Dragons — Fantasy RPG — Customizable Dragon Pack
- [x] HEROIC FANTASY CREATURES FULL PACK VOL 1
- [x] Monsters Ultimate Pack 02 Cute Series
- [x] Monsters Ultimate Pack 09 Cute Series
- [x] Skeletons Pack
- [x] Ghoul Crew — Hand Painted
- [x] GUI Pro — Fantasy RPG
- [x] GUI Pro — Survival Clean
- [x] 6200 Fantasy RPG Icons Pack
- [x] Medieval Weapons — Melee Weapon Pack
- [x] Toon Harbor Pack
- [x] FANTASTIC — Seaside Town
- [x] Fantasy Portal FX

## Tier 4 — only if a tier above leaves a gap (13)

- [x] Thalassophobia: Stylized Oceans
- [-] Underwater FX - Disabled, unavailable.
- [x] Volumetric Fog & Mist 2
- [x] Wheel Controller 3D
- [x] Obi Rope
- [x] Non-Convex Mesh Collider
- [x] DestroyIt — Destruction System
- [x] Sound Shapes: Dynamic Audio Areas
- [x] Real Footsteps
- [x] 3D Characters — Fish
- [x] Monster Sounds Pack
- [x] Conversa Dialogue System
- [x] Stylized Water 2
- [ ] Final IK ← **not downloaded** (my formatting error — it shared a line with Stylized Water 2). Demoted anyway: iStep is cached and already sphere-ready. Only fetch if iStep's foot solver falls short.

## Re-download (1)

- [x] **Wyrms** — the cached 670 MB file is a **truncated download**: the tar stream ends mid-entry and the gzip ISIZE doesn't match. Only 108 entries were recoverable. Re-fetch if you want it evaluated properly (though it's likely a skip — realistic PBR, Built-in shaders, ships `.terrainlayer` assets).

---

## Already cached — do NOT download

These came up as candidates and are already in `%APPDATA%\Unity\Asset Store-5.x\`:

**400 Low Poly RPG Weapons** · Kinematic Character Controller · Poseidon _(inside Low Poly Tools Bundle)_ · iStep · Animancer Pro v8 · A\* Pathfinding Project Pro · NodeCanvas · Sensor Toolkit · Oceanis · GrassFlow 2 · Brute Force Grass Shader · Vegetation Studio Pro · Gaia 2 · Synty POLYGON Particle FX · 100 Special Skills Effects Pack · Mesh Effects · Hovl Procedural fire · Malbers Horse Animset Pro · Stylish Archer · ARPG Samurai · all 11 Polygonmaker packs · Kevin Iglesias · Bird Flock Bundle · Synty Fantasy Kingdom / Dungeons / Vikings / Elven Realm / Pirates / Adventure / Horse / Modular Fantasy Hero / Knights · Toon Farm Pack · Toon Adventure Island · EXPLORER Stone Age · Odin Inspector · Hot Reload · Shapes · Broccoli · Asset Inventory 4

---

## Counts

| Tier                     |  Count |
| ------------------------ | -----: |
| 1 — zero-code subsystems |     16 |
| 2 — comparanda           |     25 |
| 3 — art & content        |     23 |
| 4 — contingency          |     13 |
| Re-download              |      1 |
| **Total**                | **78** |

**If you only do one tier: Tier 1.** Tiers 1–2 are mostly small code packages; Tier 3 is the heavy one on disk.

Ping me when they've landed and I'll inspect and rule on each.
