# Animation review: Spore and Overgrowth

Date: 2026-09-12.
Project state: `harvest-vertical-slice`, dirty working tree above `d1e0f624ead0448f70a867f20a9389e367527293`.
Scope: create the project review skill and inspect the two requested references. No runtime animation changes or package installations.

## Result

The reusable procedure is [pp-animation-quality-review](../../.agent-skills/pp-animation-quality-review/SKILL.md).
The project skill router and generated Claude discovery stub expose it to future work.
It covers humanoid and creature motion, locomotion, interactions, carrying, and visible transitions.

The main recommendation is to preserve authored intent and inspect where adaptation changes it.
Neither reference establishes an automatic score for natural motion.
Both provide ideas for controlled adaptation. Spore also describes a concrete human review process.

## Sources inspected

- Hecker, Raabe, Enslow, DeWeese, Maynard, and van Prooijen: *Real-time Motion Retargeting to Highly Varied User-Created Morphologies*.
  Read the supplied [11-page PDF](../../local-only/Sporeanim-siggraph08.pdf), including its limitations and references.
  Rendered and inspected PDF pages 4 and 10, containing Figure 2 and the validation grid in Figure 9.
- [Overgrowth repository](https://github.com/WolfireGames/overgrowth), pinned to `245fe4828631c84c0023d29d1525f5716ccb6106` for this review.
  Read selected animation interpolation, transition, stance, and native foot-IK paths. This was not a full repository audit.
  Source inspection does not establish the visual quality of a running Overgrowth build.
- Local evidence directory: `local-only/animation-review/research/`. It contains extracted paper text, two rendered pages, the remote tree, and selected source files.
  These local files are not a portable dependency for the skill. The findings and source links below remain usable without them.

## Ranked applications

| Priority | Recommendation for this project | Existing integration point | Evidence to seek |
|---|---|---|---|
| 1 | Compare original motion, production retargeting without corrections, and the final result | `Assets/Editor/SidekickInteractionComparison.cs` | Locate the first stage that introduces each defect; preserve natural-rate source playback |
| 2 | Record contact meaning, coordinate frame, interval, and permitted adaptation | `Assets/Scripts/Planet/Character/SidekickInteractionReview.cs`; shared interaction definitions and contact markers | Grip remains on the intended surface while the source arc and release stay recognizable |
| 3 | Review an action across representative rigs and difficult placements | Existing validation scenario record; `Assets/Editor/CreatureAnimationReviewAuthor.cs` | Separate failures by rig, action, target placement, and transition |
| 4 | Diagnose gait phase and contact before changing the foot solver | `Assets/Scripts/Game/Animation/FootPlacementSolver.cs`; `HumanoidAnimationView.cs` in the same directory | Authored swing survives terrain adaptation; support-relative drift remains acceptable |
| 5 | Inspect interpolation and transition intervals, not only key poses | `Assets/Scripts/Game/Animation/ActorPerformancePlayback.cs`; `ActorAnimationGraph.cs` | No unintended overshoot, frozen interval, contact break, or delayed recovery |
| 6 | Keep secondary motion subordinate to the action | `Assets/Scripts/Game/Animation/ProceduralPoseRig.cs` and relevant creature presentation | Extra motion does not displace a grip, destabilize support, or overwrite the primary pose |

These are application recommendations, not claims that new runtime features were implemented.

## Spore: useful findings

### Authored semantics and relative motion

Sections 3.1 and 3.2 ask animators to identify the affected body parts and the meaning of their movement.
Section 3.2.2 supports rest-relative, size-relative, ground-relative, and target-relative movement.
Figure 2 shows that a target can move the reaching pose while leaving the resting pose unchanged.
Section 3.2.3 blends between movement frames, including an external target and an internal body target.

Application: record which parts of an interaction may adapt and when.
A book grip should use the book's contact frame. A planted foot should use its support frame.
The reach should retain the source motion outside the interval that needs adaptation.
This is guidance for reviewing the current contact system, not a proposal to replace clips with generalized Spore curves.

### Shared preview and varied characters

Section 3.4 previews several characters through the same animation data and uses the game's playback code.
Section 5 describes testers running animations across changing character examples and recording failures in a validation grid.
Failures include code defects, art adjustments, and cases that require different animation handling.

Application: keep an action-by-rig matrix and use production playback for final comparisons.
Test representative proportions and target positions. Do not infer creature coverage from one Sidekick model.
Classify each defect before changing code or curves.

### Natural reach is more than endpoint accuracy

Section 4.3 distinguishes unreachable, conflicting, and implausible goals.
The last category can be geometrically reachable yet produce an unnatural pose.
Its solver uses spine and limb stages, with pose conditioning intended to improve naturalness.

Application: consider stance, limb direction, and whole-body pose before increasing hand correction.
The same reachable endpoint can produce several elbow configurations.
This supports testing approach direction and pole behavior; it does not justify porting Spore's solver.

### Secondary motion must not compete

Section 4.4 applies passive secondary motion to body subtrees outside the primary goals.
The authors report that an earlier system degraded authored motion by feeding secondary goals back into the solve.

Application: review secondary motion with and without its contribution.
Preserve the source action and contact before adding sway or follow-through.
For this project, the authored pose remains the reference. Spore's rest-pose conditioning is not a replacement for that reference.

### Limits that matter here

Section 5 explicitly excludes intra-character collision and identifies problems with volume-aware interactions.
It also reports unnatural twisting near solver singularities and imperfect anti-buckling behavior.
The paper does not solve our chest penetration problem by itself.
Its broad morphology problem is larger than our immediate need for restrained correction on authored rigs.

## Overgrowth: verified code findings

### Keyframes and interpolation

[`Animation::GetMatrices`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Source/Asset/Asset/animation.cpp#L1269) selects four neighboring keys and computes cubic interpolation weights for bone transforms.
The inspected path also blends keyed IK participation. Its IK target transform uses a two-key blend rather than the four-key bone interpolation.

Application: review the intervals between keys. Different interpolation rules can produce different paths despite matching endpoints.
Do not assume sparse keys or cubic interpolation alone produce polish. Do not replace Unity interpolation without a demonstrated defect.

### Transition state

[`AnimationClient::SetAnimation`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Source/Graphics/animationclient.cpp#L90) retains the outgoing reader in a fade collection.
[`FadeCollection`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Source/Graphics/animationclient.cpp#L282) advances outgoing playback and reduces its contribution.
It also contains an optional overshoot mode selected through a negative fade-speed input.

Application: inspect outgoing motion through transitions and interruptions.
Our shared graph and continuity rule already provide the correct reuse boundary.
Overgrowth's optional overshoot is not a default recommendation for contacts or landings.

### Authored foot motion and terrain correction

[`CDoFootIKImpl`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Source/Objects/movementobject.cpp#L2535) reads the unmodified animated foot position, applies weighted ground correction, and smooths ground height and orientation.
It retains an authored vertical component and reduces ground-normal rotation influence with foot height.
The script [`DoFootIK`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Data/Scripts/aschar.as#L15137) immediately dispatches to the native function.
The native implementation therefore supplied the evidence, rather than the unreachable script body below that dispatch.

Application: inspect foot swing and ground adaptation separately.
Measure support contacts without erasing authored lift or roll.
The exact constants and coordinate assumptions are specific to Overgrowth and should not be copied into our rigs.

### Explicit stance state

[`StartFootStance` and `HandleFootStance`](https://github.com/WolfireGames/overgrowth/blob/245fe4828631c84c0023d29d1525f5716ccb6106/Data/Scripts/aschar.as#L11726) maintain planted flags, step progress, and target positions.
This demonstrates explicit support state in that stance path; it does not prove that all Overgrowth locomotion uses this procedure.

Application: include support phase in diagnostic records and distinguish stance correction from ordinary gait playback.
Our authored locomotion preference remains unchanged.

## Skill changes derived from the references

- Added explicit preserved intent and contact coordinate frames.
- Added an action-by-rig coverage matrix for broad reviews.
- Added between-key inspection and secondary-motion ownership checks.
- Required production playback for the final preview.
- Preserved the distinction between the current ghost and an independent source reference.

## Validation and remaining work

The skill-creator validator returned `Skill is valid!`.
Local reference checks passed. Source and generated stub frontmatter match. The router contains 23 skills with the new entry in both tables.
An independent agent applied the skill to a hypothetical wolf stair-snap case with passing tests and only two screenshots.
It retained an unverified visual verdict, distinguished the overlay from source evidence, and rejected an unsupported global IK reduction.
That trial led to clarifications for optional metadata, root-output differences, timed capture evidence, and documentation-only validation.
The trial tests the review procedure, not the quality of rendered game animation.

The skill defines a procedure. It does not install new visual perception or implement an automatic animation judge.
The existing ghost remains available. A universal three-stage recorder and metric report remain future tooling work.
No live Unity run was needed for these documentation changes. No animation was certified or marked polished during this task.
The [broader continuity audit](../design/2026-09-12-animation-continuity-validation.md) remains open.
