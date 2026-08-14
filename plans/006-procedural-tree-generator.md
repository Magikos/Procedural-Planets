# Plan 006 — Procedural Tree Generator (our own, harvest-first)

**Status:** DESIGN — from a 4-agent study of Broccoli Tree Creator's source (`D:\Unity\Explore
Assets\Assets\Waldemarst\Broccoli`, studied as reference only, not installed/copied). Supersedes the
Synty-tree cut-set approach in plan 005 (which hit a wall: fixed meshes are black boxes).

## Why build our own

Every tree-harvest problem this project hit traces to one root cause: **Synty tree meshes are opaque
fixed geometry.** We can't reliably tell trunk from foliage, there are no leaf anchor points, and the
geometry doesn't cut into matching stumps/logs. That broke the stump generator (guessed the wrong
part), the fallen log (foliage splayed flat), and the particles (no real emit points).

Broccoli solves procedural trees but is a heavy 2022 editor tool (298 files / ~123k LOC, node-graph +
MonoBehaviour factory + SpeedTree/photoreal machinery). We don't install or graft it — we take its
**load-bearing 20%** (the algorithms + data model) and build a **scoped, stylized, Burst-accelerated,
harvest-first** generator we fully own and define in code.

## The design driver: what a tree must give us (harvest-first)

The generator's real job is not "a tree" — it's a **harvestable tree**. From one seeded definition it
outputs a bundle:

1. **Standing mesh** — trunk + foliage, LODs, far impostor.
2. **Matching cut-set** — a **stump mesh** and a **fallen-log mesh**, carved from the *same* trunk
   skeleton so they line up by construction (the thing Synty/Broccoli-black-box couldn't give).
3. **Anchor points** — leaf anchors (canopy) + trunk/bark anchors → exact particle emit points and
   "leaves scatter on chop" data.
4. **Gameplay metadata** — trunk girth + height → **chop HP** (bigger tree = more hits) and **wood
   yield** (∝ trunk volume); a **trunk collider mesh** for the future streamed-collider/physics fall.
5. **Determinism** — a species = a definition; variations = seeds (`Unity.Mathematics.Random`).
6. **Clean trunk-vs-leaf separation**, free, via the skeleton's `isTrunk`/`followUp` chain.

This is the spine: every downstream system (scatter prototype, stump/log renderers, particles,
multi-hit chopping, yields) is *fed by the generator* instead of reverse-engineering a fixed mesh.

## Architecture — a fixed 5-stage function (not a node graph)

Broccoli is a general node-graph interpreter (positionWeight-ordered elements → components → managers)
because it must support arbitrary species authored in an editor. We know our structure, so we
**hard-wire the stage order in code** — a plain pipeline, no graph engine, no Element⇄Component⇄Manager
indirection, no `TreeFactory` MonoBehaviour:

1. **Structure** — grow the skeleton (branch tree + leaf anchors) from the definition + seed.
2. **Cut-set carve** — derive the stump-cut and log-cut trunk sections from the skeleton (before mesh).
3. **Mesh** — tube-mesh branches (generalized cylinders) + card-mesh leaves → standing/stump/log meshes.
4. **LOD** — re-mesh at 2–3 detail levels → `LODGroup`; far impostor via the *existing* scatter baker.
5. **Bundle** — package the `HarvestableTree` (meshes + materials + anchors + metadata).

## Data model (adopted from Broccoli, scoped)

**`TreeDef`** (code-first, the format you author trees in):
- global: `globalScale`, LOD levels, `seed`.
- an ordered list of **`LevelRule`** (trunk = level 0, then branch levels, then leaf levels), nested by
  `parentId`. Each rule carries the ~8 params the study found actually drive tree shape:
  `frequency` range, `distribution` (Alternate/Opposite/Whorled) + golden-angle **twirl**, a
  `[startRange,endRange]` band along the parent, `parallelAlign` (open angle from parent), `gravityAlign`
  base→top (droop/uplift — the main silhouette control), `length` falloff base→top, `girthScale`.
- leaf groups: material + card shape (Plane/Cross/blob) + size + count.

**Skeleton** (Broccoli's `BroccoTree` model, simplified — polylines, not full beziers):
- `Branch { CurvePoint[] centerline, girthCurve, parent, children[], followUp, isTrunk, position(0-1),
  hierarchyLevel }` — `followUp` is the single child continuing the branch (the trunk chain).
- `Sprout { Vector3 surfacePos, Vector3 centerlinePos, dir, normal, scale, branchId, leafGroupId }` —
  the dual anchor (surface + centerline + frame) is exactly our particle/cut-set data, adopted as-is.

## Stage 1 — Structure (the recursion)

Level-based recursion (adopt Broccoli's model, drop the graph):
- **Trunk**: one vertical branch; length from level 0; girth base→top curve; light gravity+noise offset
  baked into the control points for lean/curve (skip Broccoli's separate BranchBender stage).
- **Branch levels**: for each parent branch, place `frequency` children along the parent's `[start,end]`
  band at phyllotaxis nodes (golden-angle ~137.5° twirl gives convincing spirals cheaply); orient each by
  the parent frame at the attach point + `parallelAlign` (open angle) + `gravityAlign` lerped base→top;
  child `length` = falloff, girth = parent girth at attach × `girthScale`. Recurse.
- **Leaf levels**: place `Sprout` anchors on tip branches (Broccoli's `FromTip` origin — walk terminal
  branches' follow-up lineage), recording surface point + direction + normal + branchId + group.
- Deterministic: `Unity.Mathematics.Random(seed + levelOffset)`; per-level work is `IJobParallelFor`-able.

## Stage 2 — Cut-set carve (the key differentiator)

Because we own the skeleton, the trunk is known (`isTrunk`/`followUp` chain) — no guessing:
- **Stump** = the trunk skin, planar-clipped at `stumpHeight` (a fraction of trunk height), top capped.
- **Fallen log** = the trunk skin section from `stumpHeight` up to a log length (or the remaining trunk),
  capped both ends, foliage excluded.
- Both are carved from the **same trunk ring data**, so the stump's top cut and the log's bottom cut
  match by construction — the thing that broke on Synty meshes. (We already prototyped the planar
  clip+cap in `TreeStumpGenerator`; here it runs on our own clean trunk, reliably.)

## Stage 3 — Mesh (adopt Broccoli's minimal DefaultMeshBuilder)

- **Trunk/branch tubes**: sweep a **low-N ring** (fixed per branch, e.g. 5–6 trunk / 3–4 twig) along the
  branch centerline; ring vertices `(cosθ·girth, sinθ·girth, 0)` rotated to the curve frame, translated to
  the sample; `N+1` seam vertex so U wraps; **faceted normals** for the toon look (skip tangents);
  cheap cylindrical UVs (U = i/N around, V = length along × tiles). Ring count adaptive by curvature +
  a forced ring at each child attach. Children = **intersecting cylinders** + shared attach ring +
  normal-average the base (Broccoli's own default; welding skirts skipped). Keeping N constant per branch
  avoids Broccoli's fiddliest piece — the variable-ring bridging triangulator.
- **Leaf cards**: one reused card/blob mesh per leaf group (a single quad, or a tiny crossed blob),
  placed at each `Sprout` via `LookRotation(dir, normal)`, scaled base→top, all welded into **one
  foliage mesh**. Skip multi-plane/grid/noise/atlas.
- Two materials: **bark (toon)** + **foliage (toon)** — matches the current per-part split; reuse
  `Planet/PropLit`-style planet-aware lit shaders.
- **Burst**: tube mesh per branch-skin + leaf placement per sprout as `IJobParallelFor`.

## Stage 4 — LODs + impostor

- Re-mesh at 2–3 detail levels (fewer ring sides + fewer rings, optionally fewer leaves) → Unity
  `LODGroup` with cross-fade (adopt Broccoli's re-mesh strategy, drop its billboard baker).
- Far impostor: **reuse the existing `ScatterImpostorBaker`** (octahedral atlas — already bakes any
  mesh). No new billboard system.

## Stage 5 — Bundle → integration (this is why it fixes everything)

Output a **`HarvestableTree`**: standing LOD meshes + stump mesh + log mesh + bark/foliage materials +
leaf/trunk anchor arrays + metadata (trunkHeight, girth → HP + woodYield) + trunk collider mesh.

- **Scatter**: standing LODs + materials + impostor → a `ScatterPrototype` (same shape the Synty trees
  use now), with the cut-set stored on the `StumpMesh` + `FallenLogMesh` fields we already added.
- **Harvest**: cut-set meshes → the `StumpRenderer`/`LogRenderer` (replacing the placeholders); leaf/trunk
  anchors → `ChopFxSystem` (real canopy-leaf + trunk-bark emit points, fixing the particle request);
  girth→HP → multi-hit chopping (the seam is already in `HarvestService`); woodYield → `HarvestYield`.

So the generator drops in where the Synty tree prototypes are and **fixes every tree-harvest issue at
the source** instead of patching fixed meshes.

## Performance (the modernization over Broccoli)

Broccoli is managed/MonoBehaviour, editor-centric. Generation is embarrassingly parallel math:
- **Burst + Jobs** for structure + mesh (per-branch, per-leaf `IJobParallelFor`) — the big win over
  Broccoli's managed code.
- **Awaitable background-thread** for the one-shot generation (project rule: expensive one-shot work off
  the main thread) — bake/generate the species set during load/stream, never hitch a frame.
- **`Unity.Mathematics.Random`** — Burst-safe *and* seed-deterministic (Broccoli uses non-Burst
  `UnityEngine.Random`).
- **Compute shader: deliberately deferred.** We generate a *fixed set of species × a few variations* as
  scatter prototypes at bake/load — Burst-on-CPU is correct there. Compute earns its place only if we
  later go *per-instance* runtime trees. The skeleton→mesh boundary is kept clean so that's a future swap.

## Tree ages (Bryan, 2026-08-12)

Every species has **age stages: Sapling → Young → Adult → Old** — and this is where procedural beats
fixed meshes (Synty would need 4× hand-modeled meshes per species; we get them from one definition).

- **`age`** is a parameter on `TreeDef` (a 0–1 scalar and/or an `AgeStage` enum) that scales the level
  rules: trunk height + girth, number of branch tiers + child frequency, canopy/leaf density, and
  lean/gnarl (old = taller/thicker, more lean + noise, maybe a dead branch or sparser crown; sapling =
  short, thin, few branches, small crown). One `TreeDef` → four staged trees.
- **Gameplay ties (free):** wood **yield** and chop **HP** scale with age; a **sapling isn't worth
  chopping** (little/no wood); and a chopped **stump can regrow a sapling over time** — that is the
  regrow mechanic, straight out of the age system. Trees could grow age-over-time later (regenerate at a
  higher age); the generator supports it since age is just an input.

## Variety per biome (Bryan)

Several distinct tree **types per biome**, each with its 4 age stages. This is cheap procedurally — a
type is a `TreeDef`, not a modeling job — so the species set is *biome × types × ages* (hundreds of
distinct trees that would be prohibitive to hand-author). Seeds add per-instance variation within a type.

## Style (Synty toon + toon-fantasy nature — Bryan)

Bryan likes **both** the Synty style and the toon-fantasy-nature style — we can hit both from one
generator. The **geometry** stays low-poly flat-shaded (Synty): low-N faceted tubes, blob/card leaves.
The **character** (proportions, canopy shape, lean, colors, leaf blob-vs-card) leans toon-fantasy and is
tunable per `TreeDef`. Current Synty trees are the color + proportion reference (their style, our
geometry, our control).

## Build stages (each verifiable, incremental)

- **T1 — Structure + debug viz.** Skeleton recursion; draw it with gizmos/lines. Verify shapes read as
  trees (trunk + branch tiers + tip clusters). Pure logic, no meshing.
- **T2 — Trunk/branch tube mesh.** Skeleton → a bare standing tree mesh (no leaves). Verify a bare tree.
- **T3 — Leaf cards + toon materials.** Foliage on the tips → a full tree. Calibrate against Synty.
- **T4 — Cut-set carve.** Stump + log from the trunk skin; wire into `StumpRenderer`/`LogRenderer`,
  replacing the placeholders for one generated species.
- **T5 — LODs + impostor + `ScatterPrototype` output.** Generate one full tree prototype and place it via
  scatter alongside the Synty ones.
- **T6 — Metadata.** Anchors → real leaf/bark particles; girth→HP → multi-hit chop; woodYield.
- **T7 — Species set + full replacement.** Author a definition per biome tree; Burst + async; replace all
  Synty tree prototypes.

## Adopt vs skip (study summary)

**Adopt:** recursive level model; `Branch`(curve + isTrunk + followUp) / `Sprout`(dual anchor + frame +
group) skeleton; skeleton-then-mesh split; generalized-cylinder tubes with *constant N*; card leaves
welded into one mesh; re-mesh LODs into a `LODGroup`; separate bark/foliage toon materials; single-seed
determinism; LOD as ring-sides + curve-tolerance.

**Skip (Broccoli's photoreal/authoring 80%):** node-graph engine + positionWeight validation +
serialization/undo; Element⇄Component⇄Manager indirection + `TreeFactory` MonoBehaviour + manager stack;
welding-skirt junctions; branch shapers / non-circular cross-sections / root flare; variable-ring
bridging; hard-normal duplicate path + 8-channel wind UVs; texture-atlas baking (RectpackSharp) +
billboard baker (use our impostor); SpeedTree wind (a light vertex sway later if wanted); roots; the
shared/variation multi-level system; procedural bark textures; and the Sprout Lab billboard sub-tool.

## Open decisions (for Bryan)

1. **Bake-time vs load-time.** *Recommend bake:* an editor tool "Generate Tree → `ScatterPrototype` +
   cut-set + impostor" that writes committable assets (matches the current prototype model; deterministic;
   no per-boot cost). Load-time generation is possible later with the same core (Burst/Awaitable) if we
   want the species set built fresh each run or per-instance variation.
2. **Species + variation count** per biome (how many distinct tree definitions, how many seeded variants).
3. **Style calibration** — match Synty proportions/colors closely, or diverge toward our own look.

## Relationship to the current harvest code

Nothing here throws away the committed harvest work (plans 003/005): the state machine, stores,
renderers, picking, verbs, particles, and seams all stay. This plan **replaces the tree ASSETS + the
placeholder stump/log meshes** with generated, cut-set-aware geometry, and hands the renderers/particles
real per-tree meshes + anchors. The placeholder work was the scaffold; this is the real structure.
