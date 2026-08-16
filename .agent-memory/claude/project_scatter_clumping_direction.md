---
name: project_scatter_clumping_direction
description: Future scatter feature — species should clump into patches/colonies, not just uniform density scatter (from Bryan's Synty target-look refs, 2026-08-10)
metadata:
  type: project
---

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
