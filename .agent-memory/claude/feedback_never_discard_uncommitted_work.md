---
name: feedback-never-discard-uncommitted-work
description: Never run a destructive git command on a dirty tree; park work with stash or a copy first. From real data loss on 2026-08-29.
metadata:
  type: feedback
---

Never run a git command that discards working-tree changes. Park the work first, always.

**Why.** On 2026-08-29 I ran `git checkout -- Assets/Graphics/Shaders/ScatterImpostor.shader` to undo
*one hunk*. It reverted the whole file to HEAD and destroyed a prior session's uncommitted work
(`_LeafCard`, `CARD_MIP_BIAS`, the leaf-translucency block). I rebuilt it by hand and reported "nothing
lost". That was wrong. The rebuild checklist came from the conversation summary, so it only covered work
from *that* session. It missed `dayColor *= PlanetCastShadow(shadowAtten, daylight, 0.25);`, added three
days earlier on 2026-08-27 and never committed. Impostor cards stopped receiving cast shadow for nine
hours, during a task about brightness pops at the mesh->card handover. Only a transcript audit found it.
Bryan (2026-08-30): "There are stash commands, there are lots of ways you could have parked changes so
they wouldn't have been lost."

This tree is **permanently dirty** — 421 modified files, several sessions of Bryan's own unfinished work
in unrelated subsystems. A destructive command here is never cheap. `git status` being noisy is exactly
why I must not treat any file as "just mine".

**How to apply.**

- Banned outright on a dirty tree, with no exception I get to decide on: `git checkout -- <path>`,
  `git restore <path>`, `git checkout .`, `git reset --hard`, `git clean -fd`, `git stash drop`,
  `git stash pop` onto changed files.
- To undo my own edit: **re-edit the specific hunk back**. I made the change; I can unmake it. That is the
  default answer, not the fallback.
- If a full-file revert is genuinely needed, park first, in this order:
  1. `cp <file> <scratchpad>/<file>.bak` — one line, no git state touched, survives anything. Prefer this.
  2. `git stash push -m "<why>" -- <path>` for a path-scoped park, then `git stash list` to confirm it
     landed before doing anything else.
  3. `git diff -- <path> > <scratchpad>/<name>.patch` when I want the delta rather than the file.
- Verify the park exists **before** running the destructive command, not after.
- `git stash` with no path argument sweeps the *whole* tree. In this repo that captures Bryan's unrelated
  work too. Always scope with `-- <path>`.
- Reverting an asset import setting is the same rule: copy the `.meta` to the scratchpad first, revert by
  copying it back. I did that correctly for `TEM_Atlas_Vegetation_1A.png.meta` on 2026-08-29 — that is the
  pattern to repeat.
- A restore checklist drawn from a **conversation summary is not a verification**. The summary knows only
  this session; the file may carry uncommitted work from days earlier. Verify against `git diff -- <path>`:
  that diff is the complete list of what is uncommitted, so every hunk in it must be one I can account for,
  and anything the transcript shows was added but is absent from the diff is a loss.
- If I do lose something, say so plainly and immediately in the next report. Don't bury it under results.

Related: [[feedback_quality_over_cheap]], [[feedback_identify_before_fixing]].
