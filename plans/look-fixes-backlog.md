# Look / content fixes backlog

Issues seen **while walking the character on the surface** (2026-08-09) — the character MVP is now a handy
on-surface look-inspection tool. These are rendering/content polish, separate from the controller (which works).
Not yet fixed; visual changes go through `pp-change-control` + an F10 review before landing.

## ⭐ Root cause behind several of these — the analytic surface ≠ the visible mesh

The planet exposes **two surfaces that disagree by ~3–24 units** (measured):
- **Analytic** — `IPlanetSurfaceSampler.TryGetSurfaceRadius` (`Planet.cs:466`, the `_surfaceProvider` local radius).
- **Visible mesh** — the rendered chunks / `IPlanetSurfaceRaycaster.TryRaycastVisibleSurface` (`Planet.cs:515`).

Anything placed/grounded on the **analytic** surface floats above or sinks below the **rendered** terrain.
This one mismatch drove **the character fall-through** (fixed by moving grounding to the raycast) **and** the
**floating scatter** (below), and will bite **collision** ([docs/design/2026-08-09-collision-strategy.md](../docs/design/2026-08-09-collision-strategy.md))
if not unified.

**DECIDED 2026-08-09 (rev 2)** — see [docs/design/2026-08-09-surface-unification.md](../docs/design/2026-08-09-surface-unification.md)
(corrected after a Codex review + independent code trace). The planet has **several** height representations,
all derived from the same noise and fixed at generation. **Scatter places on the raw analytic noise; the
rendered mesh samples the same noise at its vertices** — they agree at vertices and differ only by the triangle
chord (measured: ~0 near the camera / max LOD, sub-pixel at distance). So the near-visible float is **not**
height — it is **orientation** (a radial prop lifts its downhill edge on a slope). Policy: **per-consumer
authority + error budget**, no unification service. Character grounding uses the camera-scoped visible raycast
with an analytic fallback — "fixed" is a runtime observation until a **land** test proves it.

## The list

### 1. Rocks/bushes not oriented to terrain + parts float — FIXED via orientation (2026-08-09)
Root cause of the near-visible float: **orientation, not height** (mesh-pivot + height-sag both ruled out by
measurement — pivots sit at base; noise-vs-render sag ~0 at max LOD). A radial prop on a slope lifts its
downhill edge → gap + shadow.
- **`ConformToSlope [0..1]`** added: `up = normalize(lerp(dir, surfaceNormal, conform))`, threaded through
  DTO/rules + both `TryPlace` bodies (managed + Burst, parity 0.0000°). 0 = radial (up == dir exactly, golden
  placements intact); 1 = lies on the surface normal.
- **Per Bryan's rule** ("rocks etc. fit the terrain; trees/flowers grow up"): **rocks = 1** (14 assets);
  **bushes + flowerbushes = 0.6** (7 assets — beds without lying flat); trees/pines/palms/dead-trees/grass/
  reeds/ferns/flowers = **0** (radial). **Mushrooms left at 0** (grow up on a stalk) — flagged for review.
- Verified: 78/78 green; runtime — rock up = normal on a 25° slope, bush tilts 15° (0.6×25°), tree radial.
- **Height float** (noise vs coarse render triangle) deferred — sub-pixel except far/impostor range; only
  revisit if a pixel threshold is exceeded (surface-unification doc SU4/SU5).

### 2. Mushrooms render a solid/flat color — LEAD (unconfirmed)
Likely the mushroom scatter prototype's material is a flat albedo (no texture/normal/lighting variation), or
it's showing the unlit impostor up close. **Diagnose:** find the mushroom prototype's material + LOD/impostor
setup; compare to a good prop (e.g. a bush) under `FoliageLit`. Check whether it's the impostor tier at close
range or a genuinely flat material. Effort: S once located.

### 3. Far biome edge lines — RECURRING (known)
Visible biome-boundary lines at distance. History (see `claude/project_grass_terrain_lighting_arc.md`): the
bright-green edge line was the terrain **grass surface-overlay** (green-over-tan reads luminant at borders);
`BiomeMapBaker.KernelRadius` 6→12 softened terrain/grass; overlay saturation trimmed. If it's back at distance
it's likely the far grass overlay or the biome colour blend at the shared-atlas kernel edge. **Diagnose (F10):**
`debug.mode TerrainSelectedAlbedo` (has the line?) vs `BiomeMapFlatColor` (biome colour only) vs
`GrassLodCoverage`; zero `_GrassSurfaceBrightness` on the runtime terrain material to test if it's the overlay.
Effort: M (recurring look-tuning, needs captures).

### 4. Character capsule lit from under the planet (planet doesn't block the sun) — DONE (2026-08-09)
FIXED (commit on `character-controller-mvp`). New **`Planet/PropLit`** shader
([Assets/Graphics/Shaders/PropLit.shader](../Assets/Graphics/Shaders/PropLit.shader)) shades from the shared
analytic planet sun (`Includes/PlanetSunLighting.hlsl`) like terrain/scatter: `lerp(nightColor, dayColor,
daylight)` where `daylight = smoothstep(planetNormal · sun)` is 0 on the night side. Any object wearing it
darkens with the planet, zero per-object wiring (globals are scene-wide). `PlanetCharacterController` assigns a
runtime PropLit material to the capsule. Verified in play: capsule luminance day 0.69 → night 0.29. **Reusable
for all future surface props/NPCs** — this was Bryan's explicit ask ("fix the planet not blocking light so new
things don't suffer the bleeding-light issue").

## Suggested sequencing
1. **Unify the surface** (root) — retires the floating scatter + prevents collision drift; the biggest win.
2. **Rock orientation** (align-to-normal, per-prototype conform) — small, high visual payoff.
3. **Capsule/prop planet-aware lighting** — small, fixes the "lit from under the planet".
4. **Mushroom material** — small once located.
5. **Biome edge lines** — the look-tuning one; needs F10 captures + `pp-change-control`.
