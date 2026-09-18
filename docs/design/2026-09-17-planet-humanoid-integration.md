# Planet humanoid integration

## Active Tracker

Status: Implemented. All 366 targeted tests pass. Live Planet walking, jumping, swimming, despawn, and respawn checks pass. Visual acceptance remains pending.

Current next action: Bryan tries the spawned character in Planet. Preserve the accepted animation assets while reviewing Planet movement.

**Tree:** `harvest-vertical-slice`, `d1e0f62`, with existing uncommitted animation and world work.
**Editor:** Unity `6000.7.0a6`, matching `ProjectSettings/ProjectVersion.txt` on 2026-09-17.
Bryan approved the existing spawn command as the first Planet integration path.

## Ownership

`HumanoidActorController` contains the existing shared movement, traversal, and presentation loop.
`HumanoidAnimationPrototype` retains the review environment, keyboard controls, camera controls, and review panel.
Existing review scenes retain their script GUID and serialized fields through inheritance.
The extraction preserves existing action bodies and authored clips.

`PlanetHumanoidActor` supplies injected gravity, grounding, and water providers.
It supplements terrain grounding with nearby collider support for props.
`PlanetCharacterController` retains input, spawn placement, Planet lifecycle, grass, water interaction, harvesting, and wildlife threat reporting.
The shared actor camera replaces the older direct camera transform calculation.

There is one movement loop and one animation graph per spawned actor.
The Planet host no longer creates or moves a capsule placeholder.
The humanoid root represents the feet, so Planet spawning uses zero capsule-center offset.

## Assets and controls

`Assets/Resources/Characters/PlanetHumanoid.prefab` is inactive until its providers are configured.
It references the existing Synty model and Planet-aware materials.
It uses existing clips and performance assets rather than importing another library.

Asset assignment sources:

- `HumanoidAnimationReview`: Synty model, walking, swimming, crawling, vault, and stair assets.
- `HarvestInteractionReview`: updated jump performances, step-up and jump-grab motion, directional running, and ladder performances.
- `RollReview`: ledge travel/corner motion and dodge/roll definitions.

These are references to the existing assets. The source review scenes were not saved or modified.
Scene-bound targets and test stations are excluded from the player prefab.
The Planet host discovers existing ladder, beam, and rope components on spawn.
This integration does not populate the procedural world with new traversal fixtures.

The console command names remain `character.spawn` and `character.despawn`.
Spawn rejection now returns `ConsoleCommandResult.Fail` through the shared console executor.
The commands remain development-only.
Spawning during generation rejects until the terrain is ready.

Controls preserve WASD movement, RMB look, Shift running, Space jump/swim ascent, and Ctrl crouch/dive/drop.
Z toggles crawl. Q requests the existing directional dodge or forward roll.
The local input boundary blocks actor input while gameplay input is disabled or UI blocks input.
The existing F harvesting path remains available.
An F press used to enter or leave attached traversal cannot also harvest a tree.

## Lifecycle and continuity

Ordinary movement and action transitions retain the shared animation blends and procedural contact handling.
Spawn, explicit debug respawn, and world regeneration establish a new pose before ordinary movement resumes.
Those lifecycle resets intentionally initialize the pose; they are not gameplay traversal transitions.
Despawn releases the animation graph and camera, removes the wildlife threat, and restores free-camera input.

## Validation requirements

Accept the integration only when these checks pass:

- The resource prefab resolves its model, locomotion clips, and performance assets.
- Walking follows radial gravity at the north side, equator, and south side.
- The actor remains on a known spherical surface within 2 cm during the controlled test.
- The injected water provider drives swimming.
- Reinitialization releases the previous graph and creates one visible actor.
- Missing Planet services reject through `CommandExecutor`.
- Existing traversal, ladder, beam, animation, and console regression fixtures pass.
- A fresh Planet run spawns the humanoid and returns camera control on despawn.

Evidence belongs under `local-only/planet-player-integration/`.
Tests do not establish visual approval. Bryan reviews the resulting Planet movement separately.

## Validation progress

Core and Planet builds passed. The first Planet build reported 27 warnings and zero errors.
Build logs: `core-build.log` and `planet-build.log` in the evidence directory.

The first integration test setup omitted EditMode lifecycle initialization.
Its failure was `Expected: not null` / `But was: null`.
An attempted `SendMessage` setup triggered `Assertion failed on expression: 'ShouldRunBehaviour()'`.
The corrected fixture invokes the lifecycle methods directly, because EditMode does not dispatch ordinary MonoBehaviour callbacks.
These failed test jobs are retained: `76779c1305834d51a725926012a4deec`, `0d64a14750c849ef81f2800a0fee5a20`.

## Final evidence — 2026-09-17

- Final test job: `67aad54d09fb4eefaf4668d961d97e4f`; 366 passed, zero failed, zero skipped.
- The run includes seven Planet integration cases, console regressions, traversal, ledge catch, ladder, beam, and humanoid animation fixtures.
- Final Planet build: exit 0, 25 warnings, zero errors. Log: `planet-build-final.log`.
- Unity compilation: no `error CS` entries. Live checks: no exception entries.
- Graphify update: exit 0; 19,700 nodes and 30,436 edges. Log: `graphify-update-final.log`.
- Source comparison confirmed the extracted movement/presentation body is unchanged after the new input admission block.

Live scenario used Planet seed `1691104419` and world seed `12345`.
The existing camera first selected underwater terrain. Swimming activated through the real water service.
A second location was an elevated lake. Sea-level height alone does not establish dry ground.
The dry test location used both terrain and water queries, near `(709.53, 4883.81, 925.46)`.

The dry walk advanced 90 explicit actor ticks at 1/60 seconds.
It moved 2.391675 metres, remained grounded for all 90 ticks, and reported no swimming.
The actor's up vector matched radial gravity with dot product 1.0.
The jump test advanced 150 ticks, recorded 50 airborne ticks, reached 0.720610 metres above its initial tangent plane, and landed.
These are controlled simulation checks inside Play Mode, not a claim of full keyboard or full-speed visual review.

Despawn released the view, visible model, and actor camera.
Free-camera input resumed. The following frame contained no leftover Cinemachine brain from the actor.
Respawn succeeded with one model child.
Unity remains in Planet Play Mode with the humanoid spawned on dry ground.
No review scene or Planet scene asset was saved.

Captures in the evidence directory:

- `captures/planet-land.png`
- `captures/planet-underwater.png`
- `captures/planet-underwater-movement.png`
- `captures/planet-lake.png`

The first long-distance camera relocation caused a temporary Editor/MCP stall.
The Editor recovered and exited Play Mode without a process restart.
The final dry check waited for visible terrain before spawning.

## Limits

Planet traversal uses available collision geometry and existing interaction components.
This pass does not add world ladders, ropes, beams, doors, or collision proxies for every rendered scatter object.
The existing Planet harvesting action remains unchanged; the separate authored chopping interaction is not connected by this pass.
Visual approval, a full keyboard walkthrough, and the broader animation audit remain separate follow-up work.
