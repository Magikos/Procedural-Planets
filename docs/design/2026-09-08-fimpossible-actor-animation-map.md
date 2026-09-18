# Fimpossible references and the shared actor animation system

Date: 2026-09-08. The source review informs the shared actor animation implementation. No vendor plugins were imported.

## Available references

The inspected source root is `D:/Unity/Explore Assets/Assets/FImpossible Creations/Plugins - Animating/`.
Full source directories exist for Legs Animator, Look Animator, Spine Animator, Tail Animator, and Ragdoll Animator 2.
These local files provide reference material from the user's owned assets.

Searches covered `D:/Unity/Explore Assets`, the local Unity Asset Store cache, and Unity packages under `D:/Unity`.
The initial searches did not find Bones Stimulator, Leaning Animator, Animation Designer, or Ground Fitter.
Bryan installed these tools in the scratch project on 2026-09-08. The follow-up review verified all four source directories.
Animation Designer lives under `Editor/Plugins - Editor - Animating/Animation Designer`, relative to the FImpossible Creations root.
Eyes Animator, Optimizers, and Jiggling were priced offers in the supplied context.
No source from those three products informed this review.

## Capability map

Review every available tool for useful mechanisms. Keep one coordinated pose pipeline with optional features for each rig.
Look Animator informs gaze limits, head and spine distribution, and body compensation.
Animation Designer is also a reference candidate for editor-time adjustments to existing clips, including timing, poses, and contact alignment.
The follow-up source review verified time reversal and curve tangent/weight handling. Preserve original clips when authoring variants.
Clip authoring and runtime procedural corrections must share rig bindings and avoid applying the same correction twice.

Vendor paths below are relative to the source root above. Project paths are relative to this repository.

| Tool | Verified mechanism or availability | Existing counterpart | Useful next gap |
|---|---|---|---|
| Legs Animator | `Legs Animator/LegsAnimator.cs` separates animation and late raycast passes. `Core/Setup Settings/LegsA.Raycasting.cs` exposes cast styles and a custom NoRaycasting mode. | `Assets/Scripts/Game/Animation/FootPlacementSolver.cs`, `LimbPoseSolver.cs`, `ProceduralPoseRig.cs`; injected `IGroundingProvider`. | Improve contact transitions and body support using existing analytic terrain queries. Keep sensing independent of PhysX. |
| Look Animator | `Look Animator/FLookAnimator.cs` exposes backbone weights and large-angle compensation. | `ProceduralPoseRig.cs` and `ProceduralRigDefinition.Look`. | Shared gaze limits and coordinated head/body attention for animal and humanoid rigs. Respect threat-memory locations rather than hidden actor positions. |
| Spine Animator | `Spine Animator/FSpineAnimator.cs` exposes motion influence, motion space, and blend amount; applies a late pose pass. | `ProceduralPoseRig.cs` spine bending. | Motion-relative lag and recovery with explicit actor up; preserve animation restoration before additive changes. |
| Tail Animator | `Tail Animator/TailAnimator2.cs` supplies springiness and post-animation updates. `Code/TailAnimator.Extensions.Collision.cs` separates collision handling. | `BoneChainSpring.cs`, `SurfaceChainSolver.cs`, rig chains. | Shared collision sampling and LOD reset rules for tails, capes, ropes, and appendages. These chains do not replace whole-body support. |
| Ragdoll Animator 2 | Separate physical chains, configurable joints, animation capture, and physical-pose blending. Details below. | `CreatureAnimationView.cs`, `CreatureCarcassView.cs`, `CreatureCorpsePresentation.cs`. | Terrain-supported death settling and persistent final pose. Current carcasses use settled authored death clips. |
| Bones Stimulator | `Core/BonesStimulator.Logics.Muscles.cs` separates elastic motion, motion influence, gravity, and collision. | `BoneChainSpring.cs`, including optional analytic ground contact. | Reuse the existing spring pass; no additional muscle framework is needed for current rigs. |
| Leaning Animator | `Core/LeaningProcessor.MainLogics.cs` separates signed forward and sideways movement. | `ProceduralPoseRig.cs` acceleration lean. | Corrected scalar-speed lean to use tangent vector acceleration, including backward and sideways movement. |
| Animation Designer | `Designer Window/ADesignerWindow.Update.cs` reverses normalized sample time; `ADesignerWindow.Utils.cs` handles reversed tangents and weights. | `Assets/Editor/ActorClipVariantAuthor.cs`. | Added continuous Generic clip reversal with weighted curves and reordered event times. Stepped and object-reference curves are rejected for reversal. |
| Ground Fitter | `Scripts/Base/FGroundFitter_Base.cs` averages zone samples, blends an ahead sample, and clamps pitch/roll. | `SurfaceCharacterController.cs`, `IGroundingProvider`, `ActorDeathPose.cs`, `CreaturePoseGrounding.cs`. | Existing death support already averages bounded samples and limits tilt. Keep the shared analytic provider; avoid a second root-position writer or per-tick zone-list allocation. |

## Ragdoll Animator 2 findings

`Ragdoll Animator 2/Core/RagdollChainBone.cs` stores a `ConfigurableJoint` and creates joints in `RefreshJoint`.
It changes angular limits and motion locks, controls rigidbody gravity, and exposes sleep/wake behavior.
This is a PhysX body-and-joint system. It cannot collide with an analytic terrain surface without a contact representation.

`Core/Ragdoll Handler Partials/RagdollHandler.Update.cs` separates `FixedUpdateTick` and `LateUpdateTick`.
The late pass captures animator transforms, then applies physical rotations and optional physical positions with a blend.
This separation is useful: animation supplies the target pose; settling supplies a constrained result; one final pass writes the skeleton.

`Core/Ragdoll Handler Partials/RagdollHandler.GenerateDummy.cs` and `RagdollHandler.DummyStructure.cs` separate the physical structure from the visible rig.
`Core/Ragdoll User Utilities/RagdollHandlerUtils.Impacts.cs` isolates impact operations.
`Core/Helper Classes/RagdollAnimator2Enums.cs` and `RagdollHandler.cs` distinguish standing, falling, and sleep modes.
Vendor sleep naming does not establish a persistent, zero-simulation carcass format for this project.

The transferable design is the pose ownership and state boundary. Copying its complete physical implementation would conflict with this planet's terrain model.

## Proposed shared death path

Use one path for animals, monsters, NPCs, and the player where their rig supports it:

1. Animation remains authoritative for the visual pose during normal movement.
2. An authority-approved death or knockdown captures the current pose and impact facts.
3. A bounded settling pass constrains body support and limb lengths against analytic ground samples.
4. The pass freezes when motion stays below a threshold, or when its time budget expires.
5. Carcass presentation consumes that frozen pose before it builds exposed bones and removes flesh.

A corpse must not recapture an upright animation after freezing. Terrain edits need an explicit policy for resettling existing bodies.
Keep authored death clips as fallback for unsupported rigs and unloaded actors.
Do not add active-ragdoll walking or getting up in the first change.

The smallest terrain-compatible experiment is analytic settling for the existing deer and wolf rigs.
Reuse `IGroundingProvider`, `IGravityProvider`, existing limb constraints, and rig bindings.
Query support at torso and limb points in the local gravity frame. Preserve joint distances and prevent ground penetration.
An analytic projection solver is a new implementation, not a claim that the vendor already provides this backend.

A native rigidbody alternative requires terrain collision proxies and per-body gravity.
That option introduces proxy lifetime, observer-range coverage, and contact consistency costs.
Choose it only if later requirements need physical pushing, piles, dragging, or detailed object collisions.
Do not add terrain colliders solely to make vendor assumptions fit.

## Authority, persistence, and performance

The shared decision/FSM layer controls death, knockdown, recovery permission, health, and damage. Animation callbacks do not decide those outcomes.
Server or host authority must own gameplay-relevant body placement. Client animation interpolation must not change loot position or hit results.
Do not assume PhysX or a floating-point constraint solver produces identical results on every peer.

`Assets/Scripts/Planet/Creatures/CreatureCorpse.cs` currently persists root pose, species, source identity, time, loot, and meat fraction.
It does not persist per-bone settled transforms. A new physical pose must survive unload/reload and match across observers.
Store a versioned rig identifier plus final local bone pose, or an explicitly bounded compact pose representation.
Old saves must continue using the existing death-clip fallback. Preserve `SourceActorId` and `AppearanceIdentity`.

Only nearby, newly falling bodies need settling work. Freeze settled bodies and stop all solver updates.
Keep a shared per-tick work budget; do not allow simultaneous deaths to create unbounded physics or terrain queries.
Avoid creating joint GameObjects for distant carcasses. Reuse the current combined skeletal mesh and cached material property block.
Burst is appropriate only after numerical state is separated from Transform access. Do not send Unity Transform writes to worker threads.

## Bounded implementation gate

The next change should prove death settling on deer and wolf across flat, sloped, uneven, and sideways-gravity terrain.
Required checks include limb-length preservation, no sustained penetration, bounded settling time, stable frozen pose, and preserved flesh/bone alignment.
Reload and observer unload/reload must restore the same final pose and appearance identity.
Existing meat stock, decay, loot, and death authority must remain unchanged.
Measure worst-case settling cost for several simultaneous deaths before expanding to more rigs.

This review does not authorize vendor installation or replacement of the shared FSM, locomotion controller, or Playables graph.
