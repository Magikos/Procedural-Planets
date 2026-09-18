# Water interactions, transitions, reflections, and quality

Bryan requested all four follow-ups to the water reference work on 2026-09-07.
The work retains the existing depth absorption and underwater visibility. It does not increase clarity.

## Runtime ownership

`PlanetWaterSurface` creates `WaterPresentationController` on the generated water object.
The controller uses the existing water, weather, climate, and quality services.
It removes its render callbacks, reflection textures, particles, and owned audio filter during teardown.

- `WaterInteractor` marks transform-driven objects. The player capsule receives it when created.
- The controller also samples nearby rigidbodies through a bounded, allocation-free overlap query.
- `WaterContactTracker` emits entry splashes and surface wakes. It resets on teleports, body changes, invalid input, and long frame gaps.
- `WaterInteractions.hlsl` adds expanding rings to surface normals and foam. It bounds the active list by quality.
- `WaterSplashParticles` pools 256 droplets and four spatial audio voices. A generated splash clip requires no new asset dependency.
- `WaterImmersionState` smooths camera immersion and applies crossing hysteresis. Teleports set immersion without a screen pulse.
- `WaterListenerFilter` controls Unity's native listener-wide low-pass filter. Full immersion uses an 850 Hz cutoff.
- The listener wrapper restores any existing filter and removes a filter that it creates during teardown.
- `WaterReflectionCapture` uses a native, time-sliced reflection probe. Three cubemap textures separate the previous image, current image, and unfinished capture. Completed images blend over 0.35 seconds.

`SurfaceCharacterController` accepts an optional `ISwimmingProvider` capability.
The planet adapter supplies lake and ocean depth through the existing water query.
The player floats, moves horizontally, dives, and rises. The driver bounds diving above the sampled lake bed.
Existing grounded actors retain their current behavior when they do not supply the swimming capability.

Player controls: WASD moves, Space rises in water, and Ctrl dives. Space and Ctrl retain their land actions.

## Quality budgets

| Water tier | Screen-space reflection steps | Shaft steps | Maximum rings | Probe face size | Capture interval |
| --- | --- | --- | --- | --- | --- |
| Low | 8 | 8 | 8 | Disabled | None |
| Medium | 12 | 16 | 16 | 64 | 3 seconds |
| High | 20 | 24 | 24 | 128 | 2 seconds |

QualityController enables Unity realtime reflection probes for Medium and High water tiers.
The probe excludes the water layer and captures nearby geometry within 350 metres.
The surface retains screen-space reflections and the weather-aware sky fallback.
The probe fades out when the camera leaves its capture neighborhood. Submerged cameras release the probe.
`WaterCamera.hlsl` shares the camera immersion state with clouds, precipitation, dust, snow, and rain particles.
These effects fade out during immersion. Raised lakes use their local waterline instead of global sea level.

## Validation

Unity: `6000.7.0a5`. Seed: `1691104419`.
Local evidence: `local-only/underwater-reference-2026-09-07/`.

- Final `WaterPresentationTests`: 10/10 passed, job `b5ceeaabc6004996bcba18705721bd4a`.
- `CharacterMotorTests` and `ConsoleRegressionTests`: all 62 passed in job `f8df01fc19ff44a594ca478a6252181d`.
- That combined run failed only its two audio test harness cases. The final 10-test water run corrected those cases.
- Core and Planet code-health builds passed. Unity loaded the new types and shaders without compilation errors.
- A moving capsule generated one entry splash and 11 wake rings. See `water-entry-and-wake.png`.
- Off-screen reflection capture passed. Every target bounds corner was above the viewport; the minimum viewport y was 1.252228.
- `offscreen-only-enabled.png` and `offscreen-only-disabled.png` show the matched reflection comparison.
- `waterline-transition.csv` records the smooth crossing, screen pulse, and listener blend.
- Native listener output passed a camera-driven dry/wet/dry check after the editor restart.
- A 6 kHz tone measured RMS 0.010000 above water, 0.000003677 underwater, and 0.010000 after surfacing.
- `audio-native-runtime-proof.txt` records the output and filter state. Earlier custom-filter audio captures are superseded.
- The actual player driver entered lake #48, emitted one splash, reached 5.174 metres of depth, and surfaced.
- `player-swimming-proof.txt` records that sequence. `player-swim-wake.png` shows the player and wake.
- All eight affected surface, atmosphere, weather, and splash shaders reported zero compilation errors after restart.

The first focused test run failed to initialize: "Test job failed to initialize (tests did not start within timeout)".
The retry passed all 28 selected tests with a longer initialization timeout.

The audio ownership tests initially failed because EditMode did not invoke play-only lifecycle callbacks.
The corrected tests invoke the managed restoration and ownership methods directly.
An intermediary Unity message-dispatch attempt reported "[Assert] Assertion failed on expression: 'ShouldRunBehaviour()'".
The native playback check separately verifies Unity's actual listener lifecycle and output.

Unity crashed while Hot Reload patched methods on 2026-09-07. Bryan restarted the editor on 2026-09-08.
The saved stack enters `mono_optimize_branches` through `SingularityGroup.HotReload.CodePatcher:PatchMethod`.
The stack does not establish a water-rendering cause. The project completed a fresh play session after restart.

The storm test exposed atmospheric effects inside raised lakes. The shared immersion gate corrects that defect.
`largest-lake-storm-underwater-fixed.png` records the corrected view under forced storm conditions.

## Frame timing

The game view measured 2160 × 771. Each scenario warmed up for 180 frames and measured for 240 more frames.
The harness continuously submitted rings to fill each quality tier's active budget.
Storm cases used `weather.force 1 1 1` and 25 m/s wind. Calm cases used clear weather and 2.5 m/s wind.
Lake #32 is the largest generated lake: 1,378 cells, with about 102 metres of depth at the test camera.

| Scenario | Water tier | GPU average | GPU p95 | Valid window samples |
| --- | --- | --- | --- | --- |
| Calm ocean | High | 17.427 ms | 20.546 ms | 96 |
| Calm ocean | Low | 17.060 ms | 18.782 ms | 95 |
| Storm ocean | High | 19.471 ms | 26.360 ms | 85 |
| Storm ocean | Low | 19.288 ms | 24.735 ms | 86 |
| Largest lake, storm surface | High | 20.807 ms | 24.510 ms | 100 |
| Largest lake, storm surface | Low | 22.212 ms | 29.231 ms | 90 |
| Largest lake, submerged storm | High | 15.725 ms | 17.510 ms | 98 |
| Largest lake, submerged storm | Low | 14.937 ms | 17.041 ms | 93 |

The lake rows use the repeat after the underwater weather fix and settled terrain streaming.
The ocean rows use the initial pass; the subsequent weather gate leaves fully above-water rendering unchanged.
Raw measurements are in `water-quality-performance.csv` and `water-quality-performance-final.csv`.
Low reduced the requested water budgets, but did not consistently reduce whole-frame cost in the lake surface case.
These editor measurements do not establish a frame-rate guarantee or a water-only performance improvement.

## Practical limits

WaterMotion mirrors the three shader swell phases on the CPU.
The CPU water query supplies depth, but does not expose the mesh shoreline-distance channel.
Near shores, the CPU uses depth to estimate swell attenuation. Weather readback can also lag the GPU simulation.
The moving camera boundary is therefore an approximation near shorelines and rapidly changing weather.

The native probe updates over several frames and refreshes periodically. Moving off-screen objects can lag their current pose.
The shader samples its angular reflection direction. The former 350 m sphere projection distorted nearby banks into large reflected blobs and was removed on 2026-09-08.
The cube remains an approximate off-screen fallback. It does not reconstruct exact reflection geometry.
The shared sky fallback approximates volumetric clouds.

Performance figures measure the whole editor frame. They do not isolate water shader cost.
The PC platform exposes one native quality level. Low-profile checks therefore apply the water budgets on PC.
They do not establish performance on mobile hardware.
