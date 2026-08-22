---
name: project_water_shore_rendering
description: The 2026-08-22 shore/horizon rendering arc — what was fixed and how it was measured, plus the stepped-lakebed defect that is still open with its real mechanism.
metadata:
  type: project
---

**2026-08-22, branch `harvest-vertical-slice`.** Eight water fixes, all verified by capture. One defect still
open. Commits `ed20191` → `726c6eb`.

## The rule that explains most of this arc

**A consumer asking where water is *globally* when it should ask where water is *here*.** Same rule as the
W1–W6 arc. Every "check `_SeaLevelRadius` first" instinct paid off again.

## Fixed, with the mechanism worth remembering

- **Hard waterline + water lying on grass.** `WaterMeshBuilder` gave its inland overlap vertices a fake
  **8.25 m depth**. Coverage saturates by 6.5 m, so the overlap was fully opaque. Zero depth fixed both.
- **Coverage had a distance short-circuit**, `max(depth01, shore01*0.45)`, crossing the ramp ~5 m out where
  water is ~1 m deep. Depth alone now drives it, so the feather is slope-adaptive: 5 m → ~35 m on a 1:5.3 bank.
- **Ocean needs its own fade depth.** Removing its `+0.55` term outright made the seabed visible across whole
  bays — an ocean shelf stays a few metres deep a long way out. Lake 6.5 m, ocean 1.5 m.
- **Foam followed the WIND, not the shore.** `shorePulse` was a sine along wind with a 3× swing feeding a
  halftone *threshold*; every trough fell below it and vanished, so a gentle modulation became hard stripes.
  Replaced with `saturate(noiseA + noiseB + gradient) * gradient` — **noise added inside the saturate, then
  multiplied by the gradient**. That ordering is the whole trick: multiplying means foam cannot exist where
  there is no shore. Technique from Stylized Water 2.
- **`_ShoreFoamDepth` was DEAD** — declared in the CBUFFER, never read. Repurposed as foam depth in metres
  (32 → 2.5; 32 m of *depth* would blanket every shelf).
- **Wave detail aliased into moire.** Analytic detail has no mip chain. Fade by resolvability — but measure the
  **MINOR** footprint axis, not `fwidth`. `fwidth` is the major axis and at grazing angles flattens the entire
  sea to a mirror. Fade to a floor (0.32), never zero.
- **Limb edge.** The atmosphere preserves 38–55 % of water's own colour so haze does not wash out detail, at
  *every* distance, while terrain had a companion fade water was excluded from (`nonWaterSceneMask`). Far water
  never met the sky in tone; a 1-px unantialiased silhouette between two tones reads as a staircase.
- **Underwater bleached everything.** `FarTerrainWaterlineMask` derived its path from the analytic ocean sphere,
  which cannot answer inside a raised lake — the camera is *under* a surface 30 m *above* global sea level, so
  it sits outside that sphere and the ray misses it. Submerged now short-circuits to distance-travelled.

## GOTCHA — the depth buffer answers the wrong question

Per-pixel water depth from the depth buffer is right looking DOWN at a shore and wrong looking ALONG one: the
first opaque hit behind a water pixel is land on the far side, so the column reads zero and water vanishes. It
flips at the same view angle across the frame, drawing **a hard horizontal line partway up the sea**. Weight
the measurement by bed distance (fade 40→160 m) and hand back to the mesh's baked depth beyond that. A gate on
the *surface's* alpha has the same flaw and is unfixable — removed, with a comment so it is not retried.

## STILL OPEN: the stepped lake bed, and why five attempts failed

Cell-shaped staircase at the Lake↔LakeShore edge. **Do not tune the biome blend weight for this.**

`BiomeMapBaker` is two passes. Pass 1 builds a high-res grid of **primary ids only** —
`ResolveFromLandBiomes(..., out primary, out _, out _)` **discards secondary and blend**. Pass 2 histograms a
`KernelRadius` window over that grid to make the top-K ids/weights the shader samples. So
`LakeShoreHandoff`'s blend **never reaches the screen**. Five attempts tuning it (`LakeShoreBlendHeight`,
`LevelRings`, `ShoreRings`, and a whole separate reference-level field threaded through both parity-locked
resolvers) could not have changed a pixel. All reverted.

**Measured, and it kills the LOD theory too:** every leaf is depth 4 (1536 of them, uniform). chunk 491 m,
high-res texel 3.8 m, **kernel(12) = 46 m vs a 40.9 m mask cell — ratio 1.1**. Smoothing barely exceeds the
step, everywhere, at every distance. Distance only changes how many steps fit in frame.

Candidates: widen `KernelRadius` (cost is `(2r+1)²`; r=24 is 3.8× on a phase that is already 19 s of a 38 s
generation) or **jitter the pass-1 sample position** so pass 2 histograms a dithered edge — near-free and
matches the halftone/dither idiom already used for foam and scatter. Second looks right; unverified.

## Process lesson Bryan called out, and he was right

Every fix that landed came from a measurement that **contradicted** my first instinct. Every failure came from
finding a plausible mechanism and changing it before proving it was the active one. Read the consumer chain
end-to-end *first* — one grep for `out _, out _` would have saved five regenerate cycles at ~8 min each.

Cheap tools that worked: numeric transects via `IPlanetSurfaceRaycaster` + `WaterBodyMap.LevelAt`; calling
`BiomeDto.Registry.Resolve` directly in the live world (read-only, no regenerate); `WaterOff` / `VolumeMask` /
`SurfaceAlpha` / `AtmosphereBypass` to bisect which pass owns an artifact; ffmpeg 4× nearest-neighbour crops.

## Two git-hygiene bugs found: main did not compile from a clean clone

`Planet.cs` was committed constructing a `ChopFxSystem` whose class was never added. `WaterQueryService.cs` was
committed without its `.meta` (fresh GUID on any other machine, silently broken references). Neither reproduces
in a working tree. **Worth periodically checking `git ls-files` against referenced types and for `.cs` without
`.meta`.** Fixed in `726c6eb`.

Related: [[project_water_architecture_build]], [[reference_unity_mcp]], [[feedback_quality_over_cheap]].

## The stepped lake bed: full chain, measured (supersedes the guesses above)

Six attempts failed because two independent faults must be fixed TOGETHER. Each was tested alone; each alone
does nothing, which is why every attempt looked like "no change".

**Ruled OUT with hard evidence, do not re-investigate:**
- The bake. Read the live atlas: ids `50 50 | 73 x7 | 71 x85` with weights ramping smoothly over ~14 texels
  (~110 m). The kernel histogram works. Atlas is 1009 per face = 16 leaves x 63 stride, **7.78 m per texel**.
- The shader's 4-corner reconstruction, BOTH the albedo path and the grass overlay path. Textbook bilinear -
  `uv * res - 0.5`, `floor`, fractional lerp. No half-texel or truncation error.
- LOD. Every leaf is depth 4 (1536 uniform). Distance only changes how many steps fit in frame.

**The actual chain:**
- INNER Lake/LakeShore edge is smooth - decided by `elevation < waterLevel`, continuous.
- OUTER LakeShore/land edge is the staircase - decided by **`lakeState != 0`, a binary test on the 41 m mask**.
  Its cell-shaped outline propagates to everything downstream.
- Kernel smoothing is 46 m against 41 m steps, ratio **1.1** - it blurs ACROSS the edge but cannot remove the
  staircase SHAPE along it. A blurred staircase is still a staircase.
- `LakeShoreHandoff` exists to replace that binary test with a terrain ramp, and is defeated twice over:
  1. pass 1 keeps only the primary id, so the blend is **discarded** -> needs `Dominant(p, s, blend)`
  2. the ramp's reference level is **NoWater one ring out**, so it saturates instantly -> needs a wide
     reference-level field, dilated WITHOUT the anti-flood guard (that guard is only for the wet test)

**Untried and most likely: both together, with `Dominant` scoped to the LAKE branch only.** Applying
`Dominant` to every biome boundary was measured and is BAD - large regions flip grass -> scrub, because the
secondary is dominant across much of the planet. That also means "primary is the base biome" is not a safe
assumption about the resolver's convention.

Cheap read-only tools that made this tractable: blit a non-readable atlas to a point-filtered RT and
`ReadPixels` it; `CoordinateConverter.UnitSphereToCubeFaceUvExact(dir, out face, out uv)` then `uv * 1008`
gives the atlas texel; call `BiomeDto.Registry.Resolve` directly in the live world.
