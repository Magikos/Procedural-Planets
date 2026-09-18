# Chest animation polish

The [feedback revision](2026-09-12-chest-animation-feedback.md) supersedes this candidate after Bryan's wrist, inspection, and leg-clipping review.

Status: candidate ready for Bryan's visual review. Other interaction types still need their individual polish passes.

## Changes

- The chest has two lid-relative hand contacts instead of one actor-relative, palm-up contact.
- Initial reach uses partial IK. Opening and closing use both hands.
- Inspection holds a bent pose facing the chest interior instead of an upright pose with raised palms.
- Opening and closing allow time for hand acquisition before the lid marker.
- Closing retains hand contact until the lid completes its 66-degree movement.
- Recovery and close-entry blends take longer.
- An optional object approach anchor reuses the existing character alignment path. The chest stance sits 0.3 metres closer than the original stance.
- Shared hand IK releases over approximately one third of a second at full weight. Acquisition retains its previous speed.

The reusable phase assets own timing and motion. Scene anchors own chest-specific alignment and contacts.
The authoring command preserves existing definitions and selection anchors. Its defaults match this candidate.
No asset identities, serialized field names, or source clip references changed. The scene gained an Approach reference and a left grip.
Baseline copies for this task are under `local-only/chest-polish-2026-09-12/baseline/`.

## Evidence

The review uses `Assets/Scenes/Tests/SidekickInteractionReview.unity`, chest target 0, and the existing Sidekick actor.
The diagnostic reset and station visit establish the initial pose. Normal interaction uses the shared session and pose graph.
Each run settles for 90 manual 60 Hz steps before E. The final normal run continues inspection with E at frame 240.
It records 480 simulation frames and 240 rendered frames, then ends with an inactive session and a fully closed lid.

Exit checks: the lid must close before hand release; the complete cycle must finish; sampled interruption paths must blend without the observed fast recoil.
Visual approval remains Bryan's decision. Frame displacement measures motion, not perceived quality.

- Baseline images: `before-reach.png`, `before-open.png`, `before-inspect.png`, `before-close.png`.
- Final inspection image: `after-inspect.png`.
- Final 30 fps replay: `chest-review.mp4`, with frames under `frames/`. The direct render replay has a black background; the scene capture retains the sky.
- All captures live under `local-only/chest-polish-2026-09-12/`.
- Fixed comparison camera: position `(-1.5, 1.8, 0.1)`, target `(-3.5, 0.9, 1.5)`.
- Normal maximum step across both hands, head, and hips: 0.071733 m at 60 Hz.
- Cancellation/retrigger at frames 15, 80, 125, 180, 290, and 330: maximum 0.087454 m. Retrigger occurs three frames after cancellation.
- The earlier candidate reached 0.165423 m per frame during interrupted closing. Longer phase blends and slower IK release reduced that recoil.
- E from two offset starting positions acquired the chest and converged within 0.011667 m of the approach anchor. The residual is the grounded root height.
- Deactivating the contact cancelled the session in both offset runs.
- Selecting a closer visible target cancelled the chest session and selected target 1. An initial synthetic target at floor height was occluded; the corrected fixture used chest height.
- Graphify update completed: 15,371 nodes and 22,081 edges. The HTML preview was skipped because the graph exceeds its 5,000-node limit.
- EditMode job `988fc56fa7be4e36a63188a6e8b277d0`: 284 passed, zero failed or skipped. This includes the previous continuity groups and a new approach-cancellation regression.
- Core build: zero warnings or errors. Planet build: 21 warnings, zero errors. Editor build: 47 warnings, zero errors.
- Build logs: `core.log`, `planet.log`, `editor-restored.log`. The initial editor command used a nonexistent project and failed with `MSBUILD : error MSB1009: Project file does not exist.` The corrected project is `ProceduralPlanets.Editor.csproj`.
- A diagnostic console-reflection query failed with `Runtime error: Object reference not set to an instance of an object`. The runtime interaction checks were rerun without that query. The subsequent console exception query returned no entries.

The [broader animation continuity audit](2026-09-12-animation-continuity-validation.md) remains open.
This pass does not certify door, item, chair, crate, or traversal animation quality.
