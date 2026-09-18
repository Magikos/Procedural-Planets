# Chopping interaction

Status: First playable technology slice implemented. Focused tests and a complete runtime capture pass; artistic review and main-planet integration remain open.

Scope: A playable humanoid chopping fixture, authored start/work/exit clips, repeated impact markers and actual HarvestService damage/yield. The older planet capsule host is not being replaced in this slice.

Reuse `ActorInteractionDefinition`, `ActorInteractionSession`, `SidekickInteractionReview`, existing tool presentation, and `HarvestService`. Add an opt-in repeat flag so existing waiting interactions retain their behavior. Stop finishes the current cycle; cancellation ends authority immediately and existing view release blends the pose.

Three strikes fell the fixture tree and grant three wood. Target validation runs at each impact. Partial damage survives action cancellation within the fixture session. Reset clears damage and rewards. Partial-damage save/load is outside this slice.

The fixture uses the same harvest service with local store/inventory delegates. It is not proof of planet scatter integration. The main-game humanoid connection remains follow-up work.

## Acceptance

- One effect per impact, including a frame spanning multiple cycles.
- No reward before impact, after target loss, or twice for one depleted tree.
- Stop exits the work cycle; cancellation blends out through existing ownership.
- Complete authored motion renders on the production humanoid with visible tool contact.
- Preserve original FBX clips; save editable project clips with no per-animation generator.
- Run focused interaction/harvest tests and capture the complete action.

## Evidence

Playable scene: `Assets/Scenes/Tests/HarvestInteractionReview.unity`.

Focused EditMode run: 72 passed, 0 failed (HarvestInteractionTests, HarvestServiceTests, SidekickInteractionReviewTests). No compiler errors matched `error CS` after import.

Runtime complete sequence: 3 impacts, 3 wood, tree felled, session inactive after recovery. Runtime cancellation before first impact: 0 impacts, 0 wood, session inactive. Capture: `local-only/animation-review/chopping-2026-09-15/complete.mp4`, 360 frames at 30 Hz, side and rear views. Metadata records each frame's phase, progress and resource state. Agent inspected an overview and consecutive contact frames; normal-speed artistic approval remains pending.

Visual limitations: primitive tree fixture; axe acquisition/return is a shared prop blend rather than an authored pickup from storage. Grip roll, clipping and foot support need further visual review. This technology slice does not claim polished motion. The capture starts at the stance; automatic approach uses existing behavior but needs its own capture. Independent original-source A and uncorrected B were not captured in this slice.

The service's multi-hit progress is session-local and requires ResetProgress when a host reuses it after world reset. Existing planet prototypes retain their one-hit default; no main planet behavior was silently switched to the fixture's three-hit setting.

See [the coverage inventory](../audit/2026-09-15-interaction-animation-coverage.md) for subsequent families.

## Grip correction review — revision 2

The chopping FBXs require the vendor T_pose avatar. Creating an avatar from each motion FBX corrupted the retargeted arms. The imported source avatar now supplies all three clips. The vendor demo attaches the axe to the left hand. HeldToolGrip now fits a primary palm anchor and provides a bounded secondary handle segment through the existing pose solver. It replaces reconstruction from both hands and actor facing.

Current capture: local-only/animation-review/chopping-2026-09-15/anchored-v2.mp4. Complete 360-frame side/rear capture: three impacts, three wood, recovery to idle. Focused tests: 76 passed, zero failed. Cancellation and target loss before impact produced zero rewards. Driving-palm error during work stayed below 0.001 mm. Supporting-palm separation averaged 8.4 cm and peaked at 24.9 cm during windup; this remains a defect, not accepted polish.

A subsequent supporting-arm curve experiment regressed contact during rendered verification and was removed. anchored-v3 is rejected diagnostic evidence, not the current result. Tree Chop.anim again contains corrected-source curves, with editable asset identity preserved. No per-animation author script was added.

Revision 2 fixes the reversed axe and corrupted swing. Supporting contact, acquisition/return, and independent full A/B captures remain open. The review page retains revision 1 for comparison. User visual approval is pending.

## Supporting-hand jump — revision 3

Reported windup defect reproduced at frames 74–86. The moving-contact solver constrained the authored sideways/backward reach against actor forward and used a fixed elbow hint. A requested gap near 11 cm produced a 33 cm palm displacement. FollowAuthoredMotion now constrains reach relative to the sampled arm direction and retains the sampled elbow plane unless an explicit bend target exists. Stationary reaches retain their existing rules.

Acceptance: remove solver-induced cross-chest displacement while retaining source timing, grip acquisition/release, blade orientation and three impact events. Diagnostic before/after data: hand-baseline.json and hand-corrected.json under the chopping review directory. Maximum palm correction across frames 70–87 fell from 0.3296 m to 0.1145 m. This is correction displacement, not a claim of perfect handle contact.

Current complete capture: hand-fix-v4.mp4 (review revision 3), 360 frames at 30 Hz with side and rear views. Consecutive windup frames and full-action overview inspected. Three impacts, three wood, final idle. 36 focused EditMode tests passed; no compiler errors. Added regression for an authored reach behind the actor. Supporting-hand separation and blended prop acquisition/return remain review limitations. Other moving-contact actions have test coverage but were not rendered again in this pass. User visual approval pending.
