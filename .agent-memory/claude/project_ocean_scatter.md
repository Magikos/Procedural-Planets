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
