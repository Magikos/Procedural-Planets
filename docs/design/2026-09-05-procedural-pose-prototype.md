# Procedural pose prototype — 2026-09-05

## Active Tracker

Status: Bryan accepted the deer prototype overall on 2026-09-07. Small remaining visual issues are deferred; this is not a claim of perfect foot contact. Eating and look-target tracking were also accepted. Bryan rejected the Quirky deer art style. Thirteen EditMode cases passed, including foot-lift interruptions.

Current next action: Review the [wolf–deer encounter](2026-09-07-wolf-deer-encounter.md). Bryan accepted both models overall and deferred minor sliding. The shared hunt states and timed attack fixture are implemented; encounter visual acceptance remains open.

## Next implementation and queued Unity checks

Bryan released Unity on 2026-09-06. The transition implementation and automated checks below have now run. Visual acceptance remains open.

Implemented: active steps update their landing target when a turn stops or reverses. Entering walking releases the procedural step through the existing correction fade. Lost support clears stale contact state. Landing checks validate support at the actual endpoint before planting. Existing walking and stationary-turn regressions remain in place.

Queued Unity validation after implementation:

- Automated: all 13 pose cases pass, including stopping, reversing, entering walking, and lost support during an active lift.
- Review slow walking at 0.376 m/s and turns at positive and negative 25 degrees per second.
- Stop and reverse turns during a foot lift; switch between walking and stationary turning.
- Check slope changes and lost ground support for snapping, sliding, and penetration.
- Recheck idle, eating, and look-target tracking.

After deer acceptance, bind a second non-deer quadruped to the shared rig and validate it. Use that second model to identify required rig settings before expanding the system further. Humanoid hand interactions follow as a separate vertical slice.

The Planet build passes with 20 existing warnings and zero errors (`local-only/animation-preflight/transition-build.txt`). The regression checks establish state handling and finite poses; they do not establish precise hoof contact or visual smoothness. Live captures under `local-only/animation-preflight/transitions-live/` exercise turning, stopping, reversing, walking, turning again, and eating.

## Comparison

### Wolf validation — 2026-09-07

The comparison scene now includes a Polyperfect wolf, using `Assets/AssetPacks/PolyperfectAnimals/Wolf/WolfVisuals.asset`. The `Comparison` field supplies its visual settings. Prototype body height follows that model's settings; the two deer retain their existing scale. Presenter names now come from the selected prefab rather than always saying deer.

Imported art only: the current wolf rig, main animations, howl animation, and 2K color texture. No vendor scripts, controllers, or plugins were imported. Both prefab slots reference the same wolf model; this does not introduce a distinct female mesh. The howl clip is retained for the next action-timing stage and is not yet wired to playback or sound.

The rig contains 56 skinning bones. Bindings use four spine bones, four neck/head bones, eight tail bones, and four three-joint legs with separate paw contacts. The authored model height is 1 m. Initial walk/run reference speeds are 1 and 4 m/s. These speeds remain subject to visual stride calibration.

The first slow-walk regression failed because fixed height thresholds left the rear paw contacts above the release threshold. Measured walk lift ranges were 0.110/0.135 m at the front and 0.036/0.049 m at the rear. Contact curves now sample each clip at 97 phases and normalize against each paw's own lift range. Contact fades between 20% and 65% of that range. The shared solver and accepted deer curves did not change.

All 15 pose cases pass, including slow walking and stationary turns for both models. The Planet build passes with 18 existing warnings and zero errors. A 600-frame check at each of 0, 0.376, 1, and 4 m/s with 25-degree turns produced finite bone positions. Maximum contact-target residuals were 0.172, 0.121, 0.149, and 0.165 m respectively. These measurements do not establish exact paw contact or visual acceptance.

Evidence: `local-only/animation-preflight/wolf-build.txt`, `wolf-motion-check.json`, and `wolf-comparison.png`. Unity reported no console errors during the live check. The scene is left playing with the deer and wolf for Bryan's review.

The imported art comes from Bryan's `D:/Unity/Explore Assets/Assets` library. No vendor scripts, controllers, or editor plugins were imported.

| Property | Polyperfect deer | Quirky Forest Vol.2 deer |
|---|---|---|
| Shape | Angular, longer-legged deer | Rounded, large eyes and short rigid legs |
| Skinning bones | 41 | 11 |
| Leg structure | Multiple joints per leg | One skinning bone per leg |
| Mesh triangles | 1,030 male; 878 antlerless | 6,288 at LOD0; four source LOD models imported |
| Clips imported | Ten main clips plus separate eating clip | Nineteen clips |
| Relevant clips | Idle, breath, walk, run, transitions, eat | Three idles, fear, eat, walk, run, sit, lay |
| Foot IK suitability | Suitable for joint-chain correction | Requires additional leg rigging for bending knees |

Keep Polyperfect as the procedural prototype. The Quirky model remains a comparison asset. Its source takes and import settings both contain 0–10-frame clips at 24 FPS. The short durations are not an accidental import trim.

Source paths:

- `polyperfect/Low Poly Animated Animals/Meshes/Animals/Deer/`
- `Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.3/Forest Vol.2/`
- `FImpossible Creations/Plugins - Animating/{Spine Animator,Tail Animator,Look Animator,Legs Animator}/`

The vendor documentation and selected source informed the feature boundaries: animation-relative correction, bounded bone motion, contact locking, and explicit update order. The project implementation uses its own classes and algorithms.

## Components and ownership

| Component | Responsibility |
|---|---|
| `ProceduralRigDefinition` | Prefab authoring: body, spine, look chain, spring chains, feet, limits, and contact curves |
| `ProceduralPoseRig` | Snapshot bindings and settings; own pose history and correction order |
| `BoneChainSpring` | Damped secondary motion with anchored roots, preserved segment lengths, and angular limits |
| `LimbPoseSolver` | Analytic two-bone solve for three joints; bounded CCD for other joint counts; optional end rotation |
| `FootPlacementSolver` | Contact weighting, stance anchors, ground alignment, and body-lowering requests |
| `CreatureAnimationView` | Native Playables blending; feed sampled clips, velocity, look target, and grounding into the rig |
| `CreaturePoseGrounding` | Visible planet mesh probes and normals, with analytic fallback |
| `CreatureAnimationPrototype` | Isolated scene controls and deterministic stepping for inspection |

Shared solvers live in `Assets/Scripts/Game/Animation/`. They use Unity transforms, an explicit up direction, and the existing `IGroundingProvider`. They do not read the planet, service locator, species names, or creature AI.

The authority still owns position and heading. Resting wander now requests zero body turn. Moving wander retains its curved walking path. The presenter adds head scanning while idle and bounded spine rotation while turning.

The presenter restores the previous unmodified clip pose before evaluating the next frame. It then captures the new clip pose and applies spine, look, body lowering, spring chains, and feet. This prevents drift on bones without animation curves. Teleports and long frames reset temporal state.

Each runtime rig clones its binding arrays and foot curves. Settings remain authoring data. Inspector changes to the prefab require a new instance; prototype module toggles operate on the live rig.

## Foot contacts

Walk and run contact curves were sampled at 49 normalized positions from each imported clip. Height above each foot's clip minimum supplies the initial stance estimate. The curves are stored on each prefab's four foot definitions.

These are prototype contact estimates, not hand-approved contacts. Clip phase stays synchronized while walk and run blend. Swing feet keep their authored lift. Supporting feet request body lowering before the joint solver runs.

The solver never stretches bones. It releases an anchor when contact falls or the target exceeds its correction range. Missing support clears the contact. Sharp terrain changes, gait transitions, and more severe turns need further calibration.

## Scene controls

Open `Assets/Scenes/Tests/DeerAnimationPrototype.unity` and enter Play mode. Select `Deer Animation Prototype`.

- `Walk`: move and turn through the walk/run blend. The bounded demonstration path restarts after three metres from its start.
- `Turn In Place`: rotate while stationary when `Walk` is off. Supporting feet take separate replant steps.
- `Speed` and `Turn Rate`: adjust movement in metres per second and degrees per second.
- `Eat`: blend the imported grazing clip while stationary.
- `Spine`, `Look`, `Chains`, `Feet`: isolate each correction stage.
- `Look Target`: optional world-space target for the head.

The two Polyperfect instances exercise male and antlerless meshes. The rejected Quirky instance has been removed from the scene. Its imported art remains available. Bone rope and cape-strip examples use the same spring-chain implementation as the deer tail.

The scene uses preview material instances because production `Planet/PropLit` materials require planet shader globals. Production material assets stay unchanged.

## Reuse boundaries

Animals and monsters can supply different numbers of feet and different joint chains. NPCs and players can use the same look and limb solvers. A hand target can supply both position and orientation through `LimbPoseSolver.Solve`.

Bone-driven ropes and cape strips can use `BoneChainSpring`. The current examples demonstrate secondary motion only. They do not implement cloth shear constraints, cloth collision, rope self-collision, two-ended rope constraints, or load-bearing rope physics.

Production hand interactions still need action timing, cancellation, target ownership, and gameplay markers. Eating is a presentation control in the prototype; it does not implement feeding gameplay. Job scheduling, animation LOD, collision constraints, and authored joint hinges remain future work.

## Foot-pop correction

The old solver included toe joints in each leg chain. During curved walking, a toe joint rotated 142.425 degrees in one frame. The original clip peaked at 11.448 degrees per frame.

Each deer leg now solves its three main joints. A separate contact transform identifies the hoof endpoint. The solver preserves the authored bend plane and hoof shape. Contact release and penetration correction blend continuously. Only the added IK rotation receives temporal smoothing and a speed limit.

The real-asset regression runs 900 frames at 60 Hz on curved, uneven ground. The corrected maximum is 14.058 degrees per frame. All seven pose tests pass, including the new regression. The Planet build passes with 20 existing warnings and zero errors.

The maximum contact-target residual is 0.189 m; the mean of each frame's maximum is 0.065 m. These residuals show that contact calibration remains open. The rotation test proves removal of the large joint flips, not complete terrain-contact acceptance. Evidence: `local-only/animation-preflight/ik-pop-before.json`, `ik-pop-after.json`, and `ik-pop-build.txt`.

Eating and look-target tracking retain their accepted behavior. Unity is left playing the walking prototype for review.

## Stationary-turn correction — 2026-09-06

Bryan's next GIF showed the remaining turning problem. A controlled stationary rotation reproduced the failure: all four stance anchors eventually released. Idle has no swing phase, so the old solver never reacquired them. The base animation still used zero translational speed.

The rig now selects the foot with the largest support displacement while nearly stationary. It allows one procedural step at a time. The foot follows a lifted path toward a ground-probed landing point ahead of the continuing rotation. Walking retains its clip contact curves. Eating and look-target behavior are unchanged.

The current prototype uses a 0.25-second step, a lift of 30% of correction range, and a trigger at 40% of correction range. These are provisional motion values. Sharp turns, interrupted steps, and different rigs still need visual calibration.

A 12-second check at 25 degrees per second produced 9, 9, 14, and 15 steps across the four feet. At most one foot stepped at once. Maximum measured hoof clearance was 0.113 m. Three feet remained planted at the final sample. Maximum solver residual reached 0.302 m, so the test does not establish precise terrain contact.

All eight pose tests pass, including stationary replant scheduling and the existing walking-pop regression. The Planet build passes with 20 existing warnings and zero errors. Local evidence lives in `local-only/animation-preflight/turn-before.json`, `turn-after.json`, and `turn-build.txt`. The live prototype is left with `Walk` off and `Turn In Place` on for review.

## Slow walking turn — 2026-09-06

Bryan's next review used `Walk` on, `Speed = 0.376`, and `Turn Rate = 25`. The presenter still blended approximately 64% idle into the walk pose. Contact weights therefore never fell below the swing-release threshold. A 20-second reproduction planted each foot only once and ended with no planted feet.

The presenter now blends out idle near rest, reaching the full walking pose at 10% of the authored walking speed. Existing playback-rate scaling controls slower movement. The same reproduction now plants each foot 8–9 times. All nine pose tests pass, including the slow-turn regression. The Planet build passes with existing warnings and no errors.

Evidence: `local-only/animation-preflight/slow-turn-contacts.json`, `slow-turn-build.txt`, and `slow-turn-after.png`. Unity is left running at Bryan's reported settings. Visual acceptance remains open. Other rigs require their own bone bindings, contact curves, and motion review.

## Earlier prototype evidence and limits

- Core and Planet builds pass. Planet reports existing analyzer-version and legacy-data warnings; the build log is `local-only/animation-preflight/prototype-build.txt`.
- Six `ProceduralPoseTests` pass: limb reach under rotated gravity, preserved lengths and teleport reset, look drift, missing support, swing-clearance diagnostics, and stationary wander turn suppression.
- The initial limb test failed at 0.023 m against a 0.015 m threshold. Increasing the bounded iteration budget from 12 to 24 resolved it.
- A controlled 600-step run at 60 Hz exercised two procedural deer and the comparison deer on uneven ground. The maximum correction residual was 0.072 m; 15 of 1,200 procedural-deer samples exceeded 0.05 m. Moving contact quality remains open.
- The settled standing samples reached approximately 0.001–0.002 m residual with four planted feet per deer. These are solver residuals, not a claim about pixel-perfect hoof contact.
- `deer-comparison.png` shows both models. `female-antlerless-fixed.png` shows the corrected female head. `deer-pose-before.png` and `deer-pose-after.png` compare the same sampled base pose with corrections disabled and enabled.
- All captures are under `local-only/animation-preflight/`. Bryan's visual review remains pending.
- A fresh planet run produced seven procedural deer with `CreaturePoseGrounding` injected. Runtime snapshots are in `planet-rig-check.txt`. No new C# runtime error appeared. The console retained the unrelated `LoadPathWearTexel` grass shader warning. Foliage blocked the planet screenshot; it does not prove the live visual result.
- Graphify refreshed the code graph after implementation. No commit was made.

## Antlerless mesh correction

The initial spatial cutoff missed antler faces outside its depth range. The corrected copy removes all 14 connected antler UV islands. Four cap triangles close the two new base openings. The copy retains vertex data, UVs, bind poses, and skin weights. The original male mesh remains unchanged.

This is a project mesh variant, not a runtime visibility trick. Female and male prefabs share the rig and animation clips.
