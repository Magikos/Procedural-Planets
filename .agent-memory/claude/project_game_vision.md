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
The comparison was against Synty's weakest tree. Re-run before treating it as settled.

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
