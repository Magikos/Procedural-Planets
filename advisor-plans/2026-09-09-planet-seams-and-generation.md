# Planet seams and generation

Bryan approved the Synty terrain appearance on 2026-09-09 and requested seam fixes and generation-time work.

## Changes

- Biome padding reads elevation and climate from neighboring terrain chunks. The old path clamped these inputs to each chunk's edge.
- Climate preparation finishes before biome baking starts. Leaf climate arrays remain available until every map finishes.
- Cube-face atlas borders use a deterministic owner. This also gives all three copies of each cube corner identical IDs, weights, and colors.
- Smoothing maintains vertical column histograms and slides the horizontal sum across them. Weighted secondary biome membership remains intact.
- Parallel color work observes cancellation. Cleanup returns to Unity's main thread.

No terrain resolution, noise settings, blend width, or texture settings changed.

## Validation

Acceptance: shared chunk edges must match with varying elevation and climate. Cube-face edges and corners must match. Optimized smoothing must match exhaustive output bytes.

- Core: zero warnings and errors.
- Planet: zero errors; 19 existing warnings.
- Unity job `dc1bf5b5c18b4eea87be9e8e16b39e0a`: 25 passed, zero failed, 4.896 seconds.
- Tests include horizontal and vertical varying-input seams, every cube edge and corner, weighted smoothing, invalid IDs, storage reuse, and parallel workers.
- A live probe baked six adjacent chunk pairs from the generated planet: zero mismatched edge texels.
- The existing scatter warnings concern missing impostor cards and bright foliage tints. No new runtime error appeared in the first fresh generation.
- Graph update completed: 14,368 nodes and 20,732 edges.

## Measurements

Seed 1691104419, world seed 12345, PC quality, 2,046 chunks, 1,536 baked leaf maps. All durations below are milliseconds.

| Run | Biome map wall time | Smoothing summed worker CPU | Full generation |
|---|---:|---:|---:|
| Before | 6377.2 | 42472.7 | 34993 |
| First corrected run | 6997.4 | 11525.6 | 38850 |
| Corrected run, graph update complete | 6391.2 | 9573.2 | 39217 |
| Final import and restored play run | 6881.9 | 10770.1 | 36674 |

The corrected runs reduced smoothing CPU by 74.6–77.5%. Biome-map wall time remained near its original cost. It did not improve total generation time. Neighbor sampling adds work, and other phases varied between runs. Do not present the smoothing CPU reduction as a total startup reduction.

A later run overlapped graph extraction and is excluded from performance conclusions.

## Evidence and replay

Evidence root: `local-only/planet-seams/`.

- `before/`: original Terrain Textures capture set and sidecars.
- `after/`: orbital capture from a startup camera reset; not a matched comparison.
- `after-surface/`: restored surface camera and sun, final Terrain Textures capture set.
- `after-timings.txt`: final fresh generation after graph extraction finished.
- `before-timings.txt`, `core-build.log`, `planet-build.log`, and `graphify.log`.

Capture pose: position `(4316.23, -2448.18, -1208.83)`, forward `(0.0935, 0.9945, 0.0462)`, up `(0.9712, -0.0809, -0.2241)`. Sun direction `(0.9981, -0.0562, -0.0244)`, frozen. The restored pose uses the rounded baseline sidecar values; weather and vegetation animation are not frozen.

For replay, start a fresh Planet scene with the recorded seed and quality. Wait for the `Generation timings` log before restoring the capture camera. Run the Terrain Textures capture set and archive its PNG files and sidecars. Do not run builds or graph extraction during performance measurement.

## Limits

The new cube-edge ownership applies when building complete face atlases. The unused single-leaf `RebakeBiomeMapsAt` hook still needs a neighborhood update contract before interactive biome editing ships. No current caller uses that hook.

The existing grass shader warning also appeared: `use of potentially uninitialized variable (LoadPathWearTexel)` at `GrassNearFieldPlace.compute(250)`.

Climate arrays now remain alive across the bake. At the recorded depth, the 2,046 climate grids contain about 294 MiB of Vector4 data. This increases temporary memory use; they are released afterward. This is a calculated allocation size, not a measured peak. Generation-time work should next target climate evaluation and assignment-field sampling, which dominate the remaining biome cost.

Final state: Planet scene playing, restored surface review camera. No scene was saved. The final cleanup guard compiled and ran in a fresh generation after the 25-test run.
