# Sidekick character validation queue

Status: Trial setup and targeted validation executed on 2026-09-10 after Bryan transferred Unity ownership. Bryan accepted the scene's initial look; final outfit and garment compatibility remain open.
The execution record below supersedes the original queued rows. No automatic recurring job exists.
Updated 2026-09-10: Bryan authorized the reversible trial and comparison scene. Queue all implementation changes as well as tests.

Design: [Sidekick character swap](../docs/design/2026-09-09-sidekick-character-swap.md).
Baseline: `harvest-vertical-slice`, `d1e0f624ead0448f70a867f20a9389e367527293`, plus dirty working-tree changes, 2026-09-09.

## Handoff prerequisites

1. Confirm the current owner has released Unity before any Editor operation.
2. Use Bryan's 2026-09-10 trial authorization and reread the revised design against current source.
3. Record the active project, scene, play state, unsaved changes, branch, commit, and Unity version.
4. Coordinate shared authoring and test files before changing them.
5. Capture and archive the baseline before importing the candidate.

Do not open a second Editor on this project or switch the active Editor to scratch.
Do not save or discard another agent's scene changes. Resolve their ownership first.
Run tests serially after successful import and compilation. A stale assembly cannot validate changed source.

## Execution record — 2026-09-10

See [trial results](../docs/research/2026-09-10-sidekick-trial-results.md) for source paths, observations, and evidence.

Body-part follow-up: `SidekickBodyReview.unity` now contains bare and hybrid Sidekick candidates plus the original POLYGON actor.
The hybrid uses five converted torso/arm sections. Each now has baked muscular, heavy, skinny, and feminine frames.
The transfer/control follow-up passed five tests in job `4c9454e291ab47979d6d36c8e51b0fe2`; new endpoint visual approval remains open.
Targeted follow-up job `2915c266be714fbfa3fe42cb5d5f1a53` passed 77 tests, including both new prefab fixtures.

| Rows | Result |
|---|---|
| S00–S05 | Baseline saved; candidate imported and bound; 132 targeted tests passed, including both character fixtures |
| S06–S10 | Existing pose regressions passed; 13-clip fitting sweep and movement/water smoke executed; full station-by-station visual acceptance remains open |
| S11 | Materials rendered and source shader mapping inspected; optional vendor effects remain omitted by design |
| S12 | Sword, shield, hat, cape, and legacy torso assessed; limitations recorded instead of blanket compatibility claims |
| S13 | Existing gravity/contact and camera fixtures passed; full interactive planet comparison remains unrun |
| S14 | Repeated fresh Play-mode sessions initialized without new inspected runtime errors |
| S15 | Mesh and renderer counts recorded; controlled CPU/GPU comparison not run |
| S16, S20 | Bryan accepted the initial comparison scene's look on 2026-09-10; individual accessory acceptance and final outfit remain open |
| S17 | Not executed; original gameplay default retained |
| S18–S19 | Comparison scene built; fitting transforms saved; playback/re-enable control test passed |
| S21 | Original prefab and scene retained; original character fixture passed and comparison actor animates |

## Execution queue

Implement in this order after handoff:

1. Import the selected Sidekick art and connect the existing rig and animations.
2. Validate candidate binding, movement, contacts, and animation regressions.
3. Build `Assets/Scenes/Tests/SidekickStyleReview.unity` with both characters, deer, wolves, accessories, and building samples.
4. Run the fitting and style comparisons below and record item-level findings.
5. Present the scene and evidence to Bryan before changing the gameplay default.

All rows are QUEUED. Candidate-dependent rows require Phase 2 implementation first.

| ID | Check | Pass condition |
|---|---|---|
| S00 | Baseline | Record current prefab, clip assignments, settings, captures, and existing failures |
| S01 | Dependency and import check | All selected references resolve; no missing scripts or new import/compile errors |
| S02 | Avatar and rig binding | Avatar is valid and Humanoid; all required chains bind; scale is finite and positive |
| S03 | Existing regressions | Nonzero discovered test count; zero new failures; record skipped and pre-existing failures separately |
| S04 | Candidate-specific rig tests | Explicitly instantiate Sidekick; verify initialization, accessible bones, and teardown without exceptions |
| S05 | Root ownership | Candidate animation does not move the authority root outside the existing authorized movement path |
| S06 | Ground locomotion | Idle, walk, run, diagonals, reversal, crouch, and stairs preserve behavior and valid contact correction |
| S07 | Crawl | Entry, exit, forward, backward, and sideways cycles maintain support without limb stretching |
| S08 | Water | Entry, exit, surface, underwater, directional, and fast swimming preserve state transitions and usable head height |
| S09 | Traversal | Jump, fall, vault, hang, pull-up, and release preserve collision and existing contact rules |
| S10 | Hands and gaze | Reach and release stay within current limits; palm and finger mapping remain valid |
| S11 | Materials and bounds | No missing materials, unintended transparency, or disappearing body parts during motion |
| S12 | Accessory and clothing assessment | Test representative weapons/tools, rigid attachments, fitted clothing, and a long garment; record fit and style verdicts separately |
| S13 | Gravity and camera | Repeat representative movement with changed up direction; camera framing and controls remain usable |
| S14 | Runtime lifecycle | Three fresh start/stop cycles initialize and dispose without new errors |
| S15 | Cost comparison | Record renderer, material, mesh, and CPU/GPU cost changes under identical conditions; explain regressions before acceptance |
| S16 | Visual review | Bryan reviews matched captures and accepts the chosen preset and contact appearance |
| S17 | Final default | Accepted reference survives scene authoring/reload; initialization and movement smoke check pass |
| S18 | Comparison scene | Sidekick, original POLYGON character, project deer and wolf, and building samples appear under common lighting with recorded source paths |
| S19 | Repeatable fitting | Named objects or existing controls allow repeated accessory selection; saved transforms survive reload without altering the baseline character |
| S20 | Style coherence | Bryan reviews close, third-person, and wider views for palette, proportions, detail density, shading, and building scale |
| S21 | Restoration | Original character still initializes and animates after the trial; restoring it does not depend on Sidekick-only assets |

S18–S21 precede the S16 decision and any S17 default change. Row numbers are identifiers, not execution order.

## Item assessment record

Create one row per tested asset with these fields:

- Exact source and project paths, item name, and target body preset.
- Fit verdict: easy fit, modification required, unsuitable for this trial, or untested.
- Style verdict: fits, adjustment required, conflicts, or unreviewed.
- Attachment bone, saved transform, tested motions, and capture paths.
- Observed defects, required modifications, measured fitting time, and separately labeled remaining effort estimate.

Include garment deformation checks during walking, reaching, crouching, and crawling where relevant.
Do not infer a complete wardrobe result from one garment. Do not label untested items incompatible.
Keep original animal and building materials for baseline views. Record any later material adaptation separately.

## Targeted fixtures

Recheck these existing fixture names before running:

- `HumanoidRigBindingTests`
- `HumanoidAnimationTests`
- `HumanoidContactIntegrationTests`
- `HumanoidSwimmingTests`
- `HumanoidStairAnimationTests`
- `ProceduralPoseTests`
- `FootStepContinuityTests`
- `ActorTraversalTests`
- `ActorThirdPersonCameraTests`

Extend relevant model-dependent cases to cover both prefabs. These proposed Sidekick cases do not exist yet.
Use existing fixture tolerances. Record any new numeric thresholds before collecting candidate results.
Avoid unrelated broad suites unless shared behavior changes or a failure requires them.

## Evidence and result record

Store logs, test results, captures, and measurements under `local-only/actor-performance/<execution-date>/sidekick-swap/`.
Archive matching baseline and candidate captures before later capture runs can prune them.
Record camera pose, lighting, quality, actor count, rig scale, clip, input sequence, timestep, and observation duration.
Use the same conditions for both characters. Record planet seed when testing on the planet.

For each row, append date, status, revision, test count or observation duration, evidence paths, and exact failures.
Keep numerical results separate from Bryan's visual verdict. Leave unrun rows QUEUED.
Restore the handed-off Editor state when practical and document intentional changes.

## Full-body follow-up — executed 2026-09-10

Created `SidekickFullBodyReview.unity` with separate converted hips, legs/boots, and gloves. Preserved the upper-body baseline. All 104 tests passed; Core, Planet, and Editor builds passed. Checked four shape endpoints and walking, running, crouching, crawling, and swimming captures. No missing scene references or console errors remained. Visual acceptance, skin colour, seams, and coupled glove fingers remain review items. See the trial results for evidence paths.

## Skin and second-outfit follow-up — executed 2026-09-10

Matched exposed POLYGON skin to the Sidekick head while preserving clothing colours. Added `SidekickOutfitReview.unity` with Outfit 02 from the existing Fantasy Hero model. The same fitting algorithm generated its four body shapes. All 129 focused tests and Core, Planet, and Editor builds passed. Matched colour captures and shape/animation captures are recorded in the trial results. Different packs, loose cloth, coupled fingers, and close seam review remain open.

## Kingdom clothing follow-up — executed 2026-09-10

Created `SidekickKingdomReview.unity` with original and converted monk/peasant characters. Reused the fitting and shape-transfer algorithm. Added combined-mesh head removal and corrected the imported avatar's Hips mapping from Root to Hips. All 215 tests pass; Core, Planet, and Editor builds pass. Fresh Play has no missing references or console errors/warnings. Captures and the initial four reference-rig failures are recorded in the trial results. Robe dynamics/collision and final visual acceptance remain open.
