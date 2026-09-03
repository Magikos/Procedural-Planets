---
name: project_ocean_scatter
description: Ocean scatter needs no new placement system — altitude is signed, so a depth band is an ordinary altitude gate; only underwater rendering and density are unbuilt
metadata:
  type: project
---

2026-08-12. Doc: [docs/design/2026-08-12-ocean-scatter.md](../../docs/design/2026-08-12-ocean-scatter.md).

**CORRECTED FRAMING — I got this wrong first, Bryan caught it.** I wrote it up as a new
subsystem (emit a seabed biome, add a below-water placement mode, invent a depth axis). Bryan
asked *"why is the ocean treated different than any other biome? Shouldn't this be the same
system everywhere?"* He was right. **The ocean was never special-cased, only unpopulated.**

**Verified in the placement code:**

- `altitudeMeters = (localRadius - SeaRadiusLocal) * scale` — **signed**. Negative underwater.
  `ScatterPrototype`'s own tooltip says "negative = underwater".
- A depth band is therefore just `HasMinAltitude/HasMaxAltitude` with negative values. No new
  field, no new axis.
- `MinWaterClearanceMeters` only rejects when `> 0` — it is a **per-prototype opt-out**, not a
  system boundary. `Lake Lily` already ships with it at `0`.
- Props already place at `localRadius` = terrain surface, which below water **is the seabed**.
  `OnWater` (lily pads) is the special case; ordinary placement is general and works at any depth.

**Proof: `Ocean Coral` prototype authored with ZERO code changes** — slot 69, `Biome=Ocean`,
`MinWaterClearance=0`, altitude gate `-30..-3`, `ConformToSlope=0.7`, `ScaleRange 1.5–3.5`
(source meshes are sub-metre). Library went 69 → 70 prototypes.

**Depth banding per-prototype beats a biome axis:** shelf/reef/deep are three prototypes with
different altitude gates, competing exactly like land biomes. Adding `depth` to the resolver
would duplicate an existing mechanism. `BiomeType.Underwater` (16) and `Cave` (15) stay dead
enum values — `Ocean` + altitude gate covers the seabed.

**Shipped 2026-08-12: 4 ocean prototypes**, slots 69–72, depth-layered with deliberately
overlapping bands (`Ocean Coral` -30..-3, `Shallow` -14..-2, `Plate` -26..-4 conform 0.9,
`Deep` -60..-22). All share one material (`Assets/Art/Materials/CoralShelf.mat`, Scatter/FoliageLit
+ the vendor albedo) and one texture set, downscaled 4096→1024 on import.

**Underwater rendering CHECKED in-world — no custom ocean foliage shader needed.** Bryan: *"the
underwater foliage looks fine."* The main risk did not materialise: **`Scatter/FoliageLit`
already receives the underwater fog**, so submerged scatter attenuates like the terrain instead
of reading as lit-for-air. Caustics still do not reach scatter (seabed shows ripples, corals stay
flat-lit) — visible up close, not objectionable at swimming distance. Corals read slightly warm
vs the blue; a material tint would be cheaper than a shader. Custom shader **deferred, not
required**.

**⚠️ The real gap is general underwater effects, not foliage** (Bryan: *"the underwater effects
in general need work (outside the foliage)"*). That is water rendering, unscoped, and should come
before more submerged-prop polish — it is what the eye notices first down there.

**Still unbuilt:** density over an ocean-sized area — a reef is a colony, so see
[[project_scatter_clumping_direction]]. Far-field impostors underwater still unverified (they
bake against a sky background).

**DECIDED (Bryan): the player will be down there.** Seabed is real content, so underwater
rendering is required, and ocean candidates must be benched at swimming eye height.

**Lesson worth keeping:** before writing up a gap as a new subsystem, check whether the existing
one already expresses it. The signed-altitude field had been there the whole time.

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Ocean scatter — SHIPPED](project_ocean_scatter.md) — **DO NOT repeat the old "scatter cannot place below the waterline" claim; it is FALSE and this index line used to say it.** Altitude is SIGNED (`altitudeMeters = (localRadius - SeaRadiusLocal) * scale`), and `MinWaterClearance` only rejects when `> 0`, so a depth band is an ordinary altitude gate — no new system, no depth axis. 4 coral prototypes shipped 2026-08-12 at slots 69–72 with bands −30..−3 / −14..−2 / −26..−4 / −60..−22. **Re-verified 2026-08-17: 995 coral instances placed within 80 m of a Beach point.** Underwater foliage rendering already checked by Bryan ("looks fine") — `Scatter/FoliageLit` receives the underwater fog. Real remaining gap = general underwater water effects + reef colony density, NOT placement. Doc: docs/design/2026-08-12-ocean-scatter.md

## 2026-09-03 — six more props, and why the reef had no far field

Bryan asked for more ocean props. Six shipped at slots 80-85: Ocean Seagrass, Ocean Anemone,
Ocean Anemone Purple, Ocean Brain Coral, Ocean Barrel Sponge, Ocean Kelp Deep. Depth bands
overlap on purpose, the way the first four corals do, so the reef layers instead of banding.

**The finding worth keeping: a branching skeleton keys almost no impostor card.** Every original
reef prop is a branching form. Measured card coverage across them ran 0.0009-0.0044 against a
forest tree's 0.0142, and six of ten ocean cards flagged EMPTY. Nothing on the seabed read at
distance because nothing on the seabed was SOLID.

Brain Coral is a solid dome for exactly that reason, and it worked: coverage 0.0274 mesh /
0.0316 card, IoU 0.854. Barrel Sponge (an upright lump) scored IoU 0.950, the best of any ocean
prop. Anemones 0.80-0.83. Seagrass 0.561. Deep Kelp still flags SILHOUETTE(0.43) — strand
geometry, same as the existing Ocean Kelp at 0.46, and not fixable by re-authoring.

**Reuse ladder held: zero new mesher code.** Seagrass and Deep Kelp classify into kinds that
already existed. Anemone is one TreeDef. Brain Coral and Barrel Sponge are RockGenerator lumps —
the trick the lily pad already used, so the Lily branch generalised into RockDefFor instead of
growing a third copy.

**Cull is the card-range switch, and it is a cliff.** ScatterPrototypeDto: FarReaching is
MaxCullDistance >= ImpostorMinMeshCull (120), and ImpostorEndDistance multiplies by 4.5 only when
FarReaching. So an authored cull of 119 gets no card extension at all and 120 gets 540 m. Brain
Coral (130) and Barrel Sponge (120) were authored above the line deliberately.

**Still open, not chased:** Ocean Coral Shallow (cull 90) and Ocean Coral Plate (110) sit under
the line, so their cards end at 90 m / 110 m. Conversely a 0.52 m Ocean Coral draws a card out to
540 m at about one pixel — pure FarGatherRadius cost for an EMPTY card. Both are tuning calls on
the ORIGINAL four corals, not on the new six.
