# Crafting and campfire review — 2026-09-12

The review room adds smithing, alchemy grinding, cooking, and a campfire construction sequence.
The existing interaction definitions own animation phases, markers, blending, and cancellation.
Station actions share the same runner. Tools follow the calibrated animated hand and blend back to their rest anchors.
The work props are positioned around the artist-authored motion. This pass does not add procedural arm poses to the crafting clips.

## Controls

Visit the smithing bench, alchemy bench, or cooking fire from the review controls.
E starts the default action. Station buttons select alternate actions.
E finishes a looping craft action through its exit phase. Escape cancels through the existing blend.
At the campfire, E first builds the wood pile, then lights it with the owned flint animation.
Cooking remains unavailable until ignition. Construction and ignition commit at animation markers.
Cancellation before a marker does not commit that step. Cancellation after ignition leaves the fire burning.
R explicitly resets the diagnostic room, including construction and ignition.

## Authoring and scope

Edit the assets in `Assets/Art/Interactions/Definitions` for clip ranges, timing, blend duration, and markers.
Edit each target's Actions list to select its actions and tools.
The campfire component owns construction and ignition state, wood presentation, particles, and light.
The campfire is a fixed review fixture. Free placement, resource costs, persistence, recipes, and item rewards are not included.
Hammering, mortar grinding, pot stirring, and meat roasting use owned Survival Animations clips.
Campfire construction reuses a ground reach. Lighting uses the dedicated flint clip.
Potion pouring remains pending; the scratch search found no named pouring clip.
The fire and work props are review geometry, not final production art.

## Validation

Unity EditMode job `8621229608184be5b68c46336efaf74c`: 64 passed, zero failures or skips.
Tests cover station action choice, crafting loop completion, campfire order, early cancellation, and visual state blending.
The first build reported CS0136 from a local variable name collision. Renaming the loop variable fixed it.
The wider test selection exposed an older instant-contact expectation in ActorGazeInteractionTests.
That test now verifies blended entry and release, then checks settled hand contact and unchanged limb lengths.
Planet build: zero errors, 19 warnings. Core build: zero errors and warnings.
Logs are under `local-only/interaction-families/crafting-*.log`.
The broader animation continuity audit remains open. These checks do not certify every interruption path.

After Unity crashed during capture, the restarted editor loaded the saved review scene cleanly with all 18 targets and the campfire component.
A fresh Play Mode check confirmed: cooking rejected before construction; E constructed the fire; E lit it; stirring and roasting entered their loops and exited on E.
The fire remained lit after cooking. The pre-crash capture contains 1,763 frames (58.766 seconds).
It includes all six actions but ends before the final roasting recovery completes. No complete-capture claim is made.
Review video: `local-only/interaction-families/crafting-and-campfire.mp4`.

## Pouring follow-up

Alchemy now includes a separate Pour reagent action.
It uses the owned `ExplosiveLLC/Crafting Mecanim Animation Pack/Animations/Crafter@Item-Water.FBX` clip.
The source body motion remains intact. Hand contact IK raises the pour grip above the review bench.
The flask follows the calibrated animated hand; its liquid stream runs only during the pour phase.
The stream stops emitting on cancellation and component disable. Existing droplets expire naturally.
The review reset clears droplets immediately as an explicit diagnostic operation.
The source ghost remains available through the existing comparison controls.

Play Mode verified normal pouring, cancellation, and disable cleanup.
A complete eight-second capture shows a normal action followed by cancellation: `local-only/interaction-families/pouring.mp4`.
The flask and mixing cup remain prototype geometry. Visual approval is pending.
The inventory search found the existing Core `InventoryService`. Recipe transactions should extend that owner rather than introduce a second inventory.
Recipes and resource costs are still pending. This pass adds the previously missing pouring interaction.

Pouring validation: EditMode job `7b4ef305ede446d1a0148a5f9c34e0aa` passed all 65 focused tests with zero failures or skips.
Planet build passed with 19 warnings; Core passed with zero warnings. Both had zero errors.
The first build required a NuGet restore after Unity restart cleared temporary project assets; the restored build passed.
Runtime reset initially exposed Unity's fake-null behavior in particle references. Explicit Unity null checks fixed the failure.
Graphify update completed. The saved review scene is restored in Edit Mode.

## Flask pickup and placement correction

The previous pouring prototype incorrectly attached its tool for the whole action. It had no pickup or placement clips.
The corrected sequence uses tabletop reach/lift and put-back clips around the watering clip.
TakeTool and ReturnTool markers control attachment. The bottle remains upright at its rest anchor until pickup.
The calibrated grip uses a 55 mm body radius. Hand orientation comes from sampled source poses.
Contact sets provide separate hand and tool orientations, so the pour angle does not force an excessive wrist bend.
The source motion still supplies the torso and lower body. Contact IK positions the hand around the bottle.

Escape while holding the flask selects the existing return phase and blends into it.
Repeated Escape does not restart recovery. Leaving range or losing the target cancels without stalling in recovery.
A new action clears stale tool attachment state. Liquid emission runs only during the pour phase.
The explicit diagnostic room reset remains immediate.

Review capture: `local-only/interaction-families/flask-contact.mp4` (14 seconds, normal cycle then cancellation).
Runtime measurements: zero bottle travel before pickup; final rest position error below 0.001 mm.
The paused capture explicitly gates particle emission during simulation to avoid particles being generated after Stop.
Visual review is pending. This does not close the broader continuity audit.

Final verification: EditMode job `4981d52ba037475b9f8278bc9807d27f` passed all 66 tests with zero failures or skips.
The final fresh Play Mode check measured zero travel before pickup, 0.000000481 m rest error after cancellation, zero rest rotation error, and no liquid emission.
Planet build passed with 19 warnings and zero errors. Core build passed with zero warnings and errors.
The earlier crash interrupted a build while Unity removed Temp output; rebuilding after restart passed.

## Crate placement follow-up

A cancelled rack return left `_returning` set. Floor placement checked that stale mode before clearing it and rejected an otherwise clear floor.
The floor path now selects its mode before validation. Release clears the mode as well.
The reported live crate placement failed with `The placement space is blocked.` despite an empty overlap query.
Clearing only that flag allowed the same crate to complete TwoHandPlace and release.
Regression test covers rack-return cancellation followed by floor placement. Job `444e42f4f8a2424d81d1a25379181272`: 37 review tests passed.
Planet build: zero errors, 19 warnings.

Empty-handed locomotion remains under investigation. The scene still references the original FBX walk and run clips.
A live right/left strafe comparison selected HumanF@StrafeWalk01_Right/Left at weights above 0.9999.
Sampled hips, spine, and upper-arm local rotations matched the source ghost exactly.
Lower-leg corrections differed (up to 27 degrees in the sampled right-strafe pose), attributable to foot IK.
This does not establish that the user's visual issue is fixed or that all locomotion paths match.
Diagnostic overlay video: `local-only/interaction-families/strafe-check.mp4`.
No locomotion clips or pose settings were changed in this follow-up.
