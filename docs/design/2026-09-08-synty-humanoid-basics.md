# Synty humanoid basics

> Renamed since this was written (2026-09-18). The names below are the originals
> and stay as the record of what was done on the date in the filename. Current
> equivalents: `Assets/Art/Characters/SyntyHero/` is `Assets/Art/Characters/Human/`;
> `Sidekick*` types, scenes and menu paths are `Human*`; a baked `*_Sidekick.prefab`
> is `*_Fit.prefab`; our copies of source art drop the vendor prefix, so
> `SM_Chr_Rider_01.fbx` is `Rider_01.fbx` and `SM_Prop_Chest_01.fbx` is `Chest_01.fbx`.
> A bare `SM_*` or `SK_HUMN_*` still names a sub-mesh inside a source FBX and is
> unchanged. "Synty Sidekick Characters" is a pack name and is also unchanged.

Bryan authorized a simple Synty character on 2026-09-08 and released Unity from SoundAudition for this work.

Use one trimmed POLYGON Fantasy Hero character and owned Humanoid idle/walk/run clips.
Keep the existing player controller and animal systems unchanged.
Reuse ActorAnimationGraph, ProceduralPoseRig, limb/foot solvers, and SurfaceCharacterController.
Bind Humanoid bones through the Avatar mapping, not outfit-specific object names.
Clothing compatibility requires the same skeleton and bind poses; do not assume every Synty pack uses identical bindings.

The first review scene provides movement, gait blending, foot IK on uneven ground, gaze, and a right-hand reach target.
The reach target demonstrates posing only. It does not grant inventory ownership or complete a gameplay interaction.
Door/chest transactions, carry logic, knockdowns, and modular clothing UI remain later work.

Acceptance: valid Humanoid Avatar, trimmed active renderers, no vendor scripts, working retargeted clips,
no root-motion drift, stable grounded feet, reachable hand target, release to animation, and clean graph disposal.
Inspect the scene in Play mode and run focused regression tests. Record actual art dimensions and clip speeds after import.

## Implemented and verified 2026-09-08

Scene: `Assets/Scenes/Tests/HumanoidAnimationReview.unity`.
Prefab: `Assets/Art/Characters/SyntyHero/SyntyHero.prefab`.
The selected POLYGON Fantasy Hero Male_01 outfit has 13 active skin renderers.
Its valid Humanoid Avatar supplies bone mappings to `HumanoidRigBinding`.

Animation art came from the owned scratch project:
`D:/Unity/Explore Assets/Assets/ECM2/Shared Assets/Models/UnityCharacter/Animations/`.
Only HumanoidIdle.fbx, HumanoidWalk.fbx, and HumanoidRun.fbx were copied with their import settings.
No ECM2 runtime code or controller was installed.
The imported walk speed is 1.558586 m/s. The imported run speed is 5.662317 m/s.
The shared graph synchronizes locomotion phase and keeps root movement under SurfaceCharacterController authority.

Controls:
- WASD moves the actor; Shift runs.
- Hold RMB for the free camera; WASD moves it and Q/E changes height.
- Review toggles control the walk loop, running, feet, gaze, and right-hand reach.
- Reset actor returns to the starting pose and clears loop/run/reach.

Validation:
- Seven focused EditMode tests passed, job `01d80297a9184b70a1a5fd47dda22211`.
- Tests cover Avatar validation, alternating contact curves, skin sole measurement along actor up,
  selected outfit binding, root-motion isolation, limb length preservation, reach release, and graph disposal.
- Core and Planet builds passed with existing analyzer warnings. Unity compiled the final scene/control changes.
- Live Play-mode checks covered idle, walk, run, gaze, and right-hand reach.
- The reachable hand target measured less than 0.001 mm error after settling.
- Skin-based sole calibration reduced observed boot penetration from approximately 5 cm to 1.6 cm on the review slope.
- Minor sole clipping remains. Contact curves are initial biped defaults and still need clip-specific gait tuning.
- No runtime exceptions appeared during the review.
- Captures are in `local-only/humanoid-review/`: walk-final.png, run-final.png, reach-final.png.
- Graphify update completed: 13,386 nodes and 19,331 edges.

This validates one outfit and its Avatar. Other outfits, actor scales, and Synty packs require binding checks.
The player and NPC gameplay integrations remain separate work. The review target only demonstrates hand posing.

## Swimming, diving, and timed reach — 2026-09-08

Bryan asked for continued features while unavailable, then explicitly requested swimming and diving.

Four owned Humanoid clips were imported from:
`D:/Unity/Explore Assets/Assets/Fantacode Studios/Swimming System/Animations/`.
Selected clips: Surface Idle, Slow Swim, Underwater Idle, Underwater Swim.
Only FBX art and import settings were copied. Authoring removes source animation events and material imports.
No vendor runtime component was installed.

`HumanoidAnimationView` blends these clips through the existing `ActorAnimationGraph`.
Water transitions release foot IK and gaze before swimming poses take ownership.
Surface swimming adds bounded head clearance. Underwater pitch follows vertical travel.
No skin baking or component searches run in the animation tick.

`SurfaceSwimProfile.HoldDiveDepth` is opt-in. Existing animal profiles retain automatic buoyancy.
`SurfaceCharacterController` exposes swimming, diving, and root depth as presentation values.
Continuous ascent stops at floating depth. Dive motion clamps the root above the provider's floor.
`ActorButtons.SwimUp` carries held ascent separately from the edge-triggered Jump command.
The Planet capsule controller uses this input and opts into depth holding.
The Synty presentation remains in the review scene; the Planet player model is not replaced.

The review scene includes a shoreline and deep pool. Select Enter water, then use Ctrl to dive and Space to ascend.
Equivalent GUI toggles permit hands-free inspection. Follow actor camera follows depth; RMB permits camera movement.
Water here is a simple review surface. It does not reproduce the Planet underwater renderer.

`ActorReachMotion` is a presentation envelope, not another behavior FSM or interaction authority.
It blends in, waits for observed contact, holds a moving target, and blends out.
Cancellation, target loss, movement, swimming, and an unreachable-target timeout release the hand.
The review panel moves after one contact observation. Production door permissions, inventory changes,
replication, and authoritative interaction commits still belong in gameplay systems.

Evidence:
- Latest focused run: 26/26 passed, job `6ad5130a1b544f04946cd51e378ffb01`.
- Includes reach cancellation/timeout/single contact, actual Synty swim retargeting/root isolation,
  diving in normal and sideways gravity, resurfacing, floor clamping, and existing animal swim regressions.
- Earlier run included HumanoidRigBindingTests: 26/26 passed, job `6ae9d20929c74c2ba87922ced083a1fd`.
- Core build passed with no warnings. Planet build passed with 21 existing warnings.
- Live Synty root depth stayed at 2.999999 m after release of dive input.
- Continuous ascent settled at 1.25 m and cleared Diving while retaining Swimming.
- Live panel open/close reached 60/0 degrees with one contact per use.
- Full-weight hand tracking error during closing stayed below 0.001 mm.
- Captures: `local-only/humanoid-review/dive-hold.png` and `surface-swim-after.png`.
- `surface-swim.png` records the pre-clearance pose; its camera differs slightly from the later capture,
  so it is diagnostic evidence, not a controlled visual comparison.
- Hot Reload briefly reported CS0246 for ActorReachMotion before full import. Unity compilation and tests resolved it.

Limits: Bryan has not reviewed the motion. Breathing, drowning, water-entry jumps, and underwater collision against
arbitrary obstacles are not implemented here. The existing provider floor bounds movement; the review has no wall collision.
Outfit compatibility work remains for a later change; swimming and diving took priority in this pass.
- Final follow-camera check matched the actor-relative offset (2.60, 1.85, 3.60) in fresh Play mode.
- Live movement cancellation produced zero panel contacts and left its angle at zero.
- Final incremental Planet build passed with 19 existing warnings and zero errors.
- Graphify update completed: 13,425 nodes and 19,386 edges.

## Traversal course follow-up

The review now has a slope, six stairs, a large step, a vault barrier, a high ledge,
crouch and crawl tunnels, and three separated jump blocks. The lake remains beside the course.
The scrollable control panel has station buttons. WASD moves, Shift runs, Ctrl crouches,
Z toggles crawling, and Space jumps or requests traversal. Space pulls up from a hang;
Ctrl drops from a hang. Water retains Ctrl dive and held Space ascent.

`ActorCollision` supplies gravity-relative capsule clearance, sweep/slide, step-up, and support queries.
`SurfaceCharacterController` accepts it as an optional dependency. Existing animal callers retain their previous path.
`ActorTraversal` checks obstacle height and full-body clearance before a vault, step-up, or ledge grab.
It checks short swept segments during movement and cancels when its support moves or disappears.
The existing input owner requests actions. This does not introduce another behavior FSM.
The humanoid graph blends posture and traversal clips. Hanging aligns the body and both hands with the detected edge.
Traversal presentation removes imported platform height because the motor owns world displacement.

Owned animation art copied into `Assets/Art/Characters/SyntyHero/Animations`:
- Fantacode Studios Third Person Controller: Crouch Idle, Crouch Walk, Jump.
- ExplosiveLLC RPG Character Mecanim Animation Pack: Crawl-Idle, Crawl-Forward, Ledge-Idle, Ledge-Climb.
- Malbers Animations Common Human Anims Traversal: S_Vault_1M and S_Step_Up_1M.

No vendor runtime scripts were installed. The author strips animation events and configures Humanoid clip imports.

Validation:
- 36/36 EditMode regressions passed, job `585dcd738fa4437db3dcab95f3352168`.
- After the presentation correction, 8/8 focused tests passed, job `4159b60f5c5c4b72a57bcad3e1bb3ffc`.
- Planet build passed with 21 existing warnings and zero errors.
- Live stairs progressed beyond all six risers; large step and vault completed.
- Both tunnel interiors rejected standing while permitting their lower posture.
- A block jump landed on the next block at x=-2.80, y=0.77, with Grounded true.
- The high ledge reached Hanging, then climbed to the platform; a separate drop returned to grounded movement.
- The corrected hanging left hand matched the ledge height of 2.20 m.
- Core build passed with zero warnings and errors. An earlier command used the wrong project filename
  and failed with `MSBUILD : error MSB1009: Project file does not exist.` The corrected command succeeded.

These additions supersede the earlier no-wall-collision limitation in this document.
They remain a review prototype, not a replacement for the Planet player host.
Moving-platform traversal, ledge shimmy, fall damage, and network authority integration remain unimplemented.
The climb and vault transitions still need Bryan's visual review. The water surface is review geometry, not Planet water rendering.

## Third-person controls and review corrections — 2026-09-09

Bryan requested a camera behind the actor, WASD strafing, and camera look to change direction.
Unity 6000.7.0a5 was connected and running this review scene before control was taken.
Cinemachine 6.7.0 was already installed. No package was added or upgraded.

`ActorThirdPersonCamera` uses Cinemachine Third Person Follow for placement, damping, and obstacle avoidance.
The local host supplies a look target and gravity-relative world up. It updates the brain after actor movement.
Pitch is limited to -60 through 75 degrees. Camera collision radius encloses the near-plane corners.
Camera mode changes preserve free-camera inspection. Disposal restores the camera's previous brain state.
`WorkbenchFlyCamera.OnEnable` now reads the current camera rotation, preventing a snap when free flight resumes.

The review passes `ActorIntent.Move` directly to `SurfaceCharacterController`.
Strafing and reversing preserve facing. Mouse yaw changes facing, including while the actor stands still.
Traversal retains its detected facing. The existing motor, collision, traversal, graph, and pose solvers still own their functions.
The Planet player host remains unchanged. Its existing look code and `ActorThirdPersonCamera` overlap until the integration replaces that host code.

Controls now supersede the earlier camera instructions:

- W/S moves forward/backward; A/D strafes; Shift runs.
- Hold RMB to look and steer. Release RMB or press Escape to release the cursor.
- Tab switches third-person control and free-camera inspection. The GUI camera toggle provides the same switch.
- Free-camera mode retains RMB look, WASD flight, and Q/E height changes.
- Ctrl crouches, dives, or drops from a hang. Z toggles crawling. Space jumps, requests traversal, or climbs.
- Held Space ascends while swimming. All station buttons, walk-loop controls, and interaction controls remain available.

Six owned FBX clips came from `D:/Unity/Explore Assets/Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/Unarmed/`:
`RPG-Character@Unarmed-Strafe-{Left,Right,Backward}.FBX` and `RPG-Character@Unarmed-Crouch-Walk-{Left,Right,Backward}.FBX`.
The destination uses those suffixes under `Assets/Art/Characters/SyntyHero/Animations/`.
Only art and import metadata were copied. No vendor runtime scripts were copied.
`HumanoidReviewAuthor.ConfigureDirectionalClips` creates local Humanoid mappings, removes events and materials, and removes source root height.
The shared animation graph blends these directions without moving the authority root.
The first import retained approximately 0.9 m of source height. Corrected imports put the sampled left foot 0.081 m above the root.

Live captures also exposed stale skin matrices after manual graph sampling and procedural IK.
The skeleton and bind matrices were correct, but some rendered arm parts showed gaps.
`HumanoidAnimationView` now enables `forceMatrixRecalculationPerRender` during its lifetime and restores each previous value on disposal.
The matched `ledge-final.png` and `ledge-matrices.png` captures show the isolated correction.

Validation:

- 60/60 focused EditMode tests passed, job `6e46ea7711b74240a97c7cffb7d3f32e`.
- After the skin-matrix correction, 8/8 affected tests passed, job `3d6f35839efc4af0a9cd0b1b7b2c50dc`.
- Camera tests cover look-relative strafing/reversing, pitch limits, normal and sideways gravity, wall avoidance, recovery, mode changes, and disposal.
- Animation tests cover directional root isolation, blend-weight totals, source-height removal, and skin-matrix updates.
- Core, Planet, and Editor builds passed. They reported existing analyzer/compiler-version and project warnings; the final Planet build reported 21 warnings.
- Live checks covered slope walking, all six stairs, large step, vault, hang, pull-up, drop, both tunnels, block jump, swimming, diving, and resurfacing.
- The top stair check reached (-5.00, 1.18, 5.80) with Grounded true. An earlier sample had already walked off the stair end.
- The block jump reached (-3.04, 0.77, 9.00) with Grounded true.
- Both tunnel interiors rejected standing. Diving held at 2.999999 m and resurfacing settled at 1.25 m.
- Captures and numerical records are in `local-only/humanoid-review/2026-09-09/`.
- `camera-before.png` and `camera-after.png` document different camera behavior and viewpoints; they are not a pixel-invariance comparison.
- A missing `UnityEngine.Playables` import initially caused CS1061 in the new test. The import was fixed before the passing runs.
- Hot Reload reported stale-type errors during the camera assembly move. Full Unity compilation and the subsequent tests passed.
- The final fresh Play-mode log contained no new errors or exceptions. Free-camera switching enabled exactly one camera controller in each mode.
- Final corrected captures are `ledge-verified.png`, `strafe-verified.png`, `crouch-verified.png`, and `camera-ready.png`.
- Graphify update completed with 13,535 nodes and 19,596 edges.

Remaining review limits: the sprint strafe uses the directional gait at increased cadence.
Crawling and swimming still use their existing forward-motion clips for lateral travel.
Clip-specific contact timing, small sole intersections, and traversal transition appearance still need visual review.
Course world labels face the old inspection side. Station buttons identify the course from either camera direction.
Cinemachine collision currently sees Unity colliders. Planet terrain uses custom surface queries, so Planet camera collision needs that adapter during integration.
No Planet player integration or final visual acceptance is claimed by these checks.

### Backward jitter correction (2026-09-09)

Backward movement exposed rounding in the directional mixer. Computing forward weight as `1 - left - right - back` sometimes produced a negative value. Unity rejected that value and retained the full forward weight from the initial locomotion blend. Forward and backward poses then mixed intermittently.

Each direction now uses its own nonnegative velocity component divided by the shared total. The regression checks every sampled weight and total, including backward motion with small lateral support displacements.

Runtime reproduction held backward input for 100 steps at 0.02 seconds. Before the fix, grounded hip changes reached approximately 32 degrees per step with foot IK disabled. After the fix, the maximum was 2.400809 degrees with foot IK both enabled and disabled. Forward weight remained zero; the maximum total-weight error was 1.192093e-7. Eight focused animation and camera EditMode tests passed. Core and Planet builds passed with existing warnings. Unity remains in the review scene for manual review.

### Direction crossfade (2026-09-09)

Directional locomotion now filters the four nonnegative mixer weights with an exponential blend at 12 per second. A reversal reaches about 95 percent of its target after 0.25 seconds. Standing and crouched clips share these weights. Zero movement retains the direction blend while speed fades to idle. Reset restores the forward blend. Motor input remains immediate.

The directional regression checks outgoing and incoming weights on reversal, settling, stop continuity, and normalized weights. All four HumanoidAnimationTests passed. Core and Planet builds passed with existing warnings. Runtime standing and crouched reversals were sampled in the review scene; manual visual acceptance remains with the user.

### Idle transition and directional foot contacts (2026-09-09)

Idle-to-move previously reached full locomotion weight on the first input frame because its threshold was only ten percent of walk speed. The moving weight now has its own exponential fade. In the review scene, the first backward frame changed hip position by 0.0133 m with foot IK disabled and 0.0140 m with IK enabled, versus approximately 0.073 m before.

Humanoid foot contacts now use ankle clearance from the evaluated, blended animation before procedural body lowering. Contact fades between 0.03 m and 0.10 m above each foot's sole offset, scaled with the actor. This is a height-based contact estimate, not authored per-clip contact metadata. Idle blends toward full contact. The shared pose rig accepts optional validated contact weights; actors without these weights retain their existing curves. Both body lowering and foot anchoring use the same weights. The backward runtime check observed 79 swing-foot samples with no incorrectly planted swing foot.

All 48 focused HumanoidAnimationTests, ProceduralPoseTests, FootStepContinuityTests, and BodyLeanTests passed after a full script compile. Core and Planet builds passed with existing warnings. An initial CS0136 local-name conflict was corrected before these results. The earlier test run used the previous compiled assembly and is not evidence for this change. Visual acceptance of the backward gait and slope contact tuning remains pending.

### Stairs, fall selection, and shared transitions (2026-09-09)

The motor now retains grounded support across gaps up to 0.10 m when already grounded and not jumping. The large-step trajectory ended approximately 0.065 m above collision support. The former 0.002 m tolerance produced five airborne frames after traversal. The runtime recheck produced none. Explicit jumps expose `Jumping` separately from passive falls.

The shared procedural rig absorbs abrupt upward support changes into a decaying body offset. Collision authority remains at the cleared height. Swing feet query upcoming support up to 0.12 seconds ahead, within their correction reach, to lift toward stair treads before reaching a riser. Humanoid correction reach increased from 25 to 35 percent of leg length. The actor climbed all six review stairs. A 0.106 m collision rise produced approximately 0.017 m visible hip rise in the sampled run. Static side captures are in local-only/stairs-review/. They show separate foot placement on treads; exact sole clearance and motion feel still need user review.

Short passive drops retain the locomotion pose with foot IK released. Drops beyond 0.25 m progressively blend toward the dedicated owned Fall.fbx clip, reaching full fall selection at 1 m. A 0.12 second grace period excludes transient support loss. Explicit jumps retain their jump animation. Landing fades the outgoing pose. Fall.fbx comes from the owned ExplosiveLLC unarmed fall source and uses the shared humanoid loop import setup. The review scene references it; Planet integration remains pending.

ActorAnimationGraph now bounds final base-weight changes to eight per second and finishes at exact target weights. This preserves existing slower fades. Humanoid, creature, and bird views use it before pose evaluation or skipping. Interrupted traversal clips retain their last sampled time while fading. Platform-height correction and hang alignment use displayed weights. Creature death alignment and procedural swimming also use displayed weights so their offsets fade with the clips. Fish currently use one swim clip and have no state-to-state clip transition.

Validation: 106 focused EditMode tests passed in job 8dfbbcb5535f4c9796169fb9e1b17046. Core and Planet builds passed with existing warnings. Earlier runs exposed an exponential fade tail and an old instant swim-to-death expectation. The finite shared fade and updated death-transition regression resolved those failures. Regression coverage includes climb support handoff, interrupted graph fades, short versus long falls, body smoothing, creature swimming, bird presentation, death poses, and existing grounding/IK behavior. This is not a claim that every procedural transition has received visual acceptance.

### Basic skill animation baseline (2026-09-09)

User direction: all default animations assume the lowest skill level. Use restrained, practical movement. Flips and other acrobatic variants belong to future explicit skill-based selection; a high Acrobatics skill may enable them. No skill-selection framework is added yet.

The previous ExplosiveLLC Ledge-Climb clip inverted the torso completely during traversal. The review scene and scene author now use Basic Pull Up.fbx, copied from Universal_Traversal_Anims/Art/Animations/Traversal_Climb_End_toCliffTop.fbx. It uses a local Humanoid import, no animation events, no imported materials, and motor-owned root displacement. The old clip remains available as art but is not assigned to the default review set. Low vault and step-up remain their upright basic motions.

Shared pull-up traversal now lasts 1.6 seconds, including climbs from hanging. Collision checks and crossfades remain active. Sampling the replacement kept the head-to-hips axis above horizontal throughout the animation (minimum up dot approximately 0.191, versus -1 for the previous clip). The high-ledge runtime traversal completed grounded. Side captures are in local-only/basic-pull-up/. A regression samples the default step, vault, and pull-up clips and rejects inversion.
Validation for the basic pull-up: all 14 focused animation and traversal tests passed (job e87e30d41eae4ff98e89c6fc89f405c6). Core and Planet builds passed. Unity reported no console errors after restarting the review scene.

### Block approach consistency and traversal contact IK (2026-09-09)

The parkour blocks measured 0.78-0.90 m above their approach ground. The old 0.85 m step threshold selected StepUp on one side and ClimbUp on another. The latter begins with a hanging pose, which appeared to grab an invisible ledge. The step classification now matches the basic one-metre step clip. All twelve cardinal approaches to the three parkour blocks selected StepUp in the runtime recheck. Relative height still governs classification for genuinely taller ledges.

The shared humanoid view now owns traversal hand targets. The review host passes the detected edge for every active traversal and no longer duplicates hanging IK. Hands approach the edge near the end of a jump-grab, hold through hanging and early pull-up, and release between 45 and 80 percent of pull-up progress. The body alignment and two hand targets use the same contact weight. Runtime pull-up hand errors stayed below a millimetre during full contact and faded during release.

Foot IK now runs during step-up and the final supported phase of climb/vault. Foot contact weights use the pose after traversal height correction and include action motion even when movement input is zero. The runtime step-up recorded 23 frames with planted-foot contact, versus disabled traversal foot IK previously. Reset clears traversal-owned hand targets and the cached ledge.

All 68 focused animation, traversal, motor, procedural-pose, and foot-continuity tests passed (job 7ed2525d7e5044348d5463d4a85b30df). Core and Planet builds passed with existing warnings. The new regression covers four approach headings and ground-height variation, plus hand hold/release without moving the authority root. Visual review remains necessary for limb placement at corners and unusual ledge shapes.
