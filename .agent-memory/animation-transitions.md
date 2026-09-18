# Animation continuity rule

Bryan established this rule on 2026-09-12 after reviewing the Sidekick interaction reach.

## Default

All visible animation and procedural pose changes must transition smoothly. Use cross-fades, pose blending, or tweens as appropriate.
Treat an unexplained visible snap as a defect. Do not use instant changes because they are easier to implement.

This applies to clip changes, locomotion, stance changes, interaction entry and exit, IK weights and targets, interrupted actions, and animated props.

## Implementation

- Blend from the currently displayed pose, including when another transition is already running.
- Blend both entry and release. Cancellation, target loss, and target switching must not abruptly remove a pose contribution.
- Preserve the outgoing pose or target data long enough to fade its influence. Clearing a reference is not a visual transition.
- Choose a bounded transition duration suitable for the action. Fast actions can use short blends; no single duration fits all actions.
- Keep transitions responsive. Do not delay authoritative gameplay events to wait for a cosmetic blend.
- Reuse existing animation and pose-transition systems before adding another transform writer.

## Purposeful motion and context

Bryan requested these standing requirements on 2026-09-13. They apply to all agents.
Smooth blending alone does not establish believable motion. Each body part must support the action, balance, or intended secondary motion.
Preserve weight transfer, support contacts, momentum, anticipation, and recovery. Do not add movement merely to avoid stillness.

- Support both `run -> jump -> land -> stop` and `run -> jump -> land -> run`.
- Choose and adapt the landing from current movement intent, velocity, and support conditions, including changes during flight.
- A stopping landing prepares for contact, absorbs momentum, and settles into supported rest.
- A continuing landing contacts with the forward leg and enters the corresponding running support phase without an extra airborne stride.
- For an off-axis reach, choose comfortable facing, distance, and a suitable authored reach or hand first.
- Use coordinated torso and shoulder motion for small offsets. Turn or step for larger offsets before reaching.
- Restrained IK completes contact. Hand accuracy does not excuse an awkward elbow, crossed arm, or unsupported body.

Bryan prefers the focused review page. Present at most three actions with explicit current/previous revisions, runtime C, angles, and frame/time markers.
Keep original-source diagnostics accessible outside the main queue. Agents must investigate reported intervals before requesting another review.
Record observed motion separately from the suspected cause. Do not claim normal-speed inspection when only frames were available.
The broader audit remains open. These requirements do not certify existing animations.

## Urgency and visible contact

Bryan's round 5 ladder feedback prefers the normal climbing posture at a faster rate over the wall-climb pull-in silhouette.
Do not treat reduced completion time as proof of urgency. Preserve comfortable body clearance and a suitable support sequence.
Use physical skipped rungs only where the authored reach and current rung spacing support them.

Round 4 ladder feedback on 2026-09-13 established that urgency includes approach, mount, and exit, not only the repeated cycle.
Compare matched action phases and disclose playback rates. Do not insert a separate clock hold while a blend enters an authored action.
Keep pose and root timing synchronized. Judge the visible finger cup or palm against the object mesh; anatomical marker proximity alone is insufficient.
A complete authored short route may replace repeated ascent when height and the full collision path fit. Retain normal traversal as the fallback.
See the [round 4 review record](../docs/design/2026-09-13-animation-quality-review.md#round-4-urgency-and-complete-short-ladder-motion).

## Exceptions

An instantaneous visible change requires a specific necessity and a documented reason at the implementation site.
Explain why a short blend cannot meet the requirement. Urgency, hit reactions, and networking are not automatic exemptions.
Initialization before the first visible frame and resets while hidden do not require a visible transition.
An explicitly requested debug pose scrub or reset can be immediate within that diagnostic operation.
Do not infer permission for visible snapping in ordinary gameplay from those diagnostic exceptions.

## Verification

Check entry, exit, interruption, rapid retriggering, target switching, target destruction, and leaving interaction range.
Inspect consecutive rendered frames as well as final poses. A correct final pose does not prove a smooth transition.
Record any intentional visible snap and its reason in the relevant feature documentation.

This rule records required behavior. It does not certify that existing animation paths already comply.


## Live feedback: preparation, idle, and force

The [revision 2 feedback record](../docs/design/2026-09-13-live-animation-feedback.md#revision-2--grounded-preparation-active-inspection-authored-lift) supersedes the fixed inspection pose.
A waiting state needs authored idle movement. A held pose alone does not establish a natural idle.
Keep grounded preparation before ledge-jump travel. Do not replay that preparation during a reactive airborne catch.
For lifting, acquire at the authored grip event and let the authored hands move the prop.
A held-object review with injected acquisition cannot validate pickup weight or force. Capture the real input-to-release sequence.

Paused multi-camera comparisons must refresh skinned mesh matrices. Matching bone data does not prove that the renderer displayed the sampled pose. `SidekickInteractionComparison` now forces those updates.
