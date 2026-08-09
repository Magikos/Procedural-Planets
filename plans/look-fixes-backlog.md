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
if not unified. **Highest-leverage fix: make the analytic surface match the rendered mesh** (or route every
surface consumer through the raycast/mesh) so one ground truth serves character, scatter, collision, water.

## The list

### 1. Rocks not oriented to terrain + parts float — CAUSE PINNED
`ScatterPlacementMath.TryPlace` (`ScatterPlacementMath.cs:57,59`):
- **Orientation:** `align = FromToRotation(Vector3.up, dir)` aligns the prop's up to the **radial** `dir`, not
  the local surface normal — so rocks stand perpendicular to the sphere, not the slope. The surface normal is
  already known upstream (`slopeCos = dot(surfaceNormal, dir)`, line 40) but unused for rotation. **Fix:** for
  ground-hugging props (rocks), align up to the **surface normal** (or blend radial↔normal by a per-prototype
  "conform" weight). Tall props (trees) may want to stay radial — make it a per-prototype flag.
- **Floating:** `posLocal = dir * localRadius` places on the **analytic** surface (the root mismatch above) →
  props hover above / sink below the visible mesh. **Fix:** place on the visible mesh, or unify the surface.
- Effort: S (orientation) + shares the root-surface fix (floating). Watch: don't regress tree placement.

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

### 4. Character capsule lit from under the planet (planet doesn't block the sun) — character follow-up
The placeholder capsule uses the **default URP lit** material, so the directional sun lights it regardless of
the night side — the planet body casts no shadow at that scale. The terrain uses a **custom analytic sun**
(day/night from `surfaceNormal · sunDir`). **Fix:** give the character a **planet-aware** lit material that
darkens on the night side using the same daylight factor as the terrain (`dot(radialUp, sunDir)`), rather than
raw URP directional lighting. This will apply to real character/NPC art too. Cheapest MVP version: a small
character shader/material sampling the local sun; or multiply albedo by the terrain's daylight term. Effort: S–M.
(Same class of issue for any surface prop that uses standard lighting vs the analytic sun.)

## Suggested sequencing
1. **Unify the surface** (root) — retires the floating scatter + prevents collision drift; the biggest win.
2. **Rock orientation** (align-to-normal, per-prototype conform) — small, high visual payoff.
3. **Capsule/prop planet-aware lighting** — small, fixes the "lit from under the planet".
4. **Mushroom material** — small once located.
5. **Biome edge lines** — the look-tuning one; needs F10 captures + `pp-change-control`.
