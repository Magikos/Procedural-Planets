# Scatter Placement System — Design

Status: design (2026-07-20; amended 2026-07-22 after a Codex plan audit — stable
prototype id, underwater-aware altitude/water model, fixed-level-per-world
clarification, and CPU/GPU biome-parity decision). Branch `scatter-placement`.

A world-object placement system for discrete props on the procedural planet:
trees, boulders, bushes, rocks, flowers, seaweed, and the parked Synty grass
clumps. Objects sit at stable world points so the player can walk up to them,
and some (trees, rocks) can be interacted with (chop / collect). Generic enough
to later populate child objects at a parent's base (rocks/flowers under a tree)
and, eventually, clutter on indoor surfaces (desks, shelves).

This document specifies the whole system's shape and details the first
buildable slice (SP1). Later slices capture their seams here so decisions are
not lost, but each gets its own spec + plan before it is built.

## The spine

Two ideas carry the whole system:

1. **Placement is a pure function, not stored data.** Every object's existence,
   position, type, rotation and scale is `hash(worldSeed, node, slot)`, gated by
   biome / slope / altitude. Instances near the camera are *regenerated* from the
   hash, exactly like the near-field grass. Nothing stores millions of
   transforms; a given cube-face location always yields the same objects (this is
   what kills the parked Synty clumps' popping/sliding), and there is nothing to
   persist because it is derived.
2. **Interactions are a sparse override log.** Chopping or collecting writes a
   small record keyed by the object's stable id; regeneration consults overrides
   before emitting. This is exactly the model the terrain already uses —
   `SurfaceEditController` / `SurfaceEditStamp`: the base is deterministic, saved
   *edits* are the source of truth, everything else is a derived cache.

**Worked scenario this must support** (drives SP3 + SP5): a forest present at
game start (the pristine base), hand-chopped over time to stumps + a few
survivors, with the player planting new trees — and when the player travels far
and looks back at that hill, the *distant* view shows the chopped forest, not a
restored one. This is supported, and it pins one architectural rule: **there is
one override log, and it feeds BOTH placement paths (CPU near band and GPU far /
impostor render).** If the GPU far path regenerated from the hash alone it would
ignore edits and magically restore the forest in the distance — the override log
must reach the placement kernel. Three mutation kinds, all bounded by human
effort (hundreds–low-thousands of edits → tens of KB, still "sparse"):
- **Restate a base instance** by its deterministic id: tree → stump, tree →
  gone. Partial clearing = a subset restated.
- **Player-placed additions**: a planted object is not in the hash field, so it
  carries a separate *player-object* id and an **explicitly stored transform +
  prototype** — the one place transforms are stored, bounded by human effort.
- **Growth over time**: store the chop/plant timestamp; effective stage =
  `f(base, override, worldTime)`. Regrow-vs-permanently-cleared is an SP5
  gameplay policy; storage already supports it.

Impostors are **per-prototype** atlases (tree, stump, …), not per-instance
captures, so "the distant impostor updated" means the kernel now emits stump
instances that draw the stump atlas — no re-bake, automatically live.

The organizing structure is a **deterministic quadtree per cube face**, where
one axis — tree depth — simultaneously is **density**, **LOD**, and **spatial
query**:

- Each cube face is a root node; nodes subdivide 4-way. A node is identified
  purely by `(face, level, nodeX, nodeY)`, seeded `nodeSeed = hash(worldSeed,
  face, level, nodeX, nodeY)`. Nodes are independent — no cross-node state — so
  output is invariant to query order or region (determinism is trivial).
- Coarse levels drop sparse heroes (trees, boulders); deep levels drop dense
  groundcover (flower/tuft props). Density *tiers emerge from depth* rather than
  from separate grids.
- A query descends only where the region-of-interest overlaps, and only as deep
  as camera distance warrants. Far → shallow (heroes only, few instances);
  near → deep (everything). **Distance density-thinning and LOD selection are
  the same depth cutoff.**
- **Placement level is a world property, not a query property.** A prototype's
  `spacingMeters` fixes its quadtree level *once per generated world* (from a
  canonical metric — the grass reference `2·planetWorldRadius`), so a given area
  always uses the same cells and therefore the same ids. "LOD = depth" means only
  *which fixed levels a query draws* changes with distance — the placement math
  never re-derives a level from the camera position (that would make ids move as
  you walk). Because a fixed UV level maps to varying world spacing across a cube
  face, density is evened out with the existing grass cube-face area-keep
  probability (`GrassNearFieldPlace.compute`), not by re-choosing the level.

## Scope boundary (important)

This system places **discrete props**, counted in the hundreds-to-low-thousands
near the camera. It does **not** replace the grass-*blade* compute carpet, which
stays on its existing compute path (`GrassNearFieldPlace.compute` and friends).
Synty grass *clumps* become one scatter prototype; individual grass blades do
not. This keeps per-candidate counts low enough that a CPU deterministic core is
the right authority.

**The grass-vs-scatter line is a rendering/perf boundary, not an interactivity
one.** The deciding question for a given element is *"is it generated blade
geometry that should bend when stepped on?"* (→ grass-blade system) versus *"is
it an authored mesh wanted with LOD/impostor at distance?"* (→ scatter). Both
systems hold non-interactable things:

- **Grass-blade system** owns dense, generated, bendy detail — base grass, tall
  grass, weeds, reeds, simple bladed flowers — as per-clump *variation profiles*
  (height/width/color/bend archetypes). It's millions of instances as one draw,
  and it already gives trample / wind / path-overwrite for free; re-implementing
  that on scatter meshes would be wasteful. (This "variation profiles" work is a
  **grass enhancement, a sibling to this system — not part of it, and not now.**)
- **Scatter system** owns authored-mesh detail — mushroom/coral clusters, modeled
  flower blooms, Synty grass clumps, decor pebbles — as prototypes, frequently
  with `interaction = none`. Non-interactable mesh detail is a first-class
  scatter case, not a reason to push it into the grass system.

So a bladed weed is grass even though it is non-interactable; a modeled mushroom
cluster is scatter even though it is non-interactable. Reference look:
Scrap Mechanic ground cover (2026-07-20).

## Decomposition

Each is its own spec + plan before building.

- **SP1 — CPU deterministic placement core.** Sampler-free placement math +
  quadtree gather + `scatter.verify`. Emits an instance stream; no rendering, no
  GameObjects. The **reference authority** every later slice validates against.
  *This document details SP1.*
- **SP2 — Mesh-LOD instanced rendering** of CPU-placed near/mid instances.
- **SP3 — GPU compute placement mirror** for visual-only far/dense tiers:
  indirect draw, reads a baked biome map + surface height, no readback. Mirrors
  SP1's math in HLSL.
- **SP4 — Octahedral impostors**: per-prototype multi-view atlas bake +
  view-blend shader for the far band, so trees hold to the horizon.
- **SP5 — Interaction + persistence** (near band): pooled GameObjects with
  colliders, chop/collect verbs, override log via the surface-edit pattern,
  regrow.
- **SP6 — Hierarchical child scatter**: a placed object carries its own local
  deterministic scatter (tree → base rocks/flowers).
- **SP7 — Generic surface provider**: abstract the anchor surface (planet
  cube-face vs a flat furniture surface) so the same core populates indoor
  clutter. Interface seam only until needed.

## SP1 — CPU deterministic placement core

### Data model

`ScatterPrototype` (authoring `ScriptableObject`; runtime reads an immutable DTO
per the settings pattern):

- **`slotId` — an explicit, immutable small integer (0–15) that identifies this
  prototype in the id scheme.** This is *not* the library array index. It is
  hashed into `nodeSeed`/`slotSeed` and packed into the stable id, so it is a
  **persistence key** (see §Stable id): once assigned it is never reused or
  reordered, or saved chop/collect overrides would rebind to the wrong prototype
  and chopped objects would resurrect. Boot validates `slotId`s are non-null,
  in-range, and unique; it **fails loud** rather than truncating an oversized
  library. The runtime array index stays only a lookup (`prototypeIndex`).
- `spacingMeters` — human unit; fixed to one quadtree level per generated world
  via the canonical metric (spine rule above), not per query.
- target biome + `biomeBlendPower` — density falloff toward biome borders
  (mirrors `BiomeDefinition.GrassBiomeBlendPower`). **SP1 simplification: one
  biome per prototype**; a weighted biome list is deferred until a prototype
  actually needs it (YAGNI).
- **Altitude band as an explicit bounded range** `minAltitudeMeters` /
  `maxAltitudeMeters`, each with its own `hasMin` / `hasMax` flag. Metres are
  **signed** — negative = below sea — so an underwater ceiling (seaweed only
  below the waterline) is expressible. No "≤0 means infinity" sentinel; absence
  of a bound is the explicit flag, so any real value, negative included, is a
  bound.
- `minWaterClearanceMeters` — placement gate; land props must sit at least this
  far above the waterline. Applied only when the world has oceans.
- slope range (`maxSlopeDegrees` + soft `slopeFadeDegrees`) — placement gate.
- `weight` — for SP1, an **independent per-prototype density multiplier** (not
  mutually-exclusive selection): each prototype rolls its own acceptance. If
  weighted exclusive selection among same-slot prototypes is ever needed, it gets
  its own spec.
- scale-jitter range and yaw-jitter (SP1: full random yaw on/off; a yaw *range*
  is deferred until needed).
- `interaction` verb `none | collect | chop` — metadata only in SP1; SP5
  consumes.
- child-scatter table — empty in SP1; SP6 consumes.

`ScatterLibrary` (authoring SO) holds the prototype list; runtime reads its DTO.

### The placement math (HLSL-portable, sampler-free)

The core placement decision is a single small **pure function** with no managed
calls:

```
TryPlace(nodeSeed, slot, candidateHeight, biomeWeight, prototypeRules)
    -> (accepted, positionWS, rotation, scale)
```

It takes surface height and biome weight as *inputs*. The CPU gather samples the
managed providers, then calls it; the SP3 compute kernel samples baked textures,
then calls the HLSL mirror. This isolates the entire CPU↔GPU parity surface to
one function — the same discipline that keeps `CubeFaceToUnitSphere` mirrored
between C# and the grass compute today. Parity of this function is the primary
engineering risk and gets a dedicated equivalence check when SP3 lands.

### Gather (the query)

`Gather(regionOfInterest, maxLevelForDistance, buffer)`:

1. Enumerate the cube-face cells overlapping the region via
   `FaceSpaceCellRangeBuilder`, whose ranges are a **conservative square**, then
   **clip every candidate to the exact circular ROI** before any expensive
   sampling — so a region query never emits outside its promised disc and counts
   from different spots are comparable. Edge straddle pulls in the neighbor face;
   the builder's `UncoveredCornerStraddle` flag is **surfaced, not discarded** —
   `scatter.verify` fails on it so incomplete cube-corner coverage can't be
   called deterministic. (Solving 3-face corner straddle in the shared builder is
   a separate infra improvement that also benefits grass; SP1 fails loud instead
   of silently under-covering.) Only fixed levels ≤ `maxLevelForDistance` are drawn.
2. Per node, per prototype at that level: derive `slotSeed` from `nodeSeed` +
   the prototype's `slotId`, compute a **slot-seeded** jittered UV in the node's
   square (so two same-level prototypes never land at the same point), project to
   the unit-sphere direction.
3. Sample `IPlanetSurfaceSampler.TryGetSurfaceRadius` (height) and
   `IBiomeProvider.EvaluateBiome` (primary/secondary biome + blend weight).
4. Call `TryPlace`. Gate rejects on slope / altitude / water clearance / biome
   mismatch. Density accept test:
   `hash01(slotSeed) < membership(point, prototype.biome) ^ biomeBlendPower`
   — as membership falls toward a border, more slots reject, so trees thin out
   smoothly across the cross-bleed. No hard biome edge.
5. Emit accepted `ScatterInstance { ulong id, float3 posWS, quaternion rot,
   float scale, int prototypeIndex }` into the caller-owned buffer.
   Allocation-free.

The instance stream is **LOD-agnostic** (no mesh/impostor choice in it); the
renderer derives LOD from camera distance. So the far-LOD strategy (impostors)
needs no SP1 change.

### Stable id

`id = pack(face, level, nodeX, nodeY, slot, playerBit)` → `ulong`, where **`slot`
is the prototype's immutable `slotId`, never the library array index.** This is
the persistence key SP5 writes chop/collect overrides against, and the identity
that lets a GPU far-drawn tree and a CPU near-spawned GameObject agree they are
the same object. Because `slot` is the stable `slotId`, **reordering or inserting
prototypes in the library does not move any existing id** — a saved chop stays
bound to the same prototype. One `playerBit` distinguishes **base** ids
(derivable from the hash) from **player-placed** ids (additions with a stored
transform), so the two namespaces never collide. Bit budget (u64): face 3, level
5, x 24, y 24, slot 4, player 1 = 61 bits.

### Dependencies

`IPlanetSurfaceSampler`, `IBiomeProvider`, and `FaceSpaceCellRangeBuilder`
(topology + `ComputeMetersPerUV`). Plain deterministic C#: the managed sampler
calls preclude Burst in the SP1 inner loop, which is fine at prop counts. If
near-band gather ever costs too much, the mitigation is the SP3 GPU path for the
visual tiers, not Burst-ifying SP1.

### Verification (project idiom — no test framework)

`scatter.verify` console command — a proof that cannot pass on an empty or
duplicate-emitting gather:

- Gathers **one explicit fixed ROI** twice, enumerating cells in forward and
  reverse order, and asserts: nonzero output, unique ids (no duplicates), exact
  id-set equality, and identical prototype/position/rotation/scale for every id
  (order-independence → determinism).
- Proves region independence by comparing a smaller gather against the larger
  gather filtered to the smaller ROI (not two different regions).
- Round-trips id pack/unpack and the player bit.
- Reports candidate-vs-accepted counts at face **center, edge, and corner** to
  validate density uniformity across cube-face distortion. Corner straddle is a
  **deliberately-accepted SP1 gap** (Bryan's scope call 2026-07-22): the proof
  **reports** it (e.g. `PASS_WITH_KNOWN_CORNER_GAP`) rather than failing, and never
  claims complete cube-corner coverage. The shared 3-face-corner fix is its own
  later slice. (Per-membership-bin histograms are a deferred diagnostic nicety —
  biome-border falloff is already shown by `scatter.count` mid-biome vs at a border.)
- Prints candidate count + elapsed ms and honors a proof-radius ceiling **plus a
  candidate-budget preflight**, so the diagnostic itself can't hitch (measure
  before optimizing — no jobs/Burst until a real number demands them).

### Explicitly NOT in SP1

Rendering, GameObjects, colliders, persistence writes, impostors, GPU compute,
child scatter, furniture surface. SP1 defines the id scheme and the placement
math those all depend on.

## Later-slice seams (captured, not built)

- **Coarse "scatter control" texture** (SP3 input): a low-res per-face RGBA field
  (biome/type + density) sampled by the compute kernel instead of re-evaluating
  biome noise per candidate. This is a placement *input*, never a per-object
  store — a per-object texture's texel resolution would cap density and
  re-introduce grid clumping, so placement stays procedural (single source of
  truth). Hand-painted forests arrive later as sparse overrides (SP5), not as a
  placement texture.
- **Tier split for interaction:** groundcover + far/mid visual-only instances go
  GPU compute → indirect draw, no readback (SP3); the near interactable band
  stays CPU (small count) → real ids → GameObjects / colliders / persistence
  (SP5). Both run identical hash/gate math via the shared `TryPlace`.
- **Override log feeds both paths:** the sparse override log + player-placed
  additions upload to the GPU placement kernel as buffers (bounded → a few
  thousand entries; per-candidate lookup via a bucketed/sorted structure, an SP3
  detail). Same log the CPU near band reads. This is what keeps the distant
  forest consistent with the player's edits.
- **Impostor band (SP4), two strategies:**
  - **A — per-prototype impostors (default).** One octahedral atlas per prototype
    (tree, stump, …), baked once offline, reused by every instance. The far
    forest is thousands of instanced quads each sampling a prototype atlas; a
    chop just makes the kernel emit a *stump* instance, so the far view is live
    with **no runtime bake**. Planted/cleared/growth all fall out as
    different-prototype instances. The depth cutoff caps far instance counts.
    Watch item: transparent-quad **overdraw** at distance (not vert count — grass
    already draws far more geometry).
  - **B — per-region baked cards (escalation, only if A's overdraw profiles as a
    bottleneck).** Bake a distant region's whole forest into a few billboards /
    a proxy card; far cost drops to a handful of cards per region. A region's
    card goes stale on edit, so **re-bake the dirty region's card while the player
    is still near it** (card not yet visible), driven by the override log marking
    dirty regions. The bake is a GPU off-screen render (amortized over a frame or
    two), not a CPU background thread. The deterministic-base + override-log
    design supports this directly: a card bake renders that region's
    override-aware placement. Held in reserve; not built unless A falls short.

## Risks / open questions

- **CPU↔GPU placement parity** (SP3): the one real correctness risk. Mitigated by
  the single shared `TryPlace` function + a dedicated equivalence check. **Design
  decision (long-term):** the CPU authority and the GPU mirror must sample **one
  shared biome field** — the baked biome map (`BiomeMapBaker` output) — *not* one
  analytic (`ColorGenerator.EvaluateBiome`) and one baked. Sampling different
  biome sources would let placement diverge at borders (CPU near vs GPU far), so
  the near band and the far band would disagree about where a forest ends. SP1's
  CPU authority may call `EvaluateBiome` to bootstrap, but SP3 moves *both* onto
  the baked field so they cannot drift. (Track: confirm the baked map's resolution
  resolves the border falloff the CPU authority produces.)
- **Cube-face distortion**: a fixed UV level maps to varying world spacing across
  a face; density is evened with the grass cube-face **area-keep probability**
  (decision made above), not by re-choosing levels per query. `scatter.verify`
  runs its density profile at face center, edge, and corner to validate uniformity.
- **Cube-corner straddle** (shared infra): `FaceSpaceCellRangeBuilder` does not
  cover 3-face corners today (a known limitation that also thins grass at corners).
  SP1 surfaces the flag and fails loud; a proper fix is a shared-infra improvement
  benefiting both systems, scheduled on its own, not inside SP1.
- **Impostor bake fidelity** (SP4): lighting/depth correctness of the view-blend
  is its own investigation; MVP fallback is mesh-LOD-only, added later without
  redesign since instances are LOD-agnostic.
