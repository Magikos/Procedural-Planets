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

## THE BLANKET FIX (2026-08-16) — root cause was a MOVING edge, not a colour

Bryan: *"when you are moving through the world you can see that edge moving with you... the edge needs to be
seamless."* That observation was the diagnosis. The far grass overlay faded in over **24 → 120 m of CAMERA
distance** (`GrassFarOverlayStart/End`), so the handoff was a ring centred on the player, dragged along as he
walked. **No colour match can hide a moving edge** — which is why years of tuning brightness/saturation never
fixed it.

**Fix: the paint is a BASE LAYER at full strength everywhere** (start 0 / end 1). Nothing about the ground varies
with camera distance; blades just add geometry on top of paint that already reads as grass. Biome gating is
separate and untouched (the lake shore stayed sandy). Bryan confirmed "much better". Committed `4a7097e`.

Corollary: this removed the reason for the blade-distance increase, so it was reverted (144/200, budget back to
1.5M, −168 MB VRAM). **The seam was never about blade reach** — the good result rendered with the ORIGINAL
distances, because the quality change had never been compiled into that session.

**Measurement discipline lessons from this hunt (all of these produced wrong conclusions first):**
- `FreeCameraController` OWNS the camera and moves it every frame — `scatter.goto` gets undone, so several
  "grassland" measurements were actually taken on a sandy lake shore. **Always re-read `scatter.count`'s biome
  line immediately before sampling**, and prefer `camera.teleport`.
- Probing a world that is still streaming gives moving numbers; an apparent "blanket toggle is not idempotent"
  bug was just an unsettled world. Material state restores correctly.
- Terrain is **one shared material across 117 renderers**, so per-chunk material drift is not a thing here.
- `_GrassSurfaceSaturation` moves near AND far together (near ground contains paint between the blades), so the
  near/far ratio is invariant to it — it cannot close a handoff gap.

## Impostor "spotty horizon" (2026-08-16, analysed not yet verified)

Bryan rebaked impostors and the distant tree line still read as speckled holes. Three compounding causes, all in
storage/sampling rather than bake content — **so neither rebaking nor pushing the LOD distance back would fix it**:
1. **Atlas baked with NO mipmaps** (`new Texture2D(..., false)`), so a 128 px cell minified into a few screen
   pixels samples essentially at random. **Fixed** (mipChain true + `Apply(true)` + trilinear) — UNVERIFIED, and
   the change sits in `ScatterImpostorBaker.cs` which the parallel perf session also rewrote, so it is left
   uncommitted to avoid dragging their work in.
2. **Hard `clip(card.a - _Cutoff)` at 0.3** turns that aliasing binary — keep/discard rather than blur.
3. **Deliberate dither-out band**: `_FadeOutStart = end × 0.6` with a 4×4 screen-space Bayer pattern, so the
   furthest 40% of impostor range is dissolving in a checker. With aerial haze now in, this may be redundant.

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

## 2026-08-26: clouds had their OWN aerial distances, and it showed as sky-coloured tree holes

Bryan: *"So the silhouettes are the colour of the sky?"* They were — measured (0.258, 0.534, 0.548)
against clear sky (0.242, 0.522, 0.489). Fixed `afbbe21`.

**The trees were correct and invisible; the clouds were wrong.** Aerial perspective erases anything at
that range, so distant trees genuinely vanish — but they still write depth, so they occluded the clouds
behind them, and the clouds were still bright. The only evidence a tree existed was a tree-shaped hole
full of sky.

Clouds ran their own fade, `exp(-distance * density)`, authored as **70% hazed at 2500 m**. This planet's
radius is ~5 km, so from +30 m the horizon is ~**550 m** and the cloud base is met at ~**1764 m**, where
that curve had spent only **57%** — while the atmosphere had already taken terrain to nothing. Two
independently authored "how far can you see" numbers drifting apart; the drift was the artifact.

Fix: the cloud fade now reads **`_TerrainAerialPerspectiveDistances`** (110 m / 1250 m — the pair from the
Valheim pass above), so a cloud fades exactly as a hill at the same distance does. The cloud-side
reference distance is **deleted, not retuned** — retuning leaves two numbers to drift again.
`cloud.aerial-fade` survives as a 0-1 scale on that curve, default **1.0** (was 0.7 against the old curve,
where it meant something else). Horizon cream pixels 26538 → 3571; brightest 0.833 → 0.722.

**REJECTED, do not retry: driving it from the atmosphere's actual transmittance.** I built a shared
`ViewTransmittance` on the `_BakedOpticalDepth` LUT — the more principled quantity, and the *wrong* one.
Perceived haze here is dominated by **in-scattering, not extinction**, so `1 - transmittance` under-hazes
badly: it removed only 40% of the horizon cloud where the authored distances remove 87%. Reverted.

**Two tooling traps, both cost a round:**
- **`cloud.aerial-fade` looked dead.** The setter only marks `_staticPropertiesDirty`; the controller
  publishes on its next `Update`. A console set followed by `cam.Render()` **in the same call** renders the
  old value — identical numbers across a whole sweep. Same family as the `_SunParams` one-frame lag.
- **"messages=0" right after `ImportAsset` is NOT proof a shader compiles.** Variants compile on use: this
  reported clean, then failed with `undeclared identifier 'Luminance'` (a URP `Color.hlsl` function
  Cloud.shader does not include). Only the Unity console showed it. Check the console, not the import.
