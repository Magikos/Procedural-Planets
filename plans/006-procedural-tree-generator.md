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

## Overnight autonomous status (2026-08-12, compile-only — NOT visually verified)

Built + committed while Bryan slept (`dotnet build` clean; no editor, so all visuals are guesses):
- **T1** structure + tube mesh + `tree.gen`/`tree.age` preview (`d3ea619`).
- **T2 + cut-set** leaves (crossed toon cards) + `TreeCutSet` (stump/log carved from the trunk) + the
  `GeneratedTree` bundle (`87b76ab`).
- **LODs + injection** 2 mesh LODs + `TreeInjection` — replaces scatter tree prototypes (Chop) with
  generated trees + the generated stump, age varied per type; **DEFAULT OFF**, `tree.inject on` +
  `generate` to apply; heavily guarded so it can't break scatter (`17ae354`).

**Review on wake (likely-wrong-first-try, in order):** run `tree.gen` (bare-eye the shape/leaves/colors),
then `tree.inject on` + `generate` to see generated trees in the world. Expect to tune: **tree size vs the
old Synty scale**, **leaf blob look** (crossed cards — may read sparse/blocky), **bark/leaf colors**
(flat PropLit guesses), **faceted vs smooth** (currently smooth — Synty wants faceted), and **branch
angles/counts** in `TreeDefLibrary.SampleBroadleaf`. New `.cs` files have no `.meta` yet (unattended) —
they generate on your first import.

**Known not-done:** per-INSTANCE age variety (needs per-instance scatter mesh variants); the fallen **log**
still renders the placeholder cylinder (only the STUMP is wired to the generated cut-set via `StumpMesh`);
one species only (`SampleBroadleaf`) — the per-biome type set is next; faceted normals; the Valheim
chopping below.

## Valheim chopping + carryable logs (design stub — Bryan, 2026-08-12)

The target chop, to build interactively (needs play verification + character/input work — stubbed here,
not yet built):
1. **Fell hinged at the CHOP POINT.** A chop cuts low on the trunk (`GeneratedTree.ChopFraction`); the rest
   of the tree tips over hinged at that cut (not the ground). The current `FallingTree` already tips about
   the base ≈ the chop point, so this is a small offset tweak.
2. **Branches + leaves break off + fade** as it falls, leaving the bare **trunk**. Needs: identify the
   foliage/branch sub-meshes on the falling GO (reliable now for GENERATED trees — bark part vs foliage
   part are explicit) and fade/shrink/detach them partway through the fall (+ a leaf/bark particle burst
   from the real anchors). Do this only for generated trees where the parts are known.
3. **Trunk → carryable log sections.** The felled trunk (the `Log` cut-set mesh) can be chopped into
   shorter sections; each section is a **pickup entity** the player **carries** (Valheim-style: hoist over
   the shoulder, movement-slowed, drop/deposit). Needs a new small `CarryController` on the player (hold
   one carryable, attach to a carry anchor, drop on Interact), a `LogSection` pickup (mesh = a slice of the
   trunk tube), and a "chop log → split into N sections" step in `HarvestService`. This is the biggest new
   piece and is genuinely a new gameplay system — build it with Bryan in play, not blind.

Sequencing: land the generated-tree look first (tune injection on), then fell-fade (2), then the
carry-log system (3) as its own slice.

## Vegetation archetype roadmap (2026-08-15, Bryan: "generate nearly all our vegetation this way")

Goal: one generator produces the whole plant kingdom via structure (levels) + a foliage primitive. Each row is
a `TreeDef` param set + a foliage primitive; new primitives are small additions to `TreeLeafMesher` /
`ConiferCone`. Status: ✅ done, 🔨 in progress, ⬜ planned.

**Foliage primitives** (the reusable building blocks):
- ✅ Leaf-card clump (`LeafGroup 0`) — textured crossed cards, per-biome Synty leaf material.
- ✅ Drooping frond ribbon (`LeafGroup 1`) — palm, UV-mapped to one atlas cell.
- ✅ Solid spiky fir cone (`FoliageStyle.ConiferCone`) — dark-green geometry, AO in vtx.G, FoliageLit.
- 🔨 Needle tuft (`LeafGroup 2`) — small spiky puff on a branch tip (open pine).
- ⬜ Weeping strand — long hanging leaf/needle chains (willow).
- ⬜ Flat tiered layer (cedar), scale-frond column (cypress/juniper), pad (cactus), blade (reed/fern).

**Species archetypes:**
| Archetype | Foliage | Structure | Real trees |
|---|---|---|---|
| ✅ Broadleaf | leaf clumps | short trunk, round crown | oak, maple, beech, aspen |
| ✅ Palm | fronds | bare curved trunk, top crown | palm |
| ✅ Fir/Spruce (`Conifer`) | fir cone | tall trunk, dense cone | spruce, douglas fir, noble fir |
| ✅ Acacia | leaf clumps | short trunk, umbrella | savanna acacia |
| ✅ Shrub | leaf clumps | low multi-stem | bush |
| ✅ Pine | needle tufts | tall bare trunk, open tufted upper | ponderosa, longleaf, scots |
| ✅ Cypress/Juniper | cone, small radius | narrow columnar | cypress, juniper |
| ✅ Cedar | flat tiers | wide flat layers | western red cedar |
| ✅ Poplar | leaf clumps | narrow deciduous column | lombardy poplar |
| ✅ Willow | weeping strands | arching branches | willow |
| ✅ Birch (own) | leaf clumps | slender white trunk | birch, aspen(narrow) |
| ✅ Fern | blade fronds | no trunk, arching crown | bracken (first non-tree plant) |
| ⬜ Exotic | special | — | baobab, joshua, banyan (later) |

**Species set complete except exotics (2026-08-15).** 12 species. New this round:
- **`TreeDef.Cone{BaseFrac,RadiusFrac,Droop,Tiers}`** turn the one cone mesh into the whole conifer family:
  fir = defaults, **Cypress** = small radius (0.10) + low droop → narrow dark pillar (W/H 0.17), **Cedar** = wide
  (0.38) + almost no droop (0.12) + **only 5 tiers** → flat stacked plates. Tier count is what separates cedar
  from fir: at 7+ the plates overlap back into a smooth cone.
- **Poplar** — narrow deciduous column (W/H 0.14): many short primaries at low `ParallelAlign` (0.22) sweeping
  steeply up, so the crown hugs the trunk.
- **Fern** — first non-tree plant, and proof the pipeline generalises. New **blade primitive** (`LeafGroup 4`):
  like the palm frond but UV u spans the whole texture instead of one palm-atlas cell, so it wears the ordinary
  biome leaf material — reusable for reeds and blades generally. A fern is just a stem stub with the fronds
  radiating off it: 1.2 m tall, 1.0 m wide. Note `MaxHeight` normalises the STEM (the solve measures branch tips
  and a fern has none), so the fronds arch above it — set it ~0.6× the intended plant height.
- **`tree.gallery` rebuilt**: 12 species × 5 columns (4 ages + a **DEAD** column), 26 m × 34 m spacing so a 30 m
  crown never touches its neighbour, and a **2 m capsule beside every tree** — the only honest scale reference.
- **`TreeShowcaseSpawner` + the BiomeShowcase scene** got the same treatment (the scene's serialized 9×14 spacing
  overrode the new defaults, so it was set on the instance and the scene saved): 17 prototype rows × Synty
  original + 4 ages + DEAD, 102 capsules, one per tree. It also renders through **fade-free material copies** —
  the table is now wider than the materials' `_FadeEnd` (150 m), so viewing the whole thing would otherwise
  dither the far trees to nothing. Copies, because several of those materials are shared project assets.

Beyond trees: ferns (blade primitive), reeds/cattails (blade), cacti (pad/column), flowering bushes, vines.
All the same pipeline; scatter already places them per biome. Review each in `tree.gallery`, tune, then wire
the biome→species map.

### Real-world size calibration (2026-08-15, Bryan: "also work on the sizes")

Trees were ~half real scale — a mature Broadleaf measured **14.4 m**, smaller than the Synty tree it replaces,
against a 2 m character. Reference: `docs/research/tree-references/highest-trees.jpg` (human 1.8, English oak 35,
Douglas fir 67, sequoia 84, redwood 116).

**`TreeDef.MaxHeight` (metres at Age 1) is now the single size knob.** The recursion builds in proportion units
and `TreeStructureGenerator.NormalizeHeight` scales the finished skeleton to `MaxHeight × ageCurve(Age)`, so every
authored ratio and the segment density survive untouched and size is authored in real units instead of an abstract
`GlobalScale` (which remains, but only shapes proportions). Two couplings the normalisation handles:
- **Leaf clumps scale sublinearly** (`f^0.65`) — a 30 m oak carries finer foliage than a 10 m one, which is what
  keeps a big tree from reading as one inflated blob (POLYGON crowns are many modest masses).
- **…so clump COUNT scales up** (`f^0.35`, estimated off the trunk) or the crown thins out as the tree grows.
- **Age curve is superlinear** (`lerp(0.06, 1, age^1.6)`): a sapling is a fraction of the mature tree, not a
  half-size copy. Measured ladder, sapling/young/adult/old: Broadleaf **3.7 / 9.6 / 20.3 / 34.1 m**.

| Species | MaxHeight | Real reference |
|---|---|---|
| Broadleaf | 32 m | English oak ~35 |
| Conifer (fir/spruce) | 42 m | spruce/noble fir 40–50 (measures ~49 — the cone tops above the trunk tip) |
| Pine | 38 m | ponderosa/scots 35–40 |
| Birch | 22 m | silver birch 20–25 |
| Palm | 20 m | coconut 15–25 |
| Willow | 18 m | weeping willow 15–20 |
| Acacia | 14 m | umbrella thorn 10–15 |
| Shrub | 2.6 m | waist-to-head bush |

**Broadleaf crown re-proportioned** to the Round/Spreading form (`tree-forms-urban-forestry.jpg`): primaries start
at 0.30 of the trunk (was 0.40), `ParallelAlign` 0.74 (was 0.58), `Length` 4.3→2.4 (was 3.0→1.8), less uplift.
Crown W/H went ~0.45 → **0.85**, crown base at 8.2 m of 34 m. It was a narrow tuft on a bare pole; it now reads
as a spreading oak. Broadleaf covers 10 of the 18 tree prototypes, hence first.

**Trunk pass (same session).** `TreeDef.RootFlare` (default 0.45) widens the base ring, decaying over the bottom
fifth (`exp(-t*9)`), so a trunk meets the ground on a buttress instead of a cylinder cut off flat — the stump
cut-set inherits it for free. Broadleaf also gives its girth up to the crown: `TrunkTipScale` 0.28 → 0.18 and
primary `GirthScale` 0.52 → 0.62, so a few heavy limbs fork out of a tapering trunk rather than twigs poking out
of a full-width pole. Palm 0.18 / Shrub 0.12 (no hardwood buttress); conifers keep the 0.45 default.

**Per-species crown pass (same session).**
- **Conifer** — the cone started at 0.28h with a comment claiming pine behaviour, leaving ~14 m of bare trunk at
  the new sizes. Spruce/fir carry foliage nearly to the ground (the archetype spec above); Pine is the species
  with the clear bole. Now `baseFrac`/`radiusFrac` params on `BuildConiferCone` (0.12 / 0.26), which is also the
  knob the planned narrow cypress/columnar variant needs.
- **Pine** — tufts were too small to read. Enlarging them to 2.3 made it WORSE: the puffs merged into a solid
  mass indistinguishable from the Conifer cone, i.e. variety lost. Landed at `LeafSize` 1.5 with FEWER tips
  (branches 5–8, tufts 1–3, only in the outer 0.72–1.0 of each branch). **Sky between the puffs is the feature** —
  it is what separates Pine from Conifer at a glance. Don't raise tuft size/count without re-checking that.
- **Acacia** — was spindly, not an umbrella. `ParallelAlign` 0.68 → 0.84, near-zero gravity at the tips, primaries
  4.6→3.4 long, and foliage confined to the outer 0.72–1.0 of each limb so the space *under* the canopy stays
  open. Now reads as the flat savanna umbrella.

**Bryan's review round (photos supplied for willow).**
- **Trunks too thick on old trees.** Global girth 0.05 → 0.042 of trunk length, age girth top 1.0 → 0.82, default
  `RootFlare` 0.45 → 0.32 (Broadleaf 0.35). Then a per-species `TreeDef.TrunkGirthScale`, because one global
  ratio cannot serve both shapes: a broadleaf's trunk is ~60% of the tree (crown above it) while a conifer's runs
  the full height, so the same length-proportional girth made the fir a 4.4 m column. Conifer 0.55, Pine 0.6,
  Birch 0.35, Palm 0.3. Resulting trunk diameter as % of height: Broadleaf 6.6, Conifer 5.0, Pine 5.7, Birch 3.2,
  Palm 2.7, Willow 6.1 — slender species now read slender.
- **Conifer foliage reached the ground and hid the trunk.** `baseFrac` 0.12 was too low *because the lowest skirt
  droops ~0.9 of the bottom tier's radius below where it hangs from*, and that tier is the widest — so it buried
  itself. Now `baseFrac` 0.30 with droop 0.9 → 0.7, giving ~6 m of clear bole on a 49 m fir. The comment in
  `BuildConiferCone` now states the relation so the next edit doesn't repeat it.
- **Willow was far too narrow** (real ones are wide — see Bryan's reference photos). Short trunk (2.6–3.6 vs
  3.5–5), `ParallelAlign` 0.55 → 0.86, branches 5.2→3.6 long: crown W/H **1.50**, 27 m across an 18 m tree.
  Strands also needed to be *denser and shorter*, not longer — 16–22 per branch at `LeafSize` 3.4 and 0.11 width
  (was 9–14 at 5.0 and 0.05), or they read as loose grass blades hanging off bare sticks instead of a curtain.

- **Palm fronds needed to ARCH, not sag** (Bryan supplied a Synty palm reference). A frond leaves the crown going
  up and out, reaches an apex, then bends over and droops at the tip. Ours launched near-horizontal (the sprout
  direction) and sagged from the first segment = a limp fan. Fix in `AddFrond`: lift the start direction
  (`+ up * 0.95`), stronger per-segment bend (0.22 → 0.42) and 7 segments so it reads as a curve rather than a
  bent stick, plus a squared width falloff so the frond holds width through the arc and points at the tip.

**Wind on generated foliage (checked 2026-08-15).** `FoliageLit.ApplyWind` needs all three of: global wind
(published by `WeatherManager`; the TreeShowcase scene sets strength 0, so showcase captures prove nothing about
wind), vertex-colour B as leaf mask (our cards write B=1), and **per-material `_WindStrength`, default 0**.
Species reusing the authored `Foliage*.mat` inherit 0.08–0.20 and sway. Materials built in code do not:
`ConiferMat` / `ConiferPreviewMat` set `_ForceLeaf` but never `_WindStrength`, so **Conifer and Pine were
completely rigid** — `flex = leafMask * _WindStrength`, so the leaf-mask half alone buys nothing. Both now 0.10.
Still open: `_WindStrength` is absolute sway *metres* tuned for the old half-size trees, so 0.14 m on a 34 m oak
reads about half as strongly as it did — probably wants scaling with tree height.

**Dead trees are a MODIFIER, not a species (2026-08-15).** `TreeDef.Dead` — the generator skips every leaf tier,
gnarls the branches (curve ×1.5, noise ×2.2, length ×0.72) and `TreeDefLibrary.AsDead` greys the bark and cuts
height to 0.8×. So any species can die and still read as itself: a dead oak keeps the oak silhouette. Injection
applies it to prototypes whose name contains "Dead", which fixes a live regression — the four `* Dead Tree`
prototypes (Desert, Scrub, Tundra, IceBog) were being replaced by *leafy* generated trees, losing the dead look in
those biomes entirely. A dead tree emits a bark part only; the empty foliage part is skipped so it doesn't cost a
draw band or skew the impostor bake bounds.
**Limitation:** `Conifer` has no branch tiers (its foliage is the parametric `ConiferCone`), so a dead conifer is
a bare tapered spike with no limbs. Nothing maps to it today (no dead-conifer biome), but a dead fir needs either
a branch tier added to the def or a different species mapping.

**Birch is now a real species with marked bark (2026-08-15).** Three things were needed:
1. **Routing.** `HasTree` maps by BIOME only and no biome maps to Birch, so the birch prototypes (Forest biome)
   generated Broadleaf — the def was unreachable. `TreeInjection` now checks the prototype NAME for "birch"
   first, then falls back to biome. Name routing is deliberately birch-only; other prototypes are correct by
   biome and a general matcher would mis-route e.g. "Snow Pine" (Snow → Conifer, intentionally).
2. **A trunk UV seam vertex** (`TreeTubeMesher`). Rings emitted `sides` verts and wrapped the last quad's U from
   `(sides-1)/sides` back to 0, squeezing the entire texture backwards into one facet. Harmless while bark was a
   flat colour, fatal for any bark texture. Rings now emit `sides+1` verts, the last duplicating the first
   position with U=1. V is cumulative length in metres, so bark tiles per metre at any tree size.
3. **Procedural birch bark** — `TreeInjection.BirchBark()` generates a 64×128 wrapped texture (near-white grain +
   ~26 dark tapered lenticel dashes) into `_BaseMap` of the birch bark material only. No art asset, deterministic,
   1 texture height = 1 m of trunk. Reads as dark banding at distance rather than distinct spots; more/smaller
   dashes would sharpen it.
Crown also opened up (primaries 6–9 not 3–5, longer, `ParallelAlign` 0.58): W/H **0.21 → 0.32**. It was a stripe
of leaves stuck to one side of a pole.

### PLAY-VERIFIED ON THE PLANET 2026-08-15 (the check everything above was waiting on)

Planet scene, seed 1691104419. The whole session's work confirmed in a real world, not a showcase plane:
- **Injection live:** 109 prototypes, 54 chop trees (18 sources + 36 variants), 12 bark-only dead.
- **Per-instance variety WORKS in-world.** Within an 80 m radius the variants interleave in the same stand:
  Palm 6 / v1 11 / v2 11; Taiga Pine 17 / v1 4 / v2 20; Birch 22 / v1 13 / v2 17. Different slot hashes really do
  produce different placements, so a stand mixes ages and shapes. Counts per variant are uneven at small sample
  sizes, which is expected from independent hashes.
- **Density is FINE — the jam worry did not materialise.** Forest biome: 194 tree instances within 80 m, dense
  enough to read as forest and still walkable between trunks. Steppe/Taiga: ~30. **No scatter spacing change
  needed**; leave `SpacingMeters` alone and keep the density parity.
- **Cold fill completes:** 16,931 live tiles, 159,456 instances, 0 queued / 0 in-flight, far radius 4128 m.
- **Perf (editor, not a build):** ~18–20 ms/frame at ground level in forest; ~31 ms with the camera raised 46 m
  over dense canopy. 109 prototypes did not blow it up — off-biome prototypes cost nothing because they have no
  instances.
- **Impostors:** the distant treeline reads correctly, no slab/pop artifacts, so the shared-atlas change holds up
  at range.

**Owed / known couplings:**
- Shrub and Palm crowns unreviewed.
- **Scatter spacing** is still tuned for half-size trees — canopies are now ~2× wider, so forests may jam. Cheapest
  fix is scaling prototype `SpacingMeters` by the height ratio in `TreeInjection` (one line), but that trades away
  the density parity per-instance variety just established. Look at a real biome before choosing.
- Conifer overshoots its target because the cone builds above the trunk tip; normalisation measures branch tips.
  At 49 m its foliage still starts at 0.28h, leaving ~14 m of bare trunk — right for a mature Douglas fir, too
  bare for the "dense cone to near-ground" spruce/fir spec above. Taste call.
- Broadleaf trunk taper is now strong enough to read slightly conical; `TrunkTipScale` is the dial.
- Other species keep their old crown proportions at the new sizes — only Broadleaf got the crown pass.

### Detailed archetype specs (build from these — the reference images are NOT saved; this text is the spec)

**Reference images are saved in `docs/research/tree-references/`** (Bryan added them 2026-08-15) — open them with
the Read tool as needed. Most useful:
- `highest-trees.jpg` — real heights against a human. **The size spec** (see the calibration section above).
- `Bushes.webp`, `Shrubs-Names-in-English-with-their-Pictures.jpg`, `vsetky pokope.jpg`, `MeadowLayout_03.avif` —
  added later the same day, for the non-tree vegetation targets.
- `tree-forms-urban-forestry.jpg` — the 10 canonical **crown forms** (see taxonomy below). The key structural ref.
- `tree-types-vector-...-birch-cedar-acacia-...105282560.webp` — species sketches: Maple, Spruce, Cedar, Palm,
  Sakura, Pine, Sequoia, Acacia, Birch, Apple, Baobab, Poplar — good per-species silhouettes.
- `vector-tree-sketch-...-127535045.webp` — another stylized species/form set.
- `a-collection-of-different-types-of-trees-free-png.png` — 20 semi-realistic form variations.
- `Identify-Trees-Summary-Version-2.jpg`, `1ee62731-..._Tree_Names_in_English.png`, `543c0def...jpg`,
  `2be28c...jpg`, `61+9MYBDrFL...jpg`, `images.jpg`, `360_F_...jpg`, `1.jpg.jpg` — ID charts / species lists.
- `leaves-types-names-...57901496.webp` — leaf-shape outlines (for future leaf-texture accuracy).
- `52-types-of-wood-...webp`, `a-guide-to-state-trees-...webp` — species catalogs.

**Crown-form taxonomy** (from `tree-forms-urban-forestry.jpg`) — the structural axis; longer-term a `CrownForm`
param drives structure somewhat independently of foliage type:
| Form | Shape | Our archetype |
|---|---|---|
| Round | broad rounded crown, short trunk | ✅ Broadleaf |
| Spreading | very wide low crown (old oak) | ⬜ Broadleaf wide variant |
| Pyramidal | dense triangle to ground | ✅ Fir/Conifer |
| Oval | upright egg-shaped crown | ⬜ (Broadleaf tall variant) |
| Conical | narrow pointed (young conifer/cypress) | ⬜ Cypress narrow cone |
| Vase | branches fan up-and-out, open bottom (elm) | ⬜ new structure |
| Columnar | tall narrow pillar | ⬜ Poplar / Cypress |
| Open | sparse branches, see-through (pine) | 🔨 Pine |
| Weeping | branches cascade down (willow) | ⬜ Willow |
| Irregular | asymmetric, wind-shaped | ⬜ variation knob |

The distinguishing features are also captured in text below so the build needs no image if they're ever lost.

### Style target: Synty POLYGON (higher-def), NOT the blocky low-poly (Bryan, 2026-08-15)

Synty ships two styles; we want the **POLYGON** one — rounded, faceted-but-detailed crowns — not the very
low-poly/cubic look. Style bible: `e69a3549-..._scaled.jpg` ("POLYGON NATURE PACK"). Implications:
- **Broadleaf/deciduous** — our textured leaf-card clumps already match the game's PolygonNatureBiomes trees (leaf
  cards, not cube-blobs). Keep. Avoid the giant faceted balls (rejected earlier as "blobs").
- **Conifer** — Synty's detailed fir (`d1cda568...jpg`) = **layered drooping needle-blade sprays**: each bough is a
  flat frond of needles drooping down, stacked up the trunk. Our current `ConiferCone` is a single solid spiky
  cone — the "blockiest" of our trees. UPGRADE PATH: boughs (whorled, drooping) + a **needle-blade frond primitive**
  per bough (like the palm frond but small, needle-pointed, solid dark green, many, layered). More depth/detail.
- **Willow** — drooping leaf strands (seen in the POLYGON pack).
- General: prefer more/rounder geometry + texture detail over few big flat facets; keep it readable at gameplay
  distance (the scatter impostor covers far LODs).

Non-tree POLYGON vegetation seen in refs (`6238710a...jpg`, `5e6769f6...jpg`): baobab, cacti/succulents, agave
blades, ferns, reeds, mushrooms, grass tufts — all future targets for the same generator (blade/pad primitives).

- ✅ **Fir/Spruce** (`Conifer`, real: Norway Spruce, Douglas Fir, Noble Fir): dense **cone to near-ground**,
  spiky needle silhouette, foliage nearly the full height, tapers to a sharp spire. Dark blue-green. DONE.
- 🔨 **Pine** (Ponderosa, Longleaf, Scots): **tall bare trunk** (~half the height clear), then **open, airy**
  upper crown of a **few branches ending in rounded needle tufts** — NOT a solid cone. Trunk clearly visible.
  Medium-green. Built; tuning tuft size/openness.
- ⬜ **Cypress / Juniper / Poplar** (columnar): **very narrow, tall column**, width ≈ 12–20% of height, foliage
  hugging the trunk from low to a rounded/pointed top. Cypress/juniper = dense dark scale-foliage; Lombardy
  poplar = narrow **deciduous** column (leaf clumps). Build = a narrow ConiferCone (small `maxR`) for
  cypress/juniper; a tall thin leaf-clump column for poplar.
- ⬜ **Cedar** (real: Western Red Cedar, Lawson Cypress): broad, **flat horizontal layered tiers** (stacked
  plates), wider + flatter + more separated than the fir's drooping skirts, foliage in flat sprays. Build =
  ConiferCone variant: fewer tiers, near-zero droop, wider radius, horizontal rims.
- ⬜ **Willow** (weeping): short trunk, branches arch up then a **curtain of long thin leafy strands cascades
  straight down** to near the ground. Light yellow-green. New primitive: weeping strand = a long near-vertical
  chain of small leaf cards hanging from each upper branch tip, many per tree.
- ⬜ **Birch / Aspen** (own species; we have a `Birch` def but the world maps birch prototypes to Broadleaf):
  **slender pale/white trunk**, tall, **sparse open canopy** of small leaves in the upper 2/3, fine upward
  branches. Autumn = yellow. Wire some Forest prototypes to Birch; give it its pale bark + a white trunk tint.
- ⬜ **Broadleaf tints** (Oak/Maple/Beech): we have the round crown; add color variants — oak deep green, maple
  autumn orange/red, beech mid-green — via `LeafColor` and (later) per-instance tint variety.
- ⬜ **Exotic** (later): Baobab (fat bottle trunk, tiny top crown), Joshua (branchless then spiky yucca arms),
  Banyan (wide spreading multi-trunk + aerial roots). Each needs a bespoke structure but the same primitives.

Proportions/colors above are starting points to eyeball in `tree.gallery`, not final.

## Per-instance variety (design, 2026-08-15 — Bryan picked this as the next big piece)

Goal: a stand of one biome's trees shows many DIFFERENT trees — varied geometry per instance AND real age stages
(sapling/young/adult/old are different shapes, not just scaled). Today the injection bakes ONE generated mesh per
prototype, so every instance of a prototype is identical (only ScaleRange + RandomYaw vary).

The draw path (read 2026-08-15): `ScatterGpuDraw` keeps a per-prototype master transform buffer; `ScatterCull.compute`
distance-bands ALL of a prototype's instances per LOD; one `RenderMeshIndirect` per (part, LOD) band draws them with
that band's mesh. `ScatterDrawBuckets` carries a per-instance `ScatterId` (so `hash(id) % K` gives a stable variant
index per instance). Instanced draw = one mesh per batch, so mesh variety requires either more batches or more
prototypes.

**Approach B — variant-as-prototype (RECOMMENDED, no draw/compute change).** In `TreeInjection.Apply`, expand each
Chop tree prototype into K variant prototypes:
- Each variant = the prototype `with { Parts = generated variant k, StumpMesh = variant k, SlotId = <free slot>,
  SpacingMeters *= sqrt(K) }` (√K spacing keeps total density ~constant across K variants).
- Variant k differs by seed (always), age (spread sapling..old across k so a stand mixes ages), and optionally
  sub-species. `ScatterLibraryDto` is rebuilt from a longer `ScatterPrototypeDto[]`, which `Apply` already returns.
- Different SlotIds → different placement hashes → the K variants interleave spatially = a mixed-age, mixed-shape
  forest, drawn by the existing per-prototype path for free.
- **Slot budget:** `ScatterId` SlotBits=7 → 128 slots; current max ~67 (lake props). K=3 × ~15 tree prototypes = 45
  new prototypes → slots 68..112. Fits. Assign from a free-slot cursor starting at `maxUsedSlot+1`; assert < 128.
- **RISK to verify first:** does the gather/biome placement place a prototype purely from its own `Biome`/`Spacing`
  fields, or is there a slot→biome table in `BiomeRegistryDto` that new slots must be registered in? Check
  `ScatterField`/gather + `BiomeRegistryDto` before relying on new slots auto-placing. If a table exists, either
  register the variant slots or fall back to Approach A.
- Also confirm ScaleRange/density: K prototypes each at √K spacing should ≈ the original tree density; verify with
  `ScatterFlyBench` (no K× perf blowup, world still full).

**Approach A — sub-bucket the draw (fallback).** Per-instance variant buffer (`hash(id)%K`), `ScatterCull.compute`
gains a variant filter, K visible buffers + K draws per band. Correct and scales, but edits the fresh GPU-indirect
compute + draw — higher risk. Only if B's placement assumption fails.

**Age = shape, not scale:** for real age stages, the generator already scales height/girth/branch tiers by age
(`TreeStructureGenerator`), but leaf SIZE currently scales with GlobalScale not age. For saplings to read young,
also gate leaf tiers / foliage fullness on age. Do this in the def/generator, orthogonal to A/B.

**Recommended execution:** fresh session (this one is very long). Bryan now has the Unity MCP connected, so the
implementer can verify placement/density/perf in-editor directly. Start by answering the slot→biome RISK above,
then implement B, verify a mixed stand in the BiomeShowcase + a real biome + ScatterFlyBench.

### Built 2026-08-15 (Approach B — compile-verified, NOT yet play-verified)

- **RISK answered: no slot→biome table.** `SlotId` feeds only the placement hash (`ScatterHash.Slot`) and the
  `ScatterId` pack; biome/spacing come from the prototype's own fields. New slots auto-place. (The old
  `PackUnchecked` SlotBits=6 aliasing bug is already fixed — it derives from `ScatterId.SlotBits`.)
- **`TreeInjection.Variants` (default 3)** expands each Chop tree prototype into K generated prototypes: own
  seed, age laddered 0.25→1.0 across k, own slot from a free cursor (`maxSlot+1`, currently 73; 18 trees × 2
  extra = slots 73–108 of 127), `SpacingMeters *= sqrt(K)`. Variants are **appended after** the originals so
  prototype indices stay stable (`TreeShowcaseSpawner` pairs raw↔injected by index) and **variant 0 keeps the
  source SlotId** so saved chops still bind. Out of slots ⇒ variants dropped + a Warning, never a throw.
- **`ScaleRange` narrowed to 0.85–1.2** when K>1 (age now carries the size spread; the old 0.6–1.45 on one mesh
  was the stand-in for it).
- **Impostor cost held flat:** K× prototypes would have meant K× live octahedral bakes (64 camera renders + two
  1024² textures each). New `ScatterPrototypeDto.ImpostorShareKey` lets the variants of one species share one
  baked atlas in `ScatterImpostorFactory`; `FromPrebaked` re-frames it to each variant's own bounds, so a
  sapling still billboards at sapling size. Cached cards are session-lived, which also makes re-`generate`
  cheaper than before.
- **Age = shape:** leaf size now scales with age (`ageLeaf` in `TreeStructureGenerator`), so a sapling isn't an
  old tree wearing adult leaf cards. Branch tiers/frequency already scaled with age.
- **`tree.variants <1-8>`** console command (rebuilds + re-registers the DTO, like `tree.inject`).
- **Owed on wake:** play-verify a mixed-age stand in BiomeShowcase + a real Forest/Taiga biome, `scatter.stats`
  for per-slot density parity, and a `ScatterFlyBench` pass (K× prototypes = K× indirect draw bands for the
  biome you're standing in; only prototypes with instances cost anything, but confirm).

## Relationship to the current harvest code

Nothing here throws away the committed harvest work (plans 003/005): the state machine, stores,
renderers, picking, verbs, particles, and seams all stay. This plan **replaces the tree ASSETS + the
placeholder stump/log meshes** with generated, cut-set-aware geometry, and hands the renderers/particles
real per-tree meshes + anchors. The placeholder work was the scaffold; this is the real structure.
