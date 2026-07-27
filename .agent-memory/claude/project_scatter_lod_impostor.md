---
name: project-scatter-lod-impostor
description: 2026-07-27 scatter LOD system — shared ScatterLodBatcher, far-field impostor billboard tier, ScatterLodStrip test scene, and two hard-won Unity gotchas
metadata:
  type: project
---

Scatter LOD system on branch `scatter-placement` (2026-07-27).

**Architecture**
- `ScatterLodBatcher` (Assets/Scripts/Planet/Scatter/) is the single shared per-prototype LOD draw:
  distance-banded mesh LODs (LOD i drawn in `[LodEndDistances[i-1], LodEndDistances[i])` by squared
  distance, batched into `RenderMeshInstanced`), then an optional far-field `Impostor` tier. Both the
  planet's `ScatterRenderer` and the test harness are meant to draw through it so LOD/impostor tuning
  in the fast scene is exactly what the planet renders. Unification of `ScatterRenderer` onto the
  batcher is still pending (needs on-planet play-verify).
- Far-field impostor tier (committed f6ef526): `ScatterImpostor.shader` (cylindrical billboard around
  the instance surface-up axis, alpha cutout, distance dither cross-fade) + `ScatterImpostorBaker.cs`
  (bakes LOD0 to an RGBA card) + `ScatterLodBatcher.Impostor` draw band. Cross-fade band matches the
  mesh-LOD dither-out so mesh→impostor has no pop.
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
   silhouette alpha from the clear. Key alpha off luminance (bg is reliably pure black) with an
   ambient floor (`RenderSettings.ambientMode=Flat`, ~0.32) under the bake so shadowed geometry stays
   above threshold. Also: `Mathf.SmoothStep(from,to,t)` is a smoothed lerp between from/to, NOT HLSL
   `smoothstep(edge0,edge1,x)` — for an edge remap write `t=saturate((x-e0)/(e1-e0)); t*t*(3-2t)`.
