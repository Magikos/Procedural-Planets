# Performance follow-up — 2026-09-08

Status: Code implemented or reconciled with existing implementation. Targeted tests and Editor measurements passed.
Current next action: Bryan reviews motion quality during play. Standalone-player profiling remains separate follow-up work.
Tree: dirty working tree on `d1e0f62`; unrelated changes preserved. No commit created.

## Implementation

Bryan approved plans 007–010 and handed this task Unity.

- **007:** Extended FrameTimingCounters with creature simulation, creature presentation, scatter planning, commits, and draw submission.
  Each section measures exclusive synchronous main-thread work. Metadata and overlay enumerate every section.
  ActorAnimationGraph and ProceduralPoseRig expose nested profiler markers; these do not enter the exclusive section sum.
- **008:** Reconciled with the existing ActorPoseCadence and ActorAnimationGraph implementation already present at handoff.
  Did not create a second cadence policy or change its quality defaults.
  Verified near/full-rate behavior, staggering, action changes, lifecycle cleanup, and animation time across skipped evaluations.
- **009:** Replaced repeated full-window biome counts with horizontal sliding counts.
  Preserved kernel, padding, top-K tie order, normalized weights, and color arithmetic.
- **010:** Added WaterBodyMap.SampleStateAndLevel and captured the water map once per grid bake.
  Existing Sample and LevelAt remain available. Null-map, missing-level, and NoWater fallback behavior remains unchanged.

Instrumentation uses the existing services. The only scope extension beyond the original plans is the shared Game animation implementation.
Those shared files now own animation and pose evaluation, so the profiler markers belong there.

## Evidence

Core and Planet builds passed serially. The final Planet build also built Magikos.Game.
The compiler reported existing analyzer-version, unreachable-code, obsolete API, unawaited-call, and legacy-field warnings.
No new build errors remain. The initial test compile error was fixed:
`error CS1503: Argument 1: cannot convert from '<null>' to 'DebugCaptureContext'`.
The test now passes `default` for the value-type context.

The first test discovery omitted newly created files; forced asset refresh corrected discovery.
Do not count that partial discovery as new-fixture validation.

- Focused job `c536c8faa9014ad5b4ac8eac6239e968`: **17 passed, 0 failed, 0 skipped**.
- Regression job `58ffa77ca0044d04a85fcc7f96192815`: **129 passed, 0 failed, 0 skipped**.
- Total for the final two non-overlapping runs: **146 passed**.
- Biome tests compare every color, ID, and weight byte with the retained exhaustive reference.
  Cases include uniform, striped, checkerboard, random, out-of-range IDs, short LUTs, changing active ranges, and concurrent workers.
- Water tests compare combined and independent queries across all face-grid vertices, including cube edges and corners.
- Timing tests cover every enum value, storage size, metadata output, and reset.

## Matched CPU measurements

These measurements isolate code costs. They do not predict whole-game FPS or whole-generation speedups.
All medians below use three runs.

| Workload | Original/full-rate median | Optimized/budgeted median | Reduction |
|---|---:|---:|---:|
| Smoothing eight fixed random biome grids | 213.021 ms | 62.090 ms | 70.9% |
| Two water queries versus combined query, 100,000 fixed directions | 43.725 ms | 21.508 ms | 50.8% |
| Same 22 creature snapshots, 120 view updates per run | 6.796 ms/update | 2.185 ms/update | 67.9% |

Creature comparison uses separate presentation instances, shared immutable actor snapshots and library, identical grounding, and the same observer camera.
Audio and grass interactions were excluded from both instances. Authority simulation did not run inside these synchronous measurement loops.
The loops used the captured Time.deltaTime; these are isolated view measurements, not 120 independently rendered frames.
Budgeted p95 median was 3.432 ms versus 8.902 ms for full-rate presentation.

The near test placed the same 22 snapshots within 10 m of the camera.
All 2,640 poses evaluated in every 120-update run; none were skipped.
Median average was 6.308 ms budgeted versus 6.437 ms full-rate.
Median p95 was 8.393 ms versus 8.466 ms. One individual run exceeded 5% p95 difference; aggregate medians did not.
This timing check does not replace human motion review.

10,000 timing scopes took 0.7751 ms in one measurement.
This measures scope overhead only; overlay formatting and profiler instrumentation overhead were not isolated.

## Editor runtime observations

Seed: world 12345, planet 1691104419. PC quality, High clouds.
Fresh generation: initialize 3,008 ms; terrain 10,725 ms; lake 1,125 ms; colors 19,889 ms;
climate 394 ms; water 6,426 ms; finalize 4,053 ms; total **45,624 ms**.
Biome map bake elapsed 6,797.6 ms across 1,536 chunks.
Worker CPU sums: high-resolution grid 109,114.0 ms; top-K 19,623.7 ms. These are not additive elapsed times.

Three stationary 120-frame windows averaged 23.39, 24.18, and 28.39 ms whole-frame CPU.
Their p95 values were 30.51, 33.58, and 37.07 ms.
The scatter queue was empty, with 4,676 tiles and 254,045 instances.
These are observations on this checkout, not matched performance claims against historical runs.

A 300 m camera movement settled at 4,604 tiles, 294,861 instances, and zero queued work.
A separate 180-frame, 600 m movement capture observed planning max 9.82 ms and commit max 4.44 ms.
The commit budget is checked between work units; it is not a strict upper bound on one unit.
The camera restored after both probes. Unity remains playing, as handed over.
The console exception query returned no entries after runtime checks.

## Evidence files and limits

- [Focused test results](../../plans/2026-09-08-performance-focused-results.json)
- [Creature benchmark](../../plans/2026-09-08-actorBenchmark.json)
- [Near-creature benchmark](../../plans/2026-09-08-actorNearBenchmark.json)
- [Water benchmark](../../plans/2026-09-08-waterBenchmark.json)
- Stationary windows: `plans/2026-09-08-frame1.json` through `frame3.json`.
- Movement snapshot: `plans/2026-09-08-movingFrame.json`.
- Local screenshot and metadata: `local-only/debug-screenshots/baselines/2026-09-08-performance/F10-water.00-Off-performance-followup-20260908-132957-322.*`.
- Local movement trace: the same directory, `scatter-flight.json`.

No standalone-player capture or matched three-run whole-startup comparison was performed.
No new visual cadence defaults were introduced in this pass. Existing cadence quality still needs Bryan's review during play.
No new loading-progress trace was recorded; prior progress repairs remain covered by the earlier investigation.
Burst or compute migration remains deferred until the remaining grid/terrain costs are measured separately.
