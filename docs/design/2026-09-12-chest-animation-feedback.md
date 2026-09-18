# Chest animation feedback revision

Superseded by [the contact and clearance revision](2026-09-12-chest-contact-revision.md) after Bryan found floating hands and head/lid overlap.

Candidate for Bryan's review. This supersedes the first chest polish candidate.

Bryan identified bent wrists, missing inspection hand placement, and legs entering the chest during closing.

## Changes

- Grip contacts moved from latch height onto the lid edge.
- Lid rotation no longer determines palm rotation. Object-specific lateral elbow guides reduce wrist compensation.
- Existing finger grip solving supports the lid-edge contact.
- The inspection phase selects a named contact set. The left palm rests at the front rim; the right hand reaches inside.
- Named contact sets belong to the object. Phase definitions select the set without embedding scene transforms.
- Closing reverses a selected section of the opening clip. The original closing clip stepped the forward knee into the chest.
- The stance moved back 0.15 m from the previous candidate.
- The lid opening changed from 105 degrees to 45 degrees. The original full-open contact lay beyond arm reach from a stance with clear legs.
- Hand targets carry optional elbow directions. The shared blend retains outgoing directions and bounds direction changes.
- Missing required named contacts reject interaction before it starts. Runtime phase snapshots retain contact selection, reversal, and grip radius.

These changes add optional fields. Existing interactions keep their rig elbow guides and original phase direction by default.
No source FBX, mesh, skin binding, or asset identity changed. The authoring command and saved chest assets match this revision.

## Evidence

Folder: `local-only/chest-polish-feedback-2026-09-12/`.
The earlier baseline remains under `local-only/chest-polish-2026-09-12/`; Bryan's three supplied screenshots document the reported defects.

- EditMode job `b8cb8739e4834c2f891163988f9ee425`: 286 passed, zero failures or skips.
- New tests cover named contacts, missing contacts, immutable phase data, invalid grip radius, elbow retarget blending, and retained elbow guides during release.
- Core build: zero warnings or errors. Planet build: 21 warnings, zero errors. Editor build: 47 warnings, zero errors.
- Build logs: `core.log`, `planet.log`, `editor.log`.
- Normal and six cancellation/retrigger runs used manual 60 Hz steps. Maximum sampled hand/head/hips displacement: 0.090338 m per frame.
- Cancellation frames: 15, 80, 125, 180, 290, and 330. Retrigger followed three frames later. Inspection continued at frame 240 when active.
- The normal cycle's maximum knee centre Z was 1.529656 m. The chest front is Z=1.60 m.
- A baked-skin check sampled every third frame across 420 simulation frames. Leg vertices below the chest wall top stayed at or behind Z=1.583825 m, giving 0.016175 m minimum front clearance.
- The replacement replay records 480 normal player frames at 60 Hz, with one image every two frames. It uses the final scene and camera position `(-1.5, 1.8, 0.1)`, looking at `(-3.5, 0.9, 1.5)`.
- Final frames: `final-frames/`. Replay: `chest-review-v2.mp4`. The direct camera render uses a black background.
- Temporary CPU-skin, bone-marker, and elbow-isolation probes were runtime-only. A fresh Play Mode run removed them before final recording.

The clearance check covers the sampled fixture path. It does not prove collision clearance for every body shape or approach.
Bryan still needs to judge the revised wrist pose and reduced opening arc.
Other interaction polish and the broader animation continuity audit remain open.
