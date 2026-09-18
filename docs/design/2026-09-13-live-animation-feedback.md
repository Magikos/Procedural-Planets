# Live animation feedback — hop, ledge, chest, and crate

Status: implemented; new visual review pending. Previously accepted ladders, doors, and running jumps remain accepted.

## Changes and acceptance checks

| Action | Reported defect | Change | Evidence |
|---|---|---|---|
| Standing jump | Large jump instead of a hop | Separate authored stationary performance and 0.30 m motor setting | Complete before/after, source and production views. Observed root rise above initial support: approximately 0.28 m versus 0.72 m before. |
| Ledge grab | Jump could not reach the suspended platform | Search the reachable face band, including thin platforms; retain body-path collision checks | Complete jump-to-hang at a 2.55 m platform with a 0.20 m face. W/Space climb; S/Ctrl drop. |
| Chest | Repeated collection motion while inspecting | Enter and hold the authored bent pose; collection remains a separate one-shot | Complete 18-second before/after, including opening, extended inspection, and closing. |
| Crate | Prop lagged during movement and turning | Transport an acquired crate with the actor frame; retain acquisition/placement smoothing | Same before/after held-turn recipe. Maximum frame-relative translation step during motion: 2.33 cm before, zero after. A 120-degree turn regression also passes. |

## Controls and behavior

W and S act on a fresh directional press while hanging. W held during approach does not automatically climb after acquisition.
Space and Ctrl remain available. Releasing a ledge retains the existing recatch suppression.
Thin-platform acquisition checks standing-body clearance along the full traversal route. The reachable traversal ceiling is 2.60 m.
Ordinary stationary jumps select the new standing performance. Running jumps retain their existing performance and landing selection.

## Source and reproduction

The standing source is `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/Traversal_Movement_Jump_InPlace_WholeSequence.fbx`.
The project retains the imported source and an editable `Standing Hop.anim` variant. The motor replaces the source's airborne root arch.
The source comparison uses the production avatar and native source clock. It does not isolate avatar retargeting errors.
The chest inspection pose comes from the existing grab-item source at normalized 0.20. It is held without replaying collection.
All timed captures use 60 Hz simulation and 30 Hz frame output. Each capture stores its input and source metadata.
The matched ledge baseline uses the preserved original jump/view/traversal code and confirms no catch. Final source files were restored byte-for-byte.
The ledge fixture matches the short ladder platform height and thickness. It isolates the platform from the ladder interaction.
The crate capture injects acquisition after a hidden reset to isolate the reported held-turn defect. It does not validate the full live pickup route.

## Checks and limits

- 110 selected EditMode tests passed. They cover ledge catch, traversal, jump response, interaction sequences, and prop continuity.
- The final Editor code-health build passed: zero errors, 90 warnings. See the [local review bundle](../../local-only/animation-review/session-2026-09-13/live-feedback-review.md).
- The initial thin-platform test failed because probe spacing missed the face. Smaller spacing fixed both ascending and descending cases.
- W, S, Space, and Ctrl each completed their recorded ledge route. A running-jump continuation replay also completed.
- Agent inspection covered consecutive frames around hop flight/landing, chest hold/recovery, crate turns, and ledge transitions.
- The agent did not inspect normal-speed video playback. Timing data and timestamped frames support the findings.
- Crate source A is unavailable in this isolated held-turn capture. B omits the cloned prop; C shows the production prop.
- Ledge source A shows the pull-up source only. It does not represent the complete authored jump-grab-drop sequence.
- Exact finger wrap at the ledge, all approach angles, arbitrary actor scales, and every obstruction remain unreviewed.
- The held chest pose has no repeating search cycle. Its naturalness needs Bryan's review.
- No claim covers collisions between a carried crate and unrelated room furniture.

The broader animation continuity audit remains open. The accepted earlier set is preserved in `index-approved-set.html`.


## Revision 2 — grounded preparation, active inspection, authored lift

Bryan rejected revision 1 at JUMP-02 frame 20, CHEST-02 frame 284, and CARRY-01 frame 179.
This section supersedes the held chest pose and injected carry review above.

- Grounded ledge jumps now use the complete owned jump-to-hang clip. The actor remains on the ground during its preparation.
  The existing traversal motion asset carries the sampled body envelope and travel. Reactive airborne catches keep their short catch route.
- Chest inspection uses an editable animation variant. The bent grab pose supplies the stance.
  `HumanM@Idle01_Break01` supplies head, neck, and torso variation. The look controller yields during inspection.
  Collection remains a separate one-shot action.
- Crate pickup uses the source grip at normalized 0.35 and its native 1.133-second duration.
  The box follows the authored palm midpoint during lifting. The front-surface anchors match the narrower carried grip.
  Existing actor-frame carrying takes over after the lift. The review invokes pickup and placement through `Interact()`.
- Traversal action weights retain outgoing clips when another action interrupts a blend.

Review recipes: `TraversalQualityReviewCapture`, `HumanoidQualityReviewCapture`, and `CrateQualityReviewCapture`.
The local focused queue retains revision 1 alongside revision 2. Intermediate candidates are excluded from that queue.

Source imports remain editable and retain independent source references. The new HumanM model is the source pack's visible character.
Chest A shows the original standing idle. Runtime B/C show its variation combined with the bent stance.
Ledge A shows jump-to-hang only. Crate A shows pickup only. Both hold their final source pose during later runtime actions.
These limits are explicit; neither A represents the complete runtime action sequence.

Remaining work: the crate placement still lowers the box after the actor starts bending.
Its lowering phase needs a separate authored contact pass. Exact fingers, arbitrary actor scales, and other obstruction cases remain unreviewed.
The broader continuity audit remains open. These revisions await Bryan's visual approval.

The B comparison renderer now forces skin matrix updates during paused capture. A rendered mismatch exposed stale skinning despite matching bone sampling. All revision 2 comparisons were regenerated after this fix. Archival B views may retain stale poses.


## Feedback pass 3 — quick dip and supported inspection

Bryan requested a quick standing-hop dip, supported inspection hands, and a less robotic carried load.
The previous focused page is preserved as `feedback2-index.html` in the local review directory.

- The standing hop now has 0.18 seconds of grounded preparation. Its source preparation spans 14 frames at 30 Hz.
  Movement, loss of support, traversal, and incompatible stance cancel the preparation. The ordinary motor and moving jumps keep their existing launch behavior.
  Runtime capture records ten grounded preparation frames at 60 Hz. Moving during preparation produces no airborne frame.
- Chest inspection selects the source's normalized 0.31 pose, which places both hands near the rim.
  The editable idle retains its original planar body position. Previously, root extraction shifted the idle away from the source's contact pose.
  Two fixed rim contacts support the hands while the authored head and torso idle continues.
  Maximum measured corrections during seconds 7–12 are 2.56 cm and 5.05 cm. Both hands retain full contact weight.
- Carried two-hand objects now follow the authored hip translation before hand contact solving.
  This uses the existing prop and contact update path. It adds no oscillation or competing body-pose writer.
  The compared walking interval has 5.06 cm of body-relative vertical load travel, versus zero before.
  Two owned Crafter carrying-walk candidates did not supply a better visible loaded posture. They were not adopted; scratch sources remain available.

The crate change is incremental. A stronger sense of heavy weight and the previously flagged placement timing remain open.
No random chest variant was added; this pass uses the user's supported two-hand rim option.
All changes await visual approval. The broader continuity audit remains open.


## Feedback pass 4 — supported chest idle (2026-09-14)

Bryan rejected CHEST-02 revision 3 (`chest-rim-final-v9`) at frame 132 / 4.400 seconds.
The hands needed resting orientation. Head motion did not provide enough body variation.
Bryan deferred CARRY-01 revision 3 (`crate-weight-final-v9`) despite its unfinished weight and placement. This is not visual approval.
The standing-hop revision remains unchanged and awaits approval; silence is not sign-off.

Acceptance: palms rest visibly on the actual open rim, body motion remains restrained, feet stay supported, and entry/release blend.
The first pass rotated palm targets but exposed a geometry error: the old rim targets used the closed lid height.
That position was about 15 cm above the body rim. Earlier low marker errors did not prove surface contact.
The author now projects the contacts onto the body mesh through the existing surface projection code.
It selects the deeper 0.26 normalized source pose, adds an editable wrist extension, and retains existing solver limits.
The idle includes restrained source torso, leg, and body-position variation. Fingers still use the existing contact path.
No runtime pose writer or new animation dependency was added. The crate runtime was not changed.

Current review: CHEST-02 revision 4, `chest-rest-final-v13`, 540 frames / 18 seconds, side and rear views.
Previous comparison: `chest-rim-final-v9`. Recipe: `HumanoidQualityReviewCapture.Capture(directory, "chest-search")`, 60 Hz simulation / 30 Hz capture.
Source A uses the native idle at 1x. B/C show the editable bent composite. Neutral source shading is now unlit so fixture light range cannot hide A.
The source capture enables offscreen skin updates. Other source clocks and runtime timing remain unchanged.

Evidence lives in `local-only/animation-review/session-2026-09-13/`:
- `chest-rest-final-v13/metadata.json` records inputs, phases, source/import hashes, cameras, contact positions, and feet.
- `chest-rest-final-v13/support-motion-metrics.json` records contact rotation and body motion over the settled idle.
- `chest-rest-final-v13/whole-action.jpg`, `entry-sequence.jpg`, `idle-sequence.jpg`, and `release-sequence.jpg` support frame inspection.
- `feedback3-index.html` preserves the preceding three-action review. The active queue contains only the revised chest.

From 4.4–12 seconds, both hands retain full contact weight. Maximum contact errors are 0.0011 mm left and 0.0009 mm right.
Maximum positional corrections are 3.58 cm left and 4.48 cm right. The separate replay records zero contact rotation error.
Relative to the first sampled idle pose, hips vary by 3.82 degrees, spine by 2.54, chest by 3.95, and thighs by 1.66–1.94.
Each sole moves less than 2 mm horizontally across the measured idle. Early acquisition has larger residuals while the hands blend into support.
An idle cancellation check releases the session and both targets within two seconds. That check does not certify every interruption.

The code-health build passes with 0 errors and 69 warnings. Complete-action samples and consecutive entry/idle/release frames were inspected.
Normal-speed playback was not independently inspected. Arbitrary chest geometry/scales, random support variants, and the wider interruption matrix remain unreviewed.
The revised chest awaits Bryan's visual approval. The broader continuity audit remains open.

The animation quality skill now covers supported body idle, phase-specific support geometry, source-root extraction, anticipation clocks, and deferred review status.
Its frontmatter and routing scope remain unchanged. The skill validator and local-link checks pass.

Feedback pass 4 regression result: 113/113 EditMode tests pass (`SidekickInteractionReviewTests`, `HumanoidPerformanceTests`, `ProceduralPoseTests`), job `6938e17e566940ba88554588bdbecd3d`.

## Chest approval — 2026-09-14

Bryan approved CHEST-02 revision 4 (`chest-rest-final-v13`): "This looks good."
This supersedes the pending chest approval above. Preserve this baseline. CARRY-01 remains deferred; the broader audit remains open.
