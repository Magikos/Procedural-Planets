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

## RETRACTION — the "bake is exonerated" finding above is WRONG

The atlas read that produced it is invalid. Valid biome ids run **0..17** (gridCount 12: Ocean 0, Beach 1,
grid 2..13, Mountain 14, Snowy 15, Lake 16, LakeShore 17). The bytes read out were **50, 71, 73** — all out of
range, all resolving to NULL through GetDefinitionByIndex. Graphics.Blit resampled or format-converted rather
than handing back raw bytes, so both the ids AND the "weights ramp smoothly over ~110 m" reading are
meaningless.

**So the bake is NOT ruled out**, nor is anything that reading was used to argue. To read these atlases
properly use AsyncGPUReadback, or CopyTexture into a matching-format readable texture — never Blit, which goes
through a filtered and converted path.

Still genuinely ruled out, on other evidence:
- LOD. All 1536 leaves are depth 4, measured directly off the chunk tree.
- The shader's 4-corner reconstruction, both the albedo and grass-overlay paths — textbook bilinear, read line
  by line, no half-texel or truncation error.

## Attempt 7 (also failed), and what it DID establish

Both halves at once: wide reference level (dilated without the anti-flood guard) plus the shore ramp choosing
the dominant biome, scoped to the lake branch only. Compiled, generated, and confirmed ShoreReferenceLevelAt
returns the lake level at shore cells where LevelAt gives NoWater. **Staircase completely unchanged.**

Useful negative: terrain was NOT degraded this time, so scoping the dominant-biome rule to the lake branch does
avoid the planet-wide grass-to-scrub regression the unscoped version caused. That part of the design is sound.

Seven attempts have now changed the lake branch of the resolver, the level fields, the mask dilation and the
bake's id choice, with zero effect on this artifact. The weight of evidence says **the boundary being drawn is
probably not produced by the lake branch at all**. Next step is to identify which biome ids actually sit either
side of it — reading the atlas CORRECTLY per above — before touching any more code. It may not be a lake edge.

## CORRECTED DIAGNOSIS — supersedes every "still open" section above

Read the atlas properly (AsyncGPUReadback, not Blit) and the framing every earlier section used is wrong.

**The tan band beside a lake is Desert (id 11), an ordinary Voronoi land biome. It is NOT LakeShore.**
Measured across the boundary, 7.78 m per texel:

```
366 m   Lake 0.48 / Desert 0.27 / LakeShore 0.25
381 m   Desert 0.43 / Lake 0.31 / LakeShore 0.25
443 m   Desert 1.00
```

LakeShore (id 17) never exceeds **0.27** anywhere - a trace through the transition, then gone. Valid ids for
this world: Ocean 0, Beach 1, grid 2..13, Mountain 14, Snowy 15, Lake 16, LakeShore 17.

So the stepped edge is a **Desert-to-Lake** boundary. All seven attempts modified the LakeShore path, a biome
that is barely present here, which is exactly why nothing ever moved.

The transition is NOT hard: it ramps over ~9 texels (**70 m**) with three biomes overlapping. The bake produces
a proper gradient. The staircase is a SHAPE in plan view, not a hardness across the edge - consistent with the
kernel arithmetic (46 m smoothing against 41 m steps, ratio 1.1). The shape comes from where **Lake** is
assigned, and that is `lakeState != 0 && elevation < waterLevel`, gated on the 41 m mask.

**Untried candidates that target the real mechanism:**
- raise `WaterBodyMap.Res` 192 -> 384 (41 m cells -> 20 m), ~4x the flood-fill cost on a phase costing ~1 s
- jitter the `lakeState` test at bake time so the Lake outline dithers at 7.8 m instead of stepping at 41 m

**Process failure worth remembering.** I assumed "tan sand beside water = LakeShore" in the first minute and
never checked it. Seven attempts, two regressions and one false "ruled out" all descend from that single
unverified assumption. The check that falsified it was three read-only lines with no regeneration. **Identify
what you are looking at before theorising about why it looks wrong.**

Second: a `Graphics.Blit` into a RenderTexture is NOT a measurement of texture contents - it resamples and
converts. It returned ids 50/71/73 where the real values were 16/11/17. Use `AsyncGPUReadback.Request(tex, 0,
GraphicsFormat.R8G8B8A8_UNorm)` then `WaitForCompletion()`.

## SOLVED — it was GRASS, not biomes. Supersedes every section above.

Fixed in `52143a1`. The blocks around every lake were the grass carpet vanishing in cell-shaped patches,
letting bare ground show through. **No part of the biome system was ever involved.**

`EvaluateGrassOverlay` in PlanetVertexColor.shader fades grass out approaching water:

```
waterRadius = WaterSurfaceRadiusAt(dir, _GrassWaterRadius);
altitude    = length(relPos) - waterRadius;
waterKeep   = smoothstep(clearance, clearance + 4.0, altitude);
```

The level field it measured against stops ONE cell from the water, and past that the lookup falls back to the
GLOBAL sea radius. Beside a lake perched 30 m up that fallback is a **30 m step in altitude against a 4 m fade
band**, so waterKeep flipped 0 to 1 across a single 41 m cell edge. waterKeep multiplies every overlay weight,
which is why GrassLodCoverage showed pure black exactly where the beauty pass showed dark blocks.

Fix: two level fields. The tight one is unchanged and still answers every wetness test (the mesh reads it via
C# and MUST keep it, or water is meshed over dry ground). A second is carried ShoreRings onto dry land WITHOUT
the anti-flood guard - nothing compares it against elevation, and the cells that guard excludes are exactly the
ones the fade needs. Grass reads the second. No existing consumer changed.

**Residual to eye:** the suppressed band is now soft and contour-following but WIDER, because the fade actually
completes instead of snapping. Whether that width is right is a tuning call on `waterClearance` and the 4 m
band, not a bug.

## How it was found, after seven failed attempts

Bryan called it: stop theorising, colour-code it. Four elimination tests, all READ-ONLY, no regenerations:

| test | result |
| --- | --- |
| false-colour the biome id atlas in plan view | smooth organic regions - **clean** |
| `_CloudShadowParams` strength -> 0 | blocks remain |
| `QualitySettings.shadows = Disable` | blocks remain |
| `GrassLodCoverage` debug mode | **blocks are here** |

Seven prior attempts changed the biome resolver, both level fields, the mask dilation and the bake's id choice
- a system that was never involved - all from assuming in the first minute that tan sand beside water meant
LakeShore. It was Desert, and the artifact was grass.

**Two false negatives that nearly killed the correct hypothesis:**
- `Shader.GetGlobalFloat("_GrassWaterRadius")` returns 0 - it is a MATERIAL property, real value 5000. Reading
  a material property through the global channel returns a plausible-looking zero. Check `HasProperty` on the
  material. Terrain material is `Planet (runtime)`, shader `Planet/VertexColor`.
- `round(idPacked * 255.0)` for biome ids is EXACT for every id 0..17. Not a rounding bug; do not re-suspect it.

**Method that works here:** pick the cheapest test that splits the space in half, run it read-only, and only
then form a hypothesis. Existing debug modes (`WaterOff`, `VolumeMask`, `SurfaceAlpha`, `AtmosphereBypass`,
`GrassLodCoverage`, `BiomeMapPrimaryId`) plus runtime toggles of globals and material floats cover most of the
render pipeline without a single regeneration.

## The blocks took TWO fixes, not one. `52143a1` was only half.

`52143a1` was reported here as SOLVED. It was not. Bryan's next F10 still showed them, and the beauty pass
confirmed it: dark patches with hard 90-degree axis-aligned steps, grass still growing *inside* them, so a
darkening rather than a hole.

Second fix `538b7cc`. Same function, different mechanism:

| | |
| --- | --- |
| `52143a1` | past the tight field's edge the lookup fell back to the GLOBAL sea radius - a 30 m step against a 4 m fade band. Fixed by adding a second, wider shore field. |
| `538b7cc` | the shore field is **point-sampled with its UV snapped to a cell centre**, so any fade measured against it is a 41 m staircase *by construction*. Fixed by bilinear-blending four taps. |

Hardware bilinear cannot be used on either field: dry cells carry a sentinel far below any real level, and
letting it into the blend drags the surface to nothing. Weight only the taps above `WATER_LEVEL_NO_WATER_MAX`.
The tight field keeps its exact snap - it decides wetness and must agree with C# cell for cell.

**The stage-split that found it, three captures, no regeneration** - `_OceanDebugMode` renders terrain albedo
at three points in the same fragment shader:

| mode | stage | blocks? |
| --- | --- | --- |
| 95 `TerrainPrimaryAlbedo` | raw biome triplanar | clean |
| 94 `TerrainOverrideComposite` | coast/slope/snow masks | clean |
| 81 `TerrainSelectedAlbedo` | after overrides AND after `ApplyGrassSurfaceAlbedo` | **blocks** |

Two modes bracketing one function is worth far more than any amount of reasoning about the function. Reach for
that bracket first whenever an artifact survives a fix.

**Trap that cost a capture:** `DebugModeConstants` already used 87 (`BiomeAltitudeCooling`). A hand-added
temporary mode collided with it and produced a plausible-looking image that meant nothing. `Max=107` - read
the constants before picking a number, or better, bracket with the modes that already exist.

## The 41 m water mesh: sheets on grass, and the white rim (2026-08-23, `c1966ef`)

Both of Bryan's remaining water complaints had one shared cause. The water mesh is **192 per face = 41 m
per quad**, an order of magnitude coarser than the terrain it has to follow.

**Isolated pale sheets on open ground.** `WaterBodyMap` dilates `_level` one ring onto dry land so the mesh
can find the waterline crossing INSIDE the first dry cell. `CreateIntersection` always takes that level from
the WET end - so a carried height was never needed to decide wetness, only to describe it. The mesh used it
as its wet test anyway. `BuildLevelField`'s anti-flood guard rejects a carried cell whose **coarse** 41 m
elevation sits below the borrowed level, but the mesh samples elevation far finer, so a cell that passes the
guard still holds dips beneath it. Each dip meshed as water, had no wet neighbour to join, and became a lone
41 m sheet. Fixed with `TrySolvedLevelAt`, which answers only for cells the solve actually put water in.

**The hard white rim on every coast, ocean and lake.** `ShoreGradient` computed
`1 - saturate(max(column,0)/_ShoreFoamDepth)`. Where the mesh overhangs land the column goes negative, the
clamp reads it as zero metres of water - the *strongest* point of the band - and foam saturated to white
exactly where the sheet lay on grass. Now faded back out as the bed rises above the water plane.
`MeasuredWaterColumn` also switched to the bilinear shore field; the point-sampled one was quantising the
band's width at 41 m.

**Two wrong turns worth not repeating.**

- I first blamed `LevelAt`'s ocean-level fallback re-flooding basins that `DrainBasinsBelowMinimumArea`
  (`MinBasinCells = 16`) had drained. Source-plausible, and **measured false**: all six puddle sites came
  back `mask=Shore`, `TryLevelAt=true`. Regenerating and re-testing the six known coordinates was what
  caught it. A source-level proof is not a measurement.
- The mechanism was already written in the comment at `BuildLevelField` - "the guard stops a dilated cell
  flooding on the COARSE sample, but the mesh samples elevation far finer than 41 m". I had read that file
  twice without reading that paragraph. **Read the comments at the site before theorising about it.**

**Verification that worked:** keep the coordinates of the artifacts, regenerate, and re-query them. Nearest
water at the six sites went 1-3 m to 18-49 m, and the mesh lost 0.75% of its vertices - targeted, not a
collapse. Comparing component counts between runs did NOT work: my two passes counted different things
(triangle-corner occurrences vs unique vertices), so 13 vs 15 meant nothing.

**Use `camera.teleport <name>`** (`camera.teleports` lists 26 saved views, incl. `ShoreStudy`,
`OceanShoreStudy`, `Lake1`). It is the sanctioned way to travel - see
[[feedback_camera_teleport_wedges_editor]]. Set time with `time.set-local 0.42`, and render in a LATER
call: lighting needs a frame, so a capture in the same call still comes back at night.

## Jagged shoreline: per-pixel trim, and why it needs a cover set (2026-08-23, `9e5d2e0` + `2634101`)

The water mesh is **~21.6 m per quad** (median triangle edge; WaterBodyMap is 40.9 m, they are NOT the same
grid). Its waterline is a marching-squares contour on that grid - a chain of straight segments. That is the
jagged beach and much of why lakes read hard rather than soft. Raising mesh resolution only shortens the
segments; it cannot remove them.

**The fix is two halves, and half alone does nothing.**

1. `Ocean.shader` trims surface alpha per pixel against the measured water column, so the visible edge sits on
   the real ground crossing at pixel resolution.
2. `WaterMeshBuilder` builds from a **cover set** - the wet set grown `CoverRings` (2, ~43 m) outward - so the
   trim always has geometry to carve. Shipping (1) with only a 0.30-of-an-edge overlap left the zigzag intact
   on shallow bays: Bryan's F10 showed sand wedges cutting INTO the water, i.e. MISSING water, and no trim can
   add geometry that is not there. On a 1:200 shelf that overlap is centimetres of height, while the mesh's
   wet test uses the level field point-sampled at 40.9 m and the shader trims against the bilinear shore
   field - they disagree by more than it covered.

**The old objection is dead, but for a specific reason.** A depth-buffer trim was tried years earlier and
abandoned because looking ALONG the water the first opaque hit is the FAR shore, so the column read negative
and the surface was erased in a hard horizontal band. It works now only because `MeasuredWaterColumn` drops
`sceneValid` to zero past 160 m of bed distance, so a far-shore hit cannot trim anything. **Never trim on the
depth buffer without that gate.** Verified from 3.5 m above a lake looking across: water continuous to the far
shore, no band.

**Trap the cover set introduces:** a covered-but-dry vertex has no water history, so its `bodyFactor` is 0 -
which means LAKE - and the first build painted murky green patches with straight cover-ring edges out into the
ocean. Anything the ring carries must carry level, bodyFactor AND temperature from the wet neighbour it came
from. Level alone is not enough.

**Costs measured:** mesh 515,886 -> 548,472 verts (+6.3%). Overhang at the earlier 0.30 overlap was 1.74% ->
3.70% of verts, mean lift 0.42 -> 1.07 m.

**Workflow note:** each verify cycle is ~15 minutes of generation, so batch the checks you want per run.
Reproduce Bryan's exact viewpoint with `camera.teleport LastDebugCapture`, render at the sidecar's source
resolution, and crop with ffmpeg for a like-for-like before/after against his F10 PNG.

## Underwater: the atmosphere owns the bug, not the water (2026-08-23, `6d2e3d0` + `847e867`)

Both underwater defects lived in `Atmosphere.shader`, not in any water shader. **Render order is volume ->
atmosphere** (`DEBUG_VOLUME_AFTER_ATMOSPHERE = 41` exists precisely because that is NOT the default), so the
atmosphere gets the last word on every underwater pixel and was using it wrongly twice.

**1. Flat teal wash, no surface underside.** The water surface writes no depth (`ZWrite Off`), so every pixel
showing it classifies as SKY. The underwater branch did `return float4(UnderwaterSkyColor(viewDir), ...)`,
discarding `originalCol` - and `originalCol` was the surface the Ocean pass had just drawn, ripples, glint and
all. Now `lerp(UnderwaterSkyColor, originalCol.rgb, WaterInterfaceFrontMask(uv))`.

**2. Far shore bleached cream by day, black at night** (Bryan's exact words). The volume already attenuates
those pixels - `FarTerrainWaterlineMask` early-outs to mask 1.0, `seaPath = receiverDistance`, when
`CameraSeaOffset() < 0` - and then `CalculateScattering` laid AIR scattering over the top, so they took the
sky's colour rather than the water's. Fixed by fading the whole atmosphere contribution out with
`CameraUnderwater01()`. No-op above water, verified against the OceanShoreStudy view.

**The single most useful probe here is `_OceanDebugMode = 40` (`AtmosphereBypass`).** If a frame looks right
with the atmosphere off and wrong with it on, the atmosphere is the owner. That one capture found both bugs.
`ShouldBypassAtmosphereForWaterDebug()` lists every mode that already skips it.

**Trap:** the underwater sky branch sits BEFORE the `DEBUG_ATMOSPHERE_WATER_CUT` (42) branch, so mode 42 is
dead while submerged and tells you nothing. I wasted a capture on it. Check branch ORDER before trusting a
debug mode to isolate something.

**Still not built** (do not report underwater as done): Snell's window - straight up the surface is correctly
near-transparent at normal incidence, so there is nothing to preserve and the view stays flat; a real window
needs the sky refracted and attenuated inside the ~48.6 deg critical cone with total internal reflection
outside it. Also underwater god rays (air shafts are now suppressed when submerged, correctly).

**Open inconsistency for Bryan's eye:** `UnderwaterSkyColor` is bright teal while the volume's `deepTint` is
dark navy, so distant things fade toward a colour the surrounding water never reaches. The extinction maths is
right; the two just disagree about what deep water looks like.

## A hard block at the horizon is CLOUDS, not the sea ray (2026-08-23)

Bryan reported a rectangular hard-edged block sitting on the water horizon and asked whether the
enter-water / under-the-curve / exit-water artifact had returned after `e5ddd5a`. It had not.

Reproduced at his saved teleport `SeeThroughWater` (camera 178 m up, looking 14.6 deg BELOW horizontal over
water - the exact grazing geometry the sea-ray bug lived in):

| test | result |
| --- | --- |
| midday, clouds on | horizon smooth, no block |
| low sun (`time.set-local 0.76`), clouds on | **block appears on the horizon** |
| same, `cloud.density 0` | block gone, horizon a clean curve |
| `WaterOff` (26) | no block in the terrain silhouette |

So the block is the cloud raymarch at the horizon. It only shows at low sun because that is when the horizon
clouds are lit enough to see - which is why it reads as a time-of-day effect. The stair-stepped edges on the
other horizon clouds in the same frame are the same artifact and are the tell.

**Method note:** `cloud.density 0` is a one-command split for "is this clouds?", far cheaper than reasoning
about the raymarch. `quality.cloud-steps` is the related knob. Pair it with `time.freeze on` or the sun
drifts between the two captures and the comparison is worthless.

**Care:** `cloud.density` changes the runtime DTO only - `CloudSettings.asset` keeps its authored
`DensityMultiplier` and play-stop restores it - but the console reports 0-1 while the asset stores the
internal multiplier, so you cannot read the old value back off the asset to restore it. Record the value
BEFORE changing it.

## Snell's window: attempted and NOT landed (2026-08-23) - read this before retrying

Four hypotheses, four failures, reverted. The design is probably right; the iteration loop was not.

**What is verified true:**
- The underwater sky branch in `Atmosphere.shader` DOES run. Measured at a submerged camera with a probe
  returning the three gate terms: `SkyDepthMask = 0.99`, `CameraUnderwater01 = 0.85`,
  `WaterInterfaceFrontMask = 0.65`. Do not re-suspect the gate.
- `CalculateScattering(start, dir, sceneDepth, sceneColor)` lives in `Includes/Atmosphere.hlsl` and is in
  scope there. `_AtmosphereRadius` = 6087, `_SeaLevelRadius` = 5000, so a submerged start is BELOW the
  surface the atmosphere integrates outward from - the leading suspicion is that it returns `sceneColor`
  unchanged from such an origin. Moving the start to where the ray exits the water did not visibly change
  anything, but see the loop problem below before trusting that.
- `CameraSeaOffset()` does NOT exist in Atmosphere.shader (it is WaterVolume's). Compute the offset inline,
  as `CameraUnderwater01()` there already does.

**The real blocker is the iteration loop, not the physics.** A probe that returned flat red did not paint,
and a later measurement proved the branch was executing all along - the capture had read a STALE shader
variant. `ShaderUtil.GetShaderMessages` returning zero errors does NOT mean the new variant is live. Every
one-cycle conclusion in that stretch is therefore untrustworthy. Before resuming: establish a loop that
proves which build is running - change a colour to something unmistakable, wait, capture, confirm, and only
then measure. Budget ~2 min per cycle and do not stack hypotheses between confirmations.

**Design that was written** (kept here so it need not be re-derived): critical angle
`asin(1/1.333) = 48.75 deg`, `COS_CRITICAL = 0.6593`, `window = smoothstep(COS_CRITICAL - 0.10,
COS_CRITICAL + 0.05, dot(viewDir, camUp))`; refract with `sinAir = 1.333 * sinWater`, rebuild the direction
from the tangent and up components; attenuate by `exp(-(3.80, 1.75, 0.58) * saturate(depthAbove /
cosWater / 40))` to agree with the volume's own coefficients; composite the water pass's surface on top
with `WaterInterfaceFrontMask`.

## Seeing the far side of the ocean through the near side (2026-08-24, `2704811`)

From above water looking at the horizon, the ocean past the planet's curve rendered THROUGH the water in
front of the camera - a second waterline above the real one, sky glowing between them, checkered moire along
it. Bryan asked whether a per-frame ray check was needed. It is not.

**The key geometric fact: on a convex sphere, a back-facing water fragment cannot legitimately be seen from
above the surface.** If the underside is facing you, that water is past the horizon. So the sign of
`dot(viewDir, normalWS)` settles it - and Ocean.shader was already computing exactly that as
`signedViewFacing`, one line away, for foam.

```
float cameraAboveWater = smoothstep(-0.5, 1.5, cameraSeaOffset);   // vs the SHORE field, not _SeaLevelRadius
float frontFacing      = smoothstep(-0.03, 0.03, signedViewFacing);
layer.alpha *= lerp(1.0, frontFacing, cameraAboveWater);
```

`Cull Off` has to stay - from below, the underside IS the whole view - so this gates on the camera's side
instead. Underwater it is a no-op by construction (`cameraAboveWater` = 0, term collapses to 1).

**Bonus the same test buys:** water writes no depth, so a nearby wave's far slope is not occluded by its own
crest either. Same sign test hides both.

**Rejected, with reasons:** dynamic `Cull Back`/`Cull Front` by camera side is free but pops at the crossing
and cannot handle the near-wave case; depth-writing the surface fixes self-occlusion but breaks transparency
ordering across the whole water stack.

**Generalises:** any "I can see through to geometry that should be over the horizon" on a sphere is a
back-face question first. Reach for the facing sign before a ray.

## Horizon opacity: key it on PATH, never on the surface normal (2026-08-24, `b18e862`)

Bryan wanted the water opaque toward the horizon (so the planet's curve cannot show through) while staying
transparent when looking steeply down. His framing is the correct model and the shader already had the
quantity for it:

> Water is cumulative. The more water you look through, the more opaque it gets.

`viewPath = 1 - exp(-viewPathMeters / (DeepDepth * 0.62))`, where
`viewPathMeters = cameraDistance / max(viewFacing, 0.12)` - a Beer-Lambert integral over the SLANT distance.
Along the surface that is kilometres, so it saturates; straight down it is metres, so it stays near zero.
One term, both requirements:

```
float deepPath = smoothstep(0.80, 0.995, viewPath) * cameraAboveWater;
layer.color = lerp(layer.color, skyReflection, deepPath * lerp(0.15, 0.60, daylight));
layer.alpha = lerp(layer.alpha, 1.0, deepPath);
```

**The mistake worth not repeating.** The first version keyed this on `reflectFresnel`. That is built from
the RIPPLE NORMAL, which is interpolated per vertex, and a single water quad at the horizon covers a large
part of the screen - so the effect switched on and off facet by facet and painted a hard-edged bright
rectangle across the middle of the waterline.

**Generalises:** any effect meant to vary smoothly across the horizon must be driven by a quantity that
varies smoothly in SCREEN space. Camera distance and path length do. A per-vertex-interpolated normal does
not, and at grazing angles its faceting is magnified enormously because one quad spans many pixels. Same
family as the two field-quantisation staircases earlier in this arc: a smooth-looking gate over a piecewise
input shows the pieces.

**Also:** `time.set-local 0.78` was NIGHT at the 09:56 F10 viewpoint (global 0.10). Local time maps
differently per camera longitude - check the render, do not assume 0.75 is sunset everywhere.

## The raised slab of water: vertex DISPLACEMENT, gated 10:1 on a vertex channel (2026-08-24, `3f7082c`)

A rectangular block of water standing out of the sea - flat top, hard vertical side wall. Nothing done in
the fragment shader ever moved it, because it was never shading: the base mesh is flat (all 69,285 water
vertices within 3 km measured at exactly 0 m altitude) and the shape comes from the VERTEX displacement.

`WaterDisplacement.hlsl`:
```
openWater01 = smoothstep(0.30, 0.85, body01);
amplitude   = _SwellAmplitude * lerp(0.10, 1.0, openWater01) * energy;
```
Ten to one, driven by a per-vertex channel. Measured across the slab edge: bodyFactor 0.667 -> 0.294, so
amplitude 0.66 -> 0.10 - a 6.6x step in wave height over one mesh boundary. Fixed with three passes of
neighbour averaging on the final bodyFactor (after ClassifyWaterBodies, after the WaterBodyMap lake
override, and after BuildCoverSet's inheritance, which can leave adjacent cover vertices holding values from
different bodies).

**Whenever an artifact has a flat top and vertical sides, suspect vertex displacement before shading.**
Confirm in one step by histogramming water vertex altitudes near the camera - a flat mesh means the shape is
being made in the vertex stage.

## Method: tiled debug IMAGES beat single-pixel probes

Four consecutive wrong hypotheses on this one artifact, then Bryan said "make some debug views". Capturing
beauty + WaterData(11) + SurfaceAlpha(19) + SurfaceAlphaParts(55) at the same viewpoint and stacking them
with ffmpeg located it immediately: the first three showed the same vertical edge at the same x, the fourth
barely did.

My pixel probes had failed TWICE on the same artifact:
- `Texture2D.GetPixel` has y=0 at the BOTTOM while the image has y=0 at the TOP. A scanline chosen off the
  screenshot lands in the wrong place. Convert, or scan a range of rows.
- A single row can simply miss the edge. Scan for the max horizontal jump over a band of rows instead of
  picking one.

**Cheap and no shader edit:** modes 11 (depth01, shore01, body01), 19, 55 (alpha, viewPath, fresnel) and
64 (nearColor, farColor, pathBlend) already exist and bracket most of the surface chain.

## Process failure worth not repeating

`b18e862` was committed and verified clean. I then made THREE further changes without verifying any of them,
and Bryan looked at the result - which by then contained a regression I had introduced (the sphere-normal
swap made `horizonPathMeters = cameraDistance / max(horizonFacing, 0.02)` saturate abruptly across a whole
region, because the ripple normal's jitter had been smearing that transition). I then spent four cycles
hunting a bug I had just created. **Verify before showing, and never stack unverified changes on a verified
commit.**

## RETRACTION: the "raised slab" was NOT vertex displacement, and not bodyFactor

The section above claiming the slab was swell amplitude gated 10:1 on `bodyFactor` is **WRONG**. It was
committed to memory before the claim had been tested against the shape itself, and the fix it describes
(`3f7082c`, neighbour-averaging bodyFactor) has been reverted along with it - it changed nothing visible.

What it actually was, found the moment Bryan said "turn that shape bright red": **my own `deepPath` term**
from `b18e862`, the path-driven horizon opacity. Colouring my three added terms - R = `1-frontFacing`,
G = `deepPath`, B = `1-ShorelineTrim` - lit the slab in GREEN with its exact hard vertical edge. Reverted in
`19bb02e` together with `afe78ba`.

Wrong diagnoses I gave for this ONE shape, in order: `reflectFresnel`, `viewPath`, the interpolated ripple
normal, `depth01` (via a `depthBlend` falsification probe that changed global contrast and so masked rather
than isolated), the swell `bodyFactor` gate. Every one was a real discontinuity somewhere in the frame. None
was the shape being pointed at.

**The two rules that would have saved all of it:**

1. **Colour the shape itself first.** Not a term you suspect - the actual pixels the user is pointing at.
   Bryan has now had to give this instruction twice in one session (the lake blocks, then this), and both
   times it identified the cause in a single capture after many failed cycles. Correlated discontinuities
   are everywhere in a frame like this; only the shape itself is evidence.
2. **Suspect your own last few commits before anything in the existing codebase.** The slab appeared two
   commits after I started adding horizon terms, and I spent the entire investigation looking at code I had
   not written.

**Also:** do not write a mechanism into memory until the fix built on it has been verified against the
artifact. This retraction exists because I wrote the bodyFactor story up as settled fact on the strength of
a plausible measurement and a green build.

## The stepped shape on the sea is the ATMOSPHERE's composite depth (2026-08-24, `a96fd1c`)

A hard-edged region of slightly different water, with a vertical side, sitting near the horizon. Bryan
reported it repeatedly; I gave five wrong diagnoses before colouring it properly. Isolated at last with
`debug.mode ShapeIsCompositeDepth`, which renders the shape ON ITS OWN.

**It is not in the water at all.** `Atmosphere.shader`'s `CompositeDepthScaled` substitutes the water
forward depth from the volume prepass for the scene depth, gated by `WaterInterfaceFrontMask`. Where that
depth steps, the distance aerial perspective is computed from jumps, and the result is a patch of different
haze lying on the sea.

**Elimination chain, every step read by eye from a full frame:**

| test | result |
| --- | --- |
| `ShapeIsDepth` / `ShapeIsShore` / `ShapeIsBody` | all paint UNIFORMLY - none of those vertex channels |
| shape survives with the water painted flat red | not produced by the water surface shader |
| `SurfaceOnly`, `AtmosphereBypass` | shape ABSENT - the atmosphere owns it |
| `ShapeIsWaterMask` | clean - not the gating mask |
| `ShapeIsCompositeDepth` | shape PRESENT |

**Wrong diagnoses I gave for this one shape, in order:** `reflectFresnel`; `viewPath`; the interpolated
ripple normal; `depth01` (from a `depthBlend` probe that changed global contrast and masked rather than
isolated); the swell `bodyFactor` gate (written into memory as fact, then retracted); my own `deepPath`
horizon term (real, but a different artifact). Every one was a genuine discontinuity somewhere in the frame.
None was the shape.

**The rule that finally worked, and it was Bryan's:** paint the shape itself and hand the tool to the person
who can see it. A correlated discontinuity is not evidence; only the shape is. When an artifact survives
several confident fixes, stop fixing and build the view that makes it identifiable - and make it something
Bryan can switch on himself, so the target is agreed before any more code changes.

**Still open:** why the prepass writes a stepped water forward depth. Not investigated.

## CORRECTION: the stepped sea shape is the PREPASS forward depth (2026-08-24, `1d1258a`)

Supersedes the section above that stopped at "the atmosphere's composite depth". The composite depth carries
the shape, but only because it inherits it: `debug.mode ShapeIsPrepassDepth` renders the shape on its own -
bright region, hard right edge, V-shaped notch. The volume prepass writes a stepped water forward depth over
that region; `CompositeDepthScaled` substitutes it for scene depth, so the distance aerial perspective is
computed from jumps and a hard-edged patch of different haze lies on the sea.

Chain, every link measured rather than argued:

| quantity | verdict |
| --- | --- |
| water vertex channels (depth01/shore01/body01) | smooth |
| scene depth / seabed | smooth |
| interface mask | exactly 1.0, uniform |
| `min()` branch selection | uniformly water, never switches |
| **prepass water forward depth** | **THE SHAPE** |

Not yet investigated: why the prepass writes a step there.

## THE lesson: three bad instruments, three wrong answers

Every "that reads clean, so it is not that" in this investigation was produced by a broken visualisation, not
by the data. The measurements were fine; the instruments lied.

1. **Two-tone split at a fixed threshold.** `value < 0.5 ? red : blue` shows NOTHING when both sides of a
   boundary sit on the same side of the split. Reported depth01, shore01, body01 and the interface mask all
   "uniform" for a shape that plainly existed.
2. **Forgetting the debug view is composited over.** The water-data modes were not in
   `ShouldBypassAtmosphereForWaterDebug`, so aerial perspective hazed them and flattened the contours.
3. **`frac(d / 40)` on a distance of order a kilometre.** Cycles ~25 times and aliases into noise at grazing
   - it hid the very step it was built to find, and sent the search into the atmosphere for several rounds.

**Rules that follow.** Prefer a CONTOUR BAND (`frac(x * k)`) over a threshold, and pick `k` so the whole
range spans a handful of bands, not dozens - check the expected magnitude first. Register a new water debug
mode in `ShouldBypassAtmosphereForWaterDebug` unless it deliberately lives in the atmosphere. And when a view
reports "clean", suspect the view before believing it: a null result from an unvalidated instrument is not
evidence.

## RESOLVED: the prepass recorded the FAR ocean's distance (2026-08-24, `1fbcedf`)

Closes the arc above. `WaterVolumePrepass.shader` draws `Cull Off` - required, since from below the surface
the underside is the whole view - and `ZWrite Off` with the depth attachment bound `AccessFlags.Read`, so its
own triangles never depth-test against each other. **Whichever rasterises LAST wins the pixel, which is
index-buffer order, not distance.** At grazing the near ocean and the ocean past the horizon cover the same
pixels, so in patches the forward depth written is the FAR surface's. `CompositeDepthScaled` substitutes that
for scene depth and the atmosphere hazes those patches by the wrong distance - hard-edged, boundaries along
triangle edges, which is the V-shaped notch.

Fix: discard back-facing water in the prepass fragment when the camera is above the surface - the same test
already proven for the visible surface in `2704811`. Underwater it collapses to `clip(1.0)`, so `Cull Off`
still does its real job.

`ZWrite On` was the other candidate and is NOT available: the render graph binds that depth attachment
read-only, so enabling it would write into the camera depth buffer and change every downstream pass.

**Ruled out by measurement before any fix was written** - the discipline that finally worked:

| suspect | test that killed it |
| --- | --- |
| wave displacement | identical shape at `_SwellAmplitude 0` |
| `min()` in CompositeDepthScaled | branch selection uniformly water, never switches |
| interface mask | exactly 1.0 |
| scene depth / seabed | smooth |
| every water vertex channel | smooth on a direct ramp |

**Same root as the very first symptom Bryan reported in this arc** - the far side of the ocean visible through
the near side. `2704811` fixed that for the rendered surface; this pass had the identical flaw and was missed
because nothing had looked at what the PREPASS writes. When a back-face problem is found in one water pass,
check every other pass that rasterises the same mesh: Ocean.shader, WaterVolumePrepass.shader, and any future
one all draw it Cull Off.

## RESOLVED: Snell's window (2026-08-24, `bc198ac`)

Supersedes "Snell's window: attempted and NOT landed" above. It works: from below the surface, looking up now
shows the whole sky compressed into a cone of half-angle asin(1/1.333) = 48.75 deg about vertical, brightening
towards the rim where it crowds against the critical angle, dark water outside it.

**Three earlier attempts failed for a reason that was never the physics.** The window was computed correctly
every single time and then discarded by the last line of the branch:

```
return lerp(result, originalCol.rgb, surface);   // surface = WaterInterfaceFrontMask
```

That mask is ~1 underwater looking up, so `originalCol` - the volume's flat tint - replaced the window
entirely. The blend is right for grazing angles, where it preserves the surface's ripples and glint; it just
must not win overhead, where the surface is nearly transparent. Now faded out inside the window:
`surface * (1.0 - window * 0.85)`.

**How it was finally found:** probing what `CalculateScattering` actually RETURNED rather than theorising
about it. `(0.369, 0.486, 0.369)` against a flat-teal frame of `(0.145, 0.424, 0.435)` proved the scattering
call was fine, so the loss had to be downstream - and there was only one line left. Straight application of
[[feedback_identify_before_fixing]].

**Retract from the earlier entry:** the suspicion that `CalculateScattering` returns `sceneColor` unchanged
from a submerged origin is FALSE. It returns a real scattered colour. Passing a very large sceneDepth is
correct and is what the normal sky path does too.

**Not art-directed.** Brightness and rim falloff are physically motivated but verified at ONE sun angle only.
Expect tuning against Bryan's eye.

**Also closed the same day:** the grazing "seeing the planet's contour through the water" complaint fixed
itself with `1fbcedf` - it was a symptom of the prepass recording the far ocean's distance. The horizon
opacity work built for it (`afe78ba`, `b18e862`) was reverted and was never needed.

**Water still open:** underwater god rays (air light shafts are suppressed when submerged, correctly; the
water-column equivalent does not exist), and `shore01` is vestigial now that foam and the shoreline are
per-pixel.

### Snell's window - verified at two sun angles, and what is still missing

Checked at sunset and at local noon, 5 m down, looking straight up, 90 deg fov. Works at both; at noon the
SUN is visible refracted inside the cone, which is the correct behaviour and a good sign the refraction maths
is right.

**Known gap, not a bug in the window:** outside the critical cone the surface should be a MIRROR showing the
underwater scene (total internal reflection). That term does not exist, so it falls back to the flat ambient
`UnderwaterSkyColor`, which is bright. Consequences measured at noon: inside 0.50/0.66/0.58 vs outside
0.29/0.54/0.49 - a gentle gradient rather than a defined disc, and a green cast that bleeds across the soft
boundary.

Adding TIR is a real increment, not a tweak: it needs the underwater scene sampled about the reflected
direction, not a constant. Judgement call whether it is worth it versus underwater god rays, which are more
visible. Left for Bryan.

## Underwater compositing restructure - LANDED (2026-08-24, uncommitted)

Design doc: [docs/design/2026-08-24-underwater-compositing.md](../../docs/design/2026-08-24-underwater-compositing.md)

**The flood was `WaterVolume.shader:737`, NOT the atmosphere branch.** The earlier entry here blamed the
atmosphere on the strength of `SurfaceOnly` vs beauty - but `SurfaceOnly` returns `source` at
`WaterVolume.shader:690` too, so that comparison switched off both passes and could not attribute the
artifact to either. **A debug mode that disables more than one pass cannot tell you which one is guilty.**

The real chain: the surface writes no depth, so every underside pixel classified as "no geometry"; the volume
composite runs BEFORE transparents and returned `UnderwaterNoDepthColor` outright, painting flat teal over
the sky; `Ocean.shader` then blended the underside onto that at ~a third of an alpha. Three separate water
column colours existed in one frame. **The flood was gated on `IsProductionEquivalentDebugMode`, so no debug
mode could show it** - that is why five rounds missed it. Found with a temporary probe gated on
`_SceneDepthDebugRange`, a float settable from C# with no enum change.

Landed: one composite in `Atmosphere.shader` - `interface * transmit + column * (1 - transmit)`, with
`interface = lerp(TIR mirror, refracted sky, 1 - fresnel)`. Snell's window is now the Fresnel term, so the
rim and the hard outer edge come from physics rather than a placed smoothstep, and TIR exists for the first
time. Constants `WATER_ABSORPTION` / `WATER_IOR` hoisted into `WaterVolumeData.hlsl`.

Extended 2026-08-25: the interface now refracts and reflects about the **swell normal at the exit point**
(atmosphere includes `WaterDisplacement.hlsl` and calls `ComputeOceanSwell`), so Snell's window heaves with
the waves instead of being a fixed circle; TIR reflects about that normal too (about the PLANET normal every
direction outside the cone mapped to the same near-horizontal ray and came back flat); and the column
scatters forward toward the sun's REFRACTED direction. **W19 closed** - the drifted `CameraUnderwater01` pair
is one `CameraSubmerged01` in `WaterLevelField.hlsl`; each copy had one half right (level field vs swell
band).

Three traps found en route, all likely to recur:
- **The prepass depth channel is unusable underwater** - it clips nothing there by design, so it records the
  ocean past the horizon. Using it for the path to the surface split the view along a triangle edge. Use
  `WaterLevelField.hlsl` instead.
- **`CalculateScattering` returns the background for any ray starting inside `_SeaLevelRadius`**, and the
  exit point sits exactly on that radius. The window was black everywhere but the frame edges. Lift the
  start point clear of the sphere.
- **`Ocean.shader`'s underside colour is NOT interface radiance.** It is the water body seen from ABOVE -
  body tint, sky reflection, above-water lighting. Making the surface opaque underwater so the atmosphere
  could reuse its ripples painted an above-water sheet over the window, split along mesh triangle edges.
  Reverted; `Ocean.shader` is untouched.

Verified with play mode PAUSED. Seven viewpoints **0/518400 pixels different from HEAD**.
**GOTCHA: `Camera.Render()` still advances `_Time.y` ~0.01 s while paused**, and across two tool calls that
moves a glint and reports tens of differing pixels for a null change - print `_Time.y` and require both sides
to match, or you will chase a phantom regression (I did, twice).

**God rays landed the same day** as `UnderwaterSunShafts` - a 10-step march that asks, per step, where that
step's SUN ray crossed the surface, so light is attenuated by the real path in and out. Added over geometry
as well as sky. It REPLACED a hand-set ambient sun tint inside `UnderwaterSkyColor`; keeping both would have
been a second copy of "brighter toward the sun", the duplicated-override shape this arc exists to kill.
`SHAFT_SCATTER` was set by measuring what the tint produced, not by taste.

**Ripple hoist DONE** - `ComputeWaterRipple` now lives in `WaterDisplacement.hlsl` beside the vertex swell
(domain warp + 4 long + 3 short waves, returns a `WaterRippleField`). 53 lines out of `Ocean.shader`, which
keeps its own breakup/cell/resolve. **Proven bit-identical at 7 viewpoints, 4 showing water surface.**

**`_WaveAmplitude`/`_WaveScale` PROMOTED to globals** (2026-08-25) so the atmosphere can evaluate the same
waves: consts in `ShaderGlobalIds.Water.cs`, `Shader.SetGlobalFloat` at `PlanetWaterSurface.cs:266-267`,
deleted from Ocean's Properties block AND its local decls, `WaterDebugModule` reads globals.
`EvaluateRippleParameters` hoisted too. **Verify a promotion with `material.HasProperty(name) == false`** -
that is the direct test for the shadowing trap, and a control property that should stay True catches an
over-broad delete. Measured after: globals 3.4/480, both HasProperty False, `_WaveNormalStrength` still True.
Snell's window now visibly wave-lobed at wind 18 m/s (`v12_wind18_wide_d3.png`); at the usual wind 0.1 the
chop is subtle because ripple amplitude is gated by wind.

Measured, not guessed: mean `|tiltGain|` across the march is **0.03** at the authored 90 m swell. Focusing
goes as surface CURVATURE and curvature as 1/wavelength², so a 90 m swell focuses ~300 m down while a 2 m
ripple focuses ~14 m down - which is why real caustics are sharp on a shallow bed. **Do NOT try to reuse
`CausticPattern` per march step - it is 81 animated Voronoi cells per call (~970 per pixel at 10 steps).**

**Underwater at night was too bright (Bryan, 2026-08-25) - the cause was a COLOUR, not a light level.**
`UnderwaterSkyColor` lerped toward a "lit" colour whose night end `(0.012, 0.105, 0.165)` was BRIGHTER than
the authored deep colour in green and blue, so midnight underwater was a mid-blue however dark the world
above was. Colour and light level are now separate; the night floor is
`saturate(_NightAmbientIntensity * 0.10 + 0.015 + moonlight)` - **the same floor `Ocean.shader:701` uses for
the surface**, so both sides of the waterline move together. Daylight is unchanged by construction.

**The same defect existed a second time, in `WaterVolume.shader`, for the OCEAN only.** The seabed at night
read `(0.094, 0.247, 0.247)` while the column above it was near-black: `fogColor = volumeTint * lerp(0.14,
0.62, volumeLight)` held a seventh of daylight whatever the sun did, and `FarTerrainWaterlineColor`'s
`lerp(0.34, 0.82, volumeLight)` held a third. **The LAKE body in the same function had already been fixed
for exactly this** ("darkens to near-black at night instead of self-glowing while the terrain is dark") -
the ocean path was simply missed. Floors dropped to 0.015 / 0.02; both are unchanged at full light, so
daylight is identical by construction. **Pattern to look for: `lerp(floor, full, lightTerm)` where floor is
a meaningful fraction of full - that is a light level baked into a colour, and it self-glows at night.**

**Measured: this is a ~29x reduction at night, which is far more than "a bit".** Old night value
`(0.0105, 0.0871, 0.1528)` linear vs new `~(0.0007, 0.0035, 0.0053)`. It is near-black. Two facts before
re-tuning: (1) the house night floor really is 0.017, so this now MATCHES the surface rather than
overshooting relative to it; (2) `_MoonIntensity` is only ~0.011 at FULL moon against `_SunIntensity` 17, so
the moon term is worth ~25% and cannot carry a readable night on its own. If Bryan wants underwater night
readable rather than realistic, that is a deliberate art call and needs its own `WaterDto` level - do not
just inflate the shared floor, it moves the whole world.

Still open: TIR mirrors the column not the seabed (its reflected ray points down and behind the camera, so
SSR cannot supply it); `SHAFT_SCATTER` and the night level have not had Bryan's eye; shaft march cost is
unprofiled (~110 trig/underwater pixel). Observed but NOT investigated: distant scatter impostors over the
far seabed read as dark angled specks (`v9_seabed.png`) - same family as [[project_scatter_dusk_lighting]].
