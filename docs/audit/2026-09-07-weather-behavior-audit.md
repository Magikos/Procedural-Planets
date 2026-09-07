# Audit Summary

**Findings only — no code changed.**

Reviewed the dirty working tree on `harvest-vertical-slice`, HEAD `1df21b2`, on 2026-09-07. Existing source changes were preserved.

Scope: cloud density and lighting, weather evolution, precipitation rendering, local rain and snow, wind, and terrain/water response. This is source review, not visual acceptance or a measured performance result. Unity, builds, and runtime tests were not run. No source changed, so Graphify regeneration was not required.

The requested behavior makes sense. The current implementation supplies several layers, but does not yet satisfy the whole requirement. Eight findings follow. Missing requested features are distinguished from defects in existing behavior.

Bryan clarified that "inside" means inside the rain zone beneath a cloud, not inside the cloud volume. Distant rain uses curtains. Nearby rain uses a bounded population of 3D particles around the observer. Both must follow the same precipitation field, with a smooth distance transition. No extension of particles into the cloud volume is requested. Arid regions should reduce available moisture over time, rather than instantly delete every passing storm.

## What came back clean

- Cloud density uses weather condensation, a feathered vertical profile, shape noise, and edge erosion. See `Assets/Graphics/Shaders/Cloud.shader:151` and `Assets/Graphics/Shaders/Includes/CloudDensity.hlsl:19`.
- Storm/rain gloom darkens clouds before and during precipitation. Thin backlit edges retain silver lining. See `Assets/Graphics/Shaders/Cloud.shader:413` and `:424`.
- Cloud shadows share the vertical profile and gloom helper. See `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl:66` and `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl:87`.
- Distant curtains sample rain, storm, and cloud support. The shader provides depth clipping, noise breakup, wind motion, fog, and opacity controls. See `Assets/Graphics/Shaders/Precipitation.shader:139` and `:149`.
- Local rain has persistent 3D positions and velocity-aligned billboard geometry. It does not need individual droplet meshes.
- Whole-planet weather evolution dispatches six faces independently of camera visibility. See `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs:461` and `Assets/Scripts/Planet/WeatherEvolutionScheduler.cs:22`.
- Rendering uses camera rays, depth limits, frustum checks, bounded particle counts, and quality-dependent march limits. These are useful controls, not proof of the final frame budget.
- Water waves already consume wind and weather gloom. See `Assets/Graphics/Shaders/Includes/WaterDisplacement.hlsl:50`.
- Weather readback callbacks reject old epochs after reset. See `Assets/Scripts/Planet/WeatherQueryCache.cs:67` and `:96`.

# Findings

## W01 — Local precipitation bands and fades disagree

**Category:** Bug. **Severity:** Medium. **Confidence:** HIGH. **Effort:** M. **Fix Risk:** MED.

**Description:** The camera gate stops rain and snow at cloud base. The rain fade uses a different altitude limit.

**Evidence:** `Assets/Scripts/Planet/PrecipitationController.cs:165`–`:184` gates both local systems with `min(LocalMaxCameraAltitude, cloudBase)`. `Assets/Scripts/Planet/Precipitation/RainParticleController.cs:252`–`:254` fades against `LocalMaxCameraAltitude` alone. `Assets/Resources/RainParticleUpdate.compute:102` spawns rain between sea level and `_CloudBottomRadius`.

**Impact:** The mismatch can produce an abrupt cutoff before the intended fade. Stopping particles at cloud base is not itself a requirement defect. The original inside-cloud interpretation is withdrawn following Bryan's clarification.

Rain spawn radii also refresh only on planet generation (`RainParticleController.cs:125`, `:138`). Runtime band changes can leave spawning behind current curtain settings. Refresh the shared band on settings changes.

**Recommendation:** Share the below-cloud precipitation band and camera fade calculation. Validate entry into the rain zone from a distant view. Keep individual particles local and preserve terrain and water occlusion.

**Refactor Option:** Share the existing band calculation between the controllers.

**Behavior note:** Corrects fade and settings agreement. No extension into clouds is proposed.

## W02 — Snow does not replace rain in cold weather

**Category:** Bug. **Severity:** High. **Confidence:** HIGH. **Effort:** M. **Fix Risk:** MED.

**Description:** Snow has a temperature phase factor, but rain does not consume its complement.

**Evidence:** `Assets/Graphics/Shaders/WeatherParticles.shader:163`–`:173` computes `snowPhase`; `:207`–`:210` applies it. `Assets/Graphics/Shaders/RainParticles.shader:125`–`:150` gates drops by rain signal without temperature. `Assets/Graphics/Shaders/Precipitation.shader:139`–`:147` also has no phase distinction.

**Impact:** A cold storm can display full rain droplets alongside snow. Distant precipitation retains its rain appearance.

**Recommendation:** Share a temperature-based precipitation phase calculation. Apply complementary rain/snow weights locally. Give distant precipitation a matching cold-weather appearance, if required.

**Refactor Option:** Extend existing climate/weather shader helpers, rather than create another weather field.

**Behavior note:** Changes cold-weather visuals. Mixed precipitation can remain around the transition temperature.

## W03 — Desert cloud dissipation is not coupled to actual aridity

**Category:** Architecture. **Severity:** High. **Confidence:** HIGH. **Effort:** L. **Fix Risk:** HIGH.

**Description:** Cloud condensation relaxes toward a fixed seeded source, regardless of current humidity. Moisture supply comes from a separate latitude/noise calculation.

**Evidence:** `Assets/Graphics/Shaders/WeatherEvolution.compute:160`–`:165` sets `targetCondensation = saturate(source)`. Humidity affects precipitation at `:176` and recovers at `:186`. Seeding at `:331`–`:342` uses latitude and weather noise, not the terrain climate moisture field.

**Impact:** Dry air can stop rain without removing supported cloud cover. Terrain deserts and atmospheric moisture supply need not agree.

**Recommendation:** Couple moisture supply to the existing climate geography. Make condensation growth and evaporation depend on available humidity. Preserve advected moisture so occasional desert storms remain possible. Model below-cloud evaporation if rain must vanish before reaching dry ground.

**Refactor Option:** Extend the existing weather evolution state and rates.

**Behavior note:** Changes storm distribution and lifetime. This needs controlled climate-boundary captures and weather-grid measurements.

## W04 — Rain does not produce wet ground or accumulating snow

**Category:** Architecture. **Severity:** High. **Confidence:** HIGH. **Effort:** L. **Fix Risk:** MED.

**Description:** Surface channels reserve wetness and snow depth, but the inspected terrain path does not consume them for weather response.

**Evidence:** `Assets/Graphics/Shaders/PlanetVertexColor.shader:189`–`:190` reserves B/A. `:824`–`:836` consumes R/G for paths and scorch. Snow at `:946`–`:949` depends on static temperature. Searches for `surfaceState.[ba]`, `Wetness`, and `SnowDepth` found no implemented weather accumulation consumer in first-party C#/shader code.

**Impact:** Rain does not progressively darken or gloss ground. Stopping rain does not start drying. Ground snow does not accumulate from snowfall.

**Recommendation:** Add bounded surface moisture and snow accumulation driven by the shared precipitation phase. Include drying and melting. Reuse surface data infrastructure without recording individual droplets as persistent edit stamps.

**Refactor Option:** Keep surface evolution separate from the camera-local particle renderer.

**Behavior note:** Adds requested surface behavior. Snow accumulation is an extension beyond falling snow alone.

## W05 — Drops do not interact with actual terrain and water surfaces

**Category:** Architecture. **Severity:** High. **Confidence:** HIGH. **Effort:** L. **Fix Risk:** MED.

**Description:** Rain simulation detects only the sea-level sphere. It produces no impact output.

**Evidence:** `Assets/Resources/RainParticleUpdate.compute:168` tests `currentDist < _SeaRadius`; `:174` respawns the drop. `Assets/Graphics/Shaders/RainParticles.shader:163`–`:168` only hides drops behind scene depth. Water wave response at `Assets/Graphics/Shaders/Includes/WaterDisplacement.hlsl:50` is storm-driven, not drop-impact-driven.

**Impact:** Raised terrain and lakes do not stop simulated drops at their surfaces. Depth fading can hide this, but cannot create splashes or rain ripples.

**Recommendation:** Use existing terrain and water surface data for nearby impact classification. Render a capped local splash/ripple population. Keep distant rain as a volume effect.

**Refactor Option:** Reuse water-level and terrain sampling facilities. Avoid per-drop CPU physics calls.

**Behavior note:** Adds requested rain impacts and water interaction.

## W06 — Changing wind direction remaps the accumulated cloud motion

**Category:** Bug. **Severity:** Medium. **Confidence:** HIGH. **Effort:** M. **Fix Risk:** MED.

**Description:** The cloud noise uses the current wind axis with the total angle accumulated over its whole lifetime.

**Evidence:** `Assets/Scripts/Planet/WeatherEvolutionScheduler.cs:55` accumulates one angle. `Assets/Graphics/Shaders/Cloud.shader:186`–`:189` applies that angle about the current wind axis. The shadow shader duplicates this mapping at `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl:55`. The wind command changes direction directly at `Assets/Scripts/Planet/WeatherManager.cs:63`–`:67`.

**Impact:** After weather has run, changing direction can abruptly relocate cloud detail and shadows. Incrementally advected weather does not undergo the same relocation.

**Recommendation:** Preserve accumulated motion when direction changes. Use a shared continuous transport mapping for cloud detail and shadows. Validate a wind turn after a long run.

**Refactor Option:** Share the duplicated noise advection mapping between cloud and shadow code.

**Behavior note:** Changes wind-transition visuals.

## W07 — Snow fades away above sea-level ground

**Category:** Bug. **Severity:** Medium. **Confidence:** HIGH. **Effort:** M. **Fix Risk:** MED.

**Description:** Snow reuses the distant precipitation slab's lower boundary.

**Evidence:** `Assets/Scripts/Planet/PrecipitationController.cs:45` defaults `BottomAltitude` to 25 metres; `:283` publishes that radius. `Assets/Graphics/Shaders/WeatherParticles.shader:247`–`:256` places snow inside that slab and fades its bottom six percent.

**Impact:** Snow disappears overhead instead of falling around a sea-level observer. Local terrain height does not define its landing boundary.

**Recommendation:** Give local snow a ground-reaching volume using the same local surface contract as rain. Keep curtain fades independent from local particle landing.

**Refactor Option:** Reuse W01 and W05's shared volume/surface work.

**Behavior note:** Changes local snow placement.

## W08 — Hidden rain continues its full local compute update

**Category:** Maintainability. **Severity:** Medium. **Confidence:** HIGH for dispatch behavior; unmeasured cost. **Effort:** M. **Fix Risk:** MED.

**Description:** Once ready, the rain controller updates every frame independently of render visibility or precipitation enablement.

**Evidence:** `Assets/Scripts/Planet/Precipitation/RainParticleController.cs:223`–`:237` always calls `DispatchUpdate`. `:315`–`:318` dispatches the configured count, default 30,000 at `:34`. Render gating happens separately in `Assets/Scripts/Planet/PrecipitationRenderFeature.cs:86`.

**Impact:** Hidden local presentation retains compute work. This is separate from the useful whole-planet weather simulation.

**Recommendation:** Suspend local rain updates when the effect cannot render. Reinitialize its local population on re-entry. Avoid relying solely on lagged CPU weather samples near moving storm boundaries.

**Refactor Option:** Share render eligibility with the controller; do not pause global weather.

**Behavior note:** Must preserve immediate rain on entry. Confirm savings with GPU dispatch timings and particle counters before changing budgets.

# Refactoring Plan

These are proposed slices, not implementation authorization.

1. Unify precipitation phase, volume, and altitude fade (W01, W02, W07). Verify ground, distant-to-local rain-zone entry, below-cloud altitude limits, and cold-transition views.
2. Couple climate moisture and evaporation (W03). Track condensation, humidity, and rain across humid-to-arid boundaries with rendering disabled.
3. Preserve wind history (W06). Compare clouds, shadows, curtains, snow, drops, foliage, and water during wind turns.
4. Add bounded surface response and nearby impacts (W04, W05). Verify raised lakes, slopes, shoreline, drying, and freezing without changing caustics accidentally.
5. Gate local work and measure the whole stack (W08). Compare dry, distant-storm, inside-rain-zone, snow, orbit, and planet-out-of-view cases.

## Added requirement — lightning flashes, thunder, and later strikes

Bryan also requested cloud illumination from lightning, accompanying thunder, and eventual visible lightning strikes.

Existing source support:

- `Assets/Scripts/Planet/WeatherLightningController.cs:111` selects a strong precipitation cell and creates a short flash sequence.
- The controller publishes up to four flash cells. `Assets/Graphics/Shaders/Includes/WeatherLightning.hlsl:9` masks their contribution by location and storm strength.
- `Assets/Graphics/Shaders/Cloud.shader:449` lights cloud samples. Rain and precipitation shaders also consume the flash.
- `Assets/Scripts/Planet/WeatherLightningController.cs:148` raises `WeatherLightningEvent`, explicitly setting `isGroundStrike: false`.

Missing support: the first-party C# search found no thunder playback or listener for `WeatherLightningEvent`. The inspected lightning system renders cloud flashes, not bolt geometry or ground strikes. Existing flash quality remains visually unverified.

Proposed work: reuse the lightning event to schedule spatial thunder with distance-dependent delay and attenuation. Retain a bounded number of active sounds. Later, extend that same event with validated strike endpoints for visible bolts and contact effects. Use one authority-owned strike schedule for multiplayer; the current controller explicitly documents that local `Time.time` schedules are not synchronized. Rendering and audio should consume the event without choosing independent strikes.

This addition records scope and source support. It does not claim implementation or runtime validation.

For approved C# edits, build `ProceduralPlanets.Core.csproj`, then `ProceduralPlanets.Planet.csproj`, serially. Shader changes require Unity import. Record matched before/after captures and GPU timings at each quality tier. No performance target or final visual quality is proven by this audit.

# Prior Audit Reconciliation

Source: `2026-07-22-consolidated-code-audit.md`. Other current audit files target scatter, startup, or terrain rather than this behavior review.

| Prior item | Current status | Evidence or scope |
|---|---|---|
| F04 generation cleanup | PARTIAL | Cancellation around the await releases textures at `SphericalWeatherGrid.cs:204`. Allocation/setup/dispatch before that try remains outside cleanup. |
| F07 stale callbacks | RESOLVED | Both callbacks reject old epochs in `WeatherQueryCache.cs`. |
| F08 disabled renderers | OPEN | Rain readiness at `RainParticleController.cs:70` and precipitation readiness at `PrecipitationController.cs:131` omit enabled state. Cloud feature checks service liveness. |
| F13 settings/band ownership | OPEN | Separate rain fields and duplicated altitude bands remain; W01 demonstrates a behavior mismatch. |
| F16 precipitation service lookup | RESOLVED for this consumer | `PrecipitationRenderFeature.cs:122` caches the rain renderer. Other F16 consumers are outside scope. |
| Former W1 jitter | No new defect established | Noise/blur code exists; no runtime grain evidence was captured. |
| Former W2 gloom/silver lining | RESOLVED in source | Shared gloom and backlit rim code remain present. Final appearance still needs review. |
| Former W3 distant rain | Implemented, visually unverified | Curtain code exists. Prior resolution is not proof of current horizon readability or W01's transition. |
| Former W4 fog | RESOLVED in source | Curtain fog and local haze remain implemented. |
| Former W5 duplicate rain | RESOLVED | WeatherParticles handles dust/snow; persistent rain uses its own renderer. |
| Former W6b uploads | RESOLVED in source | Material values use change checks. This does not address W08 compute dispatch. |
| Former D2 profile parity | RESOLVED in source | Cloud and shadow paths share `CloudVerticalProfile`. |

F04/F08 remain linked maintenance work rather than duplicated new feature findings. No earlier audit was removed.

# Questions for the User

None required to complete this review. Implementation needs selection of the proposed fixes and an agreed frame budget before performance acceptance.

## Verification limits

Graphify query completed. Direct reads verified the cited functions. Searches covered first-party C# and shaders; generated assets and third-party code were excluded from behavior claims.

Two inspection commands needed correction. The installed Git returned “error: unknown option `show-current'”; `git symbolic-ref --short HEAD` succeeded. Literal wildcard path arguments to ripgrep returned “The filename, directory name, or volume label syntax is incorrect. (os error 123)”; directory searches with glob filters succeeded.

No runtime, build, shader compilation, screenshot, or GPU timing claim is made. Cloud softness, silver-lining quality, curtain horizon readability, and transition quality require live visual evidence.
