# Shared actor animator

## Implementation contract

Bryan authorized implementation and Unity validation on 2026-09-08.
Preserve existing creature behavior, authored clips, saved carcasses, and authority boundaries.
Use owned Fimpossible sources as references. Do not install vendor runtimes in the game project.
Bryan completed the scratch-project installation. The follow-up review covered Leaning Animator, Bones Stimulator, Ground Fitter, and Animation Designer.

The runtime owns one ordered pose pipeline: restore, sample clips, capture, body support, spine, gaze, secondary chains, feet, interaction limbs.
Clip playback and cosmetic posing consume gameplay state. They do not decide damage, resource consumption, or interaction success.

## Implemented work

- Weighted gaze with influence fading and stable tracking behind the actor.
- Named limb position/orientation targets using the existing shared IK solver.
- Optional analytic ground collision for tails, ropes, and other secondary chains.
- Bounded terrain-supported death settling, followed by skin clearance and frozen carcass presentation.
- Shared pose cadence with full-rate nearby actors and explicit clip clocks across skipped samples.
- Editor clip variants with speed changes and local position offsets, preserving source assets.
- Continuous Generic clip reversal, including weighted tangents and event timing, with stepped/object-reference reversal rejected.
- Signed forward/backward/sideways acceleration lean in the actor's gravity frame.
- A visible two-hand skeletal fixture with draggable targets.

Supported death settling fits an authored fallen pose. It is not active ragdoll walking, impact-driven joint physics, or a physical corpse collider.
The pose remains cosmetic. Authority-owned root position, resource stock, and combat results remain unchanged.
Frozen local pose entries refer to the current rig instance; they are not a network packet or save format.
Carcasses rebuild from their existing saved appearance identity, death clip, root pose, and current terrain.

## Acceptance checks

- Existing EditMode tests and new targeted regressions pass without relaxed precision thresholds.
- Ground and bird skipped samples reach the same clip pose as full-rate sampling.
- Gaze remains stable across the rear angle boundary and fades to the authored pose.
- Hand position/orientation targets preserve segment lengths and release cleanly.
- Secondary chain contact preserves lengths and reports infeasible penetration instead of stretching.
- Death support handles flat, sloped, and sideways-gravity surfaces; frozen poses issue no more terrain queries.
- Carcass flesh and exposed bones use the same final pose.
- Near presentation CPU mean/p95 regress by no more than 5%; mixed-distance CPU mean falls at least 25%, with p95 no worse.
- Runtime review includes swimming, bird flight/landing, hand targets, terrain death poses, and visibility transitions.

Performance measurements must state their workload and limits. Isolated CPU pose timing is not a GPU or whole-planet frame-rate result.
Dynamic scheduling is the first optimization. Transform access and Playables evaluation stay on Unity's main thread.
Burst/jobs or compute belong only in a measured numerical bottleneck with data separated from Unity objects.

## Validation log

The initial 8-test run found a small bird pose mismatch after skipped updates.
The two paths used different clip-clock advancement. Both now use explicit clock advancement followed by zero-delta sampling.
The unchanged parity test then passed. The next focused run passed all 17 tests.
The preceding full-suite run passed 684 tests (job `8569b9d5815d4f2a906840d1734bfcb6`).
After the additional source review, 13 focused tests passed (job `451899208939418388e5ff541282292d`).
These cover signed acceleration, turning, teleport reset, secondary contact, clip timing, weighted reversal, and rejected authoring operations.
Core built with zero warnings/errors. Planet built with zero errors and 21 existing warnings, including analyzer-version and unrelated source warnings.
The Unity refresh readiness wait timed out after 60 seconds; a subsequent state check confirmed compilation completed before the test run.

The isolated presentation benchmark used 25 deer/wolf views, three 120-sample windows per condition, and fixed 1/60-second advancement.
The mixed case contained five nearby, ten distant visible, and ten distant hidden views.
Median window mean CPU time fell from 3.1949 ms to 1.4468 ms. Median window p95 fell from 4.1059 ms to 2.2740 ms.
Pose evaluations fell from 3,000 to 1,000 per window. The measured loop allocated zero managed bytes.
The nearby case contained substantial timing noise; it does not establish a large nearby speedup.
Raw results and the reproducible snippet are in `local-only/ecosystem-prep/actor-animator/cpu-benchmark.txt` and `cpu-benchmark.cs.txt`.
This benchmark preceded the signed-acceleration correction. It measures presentation CPU work, not GPU or whole-planet performance.

The shared review scene is `Assets/Scenes/Tests/SharedActorAnimationReview.unity`.
It contains the existing animal previews, a two-hand skeletal fixture, and matched authored/supported deer death poses.
Use right mouse drag to look, WASD to move, Q/E for vertical movement, and Shift for faster movement.
Matched slope captures are `slope-authored-final.png`, `slope-supported-final.png`, and `slope-bones-final.png` under the benchmark folder.
The hand fixture is not a complete humanoid character. Death support is not active ragdoll physics.
The follow-up fresh Play run reported one active animal prototype and completed deer death settling.
`source-review-runtime.png` records the running scene. The console exception query returned no entries.
The technical closeout completed the Planet swimming probes, combined bird transition probe, and grass A/B measurement.
User visual acceptance remains separate from these technical results.

## Planet and bird closeout

Fresh Planet run: seed `1691104419`, radius `5257.269`, 95 water bodies.
Nine configured ground species passed movement, turning, and stopping in two actual lakes and one ocean (27 cases).
The probes used the live residency service's gravity, terrain, and water providers. They did not modify resident actor records.
The maximum final root-depth error was 0.0029 m; every swimming pose had zero planted feet and full swim weight.
Deer, rabbit, and wolf also entered and exited a real 50 m lake transect successfully.
The saved `planet-swim-closeout.png` shows temporary posed deer/rabbit models in the real lake surface.
It verifies submersion, not a recorded live crossing. The exact historical GIF location was not reproduced.
Temporary models were removed after capture; no test food or water objects were added to Planet.

The configured Eagle passed flight, landing, perch, threat-driven takeoff, and hidden-to-visible refresh through the existing residency fixture.
The combined probe evaluated 19 poses and skipped 128. Both feet planted; recorded foot error rounded to 0.0000 m.
The first probe failed because it repeated its own threat trigger and overwrote its departure height after takeoff.
The corrected probe passed without runtime source changes. Its cleanup restores the live service's console registration.
Reproducible probe snippets and JSON results are in `local-only/ecosystem-prep/actor-animator/`.

## Grass closeout

The A/B used one actual nearby rabbit, fixed camera, frozen simulation, and frozen local noon.
Three windows per condition contained 120 CPU samples each. GPU windows contained 90–96 valid samples.
The animal source count changed from zero to one as expected. Existing pool tests cover the three-animal cap and player priority.

| Condition | NearGrass CPU means (ms) | NearGrass CPU p95 (ms) | Whole-frame GPU means (ms) | GPU p95 (ms) |
|---|---|---|---|---|
| Animal interaction off | 0.0471, 0.0572, 0.0504 | 0.0645, 0.1671, 0.0766 | 40.96, 43.92, 43.73 | 91.74, 83.89, 91.02 |
| Animal interaction on | 0.0573, 0.0415, 0.0389 | 0.0613, 0.0630, 0.0544 | 42.95, 40.11, 38.52 | 85.60, 69.97, 69.44 |

The CPU section stayed below 0.06 ms mean in every window. This is not isolated registry overhead.
GPU variation dominates the comparison; the result does not establish a reliable GPU cost or improvement.
No performance defaults were changed to manufacture a result. A standalone-player GPU benchmark remains a future profiling task, not a validated claim.
Normal Game View captures show grass with interaction both enabled and disabled.
The separate manual-camera off capture missed frame-submitted grass and is excluded from visual comparison.
Use `planet-grass-on-gameview.png`, `planet-grass-off-gameview.png`, and `grass-closeout-results.json`.
The closeout restored simulation time, celestial time progression, and animal grass interaction before stopping Planet.

## Final handoff

The final full EditMode suite passed 704 tests, with zero failures and zero skips.
Job: `cef166b54a534419a0301370ad9f2656`; duration 42.27 seconds.
The graph update completed with 13,289 nodes and 19,216 edges. No runtime source changed during closeout.
Unity is stopped with `SharedActorAnimationReview.unity` open. Temporary runtime objects are gone.
The current implementation and technical checks are closed. No commit was created.
Active ragdoll physics, a complete humanoid art integration, and standalone-player GPU profiling are future scope.
