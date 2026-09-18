# Rain visual polish — 2026-09-08

Bryan requested softer rain curtains, gradual local rain entry, and darker drops under dark lighting.

## Changes

- Removed fine radial curtain noise that the ray-step budget could not resolve.
- Replaced linear per-step opacity with exponential extinction. Fog now integrates the full column instead of stopping at the curtain opacity cap.
- Kept jittered samples inside their ray segments. Smoothed the camera haze at the column boundaries.
- Removed a duplicate weather lookup and the remaining fine curtain noise. The curtain retains broad wind-sheared variation without falling streaks.
- Applied daylight, storm dimming, and lightning to local drops. Rain presentation opacity uses a 0.85 multiplier with softened streak edges and ends.
  Bryan found the initial 0.5 multiplier too faint. The revised multiplier raises drop opacity by 70% while keeping the transition and curtain fixes.
- Smoothed particle density thresholds and widened distance fades. Birth opacity reaches full strength over half a second.
- Applied altitude fading to both rain and snow. The local curtain exclusion radius now shrinks with that fade.
- Used the existing moving-water immersion state for rain eligibility. The shader retains its continuous immersion fade.
- Preserved world-space trajectories and surface-contact impacts.

## Verification

Core build: passed, zero warnings. Planet build: passed, 19 existing warnings, zero errors.
Both shaders report no compiler messages in Unity. A fresh play session completed generation.
All 12 RainParticleRegressionTests and SurfaceWeatherTests passed. These include terrain, lake, thin roof, mesh, player capsule, and camera-independent trajectories.
Test job: `c3127d65b7d3423dbca0aaee44e8ca75`.

Evidence directory: `local-only/weather-validation/2026-09-08/`.
It contains test JSON, build logs, original source snapshots, screenshots, and capture sidecars.
The initial before/after pair keeps the original camera and weather, but shader reimport invalidated cached material properties. Do not use that pair to judge particle visibility.
The runtime upload cache was invalidated after the final import. Use the `material-restored` capture for the final particle look.
Fresh-session captures use regenerated weather and are not an exact visual A/B.
Captures cover local rain, the horizon, 374 m and 376 m around the rain-column top, and a water crossing.
The moving surface reported full immersion with a zero local particle radius underwater.

Controlled curtain timing used the same fresh-session pose, weather, quality, and local particle shader. Only the curtain shader changed:

| Whole-frame GPU | Original curtain | Revised curtain |
| --- | ---: | ---: |
| Average | 22.63 ms | 22.87 ms |
| p95 | 30.14 ms | 31.71 ms |
| Valid samples | 108 | 114 |

This is a small average increase, with a larger noisy tail. It does not establish a performance win or a general frame-rate guarantee.
The final jitter-bound correction and rain-opacity reduction followed this measurement. Neither adds samples or passes.
No fullscreen blur pass, extra particles, or extra ray steps were added.

## Limits

The existing volume sampling still produces some grain. This change does not replace cloud rendering or its temporal filtering.
The captures check the rain-column boundary, not every altitude around the entire atmosphere.
Bryan's visual review remains the final judgment of the look.
Unity remains in play mode at the original camera pose. Weather and time remain frozen for inspection; particle proof mode is off.
