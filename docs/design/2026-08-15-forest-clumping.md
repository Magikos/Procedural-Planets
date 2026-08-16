# Forest clumping — groves, clearings and scattered stragglers

**Status:** DESIGN. Requested by Bryan twice — first for flowers ("they clump together... but also have a
collection where they are all together"), then widened to trees: *"trees clumped into forest and empty plains and
sometimes just a small scattering of trees"*, per biome type.

## The problem, stated precisely

Scatter density today is **uniform within a biome**. A prototype's chance of placing at a candidate point is
`AreaKeep(cell) × biome membership × weight` — none of which vary spatially *within* a biome. So every hectare of
Forest gets statistically the same number of trees.

This is not a density bug. Verified on the planet 2026-08-15: a Forest biome holds ~194 trees per 80 m radius,
which is the right *amount* — it reads as forest and stays walkable. The problem is that it is the right amount
**everywhere**, so the world has no groves, no clearings, no thinning edges, and no lone trees on open ground.
Real forests — and Valheim's — are mostly variance.

## What we want, in three regimes

Per biome, a prototype should be able to land in one of three states depending on where it is:

| Regime | Density vs today | Reads as |
|---|---|---|
| Grove | 1.5–2.5× | a thicket you push through |
| Open | 0–0.15× | a clearing / plain you can build or fight in |
| Scattered | 0.2–0.5× | a few stragglers on open ground |

The point is the **contrast between neighbouring areas**, not the average. The average should stay roughly what
it is now, because that has been play-verified as correct.

## Approach: a colony field modulating densityKeep

One low-frequency noise field per prototype (or per prototype *group* — see below), sampled at the candidate
point, remapped into a density multiplier, multiplied into `densityKeep` before the existing accept test.

```
colony   = noise(dir * PatchScale + seedOffset)        // 0..1, low frequency
strength = lerp(1, remap(colony), Clumpiness)          // Clumpiness 0 = today's uniform behaviour
densityKeep *= strength
```

Two authored numbers per prototype:
- **`Clumpiness` 0–1** — how much the field is allowed to modulate. 0 must reproduce today's placement exactly,
  so this ships safely disabled.
- **`PatchScale` (metres)** — grove size. Trees want ~150–400 m; flower colonies want ~20–60 m.

The remap should be **non-linear** — a smooth noise field lerped into density gives gentle undulation, not
groves. Something like `smoothstep(lo, hi, colony)` with a wide dead zone at the bottom, so a good fraction of
the world reads as genuinely open rather than merely thinner.

### Why per-prototype and not per-biome

Because the interesting result is species that clump *differently in the same biome*: fir in dense stands, birch
scattered along the edges, undergrowth filling the gaps the firs leave. If all species in a biome shared one
field they would clump and thin together, which just makes the biome itself lumpy.

**But** related prototypes must share a field or the variants fight each other: the K per-instance variants of one
tree (`Meadow Tree`, `Meadow Tree v1`, `v2`) must use the **same** colony field, or a "grove" of variant 0 sits in
a "clearing" of variant 1 and the whole effect averages back out to uniform. Key the field on
`ImpostorShareKey ?? DisplayName` — the same key that already groups a species' variants for impostor sharing.

## Constraints this must respect

- **Determinism.** Seed-driven, no per-frame state. Same world seed → same groves.
- **CPU/Burst parity.** `ScatterPlacementMath.TryPlace` and `ScatterGatherBurst.TryPlace` must compute an
  identical field, like every other placement rule. Burst is the default path; a mismatch shows up as props
  popping when the cache re-gathers.
- **Tile cache friendliness.** The field must be a pure function of position — no dependency on gather order,
  camera, or neighbouring tiles.
- **No new draw cost.** This only changes *whether* a candidate is accepted, so it costs one noise sample per
  candidate and nothing at draw time.

## BUILT 2026-08-16 — Bryan answered all three

1. **Shared across species? BOTH.** A biome-wide openness field every prototype obeys (so a clearing is a
   clearing for everything) plus a per-group grove field on top (so wooded areas still separate into stands).
2. **Terrain-aware? YES.** Flat ground biases open, slopes bias wooded, so meadows land in hollows and flats
   rather than on cliff faces. Uses `slopeCos`, which placement already samples — free. *Moisture is not
   available at this layer*, so "wet ground opens up" is not implemented; it would need a climate sample in the
   Burst gather.
3. **Reuse for flowers? YES.** No code needed — `PatchScaleMeters` is per-prototype, so flowers author 20–60 m
   where trees author 150–400 m.

`ScatterClumping.Keep` is pure float math on `Unity.Mathematics.snoise`, called from the identical place in both
the managed (`ScatterField`) and Burst (`ScatterGatherJob`) gathers, folded into `densityKeep` exactly like
`AreaKeep` and membership — so parity holds by construction rather than by duplicated code.

**Ships inert:** `Clumpiness` defaults to 0 and `Keep` early-returns exactly 1.0, verified bit-identical.

**Measured, 6000 samples:** mean keep is 1.00 at clumpiness 0, 0.80 at 0.5, and 0.60 at 1.0 with 15% of the
surface genuinely open. Note it redistributes but does NOT preserve the total — at 1.0 a biome carries ~40%
fewer props, so raise `Weight` by ~1/mean to hold headcount. The first constants tried measured a mean of 0.33,
a two-thirds cull, which is why these were tuned against the mean rather than by eye. Species separation is
~19% of wooded points differing by >0.15 between two species.

**The grouping trap is handled:** the grove field is keyed on `ImpostorShareKey` (the SPECIES), not the
prototype, so a species' per-instance variants share one field. Keyed per prototype, a grove of variant 0 would
land in a clearing of variant 1 and the whole effect would average back to uniform.

**Still owed:** nothing is authored yet (every prototype is at Clumpiness 0), and none of it has been seen in a
world — planet generation has been stalling, so this is verified numerically only.

## Original open questions

1. **Should clearings be shared across species?** A clearing that all trees avoid but grass fills reads as a
   meadow; independent fields per species give a patchier, less legible world. A hybrid — one biome-wide
   "openness" field all trees respect, plus a per-species field on top — is probably what actually looks right,
   at the cost of a second noise sample.
2. **Should groves respect terrain?** Real clearings sit on flats and wet ground; forests climb slopes. Feeding
   slope or moisture into the field would tie groves to the landscape rather than scattering them arbitrarily.
   Cheap, since placement already samples both.
3. **Does the grass/flower case want the same knob** or a separate one? The original request was about flower
   colonies at ~20–60 m; trees want 150–400 m. Same mechanism, very different scale.

## Sequencing

1. `Clumpiness` + `PatchScale` on `ScatterPrototype` (default 0 = today's behaviour, ships inert).
2. The field + remap in the shared placement math, mirrored in Burst, with a parity test.
3. Author values for one biome (Forest) only; compare against the current build side by side.
4. Roll out per biome by eye, then revisit flowers at their own scale.

Related: [plan 006](../../plans/006-procedural-tree-generator.md) (what fills the groves),
`.agent-memory/claude/project_scatter_clumping_direction.md`.
