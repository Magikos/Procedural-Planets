---
name: project_valheim_look_pass
description: 2026-08-15 look pass toward the Valheim forest feel — what the gap actually is (mostly NOT tree geometry), what was changed, and the ground-cover idea that measurably made it worse.
metadata:
  type: project
---

**Bryan's goal (2026-08-15, with 3 Valheim reference screenshots): "I really want the Valheim forest look."**
He judged our trees as "not getting there yet". Analysis says the trees are largely **not** the gap.

## The gap, ranked (from comparing his references against our render)

1. **Atmospheric depth.** Valheim washes trees out from ~30 m and dissolves the treeline into pale blue. Ours
   was pin-sharp to the horizon: `TerrainClarityDistance` 175 m, `TerrainAtmosphereDistance` 1600 m.
2. **Ground cover** — their ground is made of plants; ours showed pale terrain between trees.
3. **Foliage shading** — theirs is backlit/translucent with soft alpha edges; ours was opaque flat-shaded.
4. **Palette cohesion** — they sit in a narrow green-blue band; we spread from acid yellow-green to near-black.
5. **Bloom / exposure** — blown bright sky, god rays.
6. **Clumping** — real groves and clearings vs our uniform density.

**Key insight for future work: Valheim's tree MESHES are simpler than ours** (low-poly trunks + alpha canopy
cards). Their look comes from fog, grass, wind and light. Polishing tree geometry further has low return.

## Changed (uncommitted at time of writing)

- **`FoliageLit` leaf translucency.** The term existed but was `pow(dot(view,-sun), 3.0) * 0.35` — a lobe so
  tight it read as a glint, not a lit canopy. Now `pow(..., 1.8)` + a `wrap` term for light bleeding through
  leaves facing away from the sun, exposed as a per-material **`_LeafBacklight`** (0-2).
  **First attempt overshot badly**: strength 0.7 with tint (1.15,1.05,0.62) turned whole canopies mustard-brown —
  read as autumn, not sunlight. Landed at **0.38** with tint **(1.08,1.04,0.86)**.
- **Aerial perspective, deliberately gentle** (Bryan: *"don't go too much on the fog, I like how far we can see
  and I don't want to mess up our sky"*): clarity 175 → **110**, atmosphere 1600 → **1250**. Safe by design —
  the effect is terrain-only, sky and water are unaffected.

## NEGATIVE RESULT — do not repeat

**Raising the grass surface overlay made the ground WORSE, not lusher.** Hypothesis was that
`grass.surface-saturation` 0.72 → 0.95 and `surface-brightness` 0.60 → 0.72 would give Valheim's saturated
ground. Measured A/B at the same camera: the defaults give a **continuous lush yellow-green mat**; the raised
values **stripped it to bare brown dirt with sparse tufts**. Reverted to 0.72 / 0.60. Those defaults are already
tuned — treat them as load-bearing. (Screenshots `gr-before.png` / `gr-after.png`.)

Corollary: at eye level the forest floor already reads as covered. The bare-looking ground in Bryan's aerial
screenshot is a *different* problem — see the rectangles below.

## Not done, and why

- **Palette / autumn density** — the orange-brown mass in Forest is CONTENT: `Golden Forest Tree` and
  `Autumn Forest Tree` prototypes wearing Synty autumn materials. Thinning or desaturating them is Bryan's
  aesthetic call, not a bug.
- **Bloom / exposure** — the one lever that would visibly change the sky, which he explicitly ruled out.
- **Clumping** — design doc written: `docs/design/2026-08-15-forest-clumping.md`.

## Separate issue found the same day: rectangular ground patches

Bryan's aerial screenshot showed large axis-aligned rectangles of differing green. **Not the tree work.** Prime
suspect is the uncommitted startup-perf change: `ColorGenerator.GetBiomeData` → `GetClimateData` now hardcodes
the per-vertex primary biome index to `0f`, moving terrain biome from smooth per-vertex to sampled from the
baked atlas (256/face ≈ 39 m texels). Its own note says "Mode 73 shader change NOT eyeballed yet". Second
suspect: the far grass blanket is now `active=True` and was historically disabled for biome-edge banding from a
hard `smoothstep(0.38,0.72)` gate. **Untested** — A/B by toggling `grass.layer Blanket false`, then by stashing
the seven perf files and regenerating.

Related: [[project_tree_generator]], [[reference_unity_mcp]], [[project_scatter_clumping_direction]],
[[project_planet_look_dev]].
