# Scatter subsystem audit — 2026-07-26 (addendum)

Branch `scatter-placement`. Follows [2026-07-25-scatter-audit.md](2026-07-25-scatter-audit.md);
covers what landed since: the `FoliageLit` foliage pipeline, the full biome buildout (42
prototypes across all 14 land biomes), the `BiomeShowcase` preview scene, and a re-read of the
render path. Findings-first — **Needs Review** items are Bryan's call and were not auto-changed.
Numbered N* to avoid clashing with the 07-25 F* items.

---

## N1 — `ScatterRenderer.Draw` rescans the full instance list per (prototype × part × LOD)  ·  Open

- **Category:** Perf · **Severity:** Medium · **Status:** Open (recommend fix)
- **Description:** `Draw` loops prototypes → parts → LOD meshes, and for each combination scans the
  **entire** `_instances` list filtering `PrototypeIndex != p` then the distance band. With the
  library now at 42 prototypes (some 2–3 parts, up to 3 LODs) this is
  O(prototypes × parts × lods × instances) per frame just to bucket — e.g. ~42 × ~1.5 × ~2 ×
  N instances. At N≈4–8k that is millions of index/compare ops/frame before any draw call.
- **Evidence:** [ScatterRenderer.cs:169-209](../../Assets/Scripts/Planet/Scatter/ScatterRenderer.cs#L169).
  The inner `for (i in _instances) if (PrototypeIndex != p) continue;` repeats for every part×lod of p.
- **Recommendation:** bucket instance indices by prototype **once** per swap (in
  `SwapAndBuildMatrices`), then each prototype iterates only its own bucket. Turns the per-frame cost
  into O(instances × lodsPerInstance). Purely internal; determinism unaffected.

## N2 — `Configure` writes `enableInstancing` onto the SO-referenced material asset  ·  Needs Review

- **Category:** Architecture (rule) · **Severity:** Low · **Status:** Needs Review
- **Description:** [ScatterRenderer.cs:70-71](../../Assets/Scripts/Planet/Scatter/ScatterRenderer.cs#L70)
  does `part.Material.enableInstancing = true` at runtime. CLAUDE.md: "Runtime never writes shader
  properties or keywords on an SO-referenced material asset." This is a persistent import flag (not a
  per-frame write) and is defensive against per-frame `RenderMeshInstanced` log spam, but it does
  dirty the shared asset.
- **Recommendation:** set `Enable GPU Instancing` in the `.mat` assets themselves (all Foliage* and
  the generic material) and drop the runtime write, or keep it only as a validated fallback that logs
  a Warning ("material X lacked instancing; enable it in the asset"). Bryan's call on which.

## N3 — Dead tree and generic pine are single-mesh (no LOD chain)  ·  Open (low)

- **Category:** Perf/consistency · **Severity:** Low · **Status:** Open
- **Description:** the Desert/Scrub/Tundra/IceBog dead tree and the Steppe/Taiga/Snow/Mountain pine
  each have one LOD entry (`LodEndDistances: [400]`), so they draw full-detail to 400 m with no
  reduction, unlike the 3-LOD meadow/birch/swamp/palm/pohutukawa. Vert counts are modest (dead 613,
  pine 1264) so impact is small today.
- **Recommendation:** fold into the far-LOD/impostor work ([design doc](../design/2026-07-26-scatter-impostor-design.md));
  no dedicated fix needed now.

## N4 — Generic pine has non-bimodal vertex.B (partial cutout)  ·  Open (low)

- **Category:** Correctness (visual) · **Severity:** Low · **Status:** Open
- **Description:** unlike Synty nature trees (clean B∈{0,1}), `SM_Gen_Env_Tree_Pine_01` has ~600
  mid-range B verts, so `FoliageLit`'s `smoothstep(_LeafMaskLo,_LeafMaskHi,B)` gives partial leaf
  mask → partial alpha cutoff on those verts (possible edge fringing). Rendered acceptably in the
  workbench. Pine uses one atlas in both texture slots, so albedo is unaffected — only the cutout.
- **Recommendation:** if fringing shows in-scene, give `FoliagePine` `_ForceLeaf = 1` (treat all as
  cutout leaf) or a hard mask threshold. Leave until observed.

## N5 — Shadow distance raised 50 → 250 m  ·  Note

- **Category:** Perf/setup · **Severity:** Low · **Status:** Done, verify at scale
- **Description:** `PC_RPAsset` shadow distance was 50 m, so scatter shadows vanished a few metres
  out. Raised to 250 m (Bryan's report). Larger shadow distance spreads the same shadow-map texels
  over more world area → softer/blockier far shadows unless cascade split / shadow resolution are
  also tuned.
- **Recommendation:** eyeball far shadow quality at 250 m; bump shadow resolution or tune the 4
  cascade splits if the far cascade looks coarse. Cheap to revert.

## N6 — `BiomeShowcase` is a static LOD0 lineup (not a runtime LOD test)  ·  Info

- **Category:** Docs · **Severity:** Info · **Status:** By design
- **Description:** the showcase instantiates each prototype's **LOD0** part meshes as plain
  GameObjects — no `ScatterRenderer`, no distance LOD, no gather. It is an asset-audit lineup, not a
  representation of runtime LOD/impostor behaviour. Judge LOD/impostors on the real planet, not here.

## N7 — 07-25 F6 (only 3 prototypes wired) — RESOLVED

- The library is now 42 prototypes: every land biome (Beach…Mountain) has a hero tree/plant + rock,
  most have a bush and/or ground accent (grass/flowers/ferns/reeds). No (biome, slot) collisions;
  DTO validates clean. Ocean/Cave/Underwater intentionally empty. Gaps are asset-availability, not
  wiring: no cactus (Desert sparse), no snow-specific trees (Snow = pine), Enchanted Forest pack
  unused, no season tint. These are Bryan's asset calls (he is sourcing missing assets).

## N8 — FoliageLit pipeline conventions (reference, not a finding)

- Two-part trees (meadow, birch, pohutukawa): **trunk/branches part** → bark material
  (`_TrunkMap` = bark); **canopy part** → its own material with `_TrunkMap` = leaf texture +
  `_TrunkTint` dark green, so the interior fill blob (vertex-B=0) reads as canopy shadow, not brown
  clumps or bright-green "bark". Single-atlas trees (palm, pine, dead) use one texture in both slots.
  `_ForceLeaf` treats an all-B=0 mesh (moss beard, reeds) as cutout leaf. **Never** put a Synty prop
  atlas (`PolygonNatureBiomes_*_Texture_01`) in `_TrunkMap` — branch meshes span the whole atlas and
  pull in icon/palette decals (the original "icon cards" bug). Captured in memory
  `project-scatter-biome-buildout`.

---

## Verdict

No new correctness bugs. N1 (Draw rescan) is the one worth doing before the instance count grows;
the rest are low/notes or Bryan-decision. Placement core (`ScatterField`) unchanged since 07-25 and
still `scatter.verify` PASS territory (off-thread, budget-guarded, deterministic).
