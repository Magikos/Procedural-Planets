# External Asset Catalog — `D:\Unity\Explore Assets`

_Read-only survey of the scratch Unity project at `D:\Unity\Explore Assets\Assets`, catalogued for selective import into ProceduralPlanets._

> **Provenance.** Round 1 generated 2026-08-10 by a 4-agent read-only scan (Synty / stylized-nature / tools-and-shaders / characters). **Round 2 the same day** by a 5-agent scan of 32 newly added packs (core-locomotion / large-RPG-anim / action-combat-anim / Malbers-and-NPC / new-content-and-tools). Sizes, file counts, rig types, and prefab names were read off disk; `animationType` values come from the `.fbx.meta` files, in several cases from a full-tree census rather than a sample. Nothing was copied, moved, or extracted. Treat specific file paths as leads — spot-check before acting.
>
> **Scope.** **50 top-level packs, ~22 GB, ~135k files.** This project has imported **~101 MB / 64 FBX** of it. The catalog exists so the other 99.5% is a menu, not a mystery.

---

## 0. TL;DR

### The round-2 headline: the animation gap is closed, and there is prior art for the sphere

Round 1 concluded that **no humanoid animation set existed anywhere in the library**. That is now firmly false. The 32 packs added on 2026-08-10 are overwhelmingly animation — roughly **8 GB and ~7,000 Humanoid clips**, all retargetable onto Synty's Humanoid rig.

Two of them are prior art for the exact problem the character arc is solving:

- **`SuperCharacterController`** (bundled free inside ExplosiveLLC) — `up => transform.up` throughout, and it ships `Code/Examples/Gravity.cs` whose serialized field is literally named `planet`, plus a `SpaceZone.unity` sphere demo. Zero coroutines.
- **Malbers Animal Controller** — ships `IGravity { Vector3 Gravity {get;set;} Vector3 UpVector {get;} }`, which is structurally our `IGravityProvider`, and a 20-line radial-gravity driver.

### The moves worth making

| # | Move | Cost | Why |
|---|---|---|---|
| 1 | **Kevin Iglesias — Human Mega Animations** | M | **Base locomotion set.** 1,373 Humanoid clips, **zero rig import errors**, in-place by default with `[RM]` twins. 8-dir walk/run, 5-dir sprint, crouch, jump/fall/land, swim. Male + female mirrored. |
| 2 | **Read `SuperCharacterController` before writing more `CharacterMotor`** | S | Free, permissive, zero coroutines, and a working radial-gravity planet demo in the box. Its `BSPTree` also feeds the physics-bubble plan. |
| 3 | **Synty `PolygonFantasyHeroCharacters`** (selective, ~60 MB of 298) | S | Humanoid modular player character on `Generic_Basic.shadergraph` — the shader we already have. |
| 4 | **Synty `PolygonNature`** (76.7 MB) | S | 229 FBX with real LOD chains + wind shader. Cheapest high-value content in the library. |
| 5 | **`PolygonNatureBiomes` — the 3 un-imported biomes** | M | Alpine_Mountain, Arid_Desert, Enchanted_Forest map onto our empty/thin Tundra, Snow, Desert, Mountain slots. |
| 6 | **`Corals`** (~358 MB useful subset) | M | Ocean biome has **zero** scatter prototypes. 44 pre-assembled colony groups also prototype the clumping feature. |
| 7 | **`Simple_Activations` + `Loot_Anim_Set`** | S | The interaction verbs (activate, pick up, kneel-harvest) that the roadmap's "first interaction verb" needs. 128 MB combined, Humanoid, already in-place. |

### What still isn't here

Nothing in the library supplies **wildlife AI that works on a sphere**. Polyperfect's is mathematically broken (`Vector3.up` projection); Malbers' is mathematically correct but needs ground colliders and a NavMesh. Both wait on the physics bubble.

---

## 1. Baseline — what is already in this project

`Assets/AssetPacks/`, **4 packs, 64 FBX, ~101 MB.** Raw meshes + atlases only — no prefabs, no vendor materials, no vendor shaders (everything is re-materialed onto `Planet/PropLit` / `Scatter.shader`).

| Pack | FBX | MB | Contents |
|---|---:|---:|---|
| `PolygonGeneric` | 8 | 1.0 | 2 tree, 2 pine, 2 bush, 2 rock, flowers |
| `PolygonNatureBiomes` | 10 | 94.3 | Meadow_Forest / Swamp_Marshland / Tropical_Jungle slices only |
| `LMHPOLY_Vegetation` | 22 | 0.6 | flowers ×8, bushes ×4, mushrooms ×4, reeds ×2, 3D grass ×3 |
| `TEM_Vegetation` | 24 | 5.3 | grass patches ×5, flower patches ×3, lilies ×3, mushrooms ×4, plants ×5, bushes ×4 |

**68 scatter prototypes** in `Assets/Resources/Settings/Scatter/`, across **17 biomes**. **Zero animation assets. Zero character assets.**

> _Re-verified 2026-08-11 at `a0d22b4`. The **Lake biome** landed overnight on `character-controller-mvp` (`a34c843`…`57579ce`): new `Lake` + `LakeShore` biomes with 4 prototypes (cattails, reeds, rocks, wildflowers), `LakeMask` flood-fill, SlotBits raised 6→7. Tundra also picked up 2. Earlier "82 prototypes / Tundra 0" figures were wrong — 82 conflated prototype and biome `.asset` files._

### Biome coverage gaps (drives §3–§4)

| Biome | Prototypes | Gap |
|---|---|---|
| **Ocean** | **0** | **Still the only empty biome.** No underwater content exists. |
| Tundra | 2 | Thin |
| Desert | 2 (rock, dead tree) | No cactus, no bones, no dune props |
| Snow | 2 (pine, rock) | No snow-dusted variants |
| Mountain | 2 (pine, rock) | No cliffs, no alpine flora |
| Beach | 3 | No driftwood, shells, coastal debris |
| IceBog | 3 | Thin |
| Lake / LakeShore | 4 | New; shore ring done, **lily-pads-on-water deferred** |
| Grassland / Forest / Savanna / Steppe / Swamp / Taiga / Tropical / Scrub | 5–8 each | Adequate |

---

## 2. Verdict legend

| Verdict | Meaning |
|---|---|
| **IMPORT** | Drop in (selectively) and use as-is |
| **HARVEST** | Mine specific art/shader math out of it; do not import the runtime or the whole pack |
| **REFERENCE** | Study the technique; ship nothing |
| **SKIP** | Incompatible, redundant, or off-theme |
| **UNSURE** | Can't judge without a human opening it — see §12 |

For animation packs (§6) an additional axis applies: **Humanoid** (`animationType: 3`, retargets onto Synty) vs **Generic** (`animationType: 2`, does not retarget across skeletons).

---

## 3. Category A — Vegetation & scatter props

_The direct feed for `Assets/Resources/Settings/Scatter/`._

### A1. Synty `PolygonNature` — 76.7 MB, 229 FBX / 227 prefabs — **IMPORT (priority)**
`D:\Unity\Explore Assets\Assets\Synty\PolygonNature`

Best direct fit after NatureBiomes, and by far the cheapest per unit of value. Ships `Models/LODS` + `Materials/LODS` — **real LOD chains**, unlike most of the library.

- **Trees (66):** `SM_Tree_Pine_01/02`, `_Pine_Large_01/02`, `_Pine_Small_01/02`, `SM_Tree_PolyPine_01..03` (+`_Sparse_01..03`), `SM_Tree_Birch_01..04`, `SM_Tree_Birch_Dead_01`, `SM_Tree_Round_01..05`, `SM_Tree_Willow_Large/Medium/Small_01`, `SM_Tree_Swamp_01..04`, `SM_Tree_Dead_01..03`, `SM_Tree_Stump_01..04`, `SM_Tree_Log_01/02`, `SM_Tree_Twig_01..05`, `SM_Tree_Vines_01..04`, `SM_Tree_Generic_Giant_01`
- **Plants (42):** `SM_Plant_Grass_01..05`, `_Fern_01..03`, `_Bush_01..03`, `_Mushrooms_01..06`, `_FlowerPatch_01`, `_Flowers_01`, `_Reeds_01/02`, `_Lillypad_Large_01..03`, `_Undergrowth_01`
- **Rocks (30):** `SM_Rock_01..04`, `_Boulder_01`, `_Cluster_Large_01..06`, `_Pile_01..05`, `_CaveEntrance_01/02`, `_Wall_01/02`
- **Terrain features (33):** `SM_Terrain_Mountain_01..03`, `_GrassEdge_01..04`, `_Ground_Mound_Large_01/02`, `_Terrain_Ice_01`, `SM_River_Plane_01`, `SM_River_Plane_WaterFall_01`
- **Shaders:** `SyntyStudios_Trees` (wind, 24 mats), `SyntyStudios_LOD` (16 mats), `_Moss`, `_Vines`, `_Water`. 13 materials still on built-in shaders.

### A2. Synty `PolygonNatureBiomes` — 2,049 MB, 1,139 FBX — **IMPORT (cherry-pick)**
6 self-contained biomes. **3 imported, 3 not.**

| Biome | Size | Prefabs | Status |
|---|---:|---:|---|
| PNB_Meadow_Forest | 546 MB | 182 | 4 FBX imported |
| PNB_Swamp_Marshland | 421 MB | 144 | 3 FBX imported |
| PNB_Tropical_Jungle | 329 MB | 202 | 3 FBX imported |
| **PNB_Alpine_Mountain** | 246 MB | 125 | **none** → Mountain / Snow / Tundra |
| **PNB_Arid_Desert** | 132 MB | 152 | **none** → Desert |
| **PNB_Enchanted_Forest** | 375 MB | 164 | **none** → exotic/alien biome |

Un-imported highlights:
- **Alpine:** `SM_Env_Ice_Sheet_01/02`, `SM_Env_GroundCover_01..03`, `SM_Env_Bush_Flower_01`, `SM_Env_Fishing_Hole_01`, `SM_Prop_Canoe_01`, `SM_Prop_Campfire_01`, `FX_Aurora_Mesh_01/02`, `FX_Hawk_01`
- **Desert:** `SM_Env_Cactus_01..03`, `SM_Env_Crater_01..03`, `SM_Env_Lava_Pool_01`, `SM_Env_Bush_Bramble_01/02`, `SM_Prop_Bones_01..07`, `FX_Lava_Ember_01`, `FX_Dust_Blowing_01`
- **Enchanted:** `SM_Env_Dirt_Cliff_01..12`, `SM_Env_Fern_Koru_01/02`, `SM_Env_Branch_01..04`, `SM_Prop_Crystal_01..06`, `FX_Portal_01`, `FX_Fireflies_Large_01`
- Also un-imported in biomes we *do* have: `SM_Env_Grass_Short/Med/Tall_Clump_01..03`, `SM_Env_Grass_Large_01..04`, `SM_Env_Flowers_Flat_01..03`, `SM_Env_LillyPads_01..04`, `SM_Env_MossMound_01..03`, `SM_Env_Bush_Palm_01..04`, `SM_Env_Creeper_Vine_01/02`, `SM_Env_DriftWood_01..05`
- **89 terrain layers + 658 textures** — a large albedo/normal library usable for planet biome surface shading independent of the meshes.

### A3. Toon Fantasy Nature — 1.7 GB, 214 FBX / 208 prefabs — **IMPORT (selective)**
"TOON Series", same author as Toon Enchanted Meadow (`TFF_` prefix). Best-engineered stylized pack in the library:

- **URP-native** custom shaders (Amplify 1.9.9.4): `Toon/TFF_CustomToon`, `TFF_CustomToonVegetation`, `TFF_CustomToonOutline`, `TFF_CustomGrass`, `TFF_Board_Cutout`, `TFF_ToonWater`, `TFF_ToonFire`
- **Atlas materials** (`TFF_Atlas_1A_D`, `_Vegetation_1A_D`, `_Billboards_1A_D`) — ideal for re-materialing onto `Planet/PropLit`
- **36 of 61 tree prefabs have LODGroups**
- **Ships pre-baked tree billboards**: `Prefabs/Decals/TFF_Board_*` (13) + dedicated cutout shader — direct parallel to our impostor tier; worth an A/B against our own baker
- Content: Trees 61 · Plants 38 · Rocks 20 · **Water Plants 20** · Mushrooms 18 · Billboards 18 · Structures 14 · Props 11
- Names: `TFF_Aspen_Tree_01A-03D`, `TFF_Oak_Tree_01A-C`, `TFF_Pine_Tree_01A-05C`, `TFF_Tree_Fallen_01A`, `TFF_Bush_01A-03D`, `TFF_Fern_01A/02B`, `TFF_Flower_Patch_01A-01E`, `TFF_Grass_Patch_01A-02D`, `TFF_Reed_01A/02A/03A`, `TFF_Vine_01A-C`, `TFF_Glowing_Mushroom_01A/B`, `TFF_Mushrooms_01A-12A`, `TFF_Lotus_Flower_01A`, `TFF_Water_LillY_Leaf_01A`, `TFF_Glowing_Lilly_01A-02C`, `TFF_Rock_Large/Medium/Small_01A`

⚠️ **Style check needed** — toon-outlined, more saturated than Synty flat-shaded. See §12.

### A4. Toon Enchanted Meadow — remainder — **IMPORT (selective)**
814 MB, 310 prefabs. We took **24 meshes**; ~286 prefabs left. Worth grabbing:

- **All 12 trees** `TEM_Tree_01A`–`TEM_Tree_06B` (⚠️ **no LODGroups** on any — we'd build our own)
- **`TEM_Mushroom_Clump_01`–`_06`** — pre-authored mushroom **colonies**; the clumping feature already modelled
- 18 rocks `TEM_Rock_Large/Medium/Small_01A–04B`
- `TEM_Ivy_01A/02A`, `TEM_Leaf_Coverage_01A-03A` (ground litter decals), `TEM_Leaf_01A-04A`
- The `B` colour variants of everything already imported
- 14 URP shaders (Amplify 1.9.8.1) incl. `TEM_CustomStoneSpherical` — a **spherical-projection** stone shader

Skip: 140 building-kit prefabs, 25 furniture, 18 food, 16 blacksmith props (see §13).

### A5. `Fantastic Nature Pack` — 322.6 MB, 163 prefabs / 126 FBX — **HARVEST** ⭐ round 2
`D:\Unity\Explore Assets\Assets\Fantastic Nature Pack` — Tidal Flask Studios.

**This is the extracted URP variant — the round-1 UNSURE is resolved.** Five independent proofs: filename suffix `_U6-1` matches `URP_FANTASTIC_Nature_Pack_U6-1.unitypackage` exactly (the Standard archive is `_U6`); all 5 shaders declare `Tags { "RenderPipeline"="UniversalPipeline" … }`; all have a `"LightMode"="UniversalForward"` pass plus `ShadowCaster`/`DepthOnly`; 20 of 31 material refs point at URP/Lit `933532a4fcc9baf4fa0491de14d08ed7`; only the 2 skybox mats use the built-in GUID.

- Shaders: `TidalFlask/Foliage Wind URP {Advanced, Advanced Lit, Simple, Simple Lit} FNP U6-1`, `TidalFlask/Water URP U6-1`
- Trees: `P_ENV_TREE_v1_01_wood_01…v4_07`, `_trunk_v1..v3`, `_stump_v1/v2` (+`_shrooms`), `_root_01..03`
- Plants: `P_ENV_PLANT_bush_v1..v4`, `flower_v1_01..04`, `leaf_v1..v6`, `shroom_v1..v3` (+`_cut`), `tendril_01`, `waterlily_01/02`, `P_GRASS_v1_01..v2_01_FNP`
- Stones: `P_ENV_stone_01..15`
- **Camping prop set:** `P_PROP_campfire`, `_holder`, `_poles`, `_rack`, `tent`, `sleepingbag`, `woodlog`, `woodpile`, `fishingrod`, `pot`, `pan_01`, `knife`, `bowl_wood_01`, `FOOD_bread_01/cheese_01/fish`, `P_set_camping`
- FX: `P_FX_fire`, `_leaves_FNP`, `_steam`, `_water_FNP`

Take `3d/`, `2d/`, `prefabs/{plants,stones,trees,props}`. Skip the demo scenes (they drag in a `TerrainData` + lighting settings + post-process profiles). The camping props pair naturally with `Survival_Animations` (§6.4) and `Sleep_Anim_Pack`.

### A6. LMHPOLY Low Poly Nature Bundle — remainder — **IMPORT (Rocks) / cherry-pick rest**
707 MB, 20,376 non-meta files. Four bundled packs. The 10.8k prefab count is **combinatorial variants, not distinct models** — every asset ships as `_LOD`/`NoLOD` × `With_Bottoms`/`No_Bottoms` × `Capsule_Colliders`/`Mesh_Colliders` × snow/plain × colour, all on one shared atlas.

| Module | Size | Verdict |
|---|---:|---|
| **Rocks** (1,496 prefabs) | 48 MB | **HIGH** — `Rock_Flat/Block/Blocks_s/m/l`, `Rock_Flat_crk_*`, `Rock_Arch`, `Crystal_s/m`, `Crystals_s/l`, `Brick_Pile`. Atlases: `Rocks_Texture_Atlas_Moss.png` + `_Snow.png`. Best size-to-value ratio in the bundle. |
| **Trees** (5,315 prefabs) | 187 MB | **MEDIUM-HIGH** — `Acacia_Log_01..08`, stumps, firewood, `Palm_Leave_*`, `Pine_Spikes_*`, `Coconut`, `Apple`; **snow twin of every asset**; `Trees_Texture_Atlas_Autumn.png` = free seasonal recolour |
| **Vegetation** (661) leftovers | 37 MB | **MEDIUM-HIGH** — `Cactus_s/m/l` + `Cactus_a/b/c_m` (**fills the Desert gap**), `Bush_a`–`_h`, `Grass_a`–`_h`, `GrassPlane_a/b`, `Mushroom_a`–`_k` (11 species), `Plant_a`–`_e` |
| **Modular Terrain** (3,337) | 423 MB | **SKIP** — hand-authored modular *mesh terrain* kit. Architecturally orthogonal to a procedural cube-sphere. |

⚠️ **Zero shaders in 707 MB.** All 89 materials are Built-in Standard (Specular). Fine for us — we re-material anyway. LODs ship as separate `*_LOD` prefab variants, not LODGroups.

### A7. Polyart "Dreamscape Series" — 1.2 GB — **HARVEST (art) + REFERENCE (tech)**
4 modules: Meadows (461 MB), Mountains (497 MB), SharedResources (159 MB), Campsite (86 MB).

- Content: `Prefab_Birch_01..05`, `Prefab_TreeLarge_01..04`, `Prefab_Conifer_01..11`, `Prefab_Bush_01..09`, **`Prefab_FlowerField_01/02`**, `Prefab_Flower_01..04`, `Prefab_Grass_Group_01/02`, `Prefab_Mushroom_01..06`, `Prefab_RockFormation_01..12`, `Prefab_Cliff_01..05`, `Prefab_Rock_01..13`, `Prefab_RuneRock_01/02`
- **Round-2 nuance that defuses the round-1 concern:** the master shaders are **already dual-target**. `SharedResources/ShaderGraph/Master Materials/Props.shadergraph` and `Foliage/Foliage.shadergraph` each contain **both** `UniversalTarget`+`UniversalLitSubTarget` **and** `BuiltInTarget`+`BuiltInLitSubTarget`. Of 169 materials: 79 on `Props.shadergraph`, 14 on `Foliage.shadergraph`, ~20 on other SharedResources graphs — so the bulk already renders under URP. Only 38 built-in default-resource mats (particles/skybox) would be fixed by the unextracted archives.
- Look is painterly/PBR-stylized, **not** Synty-flat — biggest style-clash risk in the library
- Its real value is the tech — see §8.

### A8. `Fantasy Adventure Environment` — 273 MB, 76 prefabs — **HARVEST (technique first)** ⭐ round 2
`D:\Unity\Explore Assets\Assets\Fantasy Adventure Environment` — Staggart Creations v1.5.7 (`ASSET_ID = "70354"`).

Vegetation + rocks/cliffs + atmospheric FX; zero architecture. Art is **semi-realistic painterly fantasy forest** — hand-painted diffuse with real normal maps, LOD chains, billboard trees. **Not Synty-flat.**

- Trees (14): `FAE_Birch_A/B/C`, `FAE_Palm_A/B/C`, `FAE_Pine_A`, `FAE_Spruce_A/B/C`, `FAE_Tree_A/B/C`, `FAE_Willow_A/B`
- **Rocks (10): `Cliff_A`…`Cliff_F`, `RockCluster_A`…`RockCluster_D`** — the most style-neutral part
- Vegetation (37) incl. a full `Ivy_{Climbing,Cylinder,GroundCover,Hanging_A/B,Ledge}` set
- Effects (12): `DustMotes`, `FallingLeafs`, `Fireflies`, `FogSheet`, `RollingFog`, `Snow`, `Sunshafts`, `WindTrails`, `WindController`, `FoliageBender`
- **URP is the active mode** — zero materials reference the 12 built-in `.shader` GUIDs; all point at `Shaders/URP/*.shadergraph`

**The genuinely valuable part is a technique, not the art:** `PigmentMapGenerator` + `TerrainUVUtil` bake a top-down colour map so rocks and foliage tint to the ground beneath them — a direct answer to scattered props reading as "pasted on" against biome colour. Crucially `TerrainUVUtil` has `enum Workflow { None, Terrain, Mesh }` and a `MeshRenderer[] meshes` path, so it **survives having no Unity Terrain**. Reimplement against our cube-sphere UVs; don't import the MonoBehaviour (it's `[ExecuteInEditMode]` and writes a global `_TerrainUV`, a `ShaderGlobalIds` collision risk).

⚠️ **Highest-risk import in the library** — see §11.

### A9. Synty `PolygonAdventure` (24 MB) — **HARVEST (snow variants only)**
Bush/flower/grass/hill set overlaps heavily with Nature + NatureBiomes. The differentiator is pre-authored **snow variants**: `SM_Env_HillSnow_01..04`, `SM_Env_Hedge_01_Snow`, `SM_Bld_Hut_01_Snow`.

### A10. Procedural Worlds — Synty Nature Spawner Pack (789 MB) — **mostly SKIP**
**440 Synty POLYGON Nature prefabs** are usable. Everything else is dead weight: every `.asset` is a **Gaia** `BiomePreset`/`SpawnerSettings` operating on Unity `TerrainData`, deserializing to broken `m_Script` GUIDs without Gaia. Shipped materials are **HDRP-configured**. Since the 440 prefabs largely duplicate `PolygonNature` (A1) which we can take cleanly — **prefer A1, skip this.**

---

## 4. Category B — Ocean & underwater

_The Ocean biome has **zero** scatter prototypes. Biggest uncovered surface on the planet._

### B1. `Corals` — 1.1 GB (~358 MB useful) — **IMPORT (selective)**
Publisher unidentified (no readme; Amplify boilerplate only).

- **79 prefabs**, 4 FBX (`Corals.FBX`, `CoralGroups.FBX` 34 MB, `CoralRocks.FBX`, `Seaweeds.FBX`)
- 35 singles: `Coral1_1`…`Coral_16_2`, `CoralRock1..4`, `Seaweed`, `Seaweed_G`
- **44 pre-assembled colony prefabs** in `Prefabs/Groups/`: `Coral1_G1/G2`, `Coral_13_G1..G4`, `Coral_14_G1..G4`, `Coral_6_Sphere_G1/G2`, … — **exactly the clumping/colony pattern we want to build**, pre-authored
- **URP-tagged** shaders (24, Amplify). Texture set is `_diff`/`_normal`/`_gloss`/**`_illum`** — emissive masks, which suits bioluminescent deep-water corals
- Bonus shaders in the same folder: `ASE_StandartLava`, `ASE_StandartSnow`, `ASE_StandartSSS`/`SSS2`, `ASE_StandartInnerGlowIce`, `ASE_StandartCutoutMoss`

⚠️ **703 MB of the 1.1 GB is `__CubemapsShaders/`** — a shared-library dump with nothing coral in it. Import ~1/3.
⚠️ 2K PBR, not flat toon — see §12.

### B2. Aquatic wildlife — Polyperfect (13 species) — see §5.2
`Whale, Orca, Dolphin, Shark, Seal, Walrus, Octopus, Squid, Jellyfish, Seahorse, Starfish, Crab, Fish` — rigged and animated, including `Swim` clips. A ready-made ocean population that pairs with B1.

### B3. Toon Fantasy Nature water plants (20 prefabs) — **IMPORT**
Shoreline/shallow-water coverage in a style that *does* match: `TFF_Lotus_Flower_01A`, `TFF_Lotus_Leaf_01A`, `TFF_Water_LillY_Leaf_01A`, `TFF_Glowing_Lilly_01A-02C`, `TFF_Reed_01A/02A/03A`.

### B4. Synty PNB Swamp/Jungle water props — **IMPORT (cherry-pick)**
Already-owned biome folders contain un-imported `SM_Env_LillyPads_01..04`, `SM_Env_DriftWood_01..05`, `FX_Crabs_01`, `FX_Frogs_01`.

### B5. Swim animation sources — see §6
`Kevin Iglesias` ships a 10-direction `Swim01_*` set plus `SwimIdle01`/`SwimDrown01`; `EverydayMotionPack` and `Frank Platformer 2` also have swim. Relevant to roadmap #20 (ocean traversal).

---

## 5. Category C — Character & creature models

### C1. Synty `PolygonFantasyHeroCharacters` — 298 MB — **IMPORT (selective) — the player character**

- **Humanoid rig** (`animationType: 3` + `humanDescription`) on `Models/ModularCharacters.fbx`
- **Fully modular**: 719 SkinnedMeshRenderers on one shared skeleton, plus 720 static `MeshRenderer` copies for icons/bone-attachment
- Slot scheme `Chr_<Slot>_<Gender|Variant>_<NN>`: `Torso` 29 M / 29 F, `Hips` 29/29, `Head_Male` 23 / `Head_Female` 23, arms/hands/legs 18–21 per side per gender, `Hair_01–38`, `FacialHair_Male` 18, `BackAttachment_01–15`, `ShoulderAttachLeft/Right_01–21`, `ElbowAttach_01–06`, `HipsAttachment_01–12`, `KneeAttach_01–11`, `HelmetAttachment_01–13`, `Ear_Ear` ×3 (elf)
- Data-driven: `Gender {Male, Female}`, `Race {Human, Elf}`, `SkinColor {White, Brown, Black, Elf}`
- 54 weapons: Sword / Sword_Large / Sword_Rapier / Sword_Small (+ `_Cover`), Axe, Dagger, Mace ×2, Staff ×2, Shield ×20, Shield_Long ×16, Shield_Buckler, Joust ×3, ThrowingKnife
- **URP Shader Graph, already partly ours:** `Shaders/POLYGON_CustomCharacters.shadergraph` for 21 hero materials; the other 12 use `Generic_Basic.shadergraph` — **the exact shader already imported with PolygonGeneric**
- **35 PNGs for 720 parts** — 4 atlases × `_A/_B/_C` + 6 grayscale masks driving `_Color_Primary`, `_Color_Secondary`, `_Color_Metal_*`, `_Color_Leather_*`, `_Color_Skin`, `_Color_Hair`, `_Color_Scar`, `_Color_BodyArt`. One material, fully tintable.
- Only script is `Scripts/CharacterRandomizer.cs` — a **demo part-toggler, not AI**. No NavMesh, no Terrain, no coroutines, **nothing that fights radial gravity**.

⚠️ **Do not bulk-import.** Each of the 120 `Chr_FantasyHero_Preset_N.prefab` is a **flat 1.9 MB prefab holding the whole 836-GameObject rig with 700 objects disabled** — ~230 MB of the 298. Import `Models/FixedScale/ModularCharacters.fbx`, the shaders, the 35 textures, and a handful of presets.
⚠️ **Use the `FixedScale` FBX** (metric scale) — our motor works in real metres.
⚠️ Its `Demo.controller` points at a dangling external Synty animation-pack GUID. Supply clips from §6.

### C2. Polyperfect Low Poly Animated Animals — 679 MB — **IMPORT (art) / rewrite AI — the wildlife**

- **68 distinct species**, ~99 prefabs. Land: Bear (Grizzly/Polar + Wild), Wolf, Fox, Deer, Boar, Buffalo, Camel ×2, Capybara, Reindeer, Rabbit, Squirrel, Meerkat, Goat, Sheep, Cow, Pig, Horse, Zebra, Giraffe, Elephant M/F, Rhino, Hippo, Lion M/F, Tiger, Panda, Gorilla, Chimpanzee, Anteater, Tapir, Beaver, Rat, Snake, Spider, Crocodile, Ostrich… Birds: Eagle, Vulture, Parrot, Tucan, Flamingo, Penguin, Seagull, Dove, Goose, Hen, Rooster, Chick. **Aquatic ×13** (see B2).
- **All rigged.** `SKM_<Animal>_Rig.fbx` + `SKM_<Animal>_Animations.fbx`. `animationType: 2` (**Generic**) with `avatarSetup: 1` → per-animal Avatar.
- **536 embedded clips + 81 standalone `.anim` + 74 Animator controllers**. Vocabulary: `Walk` ×47, `Death` ×47, `Attack` ×45, `Run` ×42, `Idle` ×38, full `Idle↔Walk↔Run` transitions ×18–20 each, `Eat` ×9, `Sleep` ×8, `Fly` ×7, `Swim` ×6. Specials: `Howl`, `Bark`/`Sit`/`Stand`, `Slither`, `ChestHit`, `LookOut`/`Jump_Up`, `Scared`.
- **66 `STAT_*.asset` ScriptableObjects** — pure data (`STAT_Wolf`: dominance 10, stamina 30, power 25, toughness 50, agression 60…). **Portable as-is.**

⚠️ **The AI does not survive a sphere.** `Common_WanderScript.cs` (848 lines) requires `NavMeshAgent` + `CharacterController`, and hard-codes **`Vector3.ProjectOnPlane(..., Vector3.up)` in at least 4 places** (`:517`, `:531`, `:556`, `:564`). `Common_SurfaceRotation.cs` raycasts against a layer literally named `"Terrain"`. Pack ships baked `Navmesh/` and Unity `Terrains/` folders. → **Take** rigs, 617 clips, 74 controllers, 66 STAT assets. **Write** our own spherical wander driver — or evaluate Malbers (§7.2) once the physics bubble lands.
⚠️ **Built-in pipeline** — 174 materials on `Standard`/`Standard (Specular setup)`. Magenta in URP until upgraded.
⚠️ **Per-creature textures, no shared atlas** — 159 PNGs. Many species = many draw calls.
⚠️ **Ships no vocalizations** — pair with `Farm Animal Sounds` (§10.1), which covers 14 of these species.

### C3. Synty `PolygonKnights` characters — 34.7 MB — **MEDIUM (NPC filler)**
5 humanoid characters × 4 team colours: `SM_Chr_Knight_01/02/03`, `SM_Chr_Soldier_01/02`. One 1.56 MB `Characters.fbx`, `animationType: 3` **Humanoid**, ~1.5–3 k tris. **Zero `.cs` files** — nothing to port. All 14 materials on `Generic_Basic.shadergraph`, **the shader we already have → literal drop-in.** Not modular; colour-swap only. Its environment half plus `PolyKnights_Snow_To_Grass_Swap_01.mat` is arguably the bigger value.

### C4. `PolygonHorse` — 2 MB — **UNSURE / LOW**
One horse, 10 coats + unicorn variant. Rigged Generic, 89 bones, IK helpers. **Broken as shipped:** zero clips, and the Animator controller GUID plus **22 `m_Script` GUIDs are all dangling**. Importing produces missing-script warnings and a dead Animator. Note **Malbers `Horse AnimSet Pro` (§6.6) ships 144 horse clips** — but on its own Generic skeleton, so retargeting would be required either way.

### C5. Demo dummies shipped with animation packs — **all LOW**
Nearly every animation pack ships a rigged character. All are grey/untextured demo mannequins with no shippable value: `Android_SkeletalMesh.fbx` (RamsterZ, byte-identical across 8 packs), `RPG-Character.FBX`, `HumanM/F_Model.fbx` (Kevin Iglesias), `UnityMan.fbx`, `PolygonmakerRig_1-2_MeshBasic`, `rig_MCUnity.fbx`, `Crafter.FBX`. Two partial exceptions: Opsive's `Atlas_OmniAnimation.fbx` (a decent sci-fi character, wrong genre) and Frank Platformer 2's stylized cartoon character.

---

## 6. Category D — Animation ⭐ round 2

_~8 GB across ~27 packs. Everything below is **Humanoid** unless flagged, so it retargets onto `PolygonFantasyHeroCharacters`._

### 6.0 Cross-cutting facts

- **Clips are embedded in FBX, one clip per file** (Mixamo-style) in nearly every pack — not `.anim` assets. Exceptions: `EverydayMotionPack` (143 pre-extracted `.anim` carrying real humanoid muscle curves), `Frank Platformer 2` (309 clips across 32 multi-take FBX).
- **Controller counts are misleading.** Most packs ship one trivial single-state `.controller` per clip for their demo scene — Polygonmaker has 401 of which only **2** are real, Vendors_and_Customers has 246 throwaways. Ignore them.
- **In-place vs root motion matters** because `CharacterMotor` computes its own velocity. Preference: in-place. Coverage varies a lot — see the table below.
- **Almost every pack is Built-in pipeline** for its handful of demo materials. Irrelevant: you take the `Animations/` folder and discard `Materials/`, `Models/`, `Demo/`.

### 6.1 The base locomotion decision

| Pack | Size | Humanoid clips | In-place | Rig errors | Verdict |
|---|---:|---:|---|---|---|
| **Kevin Iglesias** | 884 MB | **1,373** | **default**, +148 `[RM]` twins | **zero** | **PRIMARY** |
| Opsive OmniAnimation | 133 MB | 150 (74 clips ×2) | no — RM only, 1 bulk checkbox | zero | PRIMARY (alt) |
| Universal_Traversal_Anims | 1,562 MB | 669 (of 1,279) | full `In_Place_Versions` mirror | **259 metas** | SUPPLEMENT |
| RPG Character Mecanim | 864 MB | **1,248** | code-driven, no RM pairs | zero | PRIMARY (clips) |
| Polygonmaker | 1,274 MB | 591 | **133 `_inplace` pairs** | zero | SUPPLEMENT |
| Grruzam Archer | 696 MB | 456 (+8 broken) | `Root/` vs `Inplace/` + `_ZeroHeight` | 8 files | SUPPLEMENT |
| EverydayMotionPack | 251 MB | 246 | default, + `root_motion/` twins | zero | SUPPLEMENT |
| Female_Anim_Starter_Pack | 176 MB | 183 | in-place only, no choice | zero | SUPPLEMENT |
| Frank Platformer 2 | 86 MB | 34 FBX / **309 clips** | both, as sibling takes | zero | SUPPLEMENT |
| Threepeat Parkour 1+2 | ~300 MB | 160 FBX | `IP-` twins | — | SUPPLEMENT |
| MoCapCentral | 607 MB | 366 | 13 `_NoRM` only | zero | LOW (idles) |

**Recommendation: `Kevin Iglesias` — Human Mega Animations Pack** as the base set.
`D:\Unity\Explore Assets\Assets\Kevin Iglesias\Human Animations\Animations\{Male,Female}\Movement`

1. **Rig risk is zero** — 1,373 clips at `animationType: 3`, `avatarSetup: 2` against its own bundled avatars, and **not a single `rigImportErrors` entry**. Contrast Universal_Traversal_Anims, where 259 metas carry `Transform 'root' not found in HumanDescription`.
2. **In-place is the default, root motion is opt-in** — `loopBlendPositionXZ: 1, loopTime: 1` out of the box, with 148 `[RM]` twins in sibling folders.
3. **Coverage matches the actual feature list.** 8-dir walk + 8-dir run + 5-dir sprint + 6-dir strafe walk/run; `Crouch01_Idle` + 8-dir crouch walk + 6-dir crouch strafe + crouch turns; `Jump01 - Begin` / `Jump01` / `Fall01` / `Jump01 - Land` — which maps 1:1 onto a jump state machine with an **indefinite airborne loop**, important when radial gravity means air time varies. Plus 10-dir swim.
4. **Male + female mirrored** (653/654) matches Synty's modular gender variants.
5. **Ships 8 reusable avatar masks** (Body Full/Upper/Head/Arms/Hands + 3 hand-pose masks) and `SpineProxy.cs`, an upper-body/spine layer helper — directly useful for mouse-look torso split.
6. Aggregates 4 products: Human Mega Animations Pack (v2.0), Hand Poses, Human Basic Motions, Human Crafting Animations.

**Then layer on, in priority order:**

- **`Opsive/OmniAnimation` for start/stop polish** — the only pack with **per-foot** `StartWalkLeft/Right` … `StopSprintLeft/Right` (20 clips) and 45°/90°/180° turn-in-place, for every gait including crouch. 74 immaculate clips, no filler. Costs one bulk re-import (tick Bake Into Pose → Root Transform Position XZ across 74 metas). Highest-value single addition to KI. Gaps: no fall loop, no land, no swim.
- **`Frank Platformer 2` for air states** — `Jump_Fall_Loop_01`–`_06`, six `Jump_0N_Start`/`Land` pairs, `Jump_Double_01`–`_05`, `Jump_Wall_01`–`_04`, `Dash`, `Dive`, `Roll`, `Jump_Bounce`. Precisely the states a radial-gravity motor needs and that KI/Opsive lack. Also an 8-way `Look_*_Oclock` aim space, plus climb/ledge/swim. ⚠️ stylized short proportions — retarget with foot-IK.
- **`Universal_Traversal_Anims` for traversal vocabulary** — the only source of climb / ledge / vault / wallrun / ladder / pole / zipline / balance-beam. See 6.2.
- **`Threepeat` Parkour for acrobatic traversal** — see 6.3.
- **`MoCapCentral` MC_Idles** later, for ambient NPC idle variety only — 22 idle families (`ArmsCrossed`, `Cold`, `Fidget`, `Smoke`, `Stretch`, `Tired`, `WallLean`…), but only 33 of 380 clips are locomotion and every one is a character quirk (orc, drunk, injured, swagger). Cannot be a base set.

### 6.2 `Universal_Traversal_Anims` — 1,562 MB — **SUPPLEMENT (high value)**
RamsterZ "Ultimate Traversal Anims" v2.0. **Mixed rig — the split is clean by folder:**
- **Humanoid, use these (669):** `Art/Animations/*.fbx` (289), `In_Place_Versions/` (269), `New Animations 2.0/` (86), `New Anims 1.1/` (18), `Updated Anims 1.1/` (4)
- **Generic, ignore (610):** the entire `Art/Animations/Legacy/**` tree — Generic duplicates of the same clips

Unique vocabulary nothing else has: `Traversal_Wall_Climb_{8 dirs}` (+ `_InwardCorner_`, `_OutwardCorner_`), `Traversal_Ledge_Climb_*`, `Traversal_Ladder_Climb_{Up_Start,Up_Loop,Down_*,Idle,End_toPlatform}`, `Traversal_Pole_Climb_*`, `Traversal_Climb_Leap_{Up,Left,Right,Behind}`, `_GripSlip_{Start,Loop,End}`, `Traversal_Fence_91cm_{JumpOver,CatJump,SitSpin}`, `Fence_345cm_ClimbOver`, `Traversal_Movement_WallRun_{Left,Right}_{Start,Loop,End}`, `Traversal_BalanceBeam_*`, `Traversal_ZipLine_*`, `Traversal_BarSwing_*` (~20), `Traversal_NarrowSpace_SideStep_*`, `Traversal_LedgeWalk_*`, slides, backflips, cartwheels, `Traversal_AimSpace_*` (9, 2D blend-tree ready). No swim.

⚠️ **259 metas carry a live rig error**, all in the root-motion set: `Copied Avatar Rig Configuration mis-match. Transform 'root' not found in HumanDescription.` The `In_Place_Versions/` set imports clean — **take from there.**
⚠️ Import settings are inconsistent: `In_Place_Versions/` has `loopTime: 0`, so you must tick Loop Time on the looping clips.

### 6.3 `Plugins\Threepeat` — Parkour Animations 1+2 — **SUPPLEMENT** ⭐ misfiled
**Not a plugin.** Threepeat Games' parkour mocap sitting in `Plugins/` by accident, and the bulk of that folder's 329 MB. **160 FBX**, each with an `IP-` in-place twin: `kipup-backwards-lyingback-to-idle`, `drop-high-reversegainer-sprint-to-fallfwd`, `landing-hardfrontroll-fallfwd-to-sprint`, `ledge-grab-braced-{idle,drop,drop180,jump,switchsides}`, `vault-low-over-kong-long-sprint-to-sprint`, `sprint-to-wallplant-forwardleap-then-backflip`. 7 demo scenes, `ParkourAnimationsOverview.pdf`. 9 demo-harness scripts with no asmdef — leave them.

### 6.4 Interaction & survival verbs — **NEAR-TERM**

These are the packs that map onto verbs the roadmap actually names. All Humanoid, all RamsterZ, all already in-place.

| Pack | Size | Verb coverage |
|---|---:|---|
| **`Simple_Activations`** | 33 MB | `Activate_Wall_ButtonPush`, `_FlickSwitch_Up/Down`, `_LargeLever_Pull/PushUp`, `_SlideLever_*`, `_WheelValve_Open/Close`, `_KeyTurn_DoorKnob` (+`_WalkThrough`), `_CardSlide`, `_HandScanner`, `Activate_Floor_TurnWheel_*`, `_Box_Push/Pull`, `Activate_Rope_GroundPull` (Start/Loop/End). **Cheapest import on the list and the least genre-contaminated.** |
| **`Loot_Anim_Set`** | 95 MB | `Loot_FloorPickUp_BendOver_Left/RightHand`, `_Kneel_*`, **`_Kneel_HarvestItem`** (literally foraging), `_Inspect_Enter/Loop/Keep/Exit` (a proper hold-item cycle), `Loot_{Low,Mid,High}Shelf_GrabItem`, `_TableTop_*`, `_Hole_ArmReach_GrabItem`, `_Generic_Rummage_Standing/Crouching`, chest/dresser/locker/trashcan/fridge, corpse looting, `Loot_PickPocket` |
| **`Survival_Animations`** | 107 MB | **dig/build half:** `Survival_Build_Shoveling`, `_PickAxe_LowHeight/MediumHeight`, `_TreeChop_Start/Vertical_Loop/Horizontal_Loop/Idle/Exit`, `_Build_Hammering_Floor/Wall`, `_Build_Crafting_GenericMovements`, `_Foraging_BerryBush`. **flavour half:** campfire suite, fishing, `_Drinking_GroundWater`, `_Healing_Bandage*`, `_Skinning_Ground`. Best thematic fit for "16 biomes, oceans, weather, exploration" — but cherry-pick. |
| `Sleep_Anim_Pack` | 60 MB | `Sleep_Tent_Enter/SleepLoop/Exit/QuickExit`, `_FloorBed_*`, `_Bed_*`, `_Sofa_*`, `_FloorSitLean_*`. Every set has a **QuickExit** (wake-on-danger). Grab the day a rest/time-skip mechanic exists, not before. |
| `Crafting Mecanim` (ExplosiveLLC) | part of 864 MB | 130 clips: `Gather`, `Gather-Kneeling`, `Dig-Start/Idle/Scoop/Finish`, `Fishing-*`, `Chop-*` (+`-Upper` partial-body), `Carry-Pickup/Idle/WalkForward/Putdown/Throw`, `Cart-Push-*`, `Climb-*`, `Crawl-*`. Plus **19 usable low-poly tool props** (Hammer, PickAxe, Shovel, Sickle, FishingPole, Cart, Ladder…). |

### 6.5 Combat, stealth, boss — **FUTURE**

The project has no combat, inventory, stealth, or enemy AI. These are speculative scope; importing now is assets aging against a design that doesn't exist.

- **`RPG Character Mecanim`** (1,248 clips) — deepest combat/traversal/emote library anywhere: `Sword-Attack-L1..L7`/`R1..R7`, blocks, dodges, `DiveRoll`, hit-reacts, deaths, revives, `Unarmed-Cast-Dual-AOE1`, bow aim/pull/fire, `Ledge-Grab/Climb/Drop`, `Crawl-*`, `Relax-Talk1..8`, `Relax-Sit-*`, sheath/unsheath per weapon set. Weapon sets: Armed 162, Unarmed 122, 2Hand-Staff 105, 2Hand-Spear/Sword 99 each, Bow/Crossbow/Axe 94 each, Shield 57, Swimming 9.
- **`Polygonmaker`** (591 clips, **zero C#**, **URP/Lit materials — the only anim pack that imports clean into URP 17.6**) — Locomotion 145, Undead 74, Hit 60, Archer 48, Death 44, Creatures 41, Interactions 39, Warrior 35, Floating 34, Fight-Street 27. Strong NPC/enemy coverage incl. `Anim_Zombie@ground-idle/move/attack/standup`. ⚠️ Creature/Undead clips target an **Ogre skeleton**, not the humanoid one. Rig is UE4-mannequin-derived — expect slight forearm-roll drift.
- **`Grruzam Archer`** (456 clips) — best ranged set anywhere: phased `Attack_Aim_A_Stand_45_1_Start` → `_2_Aim_Loop` → `_3_Shoot_Loop` → `_4_End`, `Attack_ChargeShot_*`, `Attack_RapidShot_*`, `Attack_JumpShot_Bwd`, 44 `Turn_ALL` clips, 20 `Move_To_Move` transitions, knockdown/getup taxonomy. ⚠️ **8 clips misconfigured as Generic** at `Animation/Archer/1_Movements/2__Walk/Inplace/Archer@Walk_*_Root.FBX` — flip to Humanoid+CopyFromOther on import, or use the `Root/` copies.
- `One_Handed_Sword_Attacks_and_Finishers` (136, half `_Mirrored`) — includes an 8-way *armed-stance* locomotion set, wrong silhouette for an unarmed explorer
- `StealthFinishers_Knife_and_Hand` (98, 58 paired `_Att`/`_Vic`) — needs enemy AI + paired-animation sync + a detection model
- `Vampire_Boss_Set` (62) — single-boss kit, incl. 12 fly clips
- `Flying_Mage_Volume2` (143) / `Script_Wizard_AnimSet` (143), publisher wemakethegame — both ship **no character mesh**, full `_inplace` twin sets, and complete 8-way walk+run cores. `Script_Wizard_AnimSet` has the most complete locomotion core of the small packs (idle, 8-way walk, 8-way run, 4-phase jump, dash, dodges) — **fallback base set** if KI is ever unavailable.
- `Tactical_Hand_Signals` (74) — **LOW**, modern-military squad comms locked into rifle/pistol grips. Genre mismatch that won't improve.

### 6.6 Creature & mount animation

- **Malbers `Horse AnimSet Pro`** — Horse 144 clips / Rider 89 / Wings 19. **Generic rig** (horse), Humanoid (rider). Gaits `H_Walk/Trot/Trot_Pace/Amble/Canter/Gallop/Sprint` each with `_Left/_Right/_135/_180/_Strafe`, plus `H_Jump_*`, `H_Fall_*`, `H_Swim_*` (9), `H_Fly_*` (19, pegasus), `H_Death*`, `H_Get Hit_*`, `H_Eat/Drink/Sleep/Neigh`, `H_Walk_Up/Down/_Incline_Decline`. **16 `_IP` in-place duplicates.** Complete mount/dismount + carriage system (§7.2).
- **Polyperfect** — 617 Generic clips across 68 species (§5.2).

### 6.7 Social / NPC gestures

- **`Conversation Gestures Pack`** (199 MB, 42 clips) — **HARVEST**, best signal-to-noise of the social packs: pure Humanoid clips, **zero `.cs`**, no controllers, no material debt. `Angry_01/02`, `Bored_01`, `Converse_01..06`, `Listen_01_Standard/_02_Nodding/_03_Shake_Head/_04_ArmsCrossed`, `Pointing_01/02`, `Scolding_01/02`, `Thoughtful_01..03`, `Shy_01/02`, `Tired_01/02`. Speaker+listener paired.
- `Dialogue_Anims` (100 MB, 107 clips) — same content class, worse naming, **104 junk controllers**. Take Conversation Gestures instead.
- `Vendors_and_Customers` (295 MB, 248 clips) — **LOW**. Tavern/shop NPC work with zero locomotion value and 246 throwaway controllers. Nice detail: prop-synced `Prop_*` clips (glass/rag/shaker animated in lockstep with the hand clip).

---

## 7. Category E — Character-controller prior art ⭐ round 2

_Two independent implementations of "a character that walks on a sphere." Neither should be imported wholesale; both should be read before writing more `CharacterMotor`._

### 7.1 `SuperCharacterController` — **REFERENCE (read this first)**
`D:\Unity\Explore Assets\Assets\ExplosiveLLC\SuperCharacterController` — Erik Ross ("Iron-Warrior") v2.0.0. `Code/README.txt`: *"Package is free to use, modify and redistribute."* 18 `.cs`, ~2,570 lines, **no namespace** (global scope — collision risk).

**Up is fully configurable; `Vector3.up` is not hard-coded.**
```csharp
public Vector3 up => transform.up;      // SuperCharacterController.cs:63-64
public Vector3 down => -transform.up;
```
Every grounding and movement decision routes through that — `ProbeGround`, `ClampToGround` (`transform.position -= up * d`), `SpherePosition`, `SlopeLimit` (`Vector3.Angle(n, up)`), `OnSteadyGround`. **Total `Vector3.up` occurrences: 6, none in the grounding/movement path** — 1 BSP split-plane axis in local mesh space, 1 capsule-local axis (correct — Unity capsules are locally Y-aligned), 3 gizmos, 1 quaternion helper.

**Radial gravity ships as a working demo.** `Code/Examples/Gravity.cs`, complete file:
```csharp
public class Gravity : MonoBehaviour {
    [SerializeField] Transform planet = null;
    void Update() {
        Vector3 dir = (transform.position - planet.position).normalized;
        GetComponent<PlayerMachine>().RotateGravity(dir);
        transform.rotation = Quaternion.FromToRotation(transform.up, dir) * transform.rotation;
    }
}
```
The field is literally named `planet`. There is a `Scenes/SpaceZone.unity` demo with `Mesh/SphereDemoLevel.FBX`. The example motor is up-agnostic too (`moveDirection += controller.up * CalculateJumpSpeed(...)`, `Math3d.ProjectVectorOnPlane(controller.up, moveDirection)`), and `PlayerCamera.cs:33-38` uses `Quaternion.LookRotation(machine.lookDirection, controller.up)`.

**Key files:** `SuperCharacterController.cs` (406 L, pushback + clamp + slope-limit loop), `SuperGround.cs` (298 L, **the 5-probe iterative grounding solver**), `SuperCollider.cs` (243 L, analytic `ClosestPointOnSurface` per collider type), **`BSPTree.cs` (332 L, mesh-collider triangle BSP for nearest-point queries)**, `SuperStateMachine.cs` (101 L), `Math3d.cs` (621 L).

**Compatible with our rules:** zero coroutines (`StartCoroutine|IEnumerator|yield return|async|Task.` → no hits), no `[DefaultExecutionOrder]`, no `RuntimeInitializeOnLoadMethod`.

**Take:** the `up => transform.up` discipline, the 5-probe grounding structure (primary/near/far/step/flush) and its ledge/step/flush edge-case taxonomy, `OnSteadyGround`'s distance-from-center ledge falloff, and `BSPTree` — which is a worked example of nearest-point-on-mesh without `Physics.ComputePenetration`, directly relevant to the physics-bubble plan.
**Don't take:** `SendMessage`-driven update loop, reflection-based FSM, mid-frame `gameObject.layer` mutation into a manually-created `"TempCast"` layer, `Physics.SphereCast(..., Mathf.Infinity, ...)`. Note `BSPTree` builds the full mesh BSP in `Awake()` and caches local-space vertices — not streaming-friendly as written.

### 7.2 Malbers Animal Controller v1.5.2b — **HARVEST now, re-evaluate for IMPORT after the physics bubble**
`D:\Unity\Explore Assets\Assets\Malbers Animations` — 764 MB, 618 `.cs`, 4 asmdefs, 0 DLLs (full source).

⚠️ **This is a MODIFIED copy, not pristine Asset Store.** Source carries `//MWC:` and `//CustomPatch:` edit markers (`MAnimalAIControl.cs:419, :506, :639, :828`; `MAnimalLogic.cs:1364, :1367, :1450`) and a custom `Editor/Menu/ACUpdateChecker.cs`. Treat as a fork — don't assume upstream behaviour or support.

**Gravity is first-class, and it validates our interface design.** `Common/Scripts/Core/Interfaces/IGravity.cs`:
```csharp
public interface IGravity
{
    Vector3 Gravity { get; set; }
    Vector3 UpVector { get; }
    public void Gravity_ResetDirection();
}
```
`MAnimalVariables.cs:1657-1704` backs it with a `Vector3Reference` (bindable to a shared `Vector3Var` ScriptableObject — two ship: `Global Gravity.asset`, `Global GroundGravity.asset`), plus `UpVector => -m_gravityDir.Value` and a `ground_Changes_Gravity` flag that sets `Gravity = -hit_Hip.normal` for wall-walking.

Radial gravity ships as `Common/Scripts/Utilities/Tools/GravityChanger.cs` — `animal.Gravity = (transform.position - Other.transform.position).normalized` recomputed per frame, wired into `Gravity Changer Convex/Concave.prefab` and the demo playground.

The whole motion pipeline is `UpVector`-relative: camera basis (`Vector3.ProjectOnPlane(MainCamera.forward, UpVector)`), slope movement (`Quaternion.FromToRotation(UpVector, SlopeNormal) * move`), velocity projection, `AlignToGravity()`. Of 128 `Vector3.up` occurrences package-wide, essentially all are gizmos/handles/debug/IK — all 12 in `MAnimalAIControl.cs` are `Debug.DrawRay`/`Handles.DrawWireDisc`.

**Architecture:** one fat `MAnimal` MonoBehaviour driving `AdditivePosition`/`AdditiveRotation` through `KinematicSweep()`, with 17 locomotion states authored as **ScriptableObject `State` assets** (`Idle, Locomotion, Fall, Jump, Fly, Glide, Swim, SwimUnderwater, Climb, LedgeGrab, Slide, WallRun, WallRunVertical, Death, Ladder, Ragdoll`; 85 state `.asset` files ship). AI is a separate SO-driven layer (`MAnimalBrain` + `MAIState` + 23 tasks + 23 decisions) talking to an `IAIControl` interface. Complete rider/mount/carriage system (`MRider`, `Mount`, `IKReins`, `WagonController`, `PullingHorses`).

**Why it's not a drop-in wildlife-AI replacement for Polyperfect today** — and the distinction matters:
- **Polyperfect:** broken math (hard-coded `Vector3.up` plane projection). Unfixable without a rewrite.
- **Malbers:** **correct math, wrong world contract.** Grounding is exclusively `Physics.Raycast(Main_Pivot_Point, -Up, out hit_Chest, distance, GroundLayer, ...)` against colliders, plus `Physics.CapsuleCast` depenetration (55 `Physics.Raycast`, 14 `SphereCast`, 3 `CapsuleCast` package-wide). With no ground colliders, `AlignPosition` early-returns and everything free-falls. Its stock AI also needs a `NavMeshAgent` — though the brain/decision/task layer is NavMesh-free and swappable behind `IAIControl`.
- **Zero Unity `Terrain` dependency** anywhere. That part is clean.

**Blockers against import as-is:** 113 `StartCoroutine` / 144 `IEnumerator` / 170 `yield return` (structural, not incidental); **21 `[DefaultExecutionOrder]`** including on `MAnimal` itself; no `IGroundingProvider` seam (`AlignRayCasting` is `internal virtual`); the fork; 764 MB and an asmdef pulling Cinemachine + AI Navigation.

**Mine now:** `IGravity.cs`, `GravityChanger.cs`, `GravityReaction.cs`, and the `UpVector`-relative camera-basis / slope-projection / velocity-projection block (`MAnimalLogic.cs:1406-1447, 1920-1950, 2030-2040`; `MAnimalCallBacks.cs:154-167`). Re-evaluate for real import once the physics bubble lands — at that point the remaining work is a coroutine→`Awaitable` pass and a NavMesh-free `IAIControl`.

### 7.3 `RPGCharacterController` (ExplosiveLLC) — **REFERENCE**
100 `.cs`, `namespace RPGCharacterAnims`. Built on SuperCharacterController (`RPGCharacterMovementController : SuperStateMachine`), so **the movement code is already up-vector agnostic** — only 4 real `Vector3.up` sites remain (`RPGCharacterController.cs:679-680`, `RPGCharacterInputController.cs:178, 256`, `RPGCharacterMovementController.cs:744`). Useful as a worked example of layering a full action-game state machine on top of an up-agnostic motor. ⚠️ 6 coroutine files, one isolated `NavMesh` file, and see §11.

### 7.4 `PerfectLookAt` (ExplosiveLLC) — **LOW**
Multi-bone procedural look-at (head/neck/spine) in `LateUpdate` with per-bone limits and a FABRIK leg stabiliser. Up is nominally configurable (`m_UpVector`), but two world-Y sign bugs break on the far hemisphere: `PerfectLookAt.cs:367` (`Mathf.Sign(Vector3.Cross(...).y)`) and `PerfectLookAtLegStabilizer.cs:43,46` (`if (CrossVec.y < 0.0f)`). ~3-line fix — replace `.y` with `Vector3.Dot(cross, m_UpVector)`. No namespace.

---

## 8. Category F — Shaders & techniques to harvest

_Nothing here gets imported as a running system. These are reads and transplants._

| Source | Artifact | Why |
|---|---|---|
| **Staggart Stylized Grass Shader** | `Shaders/Libraries/Wind.hlsl` (142 ln), `Bending.hlsl` (174 ln), `Lighting.hlsl` (89 ln), `Color.hlsl` | Densest directly-transplantable shader math in the library. `WindSettings` struct layering ambient + swinging + gust-noise-map; bend-vector RT decode; grass translucency. Read side-by-side with our compute grass. |
| **Fantasy Adventure Environment** | `PigmentMapGenerator` + `TerrainUVUtil` (Mesh workflow) | Top-down colour-map bake so props tint to the ground beneath them. Answers "scattered props read as pasted-on." Already supports `MeshRenderer`, so it survives having no Terrain. |
| **Polyart Dreamscape** | `ImpostorConverter.cs` + `ImpostorDataHolder.cs` + `Impostor.shadergraph` + `SM_ImpostorQuad.asset` | A complete baked-impostor toolchain. Direct comparison target for our billboard baker. |
| **Polyart Dreamscape** | `CloudComputeShader.compute` + `CloudRenderer.cs` | Another take on compute-driven clouds. |
| **Polyart Dreamscape** | `PA_SG_Planets.shadergraph`, `Polyart/Dreamscape/Builtin/Planet` | Literally named "Planet" — worth 10 minutes. |
| **Polyart Dreamscape** | `FoliageInteractor.cs`, `instancingHelper.hlsl` | Grass push-away; our interactor hook is currently inert. |
| **Broccoli** | `BillboardBuilder`, `TextureBuilder`, `Hidden/Broccoli/BillboardNormal`/`BillboardExtras`/`BillboardSubsurface` | Bake-time billboard atlas path paralleling our impostor baker. |
| **Synty PolygonGeneric** | `Shaders/` — `SnowMask`, `SplitTriplanar`, `SplitTriplanarNormal`, `triplanarWorldTopMask`, `Triplanar_Basic`, `DepthFade`, `ObjectYRotation`, `Panner` | Snow masking + triplanar directly on-theme for sphere biome blending. **Also a hard dependency** — see §11. |
| **Synty PNB_Core** | `Foliage.shadergraph` | Wind-animated foliage shader used by 176 NatureBiomes materials. |
| **Synty PolygonElvenRealm** | `Waterfall.shader`, `Large_Waterfall.shader`, `WaterFountain.shader`, `Aurora_ElvenRealm.shadergraph`, `RockTriplanar.shadergraph`, `MoonNoFog.shadergraph` | Worth stealing independent of the art. Aurora is relevant to polar biomes. |
| **Toon Enchanted Meadow** | `TEM_CustomStoneSpherical.shader` | Spherical projection — may suit planet-curvature rock materials. |
| **BOXOPHOBIC TVE** | the `(TVE Model).asset` / `(TVE Material).mat` sidecar pattern | A clean answer to "re-material an imported prefab without destroying it" — directly applicable to our prop pipeline, even though the module itself won't compile (§9.4). |
| **Sprite Shaders Ultimate** | `ASE/Effect`, `ASE/Blur`, `ASE/Fading`, `ASE/Transform` library + `Textures/Noise` + `Textures/Mask` | Only asset regenerated against a **Unity 6 / URP 17** ASE template (`MotionVectors`/`XRMotionVectors` passes). Mine the dissolve/dither patterns. Do **not** import the runtime — see §11. |
| **ARTnGAME Common Tools** | `metaballsGPU/Shaders/MarchingCubesComputeShader.compute` | Relevant if caves/digging (Phase 9) lands. **Get it from the upstream MIT repo** (dario-zubovic/metaballs), not this vendored copy. |
| **ARTnGAME IvyStudio** | `IvyGeneratorTREANT`, `ExtrudeMeshBranchIvySTUDIO`, `SplineMeshExtrusionIvySTUDIO` | Collider-based surface-crawling growth — conceptually sphere-safe. Technique read only. |
| **Kevin Iglesias** | `SpineProxy.cs` + 8 avatar masks | Upper-body/spine layer split for mouse-look torso rotation. |

---

## 9. Category G — Tools & libraries

### 9.1 Broccoli Tree Creator v1.10 + Sprout Lab — 752 MB — **IMPORT (editor-only, surgical)**
Waldemarst, node-pipeline procedural tree/branch generator. A **content factory**, not a runtime system: run it in the editor, bake tree prefabs + billboard atlases, then delete it.

- **URP-aware:** `MaterialManager.cs:218/:380` does `Shader.Find("Universal Render Pipeline/Nature/SpeedTree8")` with a `_PBRLit` fallback, gated on a real `UniversalRenderPipelineAsset` check.
- Its 26 shaders are all `Hidden/Broccoli/*` **editor bake-time blits** — pipeline-agnostic.
- Output is plain prefabs with `MeshRenderer` + **LODGroup** — exactly what our scatter system consumes.
- **Terrain coupling isolated to one opt-in class**, `BroccoTerrainController`. Don't use it; the other 297 files are terrain-free.
- Essentially no coroutines (4 `IEnumerator`, all in vendored `Clipper2Lib`). Unity-6 aware. Files stamped Nov 2025.

⚠️ **Ships zero asmdefs** — importing drops **298 `.cs` into `Assembly-CSharp`**. Better: **bake in the scratch project and import only the resulting prefabs + atlases.** Skip `BroccoliExamples/` (96 MB).

### 9.2 Asset Inventory v4.6.0 (Wetzold) — 58 MB — **IMPORT (dev tool, not this project)**
Editor tool indexing your whole Asset Store library *outside* the current project. Editor-only, **12 asmdefs**, zero runtime/pipeline/terrain coupling. Changelog top entry `[4.6.0] - 2026-07-12`; 4.6.0 added local-embedding **semantic search** and **code search** across indexed packages.

The correct long-term answer to the question this document exists to answer — install it **in the scratch project or as a UPM package outside `Assets/`**. Its `com.unity.editorcoroutines` dependency is editor-scope.

### 9.3 `Kugon\BetterAnimationEvents` v2.0.1 — 1.1 MB — **REFERENCE / low-risk IMPORT** ⭐ round 2
Replaces Unity's cramped one-strip Animation Events UI with a channelized editor: vertical event channels, zoomable frame timeline, snapping, colour/icon tagging, copy/paste, multi-select, undo integration. **Both asmdefs declare `"includePlatforms": ["Editor"]`** — zero runtime footprint. Main window `KAnimationEventEditor` at `Window → Animation → Animation Event`.

Buys nothing today (no authored clips yet), but costs nothing and helps the moment footstep/VFX events get authored on a character. ⚠️ Its `Runtime/` folder name is misleading — verify the Editor-only constraint survives if files are ever moved.

### 9.4 `BOXOPHOBIC\The Vegetation Engine Modules\Polygonal Shaders` — 7.1 MB — **SKIP (won't compile)** ⭐ round 2
The Vegetation Engine **Polygonal Shaders Module** v12.6.0 — an **add-on**, and the base asset is **not present anywhere in the tree**. It won't compile: `TVEPSHub.cs` uses `Boxophobic.StyledGUI` / `Boxophobic.Utils` (absent), its asmdef references 4 assemblies by GUID that don't resolve, and it calls a base-asset menu item.

Architecturally it's the *right* kind of tool — mesh-based, **zero Terrain dependency**, and it does the "re-material third-party props onto one uber-shader" job we do by hand. Re-evaluate only if the base is bought. Meanwhile **REFERENCE** its sidecar pattern (§8).

### 9.5 `Plugins` — two tweeners, one misfile ⭐ round 2

- **DOTween 1.3.030 (free) — SKIP.** No asmdef → 9 module `.cs` land in `Assembly-CSharp`; the DLL is closed-source; setup has never been run (no `DOTweenSettings.asset`). A procedural planet renderer has no UI/menu animation to tween; our `Awaitable` lerps already cover it.
- **PrimeTween 1.4.12 — REFERENCE.** Not installed — the payload is an inert `.tgz` installer stub (`internal/com.kyrylokuzyk.primetween-1.4.12.tgz`); the 3 asmdefs present are demo/installer scaffolding. *If* a tweener is ever needed this is the one: zero-allocation, UPM-installed (never pollutes `Assembly-CSharp`), and its `await`-friendly API coexists with the Awaitable-only rule far better than DOTween's. Cost today: 0 bytes of compiled code. **Do not import both.**
- **Threepeat — misfiled animation content**, see §6.3.

### 9.6 `TextMesh Pro` — 3.4 MB — **SKIP**
Stock TMP Essential Resources, unmodified. Unity generates its own on first TMP use.

---

## 10. Category H — Audio ⭐ round 2

### 10.1 `Farm Animal Sounds` — 157 MB, 374 WAV — **HARVEST (selective)**
Asset Store product 179750 v1.0. All uncompressed PCM 44.1 kHz / 16-bit / stereo.

**369 species one-shots across 17 species:** Horse 57, Dog 50, Cat 37, Goose 31, Birds 28, Chicken 26, Pig 25, Swan 21, Duck 18, Turkey 17, Cow 13, Sheep 12, Goat 11, Owl 8, Gosling 7, Frog 5, Peacock 3. Mostly plain-numbered (`Cow 01..13.wav`), some behaviour-tagged (`Horse Eating 01.wav`).

**5 loopable ambience beds** in `Bonus/`: `Forest Loop.wav` (16.9 MB, ~3:20), `Stream Calm Loop 1.wav`, `Stream Moderate Loop 1.wav`, `Rain Light 1.wav`, `Rain Moderate 1.wav`.

Relevant twice over: the ambience beds map onto our 16 biomes + ocean and would bind naturally to the weather grid's rain-rate tiers; and **14 of the 17 species have Polyperfect counterparts**, which ship silent.

⚠️ Import settings ship as `loadType: 0` (Decompress On Load) with `preloadAudioData: 1` — a memory hazard on a 16.9 MB bed. Re-author to Streaming + no-preload for the loops, Vorbis for the one-shots. Cherry-pick ~40–60 files (<20 MB), not all 158 MB.

---

## 11. Compatibility landmines

Read this before importing anything.

1. **Synty `PolygonGeneric/Shaders/Generic_Basic.shadergraph` is a hard cross-pack dependency.** GUID `0730dae39bc73f34796280af9875ce14` is referenced by Adventure 9/9, Knights 14/14, Dungeon 34/43, ElvenRealm 94/127, FantasyKingdom 44/51, Prototype 17/33, FantasyHeroCharacters 12/34, NatureBiomes 74/459 materials. Import that folder **first** or every other Synty pack's materials break. (Moot while we import raw FBX and re-material.)
2. **`PNB_Core/Shaders/Foliage.shadergraph`** — same story, 176 NatureBiomes materials.
3. **Staggart Stylized Grass Shader is dead on this Unity version.** Project is **Unity 6000.6.0a7 / URP 17.6.0**. `GrassRenderFeature.cs:362` hard-errors on 6.3+, and its `RecordRenderGraph` overrides are **empty stubs** — under RenderGraph it silently writes no bend RT and no `_GrassBendCoords`/`_DitheringScaleOffset` globals. It will *appear* to import fine and do nothing.
4. **🔴 CS0101 duplicate-definition failure across RamsterZ packs.** All 8 RamsterZ packs ship a byte-identical `Scripts/SimpleCameraController.cs` (md5 `02f8b295…`, Unity's stock template flycam, `namespace UnityTemplateProjects`) under **8 different GUIDs**. Importing more than one **fails compilation**. Delete all but at most one. Affected: `One_Handed_Sword_Attacks_and_Finishers`, `Survival_Animations`, `StealthFinishers_Knife_and_Hand`, `Loot_Anim_Set`, `Tactical_Hand_Signals`, `Sleep_Anim_Pack`, `Vampire_Boss_Set`, `Simple_Activations` — and the same class also appears in `Universal_Traversal_Anims`, `Female_Anim_Starter_Pack`, `Dialogue_Anims`, `Vendors_and_Customers`.
5. **🔴 `ExplosiveLLC/Editor/SetupInputLayers.cs` is an `AssetPostprocessor` whose `OnPostprocessAllAssets` force-opens an `EditorWindow` on every single asset import.** Delete it before or during any copy.
6. **🔴 Fantasy Adventure Environment uses removed Shader Graph APIs.** Its `.shadergraph` files carry `UnityEditor.Rendering.Universal.UniversalPBRSubShader` and `Vector1ShaderProperty` — SG 7.x schema; `UniversalPBRSubShader` was removed in Shader Graph 10. Against SG 17.6 / Unity 6000.6 this is the **highest-risk import in the library**. Test in a throwaway project first.
7. **259 rig-import errors in `Universal_Traversal_Anims`** — `Copied Avatar Rig Configuration mis-match. Transform 'root' not found in HumanDescription.` All in the root-motion set; `In_Place_Versions/` imports clean. Take from there, then tick Loop Time (publisher left it at `0`).
8. **8 misconfigured clips in `Grruzam Archer`** at `Animation/Archer/1_Movements/2__Walk/Inplace/Archer@Walk_*_Root.FBX` — `animationType: 2` (Generic), so they won't retarget. Flip to Humanoid + CopyFromOther (`575e745653e0ee54eb8c7183d6a6b807`), or use the `Root/` copies.
9. **Legacy built-in materials render magenta under URP.** Counts by pack (GUID `0000000000000000f000000000000000`): PolygonParticleFX **42/57**, ElvenRealm 13, PolygonNature 13, PolygonNatureBiomes 13, Dungeon 6, FantasyKingdom 6, Prototype 3. Also: **every ExplosiveLLC material**, all Polyperfect (174), all RamsterZ demo mats, all LMHPOLY (89). Polygonmaker is the only animation pack shipping URP/Lit.
10. **`BOXOPHOBIC` module will not compile** — base Vegetation Engine absent; 4 unresolvable asmdef references.
11. **Malbers is a fork** — `//MWC:` / `//CustomPatch:` markers throughout. Upstream updates are risky.
12. **Two packs still hide URP support inside unextracted archives** — Polyart Dreamscape Meadows (99 MB) + Mountains (119 MB). Partly moot: the master shadergraphs are already dual-target (§3.A7).
13. **Procedural Worlds pack is HDRP-configured and Gaia/Terrain-bound.** Art only.
14. **`Sprite Shaders Ultimate` `WindManagerSSU`** writes an ungated `Shader.SetGlobalFloat("WindTime", …)` — a raw string global with no `ShaderGlobalIds` entry, colliding with our wind system.
15. **Missing asmdefs are the norm** — Broccoli, IvyStudio, ARTnGAME, Sprite Shaders Ultimate, DOTween, Kevin Iglesias, Opsive, Threepeat, SuperCharacterController all compile into `Assembly-CSharp`. SuperCharacterController and PerfectLookAt additionally declare **global-namespace types**.
16. **Unexpected bonus:** hundreds of `.asset` files across Synty packs (Adventure 598, Dungeon 653, FantasyKingdom 2,061, NatureBiomes 282, Knights 374, ElvenRealm 427) are **baked convex collision meshes** at `Models/Collision/*_Convex.asset`. Directly relevant to [2026-08-09-collision-strategy.md](../design/2026-08-09-collision-strategy.md) — props come with cheap convex hulls already cooked.
17. **Per-clip controller spam.** Most animation packs ship one trivial `.controller` per clip for their demo scene (Polygonmaker 399 junk of 401, Vendors 246, Female 181, Universal_Traversal 404). Exclude on import.

---

## 12. UNSURE — needs a human to look

| Item | Question | How to resolve |
|---|---|---|
| **Base-set final call: Kevin Iglesias vs Opsive** | KI = 1,373 clips, in-place default, zero rig errors, has swim + fall. Opsive = 74 immaculate clips with per-foot Start/Stop and 45° turns, but root-motion-only, no fall loop, no land, no swim. Recommendation is KI base + Opsive start/stop layered on — but this is a feel call. | Retarget ~10 clips from each onto a Synty hero and walk them. Needs Bryan at the keyboard. |
| **Style match: Toon Fantasy Nature / TEM vs Synty** | Toon-outlined and more saturated than Synty flat-shaded. One planet or two? | Import 3 props, place next to `SM_Gen_Env_Tree_01`, F10 capture, eyeball. |
| **Style match: Corals (2K PBR) underwater** | Doesn't match Synty. Does that matter below the waterline? | Same test, underwater. |
| **`Corals` licensing** | No readme, no documentation, unidentified publisher. Only marker is Amplify's `u3d.as/y3X` boilerplate. | Identify the Asset Store product before shipping anything from it. |
| **Polyart Dreamscape URP archives** | 99 MB + 119 MB still unextracted; no new URP materials have appeared. Partly moot (dual-target shadergraphs already cover 93/169 mats) — the archives would only fix 38 built-in particle/skybox mats. | Extract in the scratch project if the particles matter. |
| **`PA_SG_Planets.shadergraph` / `Polyart/Dreamscape/Builtin/Planet`** | Named "Planet". Skybox backdrop or something genuinely useful? | 10-minute read. |
| **Synty terrain-layer libraries** (`PolygonNature` 19 + PNB 89, 658 textures) | Could feed planet surface shading independent of any mesh. Unknown whether resolution/tiling suits our biome texture arrays. | Compare one against `BiomeSurfaceTextureArrays` inputs. |
| **`TEM_CustomStoneSpherical.shader`** | Spherical projection — coincidence of naming, or useful on a planet? | Read the graph. |
| **Frank Platformer 2 proportions** | Stylized short character; its air-state clips are the best available, but retargeting onto Synty's realistic proportions will show slide on slides/vaults. | Retarget one slide clip with foot-IK on and judge. |
| **Toon packs' post-process profiles** (6 TFF + 3 TEM) + LUTs | We have a graded `PlanetLookProfile`. Worth A/B-ing as day/dusk/night grades? | Optional; low cost. |
| **`FANTASTIC - Nature Pack` archives** | Now redundant — the URP variant is extracted as `Fantastic Nature Pack`. 458 MB of shadowing archives. | Confirm, then the archive folder can go. |

---

## 13. Do not import

| Item | Size | Reason |
|---|---:|---|
| **LMHPOLY Modular Terrain** | 423 MB | Hand-authored mesh terrain kit — orthogonal to a procedural cube-sphere |
| **ARTnGAME InfiniGRASS** | 172 MB | Partial drop — 3 orphan scripts, no actual tool. 99% redistributed CC0 textures better fetched from source. URP shader is a hand-ported LWRP relic. |
| **ARTnGAME WallGEN** | 60 MB | Vendor-declared pre-alpha, Built-in-only, off-theme |
| **ARTnGAME Common Tools** | 4.6 MB | Built-in surface shaders, `LoomANG` thread pump (banned pattern), no asmdefs |
| **Synty PolygonPrototype** | 31.8 MB | Greybox/blockout kit |
| **Synty PolygonParticleFX** | 51.6 MB | 42/57 materials on legacy built-in particle shaders; blizzard/bubbles overlap systems we own |
| **PNB_Core runtime** (`WeatherControl.cs`, skydome, cloud rings, rain FX) | — | Duplicates our volumetric clouds / weather sim / ocean. Take the **shaders**, not the runtime. |
| **Procedural Worlds Gaia configs** | ~most of 789 MB | Gaia + Unity Terrain only; broken `m_Script` GUIDs |
| **Staggart `GrassRenderFeature` + runtime** | — | Silently no-ops on Unity 6000.6 (§11.3) |
| **Sprite Shaders Ultimate runtime** | — | Unnamespaced class, string-keyed shader global, no asmdef |
| **Corals `__CubemapsShaders/`** | 703 MB | Shared-library dump unrelated to corals |
| **`ExplosiveLLC/Fighter Pack Bundle`** | 0 B | **Empty** — 4 Windows `.url` shortcuts to Asset Store pages. Never downloaded. |
| **`Polyart\Polyart Studio`** (with space) | 0 B | Empty shell from a cancelled import, 2026-08-10 21:02:41. Has a `.meta`; delete-safe. |
| **`FANTASTIC - Nature Pack\Standard_..._U6.unitypackage`** | 224 MB | Built-in variant; the URP one is already extracted |
| **DOTween** | — | No asmdef, closed-source, never set up, and we have no tweening need |
| **TextMesh Pro** | 3.4 MB | Unity auto-generates it |
| **`Tactical_Hand_Signals`** | 82 MB | Modern-military squad comms in rifle/pistol grips. Genre mismatch. |
| **All demo scenes, lighting settings, per-clip controllers, and demo mannequins** across every pack | — | LMHPOLY alone ships 62 demo scenes + 48 lighting assets; animation packs ship ~2,000 junk controllers between them |

---

## 14. Suggested sequence

Grounded against [2026-08-09-whats-next-roadmap.md](../design/2026-08-09-whats-next-roadmap.md) — Arc B (gameplay frontier) is the active arc; backlog #1 is human-verifying the character MVP, #4 is the collision bubble, #7 is the first interaction verb, #21 is content breadth.

**Now — unblocks the active character arc:**
1. **Read `SuperCharacterController`** (§7.1) before writing more `CharacterMotor`. Free, zero coroutines, ships a working radial-gravity planet demo. Then skim Malbers' `IGravity`/`GravityChanger` (§7.2) as a second opinion on the interface shape.
2. **Import Synty `PolygonFantasyHeroCharacters`, selectively** — `Models/FixedScale/ModularCharacters.fbx` + shaders + 35 textures + a few presets, not the 230 MB of fat preset prefabs.
3. **Import `Kevin Iglesias` locomotion** — the `{Male,Female}/Movement` folders. Retarget onto the Synty hero. This finally makes the character MVP human-verifiable with real animation.
4. **Layer `Opsive/OmniAnimation`** start/stop + turn-in-place after one bulk Bake-Into-Pose re-import.

**Next — content breadth, cheap:**
5. Synty `PolygonNature` (76.7 MB) — biggest content win per MB.
6. LMHPOLY **Rocks** module (48 MB) — second-biggest; moss + snow atlases.
7. PNB **Arid_Desert** + **Alpine_Mountain** FBX cherry-picks — closes the Desert / Mountain / Snow / Tundra gaps.

**Then — new surface and the first verb:**
8. `Corals` (~358 MB subset) — first content for the Ocean biome; the 44 `_G*` colony prefabs also prototype clumping.
9. **`Simple_Activations` (33 MB) + `Loot_Anim_Set` (95 MB)** — the animation half of roadmap #7 (first interaction verb). Both are one-shot upper-body actions that bolt onto a locomotion layer without needing inventory, combat, or AI to exist. Remember the CS0101 trap (§11.4).
10. `Fantastic Nature Pack` camping props + a `Survival_Animations` slice (`_Build_Shoveling`, `_PickAxe_*`, `_TreeChop_*`, `_Foraging_BerryBush`) — pairs naturally, and prefigures digging.

**After the physics bubble lands (roadmap #4):**
11. **Re-evaluate Malbers for real import** — at that point the remaining work is a coroutine→`Awaitable` pass and a NavMesh-free `IAIControl`.
12. **Polyperfect wildlife** — import a handful of species (rigs + clips + controllers + STAT assets), run the URP material upgrade, add `Farm Animal Sounds` vocalizations. Their 13 aquatic species pair with move 8.
13. `Universal_Traversal_Anims` / `Threepeat` Parkour — climb, vault, ledge, wallrun become meaningful once there are surfaces to collide with.

**Opportunistic, any time:**
14. Broccoli in the scratch project — bake tree variants offline, import prefabs only.
15. Asset Inventory in the scratch project — makes the *next* version of this document unnecessary.
16. Harvest `Wind.hlsl` / `Bending.hlsl` when foliage wind sway (roadmap #2) is next touched.
17. Harvest the FAE pigment-map technique when scattered props next read as pasted-on.

**Deferred until the systems exist:** combat/stealth/boss animation (§6.5), settlement content — FantasyKingdom, ElvenRealm, Dungeon, Knights, TEM building kit (§3), NPC social gestures (§6.7).

---

## 15. Appendix — the full Asset Store library (885 purchases)

**Raw list: [2026-08-10-unity-asset-store-purchases.tsv](2026-08-10-unity-asset-store-purchases.tsv)** — productId, title, purchase date, for all 885.

Retrieved 2026-08-10 from the signed-in Unity Editor by calling `IAssetStoreClient.ListPurchases` through `ServicesContainer` (internal `UnityEditor.PackageManager.UI` API) and reading `AssetStoreCache.m_PurchaseInfos`. No credentials handled — the Editor's existing session did the auth. Query was `?offset=0&limit=1000`; 885 returned, so the list is complete. Purchases span 2012–2026, peaking in 2022 (291) and 2025 (144).

**Why this matters:** everything in §3–§10 was scoped to what's sitting in `D:\Unity\Explore Assets`. That's **~50 of 885 owned assets — under 6%.** The scratch folder is a small, recent slice of the library, and several of the strongest candidates for this project were never copied into it.

### 15.1 Owned but not in the scratch folder — high relevance

| Asset | Why it matters here |
|---|---|
| **Kinematic Character Controller** | The industry-standard Unity KCC. Arbitrary-up / custom-gravity is a first-class feature. Likely a stronger reference than `SuperCharacterController` (§7.1) — **check this before committing to a motor design.** |
| **Character Controller Pro** | Lightbug. Also supports arbitrary vertical alignment; another direct comparand. |
| **Character Movement Fundamentals** · **Easy Character Movement 2** · **Physics Character Controller** | Three more custom-gravity-capable controllers. Between these and KCC, the sphere-locomotion problem has four owned prior-art implementations. |
| **The Vegetation Engine** (base) + **Terrain Details Module** | **This unblocks §9.4** — the Polygonal Shaders module in the scratch folder won't compile because the base is missing. You own the base. Its "re-material any prop onto one uber-shader" job is exactly what `Planet/PropLit` does by hand. |
| **Voxelica — Voxel Engine** | Directly relevant to roadmap #22 (digging/caves, Phase 9). |
| **SECTR World Streaming for Unity 6** | Streaming/culling architecture comparand for the chunk system. |
| **Altos — Volumetric Clouds, Skybox and Weather for URP** · **EzCloud** · **UniStorm** · **InfiniCLOUD** · **Weather Maker** | Five volumetric-cloud/weather implementations to compare against ours — relevant to the "clouds parked needing polish" item and the half-res raymarch perf win (roadmap #3). Altos is the URP-native one. |
| **Stylized Water 2** (Staggart) · **Thalassophobia: Stylized Oceans** · **Oceanis 2024 Pro URP Water Framework** · **Dynamic Water Physics 2** | Ocean comparanda; DWP2 is buoyancy, which is roadmap #20. ⚠️ Crest Water 4 is **BIRP-only** — not useful. |
| **GrassFlow 2** · **Brute Force — Grass Shader** | Two more GPU-grass implementations to read against our compute blanket. |
| **Jupiter — Procedural Sky Shader & Day Night Cycle** | Day/night cycle comparand for `CelestialManager`. |
| **GTS — Terrain Shader for Unity 6** · **Advanced Terrain Shaders v2** | Terrain-shading technique (Unity Terrain-bound, but the splat/height-blend math may transfer to the biome blend). |
| **Space Graphics Planets** | Planet rendering, from the opposite direction (space-side). Worth a look purely for comparison. |
| **Brute Force — Snow & Ice Shader** | Snow/ice biomes are among our thinnest. |
| **MK Toon — Stylized Shader** · **Quibli: Anime Shaders and Tools** · **Flat Kit: Toon Shading and Water** | Stylized-shading systems, relevant to the Synty-look target. |
| **Umbra Soft Shadows for URP** · **Volumetric Fog & Mist 2** · **Aura 2** | Lighting/atmosphere. Umbra is interesting given the dusk grazing-shadow work. |
| **Odin Inspector and Serializer** | Would substantially improve the settings-SO authoring surfaces. |
| **Poly Art: Animal Forest Set** · **Forest animals** | More biome wildlife beyond Polyperfect. |
| **Real Ivy 2** | Second ivy generator alongside IvyStudio. |
| **Asset Inventory 4** | Confirms §9.2 — owned, just never run. |

### 15.2 Already-owned duplicates of things catalogued above

`Parkour Animation Set` + `Volume 2` = the misfiled `Plugins\Threepeat` (§6.3). `Omni Animation — Core Locomotion Pack` = §6.1. `Animal Controller (Malbers Character Controller)` = §7.2. `Vegetation Studio Pro` is cached but never extracted; note Fantasy Adventure Environment ships VSP integration shims (§3.A8) that would suddenly become live if VSP were imported.

### 15.3 Caveat

`AssetStorePurchaseInfo` carries only productId / title / date — **no publisher or category field**. Publisher attribution above is inferred from the download cache (`%APPDATA%\Unity\Asset Store-5.x\`, 462 publisher folders) and from titles. The download cache holds 214 `.unitypackage` files (63.3 GB); the other 671 purchases are not downloaded on this machine.

### 15.4 How to refresh this list

Re-run the same call from a signed-in Editor. Or — better — run **Asset Inventory** (§9.2) in the scratch project: it does this plus indexes every file inside every package, with semantic and code search. It has never been run; there is no database anywhere on disk.
