# Straight hanging travel

Status: functional checks passed; rendered inspection completed; Bryan's visual approval pending.

## Scope and controls

`Assets/Scenes/Tests/LedgeTravelReview.unity` adds straight hanging travel to the existing traversal controller. A/D selects left/right authored steps. Releasing movement finishes the current step. Reversal waits for a supported pose. W/Space requests climb-up; requests during travel wait for the current step. S/Ctrl releases during travel or idle.

`ActorTraversal` owns the root path and checks hand support and body clearance before each step. It checks again in short runtime segments. A blocked path or ledge end retains hanging. Removing or moving the support releases traversal. Straight travel stays on one fixed collider. Revision 2 adds fixed right-angle inside and outside corners, including a checked transfer to the adjacent collider.

The fixture uses a five-metre-wide, 2.2-metre-high ledge. Each source step lasts one second and moves about 0.55 metres. Stop latency can therefore approach one second. No pose clock is paused during blend entry.

## Authored assets and fitting

Sources are the owned Universal Traversal pack's `Traversal_Ledge_Climb_Left.fbx`, `Traversal_Ledge_Climb_Right.fbx`, and `Traversal_Ledge_Climb_HangingIdle.fbx`. Imports preserve the existing `ReviewCandidates/T_pose.FBX` avatar dependency. Editable copies are `Ledge travel Left.anim`, `Ledge travel Right.anim`, and `Ledge travel idle.anim` under `Assets/Art/Interactions/Animations/`.

Left/right motion assets reuse `ActorTraversalMotionAsset`. Their 61 samples record lateral body travel on the production avatar. The controller applies that path while playback removes the matching baked displacement. Pose and root retain the source clock. No per-animation authoring C# file was added. If an edit changes root travel, the sampled motion asset must be refreshed; a hand rotation edit alone does not require a new path.

The shared view blends the idle correction independently from the incoming motion correction. Applying an idle correction derived from an already blended pose caused a visible height error. The final path retains the settled idle correction and weights it with the outgoing idle clip.

The source's support sequence holds the trailing hand first, then the leading hand. Final contact correction is capped at 8 cm. Fixed ledge targets do not follow authored motion as moving tool targets do. Contact flags remain compatible across idle and travel. Native foot-goal IK is disabled for these hanging clips because their feet have no ground support. Other scene configurations retain their existing clip settings.

## Evidence

Bundle: `local-only/animation-review/ledge-travel-2026-09-15/index.html`.

- `travel-current`: grab, rightward travel, stop, leftward travel, stop, climb.
- `end-current`: repeated rightward travel, ledge-end rejection, S to drop.
- `before`: same movement input without lateral motion configured.
- `left-source` and `right-source`: original clip/model at 1x beside raw production retargeting.

Captures run at 30 fps with two 1/60-second simulation steps per frame. The wide rear view shows displacement; the angled view tracks the actor for contact review. Per-frame metadata records phase, progress, root, edge, palm positions and rejection. Existing grab/climb actions provide context; this slice does not reapprove their quality.

The original Android model was verified against `Chopping Source Rig.fbx`: both have SHA256 `D5CC6B720F8CDBE86E50C3CAEFAB0BD5B031A512A075555A0555FB8DB0AEFE2B`. Original source diagnostics use that model. Raw retargeted diagnostics omit runtime blends and root authority, so full isolated production-B coverage remains unavailable.

I inspected the action overview and consecutive frames around entry, repeated steps, stopping, reversal, ledge-end rejection and release. The first settled left support interval, frames 94–101, measured a maximum palm-anchor error of 3.5 mm. This limited measurement does not establish finger fit or every contact interval. Full interval metrics remain open.

95 EditMode tests passed in job `70d23115d592493bad52e5178ff2fa85`: ActorTraversalTests, ActorTraversalMotionTests, InteractionPoseBlendTests and SidekickInteractionReviewTests. Added checks cover both directions, end rejection, preflight obstruction, late obstruction without tunnelling, and support removal. Runtime checks confirmed a queued W request progresses through HangRight to ClimbUp, and support removal returns `Traversal support changed.`

## Remaining coverage

Beam walking, ledge walking, arbitrary-angle corners, gaps between colliders, moving/changing-scale supports, other body proportions, rope climbing, fishing, combat and skill variants remain open. Finger shape still requires visual review. The broader animation audit remains open.


## Revision 2 — grip and corner travel

Bryan reported that the hands sat above the ledge and requested corner travel. The original review remains at `local-only/animation-review/ledge-travel-2026-09-15/revision-1.html`.

Seven editable ledge clips now flex the wrists toward the ledge. Runtime contact targets sit 15 mm above and 15 mm inside the edge, with the palm oriented toward the top. The source finger shape remains authored. This addresses the visible grip rather than treating a palm-marker distance as proof.

Four owned `Traversal_Ledge_Climb_{Outward,Inward}Corner_{Left,Right}.fbx` clips supply 1.333-second turns. Their editable copies and sampled `ActorTraversalMotionAsset` paths live beside the straight clips. Horizontal root translation and yaw were extracted from the editable clips into the path assets. Runtime applies that movement once. Vertical body motion, limb arcs, timing, and finger shape remain in the clips. No per-animation authoring C# generator was added.

`ActorTraversal` detects adjacent perpendicular walls, checks level hand support, and validates the complete body path. Inside turns start before the actor reaches the wall. The fitted path preserves capsule clearance around the corner. Every short runtime segment checks clearance again. A late obstruction releases the hang at the last accepted pose and phase. Loss or movement of either support also releases it. W/Space queues a climb during the turn; S/Ctrl can release immediately through the existing visible blend.

The view probes both fixed wall faces during corner contact. Probing the actor's rotating forward direction missed the trailing hand and let its elbow enter the wall. The corrected bend direction keeps the elbow outside the corner. Hand corrections are capped at 12 cm during corners and 8 cm during straight steps. The settled idle body correction is stored in actor-local space so it rotates with the new wall.

The High ledge review fixture now has an outside corner to the right and an inside corner to the left. Both routes start from the existing High ledge station.

### Revision 2 evidence

- `outside-final-v6.mp4`: 510 frames / 17 seconds. Grab, travel right, outside right turn, stop, outside left return, travel, stop, S drop.
- `inside-final-v4.mp4`: 510 frames / 17 seconds. Grab, travel left, inside left turn, stop, inside right return, travel, stop, W climb.
- `corners-source.mp4`: original model/clip A beside raw editable production clip B. Order: outside left/right, inside left/right. B omits the extracted path and all runtime corrections; it is not a full isolated production graph.
- Per-frame metadata and `revision-2-evidence.json` record phases and rejections. Both final sequences completed without rejected paths.
- Consecutive-frame inspection includes the previously clipped outside-return elbow at frames 334–342 and inside return at frames 304–312. Full PNG sequences remain beside the videos.
- 101 tests passed in job `af8260242083426eb982b555e6465e19`. New tests cover both corner directions, inside support transfer and reversal, late obstruction, and support loss.
- Targeted whitespace checks passed. The whole dirty worktree also reports unrelated existing whitespace errors in `Assets/Resources/Settings/CreatureLibrary.asset` and `Assets/Scripts/Planet/PlanetDto.cs`; these were preserved.

Visual approval remains pending. Tested geometry uses fixed level tops and perpendicular walls. Arbitrary angles, gaps, moving or changing-size supports, and other actor proportions remain unreviewed. Existing jump and climb actions provide context and are not newly certified by this slice.


## Revision 3 — elbow handoff correction

Bryan approved the general result and requested an elbow check. Close frame inspection found left elbow/wrist penetration during the inside-right return at frames 296–299. Contact targets changed from no bend direction to a supplied bend direction. That incompatible target change faded contact influence out before acquiring the new target, exposing the raw elbow inside the wall.

Idle and straight-travel targets now supply their existing rig bend direction explicitly. This keeps the target representation compatible with corner targets. The shared blend interpolates elbow guidance without releasing the supported hand first. No new pose writer, increased IK reach, or clip timing change was introduced.

Complete replacement captures are `outside-elbows-v7` and `inside-elbows-v5`, each 510 frames. Consecutive checks cover corner entry, the reported outside pose, both return turns, and the corrected inside interval. `elbows-fixed-consecutive.png` records the checked close views. The previous captures remain in `revision-2.html`. No elbow inversion or wall penetration was visible in these inspected intervals; other geometries remain outside this review.

101 focused tests passed again in job `6ccf3de750174f1bb09356407c939b7a`. This small correction awaits visual review; the user's approval of the rest of the set is retained.


## Revision 4 — outside elbow bend direction

Bryan clarified that the reported defect was the outside elbow bending the wrong way, not the inside wall clipping. The earlier response addressed a different issue.

Outside turns now supply each arm's authored upper-arm-to-elbow direction to the existing solver. The prior shared outward pole could place an elbow on the wrong side of its shoulder-to-hand line. Inside turns retain their clearance guidance. Contact targets retain compatible bend fields and all existing blends.

`outside-bend-v8` replaces the outside sequence. The inside sequence remains `inside-elbows-v5`. Consecutive review covered frames 236–245 and 334–343, plus entry and settling poses. `outside-bend-comparison.png` preserves the before/after close views. Revision 4 includes a jump button for outside frame 240 and archives revision 3. The outside bend correction awaits Bryan's visual review.

101 tests passed in job `bb30861d058d472c91436ac0f04a9097`.


Bryan approved revision 4 after the outside bend correction. Preserve outside-bend-v8 and inside-elbows-v5 as accepted references. The broader audit remains open.
