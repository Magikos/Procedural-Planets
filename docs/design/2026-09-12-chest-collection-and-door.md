# Chest collection and door review

> Renamed since this was written (2026-09-18). The names below are the originals
> and stay as the record of what was done on the date in the filename. Current
> equivalents: `Assets/Art/Characters/SyntyHero/` is `Assets/Art/Characters/Human/`;
> `Sidekick*` types, scenes and menu paths are `Human*`; a baked `*_Sidekick.prefab`
> is `*_Fit.prefab`; our copies of source art drop the vendor prefix, so
> `SM_Chr_Rider_01.fbx` is `Rider_01.fbx` and `SM_Prop_Chest_01.fbx` is `Chest_01.fbx`.
> A bare `SM_*` or `SK_HUMN_*` still names a sub-mesh inside a source FBX and is
> unchanged. "Synty Sidekick Characters" is a pack name and is also unchanged.

Bryan requested hands outside the chest, a collection animation between opening and closing, and work on the next interactor.

## Chest

The previous wall-height check missed penetration of the lid. It was insufficient evidence for the reported defect.
The new check tests baked hand vertices against the lid triangles throughout the sequence.

The glove contacts now sit outside the lid, allowing for the finger curl during blending.
Two named clearance phases route the hands around the lid during transfers to and from inspection.
Recovery retains hand correction until the clip finishes. The shared target blend releases it afterward.

The sequence is now open, withdraw hands, inspect, collect, raise hands clear, close, and recover.
E leaves inspection and starts collection. `Collect from chest` is an editable phase in the Chest definition.
It uses `Loot_TreasureChest_GrabItem.fbx`, imported from the owned Loot Anim Set with its GUID preserved.
The left hand retains rim support. The right hand moves from the inside target toward the collected-item hold target.
The existing `InteractionMarker` event emits `Collect` at 65 percent of the complete collection clip. It provides the future inventory hook; this change adds no inventory UI.
Collection uses three contiguous clip sections: collect, stow, and recover.
Only the stowing section applies backward spine correction. All three sections blend.
The source comparison exposed excessive backward lean when that correction covered the entire clip.

## Door

The previous 1.5-second grip kept reaching after the handle moved beyond arm range.
The new 0.4-second push/pull contact releases while the door continues its swing.
The hand uses the handle's authored orientation and an elbow guide.
Closing requires approaching the open handle. Active door sequences retain their initial selection point while the handle moves.
The actor holds a stance outside the initial door plane. Closing also moves outward beyond the swinging edge.
The motor blends that movement. A small blended spine correction retains head clearance.
Other interactions retain their existing target-change behavior.

## Source comparison and green overlay

In Play mode, select a station. Use one of these editor menus:

- `Tools/Actors/Sidekick/Show Source Clip Comparison`: creates the reference 20 metres to the right.
- `Tools/Actors/Sidekick/Show Green Source Overlay`: places a translucent green reference over the actor.
- `Tools/Actors/Sidekick/Hide Source Clip Comparison`: removes the reference and its animation graph.

The reference uses the same character, phase clock, clip sections, reversal, and shared animation blends.
It omits procedural hand, foot, look, and spine correction. It follows the main actor's root movement.
This isolates pose differences; it does not compare authored root motion or reproduce an entire uncut source clip.
The reference colliders are disabled. Temporary objects and materials are removed when Play mode exits or scripts reload.
Select the menu again after changing stations to replace the separate reference prop.

The green shape shows differences, not failures. Target contact requires intentional differences from the source pose.
The comparison exposed unnecessary collection back lean. Restricting that correction to the source bend reduced it.

## Validation

Evidence folder: `local-only/chest-collect-door-2026-09-12/`.
The source GIF and picture supplied by Bryan remain the defect and intended-pose references.

- Core build: zero warnings/errors. Planet build: 19 warnings, zero errors. Editor build: 47 warnings, zero errors.
- Logs: `core.log`, `planet.log`, `editor.log`.
- EditMode job `e6516fdb596b4ed982a2e790e1f3a764`: 291 passed, zero failures or skips.
- New tests cover collection marker order and selection at an open door handle.
- The initial run found a target-change regression: `Expected: False; But was: True` in `SharedDefinitionRetargetsAndCancelsUncommittedMarkers`.
- Restricting retained selection to hinged-door interactions corrected that regression.
- The final chest probe samples 900 frames at 60 Hz, including inspection continuation at frame 240.
- Hand vertices inside the lid triangles: zero. Head vertices inside the lid triangles: zero.
- Door opening and closing: zero head vertices inside the moving door mesh over 180 sampled frames each.
- The door closing check first found 2,755 penetrating vertex samples. The outward stance correction eliminated them.
- Probe source: `hand-lid-probe.cs.txt`. The head check substitutes head-weighted vertices in the same probe.
- Seven cancellation/retrigger runs used frames 15, 80, 145, 190, 330, 570, and 640. Retrigger followed three frames later.
- Maximum sampled head/hips/hand displacement across those runs was 0.070057 m per 60 Hz frame.
- Replays: `chest-collection.mp4` (15 seconds) and `door-review.mp4` (7 seconds), recorded from normal player frames.
- Comparison captures: `chest-comparison.mp4` and `chest-overlay.mp4`.
- The overlay was checked in Play mode. It rendered green, retained the main model, and disabled reference colliders.

The mesh checks cover sampled vertices on the review Rider mesh. They do not prove clearance for every character or arbitrary approach.
The door replay uses a deliberate camera/setup cut between opening and closing, placing the actor near the open handle.
The chest grip still leaves a visible clearance margin at some lid angles. This pass prioritizes removing penetration.
Door finger contact and approaches from other angles still need visual review.
The broader animation continuity audit remains open. Visual acceptance remains Bryan's decision.

## Follow-up: head height, end contact, and closing delay

Bryan approved the arm placement but requested a higher head, contact at full lid travel, and a shorter closing preparation.

- The chest look anchor is higher. Lid-phase spine bend is 15 degrees instead of 25 degrees.
- `Target.OpenHandOffset` adjusts the grip with actual hinge travel. It also follows reversal without resetting the target.
- The chest uses a root-local open offset of `(0, -0.14, 0.08)` metres. Closed contact remains unchanged.
- Clearance waypoints are higher so transfers do not cut through the lid.
- Collection now takes 2.25 seconds instead of 4.33 seconds. The stowing section takes 0.65 seconds instead of 1.60 seconds.
- The reach before closing takes 0.45 seconds instead of 0.80 seconds. All phase changes retain positive blend durations.

Validation: job `a60ddd8c859b4239b0c6252fd2ab0878` passed 292 tests with no failures or skips.
The added test covers grip correction through opening, closing, and cancellation.
Core and Planet builds passed. Logs are in `local-only/chest-contact-followup/`.
The final 900-frame lid-mesh probes found zero penetrating hand or head vertex samples.
The review replay is `local-only/chest-contact-followup/chest-review.mp4`.
The earlier replay and Bryan's three screenshots are the visual baseline.
The broader continuity audit and visual sign-off remain open.

## Collection separated from closing

E during inspection now proceeds to the lid grip and close. It never emits a Collect marker.
`CollectChest.asset` retains the optional collection animation. `SidekickInteractionReview.Collect()` requests it only while inspecting an open chest.
Collection returns to inspection. The next E closes the lid. No inventory UI or automatic collection was added.
The clearance waypoint moved forward from root-local Z -0.60 to -0.35 metres, keeping the arm away from the shoulder singularity.
The maximum sampled elbow movement during the close transfer fell from 0.331357 to 0.064029 metres per 60 Hz frame.
The direct-close lid probe found zero penetrating hand vertex samples across 900 frames.
Job `516c6ce370724f03b75be839faa32fa0`: 293 passed, zero failures/skips. Planet build passed.
Evidence: `local-only/chest-direct-close/`. Visual acceptance and the broader continuity audit remain open.

## Closing arm-pose follow-up

Bryan approved the sequence except the folded arm pose during transfer to the lid.
A separate CloseClear contact set lowers the closing waypoint by 0.06 metres, moves it forward 0.10 metres, and widens each hand by 0.048 metres.
The opening waypoint and all phase durations remain unchanged.
The 900-frame hand probe found no lid penetration. Rendered comparison is in `local-only/chest-arm-transfer/`.
Visual sign-off remains open.

## Chest accepted; door grip pass

Bryan accepted the chest version for now and requested work on the other interactions.
The door now uses the existing finger-grip solver with a 0.035-metre grip radius during reach and push/pull.
The authored palm orientation turns sideways around the handle. Release retains the existing shared blend.
The approved chest scene values and definitions were not retuned in this pass.
Job `7e86b6a3d9a9435c99d08b95b0bd9e37`: 44 focused tests passed, no failures or skips.
Editor build passed with 47 warnings and zero errors. Door opening/closing head-mesh probes returned zero penetrating samples.
The door source mesh was readable during the probes. Its importer setting was already enabled and remains unchanged.
Evidence: `local-only/door-polish/`. The replay includes a deliberate station/camera cut before closing.
The door remains a visual review candidate; the broader continuity audit remains open.

Door cancellation/retrigger samples at frames 25, 65, and 85 reached maximum tracked-bone displacements of 0.139117, 0.142364, and 0.087526 metres per frame.
These samples do not certify the broader continuity audit.
Reimport surfaced an existing importer-format warning: `Serialized file "Assets/Art/Interactions/RoundOne/SM_Bld_Castle_Door_Single_01.fbx.meta" contains a ModelImporter object at version 21, below the supported minimum (25). Open and re-save the file to upgrade.`
The source metadata already used version 21. This pass does not migrate that asset.
