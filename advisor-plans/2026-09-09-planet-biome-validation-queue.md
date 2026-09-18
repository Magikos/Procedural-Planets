# Planet and biome validation queue — 2026-09-09

Status: VALIDATED BASELINE / DEFECTS REPRODUCED. Bryan handed over Unity; 13 baseline tests passed.
See [validation results and limits](2026-09-09-planet-biome-validation-results.md).
Source review: [planet and biome audit](2026-09-09-planet-biome-audit.md).

This is a documented work queue, not a scheduler or Unity reservation.
Do not interrupt the current Editor owner. Preserve the active scene and Play state.
The baseline fixtures exist. Additional regression cases below are specifications, not implemented tests.

## Operator procedure

1. Obtain the Editor handoff. Record scene, Play state, seed, settings, and camera pose.
2. Recheck source drift against the audit. Other agents are editing this working tree.
3. Import scripts and check the Unity console.
4. Build `ProceduralPlanets.Core.csproj`, then `ProceduralPlanets.Planet.csproj`, serially.
5. Discover current Unity MCP schemas. Run focused EditMode fixtures and collect complete job results.
6. Require a nonzero discovered test count. Record passed, failed, and skipped counts with exact failure output.
7. Run behavioral probes only after the Editor owner has handed off control.
8. Archive results under `local-only/debug-screenshots/baselines/2026-09-09-planet-biome-review/`.
9. Restore the prior scene, Play state, camera, and settings. Do not save changed scene assets without authorization.
10. Update this queue with evidence paths and actual results.

## Queue

| Item | Work | Pass condition | Status |
|---|---|---|---|
| Q1 | Run `ProceduralPlanets.Tests.NoiseFilterEvaluatorGoldenTests`, `ProceduralPlanets.Tests.BiomeMapBakerParityTests`, and `ProceduralPlanets.Tests.WaterBodyCombinedSampleTests` | All discovered baseline tests pass; retain exact failures | PASSED: 13/13 |
| Q2 | G01: inspect all permutation entries for seeds 256 and 65536; compare three-layer ShapeGenerator samples. After an approved fix, test seed distinction, repeated-seed determinism, permutation validity, and legacy compatibility. Schedule real Burst jobs for filter/mask parity. | Current collision reproduced; new version distinguishes these seeds; legacy results remain stable when supported | PROBED: see results; permanent regression and post-fix checks remain |
| Q3 | G02: synthesize lake-mask samples below, midway through, and above the shore handoff. Compare resolver memberships with full bake output. | Full-land samples contribute no LakeShore; intermediate membership follows the accepted ramp | PROBED: see results; permanent regression and post-fix checks remain |
| Q4 | G03: bake adjacent synthetic leaves with constant elevation/climate and a nearby assignment boundary. Compare shared-edge kernels and reverse atlas copy order. | Shared positions and contributions match; reversing copy order leaves atlas bytes unchanged | PROBED: see results; permanent regression and post-fix checks remain |
| Q5 | G04: cancel Low mesh generation while a job is outstanding. Include normal completion and a controlled failure path. | Cancellation stays `OperationCanceledException`; no disposed-allocation errors or native leaks | PROBED: see results; permanent regression and post-fix checks remain |
| Q6 | G05: repeat Low generation and water replacement ten times. Track only owned runtime mesh identities after destruction completes. Include teardown. | Owned mesh count stabilizes after warm-up and all retired meshes become destroyed | PROBED: see results; permanent regression and post-fix checks remain |
| Q7 | G06: generate Low terrain at nonzero translation, nonidentity rotation, and uniform scale. Compare face transforms and sampled/rendered positions. | Every face has identity local transform; terrain follows the planet frame | PROBED: see results; permanent regression and post-fix checks remain |
| Q8 | G07: cancel separately during lake and water computation. Record requested-to-worker-stop duration and publications. | Worker stops at a documented bounded checkpoint; no canceled publication or orphaned worker; successful checksums unchanged | PROBED: see results; permanent regression and post-fix checks remain |
| Q9 | Profile fresh High generation using a fixed seed/settings/Burst state. Separate terrain copy/reduction, climate, map bake, lake, and water substages. Record memory and edit texture counts. | Evidence identifies the dominant remaining cost; do not claim an optimization from one uncontrolled startup sample | PARTIAL: fresh 34,273 ms run; detailed instrumentation remains |
| Q10 | After approved biome fixes, capture lake banks, leaf borders, cube-face seams, and cold/high-altitude transitions at matched poses | Numeric changes are explained; Bryan accepts the visual result | PENDING APPROVED FIXES |

Q2 must use subsystem seeds directly. `planet.generate <seed>` can transform a world seed through `ISeedProvider`.
Q3 and Q4 must exercise the full bake input path. Repeating only the smoothing parity test does not cover either defect.
For Q8, choose the checkpoint interval before implementation and record its measured worst-case duration.
Do not cancel only the polling await while leaving worker ownership unresolved.

For capture comparisons, record `planet.seed` and `quality.get`, and save a camera teleport.
Archive the baseline before editing. Capture with the same seed, quality, pose, and selected diagnostic set afterward.
Build success does not certify biome appearance, resource cleanup, cancellation, or performance.

