# Planet and biome fixes

Implemented the seven confirmed defects from the planet and biome audit.
Bryan approved the corrected seed algorithm for all callers, without a compatibility mode.
Existing seeds now generate different terrain, biome warping, and weather noise.

| Finding | Change |
| --- | --- |
| G01 | `NoiseData.Create` shuffles the permutation using all seed bits. Managed filters and job snapshots share this constructor. |
| G02 | Biome baking retains resolver membership and accumulates weighted biome counts. Lake shores now fade into land. |
| G03 | Bake sample grids include shared chunk endpoints. The diagnostic shared edge now has identical colors, IDs, and weights. |
| G04 | Low terrain generation completes jobs and disposes their arrays in one owner-controlled cleanup path. Cancellation preserves `OperationCanceledException`. |
| G05 | Low terrain and water owners retain mesh references and destroy them during cleanup, even after child destruction. |
| G06 | Low terrain faces inherit the planet transform with identity local transforms. |
| G07 | Water-map solving and water-mesh computation observe cancellation internally. Canceled water-map rebuilds do not publish a replacement. |

Low biome color generation also returns to the main thread on cancellation or failure.
Water shader level fields now publish after successful mesh computation using the same captured map.

## Validation

- Unity EditMode: 72 passed, 0 failed, 0 skipped. Job: `fa81877c3b0a4d52b0160165107dbd73`.
- Fixtures: `PlanetGenerationRegressionTests`, `NoiseFilterEvaluatorGoldenTests`, `BiomeMapBakerParityTests`, `WaterBodyCombinedSampleTests`, and `ConsoleRegressionTests`.
- New regressions cover seed collisions/repeatability, shore membership, shared edges, transform inheritance, mesh disposal, and cancellation.
- Core build: 0 errors, 0 warnings. Planet build: 0 errors, 19 existing warnings in analyzers and unrelated source.
- Targeted `git diff --check` passed. Graph update completed with no reported output.
- A complete High-resolution planet generated in Play mode: seed 1691104419, world seed 12345, 2046 chunks, 1536 leaf biome maps, 108 water bodies.
- Water rebuild cancellation completed in 37.6 ms and retained the current map. The earlier probe took 6303.9 ms and replaced the map after cancellation.
- The synthetic atlas probe changed from 63 copy-order-dependent pixels to zero.
- The dry-shore probe now gives 128/255 land membership halfway through the fade and 255/255 beyond it.
- Fifteen before captures and fifteen after captures include camera and runtime sidecars. The flat-color and blend views were inspected.

The expanded test run initially stalled in a retry that awaited a Play-mode frame from Edit mode.
That run was canceled with `Cancelled by user`; the test harness was corrected and all 72 tests then passed.
This was a test-harness failure, not a passing result for the canceled run.

## Evidence and limits

Evidence root: `local-only/debug-screenshots/baselines/2026-09-09-planet-biome-fixes/`.
Build logs: `local-only/planet-fixes-core-build.log` and `local-only/planet-fixes-planet-build.log`.
The working tree contains other agents' changes. No commit or scene save was made.

Generation took 37.1 seconds in the final probe versus 34.3 seconds in the earlier audit probe.
These are single runs with different generated terrain; they do not establish a performance regression or speedup.
Weighted biome accumulation adds work. Broader optimization remains separate from these correctness fixes.

The captures show the intentionally changed world. They do not establish final visual acceptance or cover every seed and camera.
The endpoint test isolates assignment-field coordinates using constant elevation and climate.
It does not certify all biome seams when neighboring chunks have different interpolated elevation or climate samples.
Actual Burst job arithmetic can still differ slightly from direct managed evaluation, as recorded in the original audit results.
