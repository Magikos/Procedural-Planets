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
