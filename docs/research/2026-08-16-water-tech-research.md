# Water Tech Research — 2026-08-16

Survey of external water technology (shaders, buoyancy, wind/object-reactive waves, rivers)
against **our** spherical-planet architecture. Findings and ranked recommendations only —
implementation goes through a design doc.

**Tree surveyed:** branch `harvest-vertical-slice`, working tree dirty on top of `cd4e0f0`.
All file:line citations below are against that tree unless the path starts with `D:\` or
`local-only/`.

**Asset rule in force:** HARVEST-ONLY. No vendor runtime C# ever ships. Everything below is
"read the source, understand the mechanism, write our own." Where a vendor EULA was checked
it is noted; the safe posture throughout is reimplementation from understanding, never
transcription.

Recommendations carry stable IDs **W1–W28** so later design docs can cite them by number.

---

## 0. Sources examined

| Source | Location | Status |
|---|---|---|
| Stylized Water 2 v1.7.0 (Staggart) | `D:\Unity\Explore Assets\Assets\StylizedWater2` | read in full |
| Dynamic Water Physics 2 v2.3.0 (NWH) | `D:\Unity\Explore Assets\Packages\com.nwh.dynamicwaterphysics` | read in full |
| NWH Common v14.0.0 | `D:\Unity\Explore Assets\Packages\com.nwh.common` | read |
| Obi 7.1.1 | `D:\Unity\Explore Assets\Assets\Obi` | read — **Rope only, Fluid not installed** |
| Poseidon v1.8.3 (Pinwheel) | Asset Store cache → `Low Poly Tools Bundle.unitypackage` | extracted + read |
| Thalassophobia Stylized Oceans (Distant Lands) | Asset Store cache | extracted + read |
| **Polyart Dreamscape** (Meadows/Mountains) | `D:\Unity\Explore Assets\Assets\Polyart\PolyartStudio` | read |
| **Weatherade SRS** (NOT_Lonely) | `D:\Unity\Explore Assets\Assets\NOT_Lonely` | read |
| **ECM2** | `D:\Unity\Explore Assets\Assets\ECM2` | read |
| Malbers Animal Controller (swim states) | `D:\Unity\Explore Assets\Assets\Malbers Animations` | read |
| Synty PNB_Core `Water.shadergraph` | `D:\Unity\Explore Assets\Assets\Synty` | read |
| Stylized Grass Shader (bend RT) | `D:\Unity\Explore Assets\Assets\Stylized Grass Shader` | read |
| Broad sweep — 134,131 files indexed across `Assets` + `Packages` | `D:\Unity\Explore Assets` | token + content filtered |
| `FFT-Ocean-main` (gasgiant) | `local-only/` | read |
| `Fluid-Planet-main` (Lague) | `local-only/` | read |
| `GDWaterKart-main` (Godot 4) | `local-only/` | read |
| `Geographical-Adventures-main` (Lague) | `local-only/` | read |
| `Solar-System-Development` | `local-only/` | read |
| Tessendorf, *Simulating Ocean Water* | `local-only/ocean water.pdf` | read |
| Hu et al., *Realistic Real-Time Rendering of Ocean Waves* | `local-only/waves.pdf` | read |
| Yuksel & Keyser, *Fast Real-time Caustics from Height Fields* | `local-only/fastcaustics.pdf` | read |
| *How To Build A Water Shader* (HAW Hamburg, UE5.4, 2025) | `local-only/GeideckMuensterBopp-…pdf` | read |
| *Ray Tracing Gems II* ch. 29, ch. 30 §30.3 | `local-only/Ray Tracing Gems II.pdf` | targeted read |
| 7 × `*_unity_guide.md` | `local-only/` | read — **see the warning below** |

Cross-referenced rather than re-listed: `docs/research/2026-08-11-asset-adoption-map.md` §7
(Poseidon / Oceanis / DWP2 / Stylized Water 2 / FS Swimming, inspected 2026-08-11) and
`docs/research/2026-08-10-external-asset-catalog.md` §15. Where this survey contradicts
those, the contradiction is called out explicitly in §4.

### 0.1 ⚠️ The `local-only/*_unity_guide.md` files are not papers

The seven `*_unity_guide.md` files are **AI-written derivative summaries generated for this
project**, not the source papers they name. Each carries a bespoke "spherical planet
adaptation" section no real paper would contain. Three materially misrepresent their source:

- `ati_real_time_synthesis_rendering_ocean_water_unity_guide.md` — contains none of
  Mitchell's actual FFT / mip-LOD content.
- `rendering_water_caustics_unity_guide.md` — describes scrolling textures; GPU Gems 1 ch. 2
  is actually a refracted-mesh **area-ratio** method. Its own shipped `ComputeCaustics()`
  (`:451-493`) contradicts its own advice by hardcoding `worldPos.xz` and `dot(N, float3(0,1,0))`.
- `foam_splash_rippling_spectrum_ocean_unity_guide.md` — credits "Brian T. Tessendorf" and
  **omits the Jacobian**, the one formula the subject rests on.
- `effective_water_simulation_physical_models_unity_guide.md` — drops GPU Gems 1's steepness
  normalisation `Q_i = Q/(w_i·A_i·N)`, which is the load-bearing part of Gerstner, and
  replaces the chapter's analytic normal with a non-functional `cross(tangentZ, tangentX)`.

**Do not cite these as literature.** Treat them as project notes. The PDFs are genuine and
are the citations of record. The two guides that *are* faithful and substantive are
`looking_through_water_unity_guide.md` (Catlike Coding) and
`realtime_caustics_webgl_unity_guide.md` (Evan Wallace).

---

## 1. Baseline — what we already have

The single most important finding of this survey is that **our water is further along than
every prior document assumes, and further along than most of the packs surveyed.** The
2026-08-11 adoption map records "flat water surface, no waves on the sphere, no buoyancy."
The first two clauses are stale.

**We already ship real spherical wave geometry.** `ComputeOceanSwell`
(`Assets/Graphics/Shaders/Ocean.shader:756-794`) sums three directional sine waves in a
wind-aligned tangent frame and **displaces radially** — `positionWS += planetNormalWS *
swellHeight` (`:816`) — with analytic normals rebuilt from the gradient (`:790-793`). Wind
comes from `_WindDirection` / `_WindStrength01` off the weather sim
(`WeatherManager.cs:170-172`). Amplitude is gated by `openWater01` (pond vs ocean),
`deepWater01` and `shoreFade` (`:218-224`); ponds get 0.10× open-ocean amplitude. Freezing
zeroes swell. A separate fragment layer (`ComputeSurfaceWaves`, `:393`) adds 4 swell + 3
detail octaves plus a Voronoi cell pattern for normals, whitecaps and glint.

**We already ship an analytic spherical water volume.** `WaterVolume.shader` ray-marches
`RaySphere` against `_SeaLevelRadius` for the water path (`:157`), does per-channel
absorption `exp(-float3(4.85,2.05,0.72) * opticalDepth)` (`:421-426`), depth-validated
screen-space refraction (`:469-506`), triplanar chromatic flow-warped Voronoi caustics in
planet-local space, sun- and moon-lit and cloud-shadowed (`:219-378`), and an underwater
path. Storm intensity is deliberately routed to foam only, never to wave geometry — feeding
it to geometry previously stamped cyclone shapes into the ocean.

**We already solved several sphere problems the vendors have not.** Per-pixel planet up,
ray-sphere sea level, a tangent-basis branch at `|dot| > 0.92` to dodge the pole
degeneracy (`Ocean.shader:205-207`), cross-face body classification via a global
direction-keyed vertex graph (`WaterMeshBuilder.cs:530-624`), and the halftone shoreline
foam from Yingst/Alford/Parberry 2011 (`:527-559`).

### 1.1 The actual gaps

| Gap | Evidence |
|---|---|
| **No CPU water query of any kind** | The entire CPU-side water API is one scalar, `IPlanet.LastSeaLevelRadius` (`Assets/Scripts/Core/Interfaces/IPlanet.cs:18-19`). No `GetWaterHeight`, no `TryGetWaterSurface`, no `IsUnderwater`. "Underwater" exists only in HLSL. |
| **No buoyancy, no swimming** | Zero hits for `buoyan\|archimedes\|swim\|wade\|drown\|submerg` across `Assets/`. The character walks *on top of* the ocean via `Mathf.Max(surfaceRadius, _seaLevelRadius)` (`PlanetSurfaceGrounding.cs:38`), self-documented as placeholder (`:15-16`). The character has **no collider** (`PlanetCharacterController.cs:297-300`) — nothing for buoyancy to act on. |
| **No water mesh LOD** | One `MeshFilter`, one mesh, one draw. Measured **481,682 verts / 938,167 tris** (`local-only/debug-screenshots/F10-water.00-Off-20260812-125210-251.txt`), drawn a second time in the prepass. Quads are ~30.8 m at `perFaceResolution=256`. |
| **No reflections at all** | No planar, no SSR, no probe path, no sky-cube specular. Fresnel + procedural sun glitter only. |
| **No rivers, no waterfalls** | Whole-word sweep of `river\|creek\|waterfall\|hydraulic\|drainage\|watershed\|tributary\|flowmap\|flowaccum\|d8flow` over `Assets/Scripts`, `Assets/Graphics`, `Assets/Resources`: **zero hits.** All apparent matches are false positives. |
| **One global sea level** | Every water vertex is `direction * waterRadius`, one radius planet-wide (`WaterMeshBuilder.cs:138,501`). A lake above sea level is unrepresentable by construction. |
| **No water SO / DTO** | Water rides `PlanetSettings.cs:35-39` (4 fields). ~30 look values are compile-time consts in `PlanetWaterSurface.cs:60-81` and `WaterVolumeRenderFeature.cs:28-32`, with no runtime path. |
| **No `water.*` console prefix** | Verified against the full prefix list. Water is driven only via `debug.mode` / `debug.capture-set` / `planet.generate`. |

### 1.2 Dead weight to delete

> **Status 2026-08-17: all three removed** (W3), plus W-BUG-2's constant promotion. Evidence:
> all four csproj build clean; Unity console zero errors; `Ocean.shader`,
> `WaterVolumePrepass.shader`, `WaterVolume.shader` all `supported=True, errors=0`;
> `WaterVolumePrepass` dropped **3 passes → 1**; the loaded assembly confirms
> `ShaderGlobalIds.WaterWakeCount` and the `WaterWakeController` type are gone (checked via
> reflection against the rebuilt DLL, not the source files). Post-change planet generation
> produced **481,682 verts / 938,167 tris** — byte-identical to the 2026-08-12 sidecar, and
> `Water/WaterVolumeLip` is absent as expected. Look unverified (no capture-diff yet); none of
> these three had any visual output to change.

Three subsystems are inert but still cost us. Any water work should take these deletions for free:

- **The wake system is a dead write.** `WaterWakeController` publishes 4 shader globals every
  `LateUpdate`; **no shader reads any of them.** `_WakeFoamIntensity` / `_WakeNormalStrength`
  are declared (`Ocean.shader:23-24,109-110`) and set (`PlanetWaterSurface.cs:264-265`) but
  appear in **no shader math**. `WaterWakeEmitter` exists in no scene or prefab.
  (Audit finding, `docs/audit/2026-07-22-consolidated-code-audit.md:375-378`.)
- **`WaterVolumeLip` is dead.** `PlanetWaterSurface.cs:189` passes `null` for the lip mesh and
  nothing ever creates the GameObject, yet lip vertices and triangles are still computed
  every generation (`WaterMeshBuilder.cs:428-527`) and discarded. Four debug modes, one
  shader pass, the `IsCameraInsideWaterMesh` gate and a debug pass are all unreachable.
- **Water frame timing measures nothing.** `FrameTimingSection.Water` is instrumented in
  exactly one place — `WaterWakeController.cs:35`, the inert wake publish. Every sidecar
  therefore reads `Water CPU: avg=0.00 ms`. **The HUD line is misleading.** Two timed capture
  sets are registered (`DebugRegistry.cs:355,362`) and have never been run.

---

## 2. Three defects found in our own water while surveying

Reported as findings, not fixed. Findings-first rule applies.

### W-BUG-1. The three swell modes' degenerate poles all cluster on one great circle

> **Corrected 2026-08-17.** An earlier revision of this finding claimed all modes degenerate
> together at `±(A×B)`, covering 13.4 % of the ocean, reasoning that `positionTS = (0,0)`
> there. **That reasoning was wrong** — phase being zero is not phase *gradient* being zero,
> and `±(A×B)` is in fact where the waves are at their *sharpest*. The corrected mechanism and
> a much milder severity are below. Recorded rather than overwritten, per the house rule on
> conflicting findings.

`BuildPlanetWaveAxes` (`Ocean.shader:202-211`) builds `axisA = windWS`,
`axisB = normalize(cross(refAxis, axisA))`, and `ComputeOceanSwell` (`:773`) projects to
`positionTS = (dot(L,A), dot(L,B))`. Since
`theta = dot(positionTS, dirTS)·k = k·dot(L, dirTS.x·A + dirTS.y·B)`, **each mode is a plane
wave with its own fixed 3-D direction `D̂ᵢ = dirTS.x·A + dirTS.y·B`.**

On a sphere of radius R, moving by arc length `s` in tangent direction `t̂`:
`dθ = k·dot(t̂, D̂)·ds`, so local spatial frequency is `k·sin(γ)` with `γ = angle(L̂, D̂)`.

**Consequences (corrected):**

- Each mode degenerates — wavelength → ∞, gradient → 0 — at **its own** antipodal pair `±D̂ᵢ`.
  The three pairs are distinct, so the water never goes fully flat anywhere.
- At `±(A×B)`, `D̂` lies *in* the tangent plane, so the gradient is **maximal**. Waves are at
  full frequency there.
- **The real defect is clustering, not coincidence:** all three `D̂ᵢ` lie in `span{A,B}`, so all
  six degenerate points sit on **one wind-aligned great circle**, within about a 90° arc
  (`dirTS` = `(1,0)`, `∝(0.78,0.45)`, `∝(0.40,−0.74)` → poles at 0°, ≈30°, ≈−62° from the wind
  axis). They are not spread over the sphere.
- **Severity is much lower than first stated.** At `L̂ = D̂₁` only mode 1 flattens. Mode 1
  carries `amplitude·0.58` of 1.04 total, so visible wave detail drops to roughly **46 %** in a
  patch around that point — an amplitude/detail dip, **not** a glassy sheet. Modes 2 and 3
  keep working. The ≥2×-stretch radius is still 30° of arc, but it applies to one mode at a
  time.
- Some degeneracy is **unavoidable** — the hairy-ball theorem guarantees any continuous wave
  direction field on S² has singular points. The goal is to spread them, not remove them.

*Discriminating probe:* set wind along +X, fly to the point where the **surface normal equals
the wind direction** (`L̂ = D̂₁`, i.e. planet-local +X — *not* the ±Y poles), view debug mode
`WaveSwell`. Prediction: the dominant long swell stretches out and loses slope there while the
two smaller modes persist; the ±Y poles look normal.

*Fix (still small, but now a visual change so it needs a capture-diff):* give modes 2 and 3
directions with a component along `waveAxisC = cross(axisA, axisB)` — i.e. project them
through `(A,C)` instead of `(A,B)` — so their degenerate poles move off the wind great circle
and spread over the sphere. Phase stays `k·dot(L, D̂)` for a fixed 3-D `D̂`, so the formulation
remains globally single-valued and **seamless**, which is the property worth protecting.

Worth preserving in any fix: the planar-projection form `phase = k·dot(p, D̂)` for a fixed
3-D `D̂` is globally single-valued, so it has **no seam anywhere** — wavefronts are parallels
about `D̂`. Given this project's cube-face-seam history that is a genuinely good property.
The alternative great-circle form (`phase = k·R·θ`) gives uniform wavelength but reintroduces
a pole singularity and needs integer wavenumber quantisation `n = round(2πR/λ)` for
continuity (at our scale the cost is negligible: λ=90 m → n=349 → 90.017 m, 0.02 % error).

### W-BUG-2. `_SwellAmplitude` / `_SwellWavelength` are material-authored only

Every other water constant lives in `PlanetWaterSurface.cs:60-80` and is pushed at
`:251-266`. These two are not — `grep` for them in `Assets/Scripts` returns nothing, so they
run at shader defaults (5.0 m / 90 m, `Ocean.shader:16-17`). **This is a latent CPU/GPU
divergence**: the moment a CPU wave evaluator exists (W1), anyone touching the material
desynchronises it silently. This is precisely the failure mode measured in two vendor
packages (§4.2, §4.5). Promote both to C# before W1 lands.

### W-BUG-3. The volume's sea level is wave-blind by ±5 m

`WaterVolume.shader` builds its entire depth model on the scalar `_SeaLevelRadius`
(`RaySphere` at `:162,183`; `waterDepth = _SeaLevelRadius − receiverRadius` at `:537`),
while `Ocean.shader:812` displaces the visible surface by up to `_SwellAmplitude`. **The
rendered surface and the volume's notion of the surface already disagree by up to ±5 m.**
Related: the prepass vertex shader (`WaterVolumePrepass.shader:54-63`) does not apply the
swell displacement the forward pass applies, so interface depth and drawn surface disagree
by the same amount. Either feed wave height into the volume's depth term or document the
tolerance — but decide before buoyancy depends on it.

Also noted, pre-existing open items re-confirmed: `_SeaLevelRadius` has **two writers**
(`Planet.cs:496` and `AtmosphereController.cs:154`); water generation ignores its
`CancellationToken` (`docs/audit/2026-08-11-startup-planet-generation-audit.md:418`,
still `OPEN`); and 8 skill/doc files still assert the caustics prohibition Bryan lifted on
2026-08-11 (`CLAUDE.md:134-136`).

---

## 3. Verdicts on the surveyed sources

### 3.1 Ranked by usable yield

| Source | Yield | One-line verdict |
|---|---|---|
| **Tessendorf, `ocean water.pdf`** | ★★★★★ | Generation core still state of the art in 2026. Jacobian foam, loop quantisation, `kA<1`, the optics constants. |
| **Poseidon (Pinwheel)** | ★★★★★ | CPU/GPU wave parity done *right*, flat-shading that survives displacement, a sphere-friendly height-only wave, and one of only two real river implementations in the survey. |
| **Polyart Dreamscape** | ★★★★★ | The only complete ocean *system* in the whole library, URP, tessellated, wind-driven, with terrain-heightmap shore damping and a radial lake-wave mode. ⚠️ Its wave-driver C# is missing from the extracted copy. |
| **DWP2 (NWH)** | ★★★★☆ | The buoyancy algorithm, and it ports to a sphere in ~12 lines. |
| **ECM2** | ★★★★☆ | Swimming + immersion-depth buoyancy already written against an arbitrary gravity vector. Ships a Planet Walk example. |
| **Weatherade SRS (NOT_Lonely)** | ★★★★☆ | An entire category nothing else covers: wet surfaces, puddles, rain-ripple normals — and it is triplanar, so the XZ assumption is escapable. |
| **RTG2 §30.3 (Ray-Guided Caustics)** | ★★★★☆ | The one technique in the entire library that is **sphere-safe as published**. Gated on RT hardware. |
| **`FFT-Ocean-main`** | ★★★☆☆ | Take the spectrum (JONSWAP+TMA+Donelan-Banner) and the Jacobian foam. Reject the dispatch structure and the clipmap. |
| **`GDWaterKart-main`** | ★★★☆☆ | Take the ~80-line buoyancy force model. Reject the FFT+readback backend. |
| **`Geographical-Adventures`** | ★★★☆☆ | Spherical jump-flood shore-distance field; the orphan `Waves.hlsl`. No shared ancestry with our ocean. |
| **80 Level water-shader PDF** | ★★★☆☆ | Least sphere-hostile document in the set. Near/far scattering lerp; two-mask foam. |
| **Stylized Water 2** | ★★☆☆☆ | System = skip. ~6 portable shading ideas, one of them excellent. |
| **`Solar-System-Development`** | ★★☆☆☆ | Meshless analytic spherical ocean as a post-process. Elegant; no CPU query possible. |
| **Evan Wallace caustics guide** | ★★☆☆☆ | Area-compression caustics; use the analytic Jacobian, not `ddx/ddy`. |
| **`Fluid-Planet-main`** | ★☆☆☆☆ | Not a water system. Harvest the whitewater classifier and the bilateral blur. |
| **Thalassophobia** | ☆☆☆☆☆ | **Dud** — see below. |
| **Obi, Plawius, `GraphicsLib`, `Environment-Project`, `waves.pdf`, `fastcaustics.pdf`** | — | Nothing usable — see §7. |

### 3.2 Corrections to prior project documents

Three claims in `docs/research/2026-08-11-asset-adoption-map.md` §7 do not survive re-reading
the source. Recording them so the map's readers know:

1. **Poseidon's `PLightAbsorption.cginc` is not Beer's law and is not per-channel.** The whole
   file is 15 lines and computes `lerp(_Color, _DepthColor, saturate(waterDepth/_MaxDepth))` —
   a linear two-colour ramp. The map's "murky water tint = import `PLightAbsorption`" plan
   (`:500`) needs a different source. Better candidates: our own existing per-channel
   `exp(-float3(4.85,2.05,0.72)·τ)` (`WaterVolume.shader:421-426`), or Thalassophobia's
   5-stop `sqrt(eyeDepth)` ramp (`Stylized Fog.shader:516-521`), which is the better
   *stylized* approximation of the two.
2. **Stylized Water 2's RenderGraph failure mode changed between versions.** The map records
   empty `RecordRenderGraph` bodies under a "silence warning spam" comment. v1.7.0 as
   installed instead **hard-errors** on RenderGraph (`StylizedWaterRenderFeature.cs:36-40`).
   The practical verdict is unchanged (Compatibility Mode only), but "silently dead" is now
   "loudly dead."
3. **"No waves on the sphere"** (`:445`) is stale — see §1.

Confirmed unchanged: Poseidon's flat-shading trick, its Bézier wave, its hard-clipped foam,
and the absence of `RecordRenderGraph` anywhere in the Low Poly Tools Bundle (verified
stronger: **zero** files in Poseidon + Polaris + Jupiter + TextureGraph contain it).

### 3.3 Thalassophobia: a dud, recorded so nobody re-checks

`Stylized Water Surface.shader` is **Built-in RP with a `GrabPass`**, `#pragma surface surf
Unlit`, and **no vertex function at all** — no waves, and `LightingUnlit` returns
`half4(0,0,0,alpha)`, so the water is unlit. The base package and the "Import for URP"
overlay are **byte-identical** (`md5 d0d22686…`); every *other* shader in the pack was ported
but the flagship was skipped. Worse, the base is `ASE_SRP_VERSION 170003` (URP 17) while the
URP overlay is `70301` (URP 7.3.1) — **running the URP importer is a downgrade.**

Salvage, all minor: the CRT-baked `min()` caustic (W13), the per-instance wind hook shape in
`Stylized Grass.shader:322`, and a sphere-friendly boid-containment pattern (distance to a
moving anchor Transform, no `Bounds`, no min/max Y) worth remembering for spherical wildlife.
Its `Stylized Fog.shader` is an anti-pattern: density is `sqrt(eyeDepth)` with **no path
length through the volume** — it looks volumetric and is not — and its SubShader-scope
`Cull Front` leaks into the DepthOnly pass, so the fog box writes its own backface depth
into the depth texture it samples.

---

## 4. Recommendations

Ranked by (value to this architecture) ÷ (difficulty). Each names its integration point.

### Tier 0 — the biggest visual gap, added 2026-08-17 after looking at actual frames

#### W29. Fresnel sky reflection ⭐ *the top visual item; this doc originally missed it*

Two captures on 2026-08-17 (`Lake1`, and an open-ocean grazing view at `body01=1.0`,
`depth01=0.912`, 40 m above sea level — `local-only/debug-screenshots/ocean-grazing-20260817.png`)
make it plain: **at grazing angles our water is flat dull teal from the near field all the way
to the horizon.** Fresnel reflectance for water at ~89° incidence is ~100 %; that surface
should be mirroring the sky and clouds.

> **Corrected on implementation, 2026-08-17.** An earlier revision of this entry (and §1.1)
> said we have "no reflection path of any kind." **Wrong** — `Ocean.shader:656-672` has one.
> It was *capped roughly an order of magnitude below physical* and fed a hardcoded sky:
> `skyReflection` was the constant `lerp((0.010,0.018,0.030), (0.38,0.58,0.76), daylight)`, and
> `reflectionBlend = fresnel * lerp(0.08,0.38,daylight) * lerp(0.08,1.0,body01)` — so **38 %
> max on ocean, 3 % max on a lake**, with the distant path able to reach only ~5.5 % sky. A
> fixed blue constant also cannot match a golden horizon, which is exactly what the capture
> showed. The fix is to repair that path, not add one.

This is the single largest reason the water doesn't read as water, and it costs almost
nothing to fix because **we do not need a reflection RT.** We already render the atmosphere
and own `_SunParams` / `_MoonParams` / cloud coverage. An analytic **sky-colour reflection
weighted by a Schlick Fresnel term** (`F0 ≈ 0.02` for water) evaluated against the existing
atmosphere model gets the overwhelming majority of the visual win for a handful of ALU:

```
F = 0.02 + 0.98 * pow(1 - saturate(dot(V, N)), 5);
color = lerp(bodyColor, SampleSkyTowards(reflect(-V, N)), F);
```

Stylized Water 2's `_HorizonColor` / `_HorizonDistance` pair is exactly this and is
**sphere-safe as written** — the term is a pure `pow(VdotN, k)` fresnel, not a horizon-line
test (§4, W4 notes); only its name assumes a flat sea. Poseidon's `PFresnel.cginc` is likewise
already sphere-safe.

Sequencing: **do this before any other look work.** W5 (near/far scattering) and W4 (dual
depth) both change how the *body* colour reads, and both are hard to judge while the surface
is missing its dominant term. Reflection first, then re-judge.

Second-order, same area, once W29 lands: the horizon meets the sky as a **hard dark line**
with no aerial perspective, and water colour does not shift with distance at all. W5 addresses
the second; the first wants the terrain aerial-depth treatment extended to the water surface.

### Tier 1 — high value, low difficulty, no new subsystem

#### W1. CPU wave-height query with exact GPU agreement ⭐ *the keystone*

**This is the highest-leverage item in the report and it is unusually cheap for us.**

Our wave primitive (`Ocean.shader:244-252`) is a pure sine with **no horizontal
displacement**. Every other technique surveyed — Gerstner, FFT, Poseidon — displaces
horizontally, so a CPU height query must invert that displacement by fixed-point iteration:
4 evaluations in `FFT-Ocean` (`WavesGenerator.cs:130-137`), 3 in Oceanis, 15 texel reads
across 3 cascades in `GDWaterKart` (`fft_ocean.gd:299-333`). **We need none of it.** A
byte-exact CPU mirror of `ComputeOceanSwell` is three `sin` + three `cos`.

Every input is already CPU-available: `_PlanetCenter`, `_WindDirection` / `_WindStrength01`,
`_WaveSpeed`, and `_GameTime = Mathf.Repeat(Time.time, 3600)`
(`ShaderGlobalsController.cs:37`, exactly reproducible). Per-vertex `waterData`
(depth01/shore01/body01) is baked into mesh vertex colours by `WaterMeshBuilder` and is
available from the same `faceData` the builder used.

- **Blocked on:** W-BUG-2 (promote `_SwellAmplitude` / `_SwellWavelength` to C# first).
- **Interface:** mirror `IPlanetSurfaceSampler.TryGetSurfaceRadius(Vector3 dir, out float)`
  (`Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs:5`) as
  `IWaterSurfaceSampler.TryGetWaterRadius(dir, out float)`.
- **Integration point:** `PlanetSurfaceGrounding.cs:38` — adding `+ WaveHeight(radial)` makes
  the character bob on the swell in exact agreement with the GPU.
  `IGroundingProvider` composition already supports decoration
  (`PlanetCharacterController.cs:215-218`).
- **Enforce parity structurally, the way Poseidon does** (`PWater.cs:815-817`): one shared
  time global, one shared parameter source, and a debug mode that renders CPU-sampled height
  as colour and differences it against the GPU surface. Stylized Water 2 shipped **four**
  silent divergences to v1.7.0 and `GDWaterKart` has **six** live ones, both because nothing
  checked.
- **Unlocks:** buoyancy, swimming, boats, splash triggers, wave audio, camera submersion.

#### W2. Fix the shared degenerate wave axis
One line. See W-BUG-1. Verify at the ±Y poles with debug mode `WaveSwell`.

#### W3. Delete the three dead subsystems
Wake globals, `WaterVolumeLip`, and the misleading `FrameTimingSection.Water` instrumentation.
See §1.2. Free, and it removes a per-generation cost.

#### W4. Dual depth metric — camera-Z and along-normal, blended at grazing angles
*Source: Stylized Water 2, `ForwardPass.hlsl:58-61`, `:97-107`; `Common.hlsl:86-89`.*

Compute two depths: camera-space Z to the opaque surface, and distance **along the water's
own normal**. Use along-normal for colour / foam / edge-fade (it is the physically meaningful
water column), Z for the accumulation term, and `lerp` to Z at grazing angles where the
along-normal measure degenerates:
```hlsl
half VdotN = 1.0 - saturate(dot(viewDir, waveNormal));
scene.verticalDepth = lerp(scene.verticalDepth, scene.viewDepth, saturate(pow(VdotN, 64)));
```
A single Z-depth makes shallow water look deep at glancing angles and produces the classic
hard shoreline line; a single along-normal depth explodes at the horizon. Three instructions
fixes both. **`DepthDistance(wPos, viewPos, normal)` takes the normal as a parameter, so it
is sphere-safe unmodified** — feed it the radial normal.

**This targets our documented "shoreline thin line" and "washed transparent sheet" scar
tissue directly** (pp-failure-archaeology). Integration: `WaterVolume.shader` depth model,
alongside the existing `waterDepth` at `:537`.

#### W5. Near/far scattering-colour lerp
*Source: 80 Level PDF (UE Single Layer Water practice).*

`lerp(ScatteringColorNear, ScatteringColorFar, cameraDistance)` — near blue, far warm — plus
the same on `RoughnessNear/Far`. The stated benefit is exactly our failure mode: **it
separates water colour from sky colour.** Cheap, and it attacks "washed transparent sheet"
from the presentation side while W4 attacks it from the depth side.

#### W6. Jacobian foam, in the eigenvalue form
*Source: Tessendorf Eqs. 45–49; implemented at `FFT-Ocean/WavesTexturesMerger.compute:25-27`.*

`J = JxxJyy − Jxy²`; `J < 0` ⟺ folded ⟺ breaking. Our foam is currently crest-height plus
shore-depth (`Ocean.shader:530-552`); the Jacobian is the physically-motivated source and for
our sine-only waves it is nearly free — the `gradientTS` terms are already computed.

**Take the form nobody in the library uses:** the minimum eigenvalue `J−` is the earliest
onset signal, and its eigenvector `ê−` gives the **fold direction** — free foam-streak
orientation, not just a mask.

#### W7. Foam persistence
`foam *= exp(-Δt · decay)` (framerate-independent; `GDWaterKart/accumulate_foam.glsl:121-126`
uses 0.995/frame). Turns blinking foam into patches and streaks.

⚠️ **Blocked on a storage surface — see W28**, which is the same missing piece for interactive
ripples and wakes. No *water* pack surveyed solves it; the reference implementation turned up
in a grass asset.

#### W8. Hard-clipped, noise-eroded shoreline foam band
*Source: Poseidon `PFoam.cginc:41-57`.*

Two counter-scrolling noise samples multiplied, compared against a depth ramp, then **hard
clipped — no smoothstep**, which is what produces the crisp stylized edge:
```hlsl
half noise     = noiseBase * noiseFade;
half depthFade = saturate(InverseLerpUnclamped(0, _ShorelineFoamStrength * _FoamDistance * (1 + noise), waterDepth));
half noiseClip = noise >= depthFade;
```
~15 lines. Only the `worldPos.xz` noise UV needs a cube-face/spherical domain — the same swap
our cloud cube-face UVs already do. Complements, and may partly supersede, our halftone band.

#### W9. Refraction offset rejection + re-sample
*Source: Stylized Water 2 `ForwardPass.hlsl:64-83`; Catlike guide `:357-371`.*

After computing a refraction offset, sample depth **at the offset UV**, reject the offset
where that pixel is in front of the water surface, then **re-sample depth and world position
with the corrected offset** and recompose density from those. Without the rejection, objects
in front of the water smear into the refracted image — the most common stylized-water
artifact. Without the re-sample, density is computed at the un-refracted position and
disagrees with the colour shown.

Two corrections the Catlike guide loses: apply the offset **before** the perspective divide
(`uv = (screenPos.xy + offset)/screenPos.w`), or distortion is constant in screen space; and
compensate aspect on `uvOffset.y`.

**Check our existing refraction first** — `ComputeBottomDistortion`
(`WaterVolume.shader:469-506`) already does a depth-validity and path re-check, so part of
this may already be in place. Verify before implementing.

#### W10. Vertex-colour channel contract, each behind its own enable multiplier
*Source: Stylized Water 2 `Common.hlsl:81-84`, `Input.hlsl:116-118`.*

We already use all four water vertex-colour channels (`R=depth01, G=shore01, B=bodyFactor,
A=temperature01`, `WaterMeshBuilder.cs:503-507`) but decode them independently in three
places — `Ocean.shader`, `WaterVolumePrepass.shader`, and the builder — so changing one means
changing three. Adopt SW2's discipline: **one shared decode helper, each channel gated by its
own enable multiplier**, so a mesh authored under one configuration cannot misbehave under
another. This is the same "a shared function is the correct fix for keep-these-in-sync
comments" resolution as the `WeatherCloudGloom` unification.

Note the terrain shader has two **declared but never written** surface-state channels —
`B = snow depth`, `A = wetness` (`PlanetVertexColor.shader:185-191`). `A = wetness` is the
natural home for a river/damp-bank mask (W20).

#### W11. Front/back material swap at the surface crossing
*Source: Poseidon `PWater.cs:828-832`; `WaterBackFaceURP.shader` (`Cull Front`).*

A clean above/below-water surface split — our known weak spot, and the origin of the
`IsCameraInsideWaterMesh` gate and the retired global lip pass. Only the test
`camPos.y < waterHeight` needs to become a radial compare. Pairs with W12.

#### W12. Poseidon's underwater post-effect structure
*Source: `PUnderwaterCommon.cginc:44-143`.*

Complete and well-factored: depth→worldpos, ray-surface intersect, depth-tinted fog, `1-x⁸`
screen-edge fade. **Two substitutions make it radial** — `float3(0,1,0)` plane-intersect
(`:57-59`) → ray-sphere, and `_WaterLevel - camPos.y` (`:81,115,121,124`) → radial distance.
Everything downstream is already position-based. Compare against our existing underwater path
(`WaterVolume.shader:124-137`) before adopting; this may be a refinement rather than a
replacement.

#### W13. Caustics: `min()` of two counter-scrolling samples + scene-normal mask
*Sources: Stylized Water 2 `Caustics.hlsl:33-50`; Thalassophobia `Stylized Caustics.shader:181`.*

Three separable ideas:
- **`min()` of two counter-scrolling, differently-scaled taps** produces sharp crossing
  filaments where `+` produces mush. An HQ tier adds a second `min` pair.
- **Project the caustics UV through a matrix built from the main light's rotation**, set once
  per frame from C#, so caustics shear with sun angle instead of being a fixed decal. The
  light-rotation matrix is already sphere-agnostic.
- **Mask by the reconstructed scene normal's NdotL** plus the shadowmask — this is what stops
  caustics painting onto vertical rock faces.

Thalassophobia bakes the `min()` pattern into a **CustomRenderTexture** once rather than
evaluating per pixel; projecting the result as a **light cookie** is one of the few caustic
approaches that is sphere-agnostic by construction, since it inherits the light transform.

⚠️ Touches caustics. The don't-touch rule was lifted 2026-08-11 but "change deliberately and
verify the water still reads correctly" stands. Gated work under pp-change-control.

#### W14. Flat shading via baked neighbour vertices, normal recomputed after displacement
*Source: Poseidon `PCustomMeshBaker.cs:56-58` + `PCommon.cginc:159-165` +
`UniversalRP_Forward.cginc:53-69`.*

Bake the other two triangle vertices into UV0 and vertex colour, displace **all three** by
the wave, then reconstruct the face normal from the displaced triple:
```hlsl
ApplyWaveHQ(v.positionOS, v.texcoord, v.color, o.crestMask);
CalculateNormal(v.positionOS, v.texcoord, v.color, normalOS);   // cross(v1-v0, v2-v0)
float3 centerVertex = (v.positionOS.xyz + v.texcoord.xyz + v.color.xyz) / 3.0;
```
Per-facet flat lighting on a fully vertex-shaded, SRP-batchable mesh, **no geometry shader**,
and the normal stays correct *after* displacement. ~80 lines. Fully sphere-safe — a per-
triangle cross product has no world axis.

⚠️ **Skip their `RemapVertex` (`:77-85`)**, which normalises each vertex per-axis into
`[-0.5,0.5]` of the AABB. For a full cube-sphere that is a uniform rescale; for a **cube-
sphere chunk** the bounds are non-cubic and it is a non-uniform distortion. It also triples
vertex count — a real concern at our 938k tris (see W16).

⚠️ Conflicts with our current shading; this is a look decision for Bryan, not a free win.

#### W15. Bézier crest wave — the sphere-friendly wave shape
*Source: Poseidon `PWave.cginc:17-32`, `:52-55`.*

A piecewise cubic Bézier height profile whose middle control point slides
`lerp(0.5, 0.95, steepness)`, so crests sharpen **without Gerstner self-intersection**.

**The key insight, which the prior adoption map missed: only `p.y` is ever used.** The
horizontal `p.x` that makes Gerstner pinch is discarded. So this is a *pure height-profile*
wave with no lateral vertex bunching — which is why it is trivially sphere-portable (displace
along the radial normal, no tangent-frame Gerstner math, no mesh stretching) **and** why it
stays cheap on the CPU, preserving W1's no-inversion property.

Port work: phase `dot(worldPos.xz, dir)` (`:49`) → our existing spherical projection;
offset `float4(0, h, 0, 0)` (`:55`) → `radialNormal * h`.

⚠️ Note a real bug in the source not to inherit: `ApplyWaveHQ` computes the offset in **world**
space and adds it to the **object-space** vertex with no `unity_WorldToObject` round-trip
(`PWave.cginc:70-73`), so wave height scales with transform Y-scale. `ApplyMeshNoise` does the
round-trip correctly — the two paths are inconsistent.

### Tier 2 — high value, medium difficulty

#### W16. Water mesh LOD *(prerequisite for everything high-frequency — but NOT a perf win)*

> **Corrected 2026-08-17 by measurement.** This entry originally called mesh LOD "the largest
> single frame-cost lever available." **That was wrong, and the first-ever GPU water
> measurement (§8.1) disproves it:** the 938k-triangle surface draw costs about **0.05 ms
> GPU**. Essentially nothing. The cost is the fullscreen volume composite, ~2.6 ms.

938,167 tris in one draw, no LOD, drawn twice per frame. Our quads are ~30.8 m; `FFT-Ocean`'s
innermost clipmap cell is 0.124 m — a **250× resolution gap**. Any high-frequency
displacement (W22) would simply alias into our mesh, so LOD (or at least adaptive density)
remains a genuine **prerequisite for W22** — but justify it as *enabling geometry detail*,
never as a frame-time saving. Their clipmap is not the answer (XZ snapping with a
`viewer.position.y` LOD metric — flat to the bone); our own chunked cube-sphere LOD is the
model to follow.

**If the goal is frame time, optimise the volume composite instead** — see §8.1.

#### W17. Buoyancy — the design
Two sources combine cleanly. **The algorithm from DWP2, the force-application pattern from
`GDWaterKart`, the parity discipline from Poseidon, and Burst from us.**

**Algorithm (DWP2, `WaterObject.cs`).** Per-triangle submerged-area integration, Kerner-style:
classify each proxy-mesh triangle by three signed distances, dispatch 3-under / 2-under /
1-under, linearly interpolate the two waterline points, integrate hydrostatic pressure over
the submerged sub-triangle(s):
```
F = −ρ · g · h_c · (n̂·û) · A · û      per submerged face
```
**The clipping code is already gravity-agnostic** — it operates purely on an abstract signed
distance `d` and never touches `.y` (`:1277-1294`, `:1359-1372`). Redefine
`d = |P − planetCentre| − oceanRadius(P)` and it ports **unmodified**. Chord-vs-arc error over
a ≤2 m proxy triangle on a 5000 m planet is under 1 mm.

**The sphere-hostile surface is three lines**, all inside the algorithm rather than the
interface: `d0 = P0.y - wh` (`:852`), `_worldUpVector` cached once in `Awake` (`:430`) — must
become per-triangle or per-object-CoM `normalize(centre − planetCentre)` — and
`gravity = _gravity.y` (`:1138`) → `gravityMagnitudeAt(centre)`.

**Provider seam.** DWP2's `WaterDataProvider` (`:95-182`) has the right *shape* — batched
array-in/array-out, capability-flagged (`SupportsWaterHeightQueries()` etc.), single-point
queries built on top of the batch path rather than the reverse. **But its contract is a
Y-heightfield** (`float[] waterHeights`; `PointInWater` is `GetWaterHeight(...) > worldPoint.y`)
and **a spherical ocean cannot be expressed in it** — antipodal sea-level points would need
`+R` and `−R`. Our version must return **signed depth**, not height. With that one signature
change, `CalcTri` becomes `d0 = signedDepth[v0]` and the entire machinery is unchanged.

**Force application (`GDWaterKart`, ~80 lines).** A sensor/body split: markers cache a height
query per physics tick; the body sums per-sensor `buoyancy = pow(|depth|, power)` and applies
each force **at the sensor's offset** (`apply_force(F, sensorPos − bodyPos)`) so torque and
rocking come for free, plus multiplicative drag while submerged. Maps directly onto radial
gravity: `-gravity` → `-planetUp·g`, every `.y` compare → `dot(pos − planetCentre, up)`.
Sample counts in practice: buoy = 5 sensors, bomb = 1, kart = 1.

**Our deliberate improvements over both sources:**
- **Burst `IJobParallelFor` over triangles.** DWP2 ships **zero Burst and zero jobs** in its
  solver despite declaring the Burst dependency in `package.json`. The work is embarrassingly
  parallel — per-triangle independent, sum-reduce at the end. This is a straight 10–50× and
  it is where our version stops being a port.
- **One orchestrator, one batched query.** DWP2 has no central manager: N floaters ⇒ N
  `FixedUpdate` callbacks and N provider round-trips per step.
- **Fix the physics.** DWP2's drag terms are dimensionally **kg/s** — one power of velocity
  short of a Newton (`:1183`, `:1192`). Use `½ρC_dA(n̂·v)|n̂·v|` for form drag and keep the
  genuinely clever `(1 − |n̂·v̂|)` tangential weighting for skin drag.

**Traps in the source not to inherit:** `WaterNormals[i0]` / `WaterFlows[i0]` indexed with
**local corner indices 0/1/2** against arrays sized `vertexCount` (`:1086-1088`, `:1113-1115`
— latent only because both features default off); the provider queried with **last step's**
world vertices (`:553` vs `:770-773`); barycentric weights computed at the centroid so always
exactly ⅓ (`:1050-1082`, ~40 dead flops); `submergedVolume` accumulating displaced **mass in
kg**, not m³ (`:1142`).

**Blocked on:** W1 (the height query), and a collider on the character
(`PlanetCharacterController.cs:297-300` — currently none).

**Free steal alongside it:** **mass from mesh volume × material density**
(`MassFromVolume.cs:116-137` + a density `ScriptableObject`, wood 600 / ice 920 / aluminium
2700 / steel 7850). Volume via the divergence theorem over the *simulation* mesh, so
waterline behaviour is self-consistent, and authoring density instead of mass means every
object of a material floats at the same draft with no magic numbers. Fits our SO-authoring →
DTO-runtime rule cleanly.

#### W18. Spherical jump-flood shore-distance field
*Source: `Geographical-Adventures/JumpFloodCompute.compute:48` — jump flood whose metric is
`distanceBetweenPointsOnUnitSphere` with longitude wraparound.*

Would drive shoreline foam **without a depth texture** — a genuine alternative given how much
of our failure archaeology is depth-derived shoreline artifacts. Feeds Lague's radiating-band
foam (`sin(dstFromShore·freq − time·speed + noise)`, `Ocean.shader:141-142`), broken by a
triplanar noise whose threshold rises with distance.

We already compute a shore-distance BFS on the water mesh
(`WaterMeshBuilder.ComputeShoreDistance:807`) — this would be the raster equivalent, usable
by the terrain shader too.

#### W19. JONSWAP + Donelan-Banner spectrum as the *parameterisation* for our sine amplitudes
*Source: `FFT-Ocean/InitialSpectrum.compute:34-153`.*

Even without adopting FFT: derive per-mode amplitude and wavelength from `windSpeed` and
`fetch` instead of hand-tuned constants. Includes finite-depth dispersion
`ω = sqrt(g·k·tanh(min(kD,20)))`, TMA shallow-water correction, and the Donelan-Banner
directional spread. **Makes the weather sim physically drive the sea state** — we already
have wind speed and direction on the CPU and GPU.

Strictly better than the Phillips spectrum every tutorial uses; both `FFT-Ocean` and
`GDWaterKart` already moved off Phillips.

Two Tessendorf details worth taking with it: **loop quantisation** `ω̄(k)=⌊ω(k)/ω₀⌋·ω₀` for a
clean repeat period, and the hard steepness criterion **`kA < 1`** (above it the surface
self-intersects).

#### W25. Wind-driven Gerstner with terrain-heightmap shore damping, and a radial lake mode
*Source: Polyart Dreamscape `SharedResources/Shaders/Water/GerstnerWave.hlsl` +
`Water/M_Ocean_Amp.shader` (5,404 lines, URP, `RenderPipeline=UniversalPipeline` at `:82`).*

The most complete ocean system in the library, and three of its ideas are directly ours:

- **It outputs a scalar vertical offset only — no horizontal Gerstner pinch** (despite the
  file name), plus analytic normals from the derivative. **The same sphere-friendly property
  as Poseidon's Bézier wave (W15), independently arrived at**, and it preserves W1's
  no-inversion CPU query. Two independent stylized-water authors converging on
  height-only displacement is a strong signal for our radial-displacement approach.
- **Physical deep-water dispersion**: `waveSpeed = sqrt(TWOPI/wavelength * 9.8) * time`,
  `MAX_WAVES 32` in a `float4 _WaveData[]` (wavelength, amplitude, offset). Wind mode rotates
  the world gradient by `atan2(_WindDirection)` with a per-wave ±10° deviation from
  `Hash01(i)` — the deviation is what stops the sea reading as a corduroy sheet.
- **`_ShoreDistanceWPODampening` damps wave displacement near shore using a terrain
  heightmap** (`_TerrainHeightMap` + `_TerrainPosAndSize`). This is *exactly* what we would do
  with our planet height field, and it is a better formulation than our current
  vertex-colour `shoreFade` because it does not depend on the water mesh's own tessellation.
- **`_FlowPivot` radial mode** — waves radiate from a point instead of a direction. That is
  the correct wave model for a **lake**, and we currently give lakes the same directional
  swell as the ocean at 0.10× amplitude (`Ocean.shader:779`). Also worth noting: a radial
  field has **one** degenerate point instead of W-BUG-1's two, and it can be placed at the
  lake centre where nobody looks.

⚠️ **Verified gap:** `Ocean Tool.prefab:47` references a MonoBehaviour guid
(`15df23dbf27128a4ab1c1c6caa5deb5e`) that **resolves to nothing** in the extracted library —
the C# that fills `_WaveData[]` / `_WaveCount` is missing. The custom inspector confirms it
should exist. We would write that driver ourselves regardless (harvest-only), but do not
expect to read theirs.

#### W26. Immersion-depth swimming and buoyancy, already gravity-vector agnostic
*Source: ECM2 `Source/Characters/Character.cs:2830-2900`; Malbers
`States/Swim.cs:155,161,193,353`.*

**ECM2 is the best swim reference found and it needs almost no porting**, because every axis
term is already expressed against `-GetGravityDirection()` rather than `Vector3.up`:
```csharp
// CalcImmersionDepth(), :2830
Vector3 worldUp = -GetGravityDirection();
rayOrigin = GetPosition() + worldUp * height;
depth = 1 - InverseLerp(0, height, hit.distance);
// SwimmingMovementMode(), :2861
float actualBuoyancy = buoyancy * depth;      // buoyancy 1 = neutral, 0 = none
newVelocity = Vector3.ProjectOnPlane(newVelocity, worldUp) + worldUp * verticalSpeed;
```
It ships an `Examples/Planet Walk/` scene. Malbers' `Swim` state is independently the same
shape — water level found by raycast **along the gravity vector** (`:193`),
`BounceUpTarget = -Gravity * bounce` (`:161`).

**Its single planar assumption is that the water volume is a `BoxCollider`**
(`Source/Components/PhysicsVolume.cs`) — swap that for a radius test against sea level and
it is sphere-native.

This is the **cheap tier of buoyancy**, complementary to W17: immersion-depth scaling covers
a swimming character and floating props; W17's per-triangle integration is what boats and
rafts need. Ship this first.

⚠️ Do **not** use Fantacode's FS Swimming System as a reference — it hardcodes `Vector3.up`
and `Physics.gravity.y` (`Swimming.cs:190,329,376,849`) and is not sphere-portable.

#### W27. Wet surfaces, puddles and rain ripples
*Source: NOT_Lonely Weatherade SRS `Shaders/Includes/SRS_RainCoverage.hlsl`.*

**An entire category no other surveyed pack covers, and it connects water to the weather sim
we already have.** One function does all of it (`:37`):

- **Rain ripples** (`:142-190`) — up to 15 instances of a `Texture2DArray` **flipbook**
  (`_RipplesTex`, `_RipplesFPS`, `_RipplesFramesCount`), each with a random rotation matrix,
  scale and offset, optional stochastic tiling, composited via `BlendNormalRNM`.
- **Puddles** (`:200-204`) — a mask sampled at `positionWS.xz * tiling`, `smoothstep`ed,
  **gated by slope and approximate surface height so water pools in hollows**.
- **Wetness** (`:216-222`) — `albedo * _WetColor` plus smoothness pushed to 0.9–0.99.
- **Distance LOD** (`:228-237`) — fades to a pre-baked albedo+smoothness texture past a range.

Crucially it ships `SRS_Triplanar.hlsl`, **so the `positionWS.xz` assumption is escapable** —
this is the one wet-surface implementation that is not structurally flat.

Our hooks already exist: rain rate and storm intensity come off the weather grid, and the
terrain shader has **two declared-but-never-written surface-state channels**,
`B = snow depth` and `A = wetness` (`PlanetVertexColor.shader:185-191`). Note also the
triplanar-threshold caveat in §6 — blend the wetness *decision*, not three thresholds.

BOXOPHOBIC TVE has the same idea at the globals level (`TVE_WetnessValue` /
`TVE_WetnessContrast` / `TVE_WetnessNormalValue`, `TVEGlobalControl.cs:207,221-222`) — the
*pattern* for a wetness shader-global trio, nothing more.

#### W28. Interaction render-texture — the answer to W7's storage problem
*Source: Staggart Stylized Grass Shader `Runtime/StylizedGrassRenderer.cs:21,168`;
`Shaders/Bending/GrassBendMesh.shader`; `Prefabs/Benders/*`.*

W7 (foam persistence), interactive ripples, and wakes all need the same missing thing: **a
place to accumulate world-space state near the viewer.** Every water pack surveyed either
lacks it or hides it behind a paid extension. The reference implementation is in a *grass*
asset by the same author as Stylized Water 2: a camera-following `RenderTexture` written by
bender prefabs (sphere / trail / particle / particle-trail) and published as a global
(`_GrassOffsetVectors`).

Stylized Water 2's dynamic-effects hooks describe exactly the packing to use: **one RT with
R = displacement, G = foam, B/A = alpha/normal, sampled by world position**
(`ForwardPass.hlsl:361-371`, `Vertex.hlsl:98-110`) — with the useful note that dynamic
effects denote *geometry curvature*, so they belong in the **wave** normal, not the tangent
normal.

**Solve this once and four features unlock**: foam persistence (W7), object-reactive ripples,
the currently-dead wake system (§1.2), and boat wakes. The sphere-specific decision is
per-cube-face RTs versus one camera-following tangent-plane RT with reprojection; given the
effect radius is tens of metres on a 5000 m planet, a single tangent-plane patch is almost
certainly correct — and that is the same call TVE's `TVEGlobalVolume` makes, and the same one
our path-wear and scorch masks already work around.

For the *simulation* inside that buffer, two references: `GDWaterKart`'s GPU wave equation
(`new = 2·cur − prev + c²∇²`, damped — `water_waves_compute.glsl:44-68`), and Tessendorf's
**iWave** (`ocean water.pdf` §5), whose standout property is that **obstacles are handled by
multiplying `h` by a 0..1 mask** and that alone produces correct reflection and diffraction.

#### W20. Rivers — heuristic polylines grown uphill from the coast
Full analysis in §5. Ranked first among three strategies.

#### W21. Wallace area-compression caustics
*Source: `realtime_caustics_webgl_unity_guide.md` (faithful).*

Caustic brightness is a light-**density** change: `brightness = originalArea / refractedArea`,
both from screen-space derivatives, which is what removes the need for a geometry shader.
Would replace our procedural Voronoi triplanar (`WaterVolume.shader:219-346`).

Two refinements over the guide: compute the **Jacobian analytically from the wave
derivatives** instead of `ddx/ddy` (kills both the quad-granularity noise and the
`newArea→0` white blowout at once — and we already have `gradientTS`), and feed it
**low-frequency normals only** (`normalize(largeWaveN + 0.5·mediumWaveN)`); detail normals
make it strobe.

Integration point exists: `WaterVolumePrepass` already renders the water surface into
`_WaterVolumeData` before transparents (`WaterVolumeRenderFeature.cs:273,340`).
⚠️ Caustics — gated work.

Related open bug this could address: **caustics don't reach scatter** — corals sit unlit while
the seabed ripples, because caustics are a receiver-depth effect in the volume composite and
scatter is not a receiver (`docs/design/2026-08-12-ocean-scatter.md:88`). Bryan named
underwater effects as the thing to fix first (`:100-101`).

### Tier 3 — high value, high difficulty

#### W22. Viewer-anchored tangent-plane FFT patch, cross-faded into the global sine swell
Full Tessendorf unmodified in a local frame — correct near the viewer, phase-shears slowly at
range where slope and specular dominate anyway. **Blocked on W16** (250× resolution gap).

⚠️ **Before porting `FFT-Ocean`, fix its dispatch structure.** Measured: `IFFT2D` = 16 butterfly
+ 1 permute = 17 dispatches; per cascade 1 + 4×17 + 1 = 70; three cascades = **210
`ComputeShader.Dispatch` calls per frame**, plus 6 `GenerateMips`. At the 50–100 µs launch
overhead our own `CLAUDE.md` documents, that is **10.5–21 ms of pure launch overhead** — the
entire frame budget before any work happens. This is why the author labels it a prototype.
Collapse the butterfly into a single-pass shared-memory FFT first.

#### W23. Ray-Guided Water Caustics (RTG2 §30.3)
**The one technique in this entire survey that is sphere-safe as published.** It assumes no
plane, no constant sea level, no XZ domain, no `+Y` — it needs only rasterizable water
geometry, per-texel world position + normal, and a BVH, all of which a cube-sphere ocean
provides unmodified.

Rasterize the water surface from the light's view into a caustics map (world position +
normal), trace one reflected/refracted ray per texel, splat, composite. Light→water is
rasterized, not traced — that is the efficiency. **Photon Difference Scattering** sizes each
hit sprite from finite differences between *neighbouring rays*, so it never needs the
receiver's geometry — it drops in without touching every material, and produces continuous
caustic networks **with no denoising**. For ocean scale, **Cascaded Caustics Maps** (CSM
analogue, directional light only) + PDS; their 4-cascade example uses **52 threads instead of
1024**.

Measured: pool 1024² @1080p — PDS 1.7 ms / PCM 1.02 ms on RTX 2060, 0.55/0.38 ms on RTX 3090.
Seaside town, 4-cascade CCM over 240×240 m of sea: **4.89 ms on RTX 2060, 1.6 ms on RTX 3090.**

Gated on RT hardware and a BVH we do not currently build. **Highest ceiling in the report.**

#### W24. Waterfalls and whitewater
See §5.3. Depends on W20.

---

## 5. Rivers, rapids and waterfalls

The bonus ask, and the answer is more encouraging than expected.

### 5.1 Why this is more viable here than in most codebases

1. **The height function is pure, cheap, thread-safe and evaluable at arbitrary resolution.**
   `ShapeGenerator.SampleElevation(Vector3 pointOnUnitSphere)` (`ShapeGenerator.cs:57`) is
   explicitly documented as side-effect-free and off-thread safe (`:53-56`), reads only data
   frozen at `Initialize()`, and never touches a chunk, mesh or streaming state. Wrapped as
   `AnalyticGroundSampler.RadiusAt` (`:60`), already used off-thread by `LakeMask`
   (`Planet.cs:397-400`), with a Burst-jobbable mirror via `BuildNoiseFilterData`.
2. **The cube-face seam is already solved twice in our tree.**
   `CubeFaceTopology.TryMirrorUv` (`CubeFaceTopology.cs:110-145`, hand-derived 6×4 adjacency
   table with `EdgeParamReversed` handling), and `GrassSurfaceAtlasBuilder.RemapOutsideFaceUv`
   (`:298-314`) — which is a working D8-style "sample one texel outside this face → land on
   the neighbour" helper. Plus a genuinely seam-merged global BFS in
   `WaterMeshBuilder.BuildGlobalAdjacency` (`:646`).
3. **The planet is small.** R = 5000 → 314 km² total. A global ~8 m raster is 6.1 M cells;
   priority-flood + D8 + accumulation over that is a **sub-second background-thread job**
   against a ~40 s generation budget.

### 5.2 The two hard blockers

**Blocker 1 — we cannot carve.** Nothing in the project modifies terrain height at runtime or
after generation. Five-point proof: `ISurfacePathBrushService` exposes no elevation API
(`:16-43`); every paint bottoms out writing `byte[]`/`Color32[]` pixels
(`ChunkedSurfaceProvider.cs:1232`); `CpuVertices`/`CpuElevations` are written **only** at
generation (`ChunkSurfaceGenerator.cs:211-215`); the terrain shader does **zero** vertex
displacement (`PlanetVertexColor.shader:247-261`); and `WorldActionType.TerrainDeform`
(`IWorldAction.cs:18`) carries a `// FUTURE:` note and **zero implementations**.

Worse, height has **four representations that must agree**
(`docs/design/2026-08-09-surface-unification.md:29-40`): the analytic field, the
`AnalyticGroundSampler` used by scatter and LakeMask, the Burst scatter mirror
`ScatterGatherBurst.SampleRadius`, and the rendered verts from `PlanetChunkMeshJob`. A carve
must be a **Burst-blittable term inside the shared evaluator** — sample a river-distance
raster passed as a `NativeArray`, not look up a `List<RiverSpline>` — or scatter floats over
the channel and grass grows in mid-air.

**Blocker 2 — one global sea level.** Every water vertex is `direction * waterRadius`
(`WaterMeshBuilder.cs:138,501`); `OceanLevel` is a single `[Range(-0.05,0.05)] float`. "Lake"
is purely a shading factor — `LakeMask` only forces `bodyFactor = 0` (`:614-621`). A river is
water *above* sea level by definition, so **it cannot borrow the ocean mesh.**

**Per-body water level is the single change that unlocks mountain lakes, rivers and
waterfalls simultaneously** — and it is also the highest-blast-radius change, since
`WaterMeshBuilder`, `LakeMask`, both biome resolvers and the `_SeaLevelRadius` global all
assume the scalar.

### 5.3 Strategy ranking

| | Strategy | Payoff | Effort | Risk |
|---|---|---|---|---|
| 🥇 | **Heuristic polylines grown uphill from coasts + lakes** | high | low | low |
| 🥈 | Full flow-accumulation hydrology (priority-flood → D8 → accumulation → Strahler) | very high | high | medium |
| 🥉 | Hydraulic-erosion-implied channels | medium | very high | high |

**Why #1 wins.** Seed at coastal cells and lake boundaries, grow **uphill** with occasional
branching, then reverse each path. Because an uphill walk cannot get trapped in a pit, the
reversed path **descends to the ocean by construction** — you get the one property that
actually matters visually ("rivers always reach the sea, never stop in a field") for **zero
depression-filling machinery**. And the walk is over `Vector3` unit directions, not raster
indices, so **there is no seam to solve at all.** Growing from the sea upward is also the
Derzapf 2011 insight.

Cost: drainage areas are not physically correct and branching is heuristic. At 5 km planet
scale with rivers a few km long, that is not observable.

**Why #2 is second, not first.** Priority-flood on a *noise* heightfield fills an enormous
number of tiny pits, and the network is only as good as the accumulation threshold — expect
several tuning rounds before it reads as rivers rather than as a drainage-analysis figure. It
does eventually subsume `LakeMask` entirely and yields per-body water levels, which is the
real prize. Four seam problems must be handled explicitly: topology (solved), 3-face corner
degeneracy at 8 points (must not throw), **metric distortion** (D8 divides Δh by Δdistance —
use `CoordinateConverter.ArcDistance` (`:135-139`) or flow biases toward face corners), and
**UV-convention discipline** (three different entry points exist —
`CoordinateConverter.UnitSphereToCubeFace:37`, `UnitSphereToCubeFaceUvExact:86`, and
`FaceSpaceCellRangeBuilder.DirectionToFaceUv` used by LakeMask; mixing them teleports a river
at a seam).

**Why #3 is rejected for rivers.** The only in-tree implementation
(`local-only/Clouds-master/Assets/Scripts/Terrain/Scripts/Erosion.cs`, Lague's Beyer-2015
droplet model) is flat-heightmap-only, its GPU port has a designed-in race, and it ships two
real bugs (a `sizeof(int)` buffer for floats; a brush row-stride computed with `mapSize` while
the shader indexes at `mapSizeWithBorder`). Decisively: **it emits no river data at all**,
only mutated heights — there is no accumulation buffer anywhere in `local-only`. And we have
no mutable heightfield to erode. Park it as a **look pass** (bank detail inside river
corridors once a network exists), not as a river source.

### 5.4 Smallest visible slice

One new file, one new shader, **no carve, no biome change, no water-mesh change**:

1. `RiverNetwork.Build(ISurfaceGroundSampler, baseRadius, oceanThreshold, seed)` — mirror
   `LakeMask.Build`'s shape and slot it at `Planet.cs:404`, inside the existing
   `Awaitable.BackgroundThreadAsync` window, with its own `phaseTimer`. Seed ~40 coastal
   directions; step uphill at ~10 m arc steps probing 8 tangent directions using the
   `Vector3.Cross` frame from `AnalyticGroundSampler.SampleNormalAt` (`:47-49`); reverse.
   **Zero raster, zero seam handling.**
2. **Ribbon mesh**, same background thread: per station `p = dir·(RadiusAt(dir) + 0.15)`,
   `side = cross(dir, tangent)`, emit `p ± side·halfWidth`, `UV = (across,
   arcLength/8m)`. This is `TreeTubeMesher.BuildCappedTube` (`TreeTubeMesher.cs:35-60`) with
   `sides = 2` — copy its per-station frame and `vLen` accumulation directly.
3. **Shader**: minimal URP transparent with a scrolling pan along **V only**. No flowmap
   texture needed — **the mesh UV *is* the flow map**, because V is arclength.
4. Parent under the planet transform as `PlanetWaterSurface` does (`:117-130`).
5. `river.*` console prefix (`river.count`, `river.rebuild`, `river.debug`), following
   `SurfacePathDebugCommands.cs`.

**Honest limits of slice 1:** the ribbon floats over concavities and clips into convexities
where terrain curves faster than station spacing (mitigate: smaller steps, or a
slope-dependent lift). No carve, so no channel. No pooling, so no rapids/waterfall
distinction.

**Slice 2 is waterfalls**, and the data is free by then: walk the same polyline, mark segments
where `drop/arcLength > tan(35°)` (matching the existing scatter slope gate,
`ScatterPrototype.cs:28`), swap those stations to a vertical sheet plus a base emitter.

### 5.5 River/waterfall technique notes

- **Poseidon is the only real river implementation across all ten packages surveyed.** It
  bakes per-vertex flow into `UV0.w` / `COLOR.a` / `UV1` from the spline tangent
  (`PSplineMeshCreator.cs:130-143`). **Per-vertex tangent vectors have no global-axis
  dependency, so this encoding is sphere-safe as-is** — and it is strictly better than a
  global `_Direction` for a curved world. ⚠️ Its flow-aware `ApplyWaveHQ` 5-arg overload is
  **dead code** — every call site uses the 4-arg version with the global `_WaveDirection`, so
  flow drives ripples only, never waves.
- **Flow maps:** the two-phase cross-fade (Vlachos, *Water Flow in Portal 2*, SIGGRAPH 2010) —
  `lerp(tex(uv + flow·frac(t)), tex(uv + flow·frac(t+0.5)), 2·abs(frac(t)−0.5))`. TVE ships a
  clean node-graph version (`Core/Functions/Compute Flow Map.asset`) that is **cheaper to
  reimplement in ~10 lines of HLSL than to import**. Known limits: incompatible with
  directional waves; max flow speed bounded by texture resolution.
- **Slope foam** is the closest thing to rapids found anywhere: Poseidon `PFoam.cginc:70-83`
  gates by surface tilt and scrolls bands down the slope. Sphere port: `normal.y*normal.y` →
  `dot(normal, radialUp)`; `worldPos.y` scroll → radial altitude.
- **Whitewater**, when we get there: `local-only/Fluid-Planet-main/.../FluidSim.compute` has
  the best model in the library, and it is **already planet-aware** — trapped-air spawn weight
  computed free inside the pressure loop (`:427-436`), classification into spray/foam/bubble
  **by neighbour count** (`:630-632`), and per-class integration where bubbles use
  `GravitationalAccelerationAtPoint` (`:634-666`). Orientation-free; harvest the classifier,
  not the SPH sim.
- **River generation: exactly two implementations exist in the entire library**, and both are
  spline-based. Poseidon's `PSplineMeshCreator`, and Polyart's
  `SharedResources/Scripts/Tools/River Tool/SplineRiverGenerator.cs` (280 lines,
  `#if UNITY_EDITOR`, `UnityEngine.Splines` → ribbon mesh with `tileLength`, `widthSegments`,
  `width`, terrain-raycast snapping, spline-hash change detection). Both are **editor-time
  authoring tools** — patterns to port, not systems to ship, and neither generates a network.
  Nothing in the library derives rivers from terrain. Our §5.4 slice is procedural where both
  of these are hand-authored, which is the right difference for a procedural planet.
- **Rapids / whitewater: nothing anywhere in the library.** Confirmed across all packs.
  Slope foam (above) is the closest thing that exists.
- **Interactive ripples: no *water* package solves it** — Poseidon's "ripple" is ambient noise
  (`PRipple.cginc:15-20`), Stylized Water 2's dynamic effects are a *separate paid asset*
  (only hook points ship), and NOT_Lonely's ripples are rain-driven flipbooks, not
  object-reactive. See **W28** for the storage surface and the two simulation references.

---

## 6. What breaks on a sphere — consolidated

| Assumption | Sphere-safe formulation | Status here |
|---|---|---|
| `up == +Y` | `planetUp = normalize(pos − _PlanetCenter)`; `dot(N, planetUp)` | **already done** — `WaterVolume.shader:193,311-321` |
| Constant world-Y sea level | ray-sphere; `oceanViewDepth = min(dstThroughOcean, sceneDepth − dstToOcean)` | **already done** — `WaterVolume.shader:162,183` |
| XZ-tiled UV domain | you cannot tile a plane over S² | **open — the hard one** |
| 2-D wave direction field | no globally continuous 2-D direction field exists on S² (hairy ball) | **open, plus W-BUG-1** |
| Screen depth == water column | carry two depths: screen-space for fog/refraction validity, `oceanRadius − terrainRadius` for colour and gameplay | **partially** — physical depth at `WaterVolume.shader:537`; W4 completes it |
| Infinite plane / screen-space horizon | horizon is at finite `√(2Rh+h²)`; a radius-limited annulus replaces the whole construction | non-issue (closed mesh) |
| `cross(float3(0,1,0), up)` tangent basis | degenerate at `abs(up.y) > 0.99`; branch the reference axis | **already done** — branches at 0.92, `Ocean.shader:205-207` |
| Persistent foam accumulation buffer | per-cube-face RT, or camera-following RT with reprojection | **unsolved in every source surveyed** (W7) |
| Threshold-mask triplanar blending | averaging three *thresholds* regresses toward 0.5 and destroys binary clumping — blend the decision, or hash from 3-D planet-local position | **latent** — verify before any triplanar move of our halftone foam (`Ocean.shader:542-559`) |

---

## 7. Explicitly not recommending

Recorded so these die on paper and are never re-surveyed.

| Rejected | Why |
|---|---|
| **Stylized Water 2 as a system** | Compatibility Mode only (`StylizedWaterRenderFeature.cs:36-40` hard-errors on RenderGraph). Wave field is `float2 xzVtx` all the way down — a redesign, not a port. Cannot displace an arbitrary mesh. Its production shader is a `.watershader` generated by a ScriptedImporter. |
| **Thalassophobia, entirely** | Built-in RP + `GrabPass`, unlit, **zero waves**. "URP" overlay is byte-identical to the base and targets URP 7.3.1 against a URP 17 base. |
| **Obi Fluid** | **Not installed** — Obi Rope 7.1.1 only; the actor/renderer/material layer is absent, so nothing can be spawned. Even complete: particle budget rules out planet scale, and its "buoyancy" is a per-particle gravity multiplier, not displaced volume. Marching-cubes surfacer solves a problem our shader-rendered shell does not have. |
| **Plawius NonConvexCollider** | V-HACD/CoACD wrapper. Computes no volume, no CoM, no inertia. Zero buoyancy relevance. |
| **`Environment-Project-Master`** | All submodule directories (`OceanPlugin`, `BuoyancyPlugin`, …) are **empty on disk**. Only two FBXs. |
| **`GraphicsLib`** | No water outside `external/`. |
| **`fastcaustics.pdf`** | Requires a **flat finite receiver plane** with constant rest depth — the authors name this as the limitation. Seabed terrain relief (not curvature) breaks it everywhere that matters. Superseded by W23 where RT exists. |
| **`waves.pdf` Fresnel LUT + planar-mirror reflection RT** | Obsolete. Schlick is ~3 ALU today; a dependent fetch is now slower than the math. The LUT is keyed on world `(xv,zv)` and only works because up == +Y. |
| **SPH / `Fluid-Planet` as an ocean** | 1.8 M particles at 3 substeps, no surface, nothing CPU-queryable. Lague's answer to "fluid on a sphere" was to **abandon the 2-D domain entirely** and go fully 3-D with a 150³ SDF — that is the negative result worth remembering. Harvest the whitewater classifier and the world-radius-driven bilateral blur only. |
| **`GDWaterKart`'s FFT + readback backend** | Six measured live CPU/GPU divergences, plus a dead and buggy curvature term (`fft_ocean.gd:330` computes `pow(global_pos.y − camera.global_position.z, 2.0)` — mixing y with z — and pushes a parameter no shader declares). Copy its **force model**, not its wave model. |
| **Per-cube-face FFT with seam blending** | Reintroduces exactly the cube-face seam class already in our failure archaeology. |
| **Poseidon's planar reflection, tile snapping, and renderer feature** | Mirror plane about `Vector3.up` + oblique clip; XZ tile snap with `TilesFollowMainCamera`; no `RecordRenderGraph` anywhere in the bundle. Irreducibly flat or dead. |
| **DWP2's ship/sail/submarine/anchor subsystems** | Heavily Y-locked (pitch/roll via `right.y = 0`, `SignedAngle(…, Vector3.up)`, `Physics.gravity.y`), plus 8 × `[DefaultExecutionOrder]` and coroutines — both banned by our rules. The buoyancy core is the only part worth taking. |
| **Camera-following water patch** | Already retired 2026-05-28. Do not re-litigate. |
| **Global `ZTest Always` water lip pass** | Already retired — standing rule, the failure is geometric, not tunable. |
| **FS Swimming System (Fantacode)** | Hardcodes `Vector3.up` and `Physics.gravity.y` (`Swimming.cs:190,329,376,849`). Not sphere-portable. ECM2 (W26) is strictly better and already gravity-agnostic. |
| **Vefects, Hovl, MagicArsenal, KriptoFX water "shaders"** | All spell/VFX cards, not fluid. Vefects' splash/wave/whirlpool shaders are **Built-in RP with `GrabPass`**. Useful only as a separate spell-VFX harvest for the wizard game, not for the ocean. |
| **Snowify** | Contains **zero shaders** — it is a mesh-extrusion editor tool backed by a closed-source DLL. Cannot double as wetness shading. (The snow/ice *shader* is in BruteForce, `BF_SnowIceURP.shader`, whose `_ICE` half is the nearest wet-layer analogue.) |
| **SurfaceData** | Surface-**type** detection framework (material/physic-material/terrain-layer → surface). Ships no `Water` or `Wet` asset and no shaders. Would need authoring from scratch; does not detect wetness. |

### 7.1 Confirmed water-free — do not re-search

`ARTnGAME` (⚠️ **Oceanis and InfiniRIVER are welcome-screen PNG icons only — the packs are
not installed**, correcting an assumption in the 2026-08-10 catalog) · `KriptoFX`
(VolumetricBloodFX only) · `TriForge Assets` · `Fraktalia` · `MultiTexture_URP` · `IndieKit` ·
`Polygonmaker` · `Waldemarst` · `Procedural Worlds` (water = audio; "floating" hits are
floating-*origin*) · `TelePresent` · `ASTROFISH_GAMES` (`sea` hits are `seamless`/`season`) ·
`Damian Gonzalez` (portal rendering) · `UniStorm Weather System` · `Aura 2` ·
`VolumetricFog2` · `Packages/com.occasoftware.altos` · `Character Controller Pro` (water is a
surface-multiplier profile) · `Character Movement Fundamentals` · `polyperfect` (water is a
craftable item) · `Corals` (art only, no water shader) · `DTT` · `Sprite Shaders Ultimate` ·
`Assets/Particles` (one legacy 2015 gate FX prefab) · `Synty PolygonParticleFX` (**zero
shaders**; particle prefabs only) · `CombatMagicSpells` (600 `.wav`, audio only) ·
`Quirky Series`, `Layer Lab`, `KayKit`, `BitGem`, `Kugon`, `InfinityPBR`, and the Synty
Construction/Dungeon/Farm/Western/Knights/Adventure packs (props and planes, art only) ·
`_Wyrms`, `DestroyIt`, `Devdog`, `EasyGridBuilder Pro`, `MagicaCloth`, `MeshCombineStudio`,
`HighlightPlus`, `MegaBook`, `EndlessBook`, `Blaze AI`, `Opsive`, `FImpossible Creations`,
`Clothing Culler`, `POLYSTYLE_MedievalDungeons`, `StylizedDragonPack`, `PolygonHorse`,
`Fantasy_Map_Creator`, `Fantasy Portal FX`, `DynamicPortals`, `NWH/WheelController`.
`Packages/manifest.json` contains no water-related Unity package.

**Art / style references only, if a look pass ever wants them** (URP toon water, no system):
`Toon Enchanted Meadow` (`TEM_Water` / `TEM_Waterfall` / `TEM_WaterRipples` /
`TEM_WaterParticles`, all URP Amplify), `Toon Fantasy Nature` (`TFF_ToonWater`),
`Toon Harbor Pack` (`TH_ToonWater`), `Fantastic Seaside Town` / `Fantastic Nature Pack`
(TidalFlask URP depth-fade water), `Fantasy Adventure Environment` (waterfall + waterfall-foam
shadergraphs; the `.shader` twins are Built-in), `Synty PolygonElvenRealm` (`Waterfall.shadergraph`
— a trivial 3-texture scroll), `LMHPOLY` (~678 modular river-bed mesh prefabs, stock Standard).

**Swim animation clips**, for whenever W26 lands: `Kevin Iglesias` (68 matching files, the
largest set), `Fantacode Swimming System` (13 incl. dive-entry / surface-idle / underwater
strafe), `ExplosiveLLC RPG Character Mecanim` (21), `Malbers` (animal swim incl. Horse AnimSet Pro).

---

## 8. Suggested sequencing (for Bryan)

Nothing below is started. This is a proposal for what a design doc would cover.

**Phase 0 — free wins and defect fixes** (no new subsystem)
W2 (degenerate axis, one line) · W-BUG-2 (promote the two swell constants) · W3 (delete three
dead subsystems) · decide W-BUG-3 (fix or document the ±5 m tolerance). Add a `water.*`
console prefix while touching it. **Then run the two existing timed capture sets** —
`Performance Water Isolation` and `Performance Water Volume Stages` have never been run, and
we currently have **zero** GPU water measurements.

**Phase 1 — the keystone**
W1 (CPU wave query + parity debug mode). Small, and it is the precondition for buoyancy,
swimming, boats, splashes and wave audio.

**Phase 2 — the look pass** (each independently gated on a capture-diff)
W4 (dual depth) → W5 (near/far scattering) → W9 (refraction re-sample) → W6 (Jacobian foam) →
W8 (hard-clipped shore band). W4 and W5 both aim directly at the "washed transparent sheet"
and "shoreline thin line" scar tissue, from opposite ends.

**Phase 3 — physics, cheap tier first**
W26 (ECM2-style immersion-depth swimming + buoyancy — already gravity-agnostic, covers a
swimming character and floating props) **before** W17 (per-triangle hydrodynamics, which is
what boats and rafts need). Both need a character collider first.

**Phase 4 — the interaction buffer**
W28 (camera-following interaction RT). Unlocks W7 (foam persistence), object-reactive ripples,
wakes, and revives the currently-dead wake system in one piece of work.

**Phase 5 — the structural change**
Per-body water level, then W20 (rivers, slice 1), then W24 (waterfalls, slice 2). W25's
`_FlowPivot` radial lake mode lands naturally here.

**Deferred / gated:** W16 (mesh LOD — big, and the gate for W22), W13 and W21 (caustics —
deliberate-change rule), W23 (RT caustics — hardware), W27 (wet surfaces — its own arc, and
it belongs to weather as much as to water).

## 8.1 Measured water cost — first GPU numbers this project has ever had

Captured 2026-08-17, play mode, teleport `Lake1` (camera ~96 m above a sea level of 5000),
60-sample rolling window per mode. Both previously-unrun timed sets were executed.
Sidecars: `local-only/debug-screenshots/F10-water.*-20260817-0828*.txt` and `*-0829*.txt`.

Raw GPU averages, grouped by run (**only within-run deltas are valid** — the baseline drifts
between runs):

| Mode | Isolation run 1 | Isolation run 2 | Volume-Stages run |
|---|---|---|---|
| `WaterOff` (no water) | 15.39 | 15.28 | 15.19 |
| `SurfaceOnly` (938k mesh, no volume) | 15.30 | 15.41 | — |
| `VolumeOnly` (composite, no surface) | 17.83 | 18.08 | 16.52 |
| `Off` (full water) | — | 18.39 | 15.73 |

### What is robust — reproduced in every run

- **The 938,167-triangle water mesh is effectively free.** `SurfaceOnly` lands within ±0.13 ms
  of `WaterOff` in both runs that measured it (−0.09, +0.13). It is noise.
- **The fullscreen volume composite is the entire water cost.** `VolumeOnly` exceeds
  `WaterOff` by **+2.44, +2.80, +1.33 ms** — same sign, same order of magnitude, three for
  three.

### What is NOT robust — do not quote these

- **The absolute water total does not reproduce.** `Off − WaterOff` is **3.11 ms** in one run
  and **0.54 ms** in the other — a 6× spread. Worse, the Volume-Stages run has
  `Off` (15.73) *below* `VolumeOnly` (16.52), which is physically impossible and proves
  between-mode noise is comparable to the effect being measured.
- **The volume could not be decomposed into stages.** Single-sample deltas over `WaterOff`
  were `VolumeOptical` +0.14, `CausticsOnly` +0.36, `BottomDistortionOnly` +0.94 — all inside
  the noise band demonstrated above. **Indicative only.** Getting real stage numbers needs
  repeated interleaved runs, a fixed clock state, and ideally a GPU profiler capture rather
  than this frame-timer.

### What this still changes

The direction is unambiguous even if the magnitude is not: **the mesh is huge and free, the
composite is small on paper and expensive in practice.** Water perf work starts at
`WaterVolume.shader` — raymarch step counts (`viewSteps=16, sunSteps=8`, per sidecar), the
triplanar 3-layer Voronoi caustics (`:219-378`), and the full camera-colour copy — **not** at
the mesh. W16 is corrected accordingly.

**CPU side:** `Uninstrumented CPU: avg=21.70 ms` against a `22.11 ms` CPU frame — **98 % of
CPU frame time is not instrumented**. The four surviving sections (surface/terrain 0.37,
clouds 0.01, near grass 0.02, chunk grass 0.00) total 0.4 ms. Whatever the CPU is doing, no
counter sees it. Not water-specific, but worth its own investigation.

Caveats: one viewpoint, one session, one machine, and `Lake1` is a **lake, not open ocean** —
screen coverage of water is modest. Open ocean would likely raise the composite cost, not
lower it.

## 9. Open questions

1. **Per-body water level** — is that structural change in scope for this arc, or do rivers
   wait? It gates mountain lakes, rivers and waterfalls together.
2. **W14 (Poseidon flat shading)** conflicts with our current water shading. Look decision,
   yours.
3. **How far does buoyancy need to go?** Three tiers now, not two: position-snap (Poseidon's
   `SimpleBuoyController`, 23 lines) for lily pads; immersion-depth scaling (**W26**, ECM2 —
   already gravity-agnostic, covers a swimming character and floating props); full
   per-triangle hydrodynamics (**W17**) for boats and rafts. The roadmap has sailing, which
   argues for W17 eventually — but W26 ships in an afternoon and covers swimming, which is
   roadmap #20.
4. **Underwater is named as the thing to fix first** (`docs/design/2026-08-12-ocean-scatter.md:100-101`).
   Does that promote W12 + W21 above the Phase 2 surface look pass?
5. Separate cleanup, flagged not fixed: **8 skill/doc files still assert the caustics
   prohibition lifted 2026-08-11.**

---

## 10. Provenance

Survey conducted 2026-08-16 against branch `harvest-vertical-slice`, working tree dirty on
`cd4e0f0`. Eight parallel read-only agents; no code, asset or setting was modified. Cached
`.unitypackage` files were extracted to the session scratchpad, not into the project. The
breadth sweep indexed 134,131 non-`.meta` files across `D:\Unity\Explore Assets\Assets` and
`Packages`, token-filtered on ~30 water tokens, then content-grepped for `Gerstner`,
`_CameraOpaqueTexture` / `SampleSceneDepth`, `caustic`, `buoyan|Archimedes`, `ripple`,
tessellation, and URP-vs-Built-in tags before opening files. §7.1 is the negative result of
that sweep and is intended to prevent re-searching.

Nothing in this document has been visually verified. W-BUG-1 is derived analytically and has
a stated discriminating probe (§2); it has **not** been observed in a capture. Per
pp-research-methodology, treat every visual claim here as a hypothesis with a prediction
attached, not as a result.

**Sourcing depth is uneven, deliberately.** Read first-hand: Stylized Water 2, DWP2, Obi,
Poseidon, Thalassophobia, `FFT-Ocean`, `Fluid-Planet`, and our own water stack (§1, §2).
Read via a single agent hop: `GDWaterKart`, `Geographical-Adventures`, `Solar-System-Development`,
the breadth sweep (§7.1), the PDFs and the guides. All **numeric** claims — the 210
dispatches/frame, the 13.4 % stretched fraction, the 30.8 m quad size, the wavenumber
quantisation error, the metres-per-texel figures — were computed and checked rather than
quoted. Where a claim contradicts a prior project doc it is called out in §3.2 with the
source line, not silently corrected.

Re-verify volatile facts with:

```bash
# Water mesh size and whether the lip is still dead
grep -n "VolumeLipMesh" local-only/debug-screenshots/F10-water.00-*.txt | tail -1

# Wake globals still read by nothing
grep -rn "_WaterWake\|_WakeFoamIntensity\|_WakeNormalStrength" Assets/Graphics/Shaders/

# Swell constants still material-only (W-BUG-2)
grep -rn "_SwellAmplitude\|_SwellWavelength" Assets/Scripts/

# Still no rivers, no buoyancy, no CPU water query
grep -rniE "\briver\b|\bwaterfall\b|buoyan|GetWaterHeight" Assets/Scripts/ || echo "still none"

# Sea level still has two writers
grep -rn "SeaLevelRadius" Assets/Scripts/Planet/Planet.cs Assets/Scripts/Planet/AtmosphereController.cs

# The degenerate axis construction (W-BUG-1)
sed -n '200,215p' Assets/Graphics/Shaders/Ocean.shader
```
