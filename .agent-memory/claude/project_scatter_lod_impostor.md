---
name: project-scatter-lod-impostor
description: scatter LOD system — shared ScatterLodBatcher, far-field impostor tier (dynamically lit, day/night-correct), ScatterLodStrip workbench, contact-sheet validator, and hard-won Unity gotchas
metadata:
  type: project
---

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
