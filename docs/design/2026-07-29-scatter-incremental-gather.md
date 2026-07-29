# Scatter incremental gather — design (for review)

Branch `scatter-placement`. Follow-up to
[2026-07-29-scatter-gather-perf.md](2026-07-29-scatter-gather-perf.md) (Tier 1a landed the biome memo,
gather ~21s → ~6s, `scatter.verify` PASS, commit `4354691`). This doc proposes the structural change
that lets scatter keep up at full flight speed and sets up edit persistence: an **incremental,
cell-persistent gather** modelled on Valheim's ZoneSystem.

## Problem

The gather re-scans the **entire** camera-centred disc (~520 m, ~370 k candidates, all prototypes)
**every 10 m of camera movement**. Even after Tier 1a (~6 s), throughput caps at one full-disc gather
per ~6 s. At the target ~100 m/s (and even the capped ~32 m/s), the camera crosses the tree
fresh-margin (region 520 − mesh cull 400 = 120 m) faster than a gather completes, so newly-approached
trees pop in late. Cutting per-candidate cost (Lever A, baked-height atlas) helps but still re-scans
the 490 m of disc that did not change between moves. The redundant re-scan is the structural waste.

## What Valheim does (reference)

- **Zones**: the world is a fixed grid of 64 m zones. A zone is generated deterministically from
  `worldSeed + zoneX + zoneY`; same seed → same vegetation forever. (We already have this:
  `ScatterHash.Node(seed, face, level, x, y)` per quadtree cell.)
- **Baked-height placement**: vegetation is dropped onto the zone's already-built heightmap, not a live
  noise re-eval. (This is Lever A, deferred.)
- **Generate-once, frontier-only**: zones in range are instantiated and **kept**; moving generates only
  the new frontier zones and unloads exited ones. Steady-state cost = the frontier ring, not the disc.
- **Persist only edits**: an untouched tree is regenerated deterministically and never saved; only
  changes become saved objects. (Matches our planned `SurfaceEditStamp`-is-source-of-truth model.)

We already match the determinism piece. The two gaps are baked-height sampling (Lever A) and
generate-once/frontier-only (this doc, Lever B). B is the load-bearing one for scale + persistence.

## Invariants (must not regress)

1. **Determinism / seed-stability.** Every prop is a pure function of `(worldSeed, face, level, x, y,
   slot)`. The set of instances drawn at a given camera pose must be identical to today's whole-disc
   gather (modulo the region-boundary band). `scatter.verify` must PASS (unique, order-independent,
   transform-stable, region-independent, id round-trip).
2. **Partition correctness.** The union of the per-tile gathers over a region must equal the single
   whole-disc gather of that region — no prop double-drawn, none dropped at a tile seam.
3. **No per-frame O(all instances) rebuild** beyond the existing per-frame LOD banding.
4. **Off-thread placement.** Tile gathers run on `Awaitable.BackgroundThreadAsync` from immutable
   snapshots, as today. Cache mutation happens on the main thread.

## Design

### Tile grid

Introduce a single **scatter tile** grid: cube-face cells at one fixed quadtree level `Lt`
(candidate ~level 6–7 ≈ 156 m / 78 m tiles on the 5293 m planet). A tile id is `(face, tileX, tileY)`
packed to a `long`. Tiles are aligned to the quadtree, and `Lt` is coarser than every prototype level,
so **each prototype cell falls in exactly one tile** (partition, invariant 2). Face seams are already
handled: `FaceSpaceCellRangeBuilder` emits per-face tile ranges (primary + edge neighbours), so a tile
"near an edge" is simply a tile on the neighbour face.

### Cache + lifecycle (the ZoneSystem analogue)

`ScatterTileCache` (owned by `ScatterRenderer`):

- `Dictionary<long TileId, TileEntry>` where `TileEntry` holds the tile's gathered instances (grouped
  per prototype) plus a state (`Pending | Ready`).
- Each re-evaluation (throttled to ~every `Lt`-tile-fraction of movement, not every frame):
  1. Compute the set of tiles overlapping the max-region disc around the camera surface anchor
     (`FaceSpaceCellRangeBuilder` at level `Lt`).
  2. **Enqueue** in-range tiles not in the cache (nearest-first).
  3. **Evict** cached tiles now beyond region + hysteresis.
- A background worker drains the queue (one/few tiles per tick) via a `GatherTile` primitive; completed
  tiles are inserted on the main thread. **No whole-disc re-scan** — only new frontier tiles are ever
  gathered.

### Tile gather primitive

Reuse the existing pure core: `GatherTile` = `GatherCore` restricted to one tile's cell range (per
prototype, the prototype cells inside the tile). No new placement math; the determinism, biome memo,
and gates are unchanged. A prototype `P` is gathered in tile `T` **iff** `T` is within `P`'s cull +
one-tile margin, so far tiles gather only coarse prototypes (trees), near tiles gather everything —
preserving the per-prototype-cull benefit inside the tile model without a per-move whole-disc scan.

### Draw

Draw still bands each prototype by camera distance (mesh LOD → impostor → cull) via `ScatterLodBatcher`
and the per-prototype instance buckets. Buckets are rebuilt **only when the live-tile set changes**
(tile load/evict, i.e. ~every `Lt` tile crossing, not every frame) by concatenating live tiles'
per-prototype lists. Between changes, per-frame draw reads the prebuilt buckets and applies LOD banding
exactly as today (N1 buckets preserved).

### Regen / teardown

`Configure` clears the cache and cancels the in-flight queue (as the current double-buffer cancel does).
A world is never retained across `WorldReadyEvent`.

## Why this fixes flight

Per-move cost becomes **O(frontier tiles)**, independent of region size and of per-candidate cost. At
any speed, only the thin ring of newly-entered tiles is gathered; everything already in range is cached
and drawn immediately. Combined later with Lever A (cheap per-candidate ground sample), the frontier
gather is trivially fast. This is the mechanism that lets Valheim stream a whole world at run speed.

## Lever A compatibility (deferred, orthogonal)

The tile gather still calls `ISurfaceGroundSampler`. Swapping `AnalyticGroundSampler` for a baked
per-face surface-radius atlas sampler later only makes each tile cheaper and seats props on the exact
meshed terrain; it needs no change to the tile lifecycle. Retain the atlas grass already builds
(`GrassSurfaceAtlasBuilder`, 1009²/face RFloat) rather than the dropped biome-atlas Tier 1c.

## Persistence hook (future, not this slice)

Edit-stamps (`SurfaceEditStamp`) become tile-scoped overrides: a tile's gathered set is the
deterministic base minus removed ids plus stamped additions. Untouched tiles stay pure-deterministic and
are never persisted — the Valheim model. Out of scope here; the design just must not preclude it (tile
granularity is the natural unit).

## Migration

1. Extract `GatherTile(tileId)` from `GatherCore` (range = one tile's cells). No placement change.
2. Add `ScatterTileCache` (dict + queue + evict) in `ScatterRenderer`; replace the whole-disc
   double-buffer with the cache. Keep the background-thread + snapshot discipline.
3. Rebuild per-prototype draw buckets on tile-set change; keep per-frame LOD banding.
4. Extend `scatter.verify` with a partition check (union of tiles == whole-disc gather).
5. Land Lever A separately afterwards if the frontier cost still warrants it.

## Risks / open questions (for review)

1. **Tile level `Lt`.** Smaller tiles = more, cheaper units + finer eviction, but more bookkeeping and
   more face-seam tiles. Larger = fewer, chunkier gathers (a single big tile can still hitch the
   frontier). ~78–156 m is the guess; wants validation against candidate counts.
2. **Bucket rebuild on tile-set change.** Is an O(live instances) concat on each `Lt` crossing
   acceptable given LOD banding is already per-frame O(instances)? Or maintain buckets incrementally
   (append on load; eviction from a flat array is the awkward case)?
3. **Fast-flight queue backpressure.** At 100 m/s many tiles enter range per second. Need a nearest-first
   priority + a per-tick gather budget so the queue drains toward the camera; possibly a coarse
   "trees-only" first pass per tile, refined later.
4. **Eviction hysteresis** to avoid load/evict thrash at a tile boundary the camera straddles.
5. **Memory cap** for live tiles at max region + fast flight; evict-by-distance under a cap.
6. **Per-prototype tile-distance cull** interaction with the tile grid (a tile partially inside a
   prototype's cull — gather the whole tile and let draw-cull trim, or split?). Leaning gather-whole,
   draw-cull, since the tile is cached once.
7. **Interaction with the impostor far tier** (region 520 m): impostor tiles are the outermost ring;
   they must gather coarse prototypes only and evict last.

## Decision requested

Review the tile-cache model, `Lt` choice, and the bucket-rebuild-on-change vs incremental-maintenance
question before implementation.
