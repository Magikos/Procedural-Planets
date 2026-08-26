---
name: project-surface-props-lighting
description: "2026-08-09 (branch character-controller-mvp): three look fixes from Bryan's walk-test. (1) Planet/PropLit shader = REUSABLE planet-aware prop lighting (night side darkens) — use it for ALL surface objects/NPCs, NOT default URP Lit. (2) ScatterPrototype.ConformToSlope [0..1] tilts ground props to the surface normal (rocks=1, trees=0). (3) Surface-unification DECISION: render mesh = ground truth for camera-local things; analytic sampler stays camera-independent for deterministic scatter."
metadata:
  type: project
---

Three fixes from Bryan's on-surface walk-test, branch `character-controller-mvp` (commits db2a66b, 41712d4,
36ad833; NOT pushed). Bryan's framing: "don't just fix the capsule — fix the planet not blocking light so
future things don't suffer the bleeding-light issue."

**1. Capsule/prop lighting → `Planet/PropLit` shader (reusable).**
`Assets/Graphics/Shaders/PropLit.shader`. Stripped-down mirror of `Scatter.shader`'s day/night frag using
`Includes/PlanetSunLighting.hlsl`: `lerp(nightColor, dayColor, daylight)`, `daylight =
PlanetDaylightFromLocalSun(dot(planetNormal, sunDir))` = 0 on the night side. Reads the scene-wide globals
`_SunParams`/`_PlanetCenter`/`_NightAmbientIntensity` (published every frame by `AtmosphereController.Publish`)
— so ANY object wearing it darkens on the night side with zero per-object C# wiring. Default URP Lit can't (a
directional light has no positional occluder → "bleeds" onto the night hemisphere). `PlanetCharacterController`
assigns a **runtime** `new Material(Shader.Find("Planet/PropLit"))` to the capsule (destroyed in OnDestroy).
**Rule going forward: new surface objects/NPCs/dropped items use PropLit (or a FoliageLit-style planet shader),
never default URP Lit.** Verified: capsule luminance day 0.69 → night 0.29. See [[project-grass-terrain-lighting-arc]].

**2. Rock/bush orientation → `ScatterPrototype.ConformToSlope [0..1]`.**
`up = normalize(lerp(dir, surfaceNormal, conform))`. 0 = radial (up==dir EXACTLY so golden placements don't
drift); 1 = lies on the surface normal. Threaded prototype → DTO → `PlacementRules`/`PlacementRulesBurst` →
both `TryPlace` bodies (managed `ScatterPlacementMath` + Burst `ScatterGatherBurst`, parity 0.0000°),
forwarding the already-sampled `localNormal` (was thrown away as the scalar `slopeCos`). **Bryan's rule: rocks
etc. FIT the terrain; trees/flowers GROW UP (radial).** Values set: **rocks=1** (14), **bushes+flowerbushes=0.6**
(7, beds without lying flat — fixes the floating yellow bush), trees/pines/palms/dead-trees/grass/reeds/ferns/
flowers=0. **Mushrooms left at 0** (grow up on a stalk), flagged for review. 78/78 green. GOTCHA when editing
`TryPlace`: the DTO record's positional ctor changed → fix `ScatterGatherParityTests`/
`ScatterLibraryDtoValidationTests` + the `Place` helper in `ScatterPlacementMathTests`. See
[[project-scatter-gather-perf]], [[project-scatter-biome-buildout]].

**3. "Floating scatter" root cause = ORIENTATION, not height** (`docs/design/2026-08-09-surface-unification.md`
rev 2). CORRECTED after a Codex review + code trace (rev 1 had the taxonomy WRONG). Facts:
- **Scatter uses `AnalyticGroundSampler`→`ShapeGenerator` raw noise, NOT `TryGetSurfaceRadius`/
  `TryGetLocalSurfaceRadius`** (grep-proven zero calls). The rendered terrain mesh samples the SAME noise
  (`NoiseFilterEvaluator`) at its vertices → scatter-height and render-height COINCIDE at vertices, differ only
  by the triangle chord. Measured sag: **~0 at max LOD (near camera)**, metres only at distance (sub-pixel).
- So the near-visible float is **orientation** (radial prop lifts downhill edge on a slope) — fixed by #2.
  Mesh pivots sit at base (ruled out); height sag near-zero (ruled out).
- Decision = **policy 2: per-consumer authority + error budget**, NO unification service. `TryGetLocalSurfaceRadius`
  = fixed-depth leaf bilinear (camera-INDEPENDENT, contra rev1). Only the visible RAYCAST (`TryRaycastSurface`)
  is camera-scoped.
- **Character grounding "fixed" = runtime OBSERVATION, not proven**: `PlanetRaycastGrounding` falls back to the
  analytic sampler on raycast miss (`:52`); over ocean the sea-level clamp makes both identical → ocean test
  can't distinguish. A LAND test with path instrumentation (`PlanetRaycastGrounding.cs:47/52`) is owed.
- Future height-unify (only if a pixel threshold is exceeded): a Burst-readable fixed-depth radius/triangle
  atlas (the grass atlas `GrassSurfaceAtlasBuilder` is the precedent) — NOT managed quadtree traversal.
  See [[reference-collision-strategy]] (aligned: one FIXED-LOD reference, camera-independent).

**5. Biome border seamlessness (2026-08-09, long arc, doc `docs/design/2026-08-09-biome-blend-seamlessness.md`).**
The "drastic biome border" Bryan saw WALKING was a REGRESSION: `17707e3` dropped `150a482`'s grass-overlay
co-termination gate (it starved savanna). FIX (`9ff9294`): re-gate the far overlay on grass DENSITY (raise
`coverageToe` in `EvaluateGrassOverlay`), savanna-safe. Then Codex flagged a SEPARATE base biome-albedo contrast
→ pursued D2/G:
- **D2 SHIPPED** (`89324d6`): per-biome production albedo multiplier `BiomeDefinition.SurfaceAlbedoTint`
  (default white) → `_BiomeAlbedoTint[64]` global → multiplied per slot in `CornerTriplanarWeightedPbr`. Live:
  `biome.tint <BiomeType> r g b` / `biome.tint-list`. Also `biome.force-same-material 0/1` diagnostic (band
  vanishes = material contrast). Codex D-ranking D2 > D3 > D1.
- **CORRECTED ROOT CAUSE (2026-08-09 rev-2, evidence-nailed).** The earlier "corners are 1-HOT" note was WRONG
  — a flawed G probe (tested interior pixels + likely a no-op impl). The bake (`BiomeMapBaker.SampleTopKPerTexel`,
  25×25 window, `KernelRadius=12`) stores GENUINE multi-biome byte weights summing to 255 across a ~12-output-texel
  ramp; interiors are 1-hot (that's what the probe saw), BORDERS are real multi-biome (visible as the thin
  transition strips in `BiomeMapBlend` mode 79, white=saturated interior). Production `CornerTriplanarWeightedPbr`
  correctly consumes them (4-corner bilinear × baked weights) — **the blend is soft and correct, NOT a hard step,
  NOT a filtering/minification artifact, NOT a regression.** The "drastic border" is the **perceptual MIDTONE of a
  linear weighted-average between biome material albedos that are far apart in brightness/hue** (synth H1). PROVEN:
  `biome.tint` reddening ONLY the Mountain slot turned the blue-white border ring red → the ring is the Mountain
  material's own bright-veined albedo blended into dark-green Forest (light blue-grey midtone). `force-same-material`
  vanishing = same cause. Worst at Mountain↔Forest (bright veins vs dark green) but general to any high-contrast pair
  (Beach/Desert tan 0.37 ↔ Forest 0.10). **Levers that DON'T fix it: wider KernelRadius (12→20 refuted, no-op) and
  D2 tint equalization (only dampens the midtone).** STRUCTURAL fix = interlock/height blend (noise or pseudo-height
  driven) so each pixel is one material with an organic boundary, no muddy 50/50 — reuse the shader's existing
  `ValueNoise3D`; no per-biome height maps exist (arrays are albedo/normal/ARM only). The grass-overlay green line
  (150a482→17707e3 regression→9ff9294 density-gate refix) is a SEPARATE thing, already fixed in the working shader
  (coverageToe 0.38/0.72 at ~line 751). GOTCHA: shader edits need explicit `AssetDatabase.ImportAsset(ForceUpdate)`;
  `_BiomeMacroVariationScale=0.012`. Live probe: manual `cam.Render()` to RT + `debug.mode` via
  `ConsoleController.RunCommand`, ray-sphere (R≈1845) to place a top-down border cam; `ScatterRenderer.DrawEnabled`
  (static) hides scatter.
- **FIX LANDED (2026-08-09, pending Bryan F10, compiles clean).** Noise-**interlock** blend in
  `CornerTriplanarWeightedPbr` (`PlanetVertexColor.shader`): where >1 biome meets, per-biome `ValueNoise3D` is a
  pseudo-height; keep only the locally-tallest within a narrow `depth`, renormalize the top-K weights → each pixel
  resolves to (mostly) ONE material with an organic wiggly seam instead of the muddy 50/50 average. Interiors
  (single biome) untouched, ZERO extra texture taps (reweight only). Live globals `_BiomeInterlock{Enabled,Depth,
  NoiseAmp,NoiseScale}` (in `ShaderGlobalIds.Biome.cs`), published on world build by `BiomeBlendRuntime.PublishDefaults`
  (called from `BiomeSurfaceTextureArrays.Build`), default **on** at depth 0.08 / amp 0.70 / scale 0.13. Console:
  `biome.interlock <0|1> [depth] [amp] [scale]` (`BiomeDebugCommands`). VERIFIED via before/after top-down captures:
  clearly cleaner on FLAT borders (tan↔green olive smear → crisp organic seam). CAVEAT: the Mountain↔Forest
  blue-white ring only partly improves — that band is largely the **Mountain material's own steep-slope blue-white
  veins** (proven: `biome.tint` reddening ONLY slot 14 turned the ring red), an art/material issue for Bryan, NOT the
  midtone. Smaller `depth` = crisper (risk: distance aliasing on the Point/no-mip weight atlas). Next: Bryan F10 the
  default, then tune or tame the Mountain albedo.

**4. Look backlog (2026-08-09, commits 51ba3e2 + 0e9be87, pending Bryan F10 sign-off).**
- **Mushroom flat/solid-colour = wrong material.** Mushrooms (SOLID meshes) wore the shared
  `LMHPOLY_Vegetation` FoliageLit material configured for flat leaf/flower CARDS: `_ForceLeaf=1` forces
  `leafMask=1` on every vertex, then FoliageLit blends the shading normal toward up by `_LeafNormalUp*leafMask`
  (`FoliageLit.shader:301`, and toward WORLD +Y not radial — latent sphere bug). On a solid mushroom that
  flattens all normals → uniform shading. FIX: new `LMHPOLY_Mushroom.mat` (`_ForceLeaf=0`, `_LeafNormalUp=0`,
  real mesh normals) on Forest/Swamp/Taiga Mushroom; flowers/reeds keep the card material. Rule: **solid-mesh
  props need real mesh normals (FoliageLit `_ForceLeaf=0` or PropLit); only flat cards want `_ForceLeaf`.**
- **Far biome-edge line = grass-overlay saturation (green over tan).** Bryan's own tension: `150a482` greenness
  gate killed the line but made tan savanna barren; `17707e3` dropped it + trimmed overlay saturation 0.82→0.72,
  inviting iteration. Promoted the literal to a live knob **`grass.surface-saturation`** (material prop
  `_GrassSurfaceSaturation`, default 0.72, mirrors `grass.surface-brightness`) so Bryan tunes it live + bakes.
  GOTCHA: `PlanetGrassCoordinator` is a plain SERVICE not a MB (FindObjectsByType won't find it); overlay props
  are per-MATERIAL (not globals, so NOT in ShaderGlobalIds). See [[project-grass-terrain-lighting-arc]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Surface props + lighting](project_surface_props_lighting.md) — 2026-08-09 (branch character-controller-mvp): walk-test fixes. **Planet/PropLit shader** = REUSABLE planet-aware prop lighting (night side darkens via _SunParams globals) → use for ALL surface objects/NPCs, NOT default URP Lit. **ScatterPrototype.ConformToSlope [0..1]** tilts props to surface normal per Bryan's rule (rocks=1, bushes/flowerbushes=0.6 FIT terrain; trees/flowers/grass=0 GROW UP; CPU/Burst parity, 78 green). **Floating scatter = ORIENTATION not height** (rev2, Codex-corrected): scatter uses raw ShapeGenerator noise NOT TryGetSurfaceRadius; sag ~0 near camera. Char grounding "fixed" = OBSERVATION (raycast falls back to analytic; land test owed). Commits db2a66b/41712d4/36ad833/5af2008
