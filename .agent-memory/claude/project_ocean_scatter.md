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

**What IS unbuilt:** underwater *rendering* (does `Scatter/FoliageLit` read through the water
volume? caustics don't reach scatter; impostors bake against sky) and density over an
ocean-sized area — a reef is a colony, so see [[project_scatter_clumping_direction]].

**DECIDED (Bryan): the player will be down there.** Seabed is real content, so underwater
rendering is required, and ocean candidates must be benched at swimming eye height.

**Lesson worth keeping:** before writing up a gap as a new subsystem, check whether the existing
one already expresses it. The signed-altitude field had been there the whole time.
