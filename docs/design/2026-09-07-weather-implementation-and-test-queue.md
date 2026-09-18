# Weather implementation and validation

Bryan authorized the implementation and full Unity control on 2026-09-07. Work now lives in the active source tree. The staging candidate is historical: **do not apply or build it as the current implementation**.

## Current implementation

- Rain and snow share persistent GPU particles near the observer. Camera movement only changes the spawning region. Existing drops retain their world positions and trajectories.
- Segment collision checks select the first terrain, water, box, sphere, capsule, character-controller, or readable mesh contact. Birth checks reject particles beneath shelter. Nearby overhead colliders enter the shared broad phase.
- Water contacts produce expanding rings. Ground contacts produce short impact marks. Impacts remain fixed while the camera moves.
- Snow uses temperature-based particle selection and the existing snow count, size, color, opacity, and fall-speed settings.
- A stationary global surface map accumulates wetness and snow. Warmth, dry air, and wind remove moisture; warmth melts snow. Terrain shading reads this map.
- Climate moisture replenishes global weather. A dry region gradually exhausts humidity, rain, and cloud support.
- An advected direction map retains wind history for cloud detail, shadows, and curtain patterns. Changing wind no longer rotates all past displacement instantly.
- Distant curtains fade into the local particle region. Their render pass follows atmosphere and clouds. Rain reaches sea level.
- Rain computation stops when disabled, underwater, above the local ceiling, or suppressed by diagnostic rendering.
- Cloud flashes drive delayed positional thunder using three owned UniStorm clips. Playback has four bounded voices and rejects paths through the planet.

## Validation record

Unity: 6000.7.0a5, Direct3D 12. Planet scene, seed 1691104419, High quality. Captures use 3840 x 1371 output.

Twelve GPU EditMode tests passed in 0.437 seconds, job `3e788f8fa8a74a59a35499f7ab98e2fe`:

- Camera translation and rotation cannot drag existing drops.
- Reset seeds the local rain column even when cloud base is distant.
- Thin roof, player capsule, mesh triangle, terrain, and elevated lake contacts stop drops.
- A nonuniform terrain texture catches shifted face coordinates.
- Impacts persist before recycling.
- Disabling snow presentation does not convert cold precipitation to rain.
- Desert moisture depletion stops rain and dissipates clouds without instant deletion.
- A wind change preserves accumulated cloud coordinates.
- Rain wets terrain; dry air dries it; cold precipitation accumulates snow; warmth melts snow.

Live Core and Planet projects built with zero errors. Core had two analyzer version warnings. Planet had analyzer and existing ScatterHarvestStore warnings. Logs live in `local-only/weather-validation/2026-09-07/`.

The fresh run produced 3,929 water impacts, zero terrain impacts, and 886 particles within ten metres at the ocean test position. Captures confirm visible local rain, water rings, and soft snow flakes. Visual validation exposed and corrected terrain UV remapping, square snow interpolation, near-sea curtain fading, and curtain composition order.

The final fresh run completed generation in 37.013 seconds. Core and Planet builds remain at zero errors. The final runtime probes found zero active drops beneath a test roof. Underwater and orbital positions disabled local particle drawing.

Fixed-camera captures show the distant curtain with no local drops, its matching cloud flash, local rain with water impacts, soft snowfall, and dry/wet/snow terrain states. The artificial circular storm fixture deliberately isolates one column; it is not a natural weather-shape example. Surface shading captures use forced state maps; GPU tests separately verify accumulation and drying.

| Fixed warm storm, 30,000-particle budget | CPU average / p95 | GPU average / p95 |
|---|---|---|
| Local particles disabled | 17.726 / 21.685 ms | 16.981 / 20.944 ms |
| Local rain enabled | 18.298 / 22.233 ms | 17.197 / 21.194 ms |
| Cold snow enabled | 18.120 / 21.761 ms | 17.195 / 20.157 ms |

Rain added 0.572 / 0.548 ms CPU and 0.216 / 0.250 ms GPU in this paired sample. CPU windows contain 120 frames. GPU windows contain 86–95 valid samples. These are Editor whole-frame measurements, not an isolated weather-stage benchmark or a device-wide performance guarantee.

Final evidence labels: `distant-storm-no-flash` (07:41:21), `distant-storm-flash` (07:41:41), `rain-water-complete` (07:42:56), `snow-complete`, `terrain-dry` (07:44:45), `terrain-wet` (07:45:32), and `terrain-snow` (07:45:58). Tests and timing data are archived as `final-gpu-tests.json` and `performance.json`.

Unity has exited Play mode. Diagnostic weather, camera, time, and temperature overrides were temporary. The Planet scene retains only the listener and intended precipitation defaults.

The final shader inspection found zero errors in Clouds, Precipitation, RainParticles, and Planet/VertexColor. Unity reported the unrelated existing grass warning: `Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`.

Thunder playback created a spatial `Thunder-4` voice with one active scene listener. Earlier lifecycle probes verified 343 m / 1 second and 686 m / 2 seconds, four-voice bounds, planet occlusion, and cleanup.

Graphify update completed with 11,302 nodes, 16,234 edges, and 862 communities. The graph exceeded the HTML visualization limit; JSON and the report updated successfully.

## Evidence corrections

The first `before` and `after` archives do not prove local rain: `weather.force 1 1` sets zero rain. The new optional third argument explicitly sets rain: `weather.force 1 1 1`. The earlier camera at radius 5050 also did not establish correct terrain contact.

The initial collision implementation remapped already normalized face UVs. This moved its sampled terrain. The corrected -Z test location has terrain below sea level, so the appropriate receiving surface is water at radius 5000. The nonuniform texture regression test covers this error.

Captures and sidecars are archived under `local-only/weather-validation/2026-09-07/implementation/`. Earlier images with `final` in their names still precede the composition and impact-opacity corrections. Use timestamps and this record, not the label alone.

## Boundaries

- Visible lightning bolts and ground strikes remain the user's later phase.
- Mesh colliders require readable meshes or primitive colliders. An unreadable mesh produces one warning instead of silently claiming collision support.
- Water contacts use the shared water-level field. The small rings do not displace the water mesh or simulate fluid volume.
- Surface wetness and snow are geographic terrain state. They do not yet shade future building materials or resolve roof shelter at building scale.
- Lightning shape is seeded. Multiplayer strike scheduling still needs an authority-owned tick.
- Agent visual inspection verifies defects and behavior. Bryan's review determines the final artistic look.

## Audio provenance

Owned UniStorm product 2714, recorded in `docs/research/2026-08-10-external-asset-catalog.md`. The three clips come from `D:/Unity/Explore Assets/Assets/UniStorm Weather System/Sounds/Thunder/Thunder 4.wav`, `Thunder 5.wav`, and `Thunder 6.wav`.

Resource names: `Weather/Thunder/Thunder-4`, `Thunder-5`, and `Thunder-6`. Durations: 6.97, 6.36, and 6.07 seconds. Original sample bytes remain intact. Mono import supports positional playback. No UniStorm runtime code was copied.
