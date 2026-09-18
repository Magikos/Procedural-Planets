# Water, Atmosphere, Precipitation — layers and pass order

Part of `pp-gpu-rendering-reference`. Verified against the working tree 2026-07-06.
The water mesh and prepass sections were reverified against the dirty tree 2026-09-10.
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
| Volume | `Hidden/WaterVolume` fullscreen composite via `WaterVolumeRenderFeature` | Receiver fog/absorption, bottom-refraction distortion, far-terrain waterline tinting, and **caustics** |
| Submerged interface | `Hidden/Atmosphere` | Snell's window, surface reflection, and underwater sunlight shafts |

As of 2026-09-07, the two submerged consumers share `UnderwaterTransmittance` and
`UnderwaterAmbientColor` in `WaterVolumeData.hlsl`. `WaterDto` supplies fog color,
particle visibility distance, shaft width, night scale, and shaft intensity.
The reference-look implementation has Unity captures and passing console regression tests recorded in
[`2026-09-07-underwater-reference-and-test-queue.md`](../../docs/design/2026-09-07-underwater-reference-and-test-queue.md).

The surface extension uses the same wind-driven micro-ripple helper for Ocean, Atmosphere,
and bottom distortion. Ocean traces nearby visible opaque geometry for reflections, with
a sky fallback for misses. The prepass requests native opaque color and depth inputs.
Both sides use the shared displacement field; fully submerged views suppress the top sheet.

The interaction follow-up adds `WaterPresentationController`, bounded wake rings, pooled entry splashes,
and a native listener low-pass filter. `WaterCamera.hlsl` shares smoothed moving-surface immersion
with all weather passes so raised lakes suppress rain, snow, and cloud overlays underwater.
High/Medium quality use a native time-sliced reflection probe for nearby off-screen geometry.
Low disables that probe and reduces reflection, shaft, and ring budgets through `WaterQualityProfile`.
See [interaction and quality validation](../../docs/design/2026-09-07-water-interactions-and-quality.md)
for ownership, controls, measured budgets, and known approximation limits.

The Ocean pass comment states the contract: "WaterVolume owns underwater
fog/refraction/caustics. This pass adds only the top sheet color so the layer can be
validated by itself." Ocean keeps `ZWrite Off`. Since 2026-09-08, the prepass writes a
private `Depth32` water buffer. Grass and terrain still provide the camera depth that
the volume pass reads. Ocean rejects faces behind the nearest water depth.

### The mesh and its vertex-color data channel

`WaterMeshBuilder` (`Assets/Scripts/Planet/WaterMeshBuilder.cs`) builds one spherical
water mesh per world from the wet cells of the 6 cube faces (`MeshData` is documented
"Safe to produce on a background thread via `Compute`" — the Awaitable background
pattern). Its load-bearing output is **vertex color as a data channel**:
`r = depth01, g = shore01, b = body01 (pond↔ocean), a = temperature01`. Both the ocean
vertex stage and the volume prepass decode exactly this layout. Change all consumers
when this contract changes. The current builder emits one water mesh. `BuildStats`
(bodies, frozen bodies, max depth) feed the water debug module.

Horizontal river meshes share `PlanetWaterSurface.SurfaceMaterial`. Their colour alpha is 2,
which identifies river geometry; `ComputeWaterMeshDisplacement` clamps the temperature input.
The helper suppresses large swell upstream and ramps it into receiving tails.
Both visible and prepass shaders call that helper. Waterfall sheets use `Planet/River` separately.
Directional river ripples modify the shared surface normal, not its base tint.

The standing-water prepass excludes higher lake cover within lower river banks through
`RiverBankBelow`. Where the solved field contains standing water, only the channel excludes it.
The visible Ocean pass rejects pixels without a prepass owner. Segment projection and width
helpers are shared with `SampleRiver`; the exclusion uses radius before reach smoothing.
See `docs/design/2026-09-10-river-bank-overlap.md` for validation and the remaining lake-cover fringe.

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

### The volume prepass and current boundary coverage

`WaterVolumeRenderFeature` enqueues two passes at `BeforeRenderingTransparents`:

1. **Prepass** (`Hidden/WaterVolumePrepass`): draws the water mesh into an off-screen
   `R16G16B16A16_SFloat` target ("WaterVolumeData"). It encodes
   `(forwardDepth, depth01, packedShoreKind, freezeFactor)`, where
   `packedShoreKind = round(shore01 * 511) * 4 + kind`. The pass publishes the target as
   `_WaterVolumeData` and `_WaterInterfaceTexture`.
   A private depth attachment selects the nearest displaced face. The prepass rejects
   fragments behind opaque scene depth explicitly. It publishes `_WaterSurfaceDepth`
   for the Ocean pass, without changing camera depth. This replaces radial backface clipping.
   Ocean compares the sampled device depth with fragment `SV_POSITION.z`. Do not compare
   linearized device depth with eye depth rebuilt from interpolated world position:
   that precision mismatch cut valid horizon pixels in the 2026-09-08 shore regression.
2. **Composite** (`Hidden/WaterVolume`): fullscreen triangle that reads scene color +
   depth + the prepass target and rewrites `cameraColor`.

The current dirty tree has no `WaterVolumeLip` mesh or relaxed prepass. The feature draws
the primary water mesh and registered horizontal river meshes, including receiving tails.
Tail fragments require standing water at the receiving height. Packed kind bit 0 selects
lake/ocean optics; bit 1 identifies river geometry. Ocean rejects the other geometry type
selected by the prepass, preventing river/lake double composition. Gameplay body identity is unchanged.
`MeasuredWaterColumn` measures radial separation from the visible surface to the opaque receiver.
It does not sample standing-shore levels, which cannot describe elevated rivers.
Boundary coverage therefore depends on the visible depth and packed prepass data.
`WaterVolume.shader` and `Atmosphere.shader` both
derive coverage from depth through `WaterVolumeCoverage`, using the decoded kind to select the fade distance.
Atmosphere samples exact interface coverage. Neighbor dilation previously suppressed
sky lighting outside the water mesh and drew a dark horizon outline.
Change the shoreline depth stamp and packed kind as one contract. The composite has a debug-only no-depth
underwater fallback (`UnderwaterNoDepthColor`) and an orbital fade
(`VolumeLayerVisibility`).

### Caustics — editable since 2026-08-11, still fragile

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

**CLAUDE.md rule: the don't-touch prohibition was lifted 2026-08-11 (Bryan).** Caustics
are editable, and they remain fragile — historically most touches broke them, which is why
the prohibition existed. Change deliberately, one variable at a time, and verify caustics,
shoreline and depth blend before committing. Treat the caustic functions, their constants
(`CAUSTIC_SCALE 0.075`, `CAUSTIC_SPEED 1.05`), and the feature-level tuned values
(`CausticIntensity 0.42` etc. in `WaterVolumeRenderFeature`) as hand-tuned: they are
changeable but every one of them was landed by eye, so re-verify visually. Debug views
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
(`SamplePrecipitationDensity`): two broad value-noise scales and a wind-sheared sample
position (more shear near the ground). Fine radial noise was removed on 2026-09-08
because the step budget could not resolve its horizontal layers. Per-step opacity uses
exponential extinction. The march integrates the full fog column instead of stopping
at the curtain opacity cap. Each jittered sample stays inside its ray segment.
The march clips against the sea-horizon sphere so
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
As of 2026-09-08, they explicitly apply daylight, storm dimming, and lightning.
Visibility uses a smooth rank threshold, a broad distance fade, and a half-second
birth fade. Rain and persistent snow share the altitude fade. The curtain's local
exclusion radius shrinks with that fade. Water crossings use `WaterPresentationController`
immersion instead of a binary still-water cutoff. Impacts remain fixed at collision contacts.

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
# Primary-mesh-only prepass and packed boundary data
grep -n "Setup(_prepassMaterial\|shoreBody" Assets/Scripts/Planet/WaterVolumeRenderFeature.cs Assets/Graphics/Shaders/WaterVolumePrepass.shader
grep -n "WaterCoverageFromData\|WaterInterfaceCoverage" Assets/Graphics/Shaders/WaterVolume.shader Assets/Graphics/Shaders/Atmosphere.shader
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
