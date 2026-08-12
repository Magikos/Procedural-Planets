---
name: project_scatter_dusk_lighting
description: Scatter "black dot/dash" causes are STAGE-SPECIFIC — noon=mesh shaded floor, dusk=grazing shadow strength, far ribbon=impostor (not a bug). Fixes + tunables, 2026-08-10.
metadata:
  type: project
---

2026-08-10 (branch `scatter-placement`). Bryan's recurring "black dots/dashes at
distance" complaint is THREE different causes by time-of-day — diagnose by stage,
don't lump them. All diagnosed via agent self-serve captures (see
[[project_surface_props_lighting]] and the pp-run-and-operate skill's self-serve
recipe: freeze time + local noon + cam.Render, then Sun-shadows on/off diff).

1. **Noon black dots = mesh prop shaded side too dark.** Bush/rock props are
   `SyntyProps` on **Scatter.shader** (NOT FoliageLit) — bushes AND rocks share one
   material via the Synty atlas, so it's shader-scoped, not per-material. Scatter.shader
   lit with the TRUE per-face normal (no `_LeafNormalUp` canopy-softening), so a
   shaded bush side fell to `albedo*0.72` = black dot; full SSAO crushed it more.
   FIX (commit 0795b36): floor 0.72->0.85 (matches ScatterImpostor card floor),
   SSAO full->25% (matches FoliageLit). Measured split at noon: 73% dark-object /
   27% shadow — which is why an earlier impostor-shadow experiment (reverted) did
   nothing.

2. **Dusk black dashes = long grazing-sun cast shadows.** At ~11 deg sun every
   tree/bush throws a very long hard shadow; measured 89% shadow / 11% object.
   FIX (commit 3865d2d): `CelestialManager.UpdateShadowStrength` fades
   `SunLight.shadowStrength` toward `ShadowGrazingStrength` (0.35) as the sun grazes
   the VIEWER's local horizon (below `ShadowFadeElevationDeg`=22). Ground "black"
   pixels 12.8%->2.5%, shadows kept. Tunables: `time.shadow-grazing`,
   `time.shadow-fade-elev`. Also eased props into night with `sqrt(daylight)` in
   Scatter + ScatterImpostor (minor).

3. **Far coastal dusk "ribbon" = impostors, NOT a bug.** Proven: magenta-isolation
   test showed the ribbon is 100% impostors; raw-unlit-card test showed the card is
   a healthy olive (luma 0.25, no dark bake). Dim-at-dusk is correct contrast (a
   dark bush IS darker than lit sand, exaggerated against bright water). Do NOT
   re-chase it as a defect; brightening it = unnatural dusk glow.

GOTCHAS (self-serve capture): re-entering play regenerates a NEW world (seed) so
stop->play breaks pixel A/B — use freeze-time A/B in one session. `_SunParams`
publishes one frame AFTER SetTimeOfDay. `camera.surface-view true` before teleport
or the free-cam pulls back to orbit. Clouds still drift with the sun frozen, so
same-world A/B has cloud-shadow noise. Related: [[project_scatter_lod_impostor]],
[[project_planet_look_dev]], [[project_grass_terrain_lighting_arc]].
