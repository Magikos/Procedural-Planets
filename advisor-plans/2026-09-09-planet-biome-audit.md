# Planet and biome generation review

**Findings only — no code changed.**

Implementation update: the seven confirmed defects are now fixed. Read [fixes and validation](2026-09-09-planet-biome-fix-results.md).

Update: Unity validation now completed. Read [results and limits](2026-09-09-planet-biome-validation-results.md).
The original source-review state below is preserved as history. The new results supersede its pending-test statements.

Reviewed the dirty working tree on `harvest-vertical-slice`, based on `d1e0f62`, on 2026-09-09.
Other agents have active changes. This report describes inspected source, not a clean commit or a runtime certification.
Unity remains with its current owner. No build, Unity test, scene operation, or capture ran.
The [validation queue](2026-09-09-planet-biome-validation-queue.md) records the required checks.

Scope: shape/noise generation, Low terrain generation, chunk mesh generation, biome lookup/baking/atlas assembly,
and generation-time water ownership and cancellation. This was a focused review, not an exhaustive project audit.
Weather simulation, creature systems, scatter internals, shader appearance, package currency, and saved-world loading internals were not re-audited.

The review found seven defects and retained four performance opportunities from the earlier startup audit.
Visible severity and runtime resource growth remain unmeasured. The seed collision and atlas coordinate mismatch have direct arithmetic evidence.

## What came back clean

- Terrain already uses Burst jobs. A new terrain job framework is unnecessary.
- Managed and Burst noise share `NoiseData`; their simple and rigid filter formulas match on inspection.
- Shape generation publishes elevation extrema after mesh generation, rather than exposing a partial range.
- The biome baker now uses a sliding histogram. The earlier full-window performance finding is resolved at source level.
- Existing `BiomeMapBakerParityTests` cover smoothing bytes and worker isolation. They do not cover resolver inputs or atlas alignment.
- Water generation checks cancellation before uploading its completed mesh. The remaining defect concerns computation and cancellation latency.
- The recent planet timing output separates lake, colors, and finalization. The old combined-color timing complaint is partly resolved.

`graphify query "planet generation biome generation terrain noise lifecycle"` returned no visible navigation output.
Findings therefore rely on live source inspection. No graph generation or model-backed graph extraction ran; no graph token cost was reported.

# Findings

## G01 — Noise collapses the full seed to eight bits

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Noise.cs:35` creates the permutation. Lines 41–51 XOR its four seed bytes into each source entry.
  `Assets/Scripts/Planet/ShapeGenerator.cs:41` and `:113` use `seed + layerIndex`.
  `Assets/Scripts/Planet/PlanetDto.cs:50` builds three fixed-shape noise layers from settings.
- **Trigger:** Initialize shapes with subsystem seeds `256` and `65536`, using identical settings.
- **Impact:** Both shapes receive identical noise tables for their normal layer counts and therefore identical terrain.
  There are only 256 possible tables per noise filter. This does not mean every complete world has only 256 variants.
  Other world systems have their own seed handling.
- **Proof:** A source-equivalent PowerShell enumeration found 256 distinct XOR masks among seeds 0–65535.
  Seeds 256 and 65536 both produced masks `1, 0, 3` for the first three layers.
- **Effort / fix risk / confidence:** M including compatibility; HIGH; HIGH.
- **Recommendation:** Use a deterministic permutation shuffle that consumes the full seed.
  Select an explicit generation-version policy before changing existing worlds. Preserve a legacy path where required.
- **Refactor option:** None. Keep one shared noise implementation.
- **Behavior note:** Changes generated terrain and climate noise. Do not silently regenerate saved terrain under the new algorithm.
- **Validation:** Q2 in the queue. Distinguish subsystem seeds from world seeds transformed by `ISeedProvider`.

## G02 — The terrain bake discards the lake-shore height handoff

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Biomes/BiomeLookupData.cs:109` returns LakeShore plus land membership.
  `:166` retains LakeShore as primary even at full secondary membership. `:252` computes the shore handoff.
  `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs:133` discards the secondary ID and its weight; `:153` stores only primary.
- **Trigger:** A dry lake-mask sample lies above `waterLevel + LakeShoreBlendHeight`.
- **Impact:** The resolver specifies pure land, but the bake still contributes LakeShore.
  The intended height ramp cannot govern the baked bank. Spatial smoothing follows the coarse mask instead.
- **Effort / fix risk / confidence:** M; MED; HIGH for the data loss. Visible magnitude remains unmeasured.
- **Recommendation:** Preserve lake-shore membership through the existing smoothing accumulation.
  Test full-land and intermediate membership. Selecting only the dominant ID would still lose the continuous ramp.
  Keep unrelated biome assignment and top-K tie rules stable.
- **Refactor option:** None.
- **Behavior note:** Changes lake-bank biome weights and appearance to follow the existing ramp.
- **Validation:** Q3. The current smoothing parity suite cannot detect this input-stage defect.

## G03 — Neighboring biome maps disagree about their shared sample position

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Biomes/BiomeMapBaker.cs:112` and `:115` divide sample coordinates by 127.
  Lines 175 and 180 center output kernels at `2 * texel + 1`.
  `Assets/Scripts/Planet/Surface/BiomeAtlasService.cs:141` uses a 63-sample stride for 64-sample maps.
  `:611` copies all samples, including the overlapping endpoints.
- **Trigger:** A biome boundary crosses the shared edge of two leaf chunks.
- **Impact:** The left map's final sample centers at its boundary. The right map's first sample centers 1/127 leaf widths beyond it.
  Both write the same atlas pixel. Copy order can select different weights, including during regional updates.
- **Proof:** Expressed in left-leaf coordinates, the two centers are `1` and `1.00787401574803`.
- **Effort / fix risk / confidence:** M; MED; HIGH for the coordinate mismatch. Visible magnitude remains unmeasured.
- **Recommendation:** Give bake sampling and atlas stitching one endpoint convention.
  Corresponding edge kernels must sample the same positions. Test both atlas copy orders.
- **Refactor option:** A shared coordinate helper only if it replaces both calculations.
- **Behavior note:** Changes biome sample positions and some atlas bytes.
- **Validation:** Q4. Isolate this from the already documented clamping of out-of-leaf climate/elevation samples.

## G04 — Low-mode cancellation disposes its shared filters twice

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs:71` handles cancellation while jobs run.
  It disposes `filters` at line 76, then throws. The catch block unconditionally disposes `filters` again at line 88.
- **Trigger:** Cancellation arrives while the combined mesh job remains incomplete.
- **Impact:** Cleanup can replace the cancellation exception with a disposed-allocation error.
  The exception path also disposes job state without consistently completing each scheduled handle first.
- **Effort / fix risk / confidence:** S; LOW; HIGH for duplicate disposal. Exact runtime failure output is pending.
- **Recommendation:** Use one cleanup owner. Complete all scheduled handles, then dispose each allocation once in `finally`.
  Keep ownership through result copying so a copy failure also releases native storage.
- **Refactor option:** None. Match the explicit job ownership pattern in `ChunkSurfaceGenerator`.
- **Behavior note:** Preserves successful output; makes cancellation and failure cleanup reliable.
- **Validation:** Q5. Require cancellation to remain `OperationCanceledException`, with no native allocation leak.

## G05 — Runtime terrain and water meshes have no destruction owner

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs:179` allocates six runtime meshes.
  Its `Dispose` at line 149 performs no cleanup.
  `Assets/Scripts/Planet/PlanetWaterSurface.cs:129` allocates a water mesh; `:88` drops the object reference during regeneration.
  Its `Dispose` at line 346 destroys material and textures, but not the mesh.
  `Assets/Scripts/Planet/Planet.cs:471` destroys child GameObjects during regeneration.
- **Trigger:** Repeated regeneration, or teardown after creating these meshes.
- **Impact:** Destroying the containing GameObject does not supply explicit ownership cleanup for separately allocated runtime meshes.
  Low terrain and water can accumulate mesh allocations between broader unused-resource sweeps.
- **Effort / fix risk / confidence:** S; LOW; HIGH for missing ownership cleanup. Growth magnitude needs a runtime count.
- **Recommendation:** Keep references to owned meshes and destroy them during replacement and teardown.
  Retain those references before child objects disappear. Never destroy an imported/shared asset mesh.
- **Refactor option:** None. Implement symmetric cleanup in the two existing owners.
- **Behavior note:** Preserving.
- **Validation:** Q6. Track generated mesh identities, not every mesh created by unrelated agents or scene systems.

## G06 — Low-resolution faces remain at the world origin when the planet moves

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/Surface/PerFaceSurfaceProvider.cs:174` creates a world-origin GameObject.
  Line 175 assigns its parent without resetting the local transform.
  `Assets/Scripts/Planet/TerrainFace.cs:229` generates vertices in planet-local coordinates.
- **Trigger:** Generate a Low-resolution planet whose transform has a nonzero position or nonidentity rotation/scale.
- **Impact:** Parenting preserves the child's world transform. The face mesh does not inherit the intended planet frame.
  Terrain rendering disagrees with planet-relative sampling and shader globals.
- **Effort / fix risk / confidence:** S; LOW; HIGH. The origin/identity default hides the defect.
- **Recommendation:** Parent generated faces with `SetParent(_planetTransform, false)` and retain an identity local transform.
- **Refactor option:** None.
- **Behavior note:** Preserves origin/identity output; corrects transformed Low planets.
- **Validation:** Q7. Test translation, rotation, and uniform scale separately.

## G07 — Heavy water and lake generation do not observe cancellation promptly

- **Category / severity:** Bug / Medium.
- **Evidence:** `Assets/Scripts/Planet/PlanetWaterSurface.cs:175` starts the worker without a token.
  Its polling loop at line 179 waits without cancellation; line 185 checks only after completion.
  `Assets/Scripts/Planet/WaterMeshBuilder.cs:146` accepts no token.
  `Assets/Scripts/Planet/Planet.cs:533` also builds the lake map without a token.
  After returning to the main thread, it publishes the result without first checking cancellation.
- **Trigger:** Cancel generation during lake-map or water-mesh computation.
- **Impact:** Heavy work continues after cancellation. Lake data can still publish before a later stage rejects cancellation.
  The water mesh itself has a post-compute cancellation guard before upload.
- **Effort / fix risk / confidence:** M; MED; HIGH. Current latency requires measurement.
- **Recommendation:** Pass the token through bounded row/face/component work and check before publishing.
  Keep ownership of worker completion and errors; canceling only the polling await can orphan ongoing work.
  Return to the main thread on failure paths that require Unity cleanup.
- **Refactor option:** None.
- **Behavior note:** Preserves successful generation; changes cancellation latency and prevents canceled publication.
- **Validation:** Q8. This carries forward startup-audit R1 and consolidated-audit F06.

# Improvement opportunities

These are source-confirmed work volumes, not measured speedup claims for this tree.

| ID | Opportunity and current evidence | Effort | Fix risk | Validation |
|---|---|---|---|---|
| P1 remaining | `ChunkedSurfaceProvider.cs:1744` still computes managed climate for every chunk vertex. The expensive biome resolve is gone. Measure before considering a shared Burst climate snapshot. | M–L | MED | Q9 climate evaluation versus allocation time; same-seed climate and atlas checks |
| P3 retained | `ChunkedSurfaceProvider.cs:176` allocates edit resources for all chunks; `PlanetChunk.cs:292` creates per-chunk path textures and arrays. Consider lazy leaf resources plus shared blank data. | M | MED | Q9 live texture counts, retained memory, saved-edit replay, grass mask bindings |
| P4 retained | `ChunkSurfaceGenerator.cs:178` records extrema per vertex after native-to-managed copying. Measure and consider a per-chunk reduction. Keep global pre-generation readiness unchanged. | M | MED | Q9 job wait versus copy/reduction time; identical extrema and surface samples |
| P5 retained | `WaterMeshBuilder.cs:146` still drives a managed global-data and mesh pipeline. Instrument its substages before replacing graph structures or adding jobs. | M for measurement; L for rewrite | MED | Q9 seam indexing, climate, traversal, mesh creation, upload timings and water counts |

These recommendations preserve behavior in principle. Each needs the indicated evidence before accepting that claim.
Do not lower resolution or smoothing radius to obtain a performance result.

# Proposed repair order

1. Capture the existing test baseline after Editor handoff.
2. Fix G04–G06 as separate ownership/transform changes. Validate cancellation, mesh counts, and transformed Low terrain.
3. Fix G07 with explicit worker completion and publication ownership.
4. Fix G02 and G03 separately, with synthetic regressions and matched lake-bank/boundary captures.
5. Decide G01's saved-world policy before changing seeding. Test both legacy and new generation if both remain supported.
6. Measure P1/P3/P4/P5 before selecting the next optimization.

Use existing owners and NUnit fixtures. No new test framework, generation framework, or broad class split is justified by this review.

# Prior audit reconciliation

The detailed source is [the August startup audit](../docs/audit/2026-08-11-startup-planet-generation-audit.md).
Old timings are historical context, not current acceptance evidence.

| Prior item | Status | Current evidence / remaining work |
|---|---|---|
| Startup P1 | PARTIAL | `ChunkedSurfaceProvider.cs:1436` selects climate-only vertex work. Full biome lookup is removed; managed climate arrays/evaluation remain at `:1732`. |
| Startup P2 | RESOLVED | `BiomeMapBaker.cs:184` slides the histogram. Existing parity tests compare output bytes. No tests reran during this review. |
| Startup P3 | OPEN | All-chunk edit texture allocation remains at `ChunkedSurfaceProvider.cs:176`. |
| Startup P4 | OPEN | Batch copy, bounds/radius work, and per-vertex extrema updates remain in `ChunkSurfaceGenerator`. |
| Startup P5 | OPEN | Managed water computation remains. The old duration is not a current benchmark. |
| Startup P6 | PARTIAL | `Planet.cs:641` now reports lake/colors/finalize separately. Terrain and water internal timing remain insufficient to rank retained opportunities. |
| Startup R1 / July F06 | OPEN | G07 revalidates uncancellable water computation and adds the lake publication boundary. |
| July F11 | OPEN, outside repair scope | `ChunkedSurfaceProvider` still owns edit/paint logic alongside generation. This review does not justify or specify a broad split. |
| Current audit CPU/GPU noise duplication | SUPERSEDED for active terrain generation | `Planet.cs:451` selects Low/High CPU/Burst providers; no GPU terrain branch is selected. Managed/Burst noise share `NoiseData`. Dormant GPU compute parity is not certified here. |

The July audit's unrelated weather, console, infrastructure, and scatter findings were not revalidated.
No older audit files were deleted or consolidated.

## Considered but not promoted

- Clamped biome padding is already documented in `docs/design/2026-08-09-biome-blend-seamlessness.md`.
  It is distinct from G03's endpoint mismatch.
- Chunk edge normal differences need a controlled visual/numeric check before assigning impact.
- Existing golden noise tests invoke the evaluator directly; their method name does not prove execution through a scheduled Burst job.
  Q2 adds that verification requirement without declaring the formulas incorrect.

# Decisions before implementation

Select `fix`, `defer`, or `wontfix` per finding. G01 also needs an existing-world compatibility policy.
This review and the documented test queue are complete. Runtime verification remains pending Editor handoff.


