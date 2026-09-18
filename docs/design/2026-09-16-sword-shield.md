# Sword and shield review

Status: Bryan approved MELEE-01 revision 1 on 2026-09-16. Preserve attack-final, block-final, and interrupt-final.

## Contract and ownership

The action must preserve authored timing, grip, weight shift, and weapon arcs.
Interrupted windups must not damage the target. Guard must block front hits but not rear hits.
All pose changes use existing performance blending, including interruption during a blend.

`ActorMelee` owns attack/guard timing. Its strike event lets the caller resolve authoritative contact.
`ActorAttackDefinition` supplies existing range and timing data. `ActorHealth` remains the health authority.
`HumanoidAnimationView` and `ActorPerformancePlayback` own playback and transitions.
`HeldToolGrip` fits each prop to its authored palm. No new arm IK or pose writer was added.
`MeleeInteractionReview` supplies the fixed stance, target, test hits, and input adapter.

Scene: `Assets/Scenes/Tests/MeleeReview.unity`.
Controls: E attack; hold B guard; H front hit; J rear hit; Escape cancel.
The reset button restores health, target health, actor, and combat clocks.

## Assets

Source: `D:/Unity/Explore Assets/Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack`.
Six selected FBX files and metadata were copied into `Assets/Art/Interactions/Animations`.
The original `RPG-Character.FBX` avatar dependency remains intact. No vendor scripts or controllers were imported.

| Editable clip | Source clip | Duration |
|---|---|---|
| Melee Ready.anim | Armed-Shield-Idle | 2 seconds |
| Melee Attack.anim | Sword-Attack-R1 | 0.8 seconds |
| Melee Block.anim | Armed-Shield-Block | 2 seconds |
| Melee BlockHit.anim | Armed-Shield-Block-GetHit1 | 0.5 seconds |
| Melee Hit.anim | Armed-GetHit-F1 | 0.5 seconds |

`Assets/Art/Interactions/Definitions/Melee.asset` selects these editable clips.
No per-animation authoring C# runs or regenerates them.
Existing Sidekick sword and shield meshes use 0.65 scale and prop-local palm anchors.
The initial uncalibrated image and first attack capture remain in the bundle as baseline evidence.

## Evidence

Bundle: `local-only/animation-review/melee-2026-09-16/index.html`.
Each runtime sequence has 240 frames at 30 Hz, with two 1/60-second simulation steps per frame.
Two 640×480 views show front three-quarter and side/rear angles.
Recipes, PNG frames, metadata, encoding logs, and source diagnostics remain beside the page.

| Capture | Inputs | Result |
|---|---|---|
| attack-final | Attack at frames 45/120 | Target health 100 → 60 |
| block-final | Guard at 40; hits at 75/100; release at 135; hit at 170 | Player health 100 → 90 |
| interrupt-final | Attack at 45/110/165; hit at 49; cancel at 114 | Only final attack hits; target health 80 |

The original-rig diagnostic runs all five source clips at 1× beside raw retargeted clips.
`attack-no-corrections` supplies matched runtime B with final foot corrections disabled.
Gaze and hand IK are inactive in B and C. Humanoid graph playback settings remain shared.
Matched B for block and interruption remains unreviewed.

- Unity EditMode job `be2403b0e6024a25a67f7f2f3146fe21`: 12 passed, 0 failed.
- Planet build: 0 errors, 21 warnings. Full output: bundle `build.log`.
- Live out-of-range attack: `Miss`, target health 100.
- Live inactive target: `No target`, target health 100.
- Sampled palm anchor error: approximately 0.00000012 metres.
- Metadata `wristToGripDistance` measures anatomical wrist offset, not contact error.
- All five videos passed frame-count verification. Page JavaScript passed syntax checking.

Sampled action frames and consecutive strike/interruption frames were inspected.
Normal-speed playback was not inspected. These checks do not establish visual approval.

## Remaining coverage

This is a fixed-stance training fixture, not complete player combat integration.
Incoming hits use test input, without an animated opponent.
Damage uses one authored strike time, range/bearing, and obstruction checks, not swept blade collision.
Weapons start equipped. Equip/stow, combat locomotion, stamina costs, combos, deaths, opponent AI,
skill variants, other body sizes, and main-game integration remain open.
Bow and dodge remain missing. The broader animation continuity audit remains open.

## Deferred impact reactions — requested 2026-09-16

Bryan approved this set while reserving richer physical/IK combat reactions for later.
Reactions must account for where a hit lands, its direction, and its force.
Build on authored reactions with bounded procedural responses through the existing pose ownership and blend paths.
Preserve balance, support contacts, weapon grips, and smooth recovery, including repeated impacts during another reaction.
The current generic hit and block reactions do not fulfill this requirement. No impact-reaction implementation is included in this approval.

## Physical gear and equip/stow — requested 2026-09-16

The [Physical Character, Equipment, and Animation System](2026-09-16-physical-character-equipment.md) expands this requirement and governs the next milestone.

Bryan requires visible physical gear rather than weapons appearing or disappearing during equip changes.
A gear item must remain represented on the ground, on a rack, carried on the character, or held in a hand.
The character's appearance must communicate the gear they carry and have equipped.
Suggested placements include a sword at the hip or back, a shield on the back, and a bow across the chest or shoulder.
These placements are examples, not final attachment choices. Select compatible slots and authored reach motions together.

Prioritize a reusable physical equip/stow path before the next bow review.
Retain the same visible item through pickup, draw, stow, placement, and drop.
Transfer attachment ownership at visible contact, with continuous position and orientation.
Cancellation or interruption must leave the item in a valid physical location without duplication, disappearance, or snapping.
Use authored body motion and restrained contact IK through existing animation and prop ownership systems.
Check combined gear clearance; back-mounted weapons and shields must not occupy the same space.
Start with the existing sword/shield set, then apply the shared path to bow and tools.
This is a recorded requirement; physical equip/stow is not implemented by the current training fixture.
