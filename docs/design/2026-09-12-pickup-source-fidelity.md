# Small-item pickup source fidelity

Table and ground items now follow the posed right hand after acquisition. The existing pickup clips retain their authored arm motion during lifting and inspection. The hand IK contribution releases through the shared pose blend. The prop uses the calibrated palm contact after pose evaluation, with bounded position and rotation changes.

The five small-item targets enable `FollowAnimatedHand`. Crate and chair targets retain their existing carry behavior. No source clips or chest settings changed. Initial reaching and placement still use contact IK; this pass does not redesign those phases.

Placement restores the original item orientation. Surface bounds use that final orientation without changing the displayed transform during validation. The existing placement checks, markers, and collider restoration remain in use.

## Validation

- Review scene: `Assets/Scenes/Tests/SidekickInteractionReview.unity`, Sidekick Rider, manual 60 Hz steps.
- EditMode job `c5ff7e6a5917465abefd7f7cbd9a178c`: 45 passed, zero failed or skipped. Groups: SidekickInteractionReviewTests, InteractionPoseBlendTests, HumanoidContactIntegrationTests.
- Added a regression for final-orientation placement bounds and unchanged displayed rotation during the query.
- Editor build completed with 66 warnings and zero errors. Full log: `local-only/pickup-source-fidelity/build.log`.
- Five-item runtime check: maximum settled palm gap 0.000610 m. First-frame placement-cancellation displacement ranged from 0.00806 to 0.00927 m. All five items completed placement after cancellation and retry.
- Replay body for Unity execute-code: `local-only/pickup-source-fidelity/validate.cs.txt`. It requires Play Mode in the review scene. Targets are isolated during selection because the bottle station can select the nearby key.
- Review video: `local-only/pickup-source-fidelity/review.mp4`, 12 seconds. Table pickup/inspection/placement, then ground pickup/inspection/placement. Captures use 60 Hz simulation and 30 fps output.
- Comparison video: `local-only/pickup-source-fidelity/baseline.mp4`. This replay disables FollowAnimatedHand and exercises the retained fixed carry path with the same camera and timing. It is a reconstructed comparison, not a pre-edit capture.
- Visual frame review caught tilted placement during the first attempt. The final capture shows the tankard upright and the ground book flat.

Visual approval remains with Bryan. The broader animation continuity audit remains open. Crate and chair source-fidelity passes remain separate work.
