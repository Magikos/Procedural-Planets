# Performance validation queue — 2026-09-08

Status: IMPLEMENTED / PARTIALLY VALIDATED — 146 final tests passed; runtime measurements completed. Broader capture acceptance remains open.
This file records work to perform after implementation and explicit Editor handoff.
It does not create a scheduler, reserve Unity, or authorize interrupting the current owner.
Current next action: review [implementation results and explicit limits](../docs/design/2026-09-08-performance-followup.md).

## Handoff validation result — 2026-09-08

Unity MCP job `e2cefd4fddf14cf5ac6dbbb8de4bba76`: 125 passed, 0 failed, 0 skipped, 3.5318042 seconds.
Fixtures: CreatureResidencyTests, CreaturePersistenceTests, CreatureHuntTests, CreatureAudioTests,
ChunkSurfaceRaycastTests, ScatterDrawBucketsTests, ScatterRenderingRegressionTests, ScatterGatherParityTests,
and ShaderAuditRegressionTests. This is baseline regression evidence, not completion of Q2–Q7.

The handed-off Editor was playing, unpaused, in `Assets/Scenes/Planet.unity`.
Main Camera position: (4271.359, -2564.41284, -865.09906).
Rotation quaternion: (-0.124103047, -0.1382998, -0.8338291, 0.5198083).
One existing 120-frame CPU window averaged 28.1784225 ms, p95 62.1376 ms.
This is an uncontrolled handoff sample, not a matched before/after baseline.
The existing generation log reported total 54,699 ms and colors 20,802 ms.
Concurrent source changes prevent attributing differences to the earlier performance repair.
CreatureView.Sync now receives an observer camera; reconcile plan 008 before editing.

Console warning observed before tests:
`Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`

## Execution order

| Item | Prerequisite | Work | Status |
|---|---|---|---|
| Q0 | Editor handoff; capture before each optimization | Record seed, settings, camera, actors, generation timings, and three 120-frame samples | PARTIAL; matched microbenchmarks and three runtime windows; no matched whole-startup baseline |
| Q1 | Each plan implemented; Editor available | Import scripts; compile; build Core then Planet serially | PASSED |
| Q2 | 007 imported | FrameTimingSectionTests; verify overlay/capture sections and overhead | PASSED; scope overhead measured separately |
| Q3 | 008 imported | CreaturePresentationCadenceTests; creature behavior regression suite; fixed-actor runtime comparison | PASSED; reused existing implementation |
| Q4 | 009 imported | BiomeMapBakerParityTests; exhaustive output parity; isolated and fresh-run timing | PASSED; isolated median 70.9% reduction; one fresh run |
| Q5 | 010 imported | WaterBodyCombinedSampleTests plus biome parity; isolated and fresh-run timing | PASSED; isolated median 50.8% reduction; one combined fresh run |
| Q6 | Optimizations pass individually | Combined startup and camera-flight run; scatter backlog and loading-progress checks | PARTIAL; startup and camera movement passed; no new progress trace |
| Q7 | Captures collected | Bryan reviews nearby movement, distant transitions, feet, birds, and biome seams | OPEN; no new cadence defaults introduced |

## Operator procedure

1. Obtain the Editor handoff from its current owner. Record current scene, play state, camera, and modified settings.
2. Read the selected plan and recheck source drift. Do not compare different agents' simultaneous changes as one optimization.
3. Capture the immediate pre-change baseline before each optimization. Historical numbers below are context, not acceptance evidence.
4. Import scripts. Run `dotnet build ProceduralPlanets.Core.csproj`, then `dotnet build ProceduralPlanets.Planet.csproj`.
5. Discover Unity MCP schemas. Submit `run_tests(mode="EditMode", test_names=["ProceduralPlanets.Tests.<fixture>"])` for the selected fixture.
6. Poll the returned job with `get_test_job`. Record passed, failed, skipped, and exact failure output.
7. For Q3, include existing CreatureResidencyTests, CreaturePersistenceTests, CreatureHuntTests, and CreatureAudioTests.
8. For Q6, include ChunkSurfaceRaycastTests, ScatterDrawBucketsTests, ScatterRenderingRegressionTests, ScatterGatherParityTests, and ShaderAuditRegressionTests.
9. Run performance comparisons without unrelated Editor operations. Record Editor and standalone-player results separately.
10. Archive captures and timing JSON under `local-only/debug-screenshots/baselines/2026-09-08-performance/`, grouped by plan and before/after.
11. Restore the prior Editor state and settings. Update each queue row with its evidence path and actual result.

All new fixtures now exist and were discovered in the final focused run. Do not report a zero-test run as success.
Use the project's existing NUnit framework. No package installation is required by these plans.
If a baseline cannot be captured before editing, keep the performance verdict pending; do not revert shared work to obtain one.

## Historical reference, measured earlier on 2026-09-08

- World seed: 12345. Planet seed: 1691104419.
- Camera position: (2259.57764, -11957.7979, -5199.399).
- Camera rotation quaternion: (-0.0816446543, 0.5427535, 0.834172249, -0.05393935).
- Repaired Planet.Update: average 14.7912 ms, p95 17.6152 ms, 120 frames.
- Whole frame: average 26.9121 ms, p95 31.7142 ms, 120 frames.
- Isolated creature view sample: 10.5165 ms for 25 creatures. Dynamic actors prevent deterministic comparison with earlier runs.
- Fresh startup: total 42,257 ms; colors 19,161 ms; terrain 10,489 ms; water 5,411 ms; finalize 2,946 ms.
- Biome map bake: 1,536 chunks. Worker timing aggregates are CPU sums, not additive wall-clock durations.
- Prior evidence: `docs/agent-conversation/performance-2026-09-08/results.md` and adjacent JSON files, when present locally.

## Combined acceptance

Apply each plan's numeric criteria. Also require clean startup, monotonic progress, no early Planet ready report,
no renewed per-frame scatter log flood, and a settling scatter queue at a stationary camera.
Record settings and creature counts for every sample. Do not infer GPU saturation from CPU subtraction.
Report test failures and unmeasured targets explicitly. Builds alone never complete this queue.
