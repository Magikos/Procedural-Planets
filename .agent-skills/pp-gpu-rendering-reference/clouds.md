# Volumetric Clouds — every term and why it exists

Part of `pp-gpu-rendering-reference`. Verified against the working tree 2026-07-06.
Primary files: `Assets/Graphics/Shaders/Cloud.shader` (fullscreen march),
`Assets/Graphics/Shaders/Includes/CloudShadows.hlsl` (ground shadows),
`Assets/Scripts/Planet/Clouds/CloudController.cs` (globals + LOD),
`Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs` (URP pass),
`Assets/Scripts/Planet/Clouds/CloudConstants.cs` (tuned constants — many cloud knobs are
code constants, not settings).

Lineage: the renderer descends from Sebastian Lague's cloud project, which descends from
Schneider/Nubis (Horizon Zero Dawn, SIGGRAPH 2015/2017); the 2026-07 migration
back-filled the lighting terms the skeleton was missing, guided by
`docs/research/2026-07-04-cloud-visual-research.md` (absorbed below).
**Status as of 2026-07-06** (per `docs/design/2026-07-04-cloud-visual-migration-plan.md`):
Phases 0–1 landed and capture-verified; Phase 2 lighting (Beer-Powder, multi-scatter
octaves, two-tone ambient) is **coded but its capture comparison/retune is pending** —
do not "fix" those terms independently of the campaign (`pp-visual-migration-campaign`).
Phase 3 (aerial perspective) and Phase 4 (cloud-type profiles) are NOT in the shader yet;
the research doc's line numbers are stale — trust identifier names.

## 1. Pass structure

`CloudRenderFeature` enqueues one RenderGraph raster pass ("CloudEffect") at
`BeforeRenderingPostProcessing + 1` (immediately after atmosphere — clouds write no
depth, so running after prevents atmosphere from fogging them with terrain depth). It
draws a fullscreen triangle with `Hidden/Clouds`, reading `cameraColor` as `_Source` and
writing a new `CameraColor-Clouds` target that replaces `resourceData.cameraColor`.
Final composite (last line of `frag`):

```hlsl
float3 result = sceneColor.rgb * transmittance + lightEnergy;
```

i.e. classic front-to-back accumulation: the scene is attenuated by total cloud
transmittance, plus the accumulated in-scattered light.

## 2. Ray setup — a march through a spherical shell

The cloud layer is the shell between `_CloudInnerRadius = seaLevel + BaseAltitude` and
`_CloudOuterRadius = inner + LayerThickness` (set in
`CloudController.EnsureStaticPropertiesUploaded`; `cloud.altitude` range 20–1000 m,
`cloud.thickness` 50–1000 m via console).

Per pixel (`frag` in Cloud.shader):

1. Reconstruct the world-space ray from `unity_CameraInvProjection` (vertex stage).
2. `RaySphere` (analytic ray-sphere intersection, `Includes/Math.hlsl`) against the
   outer shell → `[startDistance, endDistance]`.
3. Clamp `endDistance` by scene depth AND by an ocean-sphere hit (`_SeaLevelRadius`) —
   the ocean surface is transparent and absent from the depth buffer, so without the
   explicit sphere test clouds would draw *through* the water.
4. If the camera is under the inner shell, start the march at the inner-shell **exit**
   (`innerHit.x + innerHit.y`) — you see clouds only above your head, through the gap.

## 3. Density: `SampleCloud`

Order matters — each stage can early-out before paying for the next:

1. **Shell + weather gate.** Outside the radii → zero. Sample the weather cube map
   (`SampleWeather(direction)` from `Includes/WeatherSampling.hlsl`; r=condensation,
   g=storm, b=moisture-source, a=condensation-delta). `condensation <= 0.001` → zero.
   The weather grid is the *single source of truth*: clouds exist only where the sim
   says so (contract details → `pp-weather-sim-reference`).
2. **Wind advection.** The noise sample position is rotated back around
   `cross(direction, _WindDirection)` by `-_CloudWindAngle` so the procedural shape
   flows with the wind (same tangent convention as grass and weather particles).
   Rotating the *sample position* instead of scrolling a UV keeps this correct on a sphere.
3. **Shape FBM.** `_CloudShapeNoise` is a baked 3D texture (RGBA = 4 octaves, generated
   by `CloudNoiseGenerator` at world load), combined by `WeightedNoise(noise,
   _CloudShapeWeights)` — a normalized dot product so re-weighting octaves never changes
   overall brightness.
4. **Vertical envelope.** `bottomFade * topFade` (smoothsteps over `height01` within the
   shell, feather widths `_CloudBottomFeather`/`_CloudTopFeather`) plus `topBias` (a
   height-pow term) biasing density upward. WHY: without it clouds fill the shell like a
   uniform fog slab with hard flat top/bottom. (Phase 4 will replace this single
   envelope with per-cloud-type profiles — stratus/cumulus/cumulonimbus driven by
   weather — so storms *tower*; not yet implemented.)
5. **Threshold + sharpness.** `density = saturate((cloudShape - _CloudDensityThreshold)
   * _CloudShapeSharpness)` — carves distinct cloud bodies out of continuous noise.
   `density <= 0.0001` → **return before the detail fetch** (the Phase-1 "detail
   early-out": empty air costs one 3D sample, not two).
6. **Detail erosion.** `edgeErosion = (1 - detailFBM) * (1 - density) * _CloudDetailWeight`
   — erosion is strongest where density is *low*, i.e. at cloud edges, turning smooth
   threshold boundaries into billows. Skipped entirely under `CLOUD_QUALITY_LOW`
   (`detailFBM` hardcoded 0.5).

## 4. Lighting: why each term exists

Inside the view loop, only when `cloud.density > 0.0001`:

### Beer-Lambert + the early-exit

```hlsl
lightEnergy += cloud.density * stepSize * transmittance * lighting;
transmittance *= exp(-cloud.density * stepSize * _CloudLightAbsorption);
if (transmittance < 0.01) break;   // opaque enough - stop marching
```

Transmittance-weighted accumulation = near samples occlude far samples. The `0.01`
break is the "high-opacity early-out".

### Light march (`LightMarch`)

From each lit view sample, march up to `min(_CloudLightSteps, CLOUD_LIGHT_STEPS_MAX)`
jittered steps toward `_SunParams.xyz`, accumulating `lightDensity` (density × step
length). This is the self-shadowing term: sun-facing surfaces bright, cores and
undersides dark. `sunVisibility = smoothstep(-0.55, 0.35, dot(surfaceNormal, sunDir))`
kills direct light past the terminator (planet-scale day/night, generous twilight).
Per-step hash jitter (scaled by `_CloudRayOffsetStrength`) trades banding for noise.

### Beer-Powder (`CloudBeerPowder`) — the "carved" look

```hlsl
float beer = exp(-lightDensity * _CloudLightAbsorption);
float powder = 1.0 - exp(-lightDensity * 2.0);
float powderMix = saturate(_CloudPowderStrength * saturate(cosAngle));
return _CloudDarknessThreshold + beer * lerp(1.0, powder, powderMix) * (1.0 - _CloudDarknessThreshold);
```

WHY (research doc §1, Nubis 2015): real sunlit cumulus shows **dark creases between
bulges on the sun-facing side** ("powdered sugar"): light entering a low-density region
in-scatters away before returning to the eye. Plain Beer can only *brighten* thin
regions, never darken them. The powder term multiplies darkness back in at low
`lightDensity`, gated to sun-facing geometry via `cosAngle` (= `dot(rayDir, sunDir)`).
`_CloudDarknessThreshold` floors the result so nothing goes pure black.

### Multi-scatter octaves (`CloudMultiScatter`) — luminous storm cores

```hlsl
float scatter = directLight * CloudPhaseOctave(cosAngle, 1.0);
[unroll] for (int octave = 1; octave < 3; octave++) {
    float octaveTransmittance = exp(-lightDensity * _CloudLightAbsorption * attenuation) * sunVisibility;
    scatter += strength * contribution * octaveTransmittance * CloudPhaseOctave(cosAngle, phaseScale);
    attenuation *= _CloudMultiScatterParams.x;  // a^o
    contribution *= _CloudMultiScatterParams.y; // b^o
    phaseScale *= _CloudMultiScatterParams.z;   // c^o
}
```

WHY (research doc §2, Oz 2013 / Frostbite 2016): in thick clouds most light arriving at
the eye has scattered many times. Single scattering makes storm cores flat black. Each
octave re-evaluates the SAME marched `lightDensity` with attenuation, contribution and
phase eccentricity all reduced by constant factors — approximating higher scattering
orders as "a dimmer, more diffuse copy of the light". Cost: pure ALU, zero extra texture
samples. The phase function is dual-lobe Henyey-Greenstein (`CloudPhaseOctave`: forward
lobe `_CloudPhaseParams.x`, back lobe `.y`, base brightness `.z`, strength `.w`).

### Two-tone ambient

```hlsl
float3 ambient = lerp(_CloudAmbientGround.rgb, _CloudAmbientSky.rgb, cloud.height01)
    * (_NightAmbientIntensity * 0.25 + ambientStrength);
```

WHY (research doc §3): clouds are lit from above by blue sky and from below by
warmer/darker ground bounce. A scalar ambient makes undersides uniformly gray;
height-lerped colors make them read grounded. `ambientStrength` itself lerps down to 22%
on the night side (`localSun` factor).

### Storm/rain gloom

`gloom = WeatherCloudGloomFromRain(storm, rainSignal) = max(storm, rainRate-gated
signal)` (`Includes/WeatherSampling.hlsl`). It drives: albedo lerp `_CloudColor →
_CloudStormColor`, direct-light multiplier `1 - _CloudStormDarkening * gloom`, and the
storm shadow boost on the ground. WHY the max(): rain-heavy cells must gloom even at
moderate storm values, and the SAME formula feeds `CloudShadows.hlsl` so sky darkening
and ground shadow track the same cells (the Phase-0 "unify gloom" fix — keep them in
lockstep if you touch either).

### Silver lining

```hlsl
float rimKeep = saturate(1.0 - gloom * _CloudSilverLiningParams.w);
float silverLining = _CloudSilverLiningParams.x * forwardSun * thinEdge
    * lightTransmittance * horizonSun * rimKeep;
```

WHY: strong forward scattering through optically thin edges = the bright rim when a
cloud occludes the sun. `thinEdge = pow(1 - density01, edgePower)` restricts it to thin
geometry; `rimKeep` *dims but never kills* the rim on storm clouds — dark storm clouds
keep a silver lining where they're thin (comment in code).

### Lightning

`WeatherLightning(surfaceNormal, storm)` (`Includes/WeatherLightning.hlsl`): up to 4
active lightning cells as global vec4s (direction + intensity), masked by storm level
and angular proximity, added as emissive scaled by height and density.

## 5. Anti-banding: blue noise + per-step jitter

Raymarching with few steps produces banding (visible shells at step boundaries). Fixes,
in order of application (`frag`):

1. `pixelJitter` = one sample of `_CloudBlueNoise` (`BlueNoise.png`, bound with texel
   size by `CloudController`; per-pixel, framerate-independent).
2. Per-step `withinStep` hash (`Hash12`) seeded from pixel + step + blue noise, lerped
   by `_CloudRayOffsetStrength` — decorrelates neighbor steps.
3. The light march applies its own start jitter + per-step jitter at 0.35 strength.

WHY blue noise for #1: its error spectrum is high-frequency only, which the eye (and any
later filtering) discards — the same jitter amplitude with white noise reads as blotches.
Verified by A/B captures 2026-07-05 (`20260705-051115/051118`, migration plan Phase 1).

## 6. Step budgets, quality tiers, altitude LOD

```hlsl
#ifdef CLOUD_QUALITY_LOW
    #define CLOUD_MAX_STEPS 8
    #define CLOUD_LIGHT_STEPS_MAX 3
#else
    #define CLOUD_MAX_STEPS 96
    #define CLOUD_LIGHT_STEPS_MAX 16
#endif
```

Runtime view steps = `_CloudViewSteps` clamped to the compile-time cap. The value is
computed per-frame in `CloudController.UpdatePerFrameProperties`:
settings `ViewSteps` lerped down to `MinViewSteps` by camera altitude
(`StepScaleNearAltitude`..`StepScaleFarAltitude`), then multiplied by
`QualityController.CloudStepMultiplier` (1.0 / 0.65 / 0.33), uploaded only when changed.
`CLOUD_QUALITY_LOW` is a `multi_compile` keyword enabled globally by `QualityController`
on Low-tier quality names; it also drops the detail-noise fetch and shrinks the
precipitation march (`PRECIPITATION_MAX_STEPS` 48 → 8).

Temporal accumulation / quarter-res reprojection was **built and reverted** (artifacts
rejected by Bryan). If step budget becomes the wall again, the research doc §8 records
the specific recipe to try (Frostbite 4×4 update pattern), not a re-derivation.
Don't reintroduce the EMA-blend variant.

## 7. Cloud shadows on the ground (`CloudShadows.hlsl`)

Terrain/grass/water shaders can't afford the view march, so `CloudShadowFactor(worldPos,
sunDir, localSun)` recomputes a *cheap proxy* of the same density:

- 3 fixed samples (`[unroll]`, midpoints of thirds) along the sun ray's traversal of the
  cloud shell from the receiver point.
- Density = condensation × shape FBM only — **no detail noise, no vertical envelope** —
  matching the same wind advection so shadows track moving clouds.
- `gloom` boosts storm shadows (`_CloudShadowParams.z`), smoothstep softness `.y`,
  master strength `.x`, and a horizon fade `.w` (shadows fade out at grazing sun where
  the 3-sample march gets degenerate).
- Consumers: `Grass.shader` (per-fragment as of 2026-07-06 — the grass migration Phase 1
  plans to move it to the vertex stage), `Ocean.shader`, `WaterVolume.shader` (all
  include `CloudShadows.hlsl`). `_WaterFocusMode` disables it for water debug isolation.

Keep `SampleCloudShadowDensity` and `SampleCloud` structurally in sync when editing
either — density drift between sky and ground is audit finding D2 territory and Phase 4
explicitly requires a shared helper before touching the vertical profile.

## 8. Debug modes

`_CloudDebugMode` 1–9 (weather, storm, density, optical depth, silver lining, moisture,
condensation delta, rain rate, weather precipitation signal) render false-color overlays
accumulated as max() along the ray — set via `cloud.debug-mode`; enum
`CloudDebugState.View`. These are the stage-ownership proof tools: e.g. mode 1
(`CloudWeather`) proved the historical seam lived in the weather field, not lighting.

## Provenance and maintenance

```
grep -n "CloudBeerPowder\|CloudMultiScatter\|_CloudAmbientSky" Assets/Graphics/Shaders/Cloud.shader
grep -n "CLOUD_MAX_STEPS\|CLOUD_LIGHT_STEPS_MAX" Assets/Graphics/Shaders/Cloud.shader
grep -n "BeforeRenderingPostProcessing + 1" Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs
grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Includes/CloudShadows.hlsl Assets/Graphics/Shaders/Grass.shader
grep -n "Phase 2" docs/design/2026-07-04-cloud-visual-migration-plan.md   # migration status
```

Technique rationale absorbed from `docs/research/2026-07-04-cloud-visual-research.md`
(Nubis/Frostbite/Oz citations live there); implementation claims verified against
`Cloud.shader` directly since the research doc predates the Phase-2 landing.
