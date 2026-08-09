# Surface representations & the "floating scatter" question — decision note (2026-08-09, rev 2)

Status: **decision of record.** Rev 2 corrects the surface taxonomy (rev 1 was wrong — see the Codex review
log at the bottom, all points folded in). Read alongside
[2026-08-09-collision-strategy.md](2026-08-09-collision-strategy.md) and
[../../plans/look-fixes-backlog.md](../../plans/look-fixes-backlog.md).

## TL;DR

- The planet has **several** height/surface representations, all **derived from the same analytic noise** and
  all **fixed at generation** (only *which* rendered leaves are drawn is camera-selected). They agree at
  max-depth vertices and diverge only by interpolation / tessellation error bounded by LOD.
- **Decision: policy 2 — keep the representations, give each consumer an explicit authority + error budget.**
  No new unification service. The rev-1 "make analytic follow the visible mesh" idea was aimed at an interface
  scatter does not even use, and is dropped.
- **The reported floating scatter (yellow bush) is an ORIENTATION bug, not a height bug** — a radial prop on a
  slope lifts its downhill edge. Fixed with `ConformToSlope` on bushes/flowerbushes. Height mismatch near the
  camera is ~0 (measured); it only grows at distance where it is sub-pixel.
- **Character grounding "fixed" is a runtime observation, not a proven contract** — the raycast path silently
  falls back to the analytic sampler on a miss, and the ocean test could not distinguish them. A land test
  with path instrumentation is required to prove it.

## Surface / height representations (verified against code)

Every representation below is **fixed at generation** unless marked camera-scoped. All derive from the same
`ShapeGenerator` noise.

| # | Representation (owner, file:line) | Height method | Camera/LOD? | Consumers |
|---|---|---|---|---|
| 1 | Analytic noise field — `ShapeGenerator.SampleElevation` ([ShapeGenerator.cs:57](../../Assets/Scripts/Planet/ShapeGenerator.cs#L57)), `GetScaledElevation` (:92) | raw noise sum; `R=PlanetRadius*(1+elev)` | fixed | #2, #3, #4 |
| 2 | `AnalyticGroundSampler.TrySampleRadius`/`SampleNormalAt` ([:32,60](../../Assets/Scripts/Planet/Surface/AnalyticGroundSampler.cs#L32)) | delegates to #1; normal = 2 tangent noise probes → triangle | fixed | **SCATTER** ([ScatterField.cs:264,292](../../Assets/Scripts/Planet/Scatter/ScatterField.cs#L264)) |
| 3 | Burst scatter mirror `ScatterGatherBurst.SampleRadius` ([:77,45](../../Assets/Scripts/Planet/Scatter/ScatterGatherBurst.cs#L77)) | same math via `NoiseFilterEvaluator`; golden-parity with #1/#2 | fixed | `ScatterGatherJob` (runtime tile-cache gather) |
| 4 | Rendered terrain verts — `PlanetChunkMeshJob.ComputeVertexWorld` ([:98-137](../../Assets/Scripts/Planet/PlanetChunkMeshJob.cs#L98)) | `EvaluateElevation` via **the same** `NoiseFilterEvaluator` as #3; per-vertex, ChunkResolution 97 | verts fixed; *drawn* leaf is camera-selected | GPU render; feeds #5/#7/#8 |
| 5 | Retained leaf radius grid — `PlanetChunk.CpuVertexRadii` ([ChunkSurfaceGenerator.cs:214](../../Assets/Scripts/Planet/Surface/ChunkSurfaceGenerator.cs#L214)); `PlanetChunk.TrySampleRadius` bilinear | bilinear over retained radii (not raw noise) | fixed | #6 |
| 6 | `ChunkedSurfaceProvider.TryGetLocalSurfaceRadius` ([:288-305](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L288)) → wrapped by `Planet.TryGetSurfaceRadius` | `FindLeafContaining` in a **fixed-depth** quadtree (`BuildToFixedDepth`) → #5 bilinear | fixed | **character analytic FALLBACK**; camera height |
| 7 | Visible raycaster `TryRaycastVisibleSurface` ([:829-881](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L829)) → `Planet.TryRaycastSurface` | triangle raycast over `GetVisibleLeaves` actual mesh | **CAMERA-SCOPED** | **character grounding (primary)** |
| 8a | Grass surface atlas — `GrassSurfaceAtlasBuilder` ([:182-209](../../Assets/Scripts/Planet/Grass/GrassSurfaceAtlasBuilder.cs#L182)) | per-face RFloat radius atlas, bilinear-resampled from max-depth `CpuVertexRadii` | fixed (max-depth) | grass placement compute |
| 8b | Water face sampler — `ChunkedFaceMeshSampler` (depth-2 aggregate) | aggregated depth-2 unit-sphere+elevation grid | fixed | `WaterMeshBuilder` |

**Scatter does NOT use #6 or #7** (grep over `Assets/Scripts/Planet/Scatter/` for the surface-provider APIs
returns zero matches). It uses #2/#3 — raw analytic noise. rev 1 claimed otherwise; that was wrong.

## The float bug — root cause is ORIENTATION, not height

Scatter places a prop pivot at `dir * (analytic noise radius)` (#1). The rendered terrain triangle under it is
the piecewise-linear chord of **the same** noise between max-depth vertices (#4). They **coincide at vertices**
and differ only by the triangle chord (tessellation sag). Measured sag (probe below):

| cell size (≈ LOD) | 2 m | 8 m | 32 m | 128 m | 512 m |
|---|---|---|---|---|---|
| noise-vs-chord height gap | ~0.00 m | ≤0.06 m | 0.1–0.6 m | 1–76 m | tens of m |

Near the camera the terrain is at max LOD (2–8 m cells) → **height gap ~0**. A *near* bush therefore does not
float from height. The metres-scale gap exists only at distance (coarse LOD), where it is sub-pixel and the
prop is usually an impostor.

The visible float (yellow bush, shadow gap) is **orientation**: a `ConformToSlope=0` prop stands radial, so on
a slope its base plane (⊥ radial) lifts off the terrain (tilted by the slope angle) on the downhill side by
~`base_radius · tan(slope)`. Mesh-pivot checks confirmed bush/rock/flower pivots sit at their base (not the
cause). **Fix:** bushes/flowerbushes get `ConformToSlope` (bed to the slope), per Bryan's rule "rocks etc. fit
the terrain; trees/flowers grow up." Rocks = 1 (done); bushes/flowerbushes = 0.6 (beds without lying flat);
trees/pines/palms/dead-trees/grass/reeds/ferns/flowers = 0 (radial). Mushrooms left at 0 (grow up on a stalk),
flagged for review.

## Decision — policy 2: per-consumer authority + error budget

There is **no single "ground truth" surface** and forcing one is not warranted. Each consumer names its
authority and the error it tolerates from the others:

| Consumer | Authority | Error vs. render | Budget / status |
|---|---|---|---|
| Scatter placement | analytic noise #1–3 (deterministic, off-camera) | triangle sag, 0 near → sub-pixel far | acceptable as-is; orientation fixed separately |
| Character grounding | visible raycast #7, **fallback** analytic #6 | fallback ≠ mesh where ray misses | **UNPROVEN** — needs land test (below) |
| Grass placement | grass atlas #8a (max-depth) | bilinear vs render triangle | accepted (existing) |
| Water surface | depth-2 aggregate #8b | coarse vs render | accepted (existing) |
| Collision (future) | render mesh at a **fixed** collision LOD | must NOT track active camera (off-screen bodies need collision) | define in collision doc |

This **strengthens** the rejection of camera-dependent scatter: deterministic reference data (#1–3, and the
fixed atlases #8a) already exists independently of camera selection, so nothing needs to depend on #7.

## Character grounding — observation, not contract (Codex SU3)

`PlanetRaycastGrounding.TryGround` returns the visible-mesh hit when the ray hits, else **silently falls back**
to the analytic `PlanetSurfaceGrounding` (#6) ([PlanetRaycastGrounding.cs:52](../../Assets/Scripts/Planet/Character/PlanetRaycastGrounding.cs#L52));
pose-seed has the same fallback ([PlanetCharacterController.cs:244](../../Assets/Scripts/Planet/Character/PlanetCharacterController.cs#L244)).
Over ocean both providers clamp to `_seaLevelRadius`, so they produce the **identical** point — the ocean test
this session could not tell mesh-grounding from analytic-fallback. So: the fall-through fix is a **successful
runtime observation**, not a proven mesh-grounding contract.

**Land validation (to prove it):** instrument `PlanetRaycastGrounding.cs:47` (MeshHit) and `:52` (split
AnalyticFallback vs TotalMiss), log per attempt `{path, resolved radius}` on land where analytic ≠ visible, and
declare an acceptable fallback rate before the run. Not yet done.

## If a measured height threshold is later exceeded (Codex SU5)

Do **not** route scatter through #7, and do **not** traverse the managed quadtree from Burst (gather jobs
cannot read managed `PlanetChunk`). Barycentric interpolation of three scalar leaf radii also does not exactly
reproduce the planar rendered triangle. The correct path: build an **immutable, Burst-readable fixed-depth
triangle/radius atlas once per terrain generation** and sample it in the gather — the grass atlas (#8a) is
already exactly this shape and could be the model or the source. Gate on parity tests + `GatherBatchBurst`
avg/p95 + memory before adopting. Until a threshold is exceeded (near-field sag is ~0), **do not build it.**

## Reproducible evidence (Codex SU4)

World: play mode, planet `LastGeneratedRadius` 5293, sea level 5000 (default scene, this session). Probe:
`ScreenCapture`-free `execute_code`, 6 land directions (analytic radius > sea+3), deterministic LCG seed 12345.
For each: local slope via 2 m tangent probes; height sag = `analytic(center) − bilinear(4 corners)` at cell
arcs {2,8,32,128,512} m. Slopes sampled 7–12°. Sag table above (near ≈ 0.00 m). Mesh-pivot check: bush
`SM_Gen_Env_Bush_01` bounds.min.y −0.174 (base at pivot); rocks −0.047; flowers −0.042; tree Parts[0] is the
*branches* (min.y +2.5, expected). Full probe scripts are in the session transcript; re-runnable via the
`execute_code` MCP tool. **Not yet done:** convert the largest in-render gap to screen pixels at a declared
camera pose; predeclare pixel/contact thresholds; a matched **land** grounding test.

## Follow-ups (open)

1. Land grounding test with the SU3 instrumentation — prove mesh-grounding vs silent analytic fallback.
2. Pixel-threshold analysis of the distance sag before any height-unify work is scheduled.
3. Align [collision-strategy](2026-08-09-collision-strategy.md): "one ground-truth surface" → "one **fixed**
   collision-LOD reference (camera-independent)"; keep analytic as approximate query only.

---

## Review log — Codex 2026-08-09 (rev 1) + responses

Codex reviewed rev 1 and was substantially correct; rev 2 folds in every point. Verbatim verdict + points, with
the rev-2 response after each.

**Verdict (Codex):** Keep the decision not to make deterministic scatter depend on the camera-selected visible
mesh. Rev 1 conflated independently-owned surface representations, conflicted with the collision strategy, and
treated limited runtime observations as proof.

- **SU1 (taxonomy).** Codex: `IPlanetSurfaceSampler.TryGetSurfaceRadius` is fixed-depth leaf bilinear, and
  scatter uses `AnalyticGroundSampler`→`ShapeGenerator`, not that interface. **Response: confirmed by an
  independent code trace (grep proves scatter never calls it); taxonomy table above rewritten.** ✅
- **SU2 (collision conflict).** Codex: pick one policy — single authoritative reference, or multiple with
  explicit authority + error budget. **Response: adopted policy 2 (table above); follow-up 3 aligns the
  collision doc + backlog to stop promising a single surface and to pin collision to a fixed LOD.** ✅
- **SU3 (grounding fallback).** Codex: the raycast silently falls back to analytic; the ocean test can't
  distinguish. **Response: confirmed; "fixed" re-phrased as observation; land-test instrumentation specified.** ✅
- **SU4 (evidence).** Codex: make measurements reproducible; convert the gap to pixels. **Response: probe
  method + sag table + pivot data recorded; pixel-threshold conversion left as open follow-up 2.** ⏳ (partial)
- **SU5 (future sketch).** Codex: Burst can't traverse the managed quadtree; 3-radii barycentric ≠ the planar
  triangle; build a Burst-readable atlas only if a threshold is exceeded. **Response: sketch replaced with the
  fixed-depth atlas approach (grass atlas #8a is the precedent); gated on measured need + parity/perf tests.** ✅
