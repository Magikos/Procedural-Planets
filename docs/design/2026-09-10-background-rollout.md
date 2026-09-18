# Background calculation rollout

Bryan authorized further background work on 2026-09-10, with gameplay preservation as a requirement.
This follows `2026-09-10-npc-jobs.md`. Rivers and ProcGen released Unity before validation.
Unity was returned to Rivers in EditMode after the checks below.

## Implemented: scatter planning

`ScatterTileCache` now runs candidate range calculation, tile sorting, and prototype filtering on a background thread.
It uses Unity `Awaitable.BackgroundThreadAsync` and returns through `Awaitable.MainThreadAsync`.
The existing Burst gather still generates placements. This change does not alter placement rules, density, draw ranges, or LOD choices.

The main thread captures readiness bitsets and in-flight pairs before scheduling.
Reusable arrays, dictionaries, and lists avoid an allocation for each resident tile on every replan.
Prototype tables remain immutable during a plan. Reconfiguration replaces the tables instead of changing captured arrays.
Only one planner owns the snapshot and scratch buffers at a time.

The gather continues draining the previous queue while the worker constructs the next queue.
Publication checks current readiness and in-flight work again. Results committed during planning cannot enter the queue twice.
A configuration epoch rejects results from an earlier world. Reset does not release scratch ownership while the worker uses it.
Planning failures log their exception and allow another attempt.

Eviction, result publication, draw-bucket mutations, and instance commits remain on the main thread.
These operations access live cache state or rendering data. No EventBus scheduler or second scatter manager was added.

## Measurements

The initial scene camera was approximately `(-4844.50, -1341.80, 195.04)` with 3,619 resident scatter tiles.
The stage comparison uses 12 direct invocations against a warm cache. Reflection overhead is included.

| Work | Main-thread median |
|---|---:|
| Previous candidate calculation | 1.141 ms |
| Previous tile sorting | 1.204 ms |
| Previous prototype filtering | 3.336 ms |
| New readiness snapshot capture | 0.576 ms |

The three moved stages previously totaled about 5.68 ms per replan, spread across three frames.
They now execute on a worker; this removes main-thread work rather than eliminating total calculation cost.
The first runtime capture reported worker thread 2413 and main thread 1. Its completed plan took 6.24 ms on the worker.

A separate 200-metre camera move completed its plan in 4.07 ms of worker time.
The three nonzero main-thread planning frames measured 0.890, 1.594, and 0.591 ms.
They cover eviction, capture/scheduling, and publication. The queue contained 6,961 pending pairs after publication.
Another 100-metre move completed and drained its queue within the observed 240 frames.

The runtime scene had 13 live animals. Creature simulation averaged 0.855 ms, with a 1.316 ms p95.
Creature presentation averaged 0.619 ms. The latest resource search took 0.739 ms.
CPU and GPU frame averages were 15.06 and 14.01 ms in that observation window.
Those frame measurements describe this scene; they do not establish an equivalent-camera frame-rate improvement over the earlier river capture.

## Validation

- Core build passed with zero warnings and errors.
- Planet build passed with 19 existing warnings and zero errors.
- Unity imported the scripts and passed 21 targeted EditMode tests.
- New tests cover face, edge, and corner range membership, transformed planets, nearest-first order, and duplicate prevention.
- Readiness tests cover prototype indices 0, 64, and 129, commits after capture, and replacement prototype tables.
- In-flight tests cover changes between capture, filtering, and publication.
- Existing scatter bucket, fish job, and creature detail tests passed in the same run.
- Runtime evidence confirms that the changed path executes on a background thread.
- Reset during active planning returned to Idle with zero published plans, queued pairs, or live tiles, even after configuration became active again.

The console retained existing `GrassNearFieldPlace` and `RainParticleUpdate` shader warnings and a Unity AI `SettingsResult` service error.
The observed Editor log tail contained no new planner, native collection, or job disposal errors.

Evidence is stored under `local-only/scatter-planning/`: build logs, `before.json`, `runtime.json`, `movement.json`, and `reset.json`.

## Further candidates

The table distinguishes feasible work from work already moved. These candidates are not implemented by this slice.

| Candidate | Evidence and proposed boundary | Gameplay protection |
|---|---|---|
| Animal food/water search and approach tests | `PlanetCreatureResources.Search` still evaluates shoreline rays, approach corridors, and sight on the main thread. Food candidate gathering is already asynchronous. Latest search: 0.739 ms. | Capture exact terrain and water query inputs. Apply resource memory and consumption on the main thread. Reject stale world/entity results. |
| Fish and animal terrain sight | `ThreatRegistry.HasTerrainSight` and ecology observation use the current terrain surface. Fish movement is already Burst; threat selection is still managed. | Preserve the current leaf selection and bilinear radius sampling. Analytic terrain is not an equivalent replacement. Keep immediate damage and combat responses authoritative. |
| Animal movement and pose calculation | Simulation averaged 0.855 ms for 13 animals; presentation averaged 0.619 ms. Separate pure calculations from driver and Transform writes before scheduling. | Preserve tick order, attack timing, support contacts, and interaction results. Measure snapshot and publication costs before widening the rollout. |
| Terrain visibility selection | Selection still walks the terrain trees on the main thread. Mesh generation already uses jobs. Earlier investigation measured about 0.63 ms. | Capture tree state and reject outdated selections. Coordinate with ProcGen before changing terrain ownership. |
| River generation during loading | River construction remains a startup calculation candidate. It does not explain steady-state animal update cost. | Coordinate with Rivers. Preserve deterministic generation and publish progress from actual completed stages. |

The next shared prerequisite for NPC queries is an exact terrain-query snapshot.
`Planet.TryGetSurfaceRadius` routes to the active provider. The chunked provider finds a leaf and samples `PlanetChunk.CpuVertexRadii` bilinearly.
No reusable snapshot of that complete query contract exists in the inspected surface code.
Its design must cover leaf changes, missing CPU grids, terrain edits, planet transforms, and world lifetime.

Off-screen residents already use elapsed-time coarse simulation. Polling every absent resident on a worker would add unnecessary work.
GPU frame time also remains material. Moving more CPU calculations cannot remove a GPU limit by itself.
