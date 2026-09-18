# Mountains and slope rock — validation queue

Status: IMPLEMENTED CANDIDATE; Unity tests, shader import, and captures NOT RUN.
Bryan assigned Unity to another agent. This file is a work queue, not an editor reservation or scheduler.

## Intended behavior

Gentle ground keeps its biome material. Increasing slope exposes the existing Synty Mountain rock material. The existing 28–48 degree transition remains unchanged. Slope is relative to the local planet radial direction.

## Findings and changes

1. The existing slope material path was enabled. Mountains were multiplied by signed continent elevation, which already included the continent's small displacement strength. At the authored OceanDepth of 0.3, that strength is 0.055. Thus the old mask suppressed mountain relief and also made masked layers deepen oceans.
2. `NoiseFilterEvaluator.FirstLayerMask` now computes `saturate(elevation / strength)` for positive strength, otherwise zero. This removes displacement scaling from coverage and confines masked relief to land. It also affects the existing masked terrain-detail layer. World geometry will change on regeneration.
3. `EvaluateLayers` replaces duplicated layer accumulation in `TerrainFace`, `PlanetChunkMeshJob`, and `ScatterGatherBurst`. Managed `ShapeGenerator` uses the same mask helper. Existing river carving and diagnostic terrain branches remain in place.
4. The terrain grass overlay now reduces coverage by the existing rock mask. It previously applied grass after rock without consuming that mask. This changes the surface overlay; existing physical-grass slope rules remain unchanged. Biome snow and accumulated surface snow also fade with the same rock mask so they cannot fully repaint exposed cliffs.

No new texture, shader setting, noise seed scheme, or terrain resolution was added. The reference images show the intended material distribution, not a change away from the approved Synty art style.

## Required validation

| Check | Acceptance | Status |
|---|---|---|
| Refresh generated project files when editor ownership allows | River types and current shader IDs resolve; no new compiler errors | QUEUED |
| Core then Planet build, serial | Zero errors | Core passed; Planet blocked below |
| `ProceduralPlanets.Tests.NoiseFilterEvaluatorGoldenTests` and `ProceduralPlanets.Tests.ScatterGatherParityTests` | Existing single-filter goldens pass; new mask and layer tests pass | QUEUED |
| Scheduled Burst jobs for Low, High, and scatter sampling | Match managed terrain within the existing numerical tolerance; identical land-mask semantics | QUEUED |
| Fresh planets, seeds 12345 and 1691104419 | Measurable mountain relief, stable same-seed output, no missing mesh or water failures | QUEUED |
| Terrain Geography and Terrain Textures captures | At 0°, 28°, 38°, 48°, and 70° slopes, rock rises through the existing blend; full rock remains visible with grass overlay enabled | QUEUED |
| Repeat slope checks on all six faces | Material depends on radial slope, not world-up orientation | QUEUED |
| Warm, cold, coastal and inland slopes | Grass, snow, coast, and rock transitions remain intentional; inspect silhouettes and river carving | QUEUED |
| Performance and memory | Record fresh generation timing, terrain range, water bodies, scatter counts and retained memory; stronger relief can change these | QUEUED |
| Bryan's visual review | Approve the generated mountain shapes and rock distribution | PENDING |

The new tests check normalized coverage, negative and zero inputs, managed/snapshot agreement, unchanged unmasked seafloor, and default relief exceeding 100 metres. They are written but have not run. Direct snapshot calls do not prove compiled Burst parity.

## Capture procedure

Do not take Unity until its owner releases it. Capture a before state from the owner's current loaded build if that build predates these edits. Record its revision and seed. Otherwise reconstruct a before build by reverting only this task's hunks after checking for concurrent edits; do not restore whole shared files.

Archive Terrain Geography and Terrain Textures PNG files and sidecars before and after. Keep the same seed, quality, camera direction, and frozen sun. Camera height may require adjustment because the terrain geometry changes; record that adjustment instead of claiming pixel invariance. Use the slope debug view to distinguish absent steep geometry from a material-mask defect.

## Build evidence

- `local-only/mountain-review/core-build.log`: succeeded, zero warnings and errors.
- `local-only/mountain-review/planet-build.log`: failed, 13 errors. Current generated project sources omit new river types while shared callers already reference them.
- Representative exact error: `error CS0246: The type or namespace name 'RiverFieldData' could not be found (are you missing a using directive or an assembly reference?)`.
- The corresponding `RiverField` error has the same code. River source files exist under `Assets/Scripts/Planet/Rivers/`. Do not rewrite that agent's code to bypass the missing generated-project entries.

No Unity tool was used during this task.

## Rivers coordination

Rivers confirmed the ownership split on 2026-09-09. Procedural Generation owns base terrain masking and slope materials. Rivers owns river routing, channel carving, waterfall geometry, water effects, and river integration tests.

Rivers reports that it preserves `FirstLayerMask` and `EvaluateLayers`, samples the shared base elevation, and applies the channel field to both terrain backends and scatter. It adds no mountain amplification. Waterfalls require the configured terrain drop and a drop/run ratio above 1.2.

The earlier project-file build blocker is resolved according to Rivers: regenerated project files include RiverField, and its latest Planet build passed with 19 warnings. Its 76 focused EditMode tests passed before its current overlap-height refinement. Those results do not validate this terrain queue, which remains NOT RUN.

Hold shared evaluator edits while Rivers completes its current Unity captures. Do not take Unity until explicit handoff. Rivers was asked to report any terrain or slope-material issue with that handoff.

## Radius calibration correction (2026-09-09)

The first normalized-mask candidate retained amplitudes tuned for the suppressed mask. This caused excessive terrain height. Rivers added no mountain amplification. Procedural Generation owns this correction.

`PlanetDto` now budgets mountain uplift at `0.06 * MountainHeight` times planet radius, before the land mask. Default uplift budget is 150 metres on the authored 5,000-metre radius. Continental elevation adds to that uplift. Detail uses a separate `0.005 * TerrainRoughness` radius budget. Both strengths divide by the octave amplitude sum, so additional octaves cannot inflate the budget. This bounds contributions without clipping peaks into flat plateaus.

Mountain base frequency now ranges from 6 to 12 (default 7.8). Keeping the old frequency after reducing amplitude produced almost no steep rock faces. The revised frequency restores steep faces at the smaller scale. Continent generation, seed mapping, river carving, and atmosphere settings remain unchanged.

The atmosphere controller receives the maximum terrain radius from `PlanetGeneratedEvent`, then multiplies it by `AtmosphereScale` (authored 1.15). Its outer boundary already extends beyond terrain. Excessive relief can still rise above the dense visible atmosphere. Enlarging the atmosphere would hide the terrain-scale defect rather than correct it.

### Offline calibration evidence

The probe links the actual Noise and NoiseFilterEvaluator sources and explicitly reconstructs the authored recipe. It samples 65,536 directions per seed. These are sampled maxima, not exact mesh maxima or Unity visual results. Slopes use finite differences at 0.0005 radians.

| Seed | Previous maximum altitude | Corrected maximum altitude | Corrected land at >=28 degrees |
|---|---:|---:|---:|
| 12345 | 1,929.10 m | 273.76 m | 4.15% |
| 1691104419 | 1,694.81 m | 274.68 m | 3.09% |
| 42 | 1,690.96 m | 252.66 m | 3.38% |

Probe and results: `local-only/mountain-scale-probe/Program.cs`, `frequency-results.log`.

Core build passed with zero warnings/errors. Planet build passed with 19 warnings and zero errors. Logs: `local-only/mountain-scale-probe/core-build.log` and `planet-build.log`.

The earlier test's absolute 100-metre threshold was incorrect for PlanetSettings' 50-metre class default. It now tests relative uplift and the default relief budget. New recipe tests cover radii 1, 50, and 5000, mountain control endpoints, and all detail octave counts. Unity execution remains queued with Rivers. The prior paragraph describing a 100-metre test is superseded.

Rivers granted a source-editing window and received confirmation when writes finished. Rivers retains Unity ownership and will regenerate with this correction. Final mesh height, waterfall continuity, shader import, and visual rock distribution remain pending Unity validation. No generation-time improvement is claimed.

### Unity test update from Rivers

Rivers reports job `dc9948b0d29a49558c7e73b1e108fd04` completed 92 tests after clean import. `NoiseFilterEvaluatorGoldenTests` and `ScatterGatherParityTests` had no failures. The combined run had one failure: `ConsoleRegressionTests.PastePreservesTabsAndRejectsMultilineInput`, expected `"echo alpha"`, got `string.Empty`. The combined suite is not green. Corrected-terrain play generation and actual-height captures are in progress; visual validation remains pending.

### Generated height verification

Rivers reports a completed Unity generation for seed `1691104419`, base radius `5000`: `ElevationMax = 0.0576251745`, maximum altitude `288.1259 m`, outer terrain radius `5288.126 m`, and atmosphere radius `6081.34473 m`. The atmosphere extends about `793.219 m` beyond the highest terrain. This generated maximum supersedes the offline sampled maximum for this run. Mountain visual review remains pending.

The previous river waterfall criterion (`drop/run > 1.2`) produced no falls on the corrected terrain. Rivers owns the follow-up channel-step criterion and descent carving. It will preserve base terrain height and the shared evaluator.

### Final focused validation and capture review

Rivers reports focused job `f63987892d4540d384d7601ba821a2da` passed 46/46 tests, including terrain golden and scatter parity fixtures. The earlier combined suite's console clipboard failure remains separate. Corrected terrain supports 13 generated waterfalls after river-only descent changes.

Procedural Generation inspected `local-only/river-validation/waterfall-final-corrected.png`: the carved steep face exposes rock while adjacent gentler ground retains grass. The falling water sheet still has geometric edges; Rivers recorded that refinement separately.

After the Unity handoff, MCP responded successfully. Captured `local-only/mountain-scale-probe/mountain-silhouette.png` and `mountain-close.png`. The wide view has substantial cloud obstruction. The closer view shows connected rocky ridges, vegetated gentler slopes, and a visible skyline beneath the clouds. This is a spot check, not completion of the six-face and climate matrix above. Camera pose was saved and successfully restored. No scene or source changes were made during capture review.

One transient capture-script compilation failed with `Line 1: Argument 1: cannot convert from 'UnityEngine.Vector3' to 'float'` (twice). The script was corrected to pass SampleElevation output to GetScaledElevation and then executed successfully. This was in-memory diagnostic code, not a project compilation failure.
