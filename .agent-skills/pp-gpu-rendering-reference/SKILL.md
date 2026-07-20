---
name: pp-gpu-rendering-reference
description: Use when working on or reasoning about this repo's rendering math and GPU techniques - cube-sphere face/UV mapping and cube-face seams, volumetric cloud raymarch terms (Beer-Powder, multi-scatter octaves, blue noise, silver lining, cloud shadows), GPU grass placement/indirect draw/dither LOD/wind, water surface-vs-volume layers and caustics, atmosphere scattering, rain/precipitation shaders, URP render pass order. Not for weather-grid data contracts (pp-weather-sim-reference), executing the live cloud/grass migration (pp-visual-migration-campaign), or symptom triage (pp-debugging-playbook).
---

# GPU Rendering Reference — ProceduralPlanets

Domain theory **as implemented in this repo**, each technique tied to the file that
implements it. Read this before editing any shader, compute, or rendering controller so
you know *why* each term exists, not just where it lives. All file paths are
repo-relative. All "current state" claims are date-stamped 2026-07-06 against the dirty
`code-refactor` working tree (dirty is normal here — active work lives uncommitted).

Deep dives in this directory:

| File | Covers |
|---|---|
| [cube-sphere.md](cube-sphere.md) | 6-face cube→sphere math, the THREE UV conventions, area distortion, the historical cloud-seam pitfall, multi-face seam handling |
| [clouds.md](clouds.md) | Volumetric cloud raymarch end-to-end: every lighting term and why it exists visually; quality tiers; cloud shadows |
| [grass.md](grass.md) | GPU grass: compute placement, InterlockedAdd slot-claim + rollback, indirect draw, blade geometry, dither LOD, wind, interactors, three-layer LOD state |
| [water-atmosphere-precipitation.md](water-atmosphere-precipitation.md) | Water surface/volume split + caustics don't-touch, atmosphere scattering, precipitation and particle shaders |

## When NOT to use this

- **Weather grid channels, evolution kernels, the sim→renderer data contract** → `pp-weather-sim-reference`. This skill covers how renderers *consume* weather; the sim itself is not here.
- **Executing the live cloud/grass visual migration** (phase steps, gates, capture checklists) → `pp-visual-migration-campaign`. This skill explains the techniques those phases install.
- **"Something looks wrong, where do I start?"** → `pp-debugging-playbook` (symptom→triage, stage-ownership method).
- **Adding/altering a setting, quality tier, or toggle** → `pp-settings-and-flags`.
- **Rules on when a visual change may land at all** → `pp-change-control`. Never tune visual constants without a capture diff; Bryan's eyes lock a look.

## Jargon (defined once, used everywhere below)

| Term | Meaning here |
|---|---|
| **Raymarch** | Numerically stepping along a view ray, sampling a density field at each step and accumulating color/opacity. Used by clouds, precipitation, atmosphere. |
| **Transmittance** | Fraction of light surviving a path through a medium. Beer-Lambert law: `T = exp(-density × absorption × distance)`. `T=1` clear, `T=0` opaque. |
| **Phase function** | How a scattering medium redistributes light by angle. Henyey-Greenstein (`HG` in `Cloud.shader`) has one eccentricity param `g`: `g>0` forward-scatter, `g<0` back-scatter. |
| **Indirect draw** | GPU draws N instances where N lives in a GPU buffer (`GraphicsBuffer.Target.IndirectArguments`), never read back to CPU. The placement compute writes the count; `Graphics.RenderPrimitivesIndirect` consumes it. |
| **IGN** | Interleaved gradient noise (Jimenez 2014): a one-line screen-space hash whose threshold distribution is effectively continuous. Used for grass fade dithering. |
| **Blue noise** | Noise with only high-frequency energy — errors dithered with it look like fine grain instead of blotches/bands. Texture: `Assets/Graphics/Shaders/BlueNoise.png`, bound as `_CloudBlueNoise`. |
| **Gnomonic projection** | Projecting a sphere direction onto a cube face by dividing by the dominant axis component. All cube-face UV math here is gnomonic. |
| **FBM** | Fractal Brownian motion — several noise octaves summed with decreasing weight. Cloud shape/detail textures store 4 pre-baked octaves in RGBA, combined by `WeightedNoise`. |
| **Fullscreen triangle** | One 3-vertex triangle covering the screen (`GetFullScreenTriangleVertexPosition`), drawn by `ctx.cmd.DrawProcedural(..., 3, 1, ...)` — cheaper than a quad, standard for post-style passes. |
| **Ping-pong** | Two textures alternating read/write roles each step (weather evolution uses active/scratch pairs, swapped after each dispatch). |

## The render stack (URP ScriptableRendererFeatures), in pass order

Verified from `renderPassEvent` assignments, 2026-07-06:

| Order | Pass | Event | File |
|---|---|---|---|
| 1 | Stars | `BeforeRenderingOpaques` | `Assets/Scripts/Planet/Atmosphere/StarRenderFeature.cs` |
| 2 | Opaque terrain etc. | (URP) | — |
| 3 | Water volume prepass + composite | `BeforeRenderingTransparents` | `Assets/Scripts/Planet/WaterVolumeRenderFeature.cs` |
| 4 | Grass (not a feature — draw call at `Queue = Transparent-10`, ZWrite On) | between water composite and ocean surface | `Assets/Graphics/Shaders/Grass.shader` |
| 5 | Ocean surface (transparent queue) | — | `Assets/Graphics/Shaders/Ocean.shader` |
| 6 | Atmosphere | `BeforeRenderingPostProcessing` | `Assets/Scripts/Planet/Atmosphere/AtmosphereRenderPass.cs` |
| 7 | Clouds | `BeforeRenderingPostProcessing + 1` | `Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs` |
| 8 | Precipitation curtains | `BeforeRenderingPostProcessing` | `Assets/Scripts/Planet/PrecipitationRenderFeature.cs` (`PrecipitationRenderPass`) |
| 9 | Rain particle streaks | `AfterRenderingPostProcessing` | same file, `RainParticlesAfterPostPass` |

Two orderings are load-bearing (both documented in code comments):

- **Clouds run one tick after atmosphere** so terrain-depth fog cannot wash out cloud
  bodies (`CloudRenderPass` constructor comment, `CloudRenderFeature.cs`). Consequence:
  distant clouds get **no aerial perspective** — the planned fix is a distance fade
  toward horizon color inside `Cloud.shader` (migration plan Phase 3, not yet coded as
  of 2026-07-06), never a pass reorder.
- **Grass draws right after the water-volume composite with ZWrite On** so the ocean
  surface depth-tests against blades (`Grass.shader` Tags comment).

`CloudRenderFeature.AddRenderPasses` also shows the standard skip pattern for planet
fullscreen passes: skip Preview/Reflection cameras, skip when `_WaterFocusMode` or
`_DebugSuppressWeatherPasses` globals are set, skip when the planet's atmosphere bounds
fail a frustum test, and resolve the owning controller via `ServiceLocator.TryGet` with
liveness caching.

## One-paragraph orientation per domain (details in the linked files)

**Cube-sphere** ([cube-sphere.md](cube-sphere.md)): every spherical dataset (weather,
biomes, grass placement, surface atlases) is stored as 6 cube faces indexed
`0=+Y, 1=−Y, 2=−X, 3=+X, 4=+Z, 5=−Z` and addressed by gnomonic face-UV. There are
**three distinct UV-orientation conventions** in the codebase; mixing a forward mapping
with the wrong inverse is exactly what caused the historical diagonal cloud seams. Rule:
a dataset is written and read with the *same matched pair*, and cross-checking which
pair applies is step one of any face-space work.

**Clouds** ([clouds.md](clouds.md)): a single fullscreen raymarch through a spherical
shell (`_CloudInnerRadius`..`_CloudOuterRadius`) in `Assets/Graphics/Shaders/Cloud.shader`.
Density = weather condensation × baked 3D shape FBM × vertical envelope, eroded by
detail noise. Lighting per lit sample: a jittered light march toward the sun feeding
Beer-Powder transmittance, 3-octave multi-scatter, two-tone height-lerped ambient,
storm/rain gloom, silver lining, lightning. Blue-noise ray offset kills banding.
`CLOUD_QUALITY_LOW` compiles an 8-step, no-detail variant. Ground shadows recompute a
cheap 3-sample version of the same density in
`Assets/Graphics/Shaders/Includes/CloudShadows.hlsl`.

**Grass** ([grass.md](grass.md)): compute shaders place blades directly into a GPU
instance buffer (`GrassNearFieldPlace.compute` camera-centered, `BiomeGrassPlace.compute`
per-chunk) and bump the indirect instance count with `InterlockedAdd` (with an explicit
rollback on overflow); `Grass.shader` then builds all blade geometry procedurally from
`SV_VertexID` — no mesh asset anywhere. Determinism is position-hash based so camera
motion can't re-roll blades. As of 2026-07-06 only the near-field layer is live
(`_chunkGrassEnabled = false`, `_grassBlanketEnabled = false` in
`Assets/Scripts/Planet/PlanetGrassCoordinator.cs`; current value: see
pp-settings-and-flags) — beyond 200 m there is bare terrain,
and the far-field story is an open DECISION owned by the migration campaign.

**Water** ([water-atmosphere-precipitation.md](water-atmosphere-precipitation.md)):
two-layer split — `Ocean.shader` renders only the top sheet (waves, foam, glitter, ice);
`WaterVolume.shader` is a fullscreen composite owning underwater fog, refraction, and
**caustics**. Caustics are under a hard don't-touch rule (CLAUDE.md): describe, flag
findings, never edit.

**Atmosphere** (same file): classic single-scattering Rayleigh+Mie raymarch
(`Assets/Graphics/Shaders/Includes/Atmosphere.hlsl`) with sun optical depth from a
compute-baked LUT; `AtmosphereController.cs` is the reference implementation of the
dirty-flag global-upload pattern.

**Precipitation** (same file): `Precipitation.shader` raymarches rain curtains through a
slab under the cloud shell, gated by the same weather channels; `RainParticles.shader`
draws per-drop streaks from a persistent compute-simulated buffer;
`WeatherParticles.shader` handles ambient dust/snow.

## Cross-cutting rules that govern all of the above

These are CLAUDE.md doctrine — restated here because every rendering edit touches them:

1. **Dirty-flag global uploads.** Controllers that push shader globals split static vs
   per-frame properties, set `_staticPropertiesDirty = true` on `PlanetGeneratedEvent`,
   `SettingsChangedEvent`, and every console-command setter, and upload only when dirty.
   Exemplars: `AtmosphereController.EnsureStaticPropertiesUploaded`,
   `CloudController.EnsureStaticPropertiesUploaded` (which additionally caches
   last-uploaded textures/ints so per-frame `Update` usually uploads nothing).
2. **`ShaderGlobalIds` owns every global name.** Any name passed to `Shader.SetGlobal*` /
   `Shader.GetGlobal*` must be a `const string` in the domain partial
   (`Assets/Scripts/Core/Services/ShaderGlobalIds.Cloud.cs`, `.Precipitation.cs`, etc.).
   Modules cache their own `static readonly int _xId = Shader.PropertyToID(ShaderGlobalIds.X)`.
   Per-material and compute-shader-scoped property names stay module-local (e.g. the
   `_NearFieldDrawArgs` ID in `GrassNearFieldController.cs` and the `_WeatherRead/_WeatherWrite`
   IDs in `SphericalWeatherGrid.cs` are correctly NOT in ShaderGlobalIds).
3. **Compute dispatch costs ~50-100 μs launch overhead** — don't dispatch trivial
   workloads. **`AsyncGPUReadback` adds 1-2 frames of latency** — never use it for a
   result needed this frame. Concrete in-repo example of designing around that latency:
   `SphericalWeatherGrid` CPU cell arrays start empty and are filled progressively, one
   face per readback interval, with a documented fallback until they arrive
   (`GenerateComputeAsync` comment).
4. **Per-frame hot work = compute or Burst; one-shot expensive work = background thread
   via `Awaitable.BackgroundThreadAsync`.** No coroutines, no `async void`, no `Task.Run`.
5. **Quality tiers** (`Assets/Scripts/Core/QualityController.cs`): the tier is classified
   from the Unity quality-level *name* (tokens "mobile/low/fastest" → Low,
   "medium/balanced" → Medium). Low enables the `CLOUD_QUALITY_LOW` shader keyword
   (Cloud + Precipitation variants) and sets `CloudStepMultiplier` 0.33 (Medium 0.65,
   High 1.0), which `CloudController.UpdatePerFrameProperties` applies after an
   altitude-based step LOD.

## Provenance and maintenance

Everything above was verified 2026-07-06 by reading the working tree on branch
`code-refactor` (dirty tree is the source of truth; several cited files are modified vs
HEAD `ec0b1cd`). Re-verify volatile facts before trusting them:

```
# Layer enable state (grass three-layer LOD)
grep -n "_chunkGrassEnabled\|_grassBlanketEnabled" Assets/Scripts/Planet/PlanetGrassCoordinator.cs
# Pass order
grep -rn "renderPassEvent" Assets/Scripts/Planet --include="*.cs"
# Quality tier multipliers and keyword
grep -n "CLOUD_QUALITY_LOW\|StepMultiplier" Assets/Scripts/Core/QualityController.cs
# Cloud step caps
grep -n "CLOUD_MAX_STEPS\|CLOUD_LIGHT_STEPS_MAX" Assets/Graphics/Shaders/Cloud.shader
# Near-field distances
grep -n "NearField.*Distance\|NearField.*Altitude" Assets/Scripts/Core/QualityController.cs
# Migration status (which phases have landed in code)
sed -n '1,25p' docs/design/2026-07-04-cloud-visual-migration-plan.md
sed -n '1,25p' docs/design/2026-07-04-grass-visual-migration-plan.md
```

If `graphify-out/graph.json` exists, `graphify query "<question>"` first for structural
questions; `graphify explain "CloudRenderFeature"` etc. for focused concepts. Set a
timeout — graphify query/update previously hung in this checkout (audit G19, see
pp-build-and-env Known traps); fall back to `rg`/`rg --files` on hang.
