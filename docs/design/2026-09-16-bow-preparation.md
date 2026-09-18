# Bow animation preparation

Status: Bryan approved BOW-01 revision 1 on 2026-09-16 and requested the next animation family.

GEAR-01 revision 4 is accepted for progression, with cleanup deferred.
BowReview.unity now covers bow draw, arrow retrieval, aim, release, cancellation, and stow.

## Verified source assets

Selected FBX files and their original metadata were copied into Assets/Art/Interactions/Animations.
No vendor scripts, controllers, or demo scenes were imported.

- Kevin Iglesias HumanM@BowIdle01: 1.333 seconds.
- HumanM@BowShot01 - Load: 0.833 seconds.
- HumanM@BowShot01 - Hold: 1.333 seconds.
- HumanM@BowShot01 - Release: 0.800 seconds.
- HumanArcher_Bow, HumanArcher_Arrow, HumanArcher_Quiver: prop models.
- RPG-Character@2Hand-Bow-Unsheath-Back-Unarmed: 1 second.
- RPG-Character@2Hand-Bow-Sheath-Back-Unarmed: 1 second.

Unity confirms the six motion clips are humanoid. Their original avatar dependencies already exist in the project.
Source paths are under D:/Unity/Explore Assets/Assets/Kevin Iglesias/Human Animations and ExplosiveLLC/RPG Character Mecanim Animation Pack.
GUID collisions were checked before copying. Source and target rigs were rendered at the original playback rate.
The demo bow uses a skinned mesh. Runtime presentation uses its limb bones and three string points.
The vendor demo swaps visible bow/arrow copies. Do not copy that behavior: preserve one physical item through each transfer.

## Implementation and evidence

1. Compare original clips on source and production rigs before selecting the final sequence.
2. Reuse physical equipment ownership, HeldToolGrip, shared phase playback, and contact blending.
3. Establish bow grip, arrow nock, string endpoints, quiver contacts, and non-conflicting storage anchors.
4. Keep the arrow visible through quiver, hand, string, flight, and impact. Do not spawn a replacement at release.
5. Check draw/hold/cancel, firing, and stow in at most three complete review scenarios.
6. Review head direction, target alignment, arm clearance, string tension, and interruption during transitions.

ActorBow owns action gating. PhysicalEquipmentItem owns item custody. BowInteractionReview connects authored playback, contact events, and prop presentation.
The existing pose writer applies final contact corrections. No second humanoid pose writer was added.
The same arrow E0:3 remains visible through quiver, hand, flight, and impact. Fire releases its Rigidbody from the displayed pose.
EquipmentProjectileImpact stops that same object at first collision. This is a training projectile, without damage or recovery.

Native editable clips live under Assets/Art/Interactions/Animations/Bow *.anim.
The base clips use Kevin Iglesias motion. UnsheatheBack02_L supplies bow retrieval; UnsheatheBack01_R supplies arrow retrieval.
Bounded wrist and forearm changes were saved into the retrieval clips offline. Runtime does not regenerate these clips.
Bow retrieval takes 1 second; arrow retrieval takes 1.05 seconds. Source durations are 0.633 and 0.733 seconds.
Stow and return reverse the corresponding native clips. Contact gating checks custody before completing a phase.
An editable grip curve moves the arrow contact along its shaft during nocking.

Review: local-only/animation-review/bow-2026-09-16/index.html.
Three complete 480-frame captures show the shot, cancelled draw, and interrupted transfers from two angles.
Source A contains six original clips at 1× on the source and target rigs, without props.
Matched shot B disables final terrain, contact, spine, lean, and secondary corrections. Imported Mecanim foot solving remains active in B and C.
Matched cancellation and interruption B captures are not included.
Sampled and consecutive frames were inspected. Normal-speed playback was not inspected; visual approval remains pending.

Validation: 27 EditMode tests passed, including eight ActorBow cases and existing equipment, melee, and playback cases.
Job: 3e634c00a3e04f4491df804377e0533d. Eight live checks passed, including blocked quiver return and duplicate release rejection.
The console contained no compilation errors after the run. Each encoded video passed frame-count verification.
No player build was run for this pass.
Shot capture measurements: held bow anchor error peaks at 0.44 mm. Held arrow anchor error peaks at 62.2 mm during retrieval, frame 113.
The arrow transfer offset remains a review defect. Low anchor error alone does not verify finger contact or natural motion.
Graphify AST update completed: 19,598 nodes and 30,270 edges.

## Remaining scope

- Free aiming, moving targets, movement during shooting, damage, and main-game combat integration.
- Ammunition selection, multiple arrows, and recovery from the target.
- Straps, equipment racks, storage reservations, and broader body/gear sizes.
- Skill variants and impact reactions.
- Close-body clearance, nocking, wrist paths, and foot transitions require visual approval.
- Prior equipment cleanup remains deferred. The broader continuity audit remains open.

Controls: 1 draws/stows; E retrieves/draws; Space fires; Escape cancels. Reset bow review restores the single-arrow training setup.
