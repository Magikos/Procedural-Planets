# Audit Summary

**Findings only — no code changed.**

Reviewed the working tree on `harvest-vertical-slice`, based on `d1e0f624ead0448f70a867f20a9389e367527293`.
The tree contains concurrent changes from other work. This audit did not alter product source or control Unity.

Scope: near placement, blade/card rendering, optional chunk placement, terrain blanket, controller lifetime, buffer accounting, and interactor history.
Four new findings remain open: two Medium bugs and two Low improvements.
The first affects the active near/card path and blanket. The other three affect chunk grass, which defaults to disabled.

The active near renderer changes blades into cards within its distance range. The optional chunk renderer is a separate layer.
The blanket remains a terrain base layer. Its independence from the blade fade is intentional.

## What came back clean

- Both placement computes roll back indirect instance counts when capacity is exceeded.
- Blade appearance uses position-based seeds rather than dispatch-order instance IDs.
- Near-field water checks use the actual candidate root.
- Normal controller teardown releases owned materials, buffers, and fallback textures. Readback callbacks check disposal.
- Grass forward and motion passes share vertex deformation and cutout code.
- The removed `GrassClumpScatter` implementation remains absent.
- Wildlife now uses a restricted interactor slot limit, leaving room for release trails.

Checks included a scoped `graphify query`, source tracing, prior-audit reconciliation, and two numerical reproductions.
The numerical checks exited with code 0. They validate equations, not GPU execution or visual quality.
Unity tests, shader import, runtime captures, and performance measurements remain pending in the queue below.
This was not a whole-project audit. It did not re-audit terrain generation, weather, or scatter asset rendering.

# Findings

## G01 — BUG: Empty corners change grass properties during interpolation

- **Category:** Bug
- **Severity:** Medium
- **Description:** The code normalizes each corner's grass properties before interpolating corners. Empty corners then contribute zero-valued properties.
- **Evidence:** `Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl:29` initializes empty properties. Lines 72–80 normalize valid properties. Lines 111–113 interpolate those results. `Assets/Graphics/Shaders/PlanetVertexColor.shader:670` and `:698` duplicate this behavior for the blanket.
- **Impact:** Grass near an empty corner becomes shorter, narrower, darker, and subject to lower slope limits. Water clearance also falls. Eligibility depends on how equivalent coverage is represented in the texture.
- **Effort:** M
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Blend unnormalized property numerators and their grass weights across all corners. Normalize once afterward. Preserve the existing density power response separately.
- **Refactor Option:** Share the property accumulation rules between placement and blanket code. Keep their distinct shading and coverage behavior.
- **Behavior note:** Changes grass and blanket boundaries. Requires fixed-pose capture comparison.

Numerical reproduction uses one grass biome with density 1, slope limit 40 degrees, and slope fade 5 degrees.
Represent half coverage inside one texel, then represent it halfway between a full and empty texel.

| Representation | Density | Slope limit | Slope fade | Keep at 30 degrees |
|---|---:|---:|---:|---:|
| Half-weight grass inside one texel | 0.5 | 40 | 5 | 1 |
| Halfway between full and empty corners | 0.5 | 20 | 2.5 | 0 |

Both inputs describe the same grass contribution. Only the spatial representation changes.
This is a property-weighting defect, separate from the previously rejected blanket brightness and coverage tuning.

## G02 — BUG: Chunk eligibility checks use the lane center instead of the emitted root

- **Category:** Bug
- **Severity:** Medium
- **Description:** Chunk placement checks water and slope before jittering each root. It does not repeat those checks at the final location.
- **Evidence:** `Assets/Resources/BiomeGrassPlace.compute:218` samples the lane center; `:225` rejects water; `:234` computes slope. Lines 312–328 jitter the root and sample its final radius. Lines 333–357 allocate and emit without final-root eligibility checks. `Assets/Scripts/Planet/Grass/GrassChunkDispatcher.cs:8` sets jitter magnitude to 1.1.
- **Impact:** Dry lane centers can emit submerged roots. Wet centers discard dry bank roots. Slope boundaries have the same problem.
- **Effort:** S
- **Fix Risk:** MED
- **Confidence:** HIGH
- **Recommendation:** Check water and slope at each final root before allocating its slot. Remove non-conservative whole-lane rejection at these boundaries.
- **Refactor Option:** Reuse the near-field eligibility approach where practical.
- **Behavior note:** Changes optional chunk shoreline and slope placement. Chunk grass defaults to disabled.

Numerical reproduction: water radius 100, clearance 0.2, lane radius 100.5, and jittered root radius 99.8.
The lane passes the current check. The emitted root is underwater.

## G03 — PERF: New chunks can receive duplicate placement dispatches

- **Category:** Complexity
- **Severity:** Low
- **Description:** Allocation reconciliation dispatches new chunks before the movement branch redispatches all chunks.
- **Evidence:** `Assets/Scripts/Planet/Grass/GrassPlacementController.cs:119` reconciles allocations; `:246` creates and dispatches new runtimes. Lines 125–131 then redispatch all runtimes after sufficient movement. `Assets/Scripts/Planet/Grass/GrassChunkDispatcher.cs:150` and `:168` dispatch those paths, followed by readback requests. `Assets/Scripts/Planet/Grass/GrassChunkRuntime.cs:96` and `:108` request the two readbacks.
- **Impact:** A newly allocated chunk can receive two identical dispatches and four readback requests in one tick. The timing cost remains unmeasured.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH on duplicate work; timing impact unmeasured.
- **Recommendation:** Skip runtimes already dispatched during the current reconciliation when processing the redispatch branch.
- **Refactor Option:** None.
- **Behavior note:** Preserving. Applies only when chunk grass is enabled.

## G04 — BUG: Chunk memory reporting omits retained pool allocations

- **Category:** Bug
- **Severity:** Low
- **Description:** The memory total includes live runtimes but excludes blade buffers retained by the free pool.
- **Evidence:** `Assets/Scripts/Planet/Grass/GrassBladeBufferPool.cs:37` retains released buffers until pool disposal. `Assets/Scripts/Planet/Grass/GrassPlacementController.cs:196` sums live runtime bytes; `:301` exposes that sum as `BufferMegabytes`.
- **Impact:** The counter can report zero memory while free buffers still retain the peak allocation. This can mislead residency and memory investigations.
- **Effort:** S
- **Fix Risk:** LOW
- **Confidence:** HIGH
- **Recommendation:** Include free-pool bytes in total allocated memory. Expose live and free amounts if the display needs both.
- **Refactor Option:** None. Keep the existing pool behavior.
- **Behavior note:** Rendering stays unchanged. Diagnostic output becomes accurate.

# Refactoring Plan

These are proposed slices, not implementation authorization.

1. Address G01 in both placement and blanket property blending. Validate equivalent corner representations before visual comparison.
2. Address G02 if the optional chunk path will be used. Validate final roots across water and slope boundaries.
3. Address G03 by removing duplicate dispatch work. Compare instance counts and dispatch counts at identical poses.
4. Address G04 without changing pooling. Compare retained allocation bytes with reported totals.

The shared property formula is the useful boundary for G01. No new controller interface or rendering framework is needed.
Keep accepted brightness, coverage ramps, distance fades, and disabled-layer defaults outside these fixes.

# Unity Test Queue

**Status: PENDING — another agent owns Unity.**

This is a saved test queue, not a submitted or running Unity job.
Run it after the current owner releases Unity. Record seed, camera pose, quality tier, layer flags, and current commit first.
Archive captures and sidecars before any subsequent capture can prune them.

| Order | Check | Procedure and required evidence | Status |
|---|---|---|---|
| 1 | Existing interaction tests | Run `ProceduralPlanets.Tests.CreatureGrassInteractionTests` in EditMode, outside a loaded Planet world. Save results and exact failures. | PASSED 5/5 on 2026-09-10; runner JSON archived |
| 2 | G01 corner equivalence | Compare density, height, width, slope, clearance, and tint using current and pre-G01 HLSL. Capture a grass/desert slope boundary with near grass and blanket isolated. | GPU PASSED on 2026-09-10; matched boundary captures remain pending |
| 3 | G02 final-root eligibility | Enable chunk grass in an isolated test world. Test dry-center/wet-root and wet-center/dry-root lanes, then a slope boundary. Inspect emitted roots against sampled water and slope. Restore layer flags afterward. | PENDING; synthetic fixture not authored |
| 4 | G03 duplicate work | Enable chunk grass and move more than 25 m while allocating chunks. Count placement dispatches and readback requests per new runtime. After a fix, require one dispatch and two readbacks per new runtime. | PENDING |
| 5 | G04 retained memory | Allocate chunk buffers, then leave their residency range. Compare live plus free-pool allocation bytes with the displayed total. | PENDING |
| 6 | Active layer regression | Traverse blade/card handoff and the 144–200 m fade at a fixed seed. Check the blanket, paths, shorelines, shadows, wind, and interactor recovery. Capture motion with temporal antialiasing enabled. | PARTIAL: physical/hybrid/blanket smoke captures saved; full movement matrix remains pending |
| 7 | Prior F21 measurement | Cross the 500/550 m activation gates after filling timing windows. Record allocation, main-thread and render-thread timing, and GC. Report average and p95. | PENDING |

No runtime or visual verdict is claimed. The math reproductions passed outside Unity.

# Prior Audit Reconciliation

Source: `docs/audit/2026-07-22-consolidated-code-audit.md` and its grass-history ledger.
The current audit ledger contains historical entries awaiting revalidation; this focused audit does not replace that project-wide ledger.

| Prior item | Status | Current evidence or disposition |
|---|---|---|
| F09 disabled clump allocation | RESOLVED | `GrassClumpScatter` remains absent. |
| F17 disabled branches and overlay wording | PARTIAL | Suppression remains disabled by `GrassNearFieldController.cs:38`; chunk compute frustum culling remains disabled by `GrassChunkDispatcher.cs:220`. Coordinator lines 54–61 now explain the independent base layer. Do not force overlay distances to match near grass. |
| F18 release-trail starvation | PARTIAL | `CreatureGrassInteraction.cs:90` reserves slots through its registration limit. `CreatureGrassInteractionTests.cs` covers that policy. Eight unrestricted interactors can still fill all eight slots; measure that case before changing policy. |
| F21 near allocation/readback churn | OPEN | Near buffers still allocate 1,500,000 instances. Coordinator activation still recreates them. Timing impact remains unproven; queue item 7 owns measurement. |
| G1 multiple fade mechanisms | SUPERSEDED | Accepted thinning plus visual fade remains intentional. |
| G2 brightness and G3 coverage remap | REJECTED | Preserve prior rejected tuning decisions. G01 concerns equivalent-input property math. |
| G4 independent blanket window | SUPERSEDED | The blanket is an intentional base layer. |
| G5 scattered transition constants | PARTIAL | Main near distances are centralized. Diagnostic overlay weights still embed 144/200/600 at `PlanetVertexColor.shader:803`. Treat them as diagnostics, not actual layer occupancy. |
| G6 promote/delete chunk grass | REJECTED | No change to the disabled default is proposed. |
| G7 altitude pop/allocation | PARTIAL | Fade remains present; allocation cost remains F21. |
| G8 corner/suppression notes | PARTIAL | Suppression remains inactive. G01 now provides a concrete property-interpolation counterexample. |
| July A1–A6 correctness set | RESOLVED for grass checks | Indirect-count rollback, fallback bounds, and current initialization paths remain present. Weather-specific portions were outside this audit. |
| July B3/B5 and C2/C3 | OPEN/PARTIAL | Covered by F21 and F17 above. No duplicate new findings. |
| July D6 trails | PARTIAL | Covered by F18 above. |
| July D8 bounds ownership | RESOLVED | Named padding remains. Runtime culling captures are still pending. |
| July D9 depth pass | REJECTED | No new depth-prepass recommendation. |

No prior audit files were removed. Rejected tuning and disabled-layer decisions remain in force.

# Questions for the User

None required to complete this audit. Fix selection remains a separate decision.

# G01 implementation follow-up — 2026-09-10

Bryan authorized G01 through the coordinating snow task. The historical audit above remains unchanged.
G01 now accumulates property numerators and weights across corners, then normalizes once.
Both `GrassPlacementParamBlend.hlsl` and the terrain blanket use this order.
The density power response and per-corner saturation remain unchanged.
Snow sections and rock-mask suppression remain intact. G02–G04 were not implemented.

`python tools/audit/test_grass_corner_weights.py` passed four tests with exit code 0.
The tests cover equivalent coverage, unequal grass weights, density response, and empty or tiny spatial weights.
These tests validate the numerical contract. They do not execute HLSL.
The scoped whitespace check passed. Unity import, GPU tests, and visual validation remain pending with the snow task.
The grass task ran no Unity commands or concurrent builds.

The coordinating Procedural Generation task subsequently reported clean imports for both grass compute shaders.
Its 18 climate/terrain/scatter tests passed. These tests do not replace the grass GPU fixture or boundary captures.
The final terrain error query was empty. The compiler still reported "use of potentially uninitialized variable (EvaluateGrassOverlay)".
Read-only inspection traced that warning to the existing early return before G01 executes; its helper zero-initializes the return value.
The warning's compiler behavior still needs isolated verification. No warning fix was applied during generation.
The snow task also excluded snow from grass overlay coverage, preserving G01.
Unity passed to Rivers. The grass GPU and visual queue awaits the next editor window.

# G01 GPU validation — 2026-09-10

Rivers released Unity without edits. Grass ran the GPU fixture and existing interaction tests.

- GPU: NVIDIA GeForce RTX 3090, DX12.
- The fixture extracts current placement and blanket functions directly from their source files.
- The comparison fixture extracts the same functions from pre-G01 commit `d1e0f624`.
- Five scenarios ran against each version: packed coverage, split corners, unequal density, empty coverage, and small spatial weight.
- Current placement and blanket retained the 40-degree slope limit and accepted the 30-degree slope.
- Pre-G01 placement and blanket reproduced the 20-degree limit and rejected that slope.
- Unequal grass contributions produced the expected 36-degree limit. Density matched the baseline in every scenario.
- Equivalent inputs matched height, width, clump strength, clearance, tint, and climate tint multipliers.
- All assertions passed. The generated probe assets were removed after execution.
- `CreatureGrassInteractionTests` passed 5/5, with no failures or skipped tests. Job: `196834cb265d43829400e8489528327d`.

Reproduction scripts: `tools/audit/build_grass_gpu_probe.py` generates the temporary compute assets.
Run `tools/audit/run_grass_gpu_probe.cs.txt` through Unity `execute_code`, then remove `Assets/Tests/GrassAuditProbe` through AssetDatabase.
The scripts change no planet data. The compute fixture executes extracted functions; it is not a full placement dispatch or terrain render.

Evidence directory: `local-only/grass-audit/2026-09-10/`.

| Artifact | Meaning |
|---|---|
| `gpu-results.json` | Both shader versions, all scenarios, returned vectors, and assertion results |
| `interaction-tests.json` | Full Unity runner result for all five tests |
| `surface-smoke.json` | Seed, camera, time, quality, grass state, and placement counts |
| `gameview-physical.png` | Current physical grass and blanket |
| `gameview-hybrid.png` | Current hybrid grass and blanket |
| `gameview-blanket.png` | Current blanket with near grass disabled; scatter remains visible |

Live smoke seed: `1691104419`. Near grass emitted 1,215,554 instances with zero overflow.
The surface view showed physical grass, hybrid grass, and the blanket without a missing-shader artifact.
These are after-only smoke captures. They do not certify the visual change at a biome boundary or moving-camera transitions.
Earlier `surface-*.png` captures used explicit camera rendering; use the `gameview-*.png` set for live indirect grass evidence.
The first exploratory surface position was underwater and does not count as grass validation.

Runtime inspection also corrected an earlier scope assumption: `GrassRenderDiagnostics` defaults to `Physical`.
Hybrid/card rendering exists but is optional. This pass restored `Physical` after its capture.

The live console reported these shader warnings during the fresh run:

```text
Shader warning in 'RainParticleUpdate': Program 'RainUpdate', warning X4714: sum of temp registers and indexable temp registers times 64 threads exceeds the recommended total 16384.  Performance may be reduced at kernel RainUpdate (on dx12)
Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)
```

These warnings remain open for separate inspection. This pass made no product changes to address them.
The camera restored with zero position and rotation error. Time restored to `0.273102939`, unfrozen, with time scale 1.
Near and blanket requests restored to enabled; chunk grass stayed disabled. Unity remains in Play mode.
Graphify updated successfully after adding the reproducible probe tools.
