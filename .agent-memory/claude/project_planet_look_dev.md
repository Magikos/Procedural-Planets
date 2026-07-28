---
name: project-planet-look-dev
description: 2026-07-28 planet surface look-dev toward Synty POLYGON Meadow/Forest — post/lighting/density/grass; root causes + how the grass and sun systems actually work
metadata:
  type: project
---

Look-dev arc on branch `scatter-placement` (2026-07-28), goal = planet surface views like the Synty
POLYGON Meadow/Forest marketing render (lush, warm, dense). Bryan's Synty source (incl the exact
`PNB_Meadow_Forest` pack + its demo `Global Volume Profile`) is at `D:\Downloads\!3D Sources\Extras\Synty`.

**Root causes of the flat/dark look (fixed, commit 29f1b6a):**
- Post-processing was **OFF** on the planet camera (`renderPostProcessing=false`) — no bloom/grade/tonemap
  at all. Fixed: enabled post + HDR, added a global Volume → `Assets/Settings/PlanetLookProfile.asset`
  (bloom, saturation +28, contrast +12, exposure +0.15, WhiteBalance +9 warm, Neutral tonemap — values
  cribbed from Synty's own Meadow demo grade).
- Scene ambient was a dark static `363A42` (nothing drives it per time-of-day; only the impostor baker
  touches `RenderSettings.ambientLight`). Lifted to ~0.55 in Planet.unity so foliage is lit, not black
  silhouettes. Follow-up: drive ambient from `CelestialManager` daylight so night darkens again.

**Grass = a COMPUTE BLANKET, not scatter (commit d75144d).** Params are per-biome on the
`BiomeDefinition` SO: `GrassDensity/GrassHeight/GrassWidth/GrassClumpStrength` + tints. Blades were too
short/thin (Grassland H 0.4m, W 0.02) → stubbly. Raised H ~1.7x, W→0.045, D floor 0.8 across the 8
grass biomes → lush. Gotchas: the scatter `Grassland Grass`/`Grassland Wildflowers` prototypes have NO
mesh (placement-only) and **never render** — flowers need a real mesh. Far-field grass has a limited
render distance → bare ground + a green halo band beyond it (open grass-system follow-up).

**Bushes vs trees:** trees use `Scatter/FoliageLit` (cutout, `_LeafNormalUp` canopy-softening knob;
raised 0.6→0.85 to brighten dense canopies). Bushes use `Scatter/VertexColorLit` (Scatter.shader, solid
Synty props, `SyntyProps`/Generic_01_A atlas) — dark side-on, and over-densifying them (3m spacing) made
an ugly dark-blob carpet. Understory density was reverted to sparse; keep tree density (that helped).

**Sun / day-night for look-dev:** `CelestialManager.SetTimeOfDay(0..1)` + `SetTimeFrozen(true)`;
`SunDirection` is a world vector, so local sun elevation at the camera = `dot(SunDirection, camUp)` —
solve a good daytime angle by sweeping tod for `dot≈0.8`. Do NOT rotate the sun Light directly: it
desyncs from the atmosphere's `_SunParams` and the sky goes black. Post/lighting/material tweaks are
live in play mode (no regen); density/grass params need a regen (~3 min).

Remaining toward the reference: far-field grass coverage; wildflowers (need mesh); bush brightness;
day/night-driven ambient; hero trees + landmark props. See [[project-scatter-biome-buildout]].
