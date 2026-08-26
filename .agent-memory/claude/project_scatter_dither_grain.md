---
name: project-scatter-dither-grain
description: The permanent 1-in-16 screen-door grain on scatter foliage was a Bayer table starting at 0.0, not any fade; fixed 2026-08-26 by shifting to level centres.
metadata:
  type: project
---

**2026-08-26 — "why do the trees look grainy" SOLVED, and every obvious suspect was wrong.**

Symptom: near trees, bushes and rocks wore a fine regular stipple at all times. Measured, not
eyeballed: isolated dark pixels on the near conifer locked to **one residue of a 4x4 screen lattice**
(`x%4==0, y%4==0`) at 8-17x the other 15 cells, while sky, terrain, grass and mid-distance trees sat
at ratio ~1.2-1.8 (no lattice).

Root cause: `static const float _Bayer4x4[16]` began at `0.0/16`. The screen-door is
`clip(threshold - fade)`. With **fade exactly 0** — no distance fade, no arrival ramp, nothing —
cell 0 evaluates `clip(0.0 - 0.0)` and the pixel dies. So one fragment in every sixteen was discarded
on every scatter instance, at every distance, forever. The hole showed the dark canopy interior
behind, which is why the dots read as a constant dark green rather than as sky.

Fix (`FoliageLit.shader`, `Scatter.shader`, `ScatterImpostor.shader`): shift the table to level
**centres** — `0.5/16, 8.5/16, ...` — so no threshold is 0 and no threshold is 1. Verified: lattice
ratio 8.6 -> 1.5 on the conifer, 17.4 -> 1.5 on the broadleaf, same camera, same frame budget.
`ScatterImpostor` has the compare reversed (`clip(coverage - threshold)`), where a 0.0 cell instead
KEEPS one pixel in 16 of a card that has faded to nothing — same shift fixes both directions.

**Ruled out by measurement, do not re-investigate:** SSAO (480 vs 477 cell-0 hits with the feature
off), cloud / atmosphere / precipitation render features, `scatter.fadein` arrival ramp, and the
LOD-crossfade distance fade — the near bands fade at 200-260 m, so a 5 m tree has fade 0.

**Two tooling traps that cost most of the session:**
1. A brightness-outlier metric is **not exposure-invariant**. Night fell between two captures and the
   grain metric collapsed from 2.8% to 0.019%, which read as "the fix worked". Freeze the sun
   (`time.freeze true`, `time.set-local 0.5`) before any A/B, and prefer a residue histogram over a
   raw count.
2. `Graphics.RenderMeshIndirect` **snapshots the MaterialPropertyBlock at submit time**. Editing a
   band's MPB and then calling `cam.Render()` in the same `execute_code` call changes nothing, so an
   MPB A/B silently no-ops. Material edits DO take effect immediately (they are referenced, not
   copied) — `_SeasonColor` was the positive control that proved the render path was live. When an
   MPB override and a material value disagree, the MPB wins on the NEXT frame.

Related: [[project_scatter_lod_impostor]] (the older distant-impostor speckle, a different defect —
missing mips on the atlas), [[reference_unity_mcp]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Scatter dither grain](project_scatter_dither_grain.md) — 2026-08-26 SOLVED: `_Bayer4x4` started at `0.0`, so `clip(threshold - fade)` killed 1 px in 16 at **zero fade**, forever. Not SSAO/clouds/fadein/LOD. **Traps: brightness metrics aren't exposure-invariant; RenderMeshIndirect snapshots the MPB at submit, so same-frame MPB A/Bs no-op.**
