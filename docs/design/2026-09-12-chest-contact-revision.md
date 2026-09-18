# Chest contact and clearance revision

Superseded by [chest collection and door review](2026-09-12-chest-collection-and-door.md), which checks the lid mesh after Bryan identified remaining penetration.

This candidate addresses Bryan's second screenshot review. It supersedes the previous chest feedback candidate.

## Changes

- Lid contacts now follow the actual front lip vertices. The earlier latch-derived targets sat above the mesh.
- Flat palm contact replaces the small spherical grip offset on chest phases.
- Inspection uses the imported crouching rummage loop. The left palm supports on the rim; the right reaches inside.
- The existing procedural spine chain distributes a bounded forward lean. Acquisition, cancellation, and disabling release the lean gradually.
- Lid phases use the upright 0.60–0.68 source section. The earlier deep bend intersected the moving lid.
- The lid opens 55 degrees. Its opening marker fires at 20 percent of the 1.4-second phase.
- The approach stands 0.94 m in front of the chest origin. Closing retains the reverse clip path.
- Input can leave an inspection loop before the first loop finishes.

The authoring defaults and saved scene/definitions contain these changes. Other objects retain their existing contact and lean settings.

## Evidence and limits

Evidence folder: `local-only/chest-contact-diagnosis-2026-09-12/`.
The baseline scene and definitions are saved in `baseline/`. Bryan's screenshots remain the visual defect reference.

Core builds with zero warnings/errors. Planet builds with 21 warnings and zero errors. Editor builds with 47 warnings and zero errors.
Logs are `core.log`, `planet.log`, and `editor.log`.

EditMode job `cd1aac8bb9fd4b908b2ff4145949052a`: 289 passed, zero failures or skips.
New tests cover early inspection continuation, immutable/validated lean settings, and spine acquisition/release on both humanoid fixtures.
The replay `chest-review-v3.mp4` records 480 normal player frames at 60 Hz, with one image every two frames.

The geometry probe samples every third frame across 450 manual 60 Hz frames. It opens, inspects, and closes the chest.
It tests head-weighted baked skin vertices against the lid's oriented mesh bounds.
The rejected intermediate candidate had 18,426 vertex samples inside those bounds.
The revised path has zero such samples, with 0.081687 m minimum sampled distance.
Leg-weighted vertices below the wall top reach Z=1.586258 m. The chest front is Z=1.60 m.
These checks cover this actor and fixture path. They do not prove collision clearance for all poses or body shapes.

Six cancellation/retrigger runs used cancellation frames 15, 80, 125, 180, 290, and 330.
Retrigger followed three frames later. Maximum sampled head/hips/hand displacement was 0.066059 m per 60 Hz frame.
The final Unity console returned no exception entries. The scene remains running at the reset chest station.

Automated tests do not establish visual quality. Bryan's review and the broader animation continuity audit remain open.

## Follow-up: initial hand penetration

Bryan accepted the overall motion and identified hands entering the chest during the initial reach.
That phase requested only 0.35 hand weight. It now requests full contact through the existing acquisition blend.
The saved Chest definition and authoring default both use weight 1. Other phase settings remain unchanged.

The initial 49 frames were sampled at 60 Hz with the same actor and fixture.
The probe selected hand/finger-weighted skin vertices inside the chest footprint.
Before: 1,267 vertex samples fell below the 0.43 m wall top; minimum Y was 0.335135 m.
After: zero samples fell below the wall top; minimum Y was 0.456183 m.
This checks chest-body penetration during the reported reach. It does not certify every finger against the detailed lid mesh.
Evidence is in `local-only/interaction-hand-door-2026-09-12/`, including the before/after chest captures and next door baseline.
The corrected replay is `chest-hands.mp4` in that folder.
Focused EditMode job `b298234f28f24b47ba5acf6ddbf661a9`: 40 passed, zero failures or skips.
The Editor build passed with 47 existing warnings and zero errors; its log is `editor.log` in the evidence folder.

The catalog's Loot Anim Set was already used for chest and inspection clips.
The next door pass starts from the imported `Simple_Activations` door-knob clip. No new pack was imported during this correction.
