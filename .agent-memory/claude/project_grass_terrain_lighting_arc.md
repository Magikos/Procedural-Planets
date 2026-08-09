---
name: project-grass-terrain-lighting-arc
description: "2026-08-01: fixed impostor shadows + grass-blanket brightness (3b052dc) and the bright-green biome-edge LINE (1abe68a). The line was the terrain grass-surface OVERLAY (green-over-tan reads luminant at borders), NOT biome-color blend or blades. GOTCHA: shader-CODE edits don't hot-reload in play mode; force AssetDatabase.ImportAsset."
metadata:
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
  modified: 2026-08-03T14:09:43.161Z
---

## 2026-08-01 — grass/terrain lighting fixes (branch `scatter-placement`)

Continuation of the scatter fly-feedback round. Three lighting issues from user fly-tests.

### Impostor shadows + grass blanket brightness — `3b052dc`
- **ScatterImpostor.shader took NO shadow** (planet-sun diffuse + SH only) → distant billboards
  glowed under cloud/hillside shadow while mesh trees/grass darkened. Added `MainLightRealtimeShadow`
  + `CloudShadowFactor` to the ForwardLit pass (mirrors FoliageLit leaf path, 0.5 floor). Matters now
  bushes/rocks impostor from ~100-210m (inside 250m shadow dist). Shadow audit: terrain, FoliageLit,
  Scatter.shader all already OK; only the impostor was missing.
- **Grass blanket ~32% darker than near blades** (Grass.shader): the far blanket is the cheap fake of
  the near grass so must match brightness. Two albedo floors raised: `edgeShade` 0.55→0.80,
  `_GrassChunkFade` map 0.45→0.85 (line ~389-391). Albedo-level (below the shadow term) so brightening
  never makes grass glow in shade.

### Biome-edge bright-green LINE — `1abe68a` (the hard one; I was wrong twice first)
- **NOT moisture/river. NOT the biome-color blend. NOT the grass blades.** It is the terrain
  **grass surface-overlay** (`ApplyGrassSurfaceAlbedo` in PlanetVertexColor.shader) — the fake-grass
  colour painted on the ground so it reads grassy from altitude (`_GrassSurfaceBrightness`,
  `_GrassFarOverlayStrength`, `_GrassFarOverlayOrbitStrength` on `Planet/VertexColor`).
- **Mechanism:** the overlay green is a fixed lush tint. Where the grassy biome's grass DENSITY bleeds
  onto the bright TAN arid ground past a border, green-over-tan mixes to a luminant lime → bright line,
  brighter than the same green over the darker green interior. (Biome dominance does NOT discriminate it —
  the band sits at high dominance where terrain is already tan; tested, no effect.)
- **Fix:** gate the overlay by terrain greenness: `grassCoverage *= saturate((albedo.g - max(albedo.r,albedo.b))*8.0 + 0.45)`.
  Keeps overlay on green interiors, recedes onto arid ground, border carried by the biome colour blend.
- Earlier commit `91c5d75` widened `BiomeMapBaker.KernelRadius` 6→12 (softened the broad terrain/grass
  colour transition) but did NOT touch this thin overlay line — separate layer.

### GOTCHAS (cost me many wasted cycles)
- **Shader-CODE edits do NOT hot-reload in play mode.** `refresh_unity` + even stop/play left the OLD
  compiled shader running (edits looked like they did nothing). Fix: `UnityEditor.AssetDatabase.ImportAsset(path, ForceUpdate|ForceSynchronousImport)` forces the recompile live. Material-PROPERTY changes
  (`material.SetFloat`) DO apply live; loose shader GLOBALS via `Shader.SetGlobalFloat` only work once the
  shader with that global declaration is actually recompiled (force-import first).
- **Diagnose terrain layers with the biome debug modes:** `debug.mode TerrainSelectedAlbedo` (textured,
  has the band) vs `BiomeMapFlatColor` (biome colour only, smooth, no band) vs `BiomeMapPrimaryId`
  (biome ids) vs `GrassLodCoverage`. `debug.mode <Name>` changes the game-view render; screenshot it
  yourself. Zeroing the overlay material props (`_GrassSurfaceBrightness=0` on the `Planet/VertexColor`
  materials, ~94-116 chunk instances) makes the band vanish = proof it's the overlay.
- Grass tiers share Grass.shader; both take main + cloud shadow via `MainLightRealtimeShadow` +
  `CloudShadowFactor` (line ~406-418). See [[project-scatter-gather-perf]] for the scatter side.

## 2026-08-02 — step-test harness + snow/foliage look pass

- **Step-test harness** (`427e39c`): `camera.step <m>` (FreeCameraController — walk forward along the
  surface tangent, hold altitude+aim) + `Assets/Resources/ConsoleScripts/Step Test.txt` console script:
  `camera.teleport "Step Test"` → freeze noon → `debug.screenshot` + `camera.step 10` ×6. Reproducible
  labelled dataset in `local-only/debug-screenshots` (F10-Step Test-<runid>-stepNNN). USE `debug.screenshot`
  (one image of current debug.mode) NOT `debug.capture` (iterates the active capture set → wrong modes).
  Bryan saves the pose via `camera.save-teleport "Step Test"` then `script.run "Step Test"`.
- **Diagnoses from the harness:** grass mid-hill dark band = near-field(144-200m) → far-blanket(fade 128-220,
  peak cov 0.42) LOD handoff RING (GrassLodCoverage shows concentric rings); grey far tree line = ATMOSPHERE
  aerial-perspective on distant MESH trees (persists with `atmosphere.mie 0`), NOT impostors; black trees on
  snow = dark conifer albedo × FoliageLit flat floor, near-black vs blown-white snow.
- **Look-pass fixes (`d1643b4`), verified at snow + grassland:** ScatterImpostor canopy normal tilted 0.6
  toward billUp (catches overhead noon sun; fixes grey far trees + dark rock/bush billboards). FoliageLit
  day term 0.68/1.15 → 0.82/1.25 (lifts dark trees). Snow.asset albedo 0.95→0.80 (stops blowout). Grass
  ChunkPeakDistance 220→190 (blanket peaks before near-field ends → closes the dip).
- **CRITICAL regen gotcha:** live shader-reimport does NOT reach runtime-baked IMPOSTOR materials or asset
  re-bakes (Snow albedo, biome atlas) — verify those with a clean stop→play REGEN, not a force-reimport.
  A/B on the wrong target (tree line = fog, not impostor) wasted cycles; verify the RIGHT surface.

## 2026-08-03 — REVERTED the greenness gate (`1f29045`)

The `1abe68a` terrain-greenness gate on the far grass surface-overlay was WRONG and caused a worse
regression: it keyed the overlay on ground COLOUR, but savanna grows dense grass on TAN ground, so the
gate suppressed the overlay there and distant savanna read as **barren brown** while being lush up close
(the far-field LOD under-represented the real state). Savanna-grass-on-tan and a forest-green bleed on tan
are identical to a colour test — terrain colour can't distinguish them. Dropped the gate; the overlay now
paints grass wherever grass DENSITY is high on any ground colour. For the biome-edge line (the reason the
gate existed) trimmed overlay saturation 0.82→0.72 instead (the line popped as a vivid GREEN-vs-tan
hue/saturation pop, not a brightness pop). Verified: distant savanna now greens up + matches the up-close
walk-in. LESSON: the far overlay must track grass DENSITY, never terrain colour.

## 2026-08-03 (later) — savanna close/far: DARK DOTS = distant scatter too dark (`b1afc31`)

Marker close/far comparison showed the real mismatch is NOT the terrain (Savanna albedo is golden-tan
0.82,0.72,0.22, fine) NOR the overlay (a minor far-only lever): the savanna BUSHES/scatter render bright
yellow up close but as **dark dots** in the mid-far. Confirmed NOT shadows (dots stay compact at a
near-horizon sun). Cause: impostor is baked as unlit albedo but UNDER the prop shader's own low floor with
no directional light at bake time → pre-dimmed card, then runtime impostor lighting dims it again. Fix:
raised the lit FLOORS — Scatter.shader 0.55→0.72, ScatterImpostor.shader 0.6→0.85 (peak 1.12→1.28) to
offset the baked dim. Verified: mid-far dots went black→olive/coloured vegetation, near bushes unchanged
(not blown). The far overlay saturation/coverage is only a minor lever (overlay is a far-DISTANCE effect;
mid-ground is near-field blades) — don't over-tune it. GOTCHA restated: impostor materials are runtime-baked,
so ScatterImpostor edits need a full stop→play REGEN to re-bake, not a force-reimport.
