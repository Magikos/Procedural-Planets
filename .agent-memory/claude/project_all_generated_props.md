---
name: project_all_generated_props
description: Bryan's direction (2026-08-17) — replace every Synty scatter prop with generated geometry, and eventually bake ALL generated meshes at build time so nothing generates at runtime
metadata:
  type: project
---

**Bryan's stated direction, 2026-08-17.** Replace every Synty scatter prop with our own generated
geometry, biome by biome, and generate anything a biome is missing rather than hunting for an asset.
Order he gave: trees first, then flowers/reeds/etc, then lily pads/coral/seaweed.

**THE FINAL GOAL, explicitly restated by Bryan and NOT yet built: bake the generated MESHES at build
time so nothing is generated at runtime.** Only the impostor *atlases* are baked today
(see [[project-scatter-lod-impostor]]). Meshes are still generated at boot because it measures at only
195 ms (45 ms all trees, 150 ms all rocks), and keeping them runtime preserves the
edit-a-TreeDef-and-hit-play tuning loop. Bryan accepted that ordering — "It's fine, just don't forget
that is the final goal" — on the condition it happens once the species set stops churning.

The argument for finishing it is NOT load time (195 ms will not grow much). It is ownership: a baked
mesh is an asset you can open, inspect, hand-tweak for a hero tree, put in a prefab, attach a collider
to, or hand to someone. A boot-generated mesh exists only in memory. Bryan's words: "use our
generation tech to own the design and final look completely."

Shape of the remaining work: extend `GeneratedImpostorManifest` with `Mesh[]` fields, have the bake
tool write meshes as sub-assets beside the atlases, and have `TreeInjection`/`RockInjection` load
instead of generate. Symmetrical with the atlas path — same tool, same play-mode guard, same
per-species probe hash. Roughly an hour. Afterwards the generator becomes editor-only and never ships.

**Two constraints Bryan set on the way there:**
- Pack/hand-modelled meshes stay welcome, but as **landmark pieces** (the giant tree on the hill), not
  common filler — mixed faceting density and palette is worse than either style alone. Baking erases
  the runtime distinction anyway: a generated prop and a pack prop are both just a prototype with
  meshes + an atlas.
- A hand-modelled tree does NOT get harvest for free. `GeneratedTree` carves `Stump`/`Log`/`ChopHp`/
  `WoodYield` from the same skeleton; a pack tree needs `StumpMesh`/`StumpMaterial` authored.

Related: [[project_tree_generator]], [[project-scatter-lod-impostor]], [[project_ocean_scatter]]
(underwater is blocked on a PLACEMENT axis, not geometry), [[project_scatter_clumping_direction]].
