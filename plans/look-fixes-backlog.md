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

**DECIDED 2026-08-09** — see [docs/design/2026-08-09-surface-unification.md](../docs/design/2026-08-09-surface-unification.md).
The naive "make analytic follow the visible mesh" fix is a trap: the visible raycast is a **camera-scoped**
query (it misses where the camera has no selected leaf — measured), and coupling the deterministic scatter
gather to it would break placement determinism. Resolution: **render mesh = ground truth for on-surface
camera-local things** (character grounding — done; future collision), **analytic = deterministic approximation**
for camera-independent producers (scatter/AI/spawn). The reported up-close scatter float was mostly the
**orientation** bug (now fixed); residual near-camera height gap is a max-LOD sagitta (cm). A real
height-unify, if ever needed, places props on the **max-depth leaf mesh triangle** (deterministic), not the
camera raycast — plan in the design doc, review before code.

## The list

### 1. Rocks not oriented to terrain + parts float — ORIENTATION DONE (2026-08-09)
- **Orientation — FIXED (commit on `character-controller-mvp`).** Added per-prototype `ConformToSlope [0..1]`:
  `up = normalize(lerp(dir, surfaceNormal, conform))`, threaded through the DTO/rules and both `TryPlace`
  bodies (managed + Burst, parity 0.0000°). 0 keeps up == dir (trees/mushrooms unchanged, golden placements
  intact); the 14 rock prototypes are set to 1. Verified: 78/78 green + runtime check (rock up aligns to the
  normal on a 25° slope, tree stays radial).
- **Floating — deferred to surface unification** (the root mismatch). `posLocal = dir * localRadius` still
  places on the analytic surface. Near camera (max LOD) the residual gap is cm-scale; the metres-scale gap is
  distance-only + sub-pixel. See [docs/design/2026-08-09-surface-unification.md](../docs/design/2026-08-09-surface-unification.md).

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
