---
name: project-scatter-gather-perf
description: "2026-08-01 — scatter fly-feedback round: gather now drains at job speed (sync Complete, backlog median 0 @fly-cap), biome borders softened (KernelRadius 6→12), and bushes/rocks/reeds got the impostor far-tier (gate 300→120) killing mid-prop pop-in. Earlier: incremental TILE CACHE, coarse-biome memo, parallel Burst gather. If scatter \"looks missing\" it's altitude/orbit, NOT placement or speed"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5a0ee82f-d367-47b6-bbee-397761463f85
  modified: 2026-08-01T15:10:28.618Z
---

## 2026-08-01 — fly-around feedback round (5 items; all shipped)

User flew the real planet and gave 5 items. Fixes (all committed on branch `scatter-placement`):

1+2. **Grass too bright at sunrise + shadows pop** — `6bb3302`: Grass.shader tip ramp dimmed
   (`0.40,0.52,0.32`→`1.0,0.99,0.64`, was brighter), day floor 0.12→0.07 / coeff 0.82→0.86;
   PC_RPAsset `m_CascadeBorder` 0.107→0.25 (wider shadow-cascade fade). User to eyeball at real sunrise.

3. **Bush/rock pop-in (no far-LOD)** — `3605ce0`: **the impostor gate is the knob** —
   `ScatterDtos.cs` `ImpostorMinMeshCull` **300f→120f**. `HasImpostor => MaxCullDistance >= gate`;
   impostor reaches `ImpostorRangeMultiplier`(3.0)×cull. So bushes(120→360m), rocks(250→750m),
   reeds/wildflowers now billboard past mesh cull like trees; flowers/mushrooms/grass(≤90) stay short.
   Derived policy, no per-asset authoring. Impostor disc sits INSIDE the tree gather radius → no disc
   expansion, just more billboards. Verified: bakes clean, savanna reads to the shore w/ no cull line.

4. **Can still outrun gather at speed** — `91c5d75`: `ScatterTileCache.GatherBatchBurst` was yielding a
   **whole frame per batch** (`while(!IsCompleted) await NextFrameAsync`) while the parallel Burst job
   finishes in ~ms → ~7× throttle. Now `_pending.Complete()` inline on main. Backlog **avg ~5000→median 0**
   @150. NOTE: **off-main Complete THROWS** "Job control thread mismatch" — Unity forbids completing a job
   from a worker thread; `Awaitable.BackgroundThreadAsync()+Complete()` is NOT allowed. Must be main-thread.

5. **Biome lines (hard green→dirt)** — `91c5d75`: `BiomeMapBaker.cs` `KernelRadius` **6→12** (13×13→25×25
   counting window). Terrain color AND grass density both read the same baked `_BiomeIds/_BiomeWeights`
   atlas, so one widen softens both. NOT the Voronoi SecondaryWeight (legacy path). Ceiling ~HighResolution=128.

**Perf note (impostor trade):** far-LOD adds gather/commit load. @fly-cap 106 m/s backlog median 0 (fine);
@150 (beyond cap) densest biome builds ~1300 backlog. Acceptable — camera can't exceed the cap in play.
**Editor-cycle gotcha:** a settings-DTO const (ImpostorMinMeshCull) or a compile-time const (KernelRadius)
needs a **clean stop→play**; a `refresh_unity` hot-reload keeps the OLD world/DTO and won't re-derive.

## 2026-07-31 — DONE: parallel Burst gather (the pop-in fix) shipped, verified byte-identical

Replaced the single-thread serial gather with a `[BurstCompile] IJobParallelFor` over the batch's
(tile, prototype) pairs. Commits: `dc47284` (stage 1: golden noise test), `568ddb9` (stage 2: Burst core
+ parity test), `90e6900` (stage 3+4: wired into the cache). **NOT full ECS/Entities** — Bryan re-picked
"Full ECS/DOTS" twice, but exploration proved Entities is unnecessary (draw path already fine) AND both
Entities/Burst need the SAME noise port, which is MOSTLY ALREADY DONE. So this is DOTS Jobs+Burst on the
GATHER only; the RenderMeshInstanced draw is untouched.

**Key architecture facts (reuse these):**
- Elevation noise is ALREADY blittable + `[BurstCompile]` + byte-identical: `NoiseData`,
  `NoiseFilterData`/`NoiseFilterEvaluator` (Noise.cs, NoiseFilters/NoiseFilterData.cs);
  `ShapeGenerator.BuildNoiseFilterData(Allocator)` emits the NativeArray. Placement math
  (`ScatterPlacementMath`) is already a pure "CPU<->GPU parity surface".
- The large managed piece (VoronoiBiomeField kd-tree + climate) is NOT ported — SIDESTEPPED. The gather
  memoizes biome per coarse `(face, min(level,9), cell)` center, so `ScatterBiomePrecompute` (managed, off
  main thread) evaluates `EvaluateBiome` for exactly those cells into a `NativeParallelHashMap` the job
  reads — byte-identical, no Voronoi port.
- New files: `ScatterGatherBurst.cs` (per-candidate mirror), `ScatterGatherJob.cs` (job + PairInput/
  ProtoParams/BiomeSample + NativeStream output grouped per pair), `ScatterBiomePrecompute.cs`,
  `IBurstElevationSource.cs` (AnalyticGroundSampler implements it; non-analytic samplers fall back to the
  serial path). Persistent per-world native buffers built at `Configure`, freed after `_pending.Complete()`.
- Awaitable-compatible completion (NO Task.Run): schedule on main, `while(!_pending.IsCompleted) await
  Awaitable.NextFrameAsync(); _pending.Complete();`.

**Determinism: two tiers.** Threshold math (elevation/normal/AreaKeep-sqrt/membership-pow → accept gate)
must be bit-identical → reuse shared pure helpers. Post-acceptance transforms (pos/rot/scale) only need
epsilon (the 2 native Quaternion ctors are re-derived in Unity.Mathematics). Burst-compiled noise differs
from managed-IL noise by ~1-2 ULP → ~1mm position at 5000m radius (harmless; parity-test position epsilon
is scale-aware 0.05m; the ID SET is exact). Guards added: `NoiseFilterEvaluatorGoldenTests` +
`ScatterGatherParityTests` (schedules the real job). **LIVE proof in a generated world: managed==burst
4287==4287 ids, 0 missing/extra, across all 64 protos, levels 8-13 (incl below-9), real Voronoi.**

**Measured (`ScatterFlyBench`):** @60 m/s backlog avg **234** pairs (was ~10,564 serial = ~45x less),
world holds 340k instances; @150 m/s world STAYS populated 332k-471k (serial collapsed to 110 tiles).
Pop-in fixed.

**Frame-spike follow-ups DONE same session (commits `9bba922`, `287a725`):**
- **Incremental eviction** (`9bba922`): `RebuildBuckets` (O(all live instances) rebuild on every eviction)
  replaced by `ScatterDrawBuckets` — per-proto packed matrices+positions keyed by tile, a departed tile
  swap-removed in O(its own instances). Renderer iterates buckets by index/order-agnostically so
  swap-remove is safe. `ScatterDrawBucketsTests` guards it. @150 worst frame 405->215ms, samples 65->105.
- **Commit-spread** (`287a725`): committing a whole batch's baked matrices (~100k inst) in one frame was
  the last spike; now yield a frame each `CommitInstancesPerFrame`=20k committed. @150 samples 105->150.

**Final measured (editor, grassland fly):** @60 m/s (normal) frame **median 25ms (~40fps)**, p90 45ms,
worst 93ms AT STARTUP (0.6s) — steady play is smooth, pop-in gone, world stays full. @150 m/s (beyond the
~106 m/s fly cap) median 103ms — that steady cost was the **per-frame DRAW-SCAN**: `ScatterLodBatcher`
scanned ALL of each proto's instances 5x/frame (4 LOD bands + impostor) RECOMPUTING the camera distance
each pass.

**Draw fix DONE (commit `ad20210`):** compute each instance's camera distance ONCE per proto per frame into
a reused scratch array; every band reads it. Behaviour-identical. @150 frame median 103->28ms (per-instance
draw cost ~-34%); static dense grassland 69k inst = 31ms/frame full-scene (editor). Profiled the residual
scatter draw via a `ScatterRenderer.DrawEnabled` toggle (frame ON vs draw-off): **12ms, 610 instanced draw
calls, 68M tris** — it was DRAW-CALL bound (ScatterLodBatcher chunks every proto x part x LOD band into
<=1023-instance RenderMeshInstanced calls, inflated ~1.56x by the crossfade double-draws), NOT scan-bound.

**GPU-DRIVEN INDIRECT DRAW — DONE (commits `a3c0dd7`/`05d0754`/`a1e92b0`, default ON).** Replaced the CPU
RenderMeshInstanced batcher with Graphics.RenderMeshIndirect + a GPU cull compute (reused the grass
GPU-draw scaffolding: GraphicsBuffer + IndirectArguments + Resources.Load compute). Key techniques:
- **Shader `procedural:setup` dual-path** (all 3 scatter shaders FoliageLit/Scatter/ScatterImpostor): add
  `#pragma instancing_options procedural:setup` + `StructuredBuffer<float4x4> _ScatterMatrices/_ScatterMatricesInv`
  + `StructuredBuffer<uint> _ScatterVisible` + a `setup()` that (under UNITY_PROCEDURAL_INSTANCING_ENABLED)
  writes `unity_ObjectToWorld = _ScatterMatrices[_ScatterVisible[unity_InstanceID]]`. Because setup() fills
  unity_ObjectToWorld, ALL existing vertex/wind/interactor/ShadowCaster/DepthNormals code works UNCHANGED.
  The UNITY_PROCEDURAL_INSTANCING_ENABLED guard leaves the RenderMeshInstanced fallback untouched (dual-path).
- **`ScatterCull.compute`**: per LOD band, distance-only cull (NO frustum — off-camera shadow casters must
  stay), appends master-indices to a per-band _Visible buffer + fills IndirectDrawIndexedArgs.instanceCount.
  Mirrors ScatterLodBatcher's [near2,far2) bands + 15m crossfade + impostor tier exactly. Second kernel
  `BuildInverse` computes the world->object AFFINE inverse on the GPU (so NO CPU Matrix4x4.inverse).
- **Dirty-only upload** (`ScatterDrawBuckets` per-proto dirty flag -> `ScatterTileCache.ConsumeDrawDirty`):
  re-upload+re-invert a proto's master ONLY on gather churn, never per-frame (static camera uploads nothing).
- `ScatterGpuDraw` owns per-proto master/inv buffers + per-band visible/args buffers, one dispatch + one
  RenderMeshIndirect per band. Falls back to the CPU batcher if `!Supported` (no compute/SM4.5).
MEASURED: static dense scatter draw 11.4ms GPU vs 14.2ms CPU; flythrough @60 frame median **37ms vs 73ms
CPU (~2x)**, p90 55 vs 97ms — the GPU advantage GROWS under load (cull/scan on GPU, only churned protos
re-upload). Verified visually identical (LODs/impostors/crossfade/positions/normals) + correct shadows;
EditMode 63 green. GOTCHAS: RenderMeshIndirect wants IndirectDrawIndexedArgs (5-uint indexed args, vs grass's
4-uint RenderPrimitivesIndirect); the CPU inverse on churn was the flying-spike (GPU inverse fixed it);
per-band dispatch/args-reset overhead caps the static win (a per-proto single-dispatch would widen it -
future). Scatter-perf arc COMPLETE (7 gather/eviction commits + 4 draw commits: distance-precompute + GPU 1-3).

## 2026-07-30 — perf/LOD/seam diagnosis + dev tooling (Bryan flagged: slow load, LOD blink, hard grass edge)

**Perf root cause (the "fly ahead of the scatter" pain):** placement is SINGLE-THREADED. `ScatterTileCache`
gathers a batch of independent `(tile, prototype)` pairs in a plain serial `for` loop on ONE
`Awaitable.BackgroundThreadAsync` worker ([ScatterTileCache.cs:243-247](Assets/Scripts/Planet/Scatter/ScatterTileCache.cs#L243)).
Each pair writes its own `_batchResults[i]` → embarrassingly parallel. Fix = PARALLELISM, not a new
architecture. My recommendation was **Burst `IJobParallelFor`** (refactor `GatherTilePrototype`+placement
math to native/Burst-compatible; fits the "Burst for hot work" rule; ~Ncores+SIMD). **Bryan chose full
ECS/DOTS anyway** — respect it, but scope it carefully (it's a weeks-long rewrite of scatter/render/
persistence; consider an incremental hybrid). Priority he set: **bugs now, perf (ECS) after.**

**RESULTS (2026-07-31, bench actually run after Unity restart):** flew 15s at each speed from a loaded
grassland start. **150 m/s: OUTRUN** — backlog grew 0 -> 32,233 pending pairs (never caught up), live tiles
COLLAPSED 1415 -> 110, instances 44k-115k (the pop-in). **60 m/s: still OUTRUN** — persistent ~10k backlog
(avg 10,564) but the visible region held (189k-213k instances). Worst frames 159-186 ms (main-thread
commit/evict stalls, separate issue). The background worker ALREADY runs every frame with a 200 ms budget
(ScatterTileCache.Update line 152) — it's maxed; only PARALLELISM raises throughput. Confirms the fix is
Burst-jobify the gather or the ECS rewrite Bryan chose (deferred, "perf after bugs").

**LOD blink FIXED** (commit `3913c4a`, verified with the LOD debug view): the debug view showed HARD colour
bands (green LOD0 | yellow LOD1 | orange LOD2) with razor boundary lines = instant mesh swaps = the blink.
Fix in `ScatterLodBatcher`: overlap adjacent bands by `TransitionWidth`=15 m and set the material's existing
`_FadeStart/_FadeEnd` per band so the OUTGOING LOD dithers out over its last 15 m while the INCOMING LOD is
already drawn solid underneath (one-sided screen-door crossfade, no shader change). Debug view after shows
soft dithered overlaps; real view clean.

**Grass hard edge FIXED** (`3913c4a`): `GrassNearFieldPlace.compute` replaced the binary `density <= 0.001`
cutoff with stochastic thinning `keep if Hash01 <= smoothstep(0, 0.5, density)` — grass feathers out with a
dithered edge across a biome boundary instead of a line. (If still too abrupt, widen the 0.5 or add noise;
the far-field blanket may need the same if its edge is separate.)

**Dev tooling I built (commit `e5ea644`, RAN successfully):**
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
