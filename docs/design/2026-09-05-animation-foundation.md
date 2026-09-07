# Animation foundation — 2026-09-05

## Active Tracker

Status: Live deer use native Playables with male/female prefabs. Bryan authorized the comparison and shared procedural pose prototype. Spine, look, spring chains, and foot correction now run in the prototype. Moving contact tuning, action markers, humanoid interaction, and animated carcasses remain open.

Current next action: Validate the shared procedural rig with a second quadruped. Bryan accepted the deer prototype overall on 2026-09-07 and deferred its small remaining issues. See the [procedural pose prototype](2026-09-05-procedural-pose-prototype.md). Polyperfect remains the accepted deer baseline; the Quirky deer style was rejected.

- [ ] Confirm deer rig, root motion, and material compatibility in Unity.
- [ ] Implement deer playback and lifecycle handling.
- [ ] Implement and review deer foot IK.
- [ ] Implement humanoid lid interaction and cancellation.
- [ ] Review shared playback components after both examples work.

## Scope and authority

Bryan approved preparation for project-owned Playables animation, animal foot IK, and humanoid hand interactions.
Bryan released Unity in the follow-up message "Unity is free". Editor preflight is now authorized.
The test queue below is a written queue, not an automation or submitted Editor job.

Inspected tree: `harvest-vertical-slice`, HEAD `1df21b2`, with unrelated uncommitted work. Unity version: `6000.7.0a5`.
Revalidate these facts before implementation because another agent is editing this checkout.

Goal: animate one deer on uneven spherical terrain, then one humanoid opening a hinged lid with maintained hand contact.
These examples establish the required boundaries before broader asset imports.

Source records:

- [Deer asset inspection](../research/2026-09-05-deer-animation-preflight.md).
- [Creature behavior and persistence](2026-08-26-creature-residency.md), especially section 15.
- [Asset adoption map](../research/2026-08-11-asset-adoption-map.md), animation section.

Reuse models, textures, rigs, and clips. Study vendor code only as reference for project-owned implementations.
Do not import vendor runtime scripts, controllers, demo prefabs, or editor tools.

## Ownership contract

| Concern | Owner | Contract |
|---|---|---|
| Position and orientation | Existing movement driver | Animation never writes the authoritative root transform. Start with in-place motion. |
| Behavior and action eligibility | Existing gameplay and state machine | Decide targets, transitions, interruption, and outcomes independently of renderer visibility. |
| Action progress | Project-owned action execution | Own elapsed time, cancellation, and once-only effects. Animation follows the same action identity and progress. |
| Clip evaluation | Playables presenter and Animator | Sample clips, blend poses, and manage graph resources. No Animator Controller asset required. |
| Contact and event authoring | Clip/action settings | Store named markers and continuous contact curves. Convert authoring settings to runtime snapshots. |
| Feet and hands | Rig-specific IK configuration | Resolve targets through injected capabilities. Correct the pose after clip blending. |
| Object movement | Interaction execution | The lid owns its progress; the hand follows the lid's contact target. |

Gameplay markers use the action clock, even when animation evaluation is culled.
Cosmetic markers can follow playback and may be suppressed when presentation is absent.
No sound callback or bone evaluation may transfer inventory, apply damage, or change persistent state directly.

Use an action instance identity plus marker identity to prevent repeated gameplay effects.
Evaluate crossed markers over `(previousTime, currentTime]`, accounting for loops where allowed.
Specify time-zero markers separately at action start. Preview seeks emit no effects by default.
Dispatch cancellation once and invalidate remaining markers before releasing interaction targets.

## Current integration points

| Current file | Verified fact | Required change during implementation |
|---|---|---|
| `Assets/Scripts/Planet/Creatures/CreatureView.cs` | Creates capsules; owns live and corpse views separately. | Own a presenter per visible deer; retain placeholders for other species. |
| `Assets/Scripts/Planet/Creatures/CreatureResidencyService.cs` | `LiveCreature` exposes identity, species, position, up, and forward only. | Add the minimum presentation snapshot: behavior, measured movement, and discontinuity handling. Keep Animator references out of authority code. |
| `Assets/Scripts/Planet/Creatures/CreatureLibrary.cs` | Species settings contain behavior data and placeholder dimensions/colors. | Add or reference visual authoring data without reordering persisted species indices. Resolve it outside simulation. |
| `Assets/Scripts/Game/Actors/SurfaceCharacterController.cs` | Exposes `Pose` and `Grounded`; owns vertical movement. | Expose movement inputs needed by presentation. Do not infer jump state from a selected clip. |
| `Assets/Scripts/Planet/Character/PlanetCharacterController.cs` | Owns a capsule child and player concerns. | Attach the humanoid presenter in the second example. |
| `Assets/Scripts/Game/Ai/AdaptiveStateMachine.cs` | Flat machine; shared stateless state objects; no stop API. | Harden lifecycle before interaction ownership; add nesting only when the lid action requires it. |

The deer is not a `CreatureView`-only edit. The snapshot and death-to-corpse handoff also need design and implementation.
Death currently removes the live view. Preserve a visual death transition keyed to the existing corpse identity without delaying authoritative death.
Do not leave both a live body and a corpse body drawing the same creature.

Keep reusable playback and action logic under `Assets/Scripts/Game/` within the existing assembly boundary.
Keep planet surface adapters under `Assets/Scripts/Planet/`.
Finalize new filenames after Q1–Q3 establish rig and material requirements. This plan does not authorize broad subsystem rewrites.

## Playback and evaluation

Use one owned graph per presented actor initially. Build it on presentation creation and destroy it on teardown.
Share immutable clip definitions and rig configuration; keep playback clocks, weights, and contact state per actor.
Do not rebuild the graph each frame.

The owner advances movement, sets the actor root, and prepares targets before animation evaluates.
Clip mixers feed optional action layers; procedural jobs adjust the blended pose before output reaches the Animator.
Choose one evaluation owner and clock during implementation. Never combine automatic graph advancement with a second manual evaluation.
Surface queries run outside animation jobs; jobs receive prepared target data and resolved bone handles.

Start the deer with `Deer_Idle_Breath`, `Deer_Walk`, `Deer_Run`, and `Deer_Death`.
Use authored transitions only after inspecting their movement and contact timing.
Blend gait phase deliberately. Repeated behavior snapshots must not restart clips.
Reset velocity estimates and planted contacts after teleport, promotion, or world replacement.

Default root motion is disabled, but inspect animated skeleton-root displacement as well.
Disabling `Animator.applyRootMotion` alone does not prove that a clip has no visible root drift.
Match clip stride to actual movement through measured authored speed and bounded playback scaling.

## Foot IK contract

Each foot has a contact curve, a contact target, a bend hint, reach limits, and an authored sole offset.
Contact curves describe plant and release phases. They are not currently supplied as custom curves in the deer import metadata.

1. Sample contact intent from the active gait, with a defined owner during crossfades.
2. Query the surface along local gravity, including a valid normal and distance.
3. Hold a planted contact in world space on static terrain.
4. Release the contact during swing, loss of support, teleport, or excessive reach.
5. Apply bounded body height and tilt before solving each leg.
6. Blend corrections by contact weight; preserve authored swing motion.

Confirm the actual deer limb hierarchy before selecting solver chains.
The metadata names ankles, toes, elbows, shoulders, and fingers; it does not prove which joints form each required chain.
Do not force all quadruped limbs into a guessed Humanoid layout.

Use the existing surface-query capability, not a second planet geometry implementation.
For future moving support, store contacts relative to that support. The first deer test uses static terrain.
On query failure, fade correction out and report diagnostics; never snap to zero or stretch beyond the rig's reach.

## Humanoid interaction contract

Choose one Synty humanoid and a suitable owned clip after the deer playback interface exists.
Validate hand reach, rig mapping, and clip compatibility before declaring the asset selected.
The test object is a plain hinged lid with a contact target; art polish is unnecessary for the proof.

Action phases: approach, reach, contact/open, release, complete.
Each phase defines allowed interruptions, target validity, and cleanup.
The lid progress and hand target share one interaction clock; do not run unrelated lid and character timelines.
Use IK curves for continuous hand weight, and markers for contact and release.
If reach is impossible, reposition or reject the interaction before contact.

Reuse the existing state machine for action coordination, with these prerequisites:

- Add explicit stop/cancel semantics that exit active ownership exactly once.
- Define repeated `Start` behavior; it currently replaces the state without an exit.
- Report invalid resolved destinations; `Switch` currently ignores them.
- Keep nested machine state per actor, not on shared state instances.
- Let actions update while selected interruptions are restricted. Do not revive the original blanket `IsBlocking` early return.

These are scoped prerequisites, not completed fixes. Add tests when implementing them.
Hierarchy describes action phases. Animation layers separately allow movement and upper-body actions to coexist.

## Delivery sequence

| Phase | Work | Exit evidence |
|---|---|---|
| 0 | Disk inspection and this contract | Inspection record and deferred queue exist. No Editor use. |
| 1 | Unity preflight, then minimal content import | Verified rig chains, clip measurements, material behavior, and dependency list. |
| 2 | Deer playback | Correct blends, death handoff, and lifecycle without IK. |
| 3 | Deer terrain IK | Contact metrics plus clips on uneven terrain; Bryan reviews the look. |
| 4 | Humanoid lid action | Maintained hand contact and correct cancellation/events. |
| 5 | Consolidate proven shared behavior | Both examples retain their checks; no speculative generic rig framework. |

## Deferred validation queue

Q1 has binding/hierarchy/frame-rate evidence. Q2 has sampled trajectories but no approved contact timeline. Q3 has a partial render check. Q4–Q11 remain NOT RUN; their implementations do not exist.
Unity was released by Bryan; recheck Editor readiness before each test session.

| ID | Prerequisite | Procedure | Pass condition / evidence |
|---|---|---|---|
| Q1 | Editor released | Preview the selected deer FBXs in the scratch project. Record hierarchy, units, axes, bounds, frame rate, duration, and root displacement. | Exact chain paths and measurements recorded; no missing rig bindings. |
| Q2 | Q1 | Scrub idle, walk, run, and death. Mark every hoof plant/release and inspect transitions. | Contact timeline recorded; root-motion treatment and authored gait speed resolved. |
| Q3 | Q1 | Inspect texture/material dependencies and test a project material on the skinned rig in isolation. | No missing dependencies; acceptable skinning, shadows, depth, bounds, and motion-vector behavior. Do not assume static-prop shader compatibility. |
| Q4 | Phase 2 | Exercise idle/walk/run transitions and repeated identical requests at 30, 60, and 120 FPS. | No T-pose or repeated restarts; timing remains consistent. Record video and playback diagnostics. |
| Q5 | Phase 2 | Kill the deer; cycle visibility/residency 100 times; reload the world. | One death/body handoff, no duplicate body, graph count returns to baseline, no stale callbacks or exceptions. |
| Q6 | Phase 3 | Walk, turn, stop, and flee across a slope and a crest at three widely separated planet positions. Compare IK enabled/disabled. | No inverted limbs or reach violations. Record hoof contact error and sliding during planted phases. |
| Q7 | Phase 3 | Remove query support, teleport, and cross residency boundaries. | Corrections release smoothly where appropriate; teleport clears contacts immediately; no foot remains pinned to the prior location. |
| Q8 | Phase 4 | Open the lid at different valid heights/distances. Cancel before contact, during opening, and during release. Destroy the target mid-action. | Hand follows the target; unreachable interactions reject/reposition; cleanup and gameplay effects each occur once. |
| Q9 | Marker implementation | Use the existing EditMode framework for frame skips, loops, crossfades, cancellation, time-zero markers, and silent seeking. | Exact expected marker order/count. No gameplay effect depends on renderer evaluation. |
| Q10 | FSM changes | Extend `AdaptiveStateMachineTests` for stop, repeated start, invalid destinations, nested exits, and actor isolation. | Deterministic lifecycle counts and no shared mutable child execution. |
| Q11 | Both examples | Compare fixed-camera captures and profile fixed actor counts before/after IK. | Record CPU avg/p95 and actor/graph counts; Bryan reviews visuals. Set the performance budget before measurement. |

For Q6, proposed initial targets are <=2 cm sole contact error and <=3 cm planted-foot drift on valid reachable terrain.
For Q8, propose <=2 cm hand-target position error during full contact.
These are provisional acceptance targets, not measured results. Confirm scale in Q1 before locking them; do not relax them after seeing failures.

Capture seed, camera pose, quality tier, actor count, clip time, contact weights, and query failures with each visual comparison.
Archive captures and sidecars before later runs prune them. Video is required for sliding and interruption assessment.
New animation tests and diagnostics do not exist yet. Queue descriptions are not executable command names.

No build or test was run for this documentation-only preparation. Existing NUnit tests supersede older skill text claiming no test framework exists.
No code changed, so no graphify update is required.

## Live deer implementation — follow-up authorization

Bryan explicitly requested replacing the placeholder and creating male/female appearances.
The following work supersedes the preparation-only status above:

- `CreatureVisualSettings` snapshots prefab and clip references at settings initialization.
- `CreatureSpeciesDto.Visuals` carries that snapshot; only the deer species currently references visuals.
- `LiveCreature.Velocity` reports measured movement from the simulation tick.
- `CreatureAnimationView` owns one manual PlayableGraph and blends idle, walk, and run without restarting on each snapshot.
- `CreatureView` owns and disposes presenters as residents enter and leave the live set.
- The original male mesh is unchanged. A separate antlerless mesh supplies the female prefab.

Sex is currently a stable visual assignment from the complete creature identity, with approximately equal distribution.
It does not implement breeding, sex-specific behavior, or a new persistence field.
Both variants have the same body scale and behavior settings.

Observed in the live Forest biome: seven deer, three male and four female.
Captures: `local-only/animation-preflight/deer-male-live.png` and `deer-female-live.png`.
The project shader renders both variants in the actual planet scene.
One hundred create/evaluate/dispose cycles passed a direct graph-validity assertion before and after disposal.
An identity sample produced 486 male assignments among 1,000 distinct valid creature identities.
Unity compilation succeeded after refreshing the newly added script assets.
`graphify update .` completed after these code changes.

These checks do not complete the full queue. Multi-speed gait quality, exact planted-foot contact, animated motion vectors, and corpse handoff remain open.
Carcasses still use the existing placeholder representation. No IK is implemented in this slice.
Walk/run authored speeds are provisional visual settings and need measured calibration before foot planting.

### Antlerless mesh derivation

Source: the imported `SKM_Deer_Rig.fbx` mesh. Output: `Assets/AssetPacks/PolyperfectAnimals/Deer/DeerAntlerless.asset`.
The edit preserves the original vertex channels, bone weights, bind poses, and skeleton.
Remove triangles touching vertices above Y=1.82 and behind Z=0.91, excluding vertices whose dominant bone name starts with `Ear`.
These coordinates apply only to this inspected source mesh at its imported rest pose.
Weld matching positions for boundary discovery; close each new cut loop with reverse-wound cap triangles.
The result has 878 triangles versus 1,030 originally, including 16 new cap triangles.
Unused original vertices remain in the mesh; only rendered topology changed. No runtime mesh editing occurs.
The live female capture confirms the antlerless silhouette and retained ears.

