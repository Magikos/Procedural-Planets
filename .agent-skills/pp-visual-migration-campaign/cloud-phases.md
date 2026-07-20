# Cloud phases — detailed runbook

Mirror of `docs/design/2026-07-04-cloud-visual-migration-plan.md` (the doc wins on
conflict). Read SKILL.md Step 0 first — do not enter a phase without knowing the
campaign position. All facts date-stamped 2026-07-06 unless noted.

Files this campaign may touch (per the plan, nothing else): `Cloud.shader`,
`CloudShadows.hlsl`, `CloudConstants.cs`, `CloudController.cs`, `CloudDebugState.cs`,
`CloudNoiseGenerator.cs` (+`CloudNoise.compute` Phase 4 only). Caustics untouched.

The standard capture sequence for every cloud gate:

```
debug.capture-set "Cloud Diagnostics"
debug.capture          # or press F10; writes 9 PNG+txt pairs (modes 0-9, no mode 5)
```

"Cloud Diagnostics" contains modes Off(0), CloudWeather(1), CloudDensity(3),
CloudOpticalDepth(4), CloudStorm(2), CloudMoistureSource(6), CloudCondensationChange(7),
CloudPrecipitationSignal(8), WeatherPrecipitationSignal(9) — registered at
`CloudDebugModule.cs:53-55`. Individual modes: `cloud.debug-mode <0-9>`.
Useful companions: `weather.frame-storm` (repositions camera to the strongest storm),
`camera.save-teleport <name>` / `camera.teleport <name>` (reproducible viewpoints),
`quality.cloud-steps <0.33-1>` (step multiplier, reset by `quality.set`).

## Drift log (plan text vs landed code — tree wins)

- **Gloom formula:** the plan's Phase 0 text recommends
  `gloom = max(storm, smoothstep(0.12, 0.6, rainRate))` with raw un-gated rain. What
  actually landed (per the audit's Codex correction) is the **gated-linear** form:
  `WeatherCloudGloomFromRain(storm, signal) = max(saturate(storm), saturate(signal))`
  where `signal = dynamics.b × smoothstep(storm gate from _PrecipitationParams.y/.z)` —
  `WeatherSampling.hlsl:40-55`, consumed by `Cloud.shader:388` (view march) and
  `CloudShadows.hlsl:58` (ground shadows). Both sides use the same helper, which is the
  point of A2. Do not "fix" the code to match the plan's sketch.
- **Bundle `20260705-0533xx`** exists on disk (newest cloud set) but the Phase 2
  comparison checkbox is unchecked — the tracker lags the capture. Reconcile per Step 0.

## Phase 0 — Correctness floor — DONE (verify, don't redo)

Landed: gloom unified (A2, via `WeatherCloudGloom*` helpers), debug enum mode 9 (A3),
dynamics-sample hoist (B1), `TryGet` resolve (A4). Exit baseline archived:
bundle `20260704-1325xx`.

**Regression check (run any time sky/ground disagreement is suspected):**

```
cloud.debug-mode 8        # CloudPrecipitationSignal — find a raining cell
# fly to it (weather.frame-storm gets you close), then:
cloud.debug-mode 0
```

Expected: cloud darkening above and ground-shadow darkening below track the **same
cells**. If they diverge → the gloom formulas drifted again → re-run
`grep -rn "WeatherCloudGloom" Assets/Graphics/Shaders/` and confirm both call sites still
use the shared helper; return to Phase 0 step 1 of the plan. Do not proceed to any later
phase on a diverged gloom.

## Phase 1 — Sampling quality (blue noise + detail early-out) — DONE, gate passed

Landed: `_CloudBlueNoise` bound and sampled (`Cloud.shader:19-21, 332-333`; asset
`Assets/Graphics/Shaders/BlueNoise.png`; global name in `ShaderGlobalIds.Cloud.cs:39-40`),
detail early-out restructure. Comparison bundle `20260705-051115/051118`; Bryan reviewed —
"no odd behavior". Default `ViewSteps` stayed 48 (`CloudSettings.cs:27`, range 8-96;
`MinViewSteps` 24). The plan's step-raise lever (48 → 64) remains available as a
**settings** change if later captures still band — that's an SO-default change plus
capture evidence, not shader code.

**Regression check:** backlit cumulus face, at rest and panning — grain must read as fine
film grain, no worms/blotches. If blotches return, check the blue-noise binding first
(`grep -n "_CloudBlueNoise" Assets/Graphics/Shaders/Cloud.shader` and confirm the texture
import is Repeat-wrapped) before touching step counts.

## Phase 2 — Lighting model — CODE DONE; **YOU ARE HERE: capture comparison + retune gate**

Landed code (all verified in `Cloud.shader`): `CloudBeerPowder` (~line 110),
`CloudMultiScatter` 3-octave loop (~118-134), two-tone ambient
`lerp(_CloudAmbientGround, _CloudAmbientSky, cloud.height01)` (~393). Constants in
`CloudConstants.cs` (as of 2026-07-06): `PowderStrength = 0.65`, `StormDarkening = 0.65`,
`SilverLiningStormSuppression = 0.45`, `MultiScatterAttenuation/Contribution/PhaseScale =
0.5/0.5/0.5`, `MultiScatterStrength = 0.35`, `AmbientSky = (0.62, 0.76, 0.98)`,
`AmbientGround = (0.50, 0.45, 0.38)`. These are C# consts — a tuning pass is an edit +
Unity recompile + recapture loop, not console commands.

**Gate procedure:**

1. Confirm whether `20260705-0533xx` is the post-Phase-2 capture (ask Bryan / read its
   sidecars). If not confirmed, take a fresh "Cloud Diagnostics" capture from the same
   viewpoint as the Phase 1 pair.
2. Compare against `20260705-051115/051118` (pre-Phase-2 look) and the `20260704-1325xx`
   migration baseline.
3. **The four-way legibility test (the point of the whole plan), HUD off, one capture
   each:** clear cell, humid cumulus cell, storm cell, raining cell. Locate cells with
   `cloud.debug-mode 1` (weather), `2` (storm), `8` (gated rain), `9` (raw dynamics rain);
   `weather.frame-storm` for the storm cell. Expected: clear = sparse bright; cumulus =
   carved white with dark creases (the powder term working); storm = tall dark but
   internally luminous (multi-scatter working); raining = storm + visible curtains below
   (Precipitation.shader's rainRate-gated visibility — verified present in the tree at
   `Precipitation.shader:122-154` — is a declared dependency and ships with this plan).
4. Decide with Bryan whether `StormDarkening`, `SilverLiningStormSuppression`, powder, or
   scatter strength need **one** deliberate tuning pass. Each candidate change: predict
   the direction first, edit the const, recapture same viewpoint, present the diff.
   Powder + octaves changed perceived darkness, so the Phase-0-era gloom response may
   legitimately need this one pass — that is in-plan, not knob-nudging.

**Branch instructions:**
- Four states tellable apart + Bryan approves → check the Phase 2 boxes (with his
  sign-off), record the bundle timestamp, proceed to Phase 3.
- Storm and raining cells indistinguishable → check mode 8 vs mode 9 captures: if mode 9
  shows rain where mode 8 doesn't, the storm gate (`_PrecipitationParams.y/.z`) is
  suppressing the signal — that's a weather-sim coupling question (pp-weather-sim-reference),
  not a shader tuning problem. Don't fix it by darkening constants.
- Sky gloom and ground shadow diverge on the same cells → Phase 0 regression; go back.
- **This is the plan's hard Bryan gate**: the lighting model changes the whole look —
  approve before Phase 3/4 build on it, and before grass Phase 1 unblocks.

## Phase 3 — Sky integration — NOT STARTED (no `AerialDensity` symbol in tree)

1. Aerial perspective: distance-fade cloud contribution toward the atmosphere horizon
   color (existing atmosphere globals — reuse them, don't mint new state). New constant
   `CloudAerialDensity` in `CloudConstants.cs`.
2. Silver-lining re-check: after powder + aerial, rim strength may need reducing — one
   pass, captures archived. Current knobs: `SilverLiningStrength = 0.9`,
   `SilverLiningPower = 10`, `SilverLiningEdgePower = 1.6`.

**Exit check:** wide horizon shot at midday AND sunset (drive time of day via the `time`
console prefix); distant clouds haze into the sky instead of pasting dark silhouettes on
the skyline. Before/after pair from one saved teleport.

## Phase 4 — Weather-shaped clouds — NOT STARTED (the differentiator, 2-3 days)

**Entry condition (blocking):** decide the shared-density strategy first. The vertical
profile function must be bit-identical in `Cloud.shader` and `CloudShadows.hlsl`. Either
extract a shared cloud-density helper include, or adopt a strict paired-edit rule —
propose the choice to Bryan before writing profile code. (Audit D2 only unified *weather
sampling*; density is still duplicated.)

1. Three analytic height profiles — stratus (low flat), cumulus (mid billow),
   cumulonimbus (tall, top-heavy) — blended by weather channels: condensation vs
   moisture-source drives the stratus/cumulus mix, storm drives cumulonimbus. Same
   function into `CloudShadows.hlsl` density.
2. Curl-distorted detail via `CloudNoiseGenerator` (+`CloudNoise.compute`): strongest at
   cloud base, zero at top.
3. Storm-cell height boost through the cumulonimbus profile only — **NOT by moving the
   shell radii** (shadow + precipitation math depend on them; `cloud.altitude` /
   `cloud.thickness` console ranges tell you those are load-bearing).

**Exit check (acceptance test for "clouds work with the weather system"):** time-lapse
with evolution running and `weather.wind-speed` raised — a cell transitioning
humid → storm → raining must visibly grow taller and darker, then drop curtains, then
decay. Capture the sequence; sidecar's CameraWeather line documents the cell state per
frame.

**In-phase gate:** profile blending changes coverage in ways that may need a weather-sim
settings pass (`InitialCoverage`, storm thresholds) — budget it, present it to Bryan as a
settings change with captures, per pp-settings-and-flags.

## Phase 5 — Optional polish — PARKED until 0-4 ship

Cone-sampled light march (only if light banding still visible), light-march midpoint fix
(E1, fold into the cone change if taken), cubed edge-erosion one-line A/B (R7), night
coverage-modulated ambient (R10 — belongs to the night-lighting pass), temporal
quarter-res (fenced — see SKILL.md wrong paths).
