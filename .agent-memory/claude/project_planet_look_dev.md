---
name: project-planet-look-dev
description: 2026-07-28 planet surface look-dev toward Synty POLYGON Meadow/Forest — post/lighting/density/grass; root causes + how the grass and sun systems actually work
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
  modified: 2026-07-31T16:00:37.538Z
---

**2026-07-31 cohesion pass** (commit `34ed07c`): Bryan — "assets look plopped, colors clash, stand out
too much." Fix = UNIFY the palette, not add groundcover (savanna stays open). Grade
(`PlanetLookProfile`): saturation 46->30 (pull vivid greens toward the ground), contrast 8->4 (LIFT the
crushed near-black shadows/rock undersides — the black blobs were half the clash), bloom 2.2->1.75 +
threshold 0.85->1.0 (less washout), colourFilter (1,0.94,0.83) slightly warmer to tie foliage/ground/rock
into one warm family. Rocks: `ScatterProps.mat` (`Scatter/VertexColorLit`, shared by all rock prototypes)
`_BaseColor` white->warm (1.55,1.42,1.15) — rocks are dark because the Synty mesh VERTEX COLOURS are dark;
`_BaseColor` multiplies them, lifting to warm mid-gray. All hot-reload live (no regen). Aerial before/after
confirmed softer/harmonized; ground-level rock verify was blocked by the usual TryGetSurfaceRadius mis-sample.

Look-dev arc on branch `scatter-placement` (2026-07-28), goal = planet surface views like the Synty
POLYGON Meadow/Forest marketing render (lush, warm, dense). Bryan's Synty source (incl the exact
`PNB_Meadow_Forest` pack + its demo `Global Volume Profile`) is at `D:\Downloads\!3D Sources\Extras\Synty`.

**2026-07-30 Synty-foliage + grass-gradient pass (branch scatter-placement):** committed, verified.
- FoliageLit leaf colour-noise (2-freq value noise, warm/cool tint) `356081d` and Synty **leaf AO**
  from vertex-colour G (leaf-only via leafMask, trunk G=0 untouched) `13217bf` — verified in AssetShowcase.
  Meadow tree vtx colours: `LOD0 R.32/G.55/B.88`, `Branches R.30/G0/B0` → B=leaf/trunk mask, G=leaf AO.
- Grass **BOTW colour ramp** `451b766`: [Grass.shader](Assets/Graphics/Shaders/Grass.shader) replaced the
  brightness-only `heightShade` (0.62→1.0) with a per-blade colour ramp `lerp((0.40,0.52,0.32) root,
  (1.16,1.15,0.74) tip)` — deep cool base → bright warm tip (tip>1 = sunlit-edge glow). Verified on planet:
  lush bright warm-green carpet. Baked strength (no console knob yet).
- **Capturing grass on the planet is fiddly** (workflow worth reusing): grass is compute-blanket and only
  emits on biomes with authored `GrassDensity>0` (Grassland .95, Tropical .85, Forest/Savanna/Scrub/Steppe/
  Swamp/Taiga .8; Beach/Desert/Ocean/Mountain/Snow/Tundra/IceBog = 0). Grass is culled on slopes >~35°.
  Day/night cycle is **120s** so the sun races → freeze it: `CelestialManager.SetTimeFrozen(true)` +
  `SetTimeOfDay(0.5)`, then read the real sun via `Shader.GetGlobalVector("_SunParams")`. Find a spot with
  `Planet.TryGetSurfaceRadius(dir)` + `TrySampleClimate` (scan for flat, well-above-sea, sun-facing land);
  `TryRaycastSurface` is UNRELIABLE (hit a low-LOD proxy ~200m below the real surface). To place the cam,
  disable `FreeCameraController` (else it re-clamps), set transform, capture, then re-enable + unfreeze.
  Restore knobs after: `fcc.enabled=true`, `SetTimeFrozen(false)`, `cam.fieldOfView=60`.
- Foliage **interactor push HOOK** `2f099c6`: FoliageLit reads the same global `_GrassInteractors` buffer
  the grass uses; per-material `_InteractiveBend` (plant height m; 0 = rigid) gates it so trees/rocks are
  untouched, root planted, bend grows toward the top. Folded into `ApplyWind` (all passes consistent).
  **Still INERT** - no flower/fern material sets `_InteractiveBend` yet (user wants ferns/flowers/bushes/
  grass-tufts pushable, trees rigid). Wiring + a live-interactor verify is the next step.
- **REGRESSION this introduced + fix** `e4301d3`: adding `#include GrassInteractors.hlsl` to FoliageLit
  made it declare the global `_GrassInteractors` StructuredBuffer. Where that buffer is UNBOUND (the
  AssetShowcase - no GrassInteractorRegistry), the driver **silently drops every FoliageLit draw** ->
  ALL showcase foliage went invisible (bounds exist, isVisible=true, but nothing rasterises). Planet is
  fine (registry binds it at init). Fix: `AssetShowcaseController` binds a 1-element dummy buffer + count 0.
  Lesson: any non-planet scene rendering FoliageLit must bind `_GrassInteractors`.
- **Multi-layer wind** `fcdf20a`: `ApplyWind` now = steady downwind lean + branch sway + leaf flutter
  (3 sin layers, branch+leaf spatial phases), net-positive so foliage never blows upwind; uses `_WindFreq`.
  Verified in showcase (exaggerated flex): canopy leaves shear downwind, trunk stays vertical, calm=still.
  NOTE all foliage `.mat` ship `_WindStrength: 0` (only `FoliageWildflowers`=0.05), so trees DON'T sway
  by default even with wind - flex is authored per material.
- **GOTCHA**: `material.SetFloat` on a scatter/foliage SHARED material during Play mode dirties the
  material ASSET in memory; `AssetDatabase.SaveAssets` (or the user saving) writes it to disk. After any
  runtime material tweak, restore originals from disk (read the `.mat`) or `git checkout` the `.mat`.
  Saving materials also bakes new shader-property defaults into the `.mat` (churn) - revert those too.
- **Leaf/trunk normal maps = SKIP**: POLYGON foliage is flat-shaded; `PNB_Meadow_Forest` ships normals
  ONLY for the LOD impostor cards (`treeMeadow_01_Normals`, `treeBirch_01_Normals`), none for meshes.
- **Asset import** from `D:\Unity\Explore Assets\Assets` (2026-07-30, commit `3990883`):
  - **LMHPOLY Low Poly Nature Bundle** = the win. Imported a 16-mesh subset (flowers/grass3D/reeds/
    mushrooms/bushes) + its 256px flat-colour atlas to `Assets/AssetPacks/LMHPOLY_Vegetation/`. Renders
    CORRECTLY through FoliageLit with **material config only, no shader-code change**. Recipe: atlas
    Point + mip OFF (flat-swatch atlas bleeds otherwise); `_ForceLeaf 1` (meshes have NO vertex colours
    so force the leaf path); `_LeafAOIntensity 0`; `_InteractiveBend 0.8` + `_WindStrength 0.12` (push +
    sway free). These are the real flower meshes the old scatter `Wildflowers` prototype lacked. Full pack
    has Flowers 120 / Grass3D 16 / Reeds 12 / Bush 43 / FlowerBush 43 / Mushrooms 68 to scale in.
  - **Toon Fantasy Nature (TFF) trees = BLOCKED for FoliageLit.** Their `TFF_Atlas_1A_D` is a toon
    **gradient-LUT** (grid of dark->light hue swatches); TFF's shader picks a swatch by UV then samples UP
    the gradient by the lighting dot-product. FoliageLit (plain albedo by raw UV) reads across all swatches
    => rainbow-confetti canopy (trunk ok, leaves garbage). Meshes: 3 LODs, 2 submeshes (bark+leaf), no
    vertex colours. To use TFF trees would need their own toon shader (lights from URP main-light + outlines,
    CLASHES with our `_SunParams` planet lighting) OR a custom planet-lit toon-LUT shader (real work) OR
    bake the LUT atlas to flat albedo. We already have Synty trees, so TFF is deferred. Test folder deleted.
- **LMHPOLY scatter wiring** (2026-07-30, commit `91f55fd`): wired **15 new ScatterPrototype .assets** into
  the free library slots (37, 42-49, 56-61; `ScatterLibrary.asset` 47->62). Individual wildflowers
  (blue/red/yellow/purple/white/pink -> Grassland/Savanna/Steppe/Tropical/Forest/Taiga), flowerbushes
  mushrooms (Forest/Taiga/Swamp), a reed (Beach). Small plants use `LMHPOLY_Vegetation` (pushable).
  Each prototype: Parts[0] = {material, LodMeshes=[single mesh], LodEndDistances=[45-110]}. **Verified
  on-planet**: grassland shows blue/red/yellow wildflowers among the grass. Prototypes are separate
  `.asset` files in `Resources/Settings/Scatter/`, referenced from `ScatterLibrary.Prototypes[]`.
  **SlotId hard cap 0-63**.
  **FlowerBush prototypes REMOVED** `24e741a` (Bryan: out-of-place bright-lime smooth domes; library
  62->60, slots 56-57 freed). Bush meshes + rigid `LMHPOLY_Bush` mat kept on disk for a possible
  better-toned bush later. Do NOT re-add FlowerBush without reworking its tone/shape first.
- **BIG capture-workflow win**: use the debug console `scatter.goto <Biome>` (e.g. `scatter.goto Grassland`)
  to teleport the camera to a REAL surface point of that biome - reliable, unlike my manual
  `TryGetSurfaceRadius` scans that kept landing on submerged shelves/water. `scatter.count` reports
  per-prototype instance counts at the camera. Run via `ConsoleController.RunCommand("scatter.goto Grassland")`
  (find the ConsoleController MB; RRunCommand is void, output goes to the in-game overlay). Re-enable
  `FreeCameraController` first so goto + chunk streaming work; after a big teleport wait ~7-10s for chunks
  to stream in (black+stars frame = terrain not streamed yet, not a bug).
- **TEM (Toon Enchanted Meadow) vegetation adopted** (2026-07-30, commit `f4d387d`). KEY: a Synty **toon**
  pack (TEM_CustomToon shaders, TEM_Atlas_1A = gradient-LUT like TFF) BUT its **vegetation** uses a
  SEPARATE real-textured atlas (`TEM_Atlas_Vegetation_1A`, 2048, alpha cutout) -> renders correctly in
  FoliageLit (unlike the props/structures which are gradient-LUT toon). TEM grass clumps + leafy
  flower-bushes are nicer/denser than LMHPOLY. Imported 24 meshes to `Assets/AssetPacks/TEM_Vegetation/`
  (`TEM_Vegetation` pushable + `TEM_Bush` rigid materials, same FoliageLit recipe: _ForceLeaf 1, AO 0,
  _Cutoff 0.4, mip ON - it's real art, NOT the flat-swatch Point/no-mip treatment).
  - Swapped the 3 Synty grass scatter prototypes (Grassland/Savanna/Steppe) to TEM Grass_Patch IN PLACE;
    added TEM Forest Grass + Swamp Grass + 2 TEM flower-bushes (Grassland/Forest). **Library now 64 =
    SlotId CAP (0-63 full)**. No free slots left - any further prototype needs one freed or a bigger id scheme.
  - **KEPT LMHPOLY wildflowers**: TEM flowers are blue/white ONLY; LMHPOLY gives red/yellow/purple/pink.
  - **TGA bloat gotcha**: TEM veg atlas source was a 67MB 4096 TGA; Unity caps import at 2048 anyway.
    Re-encoded to a 2048 PNG (3.8MB) via `tex.EncodeToPNG()` (set importer readable+uncompressed+2048+no-mip
    first) + `File.WriteAllBytes`, re-pointed both materials' `_BaseMap`/`_TrunkMap`, deleted the TGA.
    Repo tolerates ~16MB textures, no LFS - keep big source textures downsized to <=2048 before committing.
  - Observed: TEM grass tufts read a touch DARKER than the bright BOTW grass carpet - tunable (raise tint/
    lower saturation on `TEM_Vegetation`) if it bothers.
- **Synty toon packs (TFF, TEM props) recap**: gradient-LUT atlases (palette of dark->light hue swatches,
  shader samples up the ramp by lighting) => rainbow in FoliageLit. Need a planet-lit toon-LUT shader
  (declined) OR use only their real-textured veg atlases. Bryan: stick with Synty (POLYGON/flat) + these
  flat-veg atlases; no custom toon shader.
- **Golden-meadow look-dev pass toward `local-only/Target Example.png`** (2026-07-30, commit `cc2917d`).
  Target = Synty sunset meadow: open rolling grass, sparse trees, dense red reeds by water, warm/pink sky.
  - **Tree density is the biggest meadow-vs-forest lever** - but 45 m was an OVER-correction (commit
    `f3b9da2` fixes it): Bryan said 45 m read barren / "lost the oaks". IMPORTANT: Grassland has THREE
    stacked oak prototypes (Meadow green + Golden + Autumn, all `SM_Env_Tree_Meadow_01`), so the EFFECTIVE
    spacing is ~each/1.4, much denser than the per-prototype number. 17/19/19 -> ~12 m effective = a wall;
    45/45/45 -> ~33 m effective = barren. Landed on **28/32/32 -> ~20 m effective = lush but open**, matching
    the single-prototype Swamp Tree at 24 m which reads lush+walkable. Savanna 28, Steppe 32, bushes 6-7.
    (Forest biome stays dense at 13-17 m - correct.) When judging density account for stacked prototypes.
  - **Red reeds**: `FoliageReeds.mat` `_SeasonColor` -> warm red (1.5,0.55,0.30); reed prototypes densified
    (spacing 2.6, weight 1.5). Verified: dense crimson reed clusters in swamp = the target's red reeds.
  - **Warm/dreamy grade** (`Assets/Settings/PlanetLookProfile.asset`): WhiteBalance temp 14->22 +tint 4,
    ColorAdjustments warm colourFilter (1,0.95,0.86) + exposure 0.45->0.5 + contrast 10->8 + sat 42->46,
    Bloom intensity 1.5->2.2 / threshold 1.05->0.85 / scatter 0.85.
  - **Warm TEM grass**: `TEM_Vegetation`/`TEM_Bush` `_SeasonColor` warmed (were dark/cool vs the carpet).
  - **Still-open gaps vs target**: PINK/lavender golden-hour SKY not reproduced - our atmosphere warms the
    horizon at low sun but the upper sky stays blue-ish; a specific grassland spot may never get a low sun
    (its latitude vs the fixed sun-orbit axis), so golden framing is spot-dependent. Lily pads on water not
    added. Would need atmosphere sunset-colour tuning for the full pink dome.
- **Material re-save churn reminder**: `AssetDatabase.SaveAssets` after runtime material edits re-serializes
  ALL touched materials, adding shader-property-default churn (`_ColorNoise*`, `_FORCELEAF_ON` invalid-keyword,
  `m_EnableInstancingVariants`). `git checkout` the pure-churn `.mat`s (kept only the ones with intended value
  changes). Watch for LMHPOLY/other mats getting dirtied when you only meant to touch reeds/TEM.
- Remaining: pink-sky atmosphere tune; wire+verify plant push on a live interactor; far-hill horizon tint.

**Root causes of the flat/dark look (fixed, commit 29f1b6a):**
- Post-processing was **OFF** on the planet camera (`renderPostProcessing=false`) — no bloom/grade/tonemap
  at all. Fixed: enabled post + HDR, added a global Volume → `Assets/Settings/PlanetLookProfile.asset`
  (bloom, saturation +28, contrast +12, exposure +0.15, WhiteBalance +9 warm, Neutral tonemap — values
  cribbed from Synty's own Meadow demo grade).
- Scene ambient was a dark static `363A42` (nothing drives it per time-of-day; only the impostor baker
  touches `RenderSettings.ambientLight`). Lifted to ~0.55 in Planet.unity so foliage is lit, not black
  silhouettes. Follow-up: drive ambient from `CelestialManager` daylight so night darkens again.

**Grass = a COMPUTE BLANKET, not scatter (commit d75144d).** Params are per-biome on the
`BiomeDefinition` SO: `GrassDensity/GrassHeight/GrassWidth/GrassClumpStrength` + tints. Blades were too
short/thin (Grassland H 0.4m, W 0.02) → stubbly. Raised H ~1.7x, W→0.045, D floor 0.8 across the 8
grass biomes → lush. Gotchas: the scatter `Grassland Grass`/`Grassland Wildflowers` prototypes have NO
mesh (placement-only) and **never render** — flowers need a real mesh. Far-field grass has a limited
render distance → bare ground + a green halo band beyond it (open grass-system follow-up).

**Bushes vs trees:** trees use `Scatter/FoliageLit` (cutout, `_LeafNormalUp` canopy-softening knob;
raised 0.6→0.85 to brighten dense canopies). Bushes use `Scatter/VertexColorLit` (Scatter.shader, solid
Synty props, `SyntyProps`/Generic_01_A atlas) — dark side-on, and over-densifying them (3m spacing) made
an ugly dark-blob carpet. Understory density was reverted to sparse; keep tree density (that helped).

**Sun / day-night for look-dev:** `CelestialManager.SetTimeOfDay(0..1)` + `SetTimeFrozen(true)`;
`SunDirection` is a world vector, so local sun elevation at the camera = `dot(SunDirection, camUp)` —
solve a good daytime angle by sweeping tod for `dot≈0.8`. Do NOT rotate the sun Light directly: it
desyncs from the atmosphere's `_SunParams` and the sky goes black. Post/lighting/material tweaks are
live in play mode (no regen); density/grass params need a regen (~3 min).

**CRITICAL lighting fact (commit 7f78d34): the planet is lit by the custom `_SunParams` sun, NOT the
URP main light.** Terrain (`PlanetVertexColor`) and grass shade from `_SunParams` via
`Includes/PlanetSunLighting.hlsl`. The URP main directional light does not effectively drive the scene,
so any shader using `GetMainLight`/`UniversalFragmentPBR` gets NO directional light — only ambient →
flat/near-black regardless of sun intensity or shadows. This was the "trees not lighting correctly"
bug: all scatter (FoliageLit trees, Scatter.shader bushes/rocks, ScatterImpostor) used URP lighting.
Fixed by shading them from `_SunParams`: `albedo * lerp(0.32, 1.18, ndl)` where
`ndl = dot(surfaceNormal, PlanetSunDirection(_SunParams, planetNormal))`, blended to night by
`PlanetDaylightFromLocalSun(dot(planetNormal, sunDir))`. **Any new prop/foliage shader that must match
the world MUST light from `_SunParams` + PlanetSunLighting, never GetMainLight.** planetNormal =
`normalize(posWS - _PlanetCenter)` (mesh) or the billboard up (impostor). Diagnose lighting-source bugs
by killing ambient + cranking the sun: what stays lit uses `_SunParams`, what goes black uses URP.

Grass/flower progress (commits 07d979d, 761be68): grass render distance 240→380m
(`DefaultGrassQualitySettings` in QualityController.cs) so the blanket reaches the hills; grass base
brightened (Grass.shader `heightShade` root 0.42→0.62).

**FLOWERS NOW VISIBLE (commit 2fdb25a, 2026-07-30).** Rebuilt the wildflower prototypes:
`Grassland Wildflowers` + `Forest Wildflowers` (biomes 6/7, slots 50/51) using `SM_Env_Wildflowers_01`
on `FoliageWildflowers.mat` (FoliageLit, `_ForceLeaf`=1 alpha-cut, `_LeafNormalUp`=0), **ScaleRange
1.4–2.3** so petals clear the grass, spacing 2.5m → ~5600/3800 instances; orange/red clusters now read
as flowers on lit grass. (Debug tip: tint `_SeasonColor` magenta to confirm placement when they hide in
grass.) The old placement-only Grassland Wildflowers prototype's library ref broke on overwrite (showed
as a null entry) — compacted the ScatterLibrary array to drop it. If a null-prototype boot exception
appears, compact the array.

**GREEN TRUNKS — REAL ROOT CAUSE + FIX (commit 69d0e1d, supersedes the e74b871/d7b5d8a attempts).**
Synty's OWN setup is the key (their source is at `D:\Downloads\!3D Sources\Extras\Synty\`, shaders in
`PNB_Core/Shaders/Foliage.shadergraph`, materials like `.../Tree_Birch_Mat_01.mat`): each tree is ONE
material carrying BOTH `_Leaf_Texture` AND `_Trunk_Texture`, split per-vertex by vertex COLOUR. The tree's
canopy mesh (`SM_..._LOD0`) CONTAINS trunk geometry — verified by enabling Read/Write on the birch FBX and
reading vertex colours: `Branches` submesh blue=0 (bark), `LOD0` submesh blue 0..1 (trunk+leaves). The
mesh DOES have vertex colours; `isReadable=false` only blocks CPU reads, the GPU shader still gets them
(so the earlier "no vertex colours" conclusion was wrong). Our bug: we split trees into Trunk+Foliage
parts, and the canopy ("Foliage") material had the LEAF atlas in `_TrunkMap` (+ birch `_ForceLeaf=1`), so
the trunk geometry inside the canopy mesh rendered green. FIX: give the 3 canopy materials
(FoliageBirchCanopy/MeadowCanopy/PohutukawaCanopy) the real bark texture in `_TrunkMap`, clear
`_ForceLeaf`, normal blend (LeafMaskLo/Hi 0.6/0.85); trunk-part mats also back to normal blend. Verified:
Savanna Pohutukawa has brown bark trunk+branches. LESSON: for split-part trees, BOTH materials need the
bark in `_TrunkMap` because the canopy mesh carries trunk geo; never `_ForceLeaf` a canopy that contains
trunk verts. Synty's Foliage shader also has multi-layer wind (Light/Strong/Twist/Gale), leaf colour-noise,
and baked leaf AO — features our `FoliageLit` lacks (candidates if matching their look further).

**PLANT WIND unison FIXED (commit cf79325).** `FoliageLit.ApplyWind` spatial phase had a ~40m period → all
plants "breathed" in unison. Raised to ~4m period + a 2nd octave → decorrelated. Weather-wind coupling
deferred (the `_WindDirection/_WindStrength01` weather globals are declared in CloudShadows.hlsl, which
double-declares against the HLSLINCLUDE across shadow/depth passes — needs a shared guarded wind include).

**WILDFLOWERS are leaf-dominant (commit cf79325).** `SM_Env_Wildflowers_01` is mostly green leaves with
tiny flower tops → at scale 1.4-2.3 it read as big green weeds up close, flowers only aggregating into
colour at distance (Bryan's "flowers disappear as I get closer" + "green weeds"). Scaled to 0.7-1.15 @2.2m.
For flowers that read up close, import a dedicated flower asset from Extras — the in-pack wildflower is
inherently leafy. NOTE: the BiomeShowcase scene is out of date (broken lighting, no LODs) — do NOT use it
to judge the models; use the Planet scene (`Assets/Scenes/Planet.unity`).

**LOW-POLY "FOREST TREE" FIXED (commit 2fdb25a).** `Forrest Prototype` (Forest biome) used the generic
low-poly `SM_Gen_Env_Tree_01` (1874 verts, faceted blob) via the legacy root-mesh layout, conspicuous
next to the detailed Nature-Biomes trees (Meadow 44687v, Swamp 60803v). Repointed it at the detailed
Meadow tree's 2-part (trunk+foliage) setup via `SerializedObject.CopyFromSerializedProperty(Parts)`.

**OVERNIGHT LOOK PASS toward the POLYGON reference (2026-07-30, commits dfa4097 / c1d6f24 / cdf6b6d).**
Bryan shared the POLYGON Meadow/Forest promo shots (bright warm midday, tan trunks + lush AO-shaded
canopies, green/gold/orange tree variety, flowers). NOTE: the Synty pack ships MODELS+TEXTURES ONLY (no
shaders/materials) — the reference look is their demo's URP grade+lighting, so improve OUR shaders/grade.
Done: (1) **Canopy brightness** — our canopies read near-black because the dense canopy self-shadows its
own sides at overhead sun + low ambient floor. FoliageLit now: leaves take a SOFT self-shadow (min ~0.5,
trunk keeps full cast shadow to ground), ambient floor 0.68, ceiling 1.15 (sunlit crown pops), a leaf
BACKLIGHT/translucency term (`pow(dot(viewDir,-sunDir),3)*0.35*lm`) for the luminous look, gentle SSAO
(0.25 not full). (2) **Grade BAKED** into PlanetLookProfile.asset: postExposure 0.15→0.45, saturation
28→42, contrast 12→10, WhiteBalance +9→+14 warm. (3) **COLOUR VARIETY** — added Golden + Autumn tree
variants × Forest+Meadow (4 prototypes, weight 0.45) reusing the Meadow tree mesh with tinted canopy
materials `FoliageMeadowCanopy_Golden` (_SeasonColor 1.6,1.35,0.45) / `_Autumn` (1.55,1.0,0.45) — reads
as a green/gold/orange mix, the single biggest step toward the reference (verified). (4) **Consistency** —
lifted ScatterImpostor floor (0.6..1.12) + Scatter/bush floor (0.55..1.1, soft shadow) so far trees +
understory match. (5) generic flowers (`SM_Gen_Env_Flowers_01`, solid mesh → Scatter/VertexColorLit,
`FlowerGeneric.mat`) added as a 2nd flower type in Meadow+Forest. TECHNIQUE: `_SeasonColor` multiply on a
green leaf gives convincing gold/orange (validated); tint variant materials + weighted prototype variants
= cheap biome colour variety.

**METHODICAL ASSET WORKBENCH (2026-07-30, commits 30816ad / 8443509 / 8e943da).** After planet-scene
guessing frustrated Bryan, built `Assets/Scenes/AssetShowcase.unity` + `AssetShowcaseSpawner` (spawns
EVERY ScatterLibrary prototype — all parts + LODs — in a labelled grid) + `AssetShowcaseController`
(publishes `_SunParams`/`_PlanetCenter` so assets light like the planet, and the wind globals). Fix an
asset here (same materials the planet uses) → fixed everywhere; framed/labelled captures. USE THIS for
asset issues, NOT the planet or the broken BiomeShowcase. Framing tip: TextMesh labels face +z, so put
the camera on the -z side (looking +z) for readable labels. Fixes found+verified here:
- **Variant green trunks (30816ad):** the Golden/Autumn tree variant canopy materials were cloned from
  FoliageMeadowCanopy BEFORE the trunk fix, so they still had `leafPatch_04` in `_TrunkMap` → green
  trunks. Repointed to `Branches_01` bark + normal blend. (Base tree trunks were already fixed in 69d0e1d;
  the VARIANTS were the ones Bryan kept photographing.)
- **Cyan rocks/bushes (8e943da):** `SM_Gen_Env_Rock_01` etc sample a ~2px flat swatch (uv u[0.292-0.293])
  from `Generic_01_A`; the atlas had mipmaps+bilinear → coarse mips blended the swatch into cyan
  neighbours. Set the atlas `mipmapEnabled=false` + `filterMode=Point`. Fixes ALL Generic-atlas props
  (rocks/bushes/generic-flowers). Verified grey on the planet. (Any flat-swatch atlas must be no-mip+point.)
- **Wildflower is FINE:** at natural scale it's a proper yellow/pink/red flower cluster — the planet
  "green weeds" were the fern or over-scaled wildflowers, not a broken flower.

**SYNTY FOLIAGE INTEGRATION — plan + progress (2026-07-30).** Bryan wants their assets to have the Synty
"correct look." DECISION (mine, Bryan agreed): do NOT import Synty's `Foliage.shadergraph` as the assets'
shader — it lights from the URP main light + ambient, but our whole planet lights from `_SunParams`, so
their shader would light inconsistently and lose our cloud shadows / wind / interaction. Instead our
`FoliageLit` IS the integration point (trunk/leaf split, _SunParams, sun+cloud shadows, wind, dither) and
we PORT Synty's visual features into it. Synty source (shaders + materials) is at
`D:\Downloads\!3D Sources\Extras\Synty\` (PNB_Core/Shaders/Foliage.shadergraph; e.g. Tree_Birch_Mat_01.mat).
KEY: Synty POLYGON leaves are FLAT-COLOURED with two-frequency colour noise (`_Use_Color_Noise`, large
0.59 / small 4.44, base/noise/large tints) — the texture is mostly shape/alpha. That's why ours looked
flat (raw texture rgb). DONE (commit 356081d): ported leaf colour-noise into FoliageLit — a cheap 3D value
noise (`FoliageNoise`) drives a warm/cool tint (large freq) + per-leaf brightness (small freq), leaf-only,
params `_ColorNoiseStrength`(0.35)/`_ColorNoiseLargeFreq`(0.59)/`_ColorNoiseSmallFreq`(4.44)/warm/cool
tints. Verified in showcase: canopies vary in shade, not flat. REMAINING Synty features to port: leaf AO
(check if the FBX stores AO in a vertex-colour channel — birch LOD0 vtx R~0.30 const, G~0.2-0.35 varies,
B=leaf mask; G may be AO), leaf/trunk normal maps, multi-layer wind (Light/Strong/Gust/Twist).
Bryan's other queued threads: grass gradient + extend interaction to flowers/ferns (grass already bends to
interactors; that code is separate from the wind change, intact); far-hill = true-horizon terrain-density
tint (impostors too costly at horizon); IMPORT from `D:\Unity\Explore Assets\Assets` (LMHPOLY Nature
Bundle: 120 Flowers, 157 Grass, 68 Mushrooms, 146 Plants, 769 Rocks; Toon Fantasy Nature: aspen/birch/
logs/rocks) for proper flowers + biome variety.

**WIND SYSTEM (commit 8443509).** Foliage swayed off `_Time` unconditionally (animated with no wind,
ignored weather). The wind "interface" already existed: `IWeatherProvider` (WindDirection/WindStrength01/
WindSpeedMps), WeatherManager publishes them as globals. Fix: shared `Includes/PlanetWind.hlsl` declares
those 3 globals ONCE (CloudShadows.hlsl now includes it instead of re-declaring — the include lets foliage
use them without the cross-pass double-declaration). FoliageLit's ApplyWind reads them and returns ZERO
displacement when calm (no animation without a provider), else follows the weather wind, high spatial
frequency so no unison. PlanetWind holds ONLY globals (no _Time/funcs) so it's safe to include anywhere;
the sway math lives inline in FoliageLit (Core/_Time present). Providers: WeatherManager (planet),
AssetShowcaseController.WindStrength slider (showcase, default 0). Grass already used the weather wind.
Planet verify (post all fixes): rocks grey, grass blades DO draw (sparse in Beach by design; fully
occluded under dense forest canopy — that's why they looked "missing"), green/gold/orange tree variety good.

State after look-dev: STRONG and much closer to the reference — bright warm graded midday, lush canopies,
green/gold/orange tree variety, flowers, casts+receives sun+cloud shadows ([[project-scatter-gather-perf]]),
tree line ~1200m. Remaining polish: dark BRANCH structure still shows through the canopy (foliage
coverage/mesh — try lowering the Meadow foliage `_Cutoff` for fuller cards); grass is tall/dense vs the
reference's shorter flower-dotted meadow; more props (no mushrooms in-pack — Bryan's Extras has
LMHPOLY/Toon Fantasy Nature to import); day/night-driven ambient. See [[project-scatter-biome-buildout]].
