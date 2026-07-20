# Grass far-field continuity — research findings

Date: 2026-07-14. Branch `code-refactor`. Author: research pass requested by Bryan after the
chunk/blanket approach "hasn't worked well" and the near-grass edge "is very clear, not hidden
well." This is a findings doc, not an implementation — decision belongs to Bryan.

Related: `pp-visual-migration-campaign` grass Phase 3 (the a/b/c menu), `pp-research-frontier`
§2 (orbit-to-ground ground-cover continuity), `pp-gpu-rendering-reference` grass.md.

## 1. The real problem (reframed)

The near-field grass draws to **200 m** (full density 144 m), then hard-stops to bare terrain
(`QualityController.cs:41-42`). The chunk and blanket far layers are OFF since the biome-stripe
fight.

The load-bearing fact nobody was accounting for: **terrain stays crisp far past where grass
ends.** Atmospheric aerial perspective only starts at `TerrainClarityDistance = 175 m` and isn't
full until `TerrainAtmosphereDistance = 1600 m` (AtmosphereSettings). So:

- Grass representation ends at **200 m**.
- Terrain is fully visible and crisp until **~1600 m**.
- => there is a **~1.4 km annulus of crisp, bare, fully-lit terrain** ringing the player where
  grass should be but isn't. The edge sits in the *most* visible zone possible — no haze, full
  detail, near the camera. That is why it reads as a hard ring.

Two things make our case harder than any asset-store grass:
1. **Orbit-to-ground.** You can look straight down from altitude, which betrays any 2D far trick
   that only works at grazing angles.
2. **Whole-sphere.** The "far" is a real horizon kilometres out, not a fog wall at a draw
   distance. Generic engines hide the grass draw distance in fog; we can't, because the planet
   is meant to be seen from orbit and from a hilltop.

## 2. Why the past attempts failed (so we don't repeat them)

- **Biome-stripe fight.** The blanket painted grass *coverage* per biome; coverage
  discontinuities at biome borders produced visible stripes. A linear-coverage fix was found
  during probe sessions but reverted with the blanket and is **not in the tree** (must be
  re-derived). Root cause was the coverage math, not the idea of painting.
- **Crisp edge.** Even a perfect blanket handed off at 200 m into crisp terrain; any residual
  brightness/parallax mismatch is fully exposed there. Nothing was extending the representation
  out toward the ~1.6 km haze that would have dissolved the seam.
- **Grazing-angle flatness.** Flat painted ground has no vertical parallax, so at eye level the
  far grass reads as a flat mat while near blades have volume — the eye catches the change.
- **Hard handoff.** Blade fade-out and far-layer fade-in were not a single overlapping cross-fade
  band; a switch, however smooth per-layer, still has a location.

## 3. SOTA landscape (what shipping games actually do)

The universal answer to grass-to-horizon is a **distance LOD chain**, because individual blades
go **sub-pixel** past some distance and become pure waste:

1. **Near:** real 3D blade geometry (we have this, 0-200 m, incl. cluster cards as a mid step).
2. **Mid:** **billboard / cross-quad cards** — cheaper volume that still catches light and has
   some height. (Our cluster-card mode is already this; its range is just short.)
3. **Far:** **terrain-material texture/tint** — the ground itself is coloured and detail-normalled
   to *read* as grass. No geometry. This is unavoidable at planet scale.

Transition-hiding techniques that recur across sources:
- **Overlapping cross-fades**, not switches; **noise/dither alpha** in the fade band so the
  boundary is stochastic, not a line.
- **Single-source colour** so blades and terrain paint meet at one brightness (we already have
  `GrassCanopyAlbedo` for exactly this).
- **View-angle blend:** lerp the far factor toward full as the surface turns edge-on to the view
  — grazing angles are where the flat far layer reads worst, so push the texture there.
- **Screen-space / image-space blend** for the extreme mesh→billboard jump (heavier; keep in
  reserve).
- **Let the atmosphere help:** push the far representation out to where aerial perspective is
  already hazing terrain (~1-1.6 km here), so the *outer* edge dissolves into haze for free and
  only the *inner* cross-fade with blades needs care.

## 4. Assessment against our architecture

What we already have that fits the SOTA chain:
- Near blades + a **cluster-card mid-LOD** (`Grass.shader`, `_GrassGeometryMode`) — the mid rung
  exists, just short-range.
- **Single-source canopy colour** (`GrassCanopyAlbedo`, used by both blade shader and the terrain
  overlay) — the colour-match rung is built.
- **Terrain aerial haze** at 175-1600 m — a free outer dissolve if we reach it.
- A **regression harness** (`Grass Edge Strip Probe`) and the *diagnosis* of the biome stripes.
- Terrain paint machinery present (`EvaluateGrassOverlay`/`ApplyGrassSurfaceAlbedo` in
  `PlanetVertexColor.shader`).

What's missing is only the **far rung done right**: a terrain-texture representation that (a)
fades in *under* the blade fade-out over one overlapping band, (b) extends out toward the haze
distance instead of stopping at 200 m, (c) matches colour via the single source, (d) is driven by
a **continuous** grass factor (not the striping coverage math), and (e) gets a grazing-angle push.

**Conclusion:** the far layer essentially *must* be terrain-texture (blades can't tile a
kilometre-radius disc; billboards alone still end somewhere). The real work isn't "which layer" —
it's the **transition discipline** the past attempts skipped. The chunk mid-band is optional; our
existing cluster cards can cover mid-range more cheaply than re-enabling per-chunk geometry.

## 5. Recommendation

Pursue the **terrain-tint far layer, done as a distance-and-view driven grass shading of the
terrain material** — not a separate painted coverage bake (that was the striping path). Concretely
the hypothesis to prove, cheapest first:

**Far grass = tint the terrain toward `GrassCanopyAlbedo` (plus a cheap detail-normal breakup),
weighted by (grass-biome factor) × (distance fade that starts under the blade fade-out and
extends to ~1.2-1.6 km) × (grazing-angle boost), dithered in the cross-fade band.**

Sequence, each a gate:
1. **Prove the far tint can swallow the edge at all** (first experiment below) — colour + distance
   only, no per-biome coverage, so zero stripe risk. Pass/fail on a descent capture.
2. If it swallows the edge: add the **continuous grass-biome factor** (the re-derived linear
   coverage) and validate at the two worst biome borders with the strip probe.
3. Add **grazing-angle boost** + **detail-normal** for eye-level parallax feel; re-check descent.
4. Extend the **cluster-card mid range** if a gap between blades and tint remains.

Mid-band chunk geometry (campaign option b) stays a *held upgrade*, only if grazing-angle paint
still reads flat after step 3 — not a starting move.

## 6. Falsifiable first experiment

Cheapest test of the core unknown ("can a colour-matched, haze-reaching terrain tint make the
200 m ring disappear?"):

- Add a distance-driven grass tint to the terrain material: where the cell's biome is grassy,
  lerp terrain albedo toward `GrassCanopyAlbedo` by a factor that is **0 at ~120 m, ramping to
  full by ~300 m, held to ~1.4 km, then released as aerial haze takes over**. No coverage
  painting, no per-biome coverage curve — flat "grassy-biome → tint" only.
- Blade fade band stays 144-200 m; the tint ramps in across 120-300 m so they **overlap**.
- Capture the **orbit-to-ground descent sequence** (campaign's exit protocol) at a fixed seed +
  teleport.

**Pass:** no visible ring/step at the old 200 m boundary at any altitude; the far ground reads as
continuous grass dissolving into haze.
**Fail modes and what each means:**
- Ring still visible at 200 m from above → colour mismatch or the overlap band is too narrow
  (widen / verify `GrassCanopyAlbedo` is the shared source).
- Reads flat at eye level → grazing-angle + detail-normal needed (step 3), expected.
- Stripes appear → only if biome factor leaked in; it shouldn't in this step.

If step 1 passes, the approach is validated and we proceed through the gates. If it fails even
with colour matched and haze reached, the terrain-tint far layer is not the answer and we escalate
to billboard-field or screen-space blend — but the evidence will point the way instead of guessing.

## Provenance

- Distances: `grep -n "NearFieldDrawDistance\|NearFieldFullDensityDistance" Assets/Scripts/Core/QualityController.cs`;
  `TerrainClarityDistance/TerrainAtmosphereDistance` in `Assets/Resources/Settings/AtmosphereSettings.asset`.
- Single-source colour: `GrassCanopyAlbedo` in `Assets/Graphics/Shaders/Includes/GrassColor.hlsl`.
- Paint machinery: `EvaluateGrassOverlay`/`ApplyGrassSurfaceAlbedo` in `PlanetVertexColor.shader`.
- Probe harness: `Assets/Resources/ConsoleScripts/Grass Edge Strip Probe.txt`.
- SOTA sources (grass LOD chain, transition hiding): hexaquo "Grass Rendering Series Part 4"
  (Godot infinite grass LOD), vulpinii grass-modelisation tutorial, DynDOLOD grass LOD billboards,
  GPU Instancer feature docs, GameDev.net grass-technique threads. General technique, not code.
