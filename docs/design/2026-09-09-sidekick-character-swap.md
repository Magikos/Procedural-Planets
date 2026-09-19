# Sidekick character swap

> Renamed since this was written (2026-09-18). The names below are the originals
> and stay as the record of what was done on the date in the filename. Current
> equivalents: `Assets/Art/Characters/SyntyHero/` is `Assets/Art/Characters/Human/`;
> `Sidekick*` types, scenes and menu paths are `Human*`; a baked `*_Sidekick.prefab`
> is `*_Fit.prefab`; our copies of source art drop the vendor prefix, so
> `SM_Chr_Rider_01.fbx` is `Rider_01.fbx` and `SM_Prop_Chest_01.fbx` is `Chest_01.fbx`.
> A bare `SM_*` or `SK_HUMN_*` still names a sub-mesh inside a source FBX and is
> unchanged. "Synty Sidekick Characters" is a pack name and is also unchanged.

## Active Tracker

Status: Trial character and both review scenes implemented on 2026-09-10 after Unity handoff. Technical checks passed. Bryan accepted the comparison scene's initial look on 2026-09-10; final outfit and garment compatibility remain open.

Current next action: Review the Fantasy Kingdom monk and peasant conversions in `SidekickKingdomReview.unity`. All 215 focused tests pass after correcting the imported Kingdom avatar's hips mapping. Bryan accepted the initial full-body trial on 2026-09-10. Cloth dynamics, collar detail, and coupled fingers remain review items. See [measured trial results](../research/2026-09-10-sidekick-trial-results.md).

- [x] Phase 1: Record baseline and verify one Sidekick candidate.
- [x] Phase 2: Adopt the candidate and connect existing animation presentation.
- [ ] Phase 3: Build the fitting and style scene; complete regression and comparison checks.
- [ ] Phase 4: Select the accepted character as the default and record adoption.

## Scope and baseline

Prepared 2026-09-09 on `harvest-vertical-slice`, commit `d1e0f624ead0448f70a867f20a9389e367527293`, with extensive uncommitted work.
Recheck current actor code after the other agent finishes. That work can change this baseline.

Trial one exported Sidekick human alongside the current POLYGON character. Preserve movement, gravity, animation clips, traversal, and gameplay authority.
Use `Starter_01` as the technical candidate. Its appearance is provisional; Bryan can select another preset before final adoption.
The trial includes accessory fitting and representative legacy clothing assessments. Full wardrobe conversion remains outside this trial.
Character creation, gameplay equipment switching, and inventory integration remain separate features.
Keep the current gameplay default until Bryan chooses between the characters using the comparison evidence.

Validation is recorded in [the test queue](../../plans/2026-09-09-sidekick-character-validation-queue.md).
This document does not schedule automatic execution or reserve the Editor.

## Verified integration points

| Location | Current behavior | Planned use |
|---|---|---|
| `Assets/Editor/SyntyHumanoidAuthor.cs` | Imports Fantasy Hero art and writes `Assets/Art/Characters/SyntyHero/SyntyHero.prefab` | Reuse its project material and controller-free prefab conventions |
| `Assets/Editor/HumanoidReviewAuthor.cs` | Assigns `SyntyHumanoidAuthor.PrefabPath` directly | Allow explicit selection of the candidate while retaining the baseline |
| `Assets/Scripts/Planet/Character/HumanoidAnimationPrototype.cs` | Instantiates `CharacterPrefab` and connects existing clips and procedural rig | Reuse this presentation entry point |
| `Assets/Scripts/Game/Animation/HumanoidRigBinding.cs` | Requires a valid Humanoid avatar and resolves standard bones | Verify candidate binding without bone-name substitutions |
| `Assets/Scenes/Tests/HumanoidAnimationReview.unity` | Existing humanoid review scene | Use the same stations and conditions for comparisons |

The source candidate exists at `D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/`.
It contains a prefab, mesh asset, avatar asset, material, texture, and `.sk` authoring file.
File inspection does not establish avatar validity, shader compatibility, or successful animation playback.

## Phase 1: Baseline and dependency verification

1. Obtain Editor ownership. Record the scene, play state, dirty scene state, Unity version, and current source revision.
2. Record the old character's assigned clips, movement settings, scale, camera framing, and existing test results.
3. Capture the old character at the review stations before changing assets.
4. Inspect the candidate's serialized dependencies, source GUIDs, avatar, renderers, material slots, and required bones.
5. Resolve each dependency to an existing project asset or a selected source file.
6. Confirm the candidate needs no vendor behavior scripts in the shipped project.

Exit: A dependency manifest and archived baseline exist. Unknown dependencies block import until resolved.

## Phase 2: Adopt one exported character

Destination: `Assets/Art/Characters/Human/`, with final prefab `Human.prefab`.
Copy only required art and metadata. Preserve referenced source GUIDs after checking for collisions.
Keep Sidekick creator tooling in scratch. Keep source `.sk` files there unless a documented authoring need requires a project copy.

Adapt materials through the existing project shader convention. Verify color maps, transparency, skin, eyes, and hair before accepting the conversion.
Do not assume every Sidekick material can use the current hero material unchanged.

Configure a valid Humanoid avatar, accessible bones, no runtime Animator controller, disabled root motion, and disabled animation events.
Bind through `HumanoidRigBinding` and play the existing clips through the existing animation view.
Use a project-owned prefab wrapper if the exported hierarchy needs one. Do not rename bones to match the previous model.

Extend the existing review author with explicit character selection. Keep its current menu entry and current default until acceptance.
Do not duplicate the review scene generator, clip loading, or procedural binding logic.
The existing Fantasy Hero mesh-selection list is pack-specific; do not copy it into a second modular-part selector.
Extract a small shared authoring helper only where both actual import paths need identical behavior.

Keep existing clips at their current paths during this swap. Moving them adds unrelated reference risk.
Inspect crawl distance measurements, traversal motion profiles, contact offsets, swim heights, and camera focus against the new proportions.
Change only demonstrated mismatches. Keep model measurements in presentation unless a deliberate gameplay size change is required.

Exit: The candidate imports without new errors and initializes with the existing animation and movement systems.

## Phase 3: Regression and appearance review

Execute the linked queue in order. Reuse current test fixtures and parameterize relevant model-dependent cases for both prefabs.
Do not claim Sidekick coverage from tests that still load only `SyntyHero.prefab`.
Keep procedural corrections within existing anatomy limits; investigate bad mapping or unsuitable clips before increasing correction limits.

After animation validation, build the dedicated comparison scene described below.
Record exact source paths, fitted transforms, and observed defects for each accessory. Findings apply to tested assets only.

Exit: Automated checks pass, runtime evidence covers the listed movements, and Bryan accepts the appearance.
Gate: Obtain Bryan's visual verdict on the concrete comparison before making the candidate the default.

## Phase 4: Default selection and handoff

Change the intended character reference after acceptance. Search current consumers before changing scene or prefab assignments.
Verify scene regeneration preserves the accepted selection. Repeat initialization and one movement smoke check after the reference change.
Retain the old asset while it remains a comparison fixture or referenced dependency. Do not remove shared animation assets.
Record source files, dependencies, shader adaptation, and evidence in `SOURCE.md` and the existing asset adoption map.
Run `graphify update .` after implementation changes code.

Recovery: Restore only this task's character assignments if validation fails. Preserve unrelated changes and the old working character.

## Dedicated fitting and style scene — authorized 2026-09-10

Implemented scene: `Assets/Scenes/Tests/SidekickStyleReview.unity`. The separate motor scene is `Assets/Scenes/Tests/SidekickAnimationReview.unity`.
Reuse existing animation playback, character binding, and review authoring helpers. Do not build a second animation system or gameplay inventory.

Build the following comparison areas:

| Area | Contents | Purpose |
|---|---|---|
| Character comparison | Animated Sidekick and the current POLYGON character, side by side | Compare proportions, detail, silhouette, and movement |
| Accessory fitting | Representative owned weapons, tools, rigid attachments, fitted clothing, and a long garment | Establish actual fitting and deformation effort |
| Animal comparison | Current project deer and wolf prefabs, with their existing materials and animation where available | Compare character and animal style at gameplay distance |
| Building context | A small selection of owned wall, doorway, roof, and prop pieces | Compare scale, surface detail, palette, and silhouette in context |

Find and reuse project assets before importing comparison assets from scratch.
Resolve exact deer, wolf, and building paths during implementation; do not substitute unverified package names.
Record all selected source paths in the scene manifest.
Use common lighting, project materials, fixed comparison cameras, and a scale reference.
Include close, normal third-person, and wider context views. Avoid separate lighting that conceals a mismatch between assets.
Keep the original animal and building appearance as the baseline before trying any material adjustment.

Use named accessory objects or existing Inspector controls for selection and visibility.
The scene must support repeated fitting and animation review without rebuilding the scene for each item.
Keep both character roots independent so attachments cannot accidentally modify the baseline character.
Save fitting transforms per tested item. Check rigid attachments and deforming garments through relevant motion, not only the rest pose.

Record two independent verdicts for each item:

| Dimension | Categories | Evidence |
|---|---|---|
| Technical fit | Easy fit; modification required; unsuitable for this trial; untested | Transform-only fit or specific mesh, rig, skin-weight, and deformation findings |
| Style | Fits; adjustment required; conflicts; unreviewed | Matched views showing proportions, palette, detail density, and shading |

An easy fit uses placement, rotation, scale, or an existing material assignment without geometry or skin-weight edits.
A modification verdict names the required work and separates observed defects from estimated repair effort.
An unsuitable verdict identifies the blocker or disproportionate effort. It does not claim that conversion is technically impossible.
Keep untested assets unclassified. Record time spent on representative fitting work to replace the earlier general estimates.

Bryan reviews the scene and chooses Sidekick, targeted additional trials, or the original POLYGON character.
Keep a tested restoration path to the original character. The trial remains useful evidence even if Sidekick is rejected.

## Equipment boundary

| Asset | Adoption rule |
|---|---|
| Separate weapons and tools | Attach to the appropriate bone; validate grip, scale, and intersections |
| Rigid hats and backpacks | Fit individually; reject shapes that intersect the selected body |
| Sidekick clothing | Use supported combinations from the Sidekick authoring system |
| Legacy Fantasy Hero clothing | Inspect representative pieces in the fitting scene; classify required changes before attempting broad conversion |
| Capes and deforming garments | Verify rigging and motion separately; a model swap does not supply cloth simulation |

Synty states that Fantasy Hero Modular Characters is incompatible with Sidekick.
Some Sidekick parts also have species or part restrictions. Reference: https://syntystore.com/community/faq (checked 2026-09-09).

## Effort and open risks

Allow a few hours for the first imported, animated candidate. Allow roughly 1–3 working days for the validated swap.
These were general planning estimates, not measured asset-conversion costs. They exclude the expanded comparison scene and wardrobe assessment.
Replace them with measured trial effort. Invalid avatars, material incompatibility, or contact defects can extend the trial.

Unresolved checks include avatar validity, exact dependency closure, final preset choice, contact behavior, and measured rendering cost.
The initial scope uses one fixed human body. Extreme body proportions and runtime customization require separate validation.

Accessory trial update (2026-09-10): `SidekickAccessoryReview.unity` now compares hats, capes, backpacks, and belt pouches on the original and Sidekick rigs. See the trial results for inspected poses. Rigid mounts need body-shape-dependent offsets. Cape motion and collision remain unimplemented. The monk's front-panel overlap also occurs on its original rig; temporary weight edits did not produce an accepted fix.
