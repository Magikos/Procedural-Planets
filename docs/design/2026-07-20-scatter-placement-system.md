# Scatter Placement System — Design

Status: design, approved to write (2026-07-20). Branch `code-refactor`.

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

- `spacingMeters` — human unit; mapped to a quadtree level via the existing
  `FaceSpaceCellRangeBuilder.ComputeMetersPerUV` so spacing is ~uniform in world
  space despite cube-face distortion. (Consistent with the console human-unit
  convention.)
- target biome(s) + `biomeBlendPower` — density falloff toward biome borders
  (mirrors `BiomeDefinition.GrassBiomeBlendPower`, "density response to top-K
  blend weights").
- slope range, altitude range, min water clearance — placement gate.
- `weight` — relative pick weight when multiple prototypes occupy a level.
- scale-jitter and yaw-jitter ranges.
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

1. Descend the quadtree of each cube face overlapping the region. Edge straddle
   pulls in the neighbor face's tree; reuse `FaceSpaceCellRangeBuilder` topology
   (corner straddle is already flagged there). Descend only to
   `maxLevelForDistance` (LOD = depth).
2. Per node, per prototype registered at that node's level: derive the slot,
   compute a jittered UV in the node's square, project to the unit-sphere
   direction.
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

`id = pack(face, level, nodeX, nodeY, slot)` → `ulong`. This is the persistence
key SP5 writes chop/collect overrides against, and the identity that lets a GPU
far-drawn tree and a CPU near-spawned GameObject agree they are the same object.
One id bit is reserved to distinguish **base** ids (derivable from the hash)
from **player-placed** ids (additions with a stored transform), so the two
namespaces never collide.

### Dependencies

`IPlanetSurfaceSampler`, `IBiomeProvider`, and `FaceSpaceCellRangeBuilder`
(topology + `ComputeMetersPerUV`). Plain deterministic C#: the managed sampler
calls preclude Burst in the SP1 inner loop, which is fine at prop counts. If
near-band gather ever costs too much, the mitigation is the SP3 GPU path for the
visual tiers, not Burst-ifying SP1.

### Verification (project idiom — no test framework)

`scatter.verify` console command:

- Gathers a fixed region twice from different query origins / descent orders and
  asserts identical instance sets and ids (order-independence → determinism).
- Asserts a single node's emitted instances are independent of the path taken to
  reach it.
- Reports counts per prototype and a biome-border density profile so the
  cross-bleed falloff is inspectable.

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
  the single shared `TryPlace` function + a dedicated equivalence check.
- **Cube-face distortion**: spacing/level mapping uses `ComputeMetersPerUV`;
  validate uniformity near face edges/corners in `scatter.verify`.
- **Impostor bake fidelity** (SP4): lighting/depth correctness of the view-blend
  is its own investigation; MVP fallback is mesh-LOD-only, added later without
  redesign since instances are LOD-agnostic.
- **Density authority vs biome map resolution**: the CPU core uses analytic
  `EvaluateBiome`; the GPU mirror uses the baked biome map. Confirm the baked
  map's resolution is fine enough that the border falloff matches the CPU
  authority within tolerance.
