# Motion phase and asset research — 2026-09-09

Scope: read-only source and asset inspection on the dirty working tree above `d1e0f62`. No Unity control, import, source edits, or visual acceptance occurred.

## 1. Highest payoff: replace elapsed jump sampling with physical phase selection

`Assets/Scripts/Game/Animation/HumanoidAnimationView.cs:167` tracks airborne elapsed time. Line 192 samples the jump at `Min(airTime, clipLength * .95f)`. This reaches the end pose quickly and then holds it. The current `Assets/Art/Characters/SyntyHero/Animations/Jump.fbx.meta` imports only frames 30.5 through 36.7. It contains no phase or contact curves. This is a short animation segment, not evidence of a complete takeoff-to-recovery sequence.

Introduce an actor-neutral `ActorMotionPhase` tracker. Update it from physical observations before the existing `evaluatePose` early return. Keep clip selection and skeleton binding outside the tracker.

Suggested input: delta time, normalized gravity up, actual velocity, grounded, intentional jumping, and suspension for swimming/traversal. Teleport and respawn call Reset explicitly.

Suggested output: phase, phase elapsed time, transition sequence, airborne time, downward travel, landing speed, and tangent takeoff speed. Use Grounded, JumpAscent, JumpApex, JumpDescent, PassiveFall, and LandingRecovery for the first slice. Keep Takeoff preparation out until the motor supports it: the existing motor applies the jump immediately.

Use `Dot(velocity, up)` for vertical motion. Never use world Y. Latch intentional jumping and the selected performance for the airborne sequence. A late skill or equipment update must not switch contact technique halfway through an action. Landing begins on observed airborne-to-grounded transition. Suspension clears airborne state so traversal completion does not generate a false landing.

`SurfaceCharacterController.cs:122` owns launch velocity and clears Jumping on support at line 153. Preserve that authority. The presentation tracker must not cause jump impulses, damage, or object attachment through rendered frame callbacks.

## 2. Existing source clips offer an implementable phase set

Source directory: `D:/Unity/Explore Assets/Assets/Fantacode Studios/Third Person Controller/Animations/Locomotion/`.

| Source asset | Import evidence | Status |
|---|---|---|
| Jump.fbx | Jump take, frames 30.5–36.7, not looping | Already copied into SyntyHero |
| Jump Idle.fbx | InAir take, frames 0–80, looping | Scratch only |
| Jump Down.fbx | Jump Down take, frames 0–24, not looping | Scratch only |
| Landing.fbx | Jump take, frames 58–61.699997, not looping | Scratch only |

Frame bounds are verified import metadata. Exact contact moments, clip duration in seconds, and pose suitability have not been sampled. Do not label guessed normalized values as authored contacts. Preview these clips and the complete source take before selecting phase windows. Separate clips per phase are valid; one monolithic jump clip is not required.

The source LocomotionController uses FallTree for vertical jumping and distinct Landing/LandAndStepForward/FallingToRoll paths. This confirms separate source behaviors, not proof that its world-Y/root-motion implementation fits our gravity-relative motor.

## 3. Performance data should map phases, not blend incompatible techniques

A validated immutable runtime profile can contain the clip reference, phase window, transition duration, contact curves, and recovery duration. Authoring assets convert once at initialization. A skill rule selects a whole compatible profile. Keep basic motion as fallback. The first implementation only needs one actual profile; do not fabricate mid/high skill art.

Physical ascent progress can use observed launch speed and current vertical speed, with monotonic progress and a defined apex tolerance. Descent cannot infer landing time from vertical speed alone. Hold or loop a descent phase until support; add predictive support distance later when the motor exposes reliable contact queries. Start landing recovery from actual contact, retaining measured impact speed.

## 4. Injury assets exist for a later second case

`D:/Unity/Explore Assets/Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/Unarmed/RPG-Character@Unarmed-Walk-Injured.FBX`

`D:/Unity/Explore Assets/Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/Unarmed/RPG-Character@Unarmed-Idle-Injured1.FBX`

Armed and weapon-specific injured variants also exist. No injured clip is currently in the SyntyHero art folder. Their injured side and contact timing remain unverified. Do not assume a left-leg injury or mirror safely without visual checks.

## 5. Validation requirements

- Identical phase state under normal and sideways gravity for equivalent inputs.
- Equal phase progress with pose evaluation enabled or skipped.
- Landing after a short passive drop remains distinct from an intentional jump.
- Ceiling collision can enter descent without waiting for a fixed clip duration.
- A second jump, swim entry, traversal entry, and reset interrupt recovery cleanly.
- Landing is reported once; render evaluation never owns gameplay events.
- Profile validation rejects nonfinite or reversed phase windows and invalid clips.
- Existing authority-root isolation and procedural restore/evaluate/capture order remain intact.
- Visual sampling verifies source foot contacts before phase markers become accepted data.

A humanoid and a quadruped can use the same tracker and profile selection. Their phase clips and contact channels differ. Avoid Humanoid bone names in motion observations and phase state.
