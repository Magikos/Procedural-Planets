# Shared actor performance animation — 2026-09-09

## Active Tracker

Status: First performance/airborne implementation and reactive ledge capture are running in the review scene. The final regression run passed 131 tests. Visual acceptance remains pending.

Current next action: Review jumping, landing, and reactive grabs in HumanoidAnimationReview; then author the injured gait and contact-aligned locomotion slice.

- [ ] Phase 1: Shared performance data, phase mapping, and a physically driven jump demonstration.
- [ ] Phase 2: Contact-aligned locomotion and injured gait demonstration.
- [ ] Phase 3: Shared effectors, grasp sockets, door manipulation, and humanoid/dog pickup demonstrations.
- [ ] Phase 4: Skill-dependent attacks and coordinated procedural ownership.
- [ ] Phase 5: Active ragdoll transitions, recovery, scale testing, and Planet player integration.
- [ ] Bryan visual review of each demonstration.

---

## Goal and authority

Actors express skill, strength, injury, anatomy, and physical context through coherent movement. Hands, feet, and mouths meet actual surfaces. Transitions preserve motion and support. Basic performances assume low proficiency and avoid unrequested acrobatics.

This design extends our own Playables system. Animancer and FImpossible are reference sources for selected algorithms and authoring concepts. We do not install another animation runtime. Source adaptations require recorded provenance and applicable license review.

Bryan explicitly authorized implementation with research and Unity testing. This instruction supersedes the older design-document read-before-code workflow for this work. It does not constitute visual acceptance or authorization to remove existing functionality.

Tree inspected: `harvest-vertical-slice`, dirty working tree above `d1e0f62`. Unity: `6000.7.0a5`. Revalidate these facts when resuming.

Preservation baseline: [Synty humanoid basics](2026-09-08-synty-humanoid-basics.md) and [shared agent systems](2026-09-07-agent-systems.md).

## Existing systems to extend

| Existing owner | Keep and extend |
|---|---|
| `SurfaceCharacterController`, `ActorCollision`, `ActorTraversal` | Gravity-relative movement, collision, water, stance, traversal, and physical support |
| `ActorAnimationGraph`, `AnimationGraphSampling`, `ActorPoseCadence` | Graph lifetime, manual timing, blending, and optional pose evaluation |
| `HumanoidAnimationView`, `CreatureAnimationView`, `BirdAnimationView` | Actor-specific presentation binding; migrate incrementally through shared mechanisms |
| `ProceduralPoseRig`, `ProceduralRigDefinition` | Rig snapshots, base-pose restoration, explicit procedural ordering, and named interaction chains |
| `FootPlacementSolver`, `LimbPoseSolver`, `ActorReachMotion` | Contacts, bounded reach, release, and observed contact |
| `BoneChainSpring`, `SurfaceChainSolver` | Tail/secondary motion and surface-following chains |
| `ActorDeathPose` | Existing bounded settling and frozen death pose |

The current graph already creates base playables once per actor. Recreating that behavior under a new cache name provides no performance benefit.

The current humanoid jump advances one clip by elapsed airborne time. This cannot coordinate a variable physical flight with authored takeoff and landing. Current foot contacts use sampled foot height; they are estimates, not authored contact phases.

## Responsibilities and data flow

1. Authority updates action intent, physical movement, condition, capabilities, and outcomes.
2. Performance selection chooses an eligible authored performance from those values.
3. Phase control maps physical progress and observed support to authored clip intervals.
4. The shared graph blends compatible poses and retains outgoing state during interruption.
5. Body positioning and contact solvers align the evaluated pose to actual targets.
6. Secondary motion and physics ownership apply only where permitted by the active performance.

Rendering never determines damage, ownership, permissions, or successful inventory transactions. Presentation can report contact observations. Authority validates and commits the interaction once, using an action instance identifier.

No frame-dependent Unity object handle belongs in a save or network payload. Persist stable actor, action, performance, target, and phase identifiers with authoritative timing. Resolve scene bindings locally.

## Performance assets

Authoring assets compile to validated runtime snapshots. Share immutable clip metadata across actors. Keep clocks, fade state, contacts, and graph handles per actor.

A performance contains:

- Stable identity, action identity, supported rig family, and compatible equipment/effector requirements.
- Eligibility ranges for proficiency and relevant conditions. Monster level can supply proficiency but does not define it.
- One or more clip intervals with named phases, playback limits, and transition settings.
- Contact tracks naming effectors and normalized contact weights.
- Compatibility groups for continuous blending, including gait phase correspondence.
- Interruptible intervals and permitted recovery branches.

Select structural techniques discretely. Blend continuous style differences only within a compatible group. A careful pull-up, vault, roll, and stuck-axe extraction are distinct techniques. Do not average their complete poses using one skill slider.

Latch the selected technique for an action instance. A skill increase during a jump must not replace its sequence midair. An urgent injury or hit requests an explicit interruption/recovery path.

Missing optional variants fall back to a validated basic performance. Unsupported anatomy or required contacts reject the action. Never silently substitute an incompatible clip.

## Physical phases

Shared phases describe progress; they do not replace the existing gameplay behavior system. Each action defines its meaningful phases and valid transitions.

Jump phases distinguish takeoff/ascent, apex, descent, contact, and recovery. Actual vertical velocity and grounding govern flight transitions. Clip time must not reach landing before physical contact. Passive falls remain distinct from intentional jumps. Short support losses retain the restrained drop response.

Authored anticipation can occur before authority commits a jump. The first migration preserves immediate motor input; adding a pre-jump delay requires an explicit movement change and responsiveness review.

Long flights may hold or loop an authored airborne interval. Ceiling contact can end ascent early. Landing can interrupt descent at any time. A new jump or traversal can interrupt recovery. Reset, teleport, swimming, death, and invalid targets clear incompatible phase state.

Maintain physical phase state when pose evaluation is skipped. Do not replay skipped gameplay events on the next rendered frame. Do not use an IK correction to move the collision root.

### Opportunistic transitions — Bryan's clarification

An action does not require a fully planned sequence. While airborne, the movement system can discover a reachable ledge after a wall collision. A valid grab interrupts ascent, descent, or passive falling. The selected animation expresses the accepted physical transition.

Reuse ActorTraversal and ActorCollision queries. Validate a real supporting edge, hand reach, approach direction, body clearance, and actor capability before committing. Do not let a clip manufacture a ledge. Preserve velocity until the motor accepts the constraint. Blend pose and contact weights into the grab while collision remains authoritative.

Track the released edge and a short reacquisition restriction so deliberate drops do not immediately grab the same edge. Target movement/destruction cancels the constraint safely. Separate automatic protective grabs from a requested climb. A low-proficiency actor can catch, struggle, and pull up; a proficient actor can choose another supported recovery.

Acceptance includes jump into wall then descend into grab range, missed ledge, corner approach, blocked hanging clearance, deliberate release, target destruction, and interrupted jump recovery. Support both planned traversal requests and newly discovered opportunities through the same owner.

## Gait and injuries

Contact alignment needs explicit landmarks for each supported effector. Walk/run cycles with matching foot order can synchronize. Idle remains outside gait synchronization. Limp cycles may require unequal support intervals and per-clip phase remapping.

Use additive layers for compatible posture changes, breathing, recoil, and small pain responses. Use alternate gait performances for reduced loading, limping, dragging, or a missing limb. Contact tracks, stride, pelvis support, and movement limits must agree.

Start with a left-leg injury on the humanoid. Then prove the same condition selection on a quadruped. Do not create every skill-by-injury-by-equipment clip combination. Use explicit precedence and a small authored compatibility set.

## Effectors and interactions

Extend the existing named interaction chains. A hand, mouth, claw, or tentacle tip is an effector with capabilities, a contact socket, reach limits, and a suitable solver.

Objects expose grasp sockets and requirements. The sequence is approach, reach, contact, attach/manipulate, release, and recovery. Hand orientation and local socket offsets matter as much as position. A dog mouth uses the same interaction contract with different bindings and motion.

Authority owns target reservation and attachment. Validate target lifetime, reach, mass/capability, permissions, and action identity. Cancellation, target destruction, interruption, and actor destruction must release reservations and visual constraints. Moving targets use their current local socket transformed to world space.

A round knob requires a turning technique. A dog may retrieve a suitable object without supporting that technique. Two-handed objects require two compatible contacts and one object owner.

Door motion must follow the hinge constraint. Maintain knob contact while the actor adjusts body position. Pickup must preserve the object transform at attachment, then converge to a carry pose without a snap. Remote views must converge to authoritative attachment state without performing another transaction.

## Procedural ownership and FImpossible preservation

Preserve look, spine, lean, tail, feet, hand reach, surface chains, and death settling. Inventory their current adaptations before changing ordering. Full active ragdoll and get-up remain additional work, not existing functionality.

Each procedural pass declares affected bones, priority, and influence. Restore the sampled pose once before evaluation. Never feed last frame's procedural result back into the next base pose.

Body support precedes final limb contacts. An interaction can suppress or reduce gaze on overlapping head/neck bones. A mouth pickup therefore cannot fight the look solver. Physics-controlled bones suppress animation/procedural writes until control transfers back through a recovery blend.

Validate conflicting exclusive ownership at rig initialization. Overlap that is intentional needs an explicit composition rule. Do not rely on dictionary iteration order or independent MonoBehaviour updates.

## Performance strategy

Measure these separately: selection/phase work, graph evaluation, ground queries, procedural solve, and skinning. Report warm steady-state median and p95, allocations, active actors, evaluated bones, and target hardware.

Cache immutable clip analysis, phase/contact tables, and rig lookup results. Reuse per-actor playables and fade buffers. Invalidate shared data when its source changes. Bound dynamic state counts and release graph-local resources on disposal.

Do not cache a final IK pose across different terrain, targets, injuries, or body transforms. Pose sharing is eligible only when all relevant inputs match. Do not claim savings without measuring them.

Use distance/visibility cadence for noncritical actors. Contact interactions, visible rapid movement, and physics transitions need suitable minimum evaluation rates. Skipping visuals must not skip gameplay outcomes. Avoid raycasts and solver work for inactive contacts.

Profile before selecting jobs/Burst or a new batching design. Preserve arbitrary gravity and actor scales. Compare the same actor counts, clips, camera, and fixed input sequences before and after each optimization.

## Implementation sequence and acceptance

### Phase 1 — Performance and phase foundation

Add validated reusable performance timing and selection data. Integrate it into the current humanoid jump slot without replacing locomotion, traversal, swimming, or shared IK. Expose selected performance and phase in review diagnostics. Preserve the old constructor behavior for callers without an authored performance.

Test phase boundary continuity, invalid metadata, variable ascent/descent, early landing, passive drops, interruption, reset, sideways gravity inputs, and skipped evaluation. Capture the same physical jump before/after. Confirm the authority root is unchanged.

### Phase 2 — Injury and gait contacts

Author/import suitable normal and limp clips. Mark support phases and align them. Replace height heuristics only where verified authored data exists. Preserve fallback behavior for unmigrated actors. Inspect reversals, idle starts, slopes, stairs, and injury transitions.

### Phase 3 — Contact interactions

Extend existing reach and rig bindings with socket offsets, capabilities, ownership, and release. Prove a humanoid table pickup, a door knob interaction, and a companion mouth pickup. Measure final position/orientation error, attachment continuity, and invalid-target cleanup.

### Phase 4 — Skill performances and procedural composition

Demonstrate basic and proficient attack techniques with authority-owned outcomes. Exercise interruption and injury together. Validate look/spine/tail/lean preservation and overlapping effector ownership on humanoid and quadruped rigs.

### Phase 5 — Physics, scale, and integration

Add active ragdoll and recovery using the existing death-pose boundary. Profile mixed actor populations and cadence changes. Run preservation scenarios before migrating the Planet player. Planet camera collision requires the existing terrain-query adapter work.

All phases require code validation and runtime evidence. Bryan reviews the visible motion before visual acceptance. Tests do not certify animation quality.

## Preservation scenarios

Keep idle, walk, run, strafe, crouch, crawl, stairs, step-up, vault, ledge grab, pull-up, drop, swimming, diving, reach, camera modes, and reset available in the review scene.

Keep creature/bird locomotion, survival actions, death settling, surface fitting, look, spring chains, and pose cadence functional. Keep gameplay actor authority separate from rendering. No Planet player replacement occurs before review validation.

## First implementation and evidence — 2026-09-09

Implemented files:

- `ActorAnimationPerformanceLibrary.cs`: authoring assets, immutable metadata snapshots, deterministic proficiency/condition selection, and validation.
- `ActorAirborneMotion.cs`: gravity-relative physical phase tracking, intentional-jump latch, passive-drop distinction, and authored recovery duration.
- `ActorPerformancePlayback.cs`: manual phase sampling, explicit interrupted fades, and two reusable states per phase.
- `HumanoidAnimationView.cs`: optional profile integration, latched jump selection, diagnostics, and existing pose pipeline preservation.
- `HumanoidAnimationPrototype.cs`: library binding and post-motor reactive ledge queries.
- `ActorTraversal.cs`: bounded reactive capture through the existing JumpGrab/Hanging path, with release suppression.
- `ActorPerformanceReviewAuthor.cs`: reusable basic profile authoring; `HumanoidReviewAuthor` assigns it when rebuilding the scene.

The scene references `Assets/Art/Characters/SyntyHero/Basic Performances.asset`. Only the basic jump performance is authored. Proficiency selection and condition fallback have tests, but high-skill and injured visual variants are not supplied yet.

The full owned Jump FBX was already present inside the earlier imported file. Its default take spans source frames 20..74; the old import exposed only 30.5..36.7. `Jump Full.fbx` preserves the original asset and exposes the full take. Source contact-sheet inspection established initial intervals: ascent 32..43, apex 43..45, descent 45..56, landing/recovery 56..74. These markers require visual refinement. They are not automatically detected ground contacts.

The movement motor still jumps immediately. Anticipation is not implemented. Descent progress follows a bounded velocity ratio, not predicted time to contact. Long descent holds its final airborne pose until actual grounding. Passive falls retain the prior small-drop/large-fall behavior.

Performance selection latches per jump. The humanoid adapter requires all four phases, preventing partial profiles from dropping selection midair. Landing duration comes from the selected authored interval. The shared generic library does not require humanoid phases for other actions.

Each phase has two graph-local playables. A phase restart uses an invisible spare; if both are visible, it waits for one to fade out. Outgoing sample times remain fixed during the fade. This preserves pose continuity, but does not provide velocity-continuous inertialization. The first profile adds eight slots. Large libraries need measured active-set budgeting before broad actor deployment.

Reactive capture checks descending motion, intent toward the wall, standing capability, a vertical wall, a level top, two matching hand supports, bounded correction, and capsule clearance. The host suppresses captures while grounded, swimming, or holding drop. Capture correction is at most 0.4 m over 0.12 seconds. Climbing remains a separate request. These initial reach dimensions model the existing standing actor; per-effector anatomy remains Phase 3 work.

The live test started at (-1, terrain height, -7.4), pressed Jump once, and held forward. The actor hit the wall near z=-6.23. It entered JumpGrab while descending and reached Hanging at (-1, 0.65, -6.31). After settling, both hands were at ledge height 2.20 m, with lateral positions -1.22 and -0.78 m. Holding drop released the constraint and did not recatch during the sampled descent.

Validation:

- Final EditMode job `7dfb1977bf5c43a7bf22866439f49b89`: 131 passed, zero failed or skipped. Covers new profiles/playback/phases/catches plus humanoid, motor, traversal, procedural pose, feet, lean, graph, bird presentation, and death pose.
- Graphify update completed: 13,748 nodes and 19,884 edges. Final fresh Play session had no console entries matching exceptions.
- Earlier focused jobs: 31 passed (`c1744ce7735c4f2ea9fc30a82398fd2c`) and 81 passed (`eadd6ad43c5847239b97a2aa42daf02b`).
- Core build: zero warnings/errors. Final Planet build: 21 existing warnings, zero errors. Final Editor build: 26 existing warnings, zero errors.
- `local-only/actor-performance/2026-09-09/before/` and `after/` contain matched fixed-input jump samples. `jump-comparison.jpg` combines four times from each run.
- `source/contact-sheet.jpg` records the source take inspection. `ledge-catch.txt` records the physical catch sequence. `reactive-grab-verified.png` records settled contact.
- A warm, single-actor CPU exercise measured the three-clip baseline at 0.0670 ms median / 0.0784 ms p95, and the profile variant at 0.0712 / 0.1004 ms. Both allocated zero managed bytes across 240 measured ticks. This excludes rendering, skinning, ground queries, and crowd behavior. The baseline lacks the profile's full jump, so this is a cost observation, not a controlled visual-equivalence benchmark or performance improvement claim.

Tooling corrections during development: the first capture disabled the host, which disposed its actor; the corrected capture paused Unity instead. One temporary capture snippet lacked a Playables extension qualification. Direct SyncVS access required reflection. Hot Reload patched new playback methods onto an old four-slot instance and produced `Index was outside the bounds of the array.` A full compilation and fresh Play session rebuilt storage; the final tests and live catch succeeded.

No Animancer or FImpossible vendor source was copied in this slice. The playback implementation adopts inspected concepts with project-owned code. Research references: [playback](../research/2026-09-09-animation-playback-research.md), [motion phases](../research/2026-09-09-motion-phase-research.md), and [FImpossible map](2026-09-08-fimpossible-actor-animation-map.md).

Remaining work: authored gait contact maps, injury visuals, advanced skill art, shared effector socket/ownership rules, door/pickup gameplay transactions, dog-mouth retrieval, active ragdoll/get-up, crowd profiling, and Planet player integration. Existing FImpossible-derived passes remain in place. No final visual acceptance is claimed.

## Additional contact references — 2026-09-09

The [Final IK and Moveen source review](../research/2026-09-09-contact-solver-research.md) identifies candidate improvements for the shared pose system.
These candidates are not implemented by that research:

- Capture limb bend planes and endpoint rotations before procedural parent corrections.
- Preserve moving contacts in target-local space, with effector socket offsets and explicit ownership during interruptions.
- Plan conservative and predicted foot targets, then coordinate release against supporting limbs.

Keep the existing shared motor and generic effector direction. Do not transfer vendor biped enums, world-Y assumptions, or pickup event authority.
The research includes source paths and expected cost implications. It makes no measured performance or visual-quality claims.

## Water and ledge refinement — 2026-09-09

Implemented on the dirty `harvest-vertical-slice` branch. Visual acceptance remains pending.

- Directional swimming uses owned Kevin Iglesias left, right, and backward motions. Forward swimming retains the existing source. Direction weights and cycle timing blend through the shared graph.
- Backward swimming uses a reclined sculling stroke. A full alternating overhead backstroke remains a separate content choice. Underwater directions currently share these directional clips.
- Grounded shallow water reports root depth and applies optional depth-dependent movement resistance. Existing animal profiles retain their prior speed by default.
- Basic Wade derives from the owned walk. It raises the arms and changes leg effort and spine sway. It shares the walk phase. This first authored wade is forward-only; directional wading needs dedicated content or masked upper-body composition.
- Ctrl + Space near a top edge requests a collision-checked lower-to-hang action. Input direction chooses the edge; facing is the fallback. Holding Ctrl through the descent does not immediately release the hang. A new Ctrl press releases it.
- RMB rotates the camera during traversal without changing the actor's constrained facing.
- Active ledge contacts remain exact. Release fades contact weight and the previous body correction instead of anchoring the outgoing pose to the old ledge.

Validation: EditMode job `45eb24df7d9a4664af89ad41b1cd52bd` passed 93 tests, with zero failures or skips. This covers swimming, animal swimming, camera, ledge catches, humanoid presentation, traversal, motor, playback, and airborne phases.
The first run exposed contact drift: `Expected: less than 0.00999999978f` / `But was: 0.0180684943f`. Active-contact smoothing caused it and was removed. A second run exposed a test asset-path error, `System.InvalidOperationException : Sequence contains no matching element`; the test now loads the authored `.anim` path.

Live checks reached Hanging from the block top at root (-1, 0.65, -6.31). Camera yaw changed while actor facing stayed fixed. Release reached zero hand weight within 0.2 seconds and the root descended. Swimming and wading samples kept the head above the review water surface. Captures and measurements are in `local-only/actor-performance/2026-09-09/water/` (`sources.jpg`, `drop.jpg`, `live.jpg`, `live.txt`). These checks do not establish final animation quality or crowd performance.

Additional research: [Motion Matching harvest](../research/2026-09-09-motion-matching-harvest.md). Its strongest next candidate is inertialization between base sampling and contact solving. Its feature cache stores precomputed search data, not completed IK poses. Keep skill, injury, action, and anatomy eligibility ahead of any motion search. No Final IK, Moveen, or Motion Matching runtime code was imported in this refinement.

## Context-dependent action choices — 2026-09-09

Bryan clarified that actors must choose feasible responses from skills, environment, occupied effectors, injuries, and ongoing motion.
A character holding a torch may attempt a one-handed ledge catch, release the torch before a two-handed catch, or fail to establish support.
These are runtime choices, not one fixed authored sequence. Do not automatically discard held items without a gameplay policy that permits the action.

Separate reusable responsibilities:

- Perception supplies reachable surfaces, contacts, momentum, hazards, and currently occupied effectors.
- Action selection evaluates feasible alternatives and their costs against actor capabilities and player or AI intent.
- Gameplay authority resolves skill checks, grip capacity, item release, damage, and outcomes. Record resolved outcomes rather than rerolling them during presentation.
- Presentation selects an eligible performance, reserves required effectors, and adapts the pose to actual contacts.
- Execution monitors support and interruptions. A failed catch can lead to falling, a recovery reach, or another available contact.

Use shared action preconditions, contact requirements, and outcomes to compose situations. Avoid a separate controller for every item/action combination.
Single-handed support, torch release, and these skill checks are requirements for later implementation; they are not implemented by the current review scene.

## Directional gait correction — 2026-09-09

The review now uses a matching Kevin Iglesias walk/strafe family. Import-time cycle offsets align supporting feet before weight blending.
The shared foot solver blends toward the rig bend direction near full knee extension, where the sampled knee plane becomes unreliable.
Wading was rebuilt from the replacement walk to retain the shared stride phase. Existing source clips remain available.

A repeated 240-tick forward/forward-right/forward-left/back sequence measured maximum per-frame foot displacement at 0.0897 m without IK and 0.1635 m with IK before the change.
The replacement measured 0.1167 m without IK and 0.1163 m with IK. This shows the prior IK amplification was absent in this sequence; it does not claim that every raw clip displacement decreased or establish general visual acceptance.
The first source families had conflicting support phases. The replacement's left/right/back ankle-height differences match forward within 5 cm RMS over a sampled cycle.
EditMode job `1dba7bea6c074082963a109b8e4d08ba` passed 58 tests with no failures or skips, including support-phase alignment and near-straight knee stability.
Captures: `local-only/actor-performance/2026-09-09/gait/gait.jpg`, with forward, forward-right, forward-left, and backward columns. Bryan's visual review remains the acceptance gate.

## Nearby ledge selection, diagonal clips, and water pace — 2026-09-09

Ctrl + Space without movement now searches forward, back, and both sides for a reachable ledge. This supports descent after climbing while facing inward.
Explicit movement still selects the requested edge direction. The existing 0.55 m reach limit remains; the action does not slide from the block centre.
Oblique searches center the grip on the nearest point of the detected face. A live 45-degree-facing case exposed the old diagonal-distance rejection; the corrected geometry has a regression test.
Drop-to-hang prevalidation now sweeps the complete path as well as checking sampled body clearance.

Standing locomotion uses dedicated four diagonal clips with adjacent eight-direction blend weights. Exact forward-right input no longer mixes forward and sideways leg poses.
The new clips share measured support phases with the forward walk. Crouching retains its existing directional clips.

Default surface movement uses the upright tread/paddle motion at an authored 1 m/s. Shift increases speed to 2.5 m/s and crossfades forward movement into the inspected breaststroke clip.
Sideways and backward movement retain their directional motions; underwater movement retains its current animations. Shallow water remains grounded wading.
Idle and slow forward paddle share sampling time to prevent phase disagreement when blending the same source clip.

Validation: job `88d5d7bc30ea4b42b86a6c78d9901dae` passed 83 focused EditMode tests. After the oblique geometry correction, job `47d89a38961a48239d3aed8f5ec9831f` passed all 20 traversal tests.
Live inward-facing Ctrl + Space reached Hanging. One-second live movement samples covered about 1.01 m normally and 2.56 m with Shift, including the initial water-entry tick.
Source inspection captures are in `local-only/actor-performance/2026-09-09/swim-strokes/`. Live diagonal, slow paddle, and fast stroke captures are in `local-only/actor-performance/2026-09-09/diagonal-and-paddle/review.jpg`.
Visual acceptance remains pending. These results establish selected clips, normalized transition weights, reachable contacts, and tested state behavior; they do not establish final visual quality.

## Palm contact, relaxed arms, and jump responsiveness — 2026-09-09

Interaction targets can opt into a contact socket. Legacy wrist targets, including traversal contacts, retain their existing behavior.
Humanoid binding derives a palm frame from available mapped fingers, including Synty's reduced finger rig.
Contact solving limits extension and the forward reach cone. It separates bounded forearm rotation from bounded wrist correction.
The rig blends finger joints toward the captured open-hand pose and restores them with the sampled animation after release.
Contact success requires both position and orientation agreement. The review panel counts observed palm contact instead of wrist proximity.
These limits do not implement full anatomical joint constraints, body collision avoidance, or automatic repositioning toward unreachable targets.
The open-hand contact pose is a panel press. Object-specific grasps and finger surface constraints remain future work.

The review walk retains the Kevin Iglesias eight-way leg curves. Derived clips replace their guarded arms with ExplosiveLLC relaxed walking arms.
Editor authoring measures arm timing from both source foot cycles. The runtime plays the resulting clips without an extra blending layer.
Eight integration cases verify preserved lower-body curves and sampled foot motion, plus changed arm rotations through Playables.

The shared surface motor preserves takeoff horizontal momentum. Pressing or releasing Shift cannot change airborne speed.
Releasing movement retains momentum. A standing jump cannot gain horizontal speed from later movement input.
Airborne steering changes direction at a bounded rate while preserving speed. Collision still clips movement.
Coyote time permits a jump within 0.1 seconds after leaving support. A 0.12-second input buffer permits a jump pressed before landing.
Each press launches at most once. Reset and swimming clear the timing state; swimming retains its ascent and speed controls.

Validation: EditMode job `789ade6df0e54c1ebf4f65b281a34769` passed 93 focused tests with no failures or skips.
Live review measured 1.558586 m/s after both airborne Shift input and released movement input, matching takeoff speed.
Live palm error was 0.002543 m. The timed panel interaction registered one contact and completed its 60-degree opening.
No compilation errors or runtime exceptions appeared in the filtered console checks.
Captures: `local-only/actor-performance/2026-09-09/relaxed-walk/before-after.jpg` and `hand-contact/palm-side.png` under the same date folder.
Visual acceptance remains pending. Planet player integration remains deferred until review acceptance.

## Knob grasp and crawl transitions — 2026-09-09

Sphere contact targets now include a grip radius. The hand approaches beside the knob instead of placing its palm at the knob centre.
A shared finger solver fits articulated chains around the sphere with bounded joint rotation and segment collision checks.
Humanoid hand width supplies a conservative finger/glove clearance. The review holds a stable hand approach frame while its round knob turns with the panel.
Contact success uses the observed grasp frame, orientation error, and finger penetration. Legacy flat palm and wrist targets remain supported.
This sphere grasp uses estimated fingertip lengths. It does not yet solve arbitrary object surfaces or detailed thumb opposition.

The review uses matching crawl entry, exit, idle, forward, and backward clips. Entry and exit use the existing performance playback and crossfades.
The previous posture stays visible until the transition owns the pose. This prevents the prone loop from entering the early standing fade.
Movement pauses during posture changes. The collision system still decides whether the actor can stand; the camera follows the animated head height.
Backward motion selects the backward crawl clip with shared cycle timing.

Live captures exposed invalid per-clip avatar creation and a seven-centimetre horizontal endpoint mismatch.
The imports now share the source reference avatar and retain the transition displacement within the pose. The actor root remains fixed during animation evaluation.
The endpoint regression initially failed with `Enter endpoint: Head`, expected less than `0.0399999991f`, actual `0.0706004798f`.
After correcting the imports, EditMode job `e014ce02820d42d0ae40b376d8046337` passed all 81 focused tests, with no failures or skips.
The suite includes actual crawl endpoints, interrupted transitions, backward selection, grip articulation, joint limits, and existing movement/traversal behavior.

The live panel registered one contact, opened 60 degrees, and lost no contact frames during its full-weight hold.
Maximum observed grasp-frame error was below one micrometre in that sequence. This measures the frame, not every glove surface point.
At the closed knob, the nearest tested glove triangle was 0.041925 m from the sphere centre, outside its 0.04 m radius.
A one-second backward crawl moved 0.5564 m and reached 0.9947 backward-clip weight.
Captures: `local-only/actor-performance/2026-09-09/knob-and-crawl/grip-final.png` and `crawl-final.jpg` in that folder.
Visual acceptance remains pending. Unity remains in the isolated review scene for Bryan's next pass.

### Lateral crawl correction — 2026-09-09

The review now assigns dedicated generated left/right crawl clips. The installed libraries had no matching lateral crawl pair.
The shared HumanoidAnimationView distributes crawl weight across forward, backward, left, and right clips using its direction weights.
Pure lateral movement removes the forward crawl contribution. Direction changes retain crossfades and synchronized cycle timing.
HumanoidCrawlStrafeAuthor bakes bounded lateral endpoint and bend paths with the existing LimbPoseSolver.
The author retains body/root curves and hand release height. It reduces residual fore/aft motion to one quarter.

Validation: 26 targeted EditMode tests passed (job 42d91ae812964be4802aaec9493cd53e).
Tests cover lateral selection, normalized blending, loop closure, retained body curves, sideways hand travel, and hand side separation.
Live left/right samples are in local-only/actor-performance/2026-09-09/crawl-sideways/live-final.jpg.
The generated motions still require Bryan's visual review. They do not provide contact locking on arbitrary terrain.

### Prone contact and lateral retargeting follow-up — 2026-09-09

Bryan rejected the first lateral crawl bake: arms penetrated the ground and legs retained forward motion.
The earlier hand-only excursion test did not cover the foot goal curves used by Humanoid retargeting.
Live prone idle also placed the wrist and elbow below terrain before lateral motion began.

LimbGroundingSolver now supports low body poses with shared LimbPoseSolver instances and IGroundingProvider queries.
It raises the presentation body, projects limb endpoints above the surface, and preserves bone lengths and the actor root.
The view fades this support with crawling and low portions of crawl transitions. Standing and airborne motion retain their existing paths.
This is surface clearance and limb support, not a full contact scheduler or arbitrary mesh collision solver.

Final correction evidence: 27 targeted EditMode tests passed, job 13999c8bec354823a3c46d9b5a136204.
Tests cover evaluated foot lateral travel, inward boot clearance, sloped-ground limb clearance, and unchanged sampled bone lengths.
Fresh play-mode captures: local-only/actor-performance/2026-09-09/crawl-grounding/review-poses.jpg (32 sampled frames across both directions).
The minimum sampled joint clearance above the review terrain was 0.05244 m. This measures joints, not every mesh vertex.
Core build passed without warnings. Planet build passed with 21 existing analyzer-version, obsolete API, unreachable-code, and serialization warnings.
No new Unity compile errors remained. Bryan's visual acceptance remains pending.

### Crawl contact timing and vault continuity — 2026-09-09

Crawl playback now advances from actual travel distance, using measured directional cycle distances stored on the review host.
HumanoidCrawlStrafeAuthor measures backward foot travel over 128 samples per clip. The scene builder recalculates these values when selecting clips.
LimbGroundingSolver reuses FootPlacementSolver for hand and foot stance anchors. Supporting strokes plant; recovery strokes release.
The shared foot solver has an opt-in settled-contact lock. It retains rotation damping during contact acquisition and release.
Prone ground clearance runs after contact solves to prevent the new knee penetration found by the slope regression.

The vault uses overlapping rise, crossing, and landing curves with continuous velocity and acceleration.
Both preflight and runtime sweep the body along the path. The right hand receives a blended contact target during the supporting phase.
No new traversal animation assets were imported in this pass.

Validation: 45 targeted EditMode tests passed, job e7fd78b383db402d953dbade1417d2b1.
The settled-contact unit test holds the endpoint within 1 mm while the body translates.
A live sideways crawl measured 59 consecutive settled-contact intervals: tangent foot speed 0.00001135 m/s versus 0.47463 m/s for the uncorrected sampled pose.
This metric excludes acquisition and release intervals; it does not claim zero slip during every transition.
The review vault started and completed with no rejection using the overlapping path.
Captures: local-only/actor-performance/2026-09-09/crawl-lock-vault/review.jpg and vault-final.jpg.
Core and Planet builds passed. Planet retained 21 existing warnings. Unity reported no compile errors.
Visual acceptance remains with Bryan. Ultimate Traversal candidates remain available for a later source-clip comparison.

### Clip-derived basic vault — 2026-09-09

Bryan rejected the geometric vault again. A standing collision capsule lifted the actor origin over the wall.
Lowest-foot normalization then removed the clip's tuck. The supporting hand could not stay on the obstacle.

The review now selects Universal Traversal's basic 91 cm fence vault with left-hand support.
HumanoidVaultMotionAuthor bakes horizontal hip travel, a torso/head collision capsule, and contact-fitting weights into ActorTraversalMotionAsset.
ActorTraversalMotion validates and copies this data once. Runtime sampling allocates no frame arrays.
ActorTraversal fits the reference contact to the detected edge and validates the landing and the full sampled path.
The collision system subdivides conservative capsule sweeps when deformation expansion alone reports a possible overlap.
Every accepted subdivision remains swept. No obstacle or supporting collider is excluded.

The view samples the clip at the authority phase and removes its baked horizontal motion exactly once.
The clip retains its authored vertical body motion and leg tuck. The shared interaction solver plants the left wrist during support.
Outgoing blends retain the last accepted phase. A blocked sweep now restores the previous phase before cancellation.
The existing unprofiled traversal path remains available to current actors. Step-ups, ledge grabs, and pull-ups keep their existing motions.
The scene builder retains the selected vault profile when rebuilding the review scene.

Fresh live captures are in local-only/actor-performance/2026-09-09/vault-profile/review.jpg (48 captured frames).
The supporting wrist's maximum target error was 0.00000054 m across 16 full-contact samples at phase 0.25–0.40.
This measures the wrist target, not palm surface coverage or finger contact.
The capsule checks torso/head clearance; they do not prove full limb or clothing clearance.
This profile is limited to its authored obstacle and landing fit range. It is not a general traversal motion search system.
Visual acceptance remains with Bryan.

Validation: 52 targeted EditMode tests passed, job 3daec489cd1244469462050fd927a474.
Coverage includes baked vault completion, standing landing clearance, overhead obstruction, cancellation phase retention, late obstruction, and single motion application through blending.
Existing ledge, crawl, and foot continuity fixtures also passed. Core build passed with zero warnings.
Planet build passed with 21 existing warnings and zero errors. Unity reported no C# compile errors.

### Early vault input grace — 2026-09-09

An early jump press previously missed the 0.9 m wall query and immediately launched a normal jump.
ActorTraversal.ResolveJump now checks for a valid vault at a short predicted approach position.
The input can wait up to 0.25 seconds, with prediction capped at 0.65 m.
The existing motor continues to move from player input. The traversal begins only from the actual current pose within reach.
Stopping, turning away, changing stance, leaving ground, or changing the target obstacle cancels the pending approach.
Open-space jumps remain immediate. Expiry returns the buffered jump to the motor instead of waiting indefinitely.
The collision path and landing must pass the existing traversal validation before input can wait.

Live reproduction: pressing at z=3.70 toward the review barrier at z=4.80 previously set Jumping=true immediately.
The same input now walked to z=3.92 and began the vault after seven 20 ms ticks. Jumping remained false.
Maximum position displacement across those approach/start samples was 0.03118 m per tick; no authority relocation occurred.
The shared traversal tests cover walking/running approach, unchanged start position, stopping/reversing, open-space jumps, and expiry.

### Subdued basic walk — 2026-09-09

Bryan requested a less pronounced shoulder and torso motion in the basic walk.
The existing derived walks retained the female source torso while using relaxed arms from another library.
The new upper-body source is Kevin Iglesias HumanM@Walk01_Forward, imported with its male Humanoid reference model.
The author copies its torso and arms into all eight established directional clips, retaining root, leg, foot-goal, and timing data.
Torso and shoulder muscle oscillations are baked at 40% of the donor range around their cycle means.
This adds no runtime pose layer or per-frame damping work.

A 32-sample retargeted comparison measured chest rotation range falling from 31.85 degrees to 8.85 degrees.
The raw replacement donor measured 22.12 degrees at the chest and 20.97 degrees at each shoulder before attenuation.
The baked shoulders measured 8.39 degrees locally. The original derived shoulders measured about 5.3 degrees locally.
These local joint measurements distinguish chest sway from shoulder articulation; they do not substitute for visual review.
Before/after samples are in local-only/actor-performance/2026-09-09/neutral-walk/before-after.jpg.
Source and bake details are recorded in Assets/Art/Characters/SyntyHero/SOURCE.md.

Validation: 65 targeted EditMode tests passed, job e64e2bab5d2a4059a9bfc0a301c7b260.
This includes traversal grace, existing ledge actions, humanoid animation, foot continuity, and all eight directional walk preservation checks.
An initial author test crossed the test/editor assembly boundary (CS0103). It was replaced with baked-asset validation; Unity then compiled cleanly.
Core and Planet code-health builds passed; Planet retained 21 existing warnings. Bryan's visual review remains pending.

### Stair ascent, descent, and supporting feet — 2026-09-09

The flat-ground walk could not communicate stepping onto each tread. Height-based contact weights also treated a raised supporting foot as airborne.
The review now blends dedicated EverydayMotionPack stair ascent and descent cycles into forward walking.
Ground probes distinguish discrete level treads from flat ground and smooth slopes. A short hold bridges the centers of wide treads.
The clips share locomotion phase and existing graph cross-fades. Backward and sideways movement retain existing directional clips.
Stair selection is limited to the authored walking case; this does not implement arbitrary stair stride or full-foot geometry fitting.

Pre-IK foot travel distinguishes stance from swing while the stair clips contribute to the pose.
The shared foot solver locks settled stair contacts, uses planted support for body lowering, and prevents rotation damping from retaining reachable tread penetration.
The actor motor follows capsule-supported down-steps within its existing step-height limit. Larger drops and intentional jumps remain airborne.
The presentation body smooths both upward and downward authority steps. This changes no collision-authority pose through animation.

Live captures: local-only/actor-performance/2026-09-09/stair-gaits/review.jpg, with 28 ascent and 28 descent frames plus samples.txt.
The captures show a raised swing knee on ascent and a lower bent-knee posture on descent. Bryan's visual acceptance remains pending.
Validation: 96 targeted EditMode tests passed, job 519656b2387942fa8a3cfe7225d26735.
Coverage includes stair selection versus slopes, normalized blending, unchanged authority, tread clearance, .2m supported descent, .6m physical falls, and intentional jump release.
Existing traversal, ledge, humanoid animation, and procedural pose fixtures also passed.
Core build passed with zero warnings. Planet build passed with 21 existing warnings. Unity reported no C# compile errors.
