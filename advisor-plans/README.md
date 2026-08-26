# Startup and Planet-Generation Implementation Plans

Generated on 2026-08-12 from the startup/planet-generation audit. The repository's
existing `plans/` directory already owns a separate character/terrain roadmap, so this
series lives in `advisor-plans/` to avoid mixing numbering and status.

Execute these plans in order. Each executor must read the selected plan in full, run its
drift check, honor every STOP condition, and update the row below when finished. Do not
push or open a pull request unless Bryan explicitly asks.

## Preflight (do this before Plan 001)

These plans were authored at `fab754b`. They were re-pinned on 2026-08-15 after review:

- Re-pin baseline commit is `fdbc76c`. The only in-scope product drift since `fab754b` is
  `Assets/Scripts/Planet/Planet.cs` (+73/-1, tree-generator work); every other in-scope file
  is unchanged. Run each plan's drift check against `fdbc76c`, not `fab754b`.
- The implementation branch is taken from `harvest-vertical-slice`, not
  `character-controller-mvp`.
- The worktree currently carries ~20 unrelated dirty files (Trees, Scatter, showcase). Every
  plan has a "no unrelated dirty file staged" done-criterion, so commit or stash that work
  before starting. Do not begin Plan 001 on top of it.
- Audit finding R1 (canceling startup does not cancel water computation) is **not** covered
  by this series and remains open. It does not block the Plan 002 cancellation smoke test,
  which cancels during Phase B colors — before water generation starts.

## Execution order and status

| Plan | Title | Priority | Effort | Depends on | Status |
|---|---|---:|---:|---:|---|
| [001](001-instrument-startup-generation.md) | Instrument startup and establish the current baseline | P1 | S | — | **DONE 2026-08-15** (measured) |
| 002a | Drop the redundant per-vertex biome resolve (no Burst) | P1 | S | 001 | **DONE 2026-08-15** — 6.2×, byte-identical |
| [002](002-burst-climate-vertex-bake.md) | Move the high-resolution climate vertex bake to Burst | P1 | L | — | **REJECTED (unnecessary)** — see below |
| [003](003-exact-biome-map-smoothing.md) | Replace the biome-map rescan with exact rolling smoothing | P1 | M | — | **REJECTED (cannot reach its gate)** — see below |

## Measured outcome, 2026-08-15

Same Editor session, same seed `1691104419`, adjacent runs, identical instrumentation on
both sides (the old-code run was produced by reverting only the 002a files and keeping the
checksum/timer instrumentation).

| Phase | Before | After | Delta |
|---|---:|---:|---:|
| initialize | 2,610 ms | 2,628 ms | — |
| terrain | 7,019 ms | 7,009 ms | — |
| lake | 728 ms | 735 ms | — |
| **colors** | **40,790 ms** | **15,399 ms** | **−25.4 s** |
| climate | 270 ms | 323 ms | — |
| water | 7,135 ms | 7,020 ms | — |
| finalize | 17,787 ms | 16,854 ms | — |
| **total** | **76,343 ms** | **49,973 ms** | **−26.4 s (1.53×)** |

Phase B: `vertex` 30,060.9 ms → 4,828.4 ms (**6.2×**). `mapBake` 8,394.5 → 8,295.9 (untouched,
as expected). Three-run new medians: vertex 4,828.4 ms, mapBake 8,318.7 ms, total 52,698 ms.

**Byte parity proven, not argued.** Old and new code both emit
`ids=57C8EB2B29A2961C weights=B427200B02DC6975 blended=3F298B24A24EB326` across all six face
atlases. Four runs, one old + three new, all identical.

### Why 002 is rejected

`vertex` is now 4.8 s. Plan 002's own gate was 8 s. Bursting a 4.8-second stage that is no
longer in the top three is not worth native memory, job lifecycle, cancellation ownership,
and a new test suite. The redundant-work deletion captured the win by itself.

### Why 003 is rejected

Measured `topKCpu` share across three runs: 0.404 / 0.428 / 0.414 — median **0.414**, under
the 0.5 go/no-go. `BuildHighResIdGrid` is ~59% of the map bake, and Plan 003 does not touch
it. Even a perfect 5.78× on the smoothing pass leaves `mapBake` at roughly 5.9 s, missing its
own 5-second gate. The plan cannot succeed as written.

### The new ranking

`finalize` — grass Configure, surface-edit replay, scatter Configure — is **16.9 s, 34% of a
50-second load, and was invisible until today**; it sat outside every phase timer, including
the audit's. It is now the largest stage by a wide margin, and it is untouched by this work
(17.8 s before, 16.9 s after). Anything further should start there, not with `mapBake`
(8.3 s), terrain (7.0 s), or water (7.0 s).

## 002 was split on review (2026-08-15)

The original Plan 002 fused two independent changes that the source audit had staged
separately: deleting the redundant per-vertex biome resolve, and Bursting the climate math
that remains. They were split back apart.

- **002a — deletion.** In face-atlas mode the per-vertex payload only needs climate; ids
  and weights come from the baked maps. The old path additionally ran a domain-warped
  assignment-field lookup, a `LakeMask` sample, and a DTO resolve for each of 19,250,814
  vertices, purely to fill the `z` channel that only debug mode 73 read. Deleting it is
  ~20 lines, adds no native memory, no job lifecycle, and no new cancellation surface.
- **002 — Burst.** Now conditional on what 002a leaves behind. If the remaining per-vertex
  climate is already at or under the 8-second gate, Plan 002 should not be executed at all.

Run 002a first and measure. An L-effort plan that turns out to be unnecessary is the most
expensive thing in this series.

Status values: `TODO`, `IN PROGRESS`, `DONE`, `BLOCKED (reason)`, or
`REJECTED (rationale)`.

## Non-negotiable generation contract

All three plans preserve the current world-generation order shown in the editable
[startup-generation-flow.drawio](startup-generation-flow.drawio) and its
[PNG preview](startup-generation-flow.drawio.png):

1. Static climate is generated from latitude, elevation, seeded temperature noise,
   and seeded moisture/precipitation noise.
2. That climate assigns the global Voronoi seed biomes.
3. The assignment cleanup remains enabled for up to
   `BiomeConstants.VoronoiCleanupIterations` (currently five) iterations, with the
   current eight-neighbor, six-vote rule and tie ordering. This is the pass that prevents
   isolated desert/rainforest islands and thin anomalous stripes.
4. Terrain and the lake mask are built.
5. Full-precision static climate feeds the leaf biome map, which keeps the current
   128×128 padded sampling domain, 25×25 window, top four IDs, tie ordering, integer
   weight normalization, and face-atlas assembly.
6. The evolving runtime weather grid is still generated only after the planet. These
   plans do not make biome generation depend on live weather, because doing so would
   invert the current `WeatherManager -> IPlanet` dependency and make a saved planet's
   biome identity change as weather evolves.

"Precipitation" in this plan series means the authoritative static climate moisture
field that drives biome assignment. Runtime cloud/rain weather remains a downstream,
evolving system.

## Dependency notes

- 002 depends on 001 because the historical log is from 2026-08-03 and predates current
  lake-mask work. Optimization must use a fresh, same-seed current-HEAD baseline.
- 003 depends on 002 because its leaf-map input must remain full precision while 002
  removes the managed `Vector4[]` intermediate. It also reuses 002's test visibility and
  batch data shape if the optional Burst smoothing gate is reached.
- 003 also depends on Plan 001 Step 5b, which splits the single `mapBake` number into
  `hrGrid` (`BuildHighResIdGrid`) and `topK` (`SampleTopKPerTexel`). Plan 003 only speeds up
  `topK`. If `topK` is not the majority of `mapBake`, Plan 003 cannot reach its own gate and
  must be re-ranked instead of executed. Do not start 003 without that split measurement.
- Each plan has a measurement gate. Stop at the first implementation rung that meets its
  target; do not build the optional next rung speculatively.

## Expected outcome

Historical evidence (not a current-HEAD benchmark) recorded a 127.775-second median:
71.249 seconds in per-vertex climate/biome work and 17.396 seconds in leaf biome-map
smoothing. Everything else totals 39.2 seconds. The first outcome gate is:

- per-vertex climate/biome substage: median `<= 8 s`;
- leaf map bake: median `<= 5 s`;
- complete planet generation: median `< 60 s` if the remaining terrain and water stages
  remain near their historical medians.

The arithmetic must close before an executor accepts a gate. `39.2 + 8 + 5 = 52.2 s`, so
the `<60 s` outcome is reachable only if Plan 002 lands near 8 seconds. The earlier
`<= 24 s` climate gate was rejected on review: it implies `39.2 + 24 + 5 = 68.2 s`, which
misses this section's own target while claiming success. It was also far too soft against
the work being removed — Plan 002 deletes the per-vertex KD-tree/DTO `ResolveBiome` path
entirely, and deterministic Burst over 19.25 M vertices of two noise filters should land in
low single-digit seconds. Treat anything above 8 seconds as a profiling task, not a pass.

These are acceptance gates, not promised Burst multipliers. If both hotspot gates pass
but total generation remains above 60 seconds, use Plan 001's new measurements before
authorizing terrain, water, allocation, cancellation, or startup-order work.

## Deliberately deferred findings

- Do not lower quadtree depth, chunk resolution, climate-map resolution, biome-map
  resolution, smoothing radius, seed count, or cleanup iterations. Those are visible
  quality controls, not performance fixes in this batch.
- Do not parallelize the startup initializer graph. The current dependency ordering is
  correct and there is no meaningful independent late branch before the planet.
- Do not "add Burst" to terrain; terrain mesh and normal generation already use Burst
  jobs.
- Do not port the 0.6-second climate-map upload or 0.4-second face-atlas upload first.
- Cancellation cleanup, lazy surface-edit textures, canonical-grid reuse for internal LOD
  payloads, deeper terrain scheduling, and water graph optimization remain follow-ups.
  Re-rank them from fresh post-003 evidence.
