# Environment sound audition — 2026-09-08

## Active Tracker

Status: Unity import, six EditMode tests, serial builds, and runtime playback checks passed after Bryan released Unity.
The audition scene is open in Play mode, stopped and ready for Bryan's listening review.

Current next action: Bryan selects recordings, listens, and records Keep / Reject / Unsure decisions.
This queue is a checklist. It is not an automation or a submitted Unity test job.

Inspected tree: `harvest-vertical-slice`, dirty on top of `d1e0f62`.
Other agents have extensive uncommitted changes. This task adds separate files only.

## Approved scope

Bryan approved a separate Unity audition scene with categories, playback controls, review decisions, notes, and source details.
He also approved a second listening pass with combinations of accepted recordings.
The scene does not configure planet ambience or change the main scene or build settings.

Use `Tools > Audio > Open Sound Audition` after Unity becomes available.
The menu opens `Assets/Scenes/Tests/SoundAudition.unity` and preserves Unity's unsaved-scene prompt.
Enter Play mode and use the Game view. Playback starts only when requested.

## Listening workflow

1. Select a category and recording.
2. Press **Play from start**. Adjust master and recording volume as needed.
3. Enable **Loop playback** to assess repetition and the loop seam.
   Each playing recording shows elapsed time, duration, a progress bar, and completed loops.
   The bar turns amber and displays **LOOPED** for two seconds after a wrap.
   Starting playback resets the counter. One-shot playback ends with **Finished** and a full bar.
4. Select **Keep**, **Reject**, **Unsure**, or **Unreviewed**.
5. Add notes about unwanted sounds, character, loop problems, or a replacement request.
6. After accepting recordings, try **Coast**, **Forest day**, **Forest night**, and **Wetland**.

Selecting another recording stops playback. **Stop all** stops every audition source.
Mixes select the first kept recording per matching category in catalog order.
The panel names every selected mix recording. Rejected, unsure, unreviewed, and missing recordings cannot enter mixes.
Bird and frog calls play once unless the reviewer enables looping.
These mixes compare recordings; they do not implement random call timing or biome simulation.

Decisions, notes, recording volume, and loop choices save by stable recording ID.
The file is `Application.persistentDataPath/SoundAudition/reviews.json`; the panel displays its absolute path.
Writes replace the previous file atomically and retain a `.bak` copy.
A load error disables review writes. A save error keeps the draft and displays **Unsaved changes**.
Use **Save now** after resolving a save error. Do not exit with unsaved changes.
The files remain local and separate from world saves.

## Candidate library

`Assets/AssetPacks/EnvironmentAudition/catalog.json` records original paths, source notes, hashes, and scene metadata.
The library contains 20 recordings, approximately 119.7 MiB of original audio.
The files preserve the original bytes, stereo channels, and levels.
Preparation requests streaming PCM without preloading or normalization.
Live inspection found 19 streaming clips and one Decompress On Load clip (`ocean-a`). All clips disable preloading.
The first clip's importer differs from preparation settings. This validation preserved the existing importer configuration.

| Category | Candidates |
|---|---|
| Ocean | Ambient Sounds surf; SurfaceData waves |
| Streams | Farm Animal Sounds calm and moderate streams |
| Lakes | Ambient Sounds generic water; lake suitability needs listening |
| Wind | Ambient Sounds steady wind; UniStorm winter wind and gust |
| Insects | UniStorm crickets; Ambient Sounds crickets; Bryan's Pixabay cicada download |
| Birds | Ambient Sounds sparrow and mixed bird calls |
| Frogs | Ambient Sounds frog call |
| Rain | UniStorm light and heavy rain |
| Thunder | UniStorm thunder |
| Forest | Farm Animal Sounds forest background |
| Underwater | Swimming System underwater recording |
| Fire | SurfaceData campfire |

Visible gaps: broadleaf rustle, pine wind, grass movement, reed movement, gentle lake lapping, rain on leaves, and distant waterfall.
Missing entries accept notes but cannot receive **Keep** or play.
BigSoundBank's cicada recording remains an alternative; this pass uses Bryan's existing download.
Source/license notes remain provisional for production adoption, particularly bundled demo audio and the Pixabay recording page.

## Preparation and replacement

`python tools/audio/prepare_sound_audition.py` prepares the shortlist without calling Unity.
The tool uses the existing bird review scene's camera serialization and scene defaults.
It imports no bird components or vendor scripts.
It refuses to replace a recording when the existing bytes differ from the source.

`python tools/audio/prepare_sound_audition.py --check` checks IDs, recording hashes, scene references, and the listener count without writes.

Add alternatives with new recording IDs. Preserve old IDs so previous rejections remain attached to the correct recording.
Do not replace a file under an already reviewed ID.
Edit the preparation shortlist and rerun preparation when adding candidates.
Existing review decisions remain outside the generated scene and catalog.

## Written Unity test queue

Do not run these steps while another agent owns Unity. An idle editor does not establish release.

| Order | Check | Pass condition |
|---|---|---|
| 1 | After release, let Unity import the new scripts, scene, and clips. | No new compiler or importer errors. All 20 clip references resolve. |
| 2 | Refresh IDE projects, then build Core, Planet, Editor, and Tests.EditMode serially. | New files appear in their projects; builds pass. |
| 3 | Run EditMode fixture `ProceduralPlanets.Tests.SoundAuditionTests`. | Persistence, replacement, malformed data, invalid gain, and mix eligibility checks pass. |
| 4 | Open the audition scene through its menu and enter Play mode. | One listener; usable Game view; silence until Play; no runtime errors. |
| 5 | Play two candidates successively, change volume, toggle loop, and press Stop all. | Solo playback never overlaps; controls affect playback; Stop all leaves silence. |
| 6 | Mark candidates Keep, Reject, and Unsure; add multiline notes. Exit and re-enter Play mode. | Decisions, notes, recording volumes, and loop choices survive. |
| 7 | Restart Unity and reopen the audition scene. | The same decisions and notes load from disk. |
| 8 | Try each mix with no kept clips, then with kept clips. | Empty mixes remain silent. Only kept clips play; listed sources match playback. |
| 9 | Inspect missing categories and notes. | Missing clips cannot play or receive Keep; notes persist. |
| 10 | Exit Play mode and restore the previous scene. | No audition audio continues; the main scene remains unchanged. |
| 11 | Bryan listens and records decisions. | Accepted clips reflect Bryan's listening judgment. Alternatives remain identifiable. |

Serial build commands after IDE project refresh:

```powershell
dotnet build ProceduralPlanets.Core.csproj --no-restore
dotnet build ProceduralPlanets.Planet.csproj --no-restore
dotnet build ProceduralPlanets.Editor.csproj --no-restore
dotnet build ProceduralPlanets.Tests.EditMode.csproj --no-restore
```

## Evidence

- File preparation check passed: 27 unique entries, 20 audio hashes and scene references, one listener, 119.7 MiB.
- Isolated C# compilation passed for the runtime, menu, and test files against this checkout's Unity references.
- The isolated compilation used temporary output outside Unity's build folders.
- The preparation turn did not operate Unity. Bryan subsequently released Unity with "Unity is yours".

### Unity validation — 2026-09-08

- The previous scene was `Assets/Scenes/Tests/SharedActorAnimationReview.unity`, with no unsaved changes.
- Unity resolved 20 nonempty clips and seven missing entries. The scene has one listener.
- EditMode job `0487db7e30e240d793dddd0e5f7274e2` passed all six tests, with zero failures or skips.
- Core and Planet builds passed with zero warnings or errors.
- The first Editor build failed because its NuGet restore artifact was absent:
  `error NETSDK1004: Assets file 'C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets\Temp\obj\ProceduralPlanets.Editor\project.assets.json' not found. Run a NuGet package restore to generate this file.`
- Rerunning Editor and Tests.EditMode builds with restore enabled passed. Editor reported 26 existing warnings; Tests.EditMode reported two analyzer-version warnings.
- Fresh Play mode started with no audition sources and no runtime error.
- All 20 recordings started individually with exactly one playing source. All seven missing entries produced no playing source.
- Switching from ocean to cicadas stopped the old source.
- Audio samples confirmed nonzero output: listener peak `0.009151479`, ocean source peak `0.004973852`, wind source peak `0.00477981567`.
- An initial sample probe returned zero before its buffer filled. A later frame confirmed output without changing audio settings.
- A temporary review store verified multiline notes, Keep/Reject decisions, volume, and loop persistence through save and reload.
- Four mixes selected only kept clips. The rejected alternate ocean clip remained excluded. Total source gain stayed at or below master volume.
- Stop removed active playback. The original reviewer store was restored before leaving Play mode; test decisions stayed in a temporary file.
- The previous scene reopened successfully. The audition scene then reopened for a second fresh Play session.
- Screenshot: `local-only/sound-audition-checks/audition-initial.png`. The category list, notes, decisions, and playback controls are visible.
- The final file check passed again: 27 unique entries, matching audio hashes and scene references, one listener.
- No source-code changes were needed during validation.

### Remaining human checks

- Judge recording character, background contamination, volume balance, and loop seams by listening.
- Exercise the visible controls during review. Runtime checks invoked the same handlers directly rather than simulating every mouse gesture.
- A complete Unity process restart was not performed. File reload and fresh Play sessions passed; full process-restart verification remains optional.

### Playback progress validation — 2026-09-08

- Bryan requested visible playback and loop progress during listening.
- Planet compilation passed with zero errors and 19 warnings outside the changed file.
- Live cricket playback counted four wraps. Screenshot: `local-only/sound-audition-checks/audition-progress.png`.
- Restart reset the loop count to zero. Non-looping playback reached Finished. Stop cleared the progress rows.
