# Cloud Visual Migration Plan — 2026-07-04

## Active Tracker

Status: Phase 2 complete and signed off (2026-07-06). Phase 3 partially achieved by effect
(horizon fade + cloud pass moved after atmosphere — Bryan approved the look); the planned
`CloudAerialDensity` aerial-perspective mechanism is NOT yet coded. Phase 4 not started.

Current next action: Bryan to choose — (a) accept the current sky integration as good
enough and skip to Phase 4 or park, (b) implement the explicit Phase 3.1 aerial-perspective
(`CloudAerialDensity`) + silver-lining re-check, or (c) begin Phase 4 (weather-shaped
vertical profiles — the differentiator, needs weather-evolution time-lapse validation).

- [x] Phase 0 code corrections: gloom/debug/dynamics/light-loop cleanup
- [x] Phase 0 exit capture archived: F10 cloud set `20260704-1325xx`
- [x] Phase 1: blue-noise ray offset (now a bound `BlueNoiseTexture`, wired in Planet.unity)
- [x] Phase 1: detail-noise early-out
- [x] Phase 1 capture comparison: `20260705-051115/051118`, Bryan saw no odd behavior
- [x] Phase 2 code: Beer-Powder light density
- [x] Phase 2 code: multi-scatter octave replacement
- [x] Phase 2 code: two-tone ambient constants
- [x] Phase 2 capture comparison and retune — signed off `20260706-203143`; storm clouds
      read dark and track the storm field, gloom driven by the shared `WeatherCloudGloom`
      helper, precip debug modes 8/9 validated against the weather field. Retune applied by
      Codex (rain thresholds, `SilverLiningStormSuppression` 0.85→0.45). Note: not a
      controlled same-viewpoint A/B — pre-Codex code was overwritten in the working tree, so
      the "before" is a different cell; capability is present and field-correct.
- [~] Phase 3: sky integration — IN PROGRESS (2026-07-06):
      - [x] cloud render pass moved AFTER atmosphere (`BeforeRenderingPostProcessing + 1`)
            so clouds are not fogged transparent by terrain depth; Bryan approved the look.
      - [x] aerial perspective (`_CloudAerialDensity`): distant clouds fade toward the
            atmosphere-lit background sky. Authored as a human 0-1 `AerialFade` converted to
            the Beer-Lambert coefficient at `AerialReferenceDistance` (2500 m); live-tunable
            via `cloud.aerial-fade`. Bryan signed off at **0.7** (now the default).
      - [~] god-ray / cloud interaction: RETIRED 2026-07-07 (first attempt reverted). Tried a
            post-cloud screen-space shaft pass (`Hidden/GodRays`) reading cloud opacity from the
            cloud pass's alpha, with the atmosphere shaft removed. Two failures: (1) removing
            `CalculateLightShafts` from `Atmosphere.shader` also removed the sun's soft halo, so
            the sun read as a hard disc; (2) the clear-sky-accumulation shaft produced a broad
            glow/haze across the whole sky, not localized crepuscular rays. All god-ray code
            reverted; `atmosphere.shaft-strength` console knob kept (tunes the restored
            atmosphere shaft). DO NOT retry the naive screen-space-clearness approach. Better
            paths for a future dedicated effort: (a) volumetric in-scatter accumulated inside
            the cloud view-march (clouds already march toward the sun — physically the right
            home for crepuscular rays), or (b) keep the atmosphere shaft for the sun glow and
            add cloud occlusion via the cloud-shadow field, not screen alpha. Needs its own
            scoped effort, not a quick pass.
      - [ ] backlit inner-glow tuning (forward-scatter when sun is behind cloud).
      - [ ] silver-lining re-check after the above.
- [ ] Phase 4: weather-shaped clouds

---

Goal: migrate the cloud renderer from "functional volumetric layer" to "weather you can
read from the sky" — clear / cloudy / storm / raining must be distinguishable at a glance,
near and from orbit, without breaking the weather-sim coupling that drives them.

Hard requirement carried through every phase: **the weather grid stays the single source
of truth.** Every visual change consumes `_CloudWeatherMap` (r=condensation, g=storm,
b=moisture-source) and `_WeatherDynamicsMap` (r=humidity, g=precip water, b=rain rate) —
no phase introduces cloud state that the sim doesn't drive. Snow is a surface/particle
concern (temperature gate in WeatherParticles); the cloud layer's job for snow weather is
the same as rain weather: overcast gloom + correct shadows.

Source docs: [consolidated audit](../audit/2026-07-22-consolidated-code-audit.md)
(defects A2, A3, B1, E1-E3), [reference recommendations](../research/2026-07-04-grass-cloud-reference-recommendations.md)
(R1, R2, R7), [cloud visual research](../research/2026-07-04-cloud-visual-research.md)
(techniques 1-7). This plan sequences them; the detailed code sketches live there.

Files touched across all phases: `Cloud.shader`, `CloudShadows.hlsl`, `CloudConstants.cs`,
`CloudController.cs`, `CloudDebugState.cs`, `CloudNoiseGenerator.cs` (+`CloudNoise.compute`
in Phase 4 only). Nothing else. Caustics untouched.

Verification workflow for every phase: `debug.capture-set "Cloud Diagnostics"` + F10
before/after captures, plus `cloud.debug-mode` sweeps. Compile check via Unity import (C#
side: `dotnet build ProceduralPlanets.Planet.csproj`).

---

## Phase 0 — Correctness floor (blocks everything; ~half a day)

The look work is wasted if sky and ground disagree about the same weather cell.

1. **Unify gloom** (audit A2): pick ONE formula for `gloom` and use it in both
   `Cloud.shader` (view march) and `CloudShadows.hlsl` (ground shadows).
   Recommendation: `gloom = max(storm, smoothstep(0.12, 0.6, rainRate))` with the SAME
   rain-rate source (raw `dynamics.b`, no storm gate) in both — rain-heavy cells should
   gloom even at moderate storm, and ground shadow must match sky. Delete the false
   "Same gloom term" comment or make it true.
2. **Hoist the per-step dynamics sample** (audit B1): `CloudPrecipitationSignal` moves
   inside the `density > 0.0001` branch; debug modes 8/9 recompute it under
   `_CloudDebugMode > 0`. Pays for Phase 2's added ALU.
3. **Fix the debug enum** (audit A3): add `WeatherPrecipitationSignal = 9` to
   `CloudDebugState.View`; align the mode-8 naming with `CloudDebugModule`.
4. Optional rider while in `LightMarch`: hoist the loop-bound `min()` (E2).

**Exit check:** fly to a rain cell (`cloud.debug-mode 8` to find one). Cloud darkening
above and shadow darkening below must track the same cells. Capture set archived as the
"before" baseline for the whole migration.

## Phase 1 — Sampling quality: kill the grain (1 day)

1. **Blue-noise ray offset** (R1): generate/import a 128² tileable blue-noise texture
   (editor-time generation via `CloudNoiseGenerator` is fine — void-and-cluster, or ship a
   known-good PNG). Bind as `_CloudBlueNoise`, Repeat wrap. Replace `pixelJitter`
   (`Cloud.shader:307-310`) with the tiled sample; keep per-step hash decorrelation seeded
   from it.
2. **Detail early-out** (R2): restructure `SampleCloud` to Lague's order — shape FBM →
   threshold → only sample detail when pre-erosion density > 0. Verify `CLOUD_QUALITY_LOW`
   path still compiles (it already skips detail).
3. With the reclaimed budget, raise default `ViewSteps` if captures still band
   (`cloud.density` sweep at 48 → 64) — settings change, not code.

**Exit check:** A/B captures of a backlit cumulus face at rest and while panning. Accept
when grain reads as fine film grain (no worms/blotches) at default step count.

## Phase 2 — Lighting model: make clouds read as volumes (1-2 days)

The core look phase. All in the `density > 0.0001` branch of the view march + `LightMarch`.

1. **Beer-Powder** (research #1): change `LightMarch` so it exposes the accumulated
   `lightDensity` used to derive transmittance, then add `powder = 1 - exp(-lightDensity *
   2)` to the lit result, blended by sun-facing (`saturate(cosAngle)`) and a
   `PowderStrength` constant in `CloudConstants`.
2. **Multi-scatter octaves** (research #2): replace the single ad-hoc `multiScatter` term
   (`Cloud.shader:369-370`) with the 3-octave Oz/Frostbite loop over the already-marched
   `lightDensity` (a=b=c=0.5 starting values, constants in `CloudConstants`). Delete the
   old term — don't stack them.
3. **Two-tone ambient** (research #3): `_CloudAmbientSky` / `_CloudAmbientGround` colors
   lerped by `cloud.height01`, replacing the scalar ambient ramp. Wire sky tint from the
   atmosphere zenith color global if available; constant fallback otherwise.
4. **Retune the storm/gloom constants once, at the end of this phase** — powder +
   octaves change perceived darkness; `StormDarkening`, `SilverLiningStormSuppression`,
   and the Phase-0 gloom smoothstep get one deliberate pass with F10 evidence, not
   knob-nudging (`cloud.debug-mode 2/8` to locate comparison cells).

**Exit check (weather legibility, the point of the plan):** one capture each of a clear
cell, humid cumulus cell, storm cell, raining cell — the four must be tellable apart with
the HUD off: clear = sparse bright, cumulus = carved white with dark creases, storm =
tall dark but internally luminous, raining = storm + visible curtains below
(Precipitation.shader's rainRate-gated visibility from the working tree is a dependency —
it ships with this plan).

## Phase 3 — Sky integration (half a day)

1. **Aerial perspective** (research #6 / suggested-order #4): distance-fade cloud contribution toward the
   atmosphere horizon color (existing atmosphere globals) so distant clouds sit *in* the
   sky. Constant `CloudAerialDensity` in `CloudConstants`.
2. **Silver-lining re-check**: after powder + aerial, the rim may need its strength
   reduced — one tuning pass, captures archived.

**Exit check:** wide horizon shot at midday and sunset; distant clouds haze out instead of
pasting dark silhouettes on the skyline.

## Phase 4 — Weather-shaped clouds (2-3 days, the differentiator)

This is the phase that makes the weather system *visible in silhouette*, not just in tint.

1. **Cloud-type vertical profiles** (research #4 / suggested-order #6, Nubis 2017): replace the single
   bottomFade×topFade envelope with three analytic height profiles —
   stratus (low flat), cumulus (mid billow), cumulonimbus (tall, top-heavy) — blended by
   weather: calm+humid → stratus/cumulus mix (drive by `condensation` vs `moistureSource`),
   `storm` → cumulonimbus. Same function must go into `CloudShadows.hlsl`'s density; keep
   the two density implementations in sync, or introduce a new shared cloud-density helper
   before this phase.
2. **Curl-distorted detail** (research #5): small curl texture from
   `CloudNoiseGenerator`, distortion strongest at cloud base, zero at top.
3. **Storm-cell height boost**: let `gloom` also scale the shell's effective top (taller
   storm clouds) via the cumulonimbus profile — NOT by moving the shell radii (shadow and
   precipitation math depend on them).

**Exit check:** time-lapse (weather evolution on, `weather.wind-speed` up): a cell
transitioning humid → storm → raining must visibly *grow taller and darker, then drop
curtains*, then decay. That's the "clouds work with the weather system" acceptance test.

## Phase 5 — Optional polish (parked until 0-4 ship)

- Cone-sampled light march (research #7) — redistribution, do only if light banding still
  visible after Phase 1-2.
- Light-march first-sample midpoint fix (audit E1) — fold into #7 if taken.
- Cubed edge-erosion (R7) — one-line A/B during any phase's captures.
- Night: cloud-coverage-modulated ambient (R10) — belongs to the night-lighting pass.
- Temporal quarter-res (Frostbite recipe) — only if step budget becomes the wall again.

---

## Sequencing, risk, decision gates

- Phases are strictly ordered 0→4; each is independently shippable and capture-verified.
- **Gate after Phase 2** (Bryan review): the lighting model changes the whole look —
  approve before weather-shaping work builds on it.
- **Gate in Phase 4**: profile blending changes cloud coverage in ways that may want
  weather-sim retuning (`InitialCoverage`, storm thresholds). Budget a settings pass.
- Biggest technical risk: Cloud.shader/CloudShadows density drift (Phase 4.1). Audit D2
  only shares weather sampling; Phase 4 needs a separate shared cloud-density helper or a
  strict paired-edit rule before changing the vertical profile.
- Rain/snow/clear coupling is preserved by construction: every phase reads the existing
  weather channels; no phase writes new cloud state.
