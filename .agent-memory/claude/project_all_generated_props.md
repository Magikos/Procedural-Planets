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

## 2026-08-24: NO trim sheets for vegetation — decided, revisit at buildings

Bryan was told the project "should be using trim sheets" and asked. Answer: no, not for
vegetation. **Measured** from the live library (176 renderable prototypes, 291 parts):

- **20 distinct textures** across all vegetation — already consolidated. A trim sheet
  consolidates textures; there is nothing left to consolidate.
- **630 indirect draw bands**, one `RenderMeshIndirect` per prototype-part-LOD. Those are
  driven by distinct MESHES, not materials. **Sharing a texture or material cannot reduce
  them** — the classic trim-sheet win (fewer materials → static batching merges meshes) does
  not exist on a GPU-instanced indirect path.
- 111 materials collapse to **13 distinct (shader + texture set) combinations**. The other 98
  exist only to carry a tint — 39 materials share `leafPatch_04`+`Branches_01` across 32
  colours.

Terminology trap: a trim sheet is strips of hard-surface detail (edges, mouldings, panels)
that geometry UVs onto in bands. The vegetation equivalent is a **foliage atlas** of leaf and
branch clusters — which we already use (`_BaseMap` leaf atlas + `_TrunkMap`).

Cost of doing it anyway: leaf cards need generous padding or they bleed at mip levels, which
already bit us across 104 impostor atlases (`mipMapsPreserveCoverage`, `alphaTestReferenceValue`
— see [[project_scatter_lod_impostor]]); it also caps per-species texel density while the
species set is still churning.

**The real lever, if draw/material cost ever matters:** move tint from the material to
per-instance data (instance buffer or vertex colours), collapsing 111 materials toward 13. No
new art needed — the tints already exist. NOT measured as a win; nobody has profiled whether
630 draws or 111 materials cost anything at current framerates.

**Revisit when buildings/crafting land.** That kit — walls, floors, roofs, doors, posts — is
the textbook trim-sheet case, and UVs are expensive to retrofit, so decide before authoring it.

Related: [[project_tree_generator]], [[project-scatter-lod-impostor]], [[project_ocean_scatter]]
(underwater is blocked on a PLACEMENT axis, not geometry), [[project_scatter_clumping_direction]].
