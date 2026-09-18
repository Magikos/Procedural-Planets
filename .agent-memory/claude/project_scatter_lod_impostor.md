---
name: project-scatter-lod-impostor
description: scatter LOD system — shared ScatterLodBatcher, far-field impostor tier (dynamically lit, day/night-correct), ScatterLodStrip workbench, contact-sheet validator, and hard-won Unity gotchas
metadata:
  type: project
---

## Current-code correction — 2026-09-09

The grid/cell policy described in the September 3 entry is historical.
`Assets/Scripts/Planet/Scatter/ScatterImpostorFactory.cs` now sets `OctGridN = 8` and `AtlasCellPixels = 64`.
`Assets/Editor/GeneratedImpostorBakeTool.cs` uses those factory constants.
`Assets/Editor/ScatterImpostorBakeTool.cs` retains a 4-view grid per axis and the baker default cell size.
Do not force both routes to the old shared policy. The new `pp-scatter-and-impostors` skill routes current checks.
This is a source inspection, not a new bake or visual validation. Earlier measurements remain evidence for their recorded revisions.


Scatter LOD system on branch `scatter-placement` (2026-07-27, updated 2026-07-28).

**Architecture**
- `ScatterLodBatcher` (Assets/Scripts/Planet/Scatter/) is the single shared per-prototype LOD draw:
  distance-banded mesh LODs (LOD i drawn in `[LodEndDistances[i-1], LodEndDistances[i])` by squared
  distance, batched into `RenderMeshInstanced`), then an optional far-field `Impostor` tier. Both the
  planet's `ScatterRenderer` and the strip harness draw through it (unified b0fc0c0), so LOD/impostor
  tuning in the fast scene is exactly what the planet renders. `ScatterRenderer` buckets instances by
  prototype on swap (N1) then bands each bucket through the batcher.
- **Impostors run on the real planet (f662b76, play-verified).** `ScatterPrototypeDto` has a DERIVED
  impostor policy (computed props, no SO authoring, DTO tests unchanged): `HasImpostor` = mesh cull >=
  300 m (trees + ferns; rocks/bushes/grass excluded), `ImpostorEndDistance` = 1.75x mesh cull,
  `FarGatherRadius` = that end. `ScatterImpostorFactory.TryBuild(proto, bounds)` bakes + assembles the
  Impostor; both renderer and harness use it. `ScatterField` gathers each prototype to `FarGatherRadius`
  (per-prototype cull already existed) so trees are placed past their mesh cull for the far ring.
  `ScatterRenderer` bakes impostors at Configure, frees them on regen/teardown. Verified: 17 impostors
  (15 trees + 2 ferns), 4615 instances in a forest (budget not tripped), trees drew past 400 m cull.
  Follow-ups: extended tree gather raises candidate counts (watch budget); Scatter/Impostor shader must
  be in Always Included Shaders for a player build (Configure uses `Shader.Find`, editor-only-safe).
- Far-field impostor tier (f6ef526): `ScatterImpostor.shader` (cylindrical billboard around the instance
  surface-up axis, alpha cutout, distance dither cross-fade) + `ScatterImpostorBaker.cs` + the
  `ScatterLodBatcher.Impostor` draw band. Cross-fade band matches the mesh-LOD dither-out so
  mesh→impostor has no pop.
- **Impostors are DYNAMICALLY LIT and day/night-correct (91daf1f).** The baker renders UNLIT albedo
  (flat white ambient, no directional light); the shader lights the card at runtime from the same URP
  main light + `SampleSH` ambient the foliage mesh uses, with a synthesized spherical canopy normal
  (`GetMainLight()`, N·L). `CelestialManager.SunLight` is the URP main directional light it rotates for
  day/night, and `FoliageLit` lights through it via `UniversalFragmentPBR` — so impostor tracks the sun
  exactly like the mesh (verified in strip: sun sweep noon→low→below-horizon, mesh vs impostor parity).
  Do NOT bake lighting into the card (freezes it → bright at night). Baked normal map is a later upgrade.
- **Empty-bake guard (e566e2d):** `ScatterImpostorBaker.Card.Valid` is false when the silhouette keys
  almost no coverage (thin `_ForceLeaf` blades — Swamp/IceBog Reeds); callers skip the impostor tier and
  hard-cull at mesh range. Only trees are real impostor candidates anyway (cull 300–400).
- **Validator tool (de8abc5):** menu `Planet/Scatter/Bake Impostor Contact Sheet` bakes every prototype's
  card into `Temp/ScatterImpostorContactSheet.png` + warns which bake empty. Run after importing props.
  (2026-07-28 result: all 15 trees + rocks/bushes/grass/ferns clean, no atlas leaks; only 2 reeds empty.)
- Test scene `Assets/Scenes/Tests/ScatterLodStrip.unity` + `ScatterLodStripHarness`: lightweight, no
  planet/world services, bakes the impostor on Build and draws a fixed row at increasing distances
  plus a single camera-distance-swap asset. This is the dedicated fast-loading LOD workbench — do NOT
  develop LOD on the Planet scene (minutes to load). See [[project-scatter-biome-buildout]].

**2026-08-17 — speckled horizon FIXED, and the root cause is a trap.** Bryan reported distant
impostors reading as "spotty" and rebaking did not help. Cause: **an impostor atlas has TWO
mipmap settings and only one of them matters.** The runtime `ScatterImpostorBaker` allocates its
`Texture2D`, but the atlases the planet actually uses are the **saved PNGs** in
`Assets/Resources/Settings/Scatter/ImpostorAtlases/`, whose mip setting comes from the `.meta`
TextureImporter — and every one of them had `enableMipMap: 0`. So the runtime fix applied to a
path the planet never takes. Fixing the *bake tool* alone also does nothing to atlases already on
disk; the 104 existing `.meta` files had to be re-imported.
Why mips are required, not optional: a tree-line card covers a few screen pixels while its cell is
128 px, so unmipped it minifies ~16x and point-samples near-randomly; against the shader's hard
`clip(card.a - _Cutoff)` that noise becomes binary keep/discard = speckle. Settings that fix it:
`mipmapEnabled`, **`mipMapsPreserveCoverage` + `alphaTestReferenceValue = 0.5`** (matching
`ScatterImpostor.shader` `_Cutoff` — without it, averaging alpha down the chain thins silhouettes
until they dissolve), `filterMode = Trilinear`, `aniso 4`. The old "mips would bleed across cells"
objection is real but only in the deepest mips, where the whole card is a couple of pixels.

**2026-08-17 — generated impostor atlases now BAKED TO DISK.** `Tools > ProceduralPlanets > Bake
Generated Impostor Atlases` (`GeneratedImpostorBakeTool`) writes one atlas per `ImpostorShareKey` to
`Assets/Resources/Settings/Scatter/GeneratedImpostors/` + a `GeneratedImpostorManifest` in Resources;
`TreeInjection` loads them instead of baking. Finalize `scatterRenderer` **13,637 → 911 ms**; 136 of
138 impostor prototypes now use a disk card.
**TRAP 1 — the bake MUST run in PLAY MODE.** In edit mode the `FoliageLit` canopy renders black, the
coverage key reads that as background, and every tree bakes as a **bare trunk** — a normal-looking
atlas that shows as stick-trees on the horizon. The tool now hard-refuses outside play; do not remove
that guard.
**TRAP 2 — the staleness hash must be PER SPECIES, not per prototype.** Every age/seed variant of a
species has different meshes and they all share ONE card by design, so hashing a prototype's own
meshes matched only the variant that got baked and sent every other variant back to a live bake (cost
stayed at 4.5 s). `TreeInjection.ImpostorProbeHash` hashes a fixed probe (mid age, seed 1) of the
species instead — which also catches generator changes a def-only hash would miss. Both the tool and
the runtime call that same function.
Meshes are deliberately NOT baked: generating them is **measured at 45 ms for every tree variant and
150 ms for every rock**, so caching them would save nothing and would put the edit-a-TreeDef-and-play
loop behind a bake step. Only atlases are cached. **Rocks opt out entirely** — they keep the source
prototype's Synty card, because a 3 m stone billboards from 250 m to 1125 m where both silhouettes are
the same grey lump; baking them cost 418 ms + 26.8 MB each for nothing.
**Still live-baking every load: Swamp Reeds + IceBog Reeds (~590 ms).** They bake, fail the
empty-silhouette guard, and get discarded — every single load. Pre-existing, not yet fixed; they carry
no share key so the manifest cannot cache them.

**Two Unity gotchas (cost real debugging time — reuse these):**
1. Instanced billboards must read the per-instance matrix via `GetObjectToWorldMatrix()`, NOT raw
   `unity_ObjectToWorld._m03` field access. Under `RenderMeshInstanced` the raw field access doesn't
   resolve the instance matrix → quad draws nothing. A single non-instanced `Graphics.RenderMesh`
   hides the bug (there it IS the passed matrix), so test the instanced path.
2. A URP camera clears a manual `Camera.Render()`→RenderTexture to opaque black and IGNORES
   `backgroundColor` (verified: clear-to-red gave (0,0,0,1)). No transparent clear → can't get
   silhouette alpha from the clear. Key alpha off luminance (bg is reliably pure black); bake with
   `RenderSettings.ambientMode=Flat` + white ambient + no directional so the card is unlit albedo and
   all geometry keys above threshold. Also: `Mathf.SmoothStep(from,to,t)` is a smoothed lerp, NOT HLSL
   `smoothstep(edge0,edge1,x)` — for an edge remap write `t=saturate((x-e0)/(e1-e0)); t*t*(3-2t)`.

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Octahedral tree impostors] — 2026-08-10 (in scatter-placement): far tree impostors now octahedral (angle-grid atlas + camera-facing cell sample + blend + ShadowCaster) — kills the top-down slab + distant tree shadows; LOD reach 3×→4.5× w/ long fade. Commit e185631
- [Scatter LOD + impostor](project_scatter_lod_impostor.md) — 2026-07-27/28: shared ScatterLodBatcher (mesh LODs + far-field impostor tier, f6ef526); impostors DYNAMICALLY LIT/day-night-correct (91daf1f); empty-bake guard (e566e2d); contact-sheet validator (de8abc5); ScatterLodStrip workbench. **2026-08-17 speckled-horizon FIXED: an atlas has TWO mip settings and only the saved PNG's `.meta` importer matters** — all 104 had `enableMipMap: 0`, so runtime-baker fixes hit a path the planet never takes. Needs mipmapEnabled + `mipMapsPreserveCoverage`/`alphaTestReferenceValue 0.5` (shader `_Cutoff`) + Trilinear; fixing the bake tool alone leaves on-disk atlases broken. **2026-08-17 atlases now BAKED TO DISK** (`Tools > ProceduralPlanets > Bake Generated Impostor Atlases` + `GeneratedImpostorManifest`): finalize `scatterRenderer` **13,637 → 911 ms**. TRAP: the bake **must run in PLAY MODE** or foliage renders black and every tree bakes as a bare trunk (tool now refuses); and the staleness hash must be **per species**, not per prototype, or every variant still live-bakes. Meshes stay runtime-generated — measured 45 ms all trees / 150 ms all rocks, so caching them buys nothing. Gotchas: instanced billboards need GetObjectToWorldMatrix, URP manual-render RT clears black, Mathf.SmoothStep≠HLSL smoothstep

**2026-08-27 — canopy "tiny holes" FIXED, and the rule generalises.** Bryan reported conifers pocked
with a lattice of tiny holes. Cause: the mesh-LOD crossfade dithered the OUTGOING mesh LOD out over its
last 40 m while the incoming LOD drew solid underneath. A screen-door only hides a swap while the
successor tier **covers the same pixels**, and a coarser mesh LOD is a *decimation* of its predecessor —
thinner canopy, so it does not cover. Dithering LOD n out therefore exposed LOD n+1's gaps as a regular
Bayer lattice through every tree in the band. The impostor card is the only successor that does cover
(it is baked from the mesh, so silhouettes match), so it is the only swap allowed to dither.
`ScatterLodBatcher.FadeStartFor` is now the single owner of that rule; `ScatterGpuDraw` held a *duplicate*
hardcoded `far - TransitionWidth` and now calls the helper. Pinned by
`Assets/Tests/EditMode/ScatterLodFadeTests.cs`.
**TRAP 1 — material-level probes on scatter are no-ops.** `ScatterGpuDraw.MakeMeshBand` sets `_FadeStart`
/`_FadeEnd` on the band's `MaterialPropertyBlock`, and an MPB beats the material. Setting the value on the
Material asset changes nothing; patch the band MPB by reflection instead.
**TRAP 2 — generated foliage has no alpha map**, so albedo alpha is 1.0 and a `_Cutoff` probe (even 0.98)
cannot change the image. Do not conclude "the capture pipeline is broken" from a no-op probe.
Also note: `ScreenCapture.CaptureScreenshot` writes asynchronously, and a state change plus a capture in
the same call captures the PREVIOUS frame — set state, capture in a separate call, then poll for file size.
Follow-up DONE same day: the 40 m mesh-band overlap is gone. With intermediate LODs no longer fading it
bought nothing and cost a double draw in every annulus. Mesh bands now partition exactly - band N starts
where band N-1 culls - and both band tests are half-open [near, far), so there is no gap at the seam and
no instance is drawn twice. The impostor keeps its overlap; it is the only tier that must be solid before
its predecessor dithers. All three rules now live in ScatterLodBatcher as BandNearFor / ImpostorNearFor /
FadeStartFor, and ScatterGpuDraw no longer holds its own TransitionWidth constant.
Evidence: frozen-frame A/B at 294 m over a Taiga Pine, 184 mesh bands patched back to the old overlap by
reflection between the two captures. Amplified difference is confined to tree silhouette edges (raw
YMAX=16/255, YAVG=0.13) - the removed double draw. No seam, no canopy thinning, crowns solid in both.

**2026-08-28 REGRESSION FROM THE ABOVE, and the rule that was over-applied.** Bryan: "you can see the
firns and such apearing as I walk. So the LOD needs to be better." The hole fix made FadeStartFor return
`far` for the last mesh LOD whenever the prototype has NO impostor. That makes `_FadeStart == _FadeEnd`,
and the shader ramp `saturate((dist - _FadeStart) / max(1e-3, _FadeEnd - _FadeStart))` degenerates into a
STEP: the prop appears whole in one frame at its cull instead of dissolving. 21 of 176 drawable
prototypes have no card (HasImpostor needs MaxCullDistance >= 120): flowers 50-55 m, mushrooms 45 m,
grasses 90 m, corals 90-110 m. Those are the "ferns".

**Refined coverage rule.** A screen-door hides a swap only when what is behind the discarded pixels is
what should be there. The impostor card qualifies (baked from the mesh). **The BACKGROUND also qualifies**
- a prop with no card is supposed to be gone past its cull, so dissolving into the world IS the
disappearance. Only a coarser mesh LOD fails, because it is a decimation with a thinner canopy. So: the
last band ALWAYS fades - into the card if there is one, else over its own last 15%. Intermediate bands
still swap hard. The 15% is `ScatterPrototypeDto.MeshFadeFraction = 0.85f`, the same constant
ImpostorStartDistance uses, and it is a FRACTION on purpose: a 45 m flower cannot dither over the 40 m a
250 m rock can without being half-transparent for most of its visible range.

Evidence: live readback of every band MPB - before, 21 prototypes had a zero-width window; after,
hardPopping=0 out of 176, e.g. Grassland Grass [76.5, 90], Taiga Flower Blue [46.8, 55], Forest Tree
[340, 400]. Frozen-frame A/B at 18 m over taiga: difference is zero in the sky rows and peaks in the
mid-ground (YAVG per 120-row strip: 0, 0.19, 0.62, 1.20, 0.67, 0.60), confined to ground clutter and
correctly occluded by trunks and canopies. 264/264 EditMode tests pass.

**TRAP 3 - a prototype's bands are per PART, not per LOD.** Every flower/mushroom/grass prototype has TWO
bands, both `Lod=0`, same near/far: two drawable parts with one LOD each. Patching only "the last band"
misses half the geometry. Enumerate all bands.
**TRAP 4 - freeze time before any A/B.** With wind and the day cycle running, two captures one second
apart differ over the WHOLE frame (YAVG 21.5) and swamp the signal. `Time.timeScale = 0f` drops the
noise floor to exactly 0.
**TRAP 5 - restating TRAP 2's timing in the RenderTexture path.** RenderMeshIndirect snapshots the MPB at
submit, so a patch and a `cam.Render()` in the SAME execute_code call render the pre-patch state and diff
to zero. Patch in one call, capture in the next.

## 2026-08-28 — bare horizon canopies: SUB-PIXEL ALPHA TEST, not LOD at all

Bryan: "Those trees on the horizon have no leaves in the LOD's". Distant broadleaf trees rendered as
trunk-and-branch skeletons with a few isolated leaf specks. **It was not an LOD problem and not the
impostor.** `scatter.lodview` tinted the bare trees GREEN = LOD0, i.e. full-detail geometry.

**Mechanism.** Alpha-tested coverage does not accumulate across overlapping SUB-PIXEL primitives. While
each leaf card is several pixels wide, overlapping cards fill each other's alpha holes and the canopy
reads solid. Once a card is ~1 px, one card alone claims the pixel and the leaf texture's alpha coverage
alone decides leaf-or-sky — the canopy speckles away. Generated trees build canopies from many small
cards, so they hit this first and hardest.

**Fix (`FoliageLit.shader`).** New `LeafCutoff(uv, lm)` helper relaxes the cutoff toward 0 as the sampled
mip climbs: `_CutoffFadeMip` (default 4) start, `_CutoffFadeRange` (default 3) width. Mip level IS
texels-per-pixel, so this is independent of FOV, prototype scale, and whether leaves come from one card
texture or from a cell of a 4k atlas — no per-prototype tuning. The helper replaced three duplicated
`lerp(0.0, _Cutoff + _LeafFall, lm)` clip sites (ForwardLit / ShadowCaster / DepthNormals).

Evidence: same frozen camera, `_CutoffFadeMip` 99 (relax off) vs 4. Off = bare skeletons; on = full
canopies. Near-field crop is visually unchanged — no fattened or blocky leaves. Whole-frame diff YAVG
0.73, confined to the mid/far tree band; near field and sky are zero.

**Corrections to earlier notes.** `FoliageMeadowCanopy._Cutoff` is **0.4**, not 0.98 — every FoliageLit
material in the tree is 0.3–0.5. The leaf atlas mip chain is fine: measured coverage at cutoff 0.4 is
45.1 % at mip 0 and 48.0 % at mip 6, essentially flat. Do not re-open "mip alpha decay".

**Generated-tree band layout.** `TreeInjection.cs:288` gives every generated tree only
`{ cull * 0.6f, cull }`, so a 500 m cull means LOD0 = 0–300 m and the impostor does not start until
425 m. Anything wrong at 100–300 m is therefore LOD0 geometry, not a LOD swap — check the shader first.

**Runtime `_Cutoff` probe.** Reach the bands by reflection:
`Planet._scatterRenderer` → `ScatterRenderer._gpu` → `ScatterGpuDraw._protos[i].Bands[j].Mpb`, then
`Mpb.SetFloat("_Cutoff", x)`. MPB overrides do apply to `UnityPerMaterial` floats here. Never write the
material asset — 179 bands across 111 prototypes use FoliageLit, and restoring means
`mpb.SetFloat(name, mat.GetFloat(name))`. TRAP 5 (patch and capture in separate calls) applies.

## 2026-08-28 — see-through horizon trees: the mesh and the card CANNOT cross-dither

Bryan: "Still some transparent leaves looking trees on the horizon". A regular lattice of sky pixels
through the distant tree line. **Not the card, not the atlas, not the alpha cutoff.** It was the mesh tier
and the impostor tier screen-dooring against each other.

**Mechanism.** Both tiers clip against the same 4x4 Bayer table, opposite ways round: the mesh keeps
thresholds ABOVE its fade, the card keeps thresholds BELOW its coverage. That is complete only if the two
silhouettes agree PER PIXEL. They do not — the card is an octahedral billboard baked from the mesh, so
where the card's baked alpha is below `_Cutoff` it draws nothing at any coverage, and the mesh has already
dithered those thresholds away. Sky shows through.

**Fix (`ScatterLodBatcher.FadeStartFor`).** Stop cross-dithering. A mesh band fades ONLY when the
background is its successor (card-less props, `far * MeshFadeFraction` — the request-4 pop-in fix, kept).
With a card the last mesh LOD holds full coverage to its cull; the card ramps 0 to 1 *underneath* it over
`[ImpostorStartDistance, meshCull]` and is already opaque on the frame the mesh culls. `TransitionWidth`
(40 m) is deleted — `ImpostorNearFor` is now just `StartDistance`, which is exactly where the factory bakes
`_FadeInStart`, so the old lead-in only submitted quads the shader clipped at coverage 0.

Evidence: live readback of the shipped path — 437 of 475 mesh bands solid, 38 dithering (the card-less
grass/flower/mushroom/coral set), 155/155 card bands with band start == `_FadeInStart` and `_FadeInEnd` ==
mesh cull. Frozen-frame taiga horizon crops read solid. 265/265 EditMode tests pass.

**Ruled out by measurement, do not re-test.** Impostor `_Cutoff` 0.5 to 0.05 (YAVG 0.166, no visual
change); the baked Conifer atlas and its alpha are clean; arrival ramp / frozen time (steady state);
impostor far fade-out disabled (identical); part culls shorter than the prototype cull (none exist —
multiPartProtos=111, every drawable part's last `LodEndDistances` == its prototype's `MaxCullDistance`);
the card-less background dissolve alone (crop YAVG 0.00016); forcing card coverage to 1 everywhere
(YAVG 0.034, holes persist — which is what PROVED the card is clipped there, not merely faint).

**The ScatterImpostor ShadowCaster pass clips on `card.a - _Cutoff` only, never on coverage.** So a card
casts a full shadow anywhere its band draws, fade or no fade. Harmless at 0.85x cull and beyond, but it is
why the card band start is worth keeping tight.

---

## The handover is a SIZE, not a distance (2026-08-29, shipped)

The remaining pop was never card quality. It was **when** the swap happens. `ImpostorStartDistance` was
the authored `MaxCullDistance`, so each prop swapped at whatever on-screen size that distance implied —
5 px for a 0.7 m wildflower whose cull was pushed to 120 m to clear `ImpostorMinMeshCull`, 40 px for a
tree. **Below ~36 px a mesh's own alpha-cutout silhouette is mip-dominated and stops agreeing with
itself**: a beach reed at 8 px covers 2.2x the area it does at 192 px, a dead tree at 17 px covers 0.55x.
Whatever the card does, that swap steps.

Fix: `ScatterPrototypeDto.MeshHandoverPixels = 36f` + `ReferencePixelsPerMetre = 935f` (1080/(2·tan30))
→ `MeshCullDistance = min(authored, boundsSize · 935 / 36)`, and `ImpostorStartDistance => MeshCullDistance`.
`ScatterLodBatcher.BandFarFor` and `ScatterGpuDraw.Configure` clip every mesh band to it, skipping bands
that fall wholly past (`near >= far`) — without the clip mesh and card both draw in that gap.
`ImpostorEndDistance` and `FarGatherRadius` still key off the AUTHORED cull, so visible range and gather
cost are unchanged; only the mesh→card boundary moves inward. Average mesh cull drops to 47% of authored
(22% of the area) — a large mesh-draw saving that came free with the fix.

Measured `card/mesh` coverage at each prop's own swap, all 61 prototypes with a card, real renders:

| handover | min | p10 | median | p90 | max | step>15% |
|---|---|---|---|---|---|---|
| authored distance (before) | 0.000 | 0.799 | 1.083 | 1.125 | 1.415 | 13/61 |
| 32 px | 0.810 | 0.946 | 1.000 | 1.024 | 1.218 | 8/61 |
| **36 px (shipped)** | **0.800** | **0.984** | **0.998** | **1.004** | **1.208** | **2/61** |
| 40 px | 0.827 | 0.930 | 1.003 | 1.025 | 1.164 | 6/61 |

36 is a **measured minimum, not a round number** — a direct 30/34/36/38/40/44/48 sweep gives rms error
0.1224 / 0.0627 / 0.0552 / 0.0572 / 0.0579 / 0.1106 / 0.0981. The bad-count jitters because each mesh's
mip aliasing resonates with size; rms is the stable metric and 36 is its floor. Don't "tidy" it to 32 or 40.

**Accepted residue (2 of 61), both looked at by eye at 36 px and neither a pop:** TEM Grassland FlowerBush
pop 0.800 (its MESH covers 1.235x its own truth at 36 px; the card is right at 0.988) and Lake Lily
pop 1.208 (card 1.149 of truth; lily pads are seen near edge-on in game and the A/B is indistinguishable).

**Closed by measurement, do not re-open.** (1) Mesh `_Cutoff` as the lever — swept 0.25→0.55 by MPB on
all four then-offenders: reeds are already optimal at their authored 0.40 (0.971) and hypersensitive either
side, Lake Lily is `_Cutoff = 0` so there is no alpha test to tune, LMHPOLY Beach Reed is completely
insensitive (flat 0.826), TEM_Bush moves the WRONG way as cutoff rises (1.181 at 0.25 → 1.265 at 0.55);
Forest Tree and Birch Tree controls are flat at ~0.985/0.992. (2) `mipMapsPreserveCoverage` on the MESH
texture as the cause of TEM_Bush's bloat — disabling it on `TEM_Atlas_Vegetation_1A.png` moved mErr
1.235 → 1.227, i.e. nothing. Reverted.

Pinned by `ScatterLodFadeTests.MeshBandsPastTheHandover_AreSkipped_SoTheCardNeverDoublesWithTheMesh` and
`AHandoverBeyondTheLadder_LeavesTheAuthoredBandsUntouched`. 10/10 pass.
`Tools/ProceduralPlanets/Impostors/Validate`: 0 problems, 155/155 prototypes read a baked card.

**Rig gotcha.** Any measurement rig must set `Shader.SetGlobalFloat(ShaderGlobalIds.FoliageBacklight, 1f)`
and bind `_GrassInteractors` via `GrassInteractorFallback.Bind(ref gi)`, or FoliageLit draws nothing / the
card reads dark. Camera at elevation 10°, yaw 20° — elevation 0 is a degenerate A/B rig.

---

## 2026-08-30 — bare far-shore trees: the shared atlas was baked from the SAPLING

Bryan, with two lake screenshots: the far shore rendered as bare opaque trunks with isolated leaf specks
while the near trees carried full canopies. **Not the shader, not the cutoff, not the handover.** The
on-disk atlas for each species was baked from the WRONG variant.

**Mechanism.** `TreeInjection` ladders variant ages `Mathf.Lerp(0.25f, 1f, variant / (variantCount - 1))`
with `Variants = 3`, and all three declare the SAME `ImpostorShareKey`, so one card serves all of them
(`ScatterImpostorBaker.FromPrebaked` re-frames it to each variant's own bounds). Both selectors took
whichever prototype came FIRST: `GeneratedImpostorBakeTool` with `if (!byKey.ContainsKey(...))`, and
`ScatterImpostorFactory._sharedCards` by plain library order. First in library order is variant 0 — the
age-0.25 sapling. So every mature tree past its handover billboarded as a blown-up sapling.

Measured, generated Broadleaf by age: 0.25 -> h 4.15 m, bounds 6.10 m, 168 foliage verts, **21 leaf
clumps**; 0.625 -> 11.82 m / 17.21 / 816 / 102; 1.0 -> h 22.22 m, bounds 35.86 m, 2512 verts, **314 leaf
clumps**. 15x the clumps and 5.4x the height, sharing one card. Live library spread per share key:
Broadleaf 5.67 m -> 34.46 m (6.1x), Birch 3.82 -> 23.30, Palm 3.95 -> 23.54, Willow 4.36 -> 26.86.
Rocks are barely affected (rock-Forest 2.43 -> 2.93).

**Fix.** Both selectors now pick the LARGEST `BoundsSizeMeters` per share key — `ScatterRenderer.Configure`
sorts prototypes biggest-first before calling `ScatterImpostorFactory.TryBuild`, and the bake tool keeps
the max. They must agree or the disk atlas and the runtime fallback card disagree. Being wrong for the
small variants is harmless: their card is never more than ~36 px tall.

**The code fix alone changes nothing** — `GeneratedImpostorManifest.AppearanceHash` probes a fixed
`Species(s, 0.5f)` seed 1, so it cannot detect "baked from the wrong variant" and the stale atlases keep
validating. A rebake is mandatory: `Tools/ProceduralPlanets/Impostors/Bake Impostors (Generated Props)`,
in PLAY MODE. 52 atlases, 0 skipped.

Evidence: old vs new `Broadleaf.png` composited over grey — old cells are a twiggy sapling with ~20 leaf
clumps, new cells are a dense mature crown with the trunk mostly hidden. Old `Birch.png` cells are a bare
white stick with pale specks, which is Bryan's screenshot exactly. Atlas PNG size 2.1-2.8x (Broadleaf
396,166 -> 1,108,230 B). In-game at 60 m over Forest the far treeline is solid canopy with no poles.

**Resolved as NOT a defect, do not re-raise.** "Impostor start distances are wildly inconsistent between
variants of the same species (9 m -> 500 m)" is just the age ladder feeding
`MeshCullDistance = min(authored, size * 25.97)`.

**Audit gap that let this ship.** `ScatterLodSilhouetteAudit` measures the LIBRARY ASSET, not the injected
generated prototypes, and never compares card against mesh per share key. Fix that before trusting it.

## The share key must cover MATERIAL, not just geometry (2026-09-02)

**The concept every earlier round missed: an impostor is a LOD of a SPECIES GROUP, not of a tree.**
`ImpostorShareKey` groups prototypes and ONE baked atlas serves the whole group, so anything group
members differ by that the bake captures — geometry, scale, **and colour** — is lost. Every fix before
this one picked a better *representative* (first-in-library-order, then biggest). That is still choosing
which prototype to be wrong about. The grouping was the bug.

The card bakes GEOMETRY AND MATERIAL. The leaf material comes from the SOURCE Synty prototype via
`SyntyFoliage(p, species, def)`, not from the species, so one species key spanned several `_SeasonColor`
tints. Measured before the fix: **Broadleaf 4 tints across 11 prototypes**, Poplar 3, Birch 3,
flowerbush-Forest 2, flowerbush-Grassland 2. Broadleaf baked from `Golden Meadow Tree v2`, so the green
`Meadow Tree`, `Swamp Tree B` and tropical `Palm Tree v1` all wore a GOLDEN card and changed colour at
the handover ring. Across a far shore at roughly constant distance that ring draws a horizontal line —
Bryan's "line across the trees" and his "pop-in" were the same defect.

**Fix.** The `TreeInjection` share key is now `Species[-dead]@<foliage material name>`, and
`ImpostorProbeHash` strips everything after the `@` before the species parse (an unparsed key returns an
empty hash, the bake tool refuses to write it, and the species then live-bakes on every load forever).
Key count 52 to 61, 23 of them material-keyed. `ScatterImpostor.shader` has NO tint/season property, so
this could never have been corrected at draw time.

## Poplar and Cypress were pencils; ParallelAlign is the crown-width lever (2026-09-02)

Second, independent defect, found by measuring EVERY share key at once instead of one species per round.
Sorted by mesh aspect (crown width / height) and card coverage, living Poplar (0.12 / 4.4%) and Cypress
(0.19 / 4.8%) sat at the DEAD-tree floor (Broadleaf-dead 4.4%, Cypress-dead 4.6%, Acacia-dead 4.1%)
against Broadleaf 0.98 / 35.7% and Willow 1.21 / 45.2%. The card was FAITHFUL — the mesh really was a
3.24 m crown on a 28.02 m trunk. A card 36 px tall cannot show a real Lombardy poplar, so past its
handover it read as a bare pole standing in a canopy.

`TreeDef.ParallelAlign` (0 = along parent, 1 = perpendicular) is the width lever, NOT `Length` and NOT
`GravityAlign`. At 0.22 the branches ran along the trunk, so doubling their length only made them longer:
aspect 0.12 to 0.21. 0.55 overshot to 0.50. **0.35 lands on 0.32 at every age.** Cypress is a cone
primitive instead: `ConeRadiusFrac` 0.1 to 0.17.

After the rebake: Poplar 4.4% to 9.1-10.1% card coverage, Cypress 4.8% to 8.4%, both clear of the dead
floor. The atlases show leafy green columns and proper conifer cones. In-game at the LastDebugCapture
pose (seed 1691104419) the far shore is continuous canopy — no poles, no band.

**Ruled out here, do not re-open.** Overbright leaf tints clipping in the LDR atlas: ScatterCheck warns
`FoliageMeadowCanopy_Autumn` (1.55) and `_Golden` (1.60) exceed white, but measured clipping in those
atlases is 0.00 percent (max channel 124-130 of 255). Not the line.

**Gotchas for the next round.**
- Grep `LodDebugTint`, NOT `_LodTint`. `_LodDebugTint` is declared in the CBUFFER only
  (`ScatterImpostor.shader:181`, applied at `:318`; `FoliageLit.shader:80,407`; `Scatter.shader:28,222`),
  never in a Properties block.
- `ScatterDebugReporter.cs:63-69` prints AUTHORED impostor bands, not the DRAWN ones clipped by
  `meshCull`. It misleads.
- `ScatterRenderer` is a plain class service, not a MonoBehaviour — `FindObjectsByType` throws
  "FindAllObjectsOfType: The type has to be derived from UnityEngine.Object."
- To run a console command from Unity MCP `execute_code`, reflect `ServiceLocator.Get<IConsoleService>()`
  and call `RunCommand`.
- A Bash heredoc containing backticks is mangled by the shell wrapper before it lands. Write the file
  with the Write tool, then append it with `cat`.
- Still open, NOT a LOD defect: `Broadleaf@FoliageMeadowCanopy` cards are 12.8% covered against
  `_Golden`'s 35.7%. Same species, different group maximum, so the green Broadleaf group's biggest
  prototype is a genuinely sparser tree. The card matches its own mesh, but it reads trunk-heavy.

## 2026-09-03 — sweep of all 176 prototypes: the card was self-shadowing

`ScatterLodSweep` (on the LOD Bench) renders every tier of every prototype at the 36 px handover,
extracts coverage from a black/white backdrop pair, and writes `Assets/Screenshots/LOD/`
(176 strip PNGs + `lod_sweep.csv` + `lod_sweep_summary.txt`). Two defects it found:

- **Cards self-shadowed.** `ScatterImpostor.shader`'s ShadowCaster draws a LIGHT-facing quad through the
  prop centre; the ForwardLit pass shaded a CAMERA-facing quad and sampled the shadow map per fragment.
  The two planes intersect, so everything past the intersection read as shadowed — a hard straight line
  across the sprite and a big darkening. Measured: rock cards at lum 0.718 of their mesh, 1.031 with the
  Sun's shadows off. FIX: one shadow sample per card, at the billboard centre pushed 0.6·worldSize toward
  the sun. It does ship, it is not past the shadow distance: shadow distance is 250 m
  (`Assets/Settings/PC_RPAsset.asset:57`) and 112 of 155 cards start inside it.
- **Cards carry no self-occlusion.** A canopy shades its own trunk, a boulder its own underside; one quad
  cannot. With the self-shadow gone, cards read 1.137x the mesh (trees 1.20, rocks/bushes 1.07). FIX:
  `cardSelfOcclusion = 0.70` on the directional term only. After it: rocks 1.010, bushes 0.995,
  grass 1.007, trees 1.150. `CARD:DARK` went 37 to 0, `ok` rows 39 to 48.
- Trees keep +15% because their card COVERS 15% more than the airy mesh (cov 1.146) — a silhouette
  problem, not a lighting one. Do not chase it with more brightness.

Still open, with numbers:

- Cards are 11% fat on average (cov 1.108, iou 0.627; 30 FAT, 28 SILHOUETTE). `_Cutoff` 0.5 to 0.65 lands
  coverage at 0.995 but starves thin props (thin count 6 to 22, grass 0.86 to 0.73). Needs a per-prop
  cutoff, not a global one. Note the atlas PNGs import with `mipMapsPreserveCoverage` at
  `alphaTestReferenceValue = 0.4` while the shader clips at 0.5 — the two should agree.
- **135 of 176 prototypes draw a card baked from a DIFFERENT mesh.** 77 share keys, 50 of which cover more
  than one distinct generated mesh (every `rock-*` key holds 3 different rocks; `Pine@Gen Pine needles`
  holds 8). `002_Forest-Rock.png` is the proof: a wide flat boulder whose card is a tall pointed wedge.
  Sharing is NOT the average cause of low IoU though — solo-key cards score iou 0.527 vs shared 0.635.
  Costing a split: 1024^2 DXT5 albedo+normal is ~2 MB per atlas, so 77 keys is ~154 MB today; one atlas
  per prototype at gridN 4 (512^2) is ~0.5 MB each, ~88 MB — cheaper than today, 16 angles not 64.
- 21 prototypes have NO card at all (hard cull at mesh range), 78 have dead LODs, 3 corals render nothing
  at handover size (MESH_EMPTY) and 2 have empty cards.

## 2026-09-03 — corrections that supersede two claims above

**TRAP 2 above is REVERSED. The staleness hash is now PER PROTOTYPE, not per species.** The 2026-08-17
note was right about its own design and wrong about the design that replaced it. `ImpostorShareKey` was
carrying three unrelated jobs — grove grouping, atlas cache key, debug label — and because the ATLAS key
was per species while the variants had different meshes, **135 of 176 prototypes drew a card baked from a
different mesh.** `002_Forest-Rock`, a wide flat boulder, billboarded as the tall pointed wedge baked from
a sibling; every mature Broadleaf billboarded as the age-0.25 sapling it shared a key with (4.15 m and 21
leaf clumps against 22.22 m and 314). Split at `2d12833`:

| member | role |
| --- | --- |
| `SpeciesKey` | grove grouping only, feeds `ClumpGroupSeed` |
| `ImpostorKey` | atlas identity, `= DisplayName` (verified unique across all 176) |
| `ImpostorSourceHash` | staleness, from this prototype's own LOD0 meshes + part tints |

Both sides of the cache lookup now come from the FINISHED prototype, which is why the whole per-species
probe apparatus could be deleted: three `ImpostorProbeHash` implementations, three `_probeHashes` caches,
two `DestroyProbe` helpers, `PlantInjection.OwnsKey`, the bake tool's `IsRockKey`/`IsPlantKey` routing and
its biggest-variant selection, `ScatterImpostorFactory._sharedCards`, and `ScatterRenderer`'s biggest-first
ordering (which existed only because a shared LIVE bake was first-come). The probe only ever existed to
paper over the per-species key.

**Rocks no longer opt out.** Keeping the Synty card was wrong: the far card showed the Synty rock's colour
while the near mesh is our generated stone, so a rock visibly changed shade as the mesh took over, and at
dusk the two lighting paths diverged enough that the stale card read as glowing.

**The atlas grid is `OctGridN = 4`, not 8** — 16 angles into a 512 px card instead of 64 into a 1024 px
one. That is what pays for one atlas per prototype: ~154 MB of shared atlases becomes ~92 MB of correct
ones. `ScatterImpostorBaker.FromPrebaked` INFERS gridN as `atlas.width / AtlasCellPx`, so atlases baked at
the old grid still read correctly — **but `AtlasCellPx` has no such escape hatch and must never move**, or
every existing atlas is silently misread. Three copies of `OctGridN` must agree:
`ScatterImpostorFactory`, `ScatterImpostorBakeTool`, `GeneratedImpostorBakeTool`.

**The importer reference is 0.4 and the shader cutoff is 0.5, and they are SUPPOSED to differ.** The
2026-08-17 note saying `alphaTestReferenceValue` must match `_Cutoff` is wrong. Preserve-coverage holds the
COUNT of texels over its reference, but the card is sampled bilinearly, so two rescaled neighbours
interpolate across that threshold and the blob between them draws wider than the texels it came from — and
that widening grows with mip level, which makes a card GAIN silhouette as it shrinks. Measured as
coverage/height^2 at 192/48/24/12 px: at 0.4 the Taiga Pine runs 0.074 0.078 0.102 0.069 and even a solid
rock climbs 0.398 -> 0.410; at 0.5 the pine holds 0.072 0.072 0.069 0.069 and the rock 0.395 0.396 0.396
0.389. Flat is the point. **Past 0.6 it inverts and erodes instead** — that is the ceiling on any per-prop
cutoff calibration.

**The generated bake tool deletes orphan PNGs now**, but only after a clean full pass: a run that threw
part way would treat everything it had not reached as an orphan.
