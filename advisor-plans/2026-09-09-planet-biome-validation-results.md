# Planet and biome validation results — 2026-09-09

**Findings only — no product code changed.** Bryan handed over Unity for this validation.
The Editor ran Unity `6000.7.0a5` against the dirty tree based on `d1e0f62`.

All 13 existing baseline tests passed. Both code-health builds passed without warnings or errors.
Targeted probes reproduced the seven reported defect mechanisms, with the resource probe limited to Low terrain meshes.
The water-mesh ownership claim remains source-backed; it did not receive a repeated live replacement test.

Evidence directory:
`local-only/debug-screenshots/baselines/2026-09-09-planet-biome-review/`.
Each probe JSON includes its executable C# body and returned result. Probes compiled in memory; they did not add source files.

## Baseline tests and builds

Unity job: `6def9bb996224b348c583e34c2f04b3b`.
Result: **13 passed, 0 failed, 0 skipped**, in 6.164255 seconds.

- `ProceduralPlanets.Tests.NoiseFilterEvaluatorGoldenTests`: four tests.
- `ProceduralPlanets.Tests.BiomeMapBakerParityTests`: seven tests, including worker isolation and timing.
- `ProceduralPlanets.Tests.WaterBodyCombinedSampleTests`: two tests.

Complete results: `editmode-results.json`.
Serial build commands used `dotnet build ProceduralPlanets.Core.csproj --no-restore`
and `dotnet build ProceduralPlanets.Planet.csproj --no-restore`.
Both returned exit code 0, 0 warnings, and 0 errors. Full logs: `core-build.log` and `planet-build.log`.

The smoothing timing test measured eight chunks per sample:

| Run | Exhaustive | Sliding histogram |
|---|---|---|
| 0 | 173.256 ms | 62.472 ms |
| 1 | 176.258 ms | 57.517 ms |
| 2 | 173.691 ms | 57.919 ms |

These measure the smoothing routine, not complete planet generation.

## Defect probes

| Finding | Result | Evidence file |
|---|---|---|
| G01 seed collision | Seeds 256 and 65536 returned identical elevations at all 1,734 sampled directions. Maximum difference: 0. | `seedLow-probe.json` |
| G02 lake shore | At height 0.003 above water, the resolver returned 100% land membership. The baked texel remained 100% LakeShore. At height 0.00125, a 50% land resolver result also baked as 100% LakeShore. | `biome-probe.json` |
| G03 shared atlas endpoints | Reversing the copy order of two adjacent synthetic maps changed 63 atlas pixels. Corresponding shared-edge red bytes were 80 and 82. | `biome-probe.json` |
| G04 Low cancellation | A canceled token with outstanding mesh jobs returned `System.ObjectDisposedException` instead of cancellation. The corrected probe reproduced it in 29.8735 ms. | `cancel-probe.json` |
| G05 Low mesh ownership | Ten provider allocation/disposal cycles left 6, 12, …, 60 owned meshes alive after destroying their parents. The probe then explicitly destroyed all 60 meshes. These were allocation-path tests, not ten populated planet generations. | `seedLow-probe.json` |
| G06 Low transform | A translated, rotated, scaled parent left its generated face at world origin. Local scale was 0.5 rather than 1, with nonidentity local position and rotation. | `seedLow-probe.json` |
| G07 lake/water cancellation | Cancellation immediately after launching the rebuild took 6,303.9293 ms to finish. The lake map changed after cancellation. The probe restored the original map and shader counters. | `water-cancel-probe.json` |

G04 exact failure:

```text
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'The NativeArray has been disposed, it is not allowed to access it'.
```

G07 eventually returned the expected exception, but only after the heavy work:

```text
System.OperationCanceledException: The operation was canceled.
```

G07 used the existing `RebuildWaterCmd` path. It exercises the same lake builder and water owner as startup.
This did not interrupt a complete planet-generation transaction or world teardown.
The probe verified map replacement, not a visible frame containing that replacement.

## Actual scheduled Burst jobs

The existing golden tests call the evaluator directly. Additional probes scheduled `TerrainFaceMeshJob` through the real job API.
Burst was enabled. Each case compared 1,089 output elevations against managed sampling at the job's own generated directions.

| First layer enabled | Later layers masked | Exactly equal | Maximum absolute elevation difference |
|---|---|---|---|
| Yes | Yes | 149 / 1,089 | 0.000000116415322 |
| Yes | No | 108 / 1,089 | 0.0000126808882 |
| No | Yes | 48 / 1,089 | 0.000000119907781 |
| No | No | 101 / 1,089 | 0.0000126808882 |

Evidence: `burst-probe.json`.
**Bitwise managed/Burst parity is not established and this probe disproves it for these samples.**
The largest unmasked difference corresponds to about 6.34 cm at radius 5,000.
The enabled-first-layer masked case corresponds to about 0.58 mm.
These are numerical differences, not proof of visible placement failures.
Define accepted tolerances and test threshold-sensitive callers before deciding whether to change Burst arithmetic.
Do not loosen golden tests or change terrain from this result alone.

## Fresh generation timing

The unmodified Planet scene completed one fresh Play run:

| Phase | Duration |
|---|---|
| Initialize | 2,573 ms |
| Terrain | 8,819 ms |
| Lakes | 1,003 ms |
| Colors | 14,309 ms |
| Climate map | 294 ms |
| Water | 5,099 ms |
| Finalize | 2,170 ms |
| Total | 34,273 ms |

Settings: subsystem seed 1691104419, radius 5,000, High mode, depth 4, quality index 0, Burst enabled.
Generation built 2,046 chunks, baked 1,536 leaf maps, and produced 95 water bodies.
Biome stage timing reported 5,427.8 ms for vertex work, 6,314.4 ms for map baking,
1,404.8 ms for retain/upload, and 609.3 ms for atlas work.
Worker CPU sums in the detailed log overlap and must not be added as wall time.
Evidence: `runtime-timings.json`.

This single Editor run identifies remaining costs. It is not a before/after speedup result or a standalone-player benchmark.
Terrain-copy, water-traversal, and memory substages still need instrumentation before selecting larger rewrites.

## Probe limitations and corrected harness errors

- G05 did not quantify repeated populated water-mesh replacement.
- G06 combined translation, rotation, and uniform scale in one probe; separate transform cases remain useful regression coverage.
- G04 used a token canceled before provider entry, while jobs were still outstanding at its cancellation branch.
  It did not inject a separate scheduling failure.
- The first cancellation harness read `IsCompleted` after consuming its Awaitable. It returned `Runtime error: Awaitable is in detached state`.
  Its saved result already contained the product failure. A corrected harness reran and reproduced G04 independently.
- A proposed water-mesh identity probe failed to compile with `'Object.GetInstanceID()' is obsolete: 'Use GetEntityId instead.'`.
  It performed no mutation. Repeated water ownership testing remains open.
- No fixes were applied. Post-fix invariance, legacy seed compatibility, and visual acceptance remain pending implementation.

## Editor restoration

The handed-off scene was `Assets/Scenes/Tests/HumanoidAnimationReview.unity`, playing and unpaused, with no unsaved scene changes.
Validation restored that scene, restarted Play mode, and restored the camera position and rotation.
Final check: playing true, paused false, scene dirty false.
Camera position: `(-1.29864156, 2.1660924, -3.88)`.
Camera rotation: `(0.104528479, 0, 0, 0.9945219)`.
Restarting Play mode reset the previous animation simulation position. No scene asset was saved.

The Planet run reported two unrelated scatter warnings:

```text
[ScatterCheck] 3 impostor prototype(s) have no baked card and will bake at load (about 0.3 s): Desert Forage Grass, Mountain Forage Grass, Tundra Forage Grass. Fix with Tools > ProceduralPlanets > Impostors > Bake Impostors (Generated Props), from play mode.
[ScatterCheck] 2 scatter material(s) have an albedo tint above 1.05, which is brighter than white and reads as self-lit at night: FoliageMeadowCanopy_Autumn (1.55), FoliageMeadowCanopy_Golden (1.60).
```

No new product exception appeared during normal startup. Expected probe exceptions were caught and recorded separately.
