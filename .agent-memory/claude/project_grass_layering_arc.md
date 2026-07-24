---
name: project-grass-layering-arc
description: 2026-07-16/17 grass visual layering arc - what shipped, Synty clumps parked, gotchas
metadata:
  type: project
---

Grass "make it look good, not a golf course" arc, 2026-07-16..17 on branch `code-refactor`.
Goal (Bryan): mixture of terrain-grass base + variable non-uniform tufts + scatter, layered
so the near field is lush and grass reaches the horizon. Papers/examples in
`local-only/` (InfiniteGrass, GrassFlow, Stylized Grass, JAHRMANN RRTG paper);
Synty PolygonNatureBiomes at `D:\Unity\Explore Assets\Assets\Synty`. Research doc:
`docs/research/2026-07-14-grass-far-field-research.md`.

**Shipped and working:**
- **Far-field blanket** revived: `PlanetVertexColor.shader` `ApplyGrassSurfaceAlbedo` /
  `EvaluateGrassOverlay` (was disabled since the biome-stripe fight). Fix was the documented
  "linear coverage + toe cut" - replaced the concave `pow(density,0.62)` lift (the stripe
  cause) with linear coverage minus a 0.12 toe. Baked ON (`_grassBlanketEnabled = true`,
  PlanetGrassCoordinator). Tint ramps in 35-260 m then holds to the horizon into aerial haze.
- **Clump identity on blades** (`Grass.shader`): ~1.1 m world cells give each tuft one coherent
  height + colour (green<->golden), killing the uniform-lawn look. Per-blade hash is fine
  jitter on top.
- **Scale fixes** (biome `.asset`s in `Assets/Game Data/Planet Settings/Biomes/`): grass height
  was 1.5 m (player height!) -> Grassland 0.4, Savanna 0.5, etc. Blade width 0.08 -> 0.02
  (was fat triangles), more bend/curve. Near-field spacing 0.35 -> 0.25 + capacity 1M -> 1.5M
  (was sparse).
- On the **real Planet scene** the grass reads green + dense + clean borders. The diagnostic
  grid test scene (`Scenes/Tests/Grass.unity`, `DiagnosticGridBiomeField`) does NOT populate
  the terrain biome-density map, so ground/colour reads there are misleading - always verify
  on the real planet.
  **Scope of that caveat (clarified 2026-07-24):** it applies ONLY to the terrain
  biome-density map / ground-colour texture the blanket shader samples. It does NOT mean the
  scene lacks biome data. `ColorGenerator.ResolveBiome` (`ColorGenerator.cs:328`) evaluates
  `_biomeAssignmentField`, which in that scene IS the `DiagnosticGridBiomeField` — so
  `IBiomeProvider.EvaluateBiome` returns real Primary/Secondary/BlendWeight there. Any CPU
  consumer of `EvaluateBiome` (e.g. scatter placement) works fine in the grass scene, and its
  deterministic 5x5 grid of known borders is actually a BETTER bed for biome-border tests.
  Layout: `Game Data/Planet Settings/Tests/GrassDiagnosticBiomeGrid.asset` — face 5, 5x5,
  BlendWidth 0.01 (tight), contains Grassland x10 and Forest x2 among others.

**Reverted / dead ends:**
- Textured tuft cards (Synty `Grass_01.tga` on the cluster-card LOD): reverted. Synty is
  MESH-based, no billboard grass-alpha texture. `Grass.shader` keeps the `_GrassCardStrength>0`
  path for a real grass-billboard alpha later; currently 0 (procedural carved-blade cards).
- Terrain ground-darkening under grass: reverted - its density gradient made a lighter ring at
  biome borders on the real planet, and the real ground reads fine without it.

**PARKED - Synty clump scatter** (`GrassClumpScatter.cs`, `Enabled = false`). Multiple blind
code-only attempts did NOT converge - do not keep reconstructing it from `RenderMeshInstanced`
+ a hand-built URP/Lit material. What broke and why:
1. **Scale** - auto-scale from `mesh.bounds.size.y` still rendered huge clumps; the FBX's real
   mesh (sub-meshes / pivot / bounds) can't be trusted sight-unseen.
2. **Material/texture** - Synty grass uses their OWN shader (guid `9b98a126c8d4...`, alpha-clip
   cutoff 0.3, double-sided `_Cull:0`). URP/Lit + the raw atlas reads wrong (their shader does
   vertex-colour tinting + specific atlas UVs). Matching cutoff/cull didn't fix the look.
3. **World-stability** - clumps slide with the camera because the tangent frame is derived from
   the camera position (`camPos - center`), which rotates as you move. A camera-derived frame is
   NOT sphere-stable. Needs cube-face (face-space) cells like the near field
   (`FaceSpaceCellRangeBuilder` / `GrassNearFieldPlace.compute` stable-cell hash).

Correct path when resumed: **import Synty's actual grass PREFAB** (carries correct mesh +
material + shader + scale) and instance/pool THAT, or set it up hands-on in the editor - instead
of rebuilding the material/scale in code. Then only the placement (cube-face-stable cells) is
custom. Grass looks good WITHOUT clumps, so this is optional polish.

Next planned: holistic colour/lighting pass with all layers in (Bryan's call, on the real
planet). See [[project-current-focus]].
