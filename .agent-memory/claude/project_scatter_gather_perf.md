---
name: project-scatter-gather-perf
description: "2026-07-29 — scatter deterministic (verify PASS). Tier 1a coarse-biome memo cut gather 21s→6s (4354691); then Lever B incremental TILE CACHE replaced whole-disc gather entirely (frontier-only per move) — fly cap raised 0.006→0.02 (~106 m/s); if scatter \"looks missing\" it's altitude/orbit, NOT placement or speed"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
  modified: 2026-07-31T04:48:33.886Z
---

## 2026-07-30 — perf/LOD/seam diagnosis + dev tooling (Bryan flagged: slow load, LOD blink, hard grass edge)

**Perf root cause (the "fly ahead of the scatter" pain):** placement is SINGLE-THREADED. `ScatterTileCache`
gathers a batch of independent `(tile, prototype)` pairs in a plain serial `for` loop on ONE
`Awaitable.BackgroundThreadAsync` worker ([ScatterTileCache.cs:243-247](Assets/Scripts/Planet/Scatter/ScatterTileCache.cs#L243)).
Each pair writes its own `_batchResults[i]` → embarrassingly parallel. Fix = PARALLELISM, not a new
architecture. My recommendation was **Burst `IJobParallelFor`** (refactor `GatherTilePrototype`+placement
math to native/Burst-compatible; fits the "Burst for hot work" rule; ~Ncores+SIMD). **Bryan chose full
ECS/DOTS anyway** — respect it, but scope it carefully (it's a weeks-long rewrite of scatter/render/
persistence; consider an incremental hybrid). Priority he set: **bugs now, perf (ECS) after.**

**Dev tooling I built (commit `e5ea644`, compiles clean, NOT yet run — Unity MCP bridge dropped before I
could test):**
- `ScatterFlyBench` MonoBehaviour ([Assets/Scripts/Planet/Scatter/ScatterFlyBench.cs]): `StartBench(speedMps,
  seconds)` flies Camera.main along a great circle at fixed speed, logs load-lag (`ScatterTileCache.
  PendingPairCount` / `LiveInstanceCount`) + worst frame ms + a CSV, verdict "OUTRUN" if backlog stays >0.
  Optional screenshot capture. To use: add the component (or `new GameObject().AddComponent`), then call
  StartBench. Resolves the cache via `ConsoleRegistry.GetInstance(typeof(ScatterTileCache))`.
- **LOD debug view**: `ScatterLodBatcher.LodTintDebug = true` tints instances by LOD band (LOD0 green,
  LOD1 yellow, LOD2 orange, LOD3+ red, impostor magenta) via `_LodDebugTint` in FoliageLit + Scatter frag.
  Use it to SEE which transition blinks. Set the static bool from execute_code.

**LOD leaf-blink hypothesis (unconfirmed — confirm with the LOD debug view first):** `ScatterLodBatcher.
DrawBand` uses HARD distance bands (`d2 < near2 || d2 >= far2`) = instant mesh swaps, NO crossfade.
`ScatterRenderer` overrides FoliageLit `_FadeStart/_FadeEnd` to `cull*0.85 → cull` (line 62-63) and the
impostor hands off at the mesh cull. Likely blink sources: (a) hard LOD0→1→2 mesh swaps, (b) the mesh
dither-out (cull*0.85..cull) with no matching impostor dither-IN. Fix = LOD crossfade dither in the
transition zone (draw both LODs, complementary Bayer by distance-in-band). DID NOT implement blind
(complex + untestable tonight).

**Hard grass transition = biome-weight blend too sharp.** Grass density is a compute blend:
`density += grassDensity * pow(weight, blendPower)` over top-4 biomes in
[GrassPlacementParamBlend.hlsl](Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl#L49); the
emit test compares a per-blade hash to that density. At a grass→no-grass boundary the biome WEIGHT field
transitions over a few texels → sharp density → hard edge. Fix = feather the emit near the threshold with
spatial noise (thin blades out over a band) OR widen the biome-weight blend. Needs the compute + Unity to
test — did NOT do blind.

**MCP GOTCHA:** `refresh_unity(scope:all)` during a heavy state dropped the MCP bridge; it does NOT
re-register while Unity sits unfocused (editor throttles to ~4 FPS). Avoid `scope:all` refreshes; prefer
`scripts` or `assets`. If the bridge is at 0 instances, the user must focus/click Unity to reconnect.

---

**Scatter placement is fully deterministic / seed-based (do not doubt this).** Every prop is
`ScatterHash.Node(worldSeed, face, level, x, y)` → `Slot(node, slotId)` — pure math, no RNG, no
session state. `ScatterId.Pack` is the stable u64 persistence key. `scatter.verify` PASSES (unique,
order-independent, region-independent, transform-stable, id+player round-trip). Golden-value unit tests
lock the hashes (see [[project-test-harness]]). A tree cannot move between loads or wander into a
player's build. If asked "is scatter seed-based / consistent" the answer is YES, proven.

**Why scatter can LOOK missing (both are NOT placement bugs):**
1. It is camera-centric + near-surface-only: gathers/draws within the region of the camera's surface
   anchor. Trees mesh-cull ~400m, impostors to the region cap, bushes/rocks ~120-250m, grass ~380m.
   In orbit (default spawn = 2.5× radius via FreeCameraController.AutoPositionOnGenerate) everything is
   beyond cull → bare sphere. **Spacebar (`ToggleOrbit`, bound in InputMapService.cs:97) drops to the
   surface.** `scatter.goto <Biome>` teleports onto a biome.
2. **The gather is SLOW (~9-14s):** per candidate it does a ground sample (AnalyticGroundSampler = 3
   elevation noise evals for radius + slope normal) + a LIVE biome eval (ColorGenerator.EvaluateBiome →
   climate + Voronoi resolve, not the baked atlas). ~370-600k candidates, re-run every 10m of camera
   move (ScatterRenderer double-buffer, one gather in flight). At the old surface fly speed (~106 m/s)
   the camera out-runs it — scatter stays gathered ~1km behind, beyond draw range → invisible while
   flying. Fix (4564b76): capped placement-only prototypes to 80m gather (redundant no-mesh scatter
   grass was scanning the full region at 2.5m spacing), impostor region mult 1.75→1.3 (~700→520m),
   surface fly speed 0.02→0.006 (~106→~32 m/s) so travel-per-gather < region. STOPGAP.

**Tier 1a landed (commit 4354691), gather ~21s → ~6s, verify PASS.** Measured the REAL production
gather at ~21s (worse than the ~9-14s guess). The dominant cost is the LIVE biome eval (~50µs/candidate),
NOT the slope normal — **Tier 1b (lazy normal) alone changed nothing** (still 21s). Tier 1a memoizes
the biome at a coarse level-9 cell centre (`sampleLevel=min(level,9)`, invocation-local single-entry
memo keyed `(face,sampleLevel,xb,yb)`, value = EvaluateBiome at the cell centre → order-independent by
construction) and reuses it for the finer candidates inside → ~6s. Also shipped: two-stage
`ISurfaceGroundSampler` (TrySampleRadius + SampleNormalAt), one shared `ScatterPlacementMath.PassesAltitudeWater`
predicate, and a **range-builder +1 cell margin fix** (`FaceSpaceCellRangeBuilder.halfExtent` — the
cell-count-symmetric square around the floored centre cell fell short of the disc by up to one cell from
sub-cell camera offset, failing verify region-independence by 2 disc-edge instances; the re-layout
surfaced a pre-existing gap). Tier 1a is an approved one-time re-layout (biome snaps to ~19.5m cells near
borders; interior byte-identical). Surface screenshot confirms lush trees/rocks/bushes.

**LEVER B SHIPPED — incremental tile cache replaced the whole-disc gather (Valheim ZoneSystem model).**
Design + Codex review: [docs/design/2026-07-29-scatter-incremental-gather.md]. `ScatterTileCache`
partitions the surface into fixed cube-face tiles at `Lt = min(7, minLevel)` (~82m). Each
`(tile, prototype)` payload is gathered ONCE via `ScatterField.GatherTilePrototype` (pure fn of tile+seed,
enumerates the prototype cells whose `ScatterQuadtree.ParentTile` is that tile) and cached; readiness is
per-`(tile,proto)` (`ulong ReadyMask`) so a far tile that entered "trees only" re-fills bushes as the
camera closes. One sequential background worker (batch 48/excursion), epoch-guarded commits, in-flight set
released in `finally` (retryable), evict beyond `maxRadius + 1 tile`, append-on-commit draw buckets +
affected-only rebuild on evict. A camera move gathers ONLY the frontier ring — validated: 200m move =
417→519 tiles in fast 6-9ms batches, no whole-disc re-scan. Commits: slice1 7f34bc0 (shared
`TryGatherCandidate` + `GatherTilePrototype` + `ParentTile`, verify PASS), slice2+3 9da9e34, hardening
b5433c4, partition guard + foliage tone 81f3dfd. **`scatter.tilecheck` PASS: tile union == whole disc,
1650 inst** (partition proof). New commands: `scatter.tiles` (cache counters), `scatter.tilecheck`
(partition proof, heavy → small region). 53 EditMode tests (added ParentTile partition tests).

**Fly cap raised 0.006→0.02 (~32→~106 m/s)** in FreeCameraController.cs + Planet.unity (2588b62) — the
stopgap is lifted because the gather is now frontier-only. Sprint (3×=~318 m/s) untested; lower the mult
if it out-runs the frontier. **ImpostorRangeMultiplier 1.3→2.0** (ScatterDtos) pushes the tree line to
~878m (was ~520) — horizon cutoff much softer. **Foliage lit shade 1.18→1.0** (FoliageLit/Scatter/
ScatterImpostor) — the 1.18 over-brightened above albedo ("too bright, doesn't fit"); clamped to full
albedo, settles into the terrain palette.

**SCATTER RENDER PATH — FINAL: standard `Graphics.RenderMeshInstanced`, NOT a custom render feature.**
History: f2615c0 moved scatter into a bespoke `ScatterRenderFeature`/`ScatterRenderPass` (opaque) +
`ScatterDepthNormalsPass` (prepass) to force it into `_CameraDepthTexture`. **All of that was DELETED in
commit 9c7c554** (Phase 1). The custom pass got scatter into depth but took it OUT of URP's shadow-caster
pass → trees cast no shadows (flat). The general fix: draw the cached instances with
`Graphics.RenderMeshInstanced` (RenderParams carry `shadowCastingMode=On`/`receiveShadows` from the DTO,
default true). Because all 3 scatter shaders carry the full pass set (UniversalForward + ShadowCaster +
**DepthNormals**), the instances participate in EVERY URP pass automatically — shadow caster, depth-normals
prepass (→ `_CameraDepthTexture`, canopy fix intact), SSAO, forward — the same path a MeshRenderer
character/structure uses. `ScatterRenderer.Render()` does gather + the RenderMeshInstanced draw via
`ScatterLodBatcher.Draw(...)`; `IScatterDrawRuntime` + the cmd-buffer batcher path are gone. **Rule going
forward: scatter is a first-class instanced renderer; do NOT reintroduce a custom per-type render pass.**

**CANOPY TRANSPARENCY (7f1492a) — the durable fix is the `DepthNormals` SHADER pass on all 3 scatter
shaders.** Root cause (proven with `_SceneDepthDebug` global in Atmosphere.shader — RED=sky, GRAY=geom):
`_CameraDepthTexture` here is built by an **SSAO-forced DepthNormals prepass BEFORE the opaque phase**
(PC_Renderer SSAO Source=1 DepthNormals). Old immediate `RenderMeshInstanced` missed it only because the
shaders had NO DepthNormals pass — NOT because RenderMeshInstanced can't reach the prepass (it CAN; verified
gray). Each DepthNormals pass mirrors ForwardLit's clip + DistanceDither so the depth footprint == visible
pixels. LESSON: if a depth-dependent effect ignores scatter, the shader needs a DepthNormals pass — the
prepass (not CopyDepth) owns `_CameraDepthTexture`; a custom render pass is NOT required.

**GRASS SHADOWS (commit 097fea7).** Grass.shader sampled `CloudShadowFactor` but NOT the Sun cast
shadow → grass stayed bright green under tree canopies. Added `_MAIN_LIGHT_SHADOWS` pragmas +
`ShaderLibrary/Shadows.hlsl` include, and `surfaceDirect *= MainLightRealtimeShadow(TransformWorldToShadowCoord(posWS))`
so shadowed grass falls to its 0.12 ambient floor. (Grass draws in Queue "Transparent-10" but still reads
the main-light shadowmap fine.) Verified grass darkens under canopies.

**IMPOSTOR RANGE 2.0→3.0 (commit 94496f9).** Trees ended at ~880m; hilltop vistas showed bare terrain
beyond. `ImpostorRangeMultiplier` (ScatterDtos, one knob) scales BOTH impostor draw distance AND
`FarGatherRadius`; 2.0→3.0 → tree line ~1200m, gather ~1280m, fills a vista to the horizon. Cost grows
as the square of the multiplier: this vista gathered ~72k inst / ~1200 tiles (was ~38k/~840); near-first +
drain-loop still reach pending=0, far ring trickles. NOT a regression — the earlier "trees fade at
distance" was just the 880m limit (impostors verified working: 15 valid, drew to 800m). To push further,
raise the multiplier (watch fill/draw) or build the true-horizon terrain-tint (still unbuilt, the cheaper
long-term answer Bryan deferred).

**IMPOSTOR "BLUE BOX" (Phase 4, commit 92d0f58).** Far-tree impostors baked as navy boxes: the bake
camera ran the full URP stack so the atmosphere/cloud/star features painted SKY into the card background
→ background came out opaque sky-blue (~0,0.15,0.29), the luminance silhouette key (ScatterImpostorBaker,
keys alpha off `lum > ~0.05` against an assumed-black bg) read blue as geometry (alpha 1) → whole quad an
opaque blue box that aerial perspective tinted navy. FIX: `cam.cameraType = CameraType.Preview` on the
bake rig — every sky/atmosphere/cloud/star/scatter feature already skips Preview+Reflection, so none paint
the bake; background clears to black, key produces a clean transparent silhouette. Verified: re-baked card
bg = (0,0,0,a=0), far tree line = green silhouettes with natural aerial haze. LESSON: any manual
render-to-texture bake (impostors, thumbnails) MUST use a Preview/Reflection camera or the world's render
features bleed into it. Bryan diagnosed this ("background is being tinted → looks like a blue box").

**SUN + CLOUD SHADOW SYSTEM (Phases 1-3, commits 9c7c554 / a81fce8 / 388b862).** Scatter now casts AND
receives via the standard pipeline. CAST: RenderMeshInstanced + ShadowCaster pass (Phase 1) — verified
canopy-shadow blobs on open ground. RECEIVE: FoliageLit + Scatter ForwardLit sample
`MainLightRealtimeShadow(TransformWorldToShadowCoord(posWS))` (Sun main light tracks `_SunParams`) folded
as `lerp(0.32,1.0, ndl*shadowAtten)`, plus screen-space AO (`_SCREEN_SPACE_OCCLUSION` +
`GetScreenSpaceAmbientOcclusion`, `indirectAmbientOcclusion`) — Phase 2. CLOUD: both shaders now
`#include "Includes/CloudShadows.hlsl"` and multiply by `CloudShadowFactor(posWS, sunDir, localSun)` — the
SAME shared factor terrain/grass/water/ocean/precip already use (Phase 3). Cloud-shadow strength is the
shared global `_CloudShadowParams.x` (currently 0.35) — raise for more drama world-wide. Extreme low sun
over-darkens dense understory (AO+shadow stack); tune the 0.32 ambient floor / AO if too much.

**LOAD POP-IN — tile-cache now drains per invocation, not one batch/frame (commit cbb1889).** The
worker did ONE batch then returned; Update relaunched it only next frame, so cold-fill throughput was
pinned to frame rate (~48 pairs/frame). Now `RunWorkerAsync` loops (background gather → main commit →
repeat) under a `DrainBudgetMs=200` wall budget, and `MaxPairsPerTick` 48→256 amortizes the
`Awaitable.MainThreadAsync` hop (~a full frame at editor FPS) → measured ~2.6→~1.6 ms/pair, ~1.7×.
Near-first queue sort unchanged so the visible foreground fills first. **Cold disc is now ~15-19k pairs**
(not the old ~5300 — the 2.0 impostor range grew the disc); at ~1.6 ms/pair single-threaded that's a
~24-30s in-editor floor for the FULL disc, but near-first means the player's immediate area is ready in
<1s. `pending`→0 confirmed, 53/53 tests. The remaining big lever is the **biome pre-filter** (skip
empty wrong-biome (tile,proto) pairs, ~16× fewer — logs show most pairs commit 0 instances) OR
parallelising the gather; both deferred (pre-filter risks the deterministic partition, needs conservative
multi-sample). Batch=256 slightly coarsens first-paint granularity (nearest ~18 tiles commit together);
fine in practice (~400 ms typical, one 3.9s dense-region outlier seen).

**Remaining (not blocking):** (1) true-horizon far-field = bake low-res tree-density tint into terrain
beyond impostor range (AAA method, cheaper than more billboards); (2) biome pre-filter / parallel gather
(above) for the full-disc cold-fill floor; (3) user to fly-test the raised cap (I can't drive input);
(4) POLYGON-look match (warmer/brighter/lusher/denser) — needs Bryan's eye + the reference image (pasted
in chat, not committed); knobs = `PlanetLookProfile.asset` grading + scatter-density DTO + grass params.
The baked surface-radius atlas idea (Lever A) is now OPTIONAL — the incremental cache made the per-move
gather cheap enough without it; revisit only if the frontier cost bites. See [[project-planet-look-dev]].
