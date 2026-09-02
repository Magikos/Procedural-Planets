---
name: reference_ffmpeg_video
description: ffmpeg is installed — how to turn a video Bryan records into frames I can actually read, including the PATH quirk in the Bash tool
metadata:
  type: reference
---

**I cannot read video directly.** The Read tool handles images and PDFs only. But ffmpeg is installed, so a
video becomes readable by extracting frames.

**Installed 2026-08-21** via `winget install --id Gyan.FFmpeg -e` (Bryan asked). Version 9.0, with `ffmpeg`,
`ffprobe` and `ffplay` aliases at `C:\Users\Bryan\AppData\Local\Microsoft\WinGet\Links\`.

**PATH quirk:** a Bash tool shell started BEFORE the install will not see it. Prepend it explicitly:

```bash
export PATH="$PATH:/c/Users/Bryan/AppData/Local/Microsoft/WinGet/Links"
```

PowerShell resolves it after refreshing from the machine/user environment. A fresh session should pick it up
normally.

**Verified round trip end to end** (synth clip -> probe -> frames -> Read the PNG):

```bash
ffprobe -hide_banner -v error -show_entries format=duration -show_entries stream=width,height,nb_frames \
        -of default=noprint_wrappers=1 clip.mp4
ffmpeg -hide_banner -loglevel error -y -i clip.mp4 -vf fps=2 out_%03d.png    # 2 frames/sec
ffmpeg -hide_banner -loglevel error -y -ss 00:00:07 -i clip.mp4 -frames:v 1 at7s.png  # one moment
```

Write frames to the SCRATCHPAD, not the repo. Probe first — a long clip at a high fps sample produces
hundreds of PNGs and reading many images burns context fast. 1-2 fps is usually plenty; for a specific
moment, seek with `-ss` and pull a single frame.

**For LOD/pop problems, prefer capturing it myself over asking for video.** A pop is a function of DISTANCE,
so driving the camera away from a prototype and rendering a labelled frame every N metres is strictly more
diagnostic than a recorded walk — I control the variable instead of inferring it. Video is the right tool for
things I cannot reproduce on demand: hitches, transient flicker, input-tied behaviour.

Also: a video frame is much lower quality than a rendered capture. If Bryan does record, native resolution
matters far more than length.

Related: [[reference_unity_mcp]] (rendering captures directly from the editor).

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [ffmpeg / watching video](reference_ffmpeg_video.md) — **I can't read video directly, but ffmpeg 9.0 IS installed** (2026-08-21, winget `Gyan.FFmpeg`) so a recording becomes frames I can read. **Bash shells started before the install need `export PATH="$PATH:/c/Users/Bryan/AppData/Local/Microsoft/WinGet/Links"`.** Probe first, sample at 1-2 fps, write to the scratchpad — a long clip at high fps burns context fast. For LOD/pop issues prefer capturing frames myself at labelled distances; video is for what I can't reproduce on demand.

## 2026-08-29 — frame-diff CANNOT find pop-in. Two retractions prove it.

Ranking frames by local diff magnitude finds the **nearest** objects, never the popping ones:
diff scales with screen-space motion, and near-camera parallax is the largest motion in frame.
Twice in one review I called a "pop at the player's feet" and twice a full-resolution extraction
showed an ordinary object growing or sliding in under parallax — frames 38-47 and 181-190 of
"Recording 2026-08-29 154956.mp4".

A temporal second-difference detector, score = d(f) minus max of d(f-1) and d(f+1), does not fix
it: validated against a recording with confirmed pops, it produced the same near-field profile.
Drop duplicate frames first — a 30 fps capture of a ~20 fps game repeats every 3rd frame — or the
neighbours are meaningless.

**What decides it: a WIDE crop at FULL resolution, 8+ consecutive frames, no upscaling.**
- Parallax: the object *grows* from small, or emerges from behind an occluder or a screen edge.
- Real pop: the object appears at **full size in open space** in one frame and stays.

A tight crop upscaled 4x fakes the second case. A grass tuft emerging past the player capsule read
as "an agave rosette appearing solid in one frame" at 110 px / 4x, and as plain parallax at
420 px / 1x. Never judge a pop from an upscaled crop.

Frame size matters when choosing crop coordinates: this capture is **924x650**, not 1080p. Run
`ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 FILE` first.

ffmpeg on this box: `-vsync` is rejected, use `-fps_mode passthrough`; `-start_number` must come
**before** `-i` for an image-sequence input.

### Scatter arrival ramp — wiring verified clean 2026-08-29

`ScatterRenderer.FadeInSeconds = 0.6f` publishes `_ScatterFadeInSeconds` in `Configure()`.
`_ScatterFadeInSeconds` is declared **outside** `CBUFFER_START(UnityPerMaterial)` in
`FoliageLit.shader`, `Scatter.shader` and all three `ScatterImpostor.shader` passes, so the global
reaches it (a per-material declaration would silently read 0 and disable the ramp — check this
first if props ever appear unfaded). `ScatterDrawBuckets._born` stays in exact lockstep with
`_matrices` through `Add`, the swap-remove in `RemoveBlock`, and the tile rebuild in
`RemoveInstanceById`, so the `born.Count >= count` guard in `ScatterGpuDraw.DrawProto` never trips.
Consequence: **a newly gathered instance dithers in over 0.6 s.** Anything that appears solid in
one frame is therefore NOT a scatter birth — look elsewhere.
