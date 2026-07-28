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
  planet's `ScatterRenderer` and the test harness are meant to draw through it so LOD/impostor tuning
  in the fast scene is exactly what the planet renders. Unification of `ScatterRenderer` onto the
  batcher is still pending (needs on-planet play-verify).
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
