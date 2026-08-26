---
name: project_distant_grass_carpet
description: 2026-08-12 — "grass doesn't draw far enough / far shore = dirt" is the disabled far grass-surface carpet; exists + blade-textured; off due to biome-edge stripe; Lake1 is arid so carpet won't help there
metadata:
  type: project
---

2026-08-12 (branch character-controller-mvp). Bryan: at `Lake1` looking across the
lake, "grass isn't drawing far enough — far shore looks like dirt, but there's grass
when I walk over. Represent fake grass at distance as a carpet, blade-like not solid."

**Root cause (evidence-backed, self-serve captures):** the "fake grass at distance"
mechanism ALREADY EXISTS and is ALREADY blade-textured — the *far grass-surface
overlay* ("blanket") painted onto terrain by `PlanetVertexColor.shader ›
ApplyGrassSurfaceAlbedo/EvaluateGrassOverlay` (fiber/fleck `ValueNoise3D`,
`_GrassFarOverlayFiberStrength`). It's DISABLED by default:
`PlanetGrassCoordinator._grassBlanketEnabled = false` → sets `_GrassFarOverlayStrength=0`.
Beyond ~40 m (near-field blade range) with the blanket off you see raw terrain albedo.

**Why off:** hard density gate `smoothstep(0.38,0.72, density*slope*water)` maps a narrow
density range to a narrow SPATIAL band → the biome-edge green stripe. `grass.debug-layer-colors
true` paints the carpet red and shows the thin stripe on the far waterline.

**KEY REFRAME — `Lake1` is an ARID biome.** Runtime `_BiomeGrassParams` (18 biomes):
grassy biomes (5-10,12,13,17) density 0.80-0.95; desert/rock/water/tundra (0-4,11,14-16)
density 0.00. The Lake1 far shore didn't paint even with the carpet on → density-0 arid
biome → "far shore = dirt" there is LARGELY CORRECT. The carpet's payoff is real grassland,
NOT this lake. Wanting THIS lake lusher = a biome/content question (widen green LakeShore ring
/ wetter lake surroundings), not a carpet question.

**SHIPPED (commit 0e6d716, 2026-08-12, editor healthy after restart):** soft gate
`smoothstep(0.06,0.85,...)`; `_grassBlanketEnabled=true` by default; `_grassSurfaceBrightness
0.35→0.6` so the carpet reads at distance. Verified live at Lake1: distant grassy hills green up,
no stripe, arid waterline stays dry. **Also fixed cattails same commit:** `FoliageReeds._SeasonColor
(1.1,0.62,0.42)→white` (warm tint was making the olive/brown cattail atlas maroon); `Lake Cattails
ScaleRange 0.8-1.4→0.42-0.55` (reed mesh is 3.4m tall → old max 4.8m, new ~1.9m under the 2m capsule;
needs stop→play to rebind DTO). OPEN: Synty's green-LEAVED reed look needs a leafier mesh (Swamp pack
only has bare-stalk SM_Env_Reeds_01) — harvest follow-up. Env gotcha: uncommitted `AssetBench` C# (not
in an asmdef) loops HotReload → `isCompiling` sticks → shader iteration unreliable; commit/stash it.

**Round 2 (2026-08-12, Bryan approved grass "good job, don't let it regress"):**
- Reeds green stems + brown heads: recolored the atlas per-region (olive stem px→green, keep maroon
  heads + tan tips) → `Assets/Art/Textures/Reeds_01_Green.png`; FoliageReeds `_BaseMap`+`_TrunkMap`
  repointed. Needed because FoliageLit `_ForceLeaf=1` collapses stem+head to one tint path (can't split
  by tint; disabling _ForceLeaf breaks the thin-stem alpha cutout). Commit 115689f, verified live.
- Far-tree distortion (flat impostor + LOD-crossfade dither from ~340m): Savanna Tree LOD1 end 400→500
  (impostor start = MaxCull×0.85 → 425m; far gather = MaxCull×4.5 grows 1800→2250, sparse trees so OK).
  Commit 591bb6f, needs stop→play. Other tree prototypes unchanged — bump the same if their far trees
  distort. DIAGNOSIS: impostor is `OctGridN=8` (64 angles, 128²/cell) — distortion is billboard
  flatness+dither, not resolution; holding real geometry further is the fix, NOT atlas res.
- GRASS CARPET IS NOW DEFAULT-ON (0e6d716) and Bryan-approved — do not re-disable `_grassBlanketEnabled`.
- **CARPET WEAVE LINES fix (commit 1c72bf8)**: with blanket on, elevated/grazing views showed a diagonal
  criss-cross weave across grassland (gone with blanket off). Isolated: debug mode 5 (envCoverage, pre-texture)
  is SMOOTH → not biome-edge; zeroing `_GrassFarOverlayFiberStrength` removes it → the anisotropic FIBER term
  is the culprit. The fleck self-filters via `fwidth`; the fiber didn't, so its world-tangent-aligned noise
  aliased into lines at distance. FIX: fade fiber→neutral(0.5) over viewDist 60→190m (PlanetVertexColor.shader,
  ApplyGrassSurfaceAlbedo) — smooth grass far, blades near. Fade band tunable. Set `_GrassOverlayDebug` global
  (0=off,1=coverage,5=envCoverage) to isolate carpet artifacts.
- TREE LOD now UNIFORM: all 18 tree/pine/palm prototypes last-LOD=500m → impostor start 425m (591bb6f
  savanna + a647f40 rest). Poly survey: heavy trees (forest ~25k, swamp ~30k) already have 3 artist LODs
  (→6-8k); 1-LOD pines/dead trees are tiny (342-559 tris) so no LODs needed (impostor takes over at 425m).
  BRYAN PREFERENCE (2026-08-12): next time a tree reads heavy/distorted at range, **adjust the actual LOD
  MODEL (decimate/author a lighter LOD mesh), not the LOD ranges**. Unity has no built-in decimator →
  would need an editor simplifier (e.g. UnityMeshSimplifier). Left alone for now.
- **`grass.rebuild` console command (commit 5fe9e51)**: GPU grass controllers cache Material/compute/
  buffers once in ctor with NO reload recovery, so an in-editor shader/compute REIMPORT stales the draw
  (placement keeps emitting, draw no-ops → blades vanish). `grass.rebuild` disposes → next Tick recreates
  fresh (same as `grass.enabled false/true`), recovering blades WITHOUT a ~3min scene restart. Only C#
  RECOMPILES (domain reload) still force a re-play — unavoidable. During our iteration, avoid reimporting
  shaders mid-play; capture-verify then batch edits into one play cycle. Bryan has a SEPARATE agent on the
  AssetBench asmdef issue — leave AssetBench alone.
- **IMPOSTOR "shot with a shotgun / missing leaves" fix (commit 6e6194e)**: `ScatterImpostorBaker` keyed the
  silhouette ALPHA off LUMINANCE vs the black bake bg → dark/shadowed foliage read as background and got
  punched to holes. PROVEN by re-keying a baked atlas off max-channel coverage → full leafy trees (evidence
  local-only/debug-screenshots/impostor-BEFORE/AFTER). Fixed: alpha = max(r,g,b) coverage w/ low floor (Bake +
  BakeAtlas); also `NormalizeCoveredBrightness` (bake renders through FoliageLit's planet-sun so brightness
  depended on bake-time-of-day → near-black at dusk; normalize covered albedo to mean 0.17, brighten-only).
  DIAGNOSIS TOOL: in PLAY mode (sun globals set) call `ScatterImpostorBaker.BakeAtlas(meshes,mats,8)` offline
  via execute_code + composite over magenta to inspect the atlas (edit-mode bake renders BLACK — FoliageLit
  needs runtime sun globals). Needs fresh play to re-bake. FOLLOW-UP (Bryan's idea): PRE-BAKE impostor atlases
  as assets → cuts ~3min startup (impostor is the ONLY runtime-baked LOD; mesh LODs are FBX) + controlled bake
  lighting + inspectable. Impostor shading is still a fake hemisphere blob (`_NormalBulge`); Bryan OK flatish.

Levers: `grass.layer Blanket true|false`, `grass.overlay-strength`, `grass.surface-brightness`,
`grass.surface-saturation`, `grass.debug-layer-colors`. Doc: docs/design/2026-08-12-distant-grass-carpet.md.
Evidence PNGs: local-only/debug-screenshots/grass-distance-*.png. Committed d520d74 (doc only).
Related: [[project_grass_layering_arc]] (blanket revived then parked for the stripe), [[project_lake_biome]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Distant grass carpet](project_distant_grass_carpet.md) — 2026-08-12: "grass doesn't draw far enough / far shore=dirt" (Lake1) = the DISABLED far grass-surface overlay ("blanket", `_grassBlanketEnabled=false`), which ALREADY EXISTS + is blade-textured. Off due to biome-edge stripe (hard gate `smoothstep(0.38,0.72)`). **Lake1 is ARID** (far-shore density~0) so carpet won't green it — that's a biome/content Q, not carpet. Proposed soft-gate (0.06/0.85) in tree, UNCOMMITTED + unverified (editor isCompiling stuck: HotReload looping on uncommitted AssetBench scripts). Doc d520d74; evidence local-only/debug-screenshots/grass-distance-*.png
