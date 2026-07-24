# Grass Visual Migration Plan — 2026-07-04

## Active Tracker

Status: Phase 0 cleanup is mostly landed; visual work waits until the cloud sampling pass
is stable.

Current next action: after Cloud Phase 1 captures, start Phase 1 vertex-stage grass
lighting and measure NearGrass before/after.

- [x] Phase 0 cleanup: overflow, clamps, dead-code cleanup, shared placement include
- [ ] C2/C3 keep-vs-delete decision, if still wanted
- [ ] Phase 1: vertex-stage lighting
- [ ] Phase 1: tip specular
- [ ] Phase 1 capture and NearGrass timing comparison
- [ ] Phase 2: clump identity
- [ ] Phase 3: far-field decision
- [ ] Phase 4: frustum compaction, if measurements justify it
- [ ] Phase 5: trail/ripple RT, when gameplay needs it

---

Goal: migrate the grass from "dense uniform fuzz with visible layer seams" to "tufted,
lit, wind-alive ground cover with invisible LOD transitions" — while cutting its GPU cost
so density budget can go up, not down.

Current state this plan starts from (working tree, 2026-07-03): near-field blades are the
only live layer (144 m full density / 200 m draw from quality settings); the chunk layer
is off (`_chunkGrassEnabled = false`); the terrain-paint blanket is off
(`_grassBlanketEnabled = false`) after the biome-stripe fight. The far look is therefore
**bare terrain beyond 200 m** — the biggest open visual problem is the far-field story,
and it's a decision, not just code.

Source docs: [consolidated audit](../audit/2026-07-22-consolidated-code-audit.md)
(A1, A5, B2, C1-C5), [reference recommendations](../research/2026-07-04-grass-cloud-reference-recommendations.md)
(R3, R4, R5, R6, R8), original grass LOD findings reconciled in the
[consolidated audit](../audit/2026-07-22-consolidated-code-audit.md#former-2026-07-01-grass-lod-auditmd)
(G-series, partially shipped/reverted).

Files: `Grass.shader`, `GrassNearFieldPlace.compute`, `BiomeGrassPlace.compute`,
`GrassNearFieldController.cs`, `PlanetGrassCoordinator.cs`, `PlanetVertexColor.shader`
(Phase 3 only), quality settings in `QualityController.cs`.

Verification workflow: `debug.capture-set "Grass"` + F10; `run "Grass Edge Strip Probe"`
for any far-overlay work; `grass.overlay-status` / near-field stats via the grass debug
module; frame cost via `FrameTimingCounters` (NearGrass section).

---

## Phase 0 — Correctness floor (half a day)

1. **Overflow rollback in BiomeGrassPlace.compute** (audit A1): copy the near-field's
   one-line `InterlockedAdd(_GrassDrawArgs[1], 0xFFFFFFFFu)` rollback. (Matters even with
   the chunk layer off — it's one toggle from live.)
2. **Clamp the radius samplers** (audit A5): `p0` clamp in
   `GrassNearFieldPlace.SampleSurfaceRadius` and `BiomeGrassPlace.LoadRadius`.
3. **Dead-code deletions** (C1, C5; C2/C3 pending your keep-vs-delete call from the audit).
4. **Shared placement include** (audit D1): extract the ~150 duplicated lines
   (`BlendGrassParams`, hashes, cube-face math, bilinear corner blending, and other
   resource-independent helpers) into `Includes/GrassPlacementCommon.hlsl`, `#include`
   from both computes. Keep texture/resource samplers local unless the underlying
   resource layout is unified. Do this NOW because Phases 2 and 4 edit exactly this shared
   code — without the extraction every edit lands twice and drifts.

**Exit check:** placement stats unchanged before/after (same emitted counts on the same
seed), diff of the two computes shows only includes + kernel-specific code.

## Phase 1 — Lighting: cheaper AND better (1-2 days)

1. **Vertex-stage lighting** (R3, the big one): move sun direction, daylight, surface
   direct, night blend, and — critically — `CloudShadowFactor` (3 weather + 3D-noise
   fetches, currently per *pixel*) into `GrassVertex`; interpolate a lit color + keep only
   dither clip, cluster-card clip, and the normal-dependent wrap term per-fragment.
   Measure NearGrass frame section before/after — expect the largest single grass GPU win
   available.
2. **Tip specular** (R8): two lines in the now-vertex lighting path,
   `spec *= t * 0.12`-style, masked to lush/wet biomes later if wanted.
3. **Re-check blade↔terrain brightness seam** at the 200 m boundary after the lighting
   move (the canopy-color handoff already converges to `GrassCanopyAlbedo`; verify it
   still matches with vertex lighting).

**Exit check:** A/B captures identical-to-eye at blade scale; NearGrass ms measurably
down; grass under a cloud shadow still darkens correctly (shadow now per-blade —
acceptable by construction, verify visually).

## Phase 2 — Tufts: clump identity (1-2 days)

The change that most alters the *look*. Currently every blade rolls independent height/
yaw/tint → uniform fuzz. Port the Ghost-of-Tsushima clump model (R5):

1. In `GrassPlacementCommon.hlsl` (from Phase 0.4): `clumpId = hash(cellIndex / CLUMP_CELLS
   , face, seed)`; derive per-clump height multiplier, lean direction, and a small tint
   shift. `biome.Shape.w` (ClumpStrength — currently near-dormant) blends between
   independent-blade and clump-coherent behavior, so biomes keep authorship.
2. Pass clump lean per blade (repack into spare instance channels — `Color.a` is free;
   audit the 48-byte `BladeInstance` layout before adding stride).
3. Apply clump lean in `Grass.shader` blade construction (adds to the existing `leanWS`
   bend, NOT to wind — wind stays global).
4. Tune `CLUMP_CELLS` (clump world size ≈ 0.5-1.5 m) per biome via existing biome params
   if one size doesn't read everywhere.
5. **Clump shape: prefer nearest-jittered-seed (Voronoi) assignment over square grid
   blocks** — 9-cell lookup in the placement compute; organic clump borders instead of
   visible squares. (Idea validated by Hoskins' "Rolling hills" Shadertoy, reviewed
   2026-07-05 — CC BY-NC-SA, concept only, no code. Its ray-marched grass architecture
   itself is not applicable to our instanced-blade system.)

**Exit check:** side-by-side captures Savanna/Grassland at 5 m / 50 m / 150 m: fields read
as tufts with varied crowns, not carpet. Blade count unchanged (this is redistribution of
identity, not density).

## Phase 3 — The far-field decision + transitions (needs your call first)

Beyond 200 m there is currently nothing. Three options, in my recommendation order:

- **(a) Re-land the blanket with the linear-coverage fix** — the stripe root cause was
  found and fixed during the probe sessions (linear coverage + toe cut in
  `PlanetVertexColor.shader`, later reverted wholesale with the blanket). Re-apply the
  data-driven version, gated by the same `GrassCanopyAlbedo` single-source color so paint
  and blades meet at one brightness. Cheapest full-planet coverage; the probe script
  (`run "Grass Edge Strip Probe"`) is the regression harness and already exists.
- **(b) Re-enable the chunk layer** as the 200-420 m mid band (this is audit G6 — reserved
  as your decision). More real geometry, more cost, still ends somewhere needing (a).
- **(c) Both**: chunk mid-band + blanket far — the original three-layer design.

Whatever the pick, the transition work is the same and is where the original "sharp
visible lines" complaint lived:

1. Overlay window and blade fades stay slaved to `IGrassQualitySettings`
   (single-authority already done — G5 from the July-1 audit; keep it that way).
2. Coverage handoff verified with the strip probe at biome borders (the historical
   failure mode).
3. If (b): revisit the audit's triple-fade note (dither + geometry shrink + albedo darken
   all driven by `visualEdgeFade`) — with vertex lighting from Phase 1 the albedo-darken
   term is the first candidate to drop if the band reads as a dark ring.

**Exit check:** orbit-to-ground descent capture sequence: no visible ring, stripe, or
brightness step at any altitude. Strip-probe captures clean at the two worst biome
borders (Grassland/Desert, Savanna/Forest).

## Phase 4 — Budget headroom: frustum compaction (1 day, measure-gated)

Only after Phase 1 measurements — if NearGrass vertex cost still matters:

1. **Compaction kernel** (R4): per-frame pass reading the persistent 1M-instance buffer,
   frustum-testing with sway slack (InfiniteGrass's 1.1×/1.5× clip-space margins),
   appending survivors to a draw buffer + args. Placement/persistence untouched — no
   page-shift or rotation holes by construction.
2. Spend the winnings deliberately — per the 2026-07-05 reprioritization, the default is
   **distance, not framerate**: raise `NearFieldFullDensityDistance`/`NearFieldDrawDistance`
   (target ~220/300) so the near→far boundary moves out and covers fewer pixels. Buffer
   capacity check required: 300 m draw ≈ 2.25× the disc area of 200 m — verify emitted
   counts against the 1M capacity (near-field stats) and raise `DefaultCapacityInstances`
   or spacing if overflow stats fire.

**Exit check:** rendered-instance count from stats ≈ visible fraction; no popping on fast
camera turns (slack sufficient); net frame win recorded.

## Phase 5 — Alive-ness (parked until characters/gameplay)

- **Trail/bend RT** replacing the 8-slot interactor cap (R6) — when the character
  controller lands; subsumes the release-sample machinery.
- **Ripple impulses** (R9 / GrassFlow) — same RT, when gameplay wants shockwaves.
- Wind interaction with weather already works (shared `_WindDirection/_WindSpeedMps`);
  optional rider: scale gust flutter by local `storm` for storm-lashed fields — one
  weather sample in the vertex path Phase 1 already touches.

---

## Sequencing, risk, decision gates

- Order (revised 2026-07-05 — Bryan's stated top concern is the near-field falloff and
  its missing blend into any far representation): **0 → 1 → 3 → 4 → 2**. The far-field
  phase moves ahead of clumps: the 200 m edge is sharp because it fades to *nothing*, and
  no near-side fade tuning fixes that — Phase 3 provides the thing to fade into, Phase 1
  verifies the color handoff, Phase 4's winnings are spent on raising
  `NearFieldFullDensityDistance`/`NearFieldDrawDistance` (target ~220/300 at equal cost)
  rather than on framerate. Clumps (2) become the polish pass on top.
- Phase 3 still needs the **far-field decision (a/b/c) from you before any code**.
  Recommended: (a) blanket first — nearly free, probe harness exists — with (b) chunk
  mid-band held as the known upgrade if grazing-angle paint reads flat at eye level.
- 4 remains measure-gated. 5 is parked.
- **Gate after Phase 2**: clumps change the field's character — approve the look before
  far-field work matches terrain paint to it (the blanket must match the *new* canopy).
- Biggest risk: Phase 3(a) reopens the biome-stripe fight. Mitigation is baked in: the
  linear-coverage fix is known-good from the probe evidence, and the probe script is the
  acceptance harness — no knob-tuning without captures.
- Cost trajectory: Phase 1 pays (big), Phase 2 free, Phase 3 costs (bounded by option),
  Phase 4 pays. Net: better look at lower or equal ms.
