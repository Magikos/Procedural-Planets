# Beam walking — first review slice

Status: Implemented in BeamReview.unity. Visual approval pending. Main-game adoption is not established.

Bryan approved LEDGE-01 revision 4 and requested continued missing coverage. This slice adds fixed, straight, level beam traversal. It preserves accepted ledge clips.

## Behavior and ownership

ActorBeam owns supported root travel, entry, balance idle, forward and backward travel, turn, and exit. BeamInteraction supplies geometry and seven authored motion assets. HumanoidAnimationPrototype connects input to the existing performance player. No additional pose solver or per-animation authoring generator was added.

At either entrance, face along the beam and press E. W travels forward; S travels backward. A/D queues the authored 180-degree turn. Releasing movement finishes the current step before idle. Forward loop steps use alternating clip halves. Backward motion finishes its full clip. E, Space, or Ctrl releases beam mode. Shared performance playback blends entry, phase changes, and release over 0.15 seconds.

The route checks surface support and capsule clearance before a phase and during movement. Blocked movement retains idle and allows reversal. Support loss releases authority without moving the actor to another position. Ordinary movement then handles falling. A turn now updates camera facing throughout the action, preventing the old camera direction from reversing the actor after exit.

Beam Review.prefab provides reusable fixture geometry. BeamReview.unity contains the playable setup. Use Visit Balance beam entrance in Play mode before pressing E.

## Authored assets

Source: D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/Traversal_BalanceBeam_*.fbx.

Seven originals are copied under Assets/Art/Interactions/Animations. Beam *.anim files are editable production variants. Beam * motion.asset files store extracted forward displacement and turn yaw. Beam Performances.asset binds phases. These files are not regenerated at runtime.

| Phase | Duration, seconds | Displacement, metres |
|---|---:|---:|
| Forward_Start | 1.500 | 0.462 |
| Forward_Loop | 3.167 | 0.671 |
| Forward_End | 1.833 | 1.519 |
| Back_Loop | 1.700 | -0.384 |
| Back_End | 1.333 | -0.462 |
| Idle | 3.167 | 0 |
| Turn_180 | 1.000 | 0; 180-degree turn |

All phases retain 1x authored timing. Production clips remove extracted longitudinal body translation; the turn also removes extracted yaw. Body height, sway, and limb curves remain authored. Existing foot correction remains active in runtime C. No beam-specific foot locking was added.

## Evidence

Bundle: local-only/animation-review/beam-2026-09-15/index.html.

- forward-final: entry, complete crossing, exit, rest; 600 frames.
- control-final: entry, travel, stop, backward travel, turn, return exit, rest; 555 frames.
- control-before: same controls before the exit-facing correction.
- source: seven original clips on the verified original Android rig, beside raw editable playback on the production Sidekick rider without corrections.

C uses deterministic 1/60-second steps, captured at 30 fps, at 1280 x 480 with two 640 x 480 cameras. The beam is 4 metres long and 0.34 metres wide, with its top at world Y=1. Actor motor root settles at Y=0.975. Wide camera: (-12,4,-5), looking at (-6,1.5,0), orthographic size 3.9. Close camera follows actor offset (-3,1.3,-2), looking 0.8 metres above root, orthographic size 1.55. Capture recipes and per-frame phase/root metadata are retained in the bundle. Each capture resets the actor and visits station zero.

Source diagnostics run clips consecutively at 1x without transition blends. The Animator transform is fixed, and original baked body motion remains visible. The raw production diagnostic lacks the runtime motor trajectory and graph. A complete matched B remains unavailable; runtime blend effects are not fully isolated.

I inspected sampled full-action frames and consecutive exit frames with timing data. Normal-speed playback was not inspected. Planted-foot drift has not been quantified. Source and final screenshots do not certify full animation quality.

19 focused EditMode tests passed: ActorBeamTests, ActorPerformancePlaybackTests, ActorThirdPersonCameraTests. Job bc9047c49d3a467ababafe63aa57b89a. Tests cover both entry directions, supported stops, backward travel, turn and exit, blocked movement, support loss, cancellation, invalid pose, shared playback, and camera behavior. Focused diff whitespace checks passed. Existing unrelated analyzer warnings remain.

## Open coverage

Visual approval, precise planted-foot drift, runtime interruption renders, wider body proportions, sloped/moving/changing-size beams, falls from both sides, and main-game wiring remain open. Narrow ledge walking, rope climbing, fishing, equipment draw/stow, combat, and skill variants remain separate work. The broader animation audit remains open.

## User approval
Bryan approved both current beam sequences on 2026-09-15. Remaining coverage stays open.
