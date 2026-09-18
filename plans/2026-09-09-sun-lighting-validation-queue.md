# Sun and lighting validation queue — 2026-09-09

Status: ALL FIVE FIXES IMPLEMENTED; 70 focused Unity tests passed on 2026-09-09.
This is a deferred work list, not an automatic scheduler or Editor reservation.
Current next action: review the visual comparison. Implementation and focused regression work are complete.

Do not interrupt the current Unity owner. Do not implement audit fixes without Bryan's selection.

| Item | Work after handoff | Pass condition | Status |
|---|---|---|---|
| Q0 | Record scene, play state, seed, quality, camera, time, moon phase, and modified settings | Reproducible baseline and restoration record exist | QUEUED |
| Q1 | Run existing `MoonOrbitTests`, `ConsoleRegressionTests`, and `ShaderAuditRegressionTests` in EditMode | Nonzero discovered count; zero failures; record passed, failed, skipped, and exact failures | QUEUED |
| Q2 / S01 | Capture atmosphere, disable and re-enable its controller, then capture without regeneration | Optical-depth global points to the valid texture again; scattering matches baseline | QUEUED; current source expected to fail |
| Q3 / S02 | Freeze time; run `light.direction` with a direction unlike the current sun; inspect after two frames | Requested direction persists; shader and directional light agree | QUEUED; current source expected to fail |
| Q4 / S03 | Compare shader direction, celestial direction, and negative light forward after updates | Vectors agree within `1e-5`; repeat with producer and consumer update order reversed in a focused regression | QUEUED |
| Q5 / S04 | Sweep planet-facing rays across zero normalized horizon clearance above `1.002 * radius` | Nearby samples remain continuous; outward rays stay visible | QUEUED; current scalar formula fails |
| Q6 / S05 | Inspect terrain normal details with positive, zero, and negative `N dot L` | Direct specular becomes zero for unlit normals; opposing view/sun vectors remain finite | QUEUED; current scalar formula fails |
| Q7 | Capture local sunrise, noon, sunset, midnight, and elevated horizon views | Review terrain, foliage, shadows, sky, clouds, and water together; Bryan accepts any visual changes | QUEUED |

1. Discover current Unity test tools and exact fixture names before submission.
2. Run only existing fixtures for baseline review. Add focused regressions when the corresponding fixes are approved.
3. Archive screenshots and numeric results under `local-only/debug-screenshots/baselines/2026-09-09-sun-lighting/`.
4. Before each approved visual fix, capture the unchanged view with the same seed, quality, and celestial state.
5. After approved C# fixes, build `ProceduralPlanets.Core.csproj`, then `ProceduralPlanets.Planet.csproj`, serially.
6. Import changed shaders and scripts. Run focused regressions and capture matched views.
7. Restore the handed-off Editor state and modified settings. Record actual results and evidence paths in this queue.

Existing NUnit tests are present in the current tree. Older skill statements claiming no test framework are stale.
No framework installation is required. A build pass does not prove shader or visual correctness.

## Executed results — 2026-09-09

This section supersedes the queued statuses above. Those rows retain the original acceptance criteria.
Bryan handed over Unity for this run. Unity version: `6000.7.0a5`.

| Item | Result | Evidence |
|---|---|---|
| Q0 | COMPLETE | Original scene: `Assets/Scenes/Tests/HumanoidAnimationReview.unity`, clean, playing, unpaused. Planet probe seed `1691104419`, quality index `0`, generated radius `5293.443`, sea radius `5000`. |
| Q1 | PASS | 63 passed, zero failed, zero skipped; 2.4441049 seconds. Job `84188d028cca41acaf78a3445674cf6b`. |
| Q2 / S01 | FAIL; defect reproduced | Texture changed from `BakedOpticalDepth` to null and stayed null across later frames. Captures show the missing atmosphere. |
| Q3 / S02 | FAIL; defect reproduced | Requested direction error changed from `1.365714e-7` immediately to `1.99999976` later. Time remained frozen. |
| Q4 / S03 | FAIL in live run | Shader-to-celestial vector distance was `0.00146173371`; light-to-celestial distance was `2.08616257e-7`. A controlled reversed-order regression remains for implementation. |
| Q5 / S04 | FAIL on GPU | Extracted production horizon code returned `0.497841954` at clearance `-0.001`, then `1.0` at `+0.001`. Radius `100`, camera radius `110`. |
| Q6 / S05 | FAIL on GPU | Extracted production specular code returned red `0.00006231922` with `N dot L = -0.1`, smoothness `0.2`, and dielectric F0 `0.04`. |
| Q7 | PARTIAL | Orbit captures cover sunrise, noon, sunset, and midnight. Ground closeups, elevated sun-disc framing, and Bryan's visual review remain pending. |

The GPU probes compiled extracted production functions into an in-memory diagnostic shader.
They read an `ARGBFloat` target. No shader asset file was created.
Transient shader, material, texture, and render target resources were released after the probes.
These probes verify formulas, not final terrain highlight visibility at a gameplay viewpoint.

### Exact failing observations

```text
S01: expected a bound optical-depth texture after re-enable; actual null.
S02: expected requested direction to persist; actual requestedError=1.99999976, celestialError=0, frozen=true.
S03: expected vector distance <= 0.00001; actual shaderError=0.00146173371, lightError=0.000000208616257.
S04: expected a continuous tangent transition; actual visibility=0.497841954 then 1.000000000.
S05: expected zero direct specular for N dot L=-0.1; actual specular.r=0.00006231922.
```

These are diagnostic expectation failures. The 63 existing NUnit tests all passed.
The Unity console contained no errors during the planet probes.
Existing warnings reported three missing impostor bakes and two bright foliage material tints.
Those warnings were not changed or treated as new sun defects.

### Evidence and restoration

Evidence root: `local-only/debug-screenshots/baselines/2026-09-09-sun-lighting/`.

- `editmode-results.json`: complete test results and individual test states.
- `runtime-results.json`: baseline and numeric runtime/GPU probe results.
- `noon-before.png`, `noon-after-reenable.png`: atmosphere binding comparison, same frozen sun and camera.
- `sunrise-orbit.png`, `sunset-orbit.png`, `midnight-orbit.png`: orbit lighting sweep.

Captures use `Camera.Render` at 960 by 540. They are not F10 diagnostic sidecars.
Weather continued between captures, so the pair is not a claim of exact pixel invariance.
The missing texture and atmosphere change establish S01 independently.

The probe restored the texture binding and original celestial time/freeze state before leaving the planet scene.
The original animation review scene was reopened and Play mode restarted, unpaused.
Its previous simulation position was not preserved across restart. No scene was saved.
No product source changed. The validation skill's obsolete no-test-framework statement was corrected.

## Implemented fixes — 2026-09-09

Bryan approved S01 through S05. This section supersedes the earlier queued and pre-fix results.

| Check | Result | Evidence |
|---|---|---|
| Core build | PASS, exit 0; zero warnings/errors | `build-core.log` |
| Planet build | PASS, exit 0; 19 warnings, zero errors | `build-planet.log`; warnings concern unrelated files |
| Unity regressions | PASS: 70 passed, zero failed/skipped; 3.4720881 seconds | `fix-tests-passed.json`, job `9f144fee741444dd96c87876c15d98e9` |
| Atmosphere re-enable | PASS | Same texture remains bound; released-texture regression recreates and populates its GPU resource |
| Manual command | PASS | `CommandExecutor` accepts `light.direction 1 2 3`; shader and light errors remain `8.940697e-8` across frames |
| Reset command | PASS | `light.direction-reset` clears the override, preserves frozen time, and publishes with error `0` |
| Moving sun | PASS | Shader error `0`; light error `6.143906e-8` |
| Pole local noon | PASS | Command succeeds and holds an override; shader error remains `2.98023224e-8` across frames |
| Horizon GPU regression | PASS | Samples straddle 0.5 continuously; their difference stays below 0.01; outward visibility is 1 |
| Specular GPU regression | PASS | Negative illumination produces zero specular; opposing vectors remain finite; lit normals retain highlights |
| Graphify | PASS, exit 0 | `graphify-update.log`; 1009 AST files, 14092 nodes, 20415 edges |

`SunLightingRegressionTests` adds seven regression cases.
The run also includes `MoonOrbitTests`, `ConsoleRegressionTests`, and `ShaderAuditRegressionTests`.

### Corrected test setup

The first run completed 70 tests with two failures in the new atmosphere test setup.
EditMode did not invoke the runtime enable callback when the test activated its object.
The corrected setup invokes the actual lifecycle methods explicitly. Live Play-mode toggles separately verified Unity's callback behavior.
Both corrected cases passed in the final run. No production assertion was weakened.

Both initial failures reported:

```text
  Expected: not null
  But was:  null
```

Full initial output: `fix-tests-initial.json`. Full final output: `fix-tests-passed.json`.

### Visual evidence

All files remain under `local-only/debug-screenshots/baselines/2026-09-09-sun-lighting/`.

- `fix-noon-before.png`: pre-fix planet view.
- `fix-noon-after.png`: post-fix view.
- `fix-noon-after-reenable.png`: post-fix atmosphere re-enable view.
- `fix-runtime-results.json`: post-fix command, texture, and sun-alignment results.
- `source-before/`: source snapshots used to isolate this task's changes from concurrent work.

The matching view uses seed `1691104419`, quality index `0`, moon progress `0.5`, and time `0.524170041`.
Camera position: `(2002.004, -11996.34, -5216.155)`.
Camera rotation: `(-0.063459, 0.546711, 0.833878, -0.041565)`.
Captures use `Camera.Render` at 960 by 540. Weather time is not identical across the fresh runs.
The comparison therefore checks appearance, not exact pixel invariance or a performance claim.
Ground closeups and Bryan's final visual acceptance remain available follow-up work.

The first after-capture tool response reported `success=false` without an error message.
The PNG existed and was opened successfully, so no second capture was substituted for it.

Hot Reload required a full compile for the new command attribute. That compile completed before the passing test run.
A later unrelated concurrent edit reported:

```text
[HotReload] get semantic edits: errors: Insert MethodParameter is not supported for constructors: Adding parameter requires recompiling in unity. in ThreatRegistry.cs:12
```

This task did not edit `ThreatRegistry.cs`. No lighting runtime errors appeared during validation.
The original animation review scene was restored in unpaused Play mode without saving a scene.
