# Physical gear transfer — first implementation slice

Status: GEAR-01 revision 3 awaits visual approval. The Physical Equipment & Gear Transfer milestone remains incomplete.
Governing design: [Physical Character, Equipment, and Animation System](2026-09-16-physical-character-equipment.md).

## Implemented ownership

`PhysicalEquipmentState` uses the existing `EntityId` type. It separates item identity, actor owner, location, slot, and pending destination.
The standalone fixture allocates IDs locally; these are not yet connected to world allocation or persistence.
`PhysicalEquipmentItem` owns the prop transform while attached. It uses `HeldToolGrip` to follow the authored palm.
On release, the same GameObject becomes a world rigidbody. Gravity comes from the existing `IGravityProvider` contract.
Velocity and angular velocity carry into release, with bounded values.
No item mesh is hidden or replaced during a transfer.

`EquipmentInteractionReview` requests shared authored playback and restrained positional hand correction.
It slows/reverses the current action on cancellation rather than forcing an endpoint pose.
The current item pose survives handoff; residual attachment error settles over 0.16 seconds.
Hand contact must be within 0.12 metres to transfer. This gate is not proof of visible finger contact.
State transitions do not infer successful contact solely from clip time.

Scene: `Assets/Scenes/Tests/EquipmentReview.unity`.
Controls: 1 sword; 2 shield; Escape return through the active transfer; G drop a held sword; F recover the nearby dropped sword.
Reset restores the initial storage state and review clocks. Diagnostic reset is intentionally immediate.
The approved `MeleeReview.unity` and its clips remain unchanged.

## Motion and source selection

Sword: Kevin Iglesias `HumanM@UnsheatheHips01_R.fbx`, with its original `HumanM_Model.fbx` avatar dependency.
Shield: RPG Character Mecanim `Armed-UnSheath-L-FromArmed-Back`, with `RPG-Character.FBX` avatar dependency.
The first combined RPG shield clip reached toward the belt and was unsuitable for back storage.
Candidate reference captures remain beside the review bundle.

Editable production clips are `Equipment Sword.anim`, `Equipment Shield.anim`, and `Equipment Idle.anim`.
The held stance uses an editable `Equipment Ready.anim` copy of `Melee Ready.anim`. The approved original remains unchanged.
No runtime authoring script regenerates these clips.
Each transfer currently takes 1.3 seconds; source sword duration is 0.733 seconds and shield duration is 1 second.
Stow reverses the draw clip. Separate authored stow integration remains open.

## Revision 1 evidence (preserved baseline)

Bundle: `local-only/animation-review/equipment-2026-09-16/index.html`.
Three complete sequences contain 360 frames each at 30 Hz. Actor steps use 1/60 seconds.
Two 640×480 cameras show front three-quarter and side/rear views.
Recipes, frame PNGs, metadata, candidate baselines, and encoding logs remain beside the bundle.

- `transfer-final`: draw/stow sword, then draw/stow shield. Both finish Stowed.
- `interrupt-final`: cancellation before contact and after acquisition. Both finish Stowed.
- `drop-final`: sword finishes World and settles on the ground; shield remains Stowed.
- IDs remain `E0:2` and `E0:3` throughout all captured transfers.
- Drop capture uses explicit physics stepping while the editor is paused. It restores the prior physics simulation mode afterward.
- Original artist rig and raw retargeted diagnostics run at 1×. Matched complete runtime B is not yet captured.
- Unity job `8d35e7f8255c4930b82f264d5e1ff85e`: 18 tests passed, 0 failed.
- Planet build: 0 errors, 23 warnings; bundle `build.log` contains the full output.
- Encoded frame counts and page JavaScript syntax were verified.

Sampled and consecutive handoff/interruption frames were inspected. Normal-speed playback was not inspected.
Visual approval remains pending. Functional checks do not establish realistic grip, weight, or extraction.

## Revision 2 — mounts, grip, and steps

The sword mount now sits close to the left hip. The shield mount is lower on the back.
The shield grip sits inside its upper rear face. The native reach curves match these revised anchors.
Position-only residual contact preserves authored wrist rotation during handoff.
Both transfer clips use lifted foot placements and a common support stance.
The fixture retains its interaction position to prevent downslope drift while stationary.
Native Mecanim foot-goal curves match the edited leg curves; changing leg muscles alone did not preserve the steps.

Current captures: `transfer-v20`, `interrupt-v20`, `drop-v20`, and `retarget-v20`.
Each contains 360 frames at 30 Hz with 60 Hz simulation. Transfer and interruption finish with both items Stowed.
Drop finishes with the sword in World custody. Item IDs remain unchanged.
The front camera matches revision 1. The second camera now shows the left rear for clearer shield contact.
Matched B uses production clips and timing with final procedural corrections disabled; native Mecanim foot goals remain enabled.
Original-source A remains available at its intended playback rate. Revision 1 remains available through `revision-1.html`.

Right support ankle horizontal excursion is 5.24 mm. Measured left supported intervals stay within 1.64 mm.
These ankle measurements are contact proxies, not proof of sole or finger contact.
Settled grip-marker error is below 0.001 mm because the held item follows the palm.
Sampled complete sequences and consecutive handoff frames were inspected. Normal-speed playback was not inspected.

Unity job `1654a57b855842abb4a23e3adcdfdb40`: 18 tests passed, 0 failed.
Planet build: 0 errors, 19 warnings; `build-v2.log` retains the output.
An initial test request was rejected because Play Mode was active. Fully qualified filters in Edit Mode ran all 18 tests.
Native clips remain editable. Disposable editing recipes remain local to the evidence bundle.

Authoring lesson: use `Animator.avatarRoot` with `HumanPoseHandler` for this rig.
Unbounded twist edits can wrap humanoid muscle curves and cause a full-turn discontinuity.
Bound joint values and consecutive changes, then verify contact still occurs. Excessive smoothing can make the reach miss.

## Revision 3 — contextual ready poses and sword recovery

Bryan rejected revision 2 at four transfer frames. Revision 2 remains available in `revision-2.html` and its original captures.

| Reported frame | Finding | Revision 3 change |
|---|---|---|
| 114 / 3.800 s | Empty left arm used a shield-ready pose | Separate sword-only, shield-only, and combined ready clips select from actual custody |
| 210 / 7.000 s | Upper arm reached straight behind the shoulder | Edited native arm curves raise the elbow for an over-shoulder reach |
| 240 / 8.000 s | Shield contact sat too high and away from its rear surface | Grip anchor lowered by 0.08 local units and moved toward the rear surface by 0.025 |
| 276 / 9.200 s | Shield held on an extended arm | Bent-arm guard holds the shield closer to the torso |

The original draw timing and body motion remain the basis of the edited clips.
The arm edits remain native muscle curves; no runtime authoring generator was added.
`Equipment Sword Ready.anim` leaves the empty left arm relaxed. `Equipment Shield Ready.anim` leaves the empty right arm relaxed.
The combined ready clip keeps both armed poses. This revision does not add physical impact reactions.

F requests sword pickup within 2.5 metres. The fixture uses `ActorInteractionApproach.ForContact` and existing walking approach control.
The actor walks to the native ground-reach contact, kneels, acquires the handle, lifts, and stands.
`PhysicalEquipmentState.Acquire` restores custody only after contact; the item identity and GameObject remain unchanged.
`PhysicalEquipmentItem.Pickup` disables world physics only at acquisition and preserves the visible handoff pose.
The pickup contact gate is 0.08 metres. Final hand correction is considered only within 0.16 metres.
Cancellation before acquisition withdraws the reach. Cancellation after acquisition finishes recovery with the sword retained.
Target movement withdraws the reach. An approach timeout prevents a blocked approach from running indefinitely.

The native pickup and recovery clips copy the existing Loot Anim Set ground pickup sources.
Pickup samples 0–0.92 over 1.5 seconds; recovery samples 0.4–0.995 over 0.9 seconds.
Recovery foot-goal keys add lifted placements into the ready stance.
The copied recovery source had Loop Time enabled. Disabling it prevents a kneeling replay at the final boundary.
Original FBX assets remain unchanged.

Evidence: `transfer-v25` and `interrupt-v25` each contain 360 frames; `pickup-v25` contains 480 frames.
All use 30 Hz capture and 60 Hz simulation. The same front and left-rear cameras match revision 2.
`retarget-v25` supplies matched B for draw/stow. The pickup source study uses the target actor at original clip speed.
An original-rig pickup A and a matched full pickup B remain unavailable; the page identifies these limits.
Timestamped complete sequences and consecutive contact frames were inspected. Normal-speed playback was not inspected.
Current ankle support proxies: right excursion 5.50 mm; left measured supported intervals at most 1.64 mm.
These measurements do not prove a natural grip or stance. Bryan's visual approval remains pending.

Unity job `b6d97948cdb34e85a37a418d53aad981`: 19 passed, 0 failed.
Five live pickup checks passed: approach cancellation, pre-contact cancellation, post-contact cancellation, moving target, and out-of-range rejection.
Their state and identity results are recorded in `pickup-checks-v3.json` beside the review bundle.
Planet build: 0 errors, 21 warnings (`build-v3.log`). Video frame counts and page JavaScript syntax passed verification.

## Known limits and next work

Storage anchors currently approximate carrying mounts. There is no scabbard or strap geometry.
The draw does not yet constrain a blade to slide out of a sheath; it changes from storage to palm control.
The revised shield grip and storage clearance await Bryan's visual approval. Other rig and prop sizes remain unreviewed.
The contact gate currently checks position, not orientation or an established grip duration.
Cancellation reverses the action; physical hit interruption and interrupted sheath constraints remain open.

Shield pickup, rack/table placement, shared slot reservations, secondary equipment motion, carried locomotion,
save/load, replication, and full combat integration remain unimplemented.
Sword pickup is currently exposed through this fixture. Other item sizes, resting orientations, and terrain require broader coverage.
The state model rejects a second transfer on one item; it does not yet arbitrate competing items or actors.
The broader character-motion architecture and continuity audit remain open.

## Revision 4 — arm path and pickup orientation

Bryan rejected revision 3 at transfer frames 102, 206, and 224, and pickup frame 260.
The free arm was stiff. The shield arm folded and twisted. The picked-up sword turned upright too early.
Revision 3 remains available as revision-3.html and revision-3-assets in the local review folder.

Equipment Shield.anim now uses six controlled keys per left-arm channel instead of dense independent corrections.
The keys retain the established contact pose and blend from idle through contact into the shield guard.
Equipment Sword Ready.anim uses a softer free elbow and shoulder while retaining its idle variation.
The edits remain native editable clips. No runtime authoring generator was added.

The pickup caches the world grip orientation before acquisition. The existing contact solver uses that orientation during contact and initial lift.
The correction fades during recovery into the authored ready pose. The existing finger contact solve closes the grip.
The item still uses the same grip attachment and identity throughout. The fixture owns this pickup sequence; wider gameplay integration remains open.

Current captures: transfer-v26, interrupt-v26, pickup-v26, and matched transfer B retarget-v26.
Complete sequences contain 360 frames, except pickup at 480 frames. Both cameras use 30 Hz capture and 60 Hz simulation.
Sampled sequence frames and consecutive shield-contact frames were inspected. Normal-speed playback was not inspected.
The reported poses improved in these views. Visual approval remains pending, especially close-body shield clearance and pickup wrist motion.
The source-comparison limitations recorded for revision 3 still apply.

Unity test job bb8fb35cc9474c4a8ad2eda0b0a1b7dc: 19 passed, 0 failed, 0 skipped.
All five live pickup cancellation/rejection checks passed; see pickup-checks-v4.json.
Unity compiled with no console errors. Video frame counts and page JavaScript syntax passed.
The right ankle support proxy moves at most 5.24 mm. Measured left support intervals move at most 1.64 mm.
Grip marker error after settling is below 0.001 mm; this does not establish finger contact or natural motion.
All earlier milestone limits remain open.

Authoring lesson: per-frame numerical fitting can produce valid marker positions but invalid elbow and wrist paths.
Inspect the joint path between contact poses. Prefer a few deliberate native poses over dense corrections that alternate twist branches.

## Review decision — revision 4

Bryan accepted revision 4 as good enough to move on. He explicitly noted that cleanup remains.
Preserve transfer-v26, interrupt-v26, pickup-v26, revision-4.html, and revision-4-assets as accepted comparisons.
This approval does not close the remaining equipment milestone or wider animation audit.
