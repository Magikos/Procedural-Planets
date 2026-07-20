# Water, Atmosphere, Precipitation — layers and pass order

Part of `pp-gpu-rendering-reference`. Verified against the working tree 2026-07-06.
Primary files: `Assets/Graphics/Shaders/Ocean.shader`, `WaterVolume.shader`,
`WaterVolumePrepass.shader`, `Assets/Scripts/Planet/WaterVolumeRenderFeature.cs`,
`Assets/Scripts/Planet/WaterMeshBuilder.cs`,
`Assets/Graphics/Shaders/Includes/Atmosphere.hlsl` + `Atmosphere.shader`,
`Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs` + `AtmosphereRenderPass.cs`,
`Assets/Graphics/Shaders/Precipitation.shader`, `RainParticles.shader`,
`WeatherParticles.shader`, `Assets/Scripts/Planet/PrecipitationRenderFeature.cs`,
`Assets/Scripts/Planet/Precipitation/RainParticleController.cs`.

## 1. Water: the surface / volume split

Water is deliberately two independent layers so each can be validated alone (the water
artifact saga was won by isolating stages, not tuning):

| Layer | Shader | What it owns |
|---|---|---|
| Surface | `Planet/Ocean` (`Ocean.shader`), a normal transparent-queue mesh draw | The top sheet only: vertex swell displacement, fragment wave-normal detail, foam (shore/whitecap/wake), sun glitter, freeze/ice |
| Volume | `Hidden/WaterVolume` fullscreen composite via `WaterVolumeRenderFeature` | Everything *through* the water: underwater fog/absorption, bottom-refraction distortion, far-terrain waterline tinting, and **caustics** |

The Ocean pass comment states the contract: "WaterVolume owns underwater
fog/refraction/caustics. This pass adds only the top sheet color so the layer can be
validated by itself." `ZWrite Off` on both — grass and terrain provide the depth the
volume pass reads (and clouds do an explicit ocean-sphere test precisely because water
is absent from the depth buffer, [clouds.md](clouds.md) §2).

### The mesh and its vertex-color data channel

`WaterMeshBuilder` (`Assets/Scripts/Planet/WaterMeshBuilder.cs`) builds one spherical
water mesh per world from the wet cells of the 6 cube faces (`MeshData` is documented
"Safe to produce on a background thread via `Compute`" — the Awaitable background
pattern). Its load-bearing output is **vertex color as a data channel**:
`r = depth01, g = shore01, b = body01 (pond↔ocean), a = temperature01`. Both the ocean
vertex stage and the volume prepass decode exactly this layout — if you change one
consumer, you change three. It also emits a second mesh, the **volume lip** (child
`WaterVolumeLip`), used only underwater (below). `BuildStats` (bodies, frozen bodies,
max depth) feed the water debug module.

### Ocean.shader in brief

Vertex: `ComputeOceanSwell` sums three `EvaluateSurfaceWave` sine waves in a
wind-aligned tangent basis and displaces the vertex **radially**
(`positionWS += planetNormalWS * swellHeight`) — real 3D waves on the existing spherical
mesh, world-fixed, *not* a camera-following patch (this is a settled architecture
decision; see `.agent-memory` ocean-wave approach). Swell is gated by
`EvaluateSwellGating(depth01, shore01, body01)` — ponds get low ripples, shores go calm,
open ocean gets full swell — and zeroed where frozen. Fragment: detail normals from more
sine waves + voronoi surface cells, storm energy sampled from the weather grid
(`SampleOceanStorm` — 7-tap blur of weather `.g`), foam masks, glitter
(`pow(spec, _SunGlitterPower)`), and ice (`EvaluateFreezeFactor` per body temperature +
`ValueNoise` breakup). ~20 `_OceanDebugMode` false-color views (LumaHeat, WaveEnergy,
etc.) are the stage-ownership proof tools.

### The volume prepass and the underwater lip

`WaterVolumeRenderFeature` enqueues two passes at `BeforeRenderingTransparents`:

1. **Prepass** (`Hidden/WaterVolumePrepass`): draws the water mesh into an off-screen
   `R16G16B16A16_SFloat` target ("WaterVolumeData") encoding
   `(forwardDepth, depth01, shore01, freezeFactor)`, published globally as
   `_WaterVolumeData`/`_WaterInterfaceTexture`.
2. **Composite** (`Hidden/WaterVolume`): fullscreen triangle that reads scene color +
   depth + the prepass target and rewrites `cameraColor`.

The **lip** exists because when the camera is *inside* the water sphere the water
surface behind the camera is not rasterized, leaving screen areas with no water data. So
`AddRenderPasses` gates a second prepass draw
(`WaterVolumeRenderFeature.cs:87`):

```csharp
bool drawRelaxedVolumeLip = renderableVolumeLipMesh != null
    && IsCameraInsideWaterMesh(camera, meshFilter, mesh);
```

`IsCameraInsideWaterMesh` is a cheap radius test (camera distance vs mesh bounds radius
+ 0.5 m). The lip pass renders with `ZTest Always` plus a *relaxed* manual depth gate in
the fragment (`FragRelaxedLip`: accepted where there's no opaque scene or the lip is
within a depth slack of it), filling the missing water data without stomping terrain in
front of it. Debug views `VolumeLip*Pink` visualize acceptance. The composite also has a
no-depth underwater fallback (`UnderwaterNoDepthColor`) and an orbital fade
(`VolumeLayerVisibility`) so the effect dies out at altitude.

### Caustics — the DON'T-TOUCH rule

What they are, so you can discuss them without editing them: in
`WaterVolume.shader`, `ComputeReceiverCaustics` projects animated light patterns onto
submerged terrain. The pattern (`CausticPatternUv`) is three voronoi layers, each with
its own **directional flow vector** so cells visibly travel (the long comment explains
the failure it replaced: heavy in-place warping read as "kneaded" morphing, and
non-linear time created a robotic rhythm). Triplanar-blended by `pow(abs(planetUp), 4)`
weights, faded by receiver depth (`exp(−depth/_CausticDepth)`) and water path, lit by
sun *and moon* with `CloudShadowFactor` applied to each. Chromatic fringes
(`CausticChromaticPattern`) sample the **same** pattern at identical time but small
per-channel spatial offsets — one shape, three wavelength landings; the comment records
that per-channel *time* shifts looked like three separate animations. The same
`CausticResult` also drives volume transmittance/opacity/fog and the bottom-refraction
distortion (`ComputeBottomDistortion`).

**CLAUDE.md rule: caustics are untouchable.** They look correct and historically every
touch broke them (the origin incident is why the rule exists). Audit findings against
caustics are flag-only. Never suggest edits to the caustic functions, their constants
(`CAUSTIC_SCALE 0.075`, `CAUSTIC_SPEED 1.05`), or the feature-level tuned values
(`CausticIntensity 0.42` etc. in `WaterVolumeRenderFeature`). Debug views
(`DEBUG_CAUSTICS_ONLY`, `_MASK`, `_LIGHT`, `_PRISM`) are the sanctioned way to inspect
them.

## 2. Atmosphere

`Assets/Graphics/Shaders/Includes/Atmosphere.hlsl`, `CalculateScattering`: classic
single-scattering **Rayleigh + Mie** raymarch. Per pixel: intersect the atmosphere
sphere, clamp by scene depth and the sea-level sphere, march `_ViewSteps` samples; at
each sample accumulate exponential-falloff densities
(`exp(-height/scaleHeight)`, separate Rayleigh/Mie scale heights) and attenuate by
`exp(-(view optical depth + sun optical depth))`. The **sun** optical depth is NOT
marched per sample — it's a 2D LUT lookup (`SunOpticalDepth`: u = sun angle vs zenith,
v = height01) baked by `OpticalDepthCompute` into an `RGHalf` render texture. **Trap:**
the file's header comment says "brute-force sun ray marching. No LUT" — that comment is
stale; the code below it samples `_BakedOpticalDepth`. Trust the code. Final composite
tone-maps only the in-scattered light (`s/(1+s)`), never the terrain:
`sceneColor * viewTransmittance + toneMappedScatter`. Phase functions: standard Rayleigh
`(3/16π)(1+cos²θ)` and full Mie HG with `_MieAnisotropy`.

`AtmosphereController.cs` is the codebase's reference **dirty-flag upload**
implementation: `_staticPropertiesDirty` set on `PlanetGeneratedEvent`,
`SettingsChangedEvent`, and rebake-worthy changes; `EnsureStaticPropertiesUploaded`
uploads ~20 globals only when dirty; the only truly per-frame upload is `_SunParams`
from `CelestialManager`. `LutNeedsRebake()` compares the five bake inputs (scale
heights, atmosphere scale, texture size, steps) so console tuning
(`atmosphere.rayleigh`, `.mie`, `.scale`, `.sun-intensity` — all `SettingsProvider.Update`
on the DTO, never the SO) rebakes only when needed. The pass
(`AtmosphereRenderPass.cs`) is a fullscreen `DrawProcedural` triangle at
`BeforeRenderingPostProcessing`; it also owns sun disc and light-shaft params. Scale
heights are stored normalized in the DTO and multiplied by atmosphere thickness at
upload — the shader sees meters.

## 3. Precipitation (rendering side only — sim contract in `pp-weather-sim-reference`)

Three shaders, one feature (`PrecipitationRenderFeature`), two pass events.

**`Precipitation.shader` (`Hidden/Precipitation`) — distant rain curtains.** Fullscreen
raymarch (`PRECIPITATION_MAX_STEPS` 48, or 8 under `CLOUD_QUALITY_LOW`) through a slab
`_PrecipitationRadii.x .. .y` under the cloud shell. Per sample,
`SamplePrecipitationSignal` reads the weather cube map: rain rate = `dynamics.b` gated
by a storm smoothstep on `weather.g` **and** cloud support `smoothstep(0.58, 0.9,
weather.r)` — no rain out of clear sky, by construction. Visual shaping
(`SamplePrecipitationDensity`): value-noise curtains, wind-sheared sample position
(more shear near the ground), and an anisotropic streak noise scrolling downward so
shafts read vertical, not foggy. The march clips against the sea-horizon sphere so
distant rain can't render behind the planet's curve, and
`PrecipitationCameraAboveSea()` kills the whole effect underwater. Runs at
`BeforeRenderingPostProcessing`.

**`WeatherParticles.shader` (`Hidden/WeatherParticles`) — ambient dust and snow.** Two
passes in the same file (0 = dust, 1 = snow), drawn inside the precipitation pass as
`DrawProcedural(..., 18, count)` — 18-vertex camera-facing ribbons whose positions are
derived **entirely from `instanceID` hashes** (no persistent buffer); counts come from
the precipitation controller. Proof modes (`_WeatherParticleProof` 1=Dust, 2=Rain,
3=Snow — comment notes rain streaks live in `RainParticles.shader`, this shader draws
only dust/snow).

**`RainParticles.shader` (`Hidden/RainParticles`) + `RainParticleController.cs` —
near-camera drops.** The opposite design: a **persistent** `ComputeBuffer` of `Raindrop`
structs (pos, velocity, life, pad — 32 B; default 30,000 of max 100,000). A compute
shader advances real positions each frame — gravity toward planet center (spherically
correct), wind coupling, respawn at cloud top on landing and at random column altitude
when the camera outruns the near radius (the controller's XML doc lists these cases;
"no `frac()` teleportation, no instanceID-derived state"). The render pass draws each
drop as a 6-vertex billboard stretched along its velocity. Per-drop visibility samples
weather `dynamics.b` at the drop's position: an over-threshold gate plus a stable
per-drop rank vs `rate × _RainDensityScale`, so heavy rain = downpour, light rain =
sprinkle. Crucially it draws in `RainParticlesAfterPostPass` at
**`AfterRenderingPostProcessing`** — the pass comment records why: atmosphere composites
colored haze at `BeforeRenderingPostProcessing`, and drops drawn before it got washed
out at sunset. Rain streaks composite LAST, on top of the final atmospheric color.

## 4. Pass order across water / atmosphere / clouds / precipitation

Verified 2026-07-06 from `renderPassEvent` assignments (full stack table in
[SKILL.md](SKILL.md)):

| Event | Pass |
|---|---|
| `BeforeRenderingOpaques` | Stars |
| `BeforeRenderingTransparents` | WaterVolumePrepass, then WaterVolumeComposite |
| transparent queue (`Transparent-10` → `Transparent`) | Grass (ZWrite On), then Ocean surface |
| `BeforeRenderingPostProcessing` | Atmosphere |
| `BeforeRenderingPostProcessing + 1` | Clouds |
| `BeforeRenderingPostProcessing` | Precipitation curtains + dust/snow particles |
| `AfterRenderingPostProcessing` | Rain particle streaks |

Load-bearing consequences: the volume composite runs *before* the ocean surface, so
what you see through the sheet was already fogged/refracted; grass writes depth between
them so the ocean depth-tests against blades; clouds run one tick after atmosphere so
terrain fog can't wash them (their aerial perspective is a planned in-shader fade, not a
reorder); rain streaks are exempt from atmosphere by running after post. Precipitation
and clouds share the skip pattern: Preview/Reflection cameras, `_WaterFocusMode`,
`_DebugSuppressWeatherPasses`, ocean-debug suppression, planet frustum test, and a
live-controller lookup via `ServiceLocator.TryGet` with liveness caching
(`PrecipitationRenderFeature.TryGetLiveController`).

## Provenance and maintenance

```
# Pass events
grep -rn "renderPassEvent" Assets/Scripts/Planet --include="*.cs"
# Lip gating (camera-inside test)
grep -n "IsCameraInsideWaterMesh" Assets/Scripts/Planet/WaterVolumeRenderFeature.cs
# Surface/volume ownership comment
grep -n "WaterVolume owns underwater" Assets/Graphics/Shaders/Ocean.shader
# Vertex-color data layout (both decoders)
grep -n "waterData.r\|input.color" Assets/Graphics/Shaders/Ocean.shader Assets/Graphics/Shaders/WaterVolumePrepass.shader
# Caustic constants (read-only — don't-touch rule)
grep -n "CAUSTIC_SCALE\|CausticIntensity" Assets/Graphics/Shaders/WaterVolume.shader Assets/Scripts/Planet/WaterVolumeRenderFeature.cs
# Stale "No LUT" header vs actual LUT sample
grep -n "No LUT\|_BakedOpticalDepth" Assets/Graphics/Shaders/Includes/Atmosphere.hlsl
# Dirty-flag exemplar
grep -n "_staticPropertiesDirty\|LutNeedsRebake" Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs
# Precipitation step budget + weather gates
grep -n "PRECIPITATION_MAX_STEPS\|cloudSupport" Assets/Graphics/Shaders/Precipitation.shader
# Rain-after-post rationale
grep -n "AfterRenderingPostProcessing" Assets/Scripts/Planet/PrecipitationRenderFeature.cs
```

The water-artifact debugging history (stage isolation, `WaterVolumeLip`, "washed
transparent sheet") lives in `.agent-memory/codex/` as additional background only; every
implementation claim above stands on the cited shaders and C# in the working tree.
