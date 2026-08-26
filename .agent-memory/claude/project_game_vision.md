---
name: project_game_vision
description: The game ProceduralPlanets is becoming — a Valheim-inspired fantasy RPG where you play a wizard on a procedural planet
metadata:
  type: project
---

Stated by Bryan 2026-08-10. The planet renderer is the substrate; **this is the game it exists to serve.**

**Genre:** fantasy RPG, third-person, **you play a wizard**. Deliberately copies a lot of
**Valheim**'s gameplay loop.

**Pillars, roughly in the order Bryan named them:**

- **Building, crafting, cooking, potions** — Valheim-style. Piece-by-piece construction.
- **Resource harvesting** — chopping trees, mining ore, fishing, gardening.
- **High magic** — *lots* of spells. This is the defining feature, not a subsystem.
- **Teleportation as the fast-travel system**, via spells and **portals**.
- **Wildlife, monsters/bad guys, and friendly NPCs / townsfolk / towns.**
- **Tame and ride horses or other mounts.**
- **Carts** with a progression: wheelbarrow → handcart → wagon.
- **Late-game flight.**
- **Later:** sailing, swimming, fishing.

**Art target: low-poly / Synty style.** Consistency beats fidelity — realistic/PBR packs are
a style clash even when technically better. See [[reference_external_asset_library]].

**Refinement (2026-08-12, from the first asset-bench run):** Bryan **"generally likes Synty
better"** — Synty is the default and the tie-breaker. But it is *not* absolute: where a
non-Synty pack has genuinely richer geometry, it wins. Concretely he kept two **toon oaks**
(Toon Fantasy Nature, Toon Enchanted Meadow) over a Synty tree because the toon canopies have
**real leaf geometry** vs a **simple trunk → leaf-sphere**. So: silhouette and geometric
detail beat brand loyalty; low-poly *style* consistency still beats fidelity.

⚠️ Caveat on that specific verdict: the reference was `SM_Gen_Env_Tree_01` from
**PolygonGeneric** — Synty's deliberately-minimal *filler* pack — not the
`SM_Env_Tree_Meadow_01` (PolygonNatureBiomes, trunk + canopy parts) the game actually plants.
Re-run against the Meadow Tree confirmed both toon oaks anyway.

**Pattern after two bench batches (2026-08-12), 6 assets judged — it is category-dependent,
not a blanket preference:**

| Category | Winner | Verdict |
|---|---|---|
| Characters | **Synty** | KayKit Mage **Cut** vs Synty Fantasy Hero preset |
| Animals | **Polyperfect** | Quirky fox **Cut** vs Polyperfect fox (Synty ships none) |
| Trees | **non-Synty** | 2 toon oaks + Polyart Dreamscape all **Keep** — richer canopy geometry |
| Ocean | **Corals** (Keep) | fills a biome with zero scatter today |

Read: Synty is the house style and wins the character silhouette outright. Vegetation is where
other packs beat it, on canopy geometry. Elsewhere the question is content-gap, not style.

**TREES ARE GENERATED, NOT BOUGHT (Bryan, 2026-08-12).** Decision: *"we are going to go with a
custom Broccoli implementation, so we probably will only look at tree assets as reference for
types we don't have generated."*

- **Broccoli Tree Creator** (owned; installed in the scratch project at
  `D:\Unity\Explore Assets\Assets\Waldemarst\Broccoli`, also cached at 684 MB) becomes the tree
  pipeline. It bakes standalone `Mesh` `.asset` files with LOD chains — `PrefabBuilder.cs` has 19
  LOD hits — which is exactly the shape `ScatterPart` wants (`LodMeshes` + `Material`).
- **Harvest-only is satisfied without effort here.** Broccoli has ~300 `.cs` files and no
  asmdefs, so its `Base/Builder/Factory/...` compile as *runtime* code, and its prefabs carry
  `BroccoTreeController`. But **we consume meshes, not prefabs**, so none of that crosses. Tool
  stays in scratch; baked meshes come over. Textbook case of the editor-tool-in-scratch rule.
- **Vendor tree packs demote to reference only** — used to judge *what kinds* of tree we lack,
  not as shipping content. Bench batches should stop treating trees as candidates.
- Why: every vendor tree this session arrived broken the same way (Polyart shadergraphs NRE on
  Unity 6, toon packs not planet-aware, Synty Generic shipped with no texture assigned). Three of
  five packs in batch 2 needed shader work before they could even be looked at. Generated meshes
  have no vendor shader, no dependency closure, no import breakage.
- ⚠️ Unverified: whether Broccoli's baked meshes are UV-mapped to an atlas we can drive, and how
  its leaf cards behave under `Scatter/FoliageLit` cutout. That is the thing that would sink it.
- Consequence for the 4 promoted bench keeps: 3 are trees (2 toon oaks + Polyart Dreamscape) and
  are now **interim/reference**, superseded once generated equivalents exist. The coral is not
  affected. See [[project_ocean_scatter]].

**Judge on our shader, not the vendor's** — the bench defaults to re-rendering candidates on
`Scatter/FoliageLit` with vendor textures, because that is what the asset becomes once adopted
(a `ScatterPrototype` with our materials). Vendor shaders show their demo scene, not our world,
and they are not planet-aware. This also makes vendor shader breakage irrelevant: three of five
packs staged in batch 2 had Built-in-pipeline shaders or shadergraphs that NRE on Unity 6.

**Why this matters for every recommendation:** subsystems that looked like polish under the
old "procedural planet renderer" framing are now first-class. Specifically **magic/spell VFX**
and **portals** went from unmentioned to top-three, and **survival/crafting/building** is the
single largest gap — it has zero code today.

**Asset adoption rule Bryan set (2026-08-10):** **harvest-only.** No third-party runtime C#
ever ships in the repo. Download the real code into the scratch project, lift the parts that
earn their place, refit them into our architecture. Art/meshes/clips/textures/shaders CAN be
imported. Editor-only tools run in the **scratch project**; only their baked output crosses
over. Full per-subsystem plan: `docs/research/2026-08-11-asset-adoption-map.md`.

**How to apply:** when scoping any new work, check it against these pillars before proposing
it. Off-genre content (sci-fi, modern/urban, racing, military) is skip-on-sight — Bryan owns
a lot of it and explicitly said to skip it.

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Game vision](project_game_vision.md) — 2026-08-10 STATED BY BRYAN: the planet is substrate for a **Valheim-inspired fantasy RPG where you play a WIZARD**. Pillars: building/crafting/cooking/potions, harvesting (chop/mine/fish/garden), **high magic + lots of spells**, **portals = fast travel**, wildlife/monsters/NPCs/towns, tame+ride mounts, carts (wheelbarrow→wagon), late-game flight; later sailing/swimming/fishing. Art = low-poly Synty. **Asset rule: HARVEST-ONLY** (no vendor runtime C# ever ships; editor tools stay in scratch project). Reprioritises magic-VFX + portals to top-three; survival/crafting/building is the biggest zero-code gap. **2026-08-12:** Synty = default + tie-breaker but NOT absolute — richer geometry wins (kept 2 toon oaks with real leaf canopies over a Synty trunk→leaf-sphere); caveat: that ref was the *Generic* filler pack, not the NatureBiomes tree the game plants.
