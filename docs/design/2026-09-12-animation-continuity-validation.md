# Animation continuity validation

The shared procedural pose layer now retains outgoing hand targets during release. It bounds target position and rotation changes during retargeting. Foot support, surface support, spine correction, and body offset fade when disabled. Birds retain procedural pose state when flight starts so corrections can release.

The existing ActorAnimationGraph base-weight blend remains the shared clip transition mechanism.

## Evidence

- Core build passed with zero warnings and errors. Log: `local-only/animation-continuity-core.log`.
- Planet build passed with 21 warnings and zero errors. Log: `local-only/animation-continuity-planet.log`.
- Unity resolved the new InteractionPoseBlend type after full refresh. An earlier HotReload attempt reported CS0246 before that refresh.
- EditMode job `6b4942879af84de390e27c31ebda4c3a`: 269 passed, zero failed or skipped.
- Coverage: InteractionPoseBlendTests, SurfaceChainTests, HumanoidAnimationTests, HumanoidContactIntegrationTests, FootStepContinuityTests, ActorAnimationGraphTests, SidekickInteractionReviewTests, ProceduralPoseTests, and BirdPresentationTests.
- The first run had ten failures in an old test that required immediate hand release. The updated test requires a bounded first release frame and eventual release. All ten character configurations pass.
- Runtime scene: `Assets/Scenes/Tests/SidekickInteractionReview.unity`, tabletop tankard target, Sidekick Rider, manual 60 Hz steps during Play Mode.
- After 90 settling frames, target loss moved the hand 0.037743 m on its first frame. The settled displacement after 60 frames was 0.410902 m.
- A repeated release measured a maximum frame displacement of 0.040008 m over 60 frames.
- Restarting a reach after three release frames moved the hand 0.038934 m on the next frame.

## Limits

These results validate the changed paths. They do not prove that every animation follows the rule. Runtime measurements are numerical checks, not a completed visual review of every clip.

The broader transition audit remains open. Review procedural resets after frame hitches, secondary-chain disabling, and active clip-time changes during traversal interruptions. Initial hidden setup, explicit review-scene resets, and diagnostic scrubbing remain deliberate exceptions.

The later [Sidekick interaction sequence validation](2026-09-12-sidekick-interaction-sequences.md) records 283 passing tests and scoped runtime frame measurements.
That work does not close this broader audit.
