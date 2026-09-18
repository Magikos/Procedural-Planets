---
name: pp-animation-quality-review
description: Review and polish humanoid or creature animation through repeatable rendered sequences, authored-source comparisons, contact measurements, and transition checks. Use for unnatural motion, foot sliding, floating, grip errors, or animation quality reviews. Covers locomotion and interactions, not only jumps. Does not certify visual quality from tests alone.
---

# Animation quality review

Preserve the artist's motion and identify where the final result departs from it.
Use small, task-specific IK corrections for contact and terrain adaptation.
Do not replace authored motion with procedural motion merely because it is easier to tune.
Apply the [animation continuity rule](../../.agent-memory/animation-transitions.md).

## Scope and evidence

Choose scenarios from the user's request. A local fix does not require a whole-library review.
For a broad review, track humanoid and creature families separately. Include unsupported and unreviewed actions in the coverage record.
Read [validation and evidence](../pp-validation-and-evidence/SKILL.md) for evidence status and scenario recording.
Read [creature and animation](../pp-creature-and-animation/SKILL.md) when tracing simulation or presentation ownership.
Use the asset skills for source discovery or import. This skill does not require a new package or pose solver.

Before tuning, record the intended action, observed defect, and a falsifiable acceptance condition.
State what must remain authored and what may adapt: contact position, contact orientation, reach direction, timing, or secondary motion.
Assign each contact to its frame: ground, moving support, prop, or another body part. Include the active contact interval.
Verify contact markers against the visible skin and prop surface. Marker proximity alone does not establish visible contact.
Measure every expected support interval, including failed anchor acquisition. A nearest-rung point that changes each frame is not a persistent support anchor.
Check contact evaluation order. Sample the current authored pose before detecting support and applying final corrections.
Preserve a baseline before changing clips, curves, timing, or IK settings.
Use the existing [scenario record](../pp-validation-and-evidence/templates/scenario-record.md); add the animation fields below to that record.
Omit irrelevant fields. Mark missing relevant values as unknown and explain their effect on the comparison.

- Record scene, actor, rig/avatar, scale, clip asset and variant, import settings, phase ranges, playback speed, and root-motion convention.
- Record effective motor speeds, gravity, support geometry, input sequence, prop transforms, contact markers, and IK weights.
- Record frame rate, simulation step, time scale, camera transforms, resolution, and the relevant source revision or patch.
- State reset behavior, retained item state, and whether the capture uses live input or deterministic stepping.

Do not infer effective scene settings from code defaults.
Do not assume a diagnostic teleport preserves facing or camera state.
Check all playback clocks after a review reset, including idle and fall clips. Record retained clocks as a comparison limit if they are not reset.

## Compare three stages

| Stage | Purpose | Controls |
|---|---|---|
| A: original artist clip | Establish the intended motion and rhythm | Original asset, intended rate, original rig when available; identify any retargeting |
| B: target rig without procedural corrections | Isolate retargeting, edited curves, clip selection, phase mapping, and blending | Same target rig, controller timing, and root trajectory as C; disable all relevant correction writers |
| C: final runtime result | Inspect the complete player-visible action | Production rig, motor, blends, IK, props, and gameplay timing |

If the original rig is unavailable, label A as retargeted. Do not claim that it isolates avatar errors.
If B cannot be isolated reliably, mark it unavailable and state which cause remains uncertain.
Match the motor trajectory at the input to the correction stage. If a correction changes the root, retain and label that output difference.
A cloned final graph with IK disabled is B, not an independent original-clip reference.
Do not call a modified variant the artist's original clip.

Align comparisons by named events, such as contact or touchdown. Retain each stage's elapsed time and natural playback rate.
Use the same simulation clock for B and C. Inspect unwarped A separately when production phase remapping could cause the defect.
Do not time-warp every reference to the final result; that can conceal timing errors.

Use labeled side-by-side views for silhouette and motion. Use the optional ghost overlay for local pose differences.
Verify left/right labels and frame alignment with a known event before judging differences.
Root-align a secondary comparison when useful, but retain a world-space view for travel and sliding.
Overlay bleed-through is evidence of difference, not proof of a defect.
An intended terrain or grip correction should differ from the source.

## Inspect complete motion

Capture enough lead-in and recovery to include the surrounding action or at least one gait cycle.
Inspect the normal gameplay camera and a clear side or front view. Add another view when occlusion hides contacts.
Keep the whole body, floor contacts, and relevant prop visible. Disable obstructing diagnostic labels in captures.

Inspect timed playback when the available tool supports it. Inspect consecutive frames around each suspect event as well.
For this motion review, use timed sequences as the visual evidence. A single F10 capture cannot replace them.
Documentation-only or read-only review does not require a build. Run code and import checks when the corresponding code or assets change.
If only still-image tools are available, extract timestamped frames and use timing data; state that normal-speed playback was not inspected.
A GIF attachment or generated video file alone does not prove that every frame was inspected.
Do not judge rhythm from a contact sheet alone. Do not judge contact from a distant playback alone.

Use these checks as applicable:

| Area | Inspect |
|---|---|
| Locomotion | Starts, stops, forward/backward travel, strafing, diagonals, turns, speed changes, slopes, stairs, and uneven support |
| Stance and airborne motion | Standing/crouching/prone transitions, stationary and moving takeoff, ascent, apex, descent, touchdown, and recovery |
| Interaction | Approach, anticipation, reach, contact, grip, prop motion, release, recovery, carrying, and placement |
| Creature motion | Species-specific support sequence, body rhythm, turning, terrain adaptation, action transitions, and relevant flight/swim/rest states |
| Disruption | Cancellation during each affected phase, rapid retrigger, target switch/loss/destruction, leaving range, and interruption during an existing blend |

Judge weight transfer, balance, limb arcs, silhouette, head/gaze, and rhythm against the intended action and authored reference.
For creatures, use the species' gait and anatomy. Do not apply humanoid knee or support assumptions to every rig.
For broad reviews, maintain an action-by-rig matrix in the evidence record. Include representative proportions and difficult target placements.
Keep pass, defect, not inspected, and unsupported distinct. One successful character does not validate the row.
Distinguish deliberate stylization from unintended motion. Smooth motion can still look weak, delayed, or artificial.

## Measure suspect intervals

Use measurements to locate defects, not to replace visual judgment.
Define the contact interval and coordinate space before measuring. Record values over time, including entry and release.
Choose tolerances from scale, reference motion, and the intended action. Do not apply one universal threshold to every rig.

| Measurement | Interpretation and common trap |
|---|---|
| Planted contact drift | Track a sole/paw contact point relative to its support; moving platforms require support-local coordinates. Foot roll is not automatically sliding. |
| Grip error | Measure an authored palm/finger contact marker against the prop contact point and orientation. The wrist bone is not the grip point. |
| IK correction | Record positional correction relative to limb length, angular correction, weights, and duration. Persistent saturation can indicate a bad approach or source clip. |
| Joint continuity | Compare successive local rotations and endpoint velocities with the source. A sharp authored impact is not automatically an IK snap. |
| Root and phase timing | Compare motor travel, vertical velocity, clip phase, blend weights, and contact events. Check frozen poses and stretched phases. |
| Penetration and reach | Inspect hand, head, limb, and prop clearance throughout the interval. Endpoint accuracy alone does not prove a valid path. |

Sample between authored keys as well as at the keys. Interpolation can create contact overshoot even when both keys look correct.
For secondary motion, verify that it does not move an established grip or overwrite the artist's primary action.

Inspect finger curves and palm orientation when a hand reaches the marker but does not visibly grip the prop.
Some humanoid imports contain neutral finger curves. A solved palm position does not create a grasp.
Prefer an editable authored grip pose before adding a new procedural finger solver.
Preserve that pose through contact acquisition and release; check whether an existing solver restores bind-pose fingers.
For ladder changes, retain support across stop and reversal. Prepare contact correction before the authored landing interval.
Keep preparation weights separate from expected support. Do not redefine a missed planted interval as a successful reach.
Review all active limb contacts, even when the reported defect concerns one hand. Separate hand and foot maxima in summaries.
A slow swing apex is not a foot plant. Confirm the authored support phase or sustained surface contact before applying foot IK.
During preparation, do not freeze an along-rung grip position from an unfinished lateral hand arc. Freeze the support when acquisition begins.
Measure lateral and vertical contact error separately. Faster playback can expose an early target-selection error even when correction weights reach one.
When an exit rejects, inspect the swept body route and actual collider. Do not weaken collision to hide premature transfer selection.
Use a compatible leading-hand transfer at either ladder end. Do not freeze root travel while the source continues stepping.
Verify that fast climbing skips physical rungs. Faster playback alone does not demonstrate a larger step.
Measure slide contact against the rails in the transverse axes; vertical hand movement along a rail is intentional.
Check difficult targets from different approach directions. History-dependent state must blend without accumulating limb twists.

When an object is outside comfortable reach, prefer a supported approach or stance adjustment before the reach.
Do not increase IK strength to compensate for a wrong source clip, wrong contact frame, or unreachable target.
Do not use foot locking to hide a motor/clip speed mismatch throughout locomotion.

## Diagnose, change, repeat

### Purposeful motion and reusable behavior

Read the [context requirements](../../.agent-memory/animation-transitions.md#purposeful-motion-and-context), including when delegating review or implementation.
Give each delegated task the exact action, input sequence, reported interval, intended outcome, and evidence location.
Assign one owner for Unity access and shared pose changes.

Judge support, momentum, anticipation, and recovery before accepting smooth interpolation or accurate hand endpoints.
For a reach, inspect body facing, approach distance, torso, shoulder, elbow, and hand throughout the path.
Prefer a natural step or turn over stretching an unsuitable pose. Small contact error can coexist with unnatural body motion.
For locomotion handoffs, match support phase and actual travel. A blend can slide planted feet even when both clips look correct alone.
For jumps, review stopping and continuing landings separately. Also change movement intent during flight and during recovery.
Verify that the forward landing leg continues into the matching stride without an unnecessary airborne leg swap.

Prefer a compact authored action library with explicit phase/contact information and bounded contextual adaptation.
Use continuous values for speed, direction, and reachable offsets. Use discrete phases when contact or gameplay meaning changes.
Do not create a separate state for every combination of these values.
Add an authored variant when support sequence, momentum, or silhouette changes substantially.
Consider editable target-relative keyframes for a demonstrated coverage gap, using existing playback and correction ownership.
Do not assume procedural interpolation supplies missing artistic timing, balance, or intent.
Validate one representative action before expanding a new mechanism across the library.

Keep the user review queue small: at most three actions, stable IDs, numbered current/previous revisions, and frame/time references.
Preserve a user-preferred older result. Keep A/B/C diagnostics available without crowding the current-runtime review.
Inspect the reported interval first. A nearby improvement does not resolve the reported defect.

Trace the first stage where the defect appears:

1. Check the selected original motion and whether it suits the action.
2. Check import, avatar mapping, scale, root motion, and edited variants.
3. Check phase ranges, playback rate, event timing, and blend ownership.
4. Check motor displacement and prop/contact trajectories.
5. Check final IK targets, pole hints, weights, limits, and release behavior.

Use isolated diagnostic toggles where available. Restore them before recording the final result.
When source and production differ, record actual playable time, mixer weight, and built-in IK flags before adding contact correction.
Distinguish Unity's authored humanoid foot-goal solve from final terrain/contact correction before changing either stage.
Make their settings explicit in diagnostics. Verify the affected actions before changing shared playback defaults.
For extracted root travel, compare the edited clip plus motor trajectory against the source on the actual production avatar.
Reconstructing one clip does not validate its support contacts with the preceding and following clips.
Change the identified cause, then repeat the same scenario. Avoid simultaneous speculative pose and timing changes.
Preserve source assets; keep intended variants editable and identifiable.
Reuse the existing graph, contact, and transition systems. Do not add competing transform writers.
Run relevant functional tests, then inspect the rendered change and its affected transition neighbors.

## Supported idle and load review

For held tools, verify the artist's driving hand, prop-local grip position, and blade orientation across the full action.
Inspect the complete imported prop hierarchy. Mining's pickaxe included an authoring rig that changed its attachment frame; using the static mesh removed it.
For moving contacts, constrain the correction relative to the authored arm direction and preserve its elbow plane unless explicitly overridden.
Do not rebuild prop rotation from the two hands and character facing each frame. It discards authored roll.
Resolve the original avatar dependency before replacing a clip's CopyFromOther setup with CreateFromThisModel.
Chopping on 2026-09-15 required the pack's separate T-pose avatar; automatic per-clip avatars corrupted the arm motion.
Supporting-hand corrections on a moving tool must use palm contact and follow authored motion through the existing blend path.
Otherwise world-space target lag can pull the arm away during a fast swing. Preserve authored finger poses unless grip work requires a change.

For supported waiting poses, review palm orientation and fingers as well as contact position.
A palm marker on a rim can still leave the hand visibly tilted off the surface.
Fit support to the mesh used in that phase. A closed lid's height does not locate the open chest rim.
Use the existing contact orientation path and preserve smooth acquisition and release.
Inspect body motion separately from head motion. A moving head does not establish an active supported idle.
Transfer restrained authored body variation into an editable pose when needed; retain foot support and bounded hand corrections.
Check the complete loop seam and transitions. Do not add unrelated sway merely to avoid stillness.

For extracted idle variants, verify root import settings on the production avatar before increasing reach correction.
A frozen source sample can shift under root extraction even when its muscle curves match the source.
For a standing hop, inspect grounded anticipation before motor launch. A source-clock offset can hide the dip in A.
For carrying, separate grip accuracy from apparent weight. Check pickup force, body/load timing, turns, and placement.
A box that follows the hands can still look weightless. Preserve an unresolved defect when the user chooses to move on.
Record that choice as deferred, not visually approved, and remove it from the active feedback queue.

## Existing project tools and limits

Paths below were inspected on 2026-09-12. Reverify before use.

- `Assets/Editor/SidekickInteractionComparison.cs` provides Play Mode menus under `Tools/Actors/Sidekick/`.
- `Show Source Clip Comparison`, `Show Green Source Overlay`, and `Hide Source Clip Comparison` are existing menu names.
- The comparison copies the active clip inputs and phase timing. It is not an independent original-rig or natural-rate reference.
- `Assets/Scripts/Game/Animation/ActorAnimationGraph.cs` and `ActorPerformancePlayback.cs` own shared playback facilities.
- `ProceduralPoseRig.cs`, `InteractionPoseBlend.cs`, and `FootPlacementSolver.cs` in that directory provide correction paths.
- `Assets/Scenes/Tests/SidekickInteractionReview.unity` is an interaction fixture, not proof of every creature or locomotion action.

Use `Assets/Editor/HumanoidQualityReviewCapture.cs` for its supported humanoid recipes and `tools/animation-review/package_review.py` for focused review pages.
These tools do not cover every action. Verify source labels, phase clocks, and paused skinned-mesh refresh before trusting a new recipe.
Use production playback code for the final preview. An independent preview solver can hide production defects.
Use existing capture tooling where possible. Verify package compatibility before adding Unity Recorder or another tool.
Clean up temporary cameras, render textures, comparison graphs, and callbacks after capture. Do not save diagnostic Play Mode state into assets.

## Report and retain

For each finding, record action, stage, timestamp/frame interval, visible symptom, measured evidence, suspected cause, and confidence.
Separate an observed defect from an unverified cause and an artistic preference.
Keep the recipe, baseline, revised sequence, and metadata together outside tracked source, for example under `local-only/animation-review/`.
Link them from the owning evidence record. Preserve approved references before capture pruning can remove them.

Report statuses separately: functional checks, visual inspection, remaining defects, and Bryan's approval.
Use `not inspected` or `unverified` for missing evidence. Never turn passing tests into a claim of natural motion.
Keep the [broader continuity audit](../../docs/design/2026-09-12-animation-continuity-validation.md) open until its remaining paths receive evidence.
Present a small, complete review bundle so Bryan can judge the result without producing defect screenshots himself.

## Urgency and contact geometry checks

Round 4 ladder feedback showed that faster cycles can retain a slow mount or exit. Review the complete action when judging urgency.
Round 5 rejected the fast wall-climb silhouette despite shorter measured duration. Compare posture and body clearance with the user's preferred normal gait.
Prefer that suitable authored gait at a faster matched pose/root rate before selecting a more forceful but unsuitable action family.
Align comparisons by meaningful phase events and disclose different playback rates. Do not confuse a source-clock offset with a pose defect.
Do not add a separate authored-clock hold during blend entry. Advance pose and root together through the existing transition.
Inspect the visible finger cup, palm, and object mesh. Marker distance alone cannot establish a rung grip or surface contact.
For a short obstacle, consider a complete authored route when height and full-path clearance fit. Preserve the normal route as fallback.
Retain the reported baseline and verify the selected route before claiming the defect resolved.

## Provenance and maintenance

Created 2026-09-12 from Bryan's authored-animation preference and repeated contact, timing, and transition review failures.
Supported-idle, root-extraction, anticipation, and deferred-review guidance added from the 2026-09-13–14 live feedback iterations.
See [the live feedback record](../../docs/design/2026-09-13-live-animation-feedback.md) for revision-specific evidence and limits.
Read the [Spore and Overgrowth research note](../../docs/research/2026-09-12-animation-quality-review-research.md) when considering target-relative authoring, secondary motion, or interpolation changes.
Reverify menus with `rg -n 'MenuItem|Sample' Assets/Editor/SidekickInteractionComparison.cs`.
Reverify ownership with `rg --files Assets/Scripts/Game/Animation -g '*.cs'`.
Validate frontmatter with skill-creator's `scripts/quick_validate.py` and verify this skill's README routing and generated discovery stub.
