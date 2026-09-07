# Deer animation preflight — 2026-09-05

Status: Unity preflight advanced after Bryan released the Editor. Rig binding and a native Playables smoke check passed. Contact authoring and full material validation remain open.
Imported the two selected FBXs and base-color texture with their source metadata into the proposed deer folder. No runtime scripts, vendor controllers, vendor prefabs, or scene changes were added.

Implementation and test queue: [Animation foundation](../design/2026-09-05-animation-foundation.md).

## Source and proposed import

Source root: `D:\Unity\Explore Assets\Assets\polyperfect\Low Poly Animated Animals`.
Proposed destination after preflight: `Assets/AssetPacks/PolyperfectAnimals/Deer/`.
This destination now contains the three selected content files and metadata.

| Source relative to pack root | Bytes | Disposition |
|---|---:|---|
| `Meshes/Animals/Deer/SKM_Deer_Rig.fbx` | 200896 | Initial rig/mesh candidate. |
| `Meshes/Animals/Deer/SKM_Deer_Animations.fbx` | 7822848 | Initial clip source. Preserve selected clip definitions when importing. |
| `Meshes/Animals/Deer/SKM_Deer_Animations_Eat.fbx` | 1724000 | Defer; eating is not required for the first proof. |
| `Textures/Animals/Deer_COL_1k.png` | Not measured | Base color dependency confirmed through GUID lookup. |
| `Textures/Noise_Grey.png` | Not measured | Vendor detail texture. Inspect necessity; do not import by default. |
| `Materials/Animals/Deer.mat` | Not measured | Reference only; author a project material after skinning validation. |
| `Prefabs/Animals/Deer.prefab` | Not measured | Reference only; includes a controller and MonoBehaviour. Reconstruct a clean presentation object. |

Core FBX payload: 8,023,744 bytes, excluding texture and metadata.
The animation FBX may also contain mesh data; Unity inspection must determine duplication and importer treatment.

SHA256 fingerprints:

- Rig: `1CA12F9E56BB733F60AC9049E6C82FA712FB9AF6E2250DBE7F153375E80A3DF2`.
- Main animation FBX: `4D1739B91E1D35E82C4CFD1E215C239A3D071B139751F2C1F88F2CF5E0B24779`.

## Import metadata findings

All three FBX metadata files specify `animationType: 2` (Generic).
The animation files specify `avatarSetup: 1`; compatibility with the separate rig still requires Unity verification.
Metadata reports `globalScale: 1`, `useFileScale: 1`, and `optimizeGameObjects: 0`.
These settings do not establish the rendered size or guarantee compatible root-motion behavior.

Rig GUID: `845f263f70642de4aa151ae55cb9cfb3`.
Main animation GUID: `f54bd5cd272be07468fbfe13f6ac1c6f`.
The vendor prefab uses the rig's Avatar and a separate Animator Controller.

The rig's recycle-name table includes `Root_M`, `Chest_M`, `Hip_L`, `Hip_R`, `Knee_L`, `Knee_R`, and `Ankle_L/R`.
It also includes `Scapula_L/R`, `Shoulder_L`, `Elbow_L/R`, and `Fingers_L/R`.
These are naming evidence, not verified parent-child chains or final IK bindings.

## Main clip definitions

Frame ranges below come from `SKM_Deer_Animations.fbx.meta`. They are source frames, not seconds.
Do not infer duration without verifying the source frame rate in Unity.

| Clip | First frame | Last frame | Loop enabled | First proof |
|---|---:|---:|---|---|
| `Deer_Idle_To_Walk` | 0 | 11 | No | Inspect later |
| `Deer_Walk` | 30 | 54 | Yes | Yes |
| `Deer_Walk_To_Idle` | 70 | 82 | No | Inspect later |
| `Deer_Idle_Breath` | 110 | 140 | Yes | Yes |
| `Deer_Idle` | 160 | 346 | Yes | Optional variation |
| `Deer_Idle_To_Run` | 360 | 371 | No | Inspect later |
| `Deer_Run` | 390 | 404 | Yes | Yes |
| `Deer_Run_To_Idle` | 420 | 432 | No | Inspect later |
| `Deer_Attack` | 450 | 504 | Yes | Exclude |
| `Deer_Death` | 530 | 554 | No | Yes |

The separate eating file defines looping `Deer_Eat`, frames 1–280.
Every inspected clip definition has `curves: []` and `events: []`.
This establishes absence of custom importer curves/events, not absence of animated bone curves inside the FBX.
We must author contact curves and marker metadata after scrubbing the actual motion.

## Material dependencies

`Deer.mat` uses the same texture GUID for `_BaseMap` and `_MainTex`:
`a0119e5a4bd01354f93e0d72086a060c` resolves to `Textures/Animals/Deer_COL_1k.png`.
Its detail albedo GUID `1ac33c305c9e040debe64274ffc22c0f` resolves to `Textures/Noise_Grey.png`.
The material enables `_DETAIL_MULX2` and `_SPECULAR_SETUP` and disables `MOTIONVECTORS`.
Do not transfer those choices automatically into the project material.

Preserve required clip split/import settings deliberately. Check source GUIDs against the target project before retaining metadata.
Rebind dependencies explicitly if new GUIDs are assigned. Never copy the vendor prefab as a dependency shortcut.

## Unverified requirements

- Actual limb hierarchy, bone lengths, sole offsets, and bend planes.
- Mesh bounds, forward axis, units, and root offset relative to the current capsule-based grounding convention.
- Clip frame rate, duration, skeleton-root translation, and stride speed.
- Cross-file Generic rig compatibility and correct Avatar setup.
- Hoof plant/release timing and contact behavior during transitions.
- Skinned rendering, shadows, depth, and motion vectors with a project material.
- Visual style and acceptable gait/IK result in the game.

Continue the remaining Q2 and Q3 checks before production playback implementation.

## Unity results after Editor release

Only ProceduralPlanets was connected. Inspection therefore used the minimal import and disposable preview scenes in this project, rather than the scratch Editor.
`Assets/Scenes/Planet.unity` remained active, clean, and outside Play mode after the checks.
No persistent animation graph, presenter, or material was created.

### Q1 — rig and clip binding

- Generic Avatar reports valid. The skinned mesh has 2,056 vertices and 41 referenced bones.
- Every inspected clip binding resolves against the separate rig. There were zero missing transform paths.
- The rig faces local +Z, with Y vertical, based on head and hoof positions at scale 1.
- Rear terminal toes sit at approximately Y=0; front terminal fingers sit at Y=0.0023 in the rest pose.
- `Root_M` rests at `(0, 1.1775, -0.4469)`. Ground the visual by its feet, not the capsule center convention.

Verified chains, with left/right counterparts:

- Rear: `Root_M/Hip_L/Knee_L/Ankle_L/Toes1_L/Toes2_L/Toes3_L/Toes4_L`.
- Front: `Root_M/Spine1_M/Chest_M/Scapula_L/Shoulder_L/Elbow_L/Wrist_L/Fingers_L/Fingers3_L/Fingers4_L`.
- Root prefix: `Main/DeformationSystem/`.

Rear leg IK must preserve the extra articulated segments. Do not map hip-to-terminal-toe directly onto an assumed two-bone chain.

All ten authored clips use 24 FPS. Walk lasts 1.0 s; run lasts 0.583334 s; idle breath lasts 1.25 s; death lasts 1.0 s.
The animation FBX also contains a mesh. Avoid creating a second rendered body from it.

### Q2 — motion sampling and Playables smoke check

Recorded 6,050 trajectory rows: ten clips, 121 samples each, and five tracked bones.
Local evidence: `local-only/animation-preflight/deer-contact-samples.csv`.
Tracked bones: `Root_M`, `Fingers4_L/R`, and `Toes4_L/R`.
These terminal bones are provisional contact probes. They are not calibrated hoof sole points.

| Clip | Root X range | Root Y range | Root Z range |
|---|---:|---:|---:|
| Walk | 0.0025 m | 0.0576 m | 0.0195 m |
| Run | 0 m | 0.2159 m | 0.0200 m |
| Death | 1.4282 m | 1.0280 m | 0.4787 m |

`averageSpeed` reports zero for all ten clips. This does not establish authored stride speed.
The skeleton retains body motion, particularly the death fall, even when object-root motion is disabled.
Walk terminal probes range approximately -3 to +11 cm vertically; run probes range approximately -3 to +38 cm.
Do not derive contact flags solely from a zero-height cutoff. Hoof orientation and sole geometry need visual calibration.

A native manual PlayableGraph sampled `Deer_Walk` through the rig Animator without a controller.
After advancing 0.25 s, the front-left terminal probe moved 0.213415 m and the object root remained `(0,0,0)`.
The graph and instance were destroyed in `finally`. This is a smoke check, not Q4 blending or Q5 lifecycle certification.

### Q3 — partial material check

Local image: `local-only/animation-preflight/deer-unlit.png` shows the sampled walking pose and base-color texture.
The unlit control confirms visible skin deformation and texture mapping on the separate rig.
The first `Planet/PropLit` preview was dark and poorly framed, so it is not accepted lighting evidence.
Preview lighting does not establish correct planet sun globals.

`Planet/PropLit` reports support and these passes: `ForwardLit`, `ShadowCaster`, `DepthNormals`.
It has no dedicated motion-vector pass. Depth/shadow correctness and animated motion vectors remain unverified.
Shader inspection returned: "use of potentially uninitialized variable (WeatherCloudConvectivity)".
No shader change was made during preflight.

The console also contained: "An abnormal situation has occurred: the PlayerLoop internal function has been called recursively. Please contact Customer Support with a sample project so that we can reproduce the problem and troubleshoot it."
The console was not cleared beforehand, so this session cannot establish when that message originated.
Record a fresh console baseline before the next preview/render test.

Q2 contact phases, calibrated stride speed, Q3 planet lighting, shadows, depth, and motion vectors remain open.
Q4–Q11 required implementation at the time of this initial preflight.

## Subsequent prototype

The [procedural pose prototype](../design/2026-09-05-procedural-pose-prototype.md) records the later implementation and evidence. It supersedes the initial pending status for clip playback, imported eating, mesh variants, and initial foot correction. Moving-contact calibration and visual acceptance remain open.

