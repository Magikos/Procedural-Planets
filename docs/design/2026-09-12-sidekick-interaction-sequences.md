# Sidekick interaction sequences

The [chest polish candidate](2026-09-12-chest-animation-polish.md) records the subsequent two-hand grip, inspection pose, approach alignment, and release changes.

Status: Implemented in the dirty `harvest-vertical-slice` working tree. Visual approval remains with Bryan.
Current next action: Review the interaction motion in `Assets/Scenes/Tests/SidekickInteractionReview.unity`.

## Scope

Interactions use authored animation phases and shared IK contact solving.
The scene provides chest use, door use, item inspection and placement, two-hand carrying, seating, and chair movement.
There is no inventory interface or collection transfer. Weapon racks remain future work.

## Reusable parts

| Part | Responsibility |
|---|---|
| `ActorInteractionDefinition` | Stores clips, phase duration, blend duration, hand weights, movement rules, and named markers. Multiple objects can reference one definition. |
| `ActorInteractionPlan` | Validates and copies authored data for a running sequence. |
| `ActorInteractionSession` | Advances phases and emits each phase marker once. Supports cancellation and phases that wait for input. |
| `ActorPerformancePlayback` | Plays phase clips through the existing graph. Retains outgoing clip instances during interrupted fades. |
| `HumanoidAnimationView` | Combines interaction playback with the shared procedural pose layer. |
| `SidekickInteractionReview.Target` | Supplies object contacts, movable parts, selection anchors, and definition references for this fixture. |
| `SidekickInteractionReview` | Resolves nearby targets and interprets fixture markers. Supplies pose values before the actor evaluates animation. |

The definition and session classes live under `Assets/Scripts/Game/Animation`.
They do not depend on the review scene or its prototype controller.
The scene adapter owns its temporary object state. It is not a persistent inventory or network authority implementation.

## Authoring another interaction

1. Create an asset through **Actors > Interaction Definition**.
2. Give the action and each phase a unique name.
3. Assign Humanoid clips and select their normalized start and end times.
4. Set phase duration and a positive blend duration.
5. Set right-hand and left-hand IK weights for each phase.
6. Add named gameplay markers at the required phase progress.
7. Enable `WaitForInput` for inspection, seating, or other hold phases.
8. Enable `AllowMovement` and `UseLocomotion` when locomotion should continue under hand IK.
9. Register the definition in the actor's `Interactions` array.
10. Assign the definition and object contacts to the target.

`MatchContactRotation` uses the contact transforms' authored rotations.
Without it, the fixture supplies its existing actor-relative grip orientation.
`SelectionAnchor` remains stationary when a lid or door moves. `Contact` follows the movable part.
The optional `LookAnchor` directs gaze independently of hand contact.

Custom consumers subscribe to `ActorInteractionSession.Marker` and interpret their own marker names.
The fixture also exposes `InteractionMarker` with the selected target.
Markers follow the interaction clock. IK success does not authorize gameplay effects.
Cancellation suppresses future markers. It does not undo an already emitted effect.
A future weapon rack can supply its own action selection and reuse this sequence and playback path.

## Room behavior

| Object | E behavior |
|---|---|
| Chest | Bend, grip, open, and look inside. E continues through closing and recovery. |
| Door | Reach for the handle, operate the door, and release. E after completion closes an open door. |
| Table items | Reach, lift, and inspect. E places the held item on a clear surface ahead. |
| Ground item | Kneel, lift, and inspect. E lowers and places the item. |
| Crate | Bend, grip with both hands, and lift. Walking remains available while carrying. E puts it down. |
| Chair front | Turn toward the seated pose, sit, and remain seated. E stands up. |
| Chair back | Grip the back and move the chair while walking. E releases it onto the floor. |

The tabletop objects share `TablePickup` and `TablePlace` assets.
The crate uses `TwoHandCarry` and `TwoHandPlace`.
Definitions live in `Assets/Art/Interactions/Definitions`.
The room retains the diagnostic Visit, reach-preview, prop-preview, and reset controls.
Visit is a shortcut. Normal selection still uses the nearest eligible object in front of the actor.

Escape cancels an action. A seated actor uses the stand sequence when cancellation can safely retain the chair reference.
Cancellation after pickup retains the held item. Cancellation during placement blends back toward carrying.
Placement rejects blocked space and missing or moved support. It restores the original collider and rigidbody state after settling.
Stopping the component also restores those states. Explicit room reset restores authored transforms immediately.

## Continuity and evidence

Interaction phase fades use `ActorPerformancePlayback` and `ActorAnimationGraph.BlendBaseWeights`.
Hand entry, release, and retargeting use the existing `InteractionPoseBlend` path.
Normal item motion starts at the displayed transform and retains damping velocity during reversal.
Normal hinge motion advances from its current rotation. No normal interaction reparents or resets an item transform.
Visit and Reset remain explicit diagnostic exceptions.

The initial door measurement exposed a 0.1876 m hand movement in one 60 Hz frame.
The phase used unconstrained reach solving. Contact solving reduced that measured maximum to 0.0761 m.
The crate entry now uses a longer authored blend because its source clip starts in a deep bend.
The door chooses an opening direction away from the actor. Four tests cover both approach sides and authored swing signs.

Final EditMode job `fe72354bc2c84857aa82b9f341228735` passed 283 tests, with zero failures or skips.
Coverage includes the previous continuity groups, performance playback, and the interaction fixture regressions.
Core, Planet, and Editor builds passed. Planet reported 21 warnings; Editor reported 47 warnings.
Warnings concern existing analyzer/compiler versions and existing legacy serialization fields.
The first Editor build reported `error NETSDK1004` for its missing NuGet assets file. A restored build passed.
The first new collider test failed with `Expected: True` / `But was: False` because EditMode did not invoke the disable callback.
The test now verifies the explicit reset path. The final suite passed.

Live sampling used the Sidekick Rider, 90 settling steps, and manual 60 Hz actor steps.
Six interaction types each received a normal run and a cancellation run.
Cancellation occurred after 25 frames, followed by another E attempt three frames later.
The observed bones were both hands, the head, and the hips.
The final sampled maxima were below 0.1 m per frame. This is a numerical bound, not a universal perceptual threshold.
The separate final door opening and closing check measured a maximum hand step of 0.099959 m.
It reached 90 degrees open and zero degrees closed.
Four consecutive door frames were inspected during grip. The corrected swing capture is `door-away.png`.

The crate remained held through 3.117 m of movement and then returned to the floor with colliders restored.
Deactivating a held item cleared the action and holding state. Its first hand-release step measured 0.018694 m.
An unsupported animation request returned false without cancelling the active supported animation.
The fresh live console contained zero errors.
Reopening the scene preserved its GUID, nine targets, 13 definitions, and shared tabletop references.

Evidence directory: `local-only/sidekick-interaction-2026-09-12/`.
Captures include `before.png`, `chest-hold.png`, `chair-front-seated.png`, `crate-held.png`, and `ground-inspect.png`.
Machine-readable test and runtime results are in `results.json` beside the captures.
The baseline scene copy is `SidekickInteractionReview.before.unity` in that directory.
The scene update adds definition references and anchors. It preserves existing object and asset identities.

The earlier validation record remains [animation continuity validation](2026-09-12-animation-continuity-validation.md).
The broader continuity audit remains unfinished. These interaction checks do not certify every animation path.
