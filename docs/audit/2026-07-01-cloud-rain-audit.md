# Audit 2026-07-01 — Clouds & rain: dither, storm look, rain read

- **Commit audited:** `ec0b1cd` (branch `code-refactor`)
- **Status:** IMPLEMENTED 2026-07-01 (Bryan approved; Codex feedback amendments applied). W1 (no-history jitter only — temporal + `_CloudMaxDistance` skipped: max shell chord ≈3.7 km here, a cap never engages), W2 incl. the CloudShadows gloom match (Codex amendment), W3, W4 (knobs in `PrecipitationDto` + `precipitation.fog`/`precipitation.haze`), W5 incl. the gate split + lightning port (Codex amendments), W6a/b/c. Part 2 direction items not built. Needs in-Unity visual verification.
- **Priority focus (from Bryan):** dithered cloud look; far rain needs a better visual read; close rain via particles; rain-intensity-driven fog inside the rain volume; darker rain clouds that keep a silver lining / inner light glow; any other ideas to make rain feel better.
- **Companion report:** [2026-07-01-grass-lod-audit.md](2026-07-01-grass-lod-audit.md) (general findings there — `[DefaultExecutionOrder]`, per-frame service resolution patterns — apply to this subsystem too and are cross-referenced, not re-listed).
- **Scope not audited:** weather *simulation* (WeatherEvolution.compute, SphericalWeatherGrid, WeatherManager scheduling) beyond how its outputs are consumed; atmosphere scattering internals; snow/dust profiles except where they share rain code.

---

## Part 0 — The weather rendering stack as it ships

Five renderers consume two cube-map data channels:

- **Weather map** (`_CloudWeatherMap`): `r` = condensation (cloud cover), `g` = storm, `b` = moisture source, `a` = condensation delta.
- **Dynamics map** (`_WeatherDynamicsMap`): `b` = **rain rate** (the "how heavy is it raining here" signal).

| Renderer | What | Pass timing | Key defaults |
|---|---|---|---|
| `Cloud.shader` (fullscreen ray march) | volumetric cloud shell | before post | 48 view steps (24 min), 4 light steps, `RayOffsetStrength = 1.1`, `DensityMultiplier = 0.018` |
| `CloudShadows.hlsl` (per-material include) | 3-tap cloud shadow on surfaces | in surface shaders | strength 0.35, storm boost 1.35 |
| `Precipitation.shader` (fullscreen ray march) | distant rain curtains | before post | 32 steps, `MaxOpacity = 0.38`, `Intensity = 1.15` |
| `WeatherParticles.shader` passes Dust/Snow/**Rain** | tile-anchored local particles | before post | rain: 1,800 instances, fall speed **16 m/s**, color = `RainColor` (0.36, 0.44, 0.50) |
| `RainParticles.shader` + `RainParticleController` | physical raindrop streaks | **after** post | **30,000** instances, fall speed **200 m/s**, color (0.92, 0.95, 1.0) |

Note the last two rows: **two separate close-rain particle systems render simultaneously** (finding W5).

---

## Part 1 — Findings

### [W1] The dithered cloud look: full-strength white-noise jitter at every level, with no resolve

- **Evidence:**
  - `RayOffsetStrength` defaults to **1.1** ([CloudSettings.cs:29](../../Assets/Scripts/Planet/Clouds/CloudSettings.cs#L29)), saturated to 1.0 in the shader — jitter runs at maximum.
  - Per-pixel jitter deliberately blends 70 % toward white noise: `pixelJitter = lerp(InterleavedGradientNoise(...), Hash12(pixel), 0.7)` ([Cloud.shader:288-291](../../Assets/Graphics/Shaders/Cloud.shader#L288-L291)). IGN's whole value is its even spatial distribution; mixing it 70 % toward `Hash12` white noise throws that away.
  - Each view step then re-randomizes its own sample position with *independent* white noise: `stepNoise = Hash12(pixel + float2(stepF * 17.17 …))`, `withinStep = lerp(0.5, stepNoise, jitterStrength)` ([Cloud.shader:313-315](../../Assets/Graphics/Shaders/Cloud.shader#L313-L315)) — at strength 1.0 every step lands anywhere in its stratum, decorrelated from its neighbors.
  - The light march adds two more layers of white-noise jitter per lit sample ([Cloud.shader:186-201](../../Assets/Graphics/Shaders/Cloud.shader#L186-L201)).
  - There is no temporal accumulation and no spatial post-filter — the pass composites straight to camera color at full resolution ([CloudRenderFeature.cs:135-157](../../Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs#L135-L157)), and the noise has no time input, so the speckle pattern is *frozen* on screen. Static per-pixel hash error is exactly what reads as "dither."
  - No march-distance cap: `endDistance` is the full shell chord clipped only by scene depth ([Cloud.shader:272-296](../../Assets/Graphics/Shaders/Cloud.shader#L272-L296)). At grazing angles through a 300 m shell on a ~5,300 m planet the chord is kilometers long, so 48 steps → very large `stepSize` → each pixel's estimate is a high-variance lottery → more speckle exactly at the horizon where clouds are most visible.
- **Impact:** the signature grainy/dithered cloud edge everywhere, worst near the horizon and at altitude (view steps LOD down to 24, `CloudStepMultiplier` can drop further).
- **Effort:** S for the jitter restructure; M if the half-res + upsample option is taken. **Risk:** LOW — pure sampling change, same data. **Confidence:** HIGH.
- **Fix sketch, in order of payoff-per-line:**

**1. One stratified offset per pixel (IGN), not white noise per step.** The standard volumetric pattern: jitter the whole ray once, keep strata aligned.

```hlsl
// Cloud.shader frag — replace lines 287-291 and the per-step jitter:
float2 pixel = floor(i.uv * _ScreenParams.xy);
float pixelJitter = InterleavedGradientNoise(pixel);   // pure IGN, no Hash12 mix
float jitterStrength = saturate(_CloudRayOffsetStrength);

...
UNITY_LOOP
for (int s = 0; s < viewSteps; s++)
{
    // was: stepNoise = Hash12(...); withinStep = lerp(0.5, stepNoise, jitterStrength)
    float withinStep = lerp(0.5, pixelJitter, jitterStrength);
    float marchDistance = min(startDistance + stepSize * ((float)s + withinStep), endDistance);
    ...
```

Neighboring pixels still sample at IGN-distributed phases (edges stay soft, no banding), but each ray is internally coherent — the salt-and-pepper decorrelation disappears. Do the same in `LightMarch`: one IGN-derived start offset, fixed per-step positions (delete the `perStepJitter` block at lines 195-201).

**2. Cap and fade the march distance.** Add a `_CloudMaxDistance` (e.g. 30,000 m, console-tunable) and fade transmittance-influence to zero over the last 20 %: shorter chords → smaller `stepSize` → less variance at the horizon, plus a free perf win. Mirrors what `Precipitation.shader` already does with `_PrecipitationRadii.z`.

**3. (Optional, biggest lift) Half-resolution cloud buffer + depth-aware upsample.** Render the march into a half-res target, bilateral-upsample against depth. Halves the cost (≈4× fewer rays), and the upsample averages residual noise. This is a render-feature change (new RT + second material pass) — only take it if 1+2 don't satisfy. If 1+2 land first, evaluate on an F10 capture before deciding.

**4. (Optional) Temporal sub-pixel rotation.** `pixelJitter = frac(IGN(pixel) + (frameIndex & 7) * 0.618034)` hides remaining structure when motion is slow — but without TAA/accumulation it turns static grain into crawling grain; only pair with option 3's upsample blur or leave out.

Verification: `cloud.density`, F10 captures at surface / 500 m / horizon-grazing angles; `CLOUD_QUALITY_LOW` keyword forced via `quality.set` to check the 8-step tier too.

---

### [W2] Rain clouds don't darken with rain, and the silver lining is explicitly suppressed on exactly the clouds that should keep it

- **Evidence:**
  - Cloud albedo and darkening key **only** off the storm channel: `cloudAlbedo = lerp(_CloudColor, _CloudStormColor, cloud.storm)`, `stormLight = lerp(1.0, 1.0 - _CloudStormDarkening, cloud.storm)` ([Cloud.shader:333-334](../../Assets/Graphics/Shaders/Cloud.shader#L333-L334)). The rain rate (`dynamics.b`) is sampled in the lit loop **only to feed a debug view** ([Cloud.shader:354](../../Assets/Graphics/Shaders/Cloud.shader#L354)). A cell can be raining hard at storm ≈ 0.5 and its cloud barely darkens.
  - Silver lining is multiplied by `stormSuppression = 1 - storm * 0.85` (`SilverLiningStormSuppression = 0.85`, [CloudConstants.cs:55](../../Assets/Scripts/Planet/Clouds/CloudConstants.cs#L55), applied at [Cloud.shader:343-345](../../Assets/Graphics/Shaders/Cloud.shader#L343-L345)) — at full storm the rim light is cut to 15 %. That is the opposite of the requested look (dark cloud, bright rim).
  - There is no multi-scattering / inner-glow term: lighting is single-scatter Beer-Lambert plus flat ambient ([Cloud.shader:335-337](../../Assets/Graphics/Shaders/Cloud.shader#L335-L337)); thick storm clouds go uniformly flat-dark instead of dark-with-glowing-core.
- **Impact:** rain regions don't read from a distance (you can't see where it's raining by looking at the clouds), and storm clouds lose the one highlight that makes dark clouds look volumetric instead of gray-flat.
- **Effort:** S-M (shader-local, all knobs already flow through `CloudConstants`/DTO). **Risk:** LOW-MED — retunes the hero cloud look; keep old constants until captures approve. **Confidence:** HIGH.
- **Fix sketch** (inside the `cloud.density > 0.0001` block; one extra dynamics sample per lit step — see W6a, it's already being paid for by the debug line):

```hlsl
// Rain darkens the cloud body independently of storm classification.
float rainRate = SampleDynamics(surfaceNormal).b;            // replaces the debug-only sample
float gloom = max(cloud.storm, rainRate * 0.85);             // rain counts as darkening

float3 cloudAlbedo = lerp(_CloudColor.rgb, _CloudStormColor.rgb, gloom);
float stormLight = lerp(1.0, 1.0 - _CloudStormDarkening, gloom);

// Silver lining: suppress by how deep into the cloud body we are (transmittance),
// NOT by storminess — dark clouds keep a bright rim where they are optically thin.
// was: stormSuppression = saturate(1.0 - cloud.storm * _CloudSilverLiningParams.w);
float rimKeep = lerp(1.0, 0.55, gloom);                      // dim, don't kill (tune 0.4-0.7)
float silverLining = _CloudSilverLiningParams.x * forwardSun * thinEdge
    * lightTransmittance * horizonSun * rimKeep;

// Inner glow: cheap multi-scattering approximation. Where direct light is heavily
// absorbed (thick cloud), a soft second lobe leaks through — dark-but-luminous core.
float multiScatter = pow(saturate(lightTransmittance), 0.25) * (1.0 - lightTransmittance);
lighting = cloudAlbedo * (lightTransmittance * phase * stormLight
    + multiScatter * 0.4 * stormLight
    + ambient);
```

Constant changes to pair with it: `SilverLiningStormSuppression` becomes the `rimKeep` floor (rename it — its meaning inverts), and consider dropping `StormColor` darker (e.g. (0.22, 0.24, 0.28)) once `gloom` includes rain, since rain-heavy cells will now reach it more often. Verify with `cloud.debug-mode` 2 (storm) vs 8 (rain rate) side-by-side against the lit view, plus a sunset capture for the rim.

---

### [W3] Far rain read: curtains are faint, contrast-free, and don't scale visually with rain intensity

- **Evidence:**
  - Final opacity is hard-capped at `MaxOpacity = 0.38` ([PrecipitationController.cs:32](../../Assets/Scripts/Planet/PrecipitationController.cs#L32)) and the march breaks as soon as it's reached ([Precipitation.shader:324-325](../../Assets/Graphics/Shaders/Precipitation.shader#L324-L325)) — a drizzle and a monsoon converge to the same 0.38-alpha veil; heaviness only changes how *fast* the cap is hit, which the eye can't see.
  - Rain color is a flat mid-gray lit by camera-position sun only ([Precipitation.shader:345-350](../../Assets/Graphics/Shaders/Precipitation.shader#L345-L350)); there's no darkening of the scene behind the curtain, so curtains lack the contrast edge that makes real rain shafts legible against sky and terrain.
  - The curtain shape is isotropic value noise ([Precipitation.shader:187-192](../../Assets/Graphics/Shaders/Precipitation.shader#L187-L192)) — `fineBreakup` and `heightBreakup` modulate density ±15-25 % but nothing produces the *vertical streaking* signature of rain shafts (the `heightBreakup` noise is sampled at frequency 43 across the full layer height, closer to horizontal banding than vertical streaks).
- **Impact:** distant rain reads as "slightly gray fog patch," not "rain over there," and light vs heavy rain are indistinguishable at range — undermining the weather sim the whole stack exists to visualize.
- **Effort:** M. **Risk:** LOW-MED (tuning-heavy; debug modes 1-3 and the contribution heat view already exist for verification). **Confidence:** HIGH on mechanisms, MED on exact tuning values.
- **Fix sketch:**

```hlsl
// 1. Let heaviness raise the ceiling: opacity cap scales with the strongest
//    rain rate encountered along the ray (accumulate it in the loop).
float peakRain = 0.0;                       // in-loop: peakRain = max(peakRain, rainRate);
float opacityCap = _PrecipitationParams.w * lerp(0.6, 1.9, saturate(peakRain));
alpha = min(alpha, saturate(opacityCap));

// 2. Anisotropic curtain noise: stretch the fine noise vertically so shafts streak.
//    Replace the fineBreakup line (Precipitation.shader:191):
float2 streakUv = float2(dot(local, float2(1.0, 0.0)) / max(curtainScale * 0.16, 8.0),
                         height01 * 2.2 - _GameTime * _PrecipitationVisualParams.w * 0.02);
float fineBreakup = ValueNoise(streakUv);   // low horizontal, high vertical frequency

// 3. Contrast: rain extinction darkens the scene behind the curtain before the
//    rain color is composited (double duty with the W4 fog accumulation).
sceneColor.rgb *= lerp(1.0, 0.72, alpha * saturate(averageStorm + peakRain * 0.6));
```

Also tie the per-sample alpha rate to rain rate quadratically rather than linearly (`density` already multiplies `rainRate`, but the `0.0048` scalar at [Precipitation.shader:318](../../Assets/Graphics/Shaders/Precipitation.shader#L318) flattens it; try `* lerp(0.5, 1.6, rainRate)`), so heavy cells build opacity visibly faster. Verify with `precipitation.debug-mode` RainDots vs the lit view, and the `DEBUG_PRECIPITATION_CONTRIBUTION` heat view for before/after deltas.

---

### [W4] Rain-volume fog scaled by rain heaviness (requested feature — nothing implements it today)

- **Evidence:** the precipitation pass composites curtains but leaves scene color otherwise untouched (no extinction term, [Precipitation.shader:350](../../Assets/Graphics/Shaders/Precipitation.shader#L350)); no other system applies weather-driven fog (`MixFog` in surface shaders is stock URP fog, unaware of rain).
- **Impact:** standing inside heavy rain looks like standing in clear air with streaks in it — the single biggest missing "it is raining" cue at ground level.
- **Effort:** M. **Risk:** MED — touches every pixel when raining; needs care not to fight atmospheric scattering (which composites *after* this pass) or double-fog the sky. **Confidence:** HIGH that it's absent; design below is the recommended shape.
- **Fix sketch — two terms, both in `Precipitation.shader`'s existing march (no new pass):**

```hlsl
// (a) Through-the-volume fog: accumulate optical depth along the ray even after
//     the curtain alpha cap is hit (today the loop breaks — keep marching cheaply
//     or accumulate pre-break; the depth integral is what fog needs).
float rainOpticalDepth = 0.0;
// in-loop, before the alpha-cap break:
rainOpticalDepth += density * stepSize;

// after the loop:
float fogAmount = 1.0 - exp(-rainOpticalDepth * 0.0009);      // extinction, tune k
float3 fogColor = lerp(_PrecipitationColor.rgb, _PrecipitationStormColor.rgb,
                       saturate(averageStorm)) * light;
sceneColor.rgb = lerp(sceneColor.rgb, fogColor, fogAmount * cameraAboveSea);

// (b) Camera-inside-rain haze: when the camera itself is in a raining cell, add a
//     near-field veil driven by the LOCAL rain rate — this is the "heaviness" knob.
float3 cameraNormal = normalize(rayOrigin - _PrecipitationPlanetCenter);
float cameraRadius = length(rayOrigin - _PrecipitationPlanetCenter);
float inColumn = step(bottomRadius, cameraRadius) * step(cameraRadius, topRadius);
float localRain = SampleDynamics(cameraNormal).b * inColumn;
float hazeStrength = smoothstep(0.15, 0.9, localRain) * 0.22;  // max 22% veil at downpour
float hazeByDepth = 1.0 - exp(-min(sceneDepth, 800.0) * 0.004); // near-field ramp, ~800m
sceneColor.rgb = lerp(sceneColor.rgb, fogColor, hazeStrength * hazeByDepth * cameraAboveSea);
```

Order matters: apply (a)/(b) to `sceneColor` *before* the curtain composite so shafts stay visible inside the fog. Term (b) is the direct answer to "more fog the heavier the rain": `localRain` **is** `dynamics.b`. Expose `hazeStrength`'s max and the two extinction constants through `PrecipitationDto` + `precipitation.*` console commands (matching the existing settings pattern) so the feel is tunable live. Sky safety: both terms scale by depth/optical thickness, so pixels with sky depth get the full-volume term only where the ray actually crossed rain.

---

### [W5] Two overlapping close-rain particle systems ship enabled, with contradictory physics

- **Evidence:**
  - `WeatherParticles.shader` pass "Rain" (1,800 tile-anchored streaks, fall speed = `FallSpeed` **16 m/s**, color `RainColor` (0.36, 0.44, 0.50)) draws in the precipitation pass before post ([PrecipitationRenderFeature.cs:226-230](../../Assets/Scripts/Planet/PrecipitationRenderFeature.cs#L226-L230); profile at [WeatherParticles.shader:223-233](../../Assets/Graphics/Shaders/WeatherParticles.shader#L223-L233)).
  - `RainParticleController` (30,000 physical drops, `FallSpeedMps` **200 m/s**, near-white (0.92, 0.95, 1.0)) draws after post ([PrecipitationRenderFeature.cs:251-309](../../Assets/Scripts/Planet/PrecipitationRenderFeature.cs#L251-L309)).
  - Both are gated by the same `ShouldRenderLocalParticles` + rain thresholds, so in local rain both render at once: two rain fields with a **12.5×** fall-speed disagreement and different colors, one tinted by atmosphere and one not.
  - Vestige confirming the migration was left half-done: `ProfileProofVisibility` still special-cases a "distant rain (profile 3)" ([WeatherParticles.shader:92-94](../../Assets/Graphics/Shaders/WeatherParticles.shader#L92-L94)) whose instance count is hardwired to 0 ([PrecipitationController.cs:309-313](../../Assets/Scripts/Planet/PrecipitationController.cs#L309-L313)) — dead path.
- **Impact:** visual double-exposure (slow gray streaks under fast white streaks), wasted draw + per-vertex weather sampling, and every future rain tuning has to be done twice. This is also why close rain feels inconsistent.
- **Effort:** S (delete) / M (fold features across). **Risk:** LOW — `rain-particles.*` and `precipitation.particle-proof` console commands verify each system in isolation before/after. **Confidence:** HIGH.
- **Fix sketch:** keep **`RainParticleController`** as the close-rain system (physical velocity, world-anchored respawn, per-drop density gate — the design doc'd path) and delete the WeatherParticles **Rain** pass: remove pass 2 + `VertRain` + profile-1/3 branches from the shader, the `rainParticleCount` draw in `PrecipitationRenderPass`, and `RainParticleCount`/`RainOpacity`/`RainStreakWidth`/`RainStreakLength`/`RainThreshold` from `PrecipitationController`+DTO (Dust and Snow stay). One thing worth porting *into* `RainParticles.shader` before deletion: the storm/lightning response (`WeatherParticles` streaks flash with lightning, [WeatherParticles.shader:407-410](../../Assets/Graphics/Shaders/WeatherParticles.shader#L407-L410); `RainParticles.shader` ignores lightning entirely).

---

### [W6] Smaller correctness / perf / hygiene items

- **(a) Debug-only texture sample paid in production:** `debugRainRate = max(debugRainRate, SampleDynamics(surfaceNormal).b)` runs for every lit sample of every cloud pixel regardless of debug mode ([Cloud.shader:354](../../Assets/Graphics/Shaders/Cloud.shader#L354)) — a `Texture2DArray` sample per lit step. If W2 lands, the sample becomes load-bearing (rain gloom) and this resolves itself; otherwise wrap it in `if (_CloudDebugMode == 8)`. Effort S, confidence HIGH.
- **(b) `RainParticleController` re-uploads all material params every frame** ([RainParticleController.cs:245-259](../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L245-L259)) — violates the dirty-flag discipline (grass report PERF-2 is the same pattern). The buffer bind can stay per-frame; the six floats/colors belong behind a dirty flag. Effort S.
- **(c) Event-order dependency in `RainParticleController.OnPlanetGenerated`:** it reads `_PrecipitationRadii` back from the global shader state to learn the cloud base ([RainParticleController.cs:146-148](../../Assets/Scripts/Planet/Precipitation/RainParticleController.cs#L146-L148)). That global is written by `PrecipitationController`'s *own* `PlanetGeneratedEvent` handler — listener order isn't guaranteed, so first-generation can read a stale/zero vector and silently take the `+375 m` fallback. Cleaner: read `SettingsProvider.GetSettings<CloudDto>().BaseAltitude` (the DTO is the sanctioned source; the "avoid a dependency" comment predates the settings service). Effort S, risk LOW, confidence MED (misfire depends on listener registration order at runtime).
- **(d) `[DefaultExecutionOrder(-50)]` on `RainParticleController`** — already logged as ARCH-1 in the grass report; noted here because fixing W5/W6c touches the same file.
- **(e) Dead vector lanes:** `_PrecipitationFadeParams.zw` and `_PrecipitationVisualParams.y` are always 0 ([PrecipitationController.cs:285-294](../../Assets/Scripts/Planet/PrecipitationController.cs#L285-L294)); profile-3 dead path per W5. Sweep when touching those files (dead-code rule).
- **(f) Per-vertex weather sampling in `WeatherParticles`:** every one of the 18 vertices per particle re-runs `SampleWeather` + `SampleDynamics` + `SampleClimate01` + full spherical placement math ([WeatherParticles.shader:103-366](../../Assets/Graphics/Shaders/WeatherParticles.shader#L103-L366)). ~5k instances × 18 verts ≈ 94k redundant sample triples per frame. Real but modest on desktop GPUs; shrinks by a third if W5 deletes the rain pass. Not worth restructuring on its own — flag-only.

---

## Part 2 — Direction: making rain *feel* better (options, not defects)

Grounded in what the codebase already has; each is independent.

1. **Ground wetness.** The terrain shader already receives climate and weather globals, and `dynamics.b` is globally sampleable. A `_GroundWetness` term (driven per-frame from the rain rate under the camera, smoothed over ~30 s so ground dries slowly after rain stops) that darkens terrain albedo ~20 % and boosts specular/smoothness would sell "it has been raining" even between showers. Pairs naturally with the existing puddle-free `SurfaceEditController` stamp architecture if puddles ever become surface edits. Effort M.
2. **Splash impacts.** `RainParticleUpdate.compute` already detects landing (respawn-on-landing logic). Emitting a landing position into a small ring buffer and drawing brief screen-facing splash quads (a second pass in `RainParticles.shader`) closes the loop between falling drops and the ground. Effort M.
3. **Camera lens droplets.** A cheap screen-space droplet overlay gated by `localRain` (same signal as W4b) and camera exposure to the sky (skip when looking straight down or sheltered — a single up-ray against the cloud shell suffices). Strong first-person cue. Effort M.
4. **Lightning already illuminates clouds and curtains — extend it to the scene.** `WeatherLightning` flashes exist in cloud, precipitation, and weather-particle shaders, but the terrain/water never flash. A brief `_WeatherLightningColor`-driven additive term in the surface lighting include (same 4-cell data, [WeatherLightning.hlsl:23-30](../../Assets/Graphics/Shaders/Includes/WeatherLightning.hlsl#L23-L30)) makes strikes light the ground under the storm. Effort S-M.
5. **Wind-gust coupling.** Rain streak slant already follows wind, and grass sways with `_WindSpeedMps` — but rain *intensity* doesn't gust. Modulating `DensityScale` (RainParticles) and curtain density (Precipitation) by the existing `SmoothPatchNoise`-style time noise would give rain the surging quality of real downpours. Effort S.
6. **Under-storm ambient darkening.** `CloudShadowFactor` already darkens direct sun under clouds, but ambient stays full. Scaling ambient down by the same shadow factor (storm-boosted) would make standing under a storm cell feel appropriately gloomy, reinforcing W2's darker clouds from below. Effort S.

---

## Recommended execution order

1. **W1 steps 1-2** — jitter restructure + march cap (kills the dither; smallest diff, biggest visible win).
2. **W5** — delete the duplicate rain pass (port lightning response into `RainParticles.shader` first).
3. **W2** — rain-gloom albedo + rim-keep silver lining + inner-glow term (subsumes W6a).
4. **W4** — rain fog, both terms, with DTO/console knobs.
5. **W3** — far-rain contrast/anisotropy retune (do after W4 — fog changes the background the curtains read against).
6. W6b/c/e cleanups opportunistically while in those files; Part 2 items as Bryan picks them.

Each step verifiable live: `cloud.debug-mode` (weather/storm/density/silver-lining/rain-rate views), `precipitation.debug-mode` (mask/dots), `precipitation.particle-proof` (per-profile isolation), `rain-particles.*` commands, `DEBUG_PRECIPITATION_CONTRIBUTION` heat view, and F10 captures at surface/500 m/horizon in clear vs storm cells.

---

## Codex feedback

Reviewed against current `HEAD` (`ec0b1cd`). I agree with the core findings: W1's white-noise-per-step sampling, W2's storm-only cloud darkening, W5's duplicate close-rain systems, and W6c's shader-global event-order dependency are all supported by the current source.

Implementation cautions before fixes:

- **W1 should stay no-history first.** The smallest useful patch is pure IGN/per-ray stratification plus removing per-step/per-light white-noise jitter. Do not add temporal jitter rotation unless there is also a resolve/upsample path; otherwise static grain becomes crawling grain. If `_CloudMaxDistance` is added, route it through `CloudSettings` -> `CloudDto` -> `CloudController`, matching existing cloud knobs.
- **W2 should update cloud shadows too.** `CloudShadows.hlsl` boosts shadow density from `weather.g` only ([CloudShadows.hlsl](../../Assets/Graphics/Shaders/Includes/CloudShadows.hlsl#L67-L68)). If visible cloud lighting uses rain-driven `gloom` but surface shadows stay storm-only, rain-heavy/non-storm cells can look dark overhead while the ground stays too bright. Reuse the same rain/storm gloom term there if W2 lands.
- **W5 needs a gate split, not just deleting pass 2.** `PrecipitationRenderFeature` uses `ShouldRenderLocalParticles` for both WeatherParticles and after-post `RainParticleController`, and that gate currently depends on `PrecipitationDto.RainParticleCount` ([PrecipitationController.cs](../../Assets/Scripts/Planet/PrecipitationController.cs#L139-L163)). If the WeatherParticles rain count is removed, after-post rain should be gated by `RenderLocalParticles` + altitude + `IRainParticleRenderer.IsReadyToDraw`, not by dust/snow counts or the deleted rain profile.
- **W6c's replacement should compute the same precipitation top radius.** Reading only `CloudDto.BaseAltitude` is not quite equivalent to today's `_PrecipitationRadii.y`; `PrecipitationController` uses `seaLevel + max(BottomAltitude + 1, CloudDto.BaseAltitude + PrecipitationDto.CloudBaseOverlap)` ([PrecipitationController.cs](../../Assets/Scripts/Planet/PrecipitationController.cs#L276-L279)). Factor that formula or read both DTOs, otherwise the rain particle spawn column can drift from the distant precipitation layer.
- **W4 fog knobs belong in `PrecipitationDto`.** The subsystem already hot-reloads settings through `SettingsChangedEvent`; add fog/haze constants there plus console commands, and preserve `DEBUG_PRECIPITATION_CONTRIBUTION` so fog changes remain measurable. The after-post `RainParticleController` should stay visually on top of the fog, as it does today.

Net: agree with the execution order. I would do W1, W5, and W6c as the first tight batch; W2 then needs the matching cloud-shadow update so storm/rain darkness is consistent from sky to terrain.
