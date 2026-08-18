---
name: project_scatter_clumping_direction
description: Scatter clumping — SHIPPED and authored 2026-08-17 (was a future feature); groves/clearings live on all 79 prototypes
metadata:
  type: project
---

**STATUS 2026-08-17: DONE and authored.** `ScatterClumping.Keep` (two fields: a biome-wide
openness that every prototype obeys, plus a per-species grove field) shipped inert in `a36cb79`,
and on 2026-08-17 values were authored onto **all 79 prototype assets** — trees 0.6-0.85 at
180-320 m patches, bushes 0.6/100 m, rocks 0.55/110 m, flowers 0.85-0.9 at 28-35 m, mushrooms
0.9/18 m, grass 0.35/70 m, reeds 0.4/40 m (thin water-edge biomes stay low or they go bald).

**Clumping COSTS headcount — that is the non-obvious part.** It redistributes but does not
preserve the total, because the caller clamps `densityKeep` to 1. Measured mean keep over 20k
samples, scale-invariant in patch size: clump 0.35→0.85, 0.55→0.77, 0.70→0.71, 0.85→0.64,
1.0→0.58. So every prototype's `Weight` was multiplied by 1/mean (×1.17 … ×1.61) at the same
time. Safe by construction: the spacing grid still caps density at one instance per cell, so a
raised Weight can only fill toward the old maximum, never past it. **If you re-author clumpiness,
re-do the Weight compensation or the world quietly loses ~30% of its props.**

Grove fields key off `ScatterPrototypeDto.ClumpGroupSeed` = `ImpostorShareKey ?? DisplayName`,
so `TreeInjection` variants of one species share a grove (they set ImpostorShareKey = species)
while different species get independent overlapping fields — which is what makes an oak stand
meet a fir stand with a soft blend instead of a fence line. Verified in play: dense stands
thinning through stragglers to open meadow.

---

*Original entry (historical):*

On 2026-08-10 (branch `scatter-placement`), after the dusk look pass, Bryan shared
three Synty target-look screenshots (forest at golden hour, meadow, aerial flower
fields with windmills) and flagged a scatter-placement gap:

> the flowers and grass types seem to clump together — there is scatter, but also
> when [there are] red flowers they scatter a bit but also have a collection where
> they are all together, etc.

**The gap:** current scatter is uniform density-based — `ScatterQuadtree.AreaKeep`
(Poisson-disc-ish spacing) × biome membership × per-prototype weight. A species is
spread evenly across its biome; there is no intra-species **clustering / colony**
behaviour. The target look has both: individual scatter AND dense patches where a
species collects (a field of yellow flowers, a clump of red flowers, grass-type
clumps), with bare ground between colonies.

**Direction (NOT yet started — Bryan said "move on for now"):** add a clumping /
patch layer on top of the base scatter so a prototype's effective density is
modulated by a low-frequency "colony" field (patch noise / blue-noise cluster
centres / per-species Worley), producing dense collections separated by gaps.
Must keep determinism (seed-driven, CPU/Burst parity) and the incremental tile
cache. Likely a per-prototype `Clumpiness`/`PatchScale` on `ScatterPrototype` that
feeds `densityKeep` in both `ScatterPlacementMath.TryPlace` and
`ScatterGatherBurst.TryPlace` (parity), tested by `ScatterGatherParityTests`.

**2026-08-15 (Bryan, restated + widened to TREES — deferred again, "we can work on the scatter later"):**
the same gap at forest scale. Wanted: trees **clumped into forests**, with **empty plains** between them,
and **sometimes just a light scattering** of individual trees — per biome type. So the clump layer isn't
only a flower-patch nicety; it's the forest-vs-plain macro structure of a biome. One low-frequency field
per prototype (or per biome) with three legible regimes — dense stand / bare / sparse individuals — rather
than a single uniform density. Wants a **plan** for this before code. Current focus moved to tree variety
+ look instead (plan 006 archetypes).

Related: [[project_scatter_gather_perf]] (placement/gather internals),
[[project_tree_generator]] (what fills the clumps),
[[project_scatter_biome_buildout]] (per-biome prototypes), [[project_planet_look_dev]]
(Synty target look). Target-look reference screenshots are in the 2026-08-10
conversation, not saved to disk.
