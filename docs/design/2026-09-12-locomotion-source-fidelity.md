# Locomotion source fidelity

Bryan requested a restrained correction pass that preserves artist-authored motion. IK should support feet and hand contact.

The review previously used Relaxed Walk composites: Kevin Iglesias leg curves with arm curves from a different pack.
It also applied the same walk direction clips at running speed. Forward running had a dedicated run clip, but lateral running did not.

## Changes

- The review now selects the original eight directional walks already imported into the project.
- Eight matching run clips provide forward, backward, strafe, and diagonal running.
- An optional seven-clip RunDirectionalClips set extends the existing forward-run slot. Existing callers without it retain their previous behavior.
- The shared graph blends direction and speed weights. It does not replace the artist's limb trajectories.
- Foot IK remains active. The accepted chest and door interaction definitions are unchanged.
- The editor comparison now mirrors every live base-clip time and blend weight. It works during locomotion and interactions, including reversal and cancellation blends.
- The ghost omits procedural corrections. Its root follows the character, so it does not evaluate authored root displacement.

## Evidence and limits

Evidence folder: `local-only/locomotion-fidelity/`. The deterministic capture strafes right and left at walking speed, then at running speed.
Before captures use the prior composite walks and walking fallback during sprint-strafing. After captures use original walks and dedicated runs.
The original source clips remain intact; importer loop/root settings and measured support-phase offsets adapt them to the motor.
Foot-cycle offsets use a 64-sample comparison on the review character. The import provenance is in the SyntyHero SOURCE.md.
Test job `0134c42168d1449894a7c807b7e69bb0` passed 249 tests with zero failures or skips.
The new test checks every lateral/backward/diagonal run selection, normalized transition weights, and return to walking across ten rig fixtures.
The editor build passed. Broader animation continuity audit remains open.
This pass does not rewrite accepted interactions or claim every terrain/locomotion case is visually approved.

## Authored foot travel correction

The foot solver previously used a world-space stance anchor during ordinary humanoid locomotion.
That anchor could pull a foot away from its authored horizontal path as the motor moved.
Humanoid locomotion now keeps that horizontal path and applies terrain height and surface-normal corrections.
Explicit stair support keeps its stance lock. Existing creature anchoring and idle turn steps retain their behavior.

The solver also previously ran a two-bone solve when contact weight was positive but no correction was required.
That solve could redirect a nearly straight knee toward the fallback bend direction.
An identity correction now leaves the authored joints unchanged. Existing correction offsets still fade out smoothly.

Regression criteria cover unchanged joints on flat support, authored horizontal travel, and correction onto raised support.
The existing stair lock and penetration tests remain required.
Baseline capture: `local-only/interaction-families/strafe-check.mp4` (right and left walking, with source overlay).
Unity tests and the matching after-capture are pending for this follow-up; this section does not certify visual approval.

Authored humanoid and creature views also disable acceleration-driven body lean by default.
Previously, starts and reversals could add this lean even when spine correction was disabled.
`ProceduralPoseRig.BodyLeanEnabled` controls this contribution independently and blends its release.
General procedural callers retain the previous default. Explicit interaction forward lean and terrain body height remain available.
Regression cases cover source body rotation during acceleration and reversal, blended release, and retained interaction forward lean.

## Authored motion policy and approach validation

Artist-authored clips remain the source for body motion. IK corrects contact errors within a small working range.
The review target exposes `AuthoredContactOffset` and `MaxContactCorrection` (default 0.12 m).
The editor menu `Tools/Actors/Sidekick/Bake Authored Interaction Contacts` samples the source palm at its contact marker.
It preserves existing calibrated targets. Clear `HasAuthoredContact` before deliberately recalibrating a target.
Recalibrate after changing the source clip, character, or prefab scale.

The review now queues a motor-driven approach when horizontal contact error exceeds the target limit.
Targets with an explicit approach anchor use that anchor. The existing directional walking clips drive the approach.
The action starts after the actor reaches its position and facing tolerance. The action then holds that stance.
Cancellation clears the pending action. A moved target, interrupted movement, or blocked approach cancels without pickup.
This is a short collision-aware approach, not route planning around furniture. Vertical reach suitability still needs per-target review.

The shared humanoid foot correction preserves authored horizontal travel during normal locomotion.
Explicit stair support retains its stance lock. Identity corrections no longer redirect the knee.
Humanoid and creature views disable extra acceleration lean. Explicit interaction lean remains available.
Creature idle gaze now follows its source clip unless an explicit look target exists.
Creature rest and death release pose corrections through the existing blend instead of resetting them each frame.

Validation: Unity EditMode job `d4d57b55864443c28ba67c469a22a98b` passed 333 tests, with zero failures or skips.
The first combined run failed one old test that expected root sliding during the action. That expectation now requires a stationary stance.
Planet build passed with 21 warnings and zero errors. Core build passed with zero warnings or errors.
Logs: `local-only/interaction-families/authored-first-final-build.log` and `authored-first-core-build.log`.
A runtime tankard probe queued movement before pickup, then reached the inspect state with target 2 held.
A separate cancellation probe cleared both the queued action and motor approach target.
An earlier runtime probe started 0.3 m farther back and completed movement before its reach and lift phases.

This pass does not certify every animation visually. Bird action clip coverage, swim fallbacks, traversal interruption timing,
and individual interaction hand trajectories remain open. FImpossible tools exist in the scratch project; this pass imports none.
The matching locomotion after-capture remains pending. The broader continuity audit remains open.

## Crate pickup from every side

`FourSidedPickup` selects the nearest face of the square crate before approach and pickup.
It rotates the invisible grip anchors around the crate, preserving right/left hand order and the prop pose.
The authored approach faces that selected face. Existing pose blends still control visible hand motion.
Selection uses the crate center at grip height; a floor-level selection ray previously rejected three sides.
The scene and definition author both enable this behavior for the crate only.

EditMode job `3006035f1f3d48e9aa42d07901f964a5`: 44 passed, zero failed or skipped.
Four parameterized cases check grip side and hand order on a rotated prop without rotating its mesh.
Runtime checks started 0.95 m from each face, settled 60 frames, pressed interact, then stepped 300 frames at 60 Hz.
All four selected target 7 and reached Carry with target 7 held. Runtime station changes were discarded on exit.
Core and Planet builds passed; Planet reported 19 warnings and zero errors.
This verifies pickup behavior. A new rendered-frame visual comparison was not captured for this change.

Lower side grips: moved crate palm contacts from (+/-0.24, 0.30, -0.28) to (+/-0.30, 0.16, -0.08).
The crate renderer has approximately 0.29 m horizontal half extents. The new contacts sit outside opposite side faces.
The scene and scene author retain these positions. Existing nearest-face selection rotates this pair for each pickup side.
Job `c7237c03824f4764baca46f247689481` passed 41 interaction tests. The scene runtime reached Carry with crate 7 held.
Still capture: `local-only/interaction-families/crate-side-grips.png`; labels partly obscure the hands, so it does not certify finger contact.

## Portable items and bookshelf

Ground and table pickups now finish with authored recovery and an unlocked locomotion carry phase.
The ground recovery uses the final 60 percent of the owned kneeling inspection Exit clip.
Ground placement uses the pickup Enter clip to lower the item, followed by the authored recovery segment.
Portable targets select ground or table definitions from contact or destination height. The default boundary is 0.5 m above the actor.

The review includes three additional ground books and a six-slot bookshelf.
Slots commit only after placement completes. Cancellation leaves the next slot free; a full shelf rejects placement.
Shelved books are excluded from selection until the later book-selection feature. Reset Room clears occupancy and restores books.
IK follows the moving book contact during shelf placement. The source animation still owns the body pose.

The shelf boards use `SM_Prop_Shelf_01.fbx` from the owned Synty PolygonFantasyKingdom pack.
The import uses a new local identity and the existing Kingdom material; it imports no vendor scripts or shaders.
Generic side/back panels use `BookshelfWood.mat` without the atlas. Authored shelf UVs use the atlas correctly.
Shelf supports face the back panel. Book slots rotate books upright with their spines facing outward.
The editor command `Tools/Actors/Sidekick/Add Bookshelf And Portable Items` applies this scene setup.

Validation: job `bd203ca61c98469fb6d7d34cf48e74a3` passed 47 tests, zero failures or skips.
Planet build passed with 19 warnings. Editor build passed with 49 warnings after restoring its missing project assets file.
The initial no-restore Editor build failed with NETSDK1004; the restored build had zero errors.
Runtime ground pickup reached Carry item with movement unlocked and head height 1.53 m.
The carried actor moved 1.17 m during 45 rightward walking steps at 60 Hz.
Two books committed slots 0 and 1. Cancelling slot 1 before retry left it available.
Ground book 3 placed on the table using TablePlace; the table tankard placed on the ground using GroundPlace.
Runtime relocation between test stations was diagnostic. It does not validate navigation between those stations.
The table book's original visit station cannot approach through the table collider; the actor must move to a clear side.
Corrected visual capture: `local-only/interaction-families/bookshelf.png`.
The actor was hidden for that shelf-only capture; all runtime visibility and placement changes were discarded on exiting Play Mode.
Broader animation continuity and detailed finger-contact review remain open.

## Carry gaze, table book approach, and torso turns

Carry gaze releases to the authored forward gaze while the actor moves.
After two idle seconds it returns to the held item through the existing gaze blend.
`HeldItemLookDelay` exposes that delay on the review component. Pickup and placement retain their contact gaze.
The existing spine turn response now uses `TorsoTurnResponse` on the humanoid prototype (default 0.25 seconds).
It retains the rig's yaw limit and blends toward zero during movement-locked interactions.

Table-book approach now uses the existing collision slide when the ideal source-contact stance intersects the table.
It accepts the collision-safe stance only within the authored correction allowance plus collision skin.
This replaces a failed straight-path clamp that left too much lateral error. It does not route around furniture.
The runtime book pickup now succeeds from its original visit station, reaching Carry with target 4 held.

Job `124a490ba92e45b29286bc609951e6f1`: 84 passed, zero failures or skips.
Core build passed. Final Planet build passed with 19 warnings and zero errors.
Runtime carry check: idle gaze on book, no item gaze during 45 backward walking frames, no item gaze after one idle second,
and restored item gaze after another 90 frames. All steps used 1/60 second.
A moderate camera-turn input produced 7.20 degrees maximum torso yaw; two seconds after stopping, it settled below 0.001 degrees.
These runtime measurements validate state and bounds. A new rendered transition video was not captured for this pass.

## Crawl exit controls

Crawling used a lower support origin than standing. The settled origin reached approximately -0.07 m on the review floor.
The standing capsule then intersected that floor, so the stance switch failed even after Z cleared the crawl flag.
All stance capsules now share the same lower support baseline. Ctrl clears crawl mode and requests crouch.
Z clears the diagnostic crouch toggle when toggling crawl. Ceiling clearance checks remain active.

Job `bc82048eeec04a4e9651ce26f2e525a1` passed 257 tests, zero failures or skips.
The new cases settle the crawling capsule on a floor and then switch to standing or crouching.
Existing low-ceiling rejection tests passed. Planet build passed with 21 warnings and zero errors.
Runtime at the reported open-floor location verified Crawl -> Stand and Crawl -> Crouch -> Stand.
The authored crawl transition finished and released its blend in both cases.
A first probe beside the ground book could not enter crawl because the longer capsule intersected that item; the open-floor probe covered the reported defect.

## Direct prone-to-crouch transition

Crouch now selects `ExitToCrouch`, which plays the authored prone exit only through normalized time 0.8.
The existing blend then releases into crouch locomotion. Standing still selects the full Exit phase.
Transition duration uses the selected clip range instead of its full length.
Job `2632a9227b1b4bb8a30cbbad8ef00373` passed 225 tests, zero failed or skipped.
The animation fixtures now verify that prone-to-crouch stays below standing height and finishes its blend.
Runtime Ctrl from settled prone reached crouching directly. Maximum head height was 1.06 m; settled crouch head height was 0.89 m.
Planet build passed with 21 warnings and zero errors. No new rendered transition capture was recorded.

## Reduced review jump

The humanoid review now exposes JumpHeight and requests 0.75 m instead of the shared controller's previous fixed 1.6 m.
The shared controller validates the configurable height; other hosts retain the 1.6 m default unless configured.
The change retains the existing airborne momentum and authored takeoff/landing transitions.

Job `1f938b82232c448b93205e75a4290fe6` passed 229 tests, zero failures or skips.
The new motor cases verify apex and range at both configured heights. Planet build passed with 21 warnings and zero errors.
At the scene's actual 5.662317 m/s run speed, the 120 Hz runtime probe measured 0.734 m apex rise,
4.388 m horizontal travel, and 0.775 seconds until landing. The initial 3.1 m estimate assumed the code default of 4 m/s.
No rendered jump capture was recorded for this tuning pass. Visual approval remains with Bryan.

## Short hop presentation

The interaction scene now selects `Short Hop Performances.asset`, using the editable `Animations/Short Hop.anim`.
It derives from the existing Jump Full source. Arm/shoulder muscle deviations retain 40 percent of the source excursion
relative to the selected idle pose; spine/chest deviations retain 60 percent. Leg curves and phase timing remain authored.
The variant removes source hand-goal curves so hand targets do not compete with edited arm curves. Phase blends are 0.16 seconds.
Original Jump Full and Basic Performances assets remain available. Jump physics stays at 0.75 m.
An owned Polygonmaker Regular jump was previewed and rejected for its large arm raise; that temporary import was removed.

Runtime verified the selected Short Hop Performances through apex and landing. Apex capture: `local-only/interaction-families/short-hop-apex.png`.
The captured hands stay below shoulder level. This is a review candidate, not user visual approval.
No code changed and no automated suite was rerun for this clip-only tuning pass.
