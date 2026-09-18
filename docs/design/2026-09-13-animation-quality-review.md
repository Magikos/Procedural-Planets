# Animation quality and continuity review

## Review-set approval — 2026-09-13

Bryan confirmed: "I have reviewed all the animations in this review set and they all look good."
All current situations in the focused review set are approved: LADDER-01 revision 7, DOOR-01 revision 2, and JUMP-01 revision 4.
Preserve these current captures as accepted visual baselines. Previous captures remain historical comparisons.
This approval supersedes earlier pending-approval notes for these current revisions. Keep measured residuals as evidence, not pending visual approval.
The broader animation audit and coverage outside this review set remain open.


## Round 5: normal posture for fast ladder climbing

Bryan rejected revision 6 tall sprint at C side frame 95 / 3.167 seconds. The wall-climb take still pulled the torso into the ladder.
Use the existing normal ladder gait at the configured fast rate. Preserve the short native pull-up and fast mounts and dismounts.
The tall 0.25 m rung spacing supports alternating two-rung and one-rung hand acquisitions without new procedural reach.
V28 removed the entry dip but exposed earlier contact preparation selecting a wrong rung. Hand and direction must identify the landing event.
The normal planner now uses that event identity. Four regression cases cover early preparation in both directions without raising the 6 cm correction limit.
All 363 selected EditMode tests passed. The Editor build had zero errors and 88 warnings.
Five V29 routes completed. Tall sprint takes 1.667 seconds from gait entry to top dismount, versus 3.233 seconds for normal.
The prior entry dip is absent. A 2.46 cm hand-acquisition error settles within 50 ms and remains a contact defect.
Bryan reviewed and approved ladder revision 7 (V29). Preserve V29 as the accepted visual baseline.
Exact finger and foot contact limitations remain documented. Agent inspection of normal-speed playback remains incomplete. The broader continuity audit remains open.
Keep the [round 5 report](../../local-only/animation-review/session-2026-09-13/feedback-round5-review.md) with complete current/previous captures and remaining defects.
Do not infer urgency from completion time alone. Preserve the preferred posture, body clearance, and support sequence.

## Round 4: urgency and complete short-ladder motion

Bryan reported a dip at V25 sprint-short C side frame 100 / 3.333 s and insufficient urgency at sprint-tall frame 114 / 3.800 s.
The short report occurs at sprint entry, not the top exit. Faster middle cycles alone do not establish an urgent complete action.
Review approach, mount, repeated ascent, and exit timing together. Compare the same phase boundaries and state the source and runtime rates.
A blend must not add a separate clock hold before the incoming authored action advances. Preserve pose/root synchronization through the existing blend.

The selected short-route trial uses the complete owned Threepeat 3 m climb at native 1x. Height compatibility and a clear full route govern selection.
The normal ladder route remains the fallback when the height does not fit. A blocked selected short route rejects entry before commitment.
Selection does not certify final contact or rhythm.
The native source rises approximately 2.5547 m in 1.2 s and contains its own brief loading dip before the pull-up.
The [round 4 source survey](../../local-only/animation-review/session-2026-09-13/ladder-urgency-source-survey-round4.md) separates that source motion from the reported runtime boundary.
The [source provenance](../../local-only/animation-review/session-2026-09-13/ladder-round4-source-provenance.md) records hashes and the editable copy.

Contact review must inspect the visible finger cup or palm against the rung mesh. A nearby anatomical marker does not prove the rung enters the grip.
Retain mesh clearance, support timing, and correction size alongside marker distance. These principles do not establish visual approval.
The [revision 6 report](../../local-only/animation-review/session-2026-09-13/feedback-round4-review.md) links eight complete captures and remaining defects.
Top and bottom fast dismounts measured 1.117 and 0.683 seconds, versus 2.133 and 1.267 seconds previously.
The final round 4 run passed 359 selected EditMode tests. The Editor build had zero errors and 90 warnings.
Captures precede a hitch-only correction with an unchanged 60 Hz path. The report records that provenance limit and the corrected rounding regression.
Tall-entry settling, some contact acquisition errors, and exact foot contact remain open. Bryan's visual approval remains pending.

## Review feedback: rung grip and fast ladders

Bryan reported a missed rung grip in LADDER-01 revision 4, short ladder, rear frame 144 / 4.800 seconds.
He also requested sprint ascent that skips rungs and authored slide descent.
The [round 3 report](../../local-only/animation-review/session-2026-09-13/feedback-round3-review.md) records complete captures, measurements, remaining defects, and approval status.

Normal climbing now retains planned support across stop and reversal. The shared solver preserves the authored grip through release.
Editable CCP grip variants change 46 finger, wrist, and forearm-twist curves. They preserve arm stretch, body, and root curves.
Contact preparation remains distinct from expected support. A reaching hand can adjust its position along a rung before acquisition.
The motor and contact planner derive the same rung count from source palm separation and ladder pitch.
Mirrored end transfers match the leading hand. A low top transfer finishes its bounded authored handover before retrying the route.
The same collision authority validates the full route and rejects persistent obstacles. Reversal and cancellation retain control.

Sprint ascent uses owned left-leading and right-leading wall-climb takes with physical skipped-rung reaches.
Production advances pose and native root together at 1.5x. One clearance rule governs sprint entry and continuation.
Editable sprint grip variants change only 40 finger curves. They close during support and release during the reach.
Slide descent uses the owned start, loop, and brake family at 1x, with bounded transverse rail contact.
The brake prepares a fixed return rung from the final normal source pose. Bounded endpoint fitting retains the native upward brake motion.
The implementation uses existing performance playback, character collision, and contact correction ownership.
It adds no competing pose writer. Hand IK remains capped at 6 cm; body fitting remains bounded at 15 cm per transfer.
The foot-contact heuristic requires 100 ms near the same rung and at least three observations before acquisition.
This rejects a slow swing apex that previously caused a one-frame sprint IK kick. It retains existing release blending and correction limits.

Controls: W/S climbs normally; Left Shift + W skips rungs; Left Shift + S slides.
The focused review keeps stable action and situation IDs, current/previous versions, visible frame numbers, and separate stress-test situations.
A uses independent original clips on the production avatar at documented source rates. It is not an original-artist-rig reference.
B omits final limb corrections but shares C's runtime root and body fitting. C is the final runtime result.
The separate full native sprint-source take avoids truncation when faster production switches phases before source A completes.

Initial entry sliding, forefoot calibration, other avatars, and broader animation families remain outside this grip/fast-mode correction.
Each review now reports hand and foot contacts separately; hand-only maxima hid the false foot plant during an earlier capture pass.
The final code checkpoint passed 456 selected EditMode tests. The Editor build completed with zero errors and 88 warnings.
The reported frame 144 now has a persistent left-hand anchor with zero marker error, compared with the previous 6.41 cm nearest-rung gap.
Sprint reversal still has a brief incoming-hand gap and a smaller right-foot correction. Stable proximity alone does not prove an authored foot plant.
Consecutive frames and timing data support the review. Normal-speed playback and Bryan's visual approval remain pending.
The broader animation continuity audit remains open.

## Review feedback: alternating ladder hands and visible door contact

Bryan reported two defects after the hybrid trial. The reported hand pattern and door gap are corrected in the inspected intervals. Visual approval remains pending.
The [feedback record](../../local-only/animation-review/session-2026-09-13/feedback-round2.md) preserves exact action IDs, revisions, frames, and acceptance checks.

- LADDER-01 short frame 85 and tall frame 130: hands should alternate and pass one another when another rung is available.
- DOOR-01 open frame 60: the visible hand must contact the door or handle before pushing.

The door's old marker was 9.53 cm from the visible mesh. Previous marker-error measurements did not establish contact.
The baseline ladder up source brings the following hand alongside the leading hand. Source suitability must be resolved before stronger rung IK.
The prior review remains archived at `local-only/animation-review/session-2026-09-13/index-hybrid-round1.html`.

The [round 2 report](../../local-only/animation-review/session-2026-09-13/feedback-round2-review.md) links complete current/previous actions and measured limits.
LADDER-01 revision 4 uses matched authored climbing and native travel. It preserves the current gait pose when stopped and matches phase on reversal.
Contact detection now reads the current source pose through the existing `BeforeCorrections` event. The old order added about 2 cm of false reach error.
The original climb reference uses the publisher's 1.5x rate. Production remains a labeled 1x trial on the same retargeted avatar.
The three-rung tall trial was rejected because it changed the leading hand at the top transfer.
Full rung support remains defective: expected nearest-rung errors reach 13.4 cm on short descent and 8.17 cm on tall climbing.
The 6 cm rung correction cap remains. Initial ground-entry sliding and ladder forefoot calibration remain open.

DOOR-01 revision 2 projects contact onto the visible panel and measures the rendered palm skin independently.
The palm contacts the panel 50 ms before hinge motion. Peak correction is 3.13 cm; push penetration reaches 2.81 mm.
The action uses a small authored push followed by free swing. Cancellation emits no opening marker, but brief 6.04 mm penetration remains.
Other door heights, avatars, closing, and opposite-side use remain unreviewed.

The focused queue retains JUMP-01 revision 4 without new jump changes. Chest and creature findings remain outside this round.
Consecutive frames and timing data support this review. Normal-speed playback was not inspected.

## Hybrid implementation checkpoint — 2026-09-13

Status: the bounded hybrid trial is implemented and packaged for review. Visual approval is pending. The broader audit remains open.
The [focused page](../../local-only/animation-review/session-2026-09-13/index.html) contains JUMP-01, LADDER-01, and DOOR-01, with separate situations under each action.
The [complete findings](../../local-only/animation-review/session-2026-09-13/hybrid-review.md) identify remaining defects and rejected experiments.

The running jump now consumes movement intent separately from carried airborne velocity.
It keeps one authored airborne action and selects stopping or continuing recovery at actual support.
The continuing branch uses late authored preparation and a mirrored landing matched to the incoming running support phase.
The stopping branch reuses the older compact recovery. Resumed movement releases that recovery through the existing blend, including immediate post-touchdown input.
The runtime uses the existing performance graph and foot correction stage. It does not add a second pose writer.

The door now uses the existing motor approach before reaching. The actor holds the attained stance during the action.
Source measurements exposed an independent height mismatch in the old DoorKnob clip.
The new native high-reach candidate is a palm push. It is not a finger knob twist.

The ladder now has an authored final approach step before mounting.
Its editable clip and motor trajectory reconstruct the original motion within the 2 mm regression tolerance on the production avatar.
The regression covers native humanoid foot solving both enabled and disabled. The production graph retains its existing native foot solving explicitly.
Disabling that shared setting was rejected because stopping foot contact regressed. No new procedural pose writer was retained.
The final step uses the existing terrain correction and soft support. Entry still slides; source reconstruction does not certify cross-clip support.
Failed intermediate captures remain outside the main review queue.

The focused review page now groups situations under each action. Stop, continue, and changed-input jumps are different situations, not different revisions.
Feedback retains the action, situation, revision, angle, time, and frame interval.
Chest feedback and the broader creature/traversal audit remain open outside this focused round.

| Area | Final evidence | Remaining finding |
|---|---|---|
| Running jump | Five complete context-v6 recipes: stop, run, flight resume, late stop, immediate recovery resume | About 4–5 cm sole movement into idle; continuing landing needs artistic approval |
| Ladder | Four v11 recipes: short, tall, mount retry, blocked top exit | Initial foot excursion about 5 cm; maximum planar correction 8.80 cm |
| Door | Native-reach-v3 and contact-cancel-v3 | Palm push, fast cancellation, no arbitrary-height feasibility guard |
| Functional validation | 435/435 selected Edit Mode tests; editor build has zero errors and 87 warnings | These checks do not establish visual quality |
| Review tooling | Sixteen selected captures encoded into both runtime angles; frame counts checked by ffprobe | UI state tested with a DOM stub; interactive browser rendering and normal-speed playback were not inspected |

Final test evidence: `local-only/animation-review/session-2026-09-13/tests-hybrid-final.json`.
Build log: `local-only/animation-review/session-2026-09-13/build-hybrid-editor-final.log`.
Unity was left in Edit Mode with the saved, clean `SidekickInteractionReview` scene.
The skill records actual playable settings and source reconstruction checks. Its bundled validator could not run: `ModuleNotFoundError: No module named 'yaml'`.
Frontmatter and discovery routing did not change.

## Context-sensitive motion direction

Recorded 2026-09-13 from Bryan's feedback and approved for the bounded trial above. This section records the architecture direction.
The [standing requirements](../../.agent-memory/animation-transitions.md#purposeful-motion-and-context) define the two landing outcomes and coordinated whole-body reach.

Recommend a hybrid built on existing playback and contact ownership, with mostly artist-authored motion.
Avoid a separate animation state for every speed, target position, and possible outcome.
Organize actions by meaningful phases, such as approach, anticipation, contact, recovery, and release.
Keep speed, direction, target offsets, and movement intent as values consumed by those phases.
Use explicit support events and phase-compatible selection where the support sequence changes.
Keep authoritative movement and collision in the motor. Presentation consumes that state; IK must not become a second movement authority.

| Mechanism | Responsibility | Boundary |
|---|---|---|
| Authored clips and editable variants | Timing, limb arcs, weight transfer, and recognizable action | Add variants for materially different support or momentum, not every target position |
| Context selection in existing action playback | Select stop/continue, approach, hand, and compatible entry/exit | Reevaluate intent without oscillating between incompatible poses |
| Bounded body and trajectory adaptation | Small facing, reach, and alignment changes during permitted phases | Respect collision, planted support, and authored motion; step when adaptation exceeds reach |
| Existing contact correction stage | Hands, feet, terrain, rungs, and final contact | One pose owner; release and retarget smoothly |
| Optional target-relative authored keys | Fill a demonstrated gap in reusable reaches or contact transitions | Prototype one action; preserve editable keys, curves, timing, and source comparison |

The first trial is the running jump: stop, continue, release input during flight, and resume input before contact.
Use current intent and actual momentum together. Late input changes must preserve established support and blend into a plausible recovery.
Then validate an off-axis reach with small offsets, required turns/steps, obstruction, and target changes.
Keep the ladder approach slide as a separate open defect. It needs support-compatible deceleration and mounting.
Do not expand to a general procedural animation generator before these cases demonstrate a repeatable improvement.

Overgrowth is a useful reference for combining authored keys with procedural adaptation.
David Rosen's [GDC 2014 session overview](https://www.gdcvault.com/play/1020583/Animation-Bootcamp-An-Indie-Approach) describes fluid animation using few keyframes and procedural techniques.
The [existing source research](../research/2026-09-12-animation-quality-review-research.md) records specific interpolation, stance, and foot correction paths.
These references support experimentation, not a claim that adopting their approach guarantees our desired motion quality.
The GDC overview was checked on 2026-09-13. The full talk was not watched during this documentation update.

## Earlier review workflow and corrected jump finding

This section preserves the pre-hybrid findings. The checkpoint above and current queue supersede its current-status statements.

Bryan clarified that the running-jump defect occurs during flight: the legs swap like a running stride before landing.
The useful interval is approximately 1.00–1.53 seconds, capture frames 0030–0046.
The later gait-phase change does not fix that defect. The jump remains **needs correction**.
Bryan prefers the `run-jump-before` landing. Preserve that reference and prepare a compact landing with both legs moving forward.
Authored provenance does not establish that the selected motion suits the action.

The [current review page](../../local-only/animation-review/session-2026-09-13/index.html) now presents a queue of at most three actions.
Each action shows runtime C only, one angle at a time, with explicit current/previous labels and numbered revisions.
The current round contains `JUMP-01`, `CHEST-01`, and `LADDER-01`.
The jump is already marked as needing correction; Bryan does not need to report the same defect again.
Bryan also reports foot sliding in `ladder-bottom-authored` at 1.291 seconds as the approach stops.
`LADDER-01` now remains **needs correction**, separate from the previously corrected upward mount trajectory.
Review floor-relative sole movement during deceleration, stopping, and weight transfer before mounting. Smooth blending alone is insufficient.
The [diagnostic archive](../../local-only/animation-review/session-2026-09-13/diagnostics.html) retains the earlier A/B/C bundle.

Videos contain zero-based capture frame numbers and elapsed video time. Marking an interval copies the action ID, revision, capture, angle, and frames.
Feedback also includes the observed behavior, expected behavior, and verdict. Nothing is sent automatically.
The page keeps draft feedback during action switches. Bryan must copy feedback before reloading or closing it.
The page provides a clipboard fallback because browser permissions vary for local files.

Use this loop for subsequent animation changes:

1. Record the observed defect and intended motion, including preferences for previous versions.
2. Reproduce the complete action and inspect its timing, weight transfer, silhouette, contacts, and source suitability.
3. Fix the identified interval and review its neighboring transitions. Passing tests and small correction values do not establish natural motion.
4. Publish one explicitly numbered current result per action. Keep earlier revisions and source diagnostics outside the main review queue.
5. Record Bryan's verdict against that exact capture. Do not ask him to rediscover a defect already reported.

Playing test scenes is a useful additional source of feedback. It does not replace the agent's complete-action review.
The remaining animal and broader audit coverage stays in the evidence records; it is not another approval list on the main page.

## Revision after Bryan's visual review

Bryan identified a running-jump kick, chest searching outside the opening, odd ladder mounting, confusing goat sleep coverage, and animal jitter.
The [revision findings](../../local-only/animation-review/session-2026-09-13/review-revision.md) record the fixes, fourteen selected captures, and remaining limits.
The [current review index](../../local-only/animation-review/session-2026-09-13/index.html) now focuses on those changes and additional coverage.
The [earlier broad index](../../local-only/animation-review/session-2026-09-13/index-first-review.html) preserves the first review set.

The latest regression run passes 395 tests with zero failures or skips. All three code-health builds pass.
The running-jump handoff now matches the incoming support phase. The source's own trailing-leg recovery step remains.
Chest search now uses a chest-specific authored reach with lift/examine gestures. The ladder ground mount now follows its authored root trajectory.
Focused goat drinking excludes sleep, and gradual stationary support return reduces the reproduced animal correction spike.
Added coverage includes sustained running after landing, ladder mount cancellation and retry, focused Wolf drinking, and a Deer gait recheck.
These changes await Bryan's visual review. The broad audit remains open.

## Earlier review checkpoint

Status: The broad audit remains open. The reviewed samples show measured improvements. They do not establish that all animations are polished.

The user authorizes continued review, fixes, tests, and rendered verification. Visual approval is not a gate for this work.
Continue the coverage below and provide the complete evidence bundle for review afterward.
Normal-speed playback has not been inspected. Current visual findings use consecutive frames and recorded timing.

Date: 2026-09-13. Branch: `harvest-vertical-slice`, dirty worktree above `d1e0f624ead0448f70a867f20a9389e367527293`.
Unrelated work remains preserved. The initial worktree records are:

- `local-only/animation-review/session-2026-09-13/starting-status.txt`
- `local-only/animation-review/session-2026-09-13/starting-animation.patch`

## Review method and evidence

The [review index](../../local-only/animation-review/session-2026-09-13/index.html) packages selected captures.
The [session notes](../../local-only/animation-review/session-2026-09-13/review-notes.md) retain repeat commands.
Each selected capture retains PNG frames, metadata, a labeled MP4, and encoding logs.
The package summary records selected folders and encoded frame counts. Local evidence must accompany this document when shared.

The comparison separates original source A, production playback without corrections B, and final runtime C.
The original HumanF rig provides locomotion A. Idle A remains retargeted where labeled.
Stance A uses original HumanF crawl enter/exit clips. Stand/crouch A remains unavailable.
Original deer source A uses the original rig. Other capture-specific source limits remain in metadata.
The green ghost copies production clips and timing; it is not independent source evidence.
Source-only jump candidate columns are not A/B/C. Closing source A plays forward while production chest closing reverses the opening clip.

Capture simulation runs at 60 Hz, with rendered output at 30 Hz where metadata states that recipe.
Root-aligned views do not prove authored world travel. Source switches do not prove production transition quality.
Browser frame seeking and synchronized playback are approximate. Original PNG frames and timestamps remain authoritative.
Tests do not establish visual quality. Complete normal-speed playback and wider coverage remain work to do.

## Current findings

### Humanoid proportions and locomotion

`directions-supported` captures eight walking directions and eight running directions on an isolated support floor.
It supersedes `directions-reviewed`, which fell off the review floor and cannot validate running support.
Original HumanF source playback supports the direction comparisons. Start/stop timing and broader terrain coverage remain separate concerns.

The measured arm-to-leg length ratio is approximately 0.65 on the production model and 0.538 on the source rig.
The production model therefore has arms about 21% longer relative to its legs by that bone-length measure.
This supports a model-proportion explanation for the initial long-arm concern. It does not prove that every pose or retarget is correct.
The `walk-final` stop sample from 2.467–2.633 seconds has maximum hand/head B/C correction of 0.0119 m.
No large procedural arm extension appears in that sampled sequence. Finger length and silhouette still affect the apparent reach.

`standing-jump-reviewed` preserves a four-second baseline. Its recorded phases did not reproduce an airborne freeze.
The authored running-jump change shortens landing recovery from about 0.6 seconds to 0.1667 seconds.
`run-jump-before` and `run-jump-final` retain the comparison. Consecutive touchdown frames from 1.4–1.567 seconds retain the running stride.
A curve comparison found zero key differences across 130 source bindings. This establishes curve preservation, not visual suitability.

`stance-final-interrupt` adds rapid stance changes and interruptions. Crawl A uses original source motion, including the full source exit.
The production exit-to-crouch interval is cropped, so its timing does not directly match the full source exit.
Stand/crouch source A remains unavailable. Stance coverage is sampled rather than complete.

### Chest interaction

`chest-final`, `chest-final-cancel`, and `chest-final-approach` provide the current complete-action and disruption evidence.
The [chest analysis](../../local-only/animation-review/session-2026-09-13/chest-analysis.md) records the reproduced failures and fixes.
Opening uses the owned OpenOnly clip at native rate. Inspection uses owned crouching rummage start, loop, and end clips.
Closing reverses opening, with that adaptation stated in source labels. The prop retains its dimensions.

The fit moves the approach, matches authored lid travel, and lets palms slide across actual lid triangles.
Runtime stores the authored surface data. A pre-correction callback updates the existing hinge writer from the current source pose.
Relative correction blending avoids dragging hands behind moving authored contacts.
Fixed ledge and rung targets retain world support behavior; authored-following contact behavior is explicit.
Cancellation retains outgoing influence through the shared blend.

| Final chest measure | Left | Right |
| --- | ---: | ---: |
| Peak hand correction | 0.0323071 m | 0.0485706 m |
| Opening actual palm-to-contact error | 0.0149011 m | 0.0420316 m |
| Closing actual palm-to-contact error | 0.0178666 m | 0.0405777 m |

Consecutive final lift frames 57–58 and closing frames 192–193 show the earlier lid delay removed.
B/C body and elbow motion remain close in these samples. Small contact separation remains measurable.
The approach and cancellation captures do not close the complete target-change and interruption matrix.

### Creatures and secondary motion

`deer-reviewed` and `deer-final` show reduced correction while preserving authored hoof travel.
Straight-walk maximum correction changes from 0.2355667 to 0.0372936 m.
Turn correction changes from 0.2506652 to 0.0485484 m; stop correction changes from 0.2767529 to 0.0440902 m.
Consecutive stop frames from 4.333–4.667 seconds retain the authored stance without the earlier bent foreleg.
These values measure B/C correction, not planted-hoof sliding across arbitrary terrain.

`goat-drink-final` reviews the drinking pose and contact adjustment.
The `GoatJaw1_M` proxy lies about 1.5–3.9 cm from the measured target during the sampled contact interval.
This is a jaw proxy, not an exact lip-contact measurement. Other creature faces and drinking targets remain unreviewed.
`wolf-ground-release` and wolf swimming evidence extend the ground/swim and release checks.
A procedural swimming fallback does not establish authored species-specific swimming coverage.

An actual accessory probe sampled 40 frames through disable, retrigger, and complete release.
It verified the sampled accessory release reaches zero influence. Other chains, rigs, and hitch cases still need coverage.

### Traversal

`step-up-contact-fit`, `climb-final`, and `vault-final` extend the traversal comparisons.
They provide scoped entry, contact, and exit evidence. They do not validate every obstacle size, approach speed, or interruption.
The ladder uses owned authored clips, physical rung targets, and collision-controlled travel.
`ladder-complete-short` records 760 samples over 12.667 seconds. `ladder-complete-tall` records 980 samples over 16.333 seconds.
Both complete the round trip. Maximum active ladder correction is 0.0698754 m and 0.06966245 m respectively.
Maximum anchored final rung error is 0.0510 m and 0.0626 m, including acquisition and release.
Ordinary walking between ladder ends reaches about 0.09 m ground-foot correction. This is not ladder IK.
Both source-yaw final collision-route JSON files report zero blocked segments.

`ladder-reviewed-blocked` records top rejection followed by bottom exit: 531 samples over 8.85 seconds.
`ladder-blocked-bottom` records bottom rejection followed by top exit: 511 samples over 8.517 seconds.
`ladder-reviewed-interrupt` and `ladder-reviewed-target-loss` each record 350 samples over 5.833 seconds.
Both recover to floor height -0.025 m with authority `None`.
A shared-blend regression caused fixed ledge hands to drift about 0.10 m during hang-to-climb.
The corrected blend makes active authored following explicit while retaining smooth outgoing release. The final broad rerun passes all 363 tests.

## Action × rig coverage

| Action | Rig | Current evidence/status | Open coverage |
| --- | --- | --- | --- |
| Walk/run, eight directions | Production humanoid; original HumanF A | `directions-supported`; supported capture and bone ratios measured | Normal-speed rhythm, speed variation, turns, terrain, other proportions |
| Standing jump | Production humanoid | `standing-jump-reviewed`; baseline timing sampled | Slopes, repeated jumps, varied height, full playback |
| Running jump | Production humanoid | Before/final; recovery shortened, source curves preserved | Speed changes, landing slide across terrain, repeated jumps |
| Stand/crouch/prone | Production humanoid; original HumanF crawl A | `stance-final-interrupt`; interruption sample captured | Stand/crouch A, obstruction, full transition matrix |
| Chest open/inspect/close | Production humanoid | Final action/cancel/approach captured; measured contact improvement | Other placements, target switch/loss, complete phase interruption matrix |
| Other interactions | Other props and grips | Broad family remains open | Pickup/carry/place, doors, controls, crafting, combat grips, target heights |
| Step up | Production humanoid | `step-up-contact-fit`; contact fit sample | Obstacle sizes, approach speed, cancellation |
| Climb and vault | Production humanoid | `climb-final`, `vault-final`; sampled contact and transition review | Geometry ranges, interruption, target movement, other rigs and support geometries |
| Ladder | Production humanoid | Short/tall complete routes, blocked ends, interruption and target loss captured | Other geometries, moving ladders, broader disruption matrix |
| Walk/turn/stop | Male deer | Before/final correction improvement and consecutive stop frames | Slopes, terrain, speed ranges, other deer rigs |
| Drink | Goat | `goat-drink-final`; jaw proxy measurement | Exact lip geometry, other species, target variation |
| Ground release/swim | Wolf | `wolf-ground-release` and swim sample | Authored swim source, entry/exit matrix, terrain/water variations |
| Secondary accessories | Actual sampled accessory | 40-frame disable/retrigger/release probe | Other chain setups, rigs, flight and swim motion, hitches |
| Other creature actions | Other creature rigs | Unreviewed | Rest, attacks, death, flight, authored swim, species transitions |

The [broader continuity audit](2026-09-12-animation-continuity-validation.md) remains open.
No row establishes complete family coverage or validates another rig.

## History and excluded captures

The initial package contained twelve videos and passed its frame-count checks.
Later captures supersede those first-pass results. The current package summary determines the delivered set.
The script packages only explicitly selected folders and shows synchronized pairs only when both captures are selected.
The ladder comparison now selects `ladder-complete-short` as its after capture.
The earlier `ladder-final-short` contains superseded defects and must not serve as the final result.
Current additional coverage includes the tall route, blocked top and bottom, interruption, and target-loss captures named above.

Preserve these unsuccessful or superseded attempts as diagnosis, not additional passing evidence:

- `directions-reviewed`: the character fell off the review support floor.
- `chest-approach`: the 0.55 m farther start selected no action because of the selection limit.
- `deer-stop-after`: its experiment failed horizontal error, 0.09227 m against a requirement below 0.02 m.
- `chest-relative-blend`: runtime surface data was empty; both hands received the right marker.
- Earlier `chest-baseline`, `deer-baseline`, `jump-originals`, `jump-originals-follow`, `run-jump-baseline`, and `walk-before`: superseded attempts with individual capture limits.

The deer failure remains in `test-34446ce13021455ea40c4d7eee3a4c8a.json`.
Earlier focused passes included 48 foot/pose tests and a 110-test combined run.
These historical passes precede the latest shared changes and are not the final regression result.
The [catalog review](../../local-only/animation-review/catalog-review.md) and imported `SOURCE.md` files retain authored provenance and dependency mappings.

## Current validation checkpoint

The latest focused run passed 256 tests.
A broader 362-test run found ten fixed-ledge failures after relative blending changed active world anchors.
That regression has been fixed. The final broad run passes 363 tests with zero failures.
The final `ActorLadderTests` run passes 16 tests, including blocked-bottom recovery and missing, empty, and mismatched motion assets.
The production adapter rejects invalid authored motion before approach. Valid configured ladder behavior is unchanged.
A fresh 49-test chest fixture run also passed after the current-pose hook.

Current build counts are Core 2 warnings, Planet 19 warnings, and Editor 62 warnings. All three have zero errors.
These warnings include an analyzer/compiler version mismatch. Counts do not prove that all warnings are new.
The final graph checkpoint contains 15,930 nodes and 22,851 edges. The update completed successfully.
The final graph update log is `local-only/animation-review/session-2026-09-13/graphify-final.log`.

Packaging Python compilation and synthetic index checks pass. The index escapes labels, filters incomplete pairs, and does not autoplay.
Browser playback controls and full normal-speed viewing remain unverified.
The delivered index contains 33 selected captures. Every encoded video passed its frame-count check.
The root reviewer inspected consecutive final short/tall ladder mount and exit frames, plus stance interruption frames.
These samples preserve the outgoing motion through state changes. They do not establish complete normal-speed quality.
The review scene is saved in Edit Mode with both ladder fixtures.
Repository-wide whitespace checks report existing lines in `CreatureLibrary.asset` and `PlanetDto.cs`; unrelated changes remain intact.
The next review covers full playback, artistic acceptance, and the open coverage listed above.
