---
name: normal-mapping-flat
description: "Known issue — terrain looks flat in normal play despite Phase B step 8 normal-mapping pipeline being wired end-to-end and texture arrays loaded 16/16 from source"
metadata:
  type: project
---

Phase B step 8 shipped triplanar normal + ARM sampling 2026-05-31, but terrain still appears flat under direct lighting. Several investigation cycles tried without solving it.

**What we confirmed works:**
- All three texture arrays (`_BiomeAlbedoArray`, `_BiomeNormalArray`, `_BiomeArmArray`) loaded 16/16 slots from source PNGs (no placeholder fallback) — per `[BiomeSurfaceTextureArrays] BiomeNormalArray: 512x512 RGBA32, 16/16 slots from source` diagnostic.
- Normal viz mode (`DEBUG_TERRAIN_SURFACE_NORMAL`, mode 83) shows vivid rainbow speckles when displaying `(surfaceNormalWS - geometricNormal) * 20` — proves the normal sample is producing non-trivial perturbation.
- AO/Roughness debug modes (84/85) show source texture content with mild variation.

**What we tried:**
1. Added `_BiomeNormalStrength` material slider so subtle ground-PBR maps can be amplified.
2. Fixed `ScaleTangentNormal` bug where `tn.z = sqrt(saturate(1 - dot(xy,xy)))` collapsed z to 0 when scaled xy exceeded unit length, flipping normals entirely sideways. Switched to `normalize(tn)` after xy scale.
3. After fix: terrain still looks flat per Bryan's eye test.

**Likely remaining causes (untested):**
- The custom analytic-sun lighting equation `dayLight = lerp(0.34, 1.08, terrainDiffuse)` compresses diffuse variation into a narrow 0.34→1.08 range. Even strong dot-product changes from bumpy normals translate to small brightness deltas. A real PBR pipeline (Cook-Torrance, full range 0..1+ HDR) would make bumps far more visible.
- Source ground PBR maps may have subtle normals by design. Sand/dirt/grass textures from `local-only/Game Buffs/Free Realistic Nature Textures` aren't intended for "obvious bumpy rock" appearance.
- Triplanar tiling at `_BiomeTriplanarTiling = 0.065` means each tile spans ~15 world units. At normal viewing altitude the per-pixel normal variation is sub-pixel after rasterizer filtering — visible in close-up captures but washed out at distance.
- Possible: lighting being overwhelmed by URP cascaded shadow / ambient term that doesn't respect our custom analytic normal path.

**How to apply when picking this up:**
- First check if there's a HEMISPHERIC ambient term making everything bright regardless of normal — if so, scale it by `dot(geomN, sunDir)` not by 1.0.
- Consider replacing the `lerp(0.34, 1.08, terrainDiffuse)` compression with a wider response curve so normal variation reads on screen.
- Consider procedurally generated obvious-bump normal maps as a control test (sine waves or noise) to rule out source PBR being too subtle.
- Bryan accepted moving past this 2026-05-31 to keep momentum; revisit if the rendering pipeline gets touched anyway in Phase C+ or a future polish pass. [[project-current-focus]]
