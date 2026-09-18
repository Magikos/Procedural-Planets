# Contact solver references: Final IK and Moveen

Date: 2026-09-09. Inspected the dirty working tree on `harvest-vertical-slice`, based on `d1e0f62`.
Scope: read-only source research. No vendor runtime was imported. No Unity benchmark or visual test was performed for these findings.

Final IK offers the strongest immediate references for coordinated contacts. Moveen offers useful foot target planning and support scheduling.
Keep the project-owned runtime and shared motor. These findings identify candidate mechanisms, not implemented features.

## Verified source roots

- Final IK: `D:/Unity/Explore Assets/Assets/Plugins/RootMotion/FinalIK/`
- Moveen: `D:/Unity/Explore Assets/Assets/Plugins/YK/MoveEn/Assets/Scripts/`

Vendor paths below are relative to these roots. Project paths are relative to the repository root.

## Findings ranked by expected benefit

| Priority | Mechanism | Verified source | Project integration |
|---|---|---|---|
| 1 | Capture the animated bend plane and endpoint rotation before parent corrections | Final IK `IK Solvers/IKSolverLimb.cs`, `MaintainBend`, `MaintainRotation` | Capture reference data in `Assets/Scripts/Game/Animation/ProceduralPoseRig.cs`; consume it in `LimbPoseSolver.cs` |
| 2 | Read a numerical pose, solve connected constraints, then write transforms | Final IK `IK Solvers/IKSolverFullBody.cs`, `ReadPose`, `Solve`, `WritePose`; `FBIKChain.cs` | Coordinate pelvis, shoulder, hand, and foot corrections within the shared pose pass |
| 3 | Preserve paused contact relative to the moving target | Final IK `InteractionSystem/InteractionEffector.cs`, `Pause`, `Update` | Extend `InteractionPoseTarget.cs` and shared interaction ownership for moving handles and objects |
| 4 | Preserve previous targets and weights during interruption | Final IK `InteractionEffector.cs`, `StorePrevious`, `Switch` | Prevent target snapping when one reach interrupts another |
| 5 | Separate conservative reachable foot targets from predicted future targets | Moveen `Step2.cs`, `calcAbs` | Extend `FootPlacementSolver.cs` swing planning for stairs and body motion |
| 6 | Coordinate leg release using other legs' progress and support | Moveen `Stepper5.cs`, per-step `affectedByProgress` and `affectedByDeviation` evaluation | Extend the existing idle replant arbitration for quadrupeds and injury support |
| 7 | Delay release to express weight | Moveen `Step2.cs`, `undockPause` | Author support timing with the selected performance instead of adding random pose noise |
| 8 | Sample heel, toe, and side support with adjustable query quality | Final IK `Grounder/GroundingLeg.cs`; `Grounding.cs` | Improve edge orientation through existing grounding providers |

`FBIKChain` exposes pin, pull, push, parent influence, and bend constraints. These provide a reference for body participation during reaching.
Our `LimbPoseSolver` already provides analytic two-bone solving. Replacing that mathematics alone would add less capability than coordinating body support.

`InteractionTarget.cs` supports rotational freedom around a pivot and target-specific curve multipliers.
These mechanisms could support natural grasp orientation around symmetric objects.
The interaction API uses `FullBodyBipedEffector`; our actor API must keep named effectors for hands, mouths, claws, and other anatomy.

## Constraints that must not transfer

- Moveen `Step2.calcAbs` uses `Vector3.up` and `.withSetY`. Convert geometric reasoning to the supplied gravity frame.
- Moveen `Stepper5` uses world-Y support and gravity calculations despite exposing an `up` field.
- Moveen `SurfaceDetector1` uses PhysX and returns world-Y-zero support on a miss. Preserve explicit grounding failure in our provider.
- Moveen owns body acceleration and motor behavior. Do not add a second root-motion authority beside `SurfaceCharacterController`.
- Final IK interaction events can trigger pickup directly. Gameplay must retain attachment, inventory, and outcome authority.
- Final IK's interaction layer assumes a biped. Its lower-level solver structure is more general than that interaction API.

A mouth contact can overlap gaze and spine bones. Rig ownership must resolve that conflict explicitly.
Contact can reduce gaze influence while unrelated tail motion continues. Increasing every IK weight does not solve competing bone writes.

## Performance implications

These are source observations, not measured performance results.

- Final IK full-body solving defaults to four iterations. Each solve includes chain constraints, effector updates, and pose mapping.
- Keep independent analytic limb solves when connected body solving is unnecessary.
- Our longer-chain CCD already caps work at 24 iterations. Any connected solver needs explicit iteration and convergence limits.
- Final IK ground quality selects one ray, three rays, or a ray plus capsule cast per foot. Additional support detail increases query work.
- Moveen evaluates dependency lists for each step. Dense leg relationships increase scheduling work.
- Keep stepping history collection in diagnostics. Do not require it for normal actor updates.
- Cache rig bindings and reusable numeric buffers. Dynamic contact positions must still follow their objects and current terrain.

No source observation establishes that either vendor implementation is faster than our current solver.

## Candidate next steps

1. Capture stable limb reference data before procedural body changes.
2. Add target-local contact persistence, effector socket offsets, and explicit ownership during interruptions.
3. Add conservative and predicted foot targets with support-aware release.
4. Prove one human hand and one dog mouth can retrieve the same supported object through different rig bindings.

Test moving targets, unreachable targets, interruption before and after attachment, and gaze changes during mouth contact.
Retain existing quadruped feet, swimming secondary motion, bird grounding, death settling, reduced evaluation, and sideways gravity.

Related design: [shared actor performance animation](../design/2026-09-09-actor-performance-animation.md).
Related preservation map: [FImpossible references](../design/2026-09-08-fimpossible-actor-animation-map.md).
