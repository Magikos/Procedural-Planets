# Climbing and balance traversal

## Active Tracker

Status: Design and written test queue prepared. New traversal behavior is not implemented.

Current next action: Coordinate the Unity handoff, recheck the working tree, and implement the first lateral ledge slice.

- [ ] Lateral ledge movement and contact transfer validated.
- [ ] Upward and downward climbing validated.
- [ ] Corners and reactive contact recovery validated.
- [ ] Narrow beam and tightrope traversal validated.
- [ ] Bryan accepts the review-scene movement before Planet integration.

**Tree:** `harvest-vertical-slice`, `d1e0f62`, with substantial uncommitted actor work, inspected 2026-09-09.
Another agent owns Unity. This pass changes documentation only. No imports, builds, tests, or captures ran.

This extends the [actor performance design](2026-09-09-actor-performance-animation.md).
The [validation queue](../../plans/2026-09-09-climbing-and-balance-validation-queue.md) records the implementation order and evidence requirements.

## Intended behavior

The actor can shift left, right, up, and down between reachable contacts.
The actor finds contacts from nearby geometry and supported climb surfaces.
It does not require a complete route before starting.
A jump, wall collision, or falling state can expose a new catch opportunity.

Climbing must show weight transfer. A limb reaches, establishes contact, and supports the next body movement.
The actor stops when it cannot find a safe next contact.
Input must never move the actor directly to a distant grip.

Low proficiency uses cautious techniques and longer transfers.
Higher proficiency can select faster techniques within the same reach and collision limits.
The first implementation uses basic techniques. Acrobatics remain separate choices.

## Existing code and extension points

| Existing owner | Current responsibility | Extension |
|---|---|---|
| `ActorTraversal` | Step, vault, catch, hang, climb-up, top descent | Contact transfers and traversal phases |
| `ActorCollision` | Support queries and swept body clearance | Candidate grip and transfer clearance checks |
| `SurfaceCharacterController` | Movement authority and grounding | Consume traversal motion without a second motor |
| `ActorAnimationPerformanceLibrary` | Action, rig, condition, proficiency selection | Climbing and balance performances |
| `ActorPerformancePlayback` | Performance phase playback | Reach, transfer, release, and settle playback |
| `ProceduralPoseRig` | Procedural presentation | Coordinate contacts with the selected clip |
| `InteractionLimbSolver` / `InteractionPoseTarget` | Limb reach and contact presentation | Separate hand targets with surface orientation |
| `FootPlacementSolver` | Foot placement and stance support | Validated climbing foot contacts and narrow support |

`ActorTraversal` currently stores one `Edge` and one support collider.
Its hanging tick returns a fixed pose. That cannot represent independent grip transfers.
The existing support movement checks cancel traversal. Moving-support behavior needs an explicit extension and tests.

Extend these owners instead of adding a parallel climbing controller or animation graph.
The performance library currently selects clips and phases. It does not yet provide the full contact authority described here.
Keep authoritative contact state outside presentation-only `InteractionPoseTarget` values.

## Contact state and phase rules

Each active contact needs an effector name, support identity, local point, local normal, and acquisition state.
Track the support revision so geometry changes can invalidate a grip.
Resolve support-local anchors each simulation tick. Persist stable world identities, not Unity instance IDs.

Contact requirements belong to the technique and anatomy.
A two-hand ledge hang can have no foot support.
A cautious rock-climbing transfer can require three supporting limbs.
Do not apply a universal three-contact rule to every action.

Use this sequence for the first transfer:

1. Hold the existing contacts and choose a reachable candidate.
2. Reach with the available limb while preserving the supporting contacts.
3. Validate the new contact against current geometry.
4. Transfer body weight along a swept path.
5. Release and move the trailing limb when the technique permits it.
6. Settle into the new hold and accept the next transfer.

The simulation owns phase completion and failure.
The animation supplies pose, phase timing, and intended contacts.
An animation event or completed IK solve alone cannot establish gameplay success.
Crossfades must preserve loaded contacts until the next technique explicitly releases them.

If a candidate fails during reach, return to the valid hold when possible.
If support disappears, attempt a bounded recovery catch or enter falling.
Never retain a stale world anchor or teleport to a replacement grip.
Death, ragdoll, swimming, and actor reset release traversal state through the existing lifecycle.

## Finding the next contact

Search in the requested direction using the local wall frame and actor gravity.
Filter candidates by grip geometry, surface permission, reach, limb limits, occupancy, and body clearance.
Use bounded candidate counts and deterministic ranking.
Prefer the closest suitable contact in the requested direction, with stable tie-breaking.

Natural ledge geometry can supply grips. Flat walls require a supported climb surface or explicit holds.
Do not invent handholds on an ordinary blank wall.
An authored hold can supply an approach normal and usable grip area.
Procedural geometry can supply equivalent validated information later through the same contact data.

Check the body path and limb reach throughout transfer, including the supporting collider.
An endpoint-only clearance test cannot prevent clipping through a corner.
Inside and outside corners need a continuous wall frame and bounded body rotation.
Stop at a missing corner contact. A gap jump requires explicit jump intent.

Revalidate active supports every tick. Search new candidates on intent changes, action boundaries, or relevant support changes.
Cache immutable clip/contact analysis and validated candidate data with support revisions.
Do not cache a solved world pose across actors, wounds, or changing supports.
Presentation update budgets must not skip authoritative contact validation.

## Input behavior

Shared actor code consumes `ActorIntent`, not keyboard state.
The review host maps the existing controls to these contextual actions.

| Input while attached | Action |
|---|---|
| Left / right | Shift along the ledge or wall |
| Up / down | Reach a higher or lower valid climbing contact |
| Space at a valid top | Pull up or mantle using the existing action |
| Explicit jump across a gap | Release into a validated jump or reject the action |
| Ctrl | Release the hold using the existing drop behavior |
| Ctrl+Space from the top | Enter the existing top-to-hang descent |
| Right mouse look | Move the camera without rotating the loaded body |

Climbing direction follows the stable wall frame.
Free look must not reverse left/right when the camera crosses behind the actor.
Downward movement acquires a lower contact before release. Without a lower contact, the actor holds position.
Preserve jump buffering, coyote time, and airborne speed rules outside attached traversal.

## Skills, equipment, and anatomy

Select and latch a technique at action entry using existing performance selection.
Do not change techniques halfway through a loaded transfer because proficiency or input changes.
Resolve any gameplay skill outcome once per attempt with reproducible attempt state.
Do not roll random failure each frame.

An occupied hand changes eligibility.
Gameplay can choose a supported one-hand technique, stow an item, drop an item, or reject the transfer.
Animation must not discard inventory to make a pose work.
A wounded limb can reduce reach, load capacity, or available techniques before clip selection.

Effectors describe capability rather than assuming every contact is a human hand.
A companion can carry an object with its mouth when its anatomy supports that interaction.
That does not make a dog eligible for human rock-climbing techniques.
Keep balance, reach, and contact limits specific to the rig and technique.

## Tightrope and narrow support

Start with a rigid narrow beam and a stable traversal path.
Support width and reachable entry determine whether the actor can enter.
The actor approaches through normal movement and establishes support before balancing.
Do not snap the root to the path centerline.

Support geometry controls progress, foot placement, and permitted lateral correction.
Provide forward movement, backward movement, stopping, recovery, and a fall when balance cannot recover.
Use body lean and arm motion to show balance error.
Turning and passing another actor depend on actual available clearance.
A manual balance minigame is optional tuning, not required for the first slice.

A flexible tightrope is a later slice of the requested feature, not completed by the rigid beam.
Its sag, motion, attachments, contacts, and collision must use the same authoritative rope shape.
Animating a sagging rope beneath fixed feet would recreate the current contact problem.
Measure moving-support behavior before choosing a rope simulation dependency.

## Traversal scope and order

| Slice | Work | Completion boundary |
|---|---|---|
| 1 | Ledge left/right, hold, stop, ledge ends | Independent hands; no body snap or sliding contacts |
| 2 | Up/down holds and ledge-to-wall transitions | Real four-direction climbing with valid support |
| 3 | Inside/outside corners and reactive recovery | Swept transfers; catches remain available after unplanned falls |
| 4 | Rigid beam balance and entry/exit | Forward/backward balance with real support and failure |
| 5 | Flexible tightrope | Shared rope geometry for physics and presentation |
| Follow-up | Ladders and narrow wall-side ledges | Reuse contact transfers with explicit entry/exit |
| Follow-up | Pole/rope climb, monkey bars, zipline, swing | Add only the missing support mechanics and suitable techniques |
| Later skills | Wall run, larger gap leaps, acrobatic vaults | Explicit skill eligibility and collision-safe motion |

Existing walk, strafe, stairs, crouch, crawl, swim/dive, step-up, vault, jump, catch, pull-up, and drop remain regression scope.
The table is a traversal backlog, not a claim that every movement type already exists.
Review each slice in `HumanoidAnimationReview` before expanding its scope.
Bryan's visual review remains separate from automated checks.

## Verified local animation candidates

Read-only filename and importer inspection completed on 2026-09-09.
Root: `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/`.

| Purpose | Exact filenames relative to root |
|---|---|
| Lateral ledge | `Traversal_Ledge_Climb_Left.fbx`, `Traversal_Ledge_Climb_Right.fbx` |
| Corners | `Traversal_Ledge_Climb_InwardCorner_Left.fbx`, `Traversal_Ledge_Climb_OutwardCorner_Right.fbx` |
| Ascent | `Traversal_Wall_Climb_Up_LeftHand.fbx`, `Traversal_Wall_Climb_Up_RightHand.fbx` |
| Descent | `Traversal_Wall_Climb_Down_LeftHand.fbx`, `Traversal_Wall_Climb_Down_RightHand.fbx` |
| Diagonals | `Traversal_Wall_Climb_UpLeft.fbx`, `Traversal_Wall_Climb_DownRight.fbx` |
| Wall hold | `Traversal_Wall_Climb_4Limbs_Hold_Idle.fbx` |
| Hang-to-wall | `Traversal_Ledge_Climb_HangingIdle_To_4Limbs_Hold_Idle.fbx` |
| Balance | `Traversal_BalanceBeam_Forward_Loop.fbx`, `Traversal_BalanceBeam_Back_Loop.fbx`, `Traversal_BalanceBeam_Idle.fbx` |
| Ladder | `Traversal_Ladder_Climb_Up_Loop.fbx`, `Traversal_Ladder_Climb_Down_Loop.fbx` |
| Pole | `Traversal_Pole_Climb_Up.fbx`, `Traversal_Pole_Climb_Down.fbx`, `Traversal_Pole_Climb_Idle.fbx` |
| Narrow ledge | `Traversal_LedgeWalk_Left_Loop_Crouched.fbx`, `Traversal_LedgeWalk_Right_Loop_Crouched.fbx` |

Checked importers use Humanoid and reference `T_pose.FBX`, GUID `2091e1aca366db648b573fbcebc047c6`.
Checked imports enable looping, including a corner action. Review one-shot settings before import.
Metadata cannot establish contact quality, root displacement, or suitability for irregular rock holds.
Sample retargeted poses in Unity before selecting clips or deriving contact windows.

The filename search found no dedicated tightrope, squeeze, or rope-climbing clip.
Balance and pole clips are candidates, not verified substitutes.
Do not import whole packages. Reuse the existing selective import and source-provenance workflow after handoff.
