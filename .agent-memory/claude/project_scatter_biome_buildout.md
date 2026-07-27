---
name: project-scatter-biome-buildout
description: Scatter biome coverage + FoliageLit foliage pipeline conventions (branch scatter-placement)
metadata:
  type: project
---

Scatter placement now covers **all 14 land biomes** (branch `scatter-placement`, built 2026-07-26). `ScatterLibrary.asset` = 42 prototypes; prototype assets in `Assets/Resources/Settings/Scatter/`. Ocean/Cave/Underwater intentionally have no surface scatter.

**FoliageLit shader** (`Assets/Graphics/Shaders/FoliageLit.shader`) is the tree/plant shader. Hard-won rules:
- **Never point `_TrunkMap` at a Synty prop atlas** (e.g. `PolygonNatureBiomes_*_Texture_01`). Those atlases hold icons/palette/skull decals; a tree's separate *branches* mesh UVs span the whole atlas, so corner UVs render as icon cards. Always read the SOURCE prefab's branch material for the real bark texture (e.g. `Branches_01.tga`). Vertex-colour BLUE is the leaf/trunk mask (0 trunk / 1 leaf); it is clean bimodal on Synty nature trees.
- `_ForceLeaf` (toggle) lifts the leaf mask to 1 for all-B0 cutout props (moss beards, reeds) so they alpha-cut instead of rendering solid.
- Double-sided: `Cull Off` + `IS_FRONT_VFACE` normal flip on both passes (foliage cards vanished/blackened without it).
- `_LeafNormalUp` (0..1, default 0.6) blends leaf normals toward world-up so dense canopies read soft, not dark clumps. Per-material knob; tune in-scene.

**Per-biome slot convention** (SlotId must be unique within a biome; reused across biomes): 0 = legacy generic tree, 1 = bush, 2 = rock, 3 = hero tree, 4 = grass/fern/reeds, 5 = wildflowers/forest-fern. GOTCHA: existing **Birch is slot 4**, not 3.

**Import workflow**: copy FBX + its `.meta` and textures + their `.meta` from `D:\Downloads\!3D Sources\Extras\Synty` (preserves guid + `useFileScale=1`). PolygonGeneric props share one atlas material (`50cb739c…`) — reuse it for rocks/bushes with the legacy single-Material prototype path (no Parts).

**Gaps / future**: no cactus (Desert = dead tree + rock), no snow-specific trees (Snow = pine), `PNB_Enchanted_Forest` pack unused, mushrooms/lilypads not wired, snow/autumn `_SeasonColor` tinting not applied. Foliage still reads dark under backlight — a diffuse wrap / lit underside is the next polish lever if wanted. Related: [[project-grass-layering-arc]].
