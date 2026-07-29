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

---

## Codex feedback — 2026-07-29

### Audit Summary

**Findings only — no code changed.**

Reviewed against branch `scatter-placement`, HEAD `fa63ac9`, the live Tier 1a gather and renderer,
the two scatter audits, the accepted feedback in
[2026-07-29-scatter-gather-perf.md](2026-07-29-scatter-gather-perf.md), the shared face-range
builder, and the current surface-edit persistence type. Graphify found only unrelated generic
`Renderer` nodes for this scope, so the findings below were verified directly against the working
tree. No build, Unity run, or performance measurement was performed.

**Verdict: REVISE BEFORE IMPLEMENTATION — 2 High, 5 Medium.**

The fixed tile grid is the right next structure, but the cache cannot have one `Pending | Ready`
state while deciding prototype content from the tile's distance at first load. That makes a retained
tile path-dependent and incomplete when it later enters a shorter prototype's range. The design also
needs a measured throughput/prefetch contract before it can claim full-speed flight.

### What Came Back Clean

- A fixed cube-face tile level is a sound parent partition for the current level-8 through level-12
  prototype cells, provided `Configure` validates `Lt <= every prototype level`.
- `GatherTile` should reuse the current hash, biome memo, sampler, and placement gates. No second
  placement algorithm, factory, or GPU path is justified in this slice.
- One background worker producing local results and main-thread cache commits matches the current
  immutable-snapshot and Unity ownership rules.
- Lever A, edit persistence, GPU generation, parallel workers, and a special trees-first pass are
  correctly outside the first slice. Keep them deferred until tile backlog measurements demand one.
- A separate hard memory cap is not needed initially. A geometrically bounded live/retention set plus
  removal of stale queued work already bounds memory; add a cap only if measured resident bytes exceed
  an agreed budget.

### Findings

#### I1 — Track readiness per tile and prototype

- **Category:** Bug
- **Severity:** High
- **Description:** A tile has one `Pending | Ready` state, but its contents are selected from the
prototype culls at the camera position where it was first gathered. A far tile can therefore become
`Ready` with only coarse prototypes. If the camera approaches while that tile remains cached, it is
not a new tile and is never enqueued again, so its bushes, grass, flowers, or other short-range
prototypes remain permanently absent. The reverse travel path can produce a different cached payload.
- **Evidence:** The single tile state and conditional content are specified at
`docs/design/2026-07-29-scatter-incremental-gather.md:58-77`. The live gather deliberately gives each
prototype its own radius at `Assets/Scripts/Planet/Scatter/ScatterField.cs:116-124,160-197`; current
assets include 90 m grass beside 520 m far-tree/impostor coverage.
- **Impact:** Visible placement becomes dependent on travel direction and first encounter, breaking
the determinism/partition invariants even though every individual placement decision remains pure.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Make the work and readiness unit `(tileId, prototypeIndex)`. A tile entry may
hold per-prototype payloads plus a ready bit mask; enqueue a missing pair whenever that prototype's
required tile set includes it. Gather the complete prototype-cell payload for that parent tile, never
a camera-clipped subset.
- **Refactor Option:** With at most 64 prototype slots, `ulong readyMask` plus per-prototype lists is
enough. Keep pending work in a separate `HashSet<WorkKey>`; do not introduce a generic job system.
- **Behavior note:** Preserving when the draw-filtered tile union is exactly equal to the current
whole-disc result.

#### I2 — Prove that frontier throughput and prefetch lead close at the target speed

- **Category:** Architecture
- **Severity:** High
- **Description:** Incremental work is bounded by frontier *throughput*, not independent of speed.
The proposed max-region set also leaves no stated prefetch lead for the farthest prototype: its far
draw radius is already the ~520 m max region, while the design asks for cull plus one tile of margin.
Those outer tiles cannot be both outside 520 m for prefetch and inside a 520 m live set.
- **Evidence:** The unbounded-speed claim is at
`docs/design/2026-07-29-scatter-incremental-gather.md:92-97`; queue backpressure remains open at
`:130-132`. The measured whole gather is 5.9-6.1 s at
`docs/design/2026-07-29-scatter-gather-perf.md:375-384`, and current far gather radii derive from mesh
cull/impostor end at `Assets/Scripts/Planet/Scatter/ScatterDtos.cs:80-98`. Even using the most
optimistic radius for every unit of work, steady worker demand is at least
`6 s * 2v / (pi * 520 m)`: about 0.73 worker-seconds per real second at 100 m/s and 2.2 at the
300 m/s sprint, before tile overfetch, queue, and bucket costs. The actual demand is higher because
many dense prototypes use much smaller radii.
- **Impact:** The queue can grow without bound and newly visible props can still arrive late; the
speed cap could be lifted on a structural argument that cannot meet sprint throughput on one CPU
worker.
- **Effort:** M
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Recommendation:** Define each prototype's required radius as its actual far draw end plus an
explicit prefetch lead. Instrument tile candidates, p50/p95 tile time, queue depth, oldest required
work age, missing required `(tile, prototype)` pairs, live instances, and resident bytes during a
fixed-seed straight 100 m/s flight. Pass only when required pairs become ready before crossing the
draw boundary and backlog stays bounded. Keep sprint capped unless the same test passes at 300 m/s.
- **Refactor Option:** Extend the existing `scatter.*` diagnostics/logging; do not add a profiling
framework. Start with one sequential worker and `Lt=7`.
- **Behavior note:** Preserving. A larger prefetch radius changes memory/work, not placement.

#### I3 — Do not claim cube-corner partition coverage from the current range builder

- **Category:** Bug
- **Severity:** Medium
- **Description:** The design says face seams are already handled and requires no tile-seam drops, but
the shared range builder explicitly leaves cube-corner straddles uncovered. A normal center/edge
partition check will not exercise that gap.
- **Evidence:** The claim is at
`docs/design/2026-07-29-scatter-incremental-gather.md:49-54`; the live builder documents the
unhandled case and returns `UncoveredCornerStraddle` at
`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs:3-14,31-40,169-189`. The current gather only
surfaces that flag (`ScatterField.cs:166-167`).
- **Impact:** Props can remain missing around cube corners while the new partition verification passes
elsewhere and the design declares seam correctness.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Either make corner completion a prerequisite with its own behavior/visual gate,
or explicitly retain the known corner gap and narrow invariant 2. The partition diagnostic must run
at face center, edge, and corner anchors and report the corner status separately. Do not silently
change the grass overload as incidental scatter work.
- **Refactor Option:** If approved, fix the local range enumeration in one focused slice; only unify
the duplicated grass/scatter overloads under a separate capture-gated cleanup.
- **Behavior note:** Fixing the gap adds instances in a previously uncovered band; that is a small,
intentional placement change requiring approval.

#### I4 — Define tile payload and partition equality without a camera-dependent clip

- **Category:** Architecture
- **Severity:** Medium
- **Description:** “Union of tiles equals the whole-disc gather” is not true if a tile payload contains
the whole tile: boundary tiles necessarily add candidates outside the disc. Clipping `GatherTile` to
the current camera disc would make the supposedly persistent payload depend on the camera that first
loaded it. In addition, `FaceSpaceCellRangeBuilder` emits a conservative square plus an extra cell
ring and relies on downstream candidate-distance clipping; it does not return an exact set of tiles
overlapping a disc.
- **Evidence:** The absolute equality invariant is at
`docs/design/2026-07-29-scatter-incremental-gather.md:35-40`; whole-tile gathering is proposed at
`:71-77`. The live builder creates a square and `+1` ring at
`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs:153-167`, while exact ROI clipping currently
happens per candidate at `Assets/Scripts/Planet/Scatter/ScatterField.cs:138-140,194-198`. The `Lt`
parent assumption is stated but not given a validation rule at
`docs/design/2026-07-29-scatter-incremental-gather.md:49-52`.
- **Impact:** An executor must choose between a false verification target and a path-dependent cache;
future settings overrides can also introduce a prototype level coarser than `Lt` and invalidate the
parent mapping.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Recommendation:** Define a tile payload as every candidate cell whose fixed parent is that tile.
The proof should be:
`FilterToPrototypeDisc(Union(required tile payloads)) == current whole-disc gather`, comparing IDs
and transforms. Treat builder output as a conservative required set and include its overfetch in the
performance counters. At `Configure`, either choose `Lt = min(7, minimum prototype level)` or fail
loud when the configured levels violate the chosen fixed level.
- **Refactor Option:** One pure parent-key helper
`ParentTile(level, x, y, tileLevel)` is sufficient; no tile-coordinate interface is needed.
- **Behavior note:** Preserving.

#### I5 — Avoid rebuilding every prototype bucket on every tile completion

- **Category:** Complexity
- **Severity:** Medium
- **Description:** The design forbids a per-frame O(all instances) rebuild, then rebuilds every
prototype bucket whenever the live-tile set changes. Initial fill, teleport recovery, and sustained
backlog can complete tiles on many consecutive frames without any `Lt` crossing, so “only on tile
crossing” is not a valid bound.
- **Evidence:** The invariant and rebuild proposal conflict at
`docs/design/2026-07-29-scatter-incremental-gather.md:41,79-85`. The current whole-buffer
`SwapAndBuildMatrices` clears every prototype and revisits every instance at
`Assets/Scripts/Planet/Scatter/ScatterRenderer.cs:182-197`; it is cheap today because it runs once
per six-second whole gather, not once per tile result.
- **Impact:** A successful placement optimization can move the bottleneck to repeated main-thread
matrix reconstruction and allocation churn, reintroducing travel hitches.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Append matrices/positions only for the completed tile-prototype payload. Batch
all evictions from one cache reevaluation, collect the affected prototypes, and rebuild only those
prototype buckets once from remaining live tiles. Measure that version before considering per-tile
draw buckets or tombstone/compaction machinery.
- **Refactor Option:** Keep one flat draw bucket per prototype plus tile-owned source payloads. This is
smaller than changing `ScatterLodBatcher` to understand tiles.
- **Behavior note:** Preserving if rebuilt buckets are compared by ID/transform rather than list order.

#### I6 — Make stale, failed, and cancelled queue work retryable

- **Category:** Maintainability
- **Severity:** Medium
- **Description:** `Pending | Ready` has no defined transition for a gather exception, cancellation,
eviction while queued, or a result completed after `Configure`. A stale pending entry can remain
unretryable, while a queue built for an old camera direction can spend scarce worker time on tiles
that are no longer required. “One/few tiles” also leaves accidental parallel access to the shared
sampler/biome pipeline open.
- **Evidence:** Queue/state/lifecycle are specified at
`docs/design/2026-07-29-scatter-incremental-gather.md:56-69,87-90,130-134`. The current renderer has
one worker, catches failures, and checks its captured token before swap at
`Assets/Scripts/Planet/Scatter/ScatterRenderer.cs:136-179`; the tile design replaces that lifecycle
and must preserve its stale-result guard per result.
- **Impact:** Failures can create permanent holes, old-world results can contaminate a new cache, and
fast direction changes can starve useful frontier work.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Use one sequential worker. Keep ready payloads in the cache and pending keys in a
separate set. Rebuild a small sorted latest-required work list on each reevaluation; priority should
be signed distance to the prototype's draw boundary, then distance. Produce into a worker-local list,
commit on the main thread only when the captured token/epoch is current, and remove the pending key
in `finally` on every exit so the next reevaluation can retry it.
- **Refactor Option:** A concrete `ScatterTileCache` plus a small `TilePrototypeWork` value type is
enough. Do not add worker interfaces, multiple workers, or a custom priority-queue abstraction.
- **Behavior note:** Preserving; error recovery improves.

#### I7 — Use the surface-edit pattern, not `SurfaceEditStamp`, for scatter overrides

- **Category:** Architecture
- **Severity:** Medium
- **Description:** The persistence hook says `SurfaceEditStamp` becomes the tile-scoped scatter
override. That repeats a dependency claim already withdrawn in the accepted gather-performance
feedback. The live stamp is terrain/path state and has no scatter ID, prototype, explicit transform,
or interaction state. Tile membership should be a derived index, not the identity of an edit.
- **Evidence:** The claim appears at
`docs/design/2026-07-29-scatter-incremental-gather.md:27-28,106-111`; its withdrawal is recorded at
`docs/design/2026-07-29-scatter-gather-perf.md:339-346`. The live type is
`Assets/Scripts/Planet/Surface/SurfaceEditController.cs:608-628`; stable scatter persistence identity
already lives in `Assets/Scripts/Planet/Scatter/ScatterId.cs:3-7`. The placement-system design says
to reuse the *pattern* at `docs/design/2026-07-20-scatter-placement-system.md:20-52,127-129`.
- **Impact:** A later persistence slice could overload the terrain ledger, couple unrelated save
schemas, and lose the stable-ID semantics needed when tiles are evicted/regenerated.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Recommendation:** Replace the wording with “a scatter override log following the
SurfaceEditStamp source-of-truth pattern.” SP5 should define its own record keyed by `ScatterId`;
player additions also store prototype, transform, and timestamp. A tile-to-overrides lookup may be a
derived runtime index.
- **Refactor Option:** None in this slice; YAGNI.
- **Behavior note:** Future persistence only; no current runtime behavior change.

### Refactoring Plan

1. Fix the contracts first: choose `Lt=7` for the first measured version, validate it against every
   configured prototype level, define whole-tile payloads, and change partition proof to
   draw-filtered equality.
2. Make `(tileId, prototypeIndex)` the work/readiness unit. Derive each prototype's conservative
   required tile set through the existing range builder using its far draw end plus explicit prefetch
   lead.
3. Extract one pure `GatherTilePrototype` path from `ScatterField`: enumerate only the prototype cells
   whose parent is the tile, retain the current hash/gates exactly, and do no camera-radius clipping.
4. Add one sequential latest-required worker with worker-local output, captured token/epoch checks,
   main-thread commit, and unconditional pending cleanup.
5. Append completed payloads to their prototype draw buckets. Batch evictions and rebuild only
   affected prototypes once per reevaluation.
6. Extend `scatter.verify` with center/edge/corner partition checks and exact ID+transform comparison.
   Add production counters for tile time, candidates, queue age/depth, missing required pairs, live
   instances, and resident bytes.
7. Run the fixed-seed, fixed-tier 100 m/s flight gate. Keep the current cap for any speed whose
   backlog or visibility deadline fails. Add Lever A, trees-first staging, parallel workers, or a hard
   memory cap only in that order of measured need.
8. Correct the persistence wording now; leave the actual scatter override schema to SP5.

Functionality is preserved only if the draw-filtered tile union matches the current Tier 1a accepted
IDs and transforms at the same seed/pose. Fixing the known cube-corner gap is the one explicit
placement addition and must be approved separately.

### Prior Audit Reconciliation

| Prior item | Status | Current evidence |
|---|---|---|
| 2026-07-25 F1 — LOD-dependent ground sampling | RESOLVED | Tile gathering reuses the current deterministic `ISurfaceGroundSampler` path. Lever A remains a separate, behavior-reviewed sampler change. |
| 2026-07-25 F2 — synchronous main-thread gather | RESOLVED | The design retains off-thread placement and main-thread publication; I6 specifies the per-tile stale-result lifecycle. |
| 2026-07-25 F4 — far-field cap/LOD | PARTIAL, UNAFFECTED | Tile streaming changes generation freshness, not the accepted far representation/quality work. |
| 2026-07-26 N1 — repeated full-list draw scan | RESOLVED IN CURRENT CODE | Current per-prototype buckets remain sound. I5 prevents the tile migration from replacing that win with repeated full rebuilds. |
| Gather-perf P1 — placement contract | RESOLVED | Tier 1a's one-time layout change was approved and landed; this migration must preserve that current layout exactly. |
| Gather-perf P2/P4 — level-aware memo and sampler/gate ownership | RESOLVED | The live code uses `sampleLevel=min(level,9)`, a level-bearing local memo key, split sampling, and one shared altitude/water predicate. |
| Gather-perf P3 — production freshness budget | PARTIAL | The production 6 s baseline now exists; I2 supplies the missing frontier/backlog acceptance contract. |
| Gather-perf P5/P6 — biome atlas and premature GPU path | RESOLVED | Biome-atlas Tier 1c was dropped and GPU generation remains deferred. The radius-atlas Lever A is distinct and still measurement-gated. |
| Consolidated audit G8 — corner range risk | OPEN / KNOWN | The current builder still reports an uncovered corner straddle; I3 prevents this design from silently declaring it solved. |

### Questions for the User

None required to proceed. Recommended defaults are `Lt=7`, one sequential worker, and unsprinted
100 m/s as this slice's acceptance target. Keep the cap for sprint until the same backlog/visibility
gate passes; defer a hard memory cap and trees-first refinement until counters show they are needed.

---

## Resolution — 2026-07-29 (Codex feedback accepted)

All seven findings are correct and adopted; defaults (`Lt=7`, one sequential worker, unsprinted 100 m/s
gate) accepted. Revised contracts before implementation:

**Invariants (revised).**
- (1) The draw-filtered tile union must equal the **current Tier 1a whole-disc accepted IDs + transforms**
  at the same seed/pose. That is the concrete determinism target.
- (2) Partition proof is `FilterToPrototypeDisc(Union(required tile payloads)) == whole-disc gather`
  (compare IDs + transforms), **evaluated at face-centre, edge, AND corner anchors**, with the corner
  status reported separately. The cube-corner straddle stays a **known, retained gap** (I3): invariant 2
  is narrowed to exclude it; fixing it is a separate, approval-gated placement change, not part of this
  slice.

**Tile / payload (I1, I4).**
- Work + readiness unit is `(tileId, prototypeIndex)`. A `TileEntry` holds per-prototype payload lists +
  a `ulong readyMask` (≤64 slots). A missing pair is enqueued whenever that prototype's required tile set
  includes it — a far tile that entered "trees-only" re-enqueues its bush/grass pairs as the camera closes.
- Tile payload = every candidate cell whose **fixed parent** is that tile — the **whole tile, never a
  camera-clipped subset**. One pure helper `ParentTile(level, x, y, tileLevel)`.
- `Configure` picks `Lt = min(7, min configured prototype level)` and **fails loud** if any configured
  level is `< Lt` (settings override could otherwise coarsen a prototype below `Lt`).

**Throughput / prefetch (I2).**
- Drop the "speed-independent" claim. Each prototype's required radius = its **actual far draw end +
  explicit prefetch lead** (the farthest prototype's live region grows past 520 m to give the lead).
  The required tile set per prototype comes from the range builder at `Lt` using that radius (treated as a
  conservative superset; overfetch counted).
- Acceptance = a fixed-seed straight **100 m/s** flight where every required `(tile, proto)` is Ready
  before the camera crosses that prototype's draw boundary and backlog stays bounded. Sprint (300 m/s)
  stays capped until the same gate passes there.

**Worker / lifecycle (I5, I6).**
- One sequential worker. Ready payloads live in the cache; pending keys in a separate `HashSet<WorkKey>`.
  Each reevaluation rebuilds a small sorted "latest-required" work list; priority = signed distance to the
  prototype's draw boundary, then distance. The worker produces into a worker-local list and commits on the
  main thread **only if the captured epoch/token is current**, removing the pending key in `finally` on
  every exit (exception / cancel / evict-while-queued / stale-after-`Configure`) so it stays retryable.
- Draw buckets: **append** the completed `(tile, proto)` payload to that prototype's bucket. Batch all
  evictions from one reevaluation, collect affected prototypes, and rebuild only those buckets once. No
  full O(all instances) rebuild per completion.

**Persistence wording (I7).** A future scatter override log **follows the SurfaceEditStamp source-of-truth
pattern**, keyed by `ScatterId` (player additions also store prototype, transform, timestamp);
tile→overrides is a derived runtime index. `SurfaceEditStamp` itself (terrain/path state) is not the
scatter record. Out of scope for this slice.

**Deferred, in order of measured need:** Lever A (baked-height atlas), trees-first staging, parallel
workers, hard memory cap.

### Implementation order (revised, per Codex)

1. **Contracts (pure, EditMode-testable):** `ParentTile(level, x, y, tileLevel)` + `Lt` validation;
   extract `GatherTilePrototype` from `ScatterField` (enumerate only the prototype cells whose parent is
   the tile, same hash/gates, no camera clip). Unit-test parent mapping + that the union of a region's
   tile payloads (draw-filtered) equals the current whole-disc gather.
2. **Cache + worker:** `ScatterTileCache` (dict + `HashSet<WorkKey>` + one sequential worker + reeval /
   enqueue / evict, epoch-checked commit, `finally` cleanup) replacing the whole-disc double-buffer.
3. **Draw:** append-on-commit buckets; batched evict-rebuild of affected prototypes only.
4. **Verify + counters:** centre/edge/corner partition checks (corner reported separately) + exact
   ID/transform equality; `scatter.*` counters (tile time, candidates, queue depth/age, missing required
   pairs, live instances, resident bytes).
5. **Flight gate:** fixed-seed straight 100 m/s; keep the cap where the backlog/visibility deadline fails.
   Add Lever A / staging / parallelism / memory cap only in that measured order.
