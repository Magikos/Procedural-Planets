# Directional dodge

Status: DODGE-01 revision 1 implemented and captured; Bryan's visual approval is pending.

Bryan approved BOW-01 revision 1 and requested the next missing animation family.
This pass covers unarmed left, right, and backward authored evasive steps.
Rolls, armed variants, invulnerability, stamina, and damage reactions remain outside this pass.

## Acceptance conditions

- Preserve the source dip, push-off, travel, landing, and recovery at its original rate.
- Drive displacement from the same authored clock as the clip.
- Reject blocked or unsupported routes before movement starts.
- Recheck clearance during movement. Blend out if geometry changes.
- Reject repeated activation during a committed step. A stop request completes that short step.
- Return to ordinary movement without a root teleport or stranded action state.
- Preserve source A, matched uncorrected B, and final C captures.
- Review left/right motion, backward motion, and rejection/interruption in three focused scenarios.

## Source and ownership

Selected RPG Character Mecanim Unarmed-Dodge-Left, Right, and Backward.
Each clip lasts 0.667 seconds. Source FBX and original avatar references remain intact.
Native editable clip copies and existing ActorTraversalMotionAsset data drive playback and travel.
ActorDodge uses ActorCollision for gravity-relative swept clearance and support.
HumanoidAnimationPrototype connects the action to the existing motor and animation graph.
No new pose solver or per-animation authoring script runs at runtime.

## Evidence and limits

Review: local-only/animation-review/dodge-2026-09-16/index.html.
Scene: Assets/Scenes/Tests/DodgeReview.unity. Q dodges backward; A/D + Q selects a side.
WASD resumes ordinary movement after recovery. Escape lets the committed step land.
The scene uses a flat 16-metre floor, the existing Sidekick actor, and unarmed source clips.
Travel is 2.56 metres at the source duration of 0.667 seconds, with a 0.10-second entry blend.
The native clips remain editable. Root travel data must stay synchronized with edits that change source translation or timing.

Captures use deterministic 60 Hz simulation, 30 FPS images, and two 640×480 views.
The metadata records requests, phase, root position, foot positions, and foot velocities.
Lateral and retreat sequences contain 180 frames. The obstruction sequence contains 270 frames.
Source A contains 63 frames, including each original endpoint. Its camera follows the root.
It compares the original RPG avatar with the raw production avatar at 1×.
Matched lateral B uses the same root trajectory and clocks. Final terrain, spine, lean, and secondary corrections are disabled.
Mecanim foot-goal solving remains enabled in B and C. Ground foot correction is disabled during the runtime evasive hop.
Retreat and interruption do not have matched B captures.
Sampled and consecutive frames were inspected. Normal-speed playback was not inspected.

Validation: all 31 focused EditMode tests passed (job 58ea3cba6b1746c181c874d6f7114eb1).
The seven dodge cases cover complete travel, retrigger, walls, missing support, changed obstacles, arbitrary up, and invalid input.
Five live checks passed: crouching, airborne, interaction lock, invalid direction, and resumed locomotion.
The Planet code build passed with zero errors and 21 existing warnings; no player build was run.
The initial Hot Reload pass briefly could not resolve the new ActorDodge type. Unity's completed import resolved it.
One capture tool disconnected during reload; its files completed. Final captures were repeated after compilation.

Known review items: the relaxed idle differs from the source combat stance, so foot settling during that transition needs approval.
Dynamic obstruction uses a blend to idle, not an authored brace or impact reaction.
The first implementation requires approximately level support and a standing body envelope.
It does not claim rolling clearance, moving-platform support, or full combat integration.
