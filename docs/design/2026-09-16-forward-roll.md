# Forward roll

Status: Bryan approved ROLL-01 revision 2 on 2026-09-16.

Bryan accepted DODGE-01 for now and requested continuation.
This pass adds an unarmed forward roll using the owned RPG Character Mecanim clip.
It reuses ActorDodge, the existing animation graph, and editable motion assets.

Acceptance: preserve authored dip, tuck, travel, landing, and recovery at the original 0.8-second duration.
Reject walls and unsupported routes before starting. Reject repeated activation during a committed roll.
Resume locomotion after recovery. Preserve A/B/C comparisons and complete two-angle captures.

The roll retains conservative standing clearance with additional forward and backward body clearance.
Low obstacles, armed variants, moving platforms, stamina, invulnerability, and dedicated collision reactions remain open.
Visual approval is pending.

## Implementation and evidence

RollReview.unity uses W + Q for the forward roll. The existing ActorDodge owns route checks and root travel.
Forward Roll.anim remains editable. Its original 0.8-second timing is unchanged.
The motion asset travels approximately 2.93 metres on the production avatar.
Committed rolls finish on stop requests. Repeated activation is rejected.

Review: local-only/animation-review/roll-2026-09-16/index.html.
Three complete two-angle scenarios cover stopping, continuing into a run, and blocked routes with repeated input.
Independent source A uses the original avatar and source clip at 1×, with a root-following diagnostic camera.
Matched roll-and-stop B disables final procedural corrections. C uses final runtime playback.
Separate B captures for running and blocked routes are not included.
Capture recipes, PNG sequences, frame metadata, and encoded videos remain in the evidence folder.

All 25 focused EditMode tests passed (ActorDodgeTests, ActorPerformancePlaybackTests, and ActorBeamTests).
The Planet code build passed with 23 warnings and zero errors. See build.log in the evidence folder.
Graphify update completed. Video frame counts and review-page JavaScript validation passed.
Consecutive and sampled frames were inspected. Normal-speed playback and a player build were not performed.

Inspect hood/head clearance during the tuck and foot settling into relaxed idle.
Dynamic interruption during a roll was not rendered in this pass; dedicated bracing remains absent.
The conservative clearance checks do not establish full mesh collision accuracy.
Armed rolls and main-game combat integration remain unreviewed. The broader continuity audit stays open.

## Revision 2: continuing recovery

Bryan reported a pause in roll-to-run. The source root finishes travel near 50% of the clip.
The previous movement lock continued through its stopping tail to 100%, although movement input was held.
Acceptance: resume requested movement from supported recovery, without a stationary tail or pose snap. Preserve the full stop without input.

ActorTraversalMotionAsset now stores an editable LocomotionExitNormalized marker. Its default of one preserves existing behavior.
The forward roll sets this marker to 0.5. ActorDodge permits release only after the marker with ground support.
The existing animation graph blends out the roll while the normal motor resumes movement on the same simulation tick.
The clip and source playback rate are unchanged. No new pose writer or generated animation was added.

Matched before/after input holds forward sprint from frames 30 through 59. Both captures use the same cameras and 60 Hz simulation.
Before: forward travel falls below 0.12 m/s at frames 43–53; running starts during frame 54.
After: running starts at frame 42, removing approximately 0.4 seconds of stationary recovery.
The measured first full running frame travels at 5.6625 m/s. This fixes the lock; visual acceleration and foot support still require review.
See recovery-v2.json for frame measurements. Matched B and C root trajectories are identical.
The third scenario applies movement at frame 50 during recovery and releases it immediately.
No-input live verification still completes the full clip at the original 2.92844-metre endpoint.

17 focused EditMode tests passed. The Planet code build passed with 21 warnings and no errors (build-v2.log).
Graphify update, video frame counts, and page JavaScript validation passed.
Consecutive recovery frames 39–54 and late-input frames 48–55 were inspected, including a full-resolution contact frame.
Normal-speed playback remains uninspected. Revision 1 and the matched before capture are preserved.

Bryan subsequently approved revision 2. The reviewed page is preserved as revision-2.html.
This approval covers the review set; it does not close the remaining integration or broader audit work.
