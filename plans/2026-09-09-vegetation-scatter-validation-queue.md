# Vegetation scatter validation queue — 2026-09-09

**Status:** QUEUED as written backlog. No scheduled execution or automatic Unity access.
Another agent owns Unity. This review did not access the Editor, import assets, build, or run tests.
Source review: [Vegetation scatter review](2026-09-09-vegetation-scatter-review.md).
Baseline: `harvest-vertical-slice`, `d1e0f624ead0448f70a867f20a9389e367527293`, plus existing uncommitted work.

## Handoff and baseline

1. Confirm the current owner has released Unity before any Editor action.
2. Record the current branch, HEAD, relevant dirty files, Unity version, open scene, and Play state.
3. Recheck current test names and command signatures. Concurrent edits can change them.
4. Record the active generated or source plant route and effective lily and woodland DTO values.
5. Save camera poses, planet/world seeds, quality tier, weather, time, and relevant overrides before changing the scene.
6. Preserve existing saves and harvested state. Use a separate test world when regeneration is required.
7. Archive captures and sidecars under `local-only/debug-screenshots/baselines/2026-09-09-vegetation-scatter/`.

Exact scene, seeds, poses, quality, and environment remain UNKNOWN until handoff.
Select one populated shallow lake bank, one deep lake region, and one forest with ferns or mushrooms.
Wait for planet generation and scatter tile loading to complete before recording the baseline.

## Existing baseline checks

Run these fixtures serially through the existing Unity test runner after clean compilation:

- `ProceduralPlanets.Tests.ScatterGatherParityTests`
- `ScatterPlacementMathTests`
- `ScatterLibraryDtoValidationTests`
- `ScatterQuadtreeParentTileTests`
- `ScatterHashTests`
- `ScatterIdTests`
- `ScatterDrawBucketsTests`

Resolve full names from the current test inventory before submission. These are existing fixtures, not new tests.
Record the test job ID, pass/fail/skip counts, and full failures. Do not classify unexecuted or inconclusive results as passes.

At each suitable populated viewpoint, use the current existing commands:

```text
scatter.verify 60
scatter.tilecheck 60
scatter.density 60
```

The commands are diagnostics, not proof of habitat quality. Preserve any known corner-gap qualification in their output.
Capture the current appearance before any approved visual change.

## Queued regression and visual checks

New regression cases below are proposals. Add them only with the associated approved implementation.

| ID | Check | Acceptance condition | Status |
|---|---|---|---|
| Q01 | Existing fixture baseline | All selected tests pass after successful compilation; record full results | QUEUED |
| Q02 / V01 | Dry, shallow, and deep candidate eligibility | Dry candidates reject. Candidates inside the authored depth band can pass. Deeper candidates reject | PROPOSED |
| Q03 / V01 | Raised lake and scale | Local basin levels determine depth. Accepted transforms follow the correct water surface at supported planet scales | PROPOSED |
| Q04 / V01 | Coarse biome edge | A dry candidate rejects even when its coarse biome sample is Lake | PROPOSED |
| Q05 / V03 | Shared woodland field | Changing species colony scale leaves the shared woodland sample unchanged at a fixed biome and position | PROPOSED |
| Q06 / V03 | Actual authored species | Evaluate the current 220/60/18 metre tree, fern, and mushroom settings; measure shade-plant correlation with tree stands | QUEUED baseline; proposed post-fix check |
| Q07 / V04 | Terrain bias | At fixed position, increasing flatness never increases neutral woodland keep; include at least one nonsaturated strict comparison | PROPOSED |
| Q08 / V05 | Active managed/Burst parity | With clumping, shade, floating plants, and basin levels enabled, ID sets match exactly; transforms match existing fixture tolerances | PROPOSED |
| Q09 | Determinism and boundaries | Reverse traversal, revisit, and tile-union checks preserve IDs; inspect shorelines and tile boundaries for visible seams | QUEUED |
| Q10 / V02 | Lily colony appearance | Matched captures show multi-pad colonies near eligible shallows and open water between colonies; Bryan reviews the result | QUEUED baseline; pending visual change |
| Q11 / V02 | Colony statistics and cost | Record pad count, eligible area, nearest-neighbor distances, candidates, gather duration, and cache limits before and after | QUEUED baseline |

For Q06, record the baseline correlation before setting a post-fix quantitative target. Do not claim a measured improvement from code inspection.
For Q10, choose numerical depth, density, and colony targets before tuning. They are currently unspecified, not implicitly approved defaults.
For Q11, compare the same region and setup. Denser candidates predict increased gather work; no performance improvement is expected without measurement.
Measure both normal travel and stationary views. Record the timing method and sample count before comparing results.

## After an approved implementation

Build `ProceduralPlanets.Core.csproj`, then `ProceduralPlanets.Planet.csproj`, serially, using current project build instructions.
Refresh Unity only after handoff. Do not run stale assemblies after compilation fails.
Run the selected regressions, restore the recorded setup, and capture matched views.
Check saved identity assumptions if spacing changes the candidate grid level.
Record Bryan's visual decision separately from automated results.
Restore the handed-off Editor state and settings without saving unrelated scene changes.

## Results

No executed results. Baseline setup, numeric measurements, captures, and visual acceptance remain pending.

### Existing-test baseline — 2026-09-12

This entry supersedes the unexecuted status for Q01 only. Runtime views and proposed habitat regressions remain queued.
Bryan requested Unity sharing with Interaction System. That task explicitly released the Editor for this bounded test window.

- Unity version: `6000.7.0a5`.
- Test job: `16a3288e2cc84debbc9ccd9f236d701f`.
- Result: 63 passed, zero failed, zero skipped; test duration 1.8846724 seconds.
- Scope: the seven existing fixtures listed above, including managed/Burst gather parity.
- Full results: `local-only/validation/2026-09-12-scatter-baseline.json`.

Before and after the tests, Unity had one clean scene open: `Assets/Scenes/Tests/SidekickInteractionReview.unity`.
Unity remained in Edit Mode. No compilation, test, or Play transition was pending at release.
No scene restoration was required. No source or asset changes, imports, or new fixture activation occurred.
Meadow, woodland, lake, and plains captures were not attempted during this bounded window.
Existing parity coverage does not prove clumping, age transitions, or aquatic habitat correctness.

A redundant read-only execute_code state probe returned `success=false` with no message or data.
The supported scene and Editor-state tools independently verified the state before and after the successful test run.
The pre-run console contained an existing test-result-save entry, not a reported C# compilation failure.

### Current-state captures — 2026-09-12

A subsequent explicit Interaction handoff allowed four baseline captures: grassland, forest, lake bank, and steppe.
See [capture notes](2026-09-12-vegetation-vista-baseline.md) for artifacts, observations, warnings, and limitations.
These captures supersede the pending baseline-image status only. Habitat implementation and visual acceptance remain pending.
Scatter restored the clean interaction scene in Edit Mode and explicitly released Unity for the wheel pass.

### Authorized implementation — 2026-09-12

The habitat changes are implemented. The final focused suite passed 76 tests with no failures or skips.
See [implementation evidence](2026-09-12-vegetation-habitat-implementation.md) for runtime measurements, captures, compatibility changes, and limitations.
Interaction accepted Unity after restoration to the clean interaction scene in Edit Mode.
Visual approval remains with Bryan.

### Floating objects regression — 2026-09-17

Bryan supplied `codex-clipboard-c0905480-0fec-4739-904d-da359939cd06.png` showing plants and a rock above terrain.
The cause remains unconfirmed. No scatter source changes have been made for this report.

Interaction released Unity. Shadows currently owns Unity and reported an unresponsive Editor during a shadow capture.
Scatter must wait for an explicit handoff before Editor operations. Preserve the active scene and camera at handoff.

Queued checks:

1. Record the seed, camera pose, terrain quality, and visible prototype identities.
2. Run `local-only/validation/scatter-floating-height-probe.cs.txt` through the existing Editor code tool.
3. Compare cached scatter pivots, current analytic heights, and radial hits against visible terrain triangles.
4. Compare mesh and impostor rendering at the same pose if pivot heights are correct.
5. Fix the proven owning stage. Check managed/Burst parity and water placement if placement changes.
6. Capture the same view after the fix and release Unity explicitly.

Static finding: `Planet` supplies `AnalyticGroundSampler` to scatter, while terrain renders sampled mesh triangles.
Both terrain generation and Burst scatter call the shared noise evaluator and river carving code.
This establishes a possible surface approximation discrepancy, not proof of the screenshot's cause.
The September 12 test results do not validate this new report.

Recovery update: Shadows released Unity. ProceduralPlanets was absent from the process inventory; the other running Editor belonged to Explore Assets.
Scatter reopened the project with the currently pinned Unity `6000.7.0a6`.
Startup stopped at `Scene Backup Detected`, offering `Keep Backups` and `Delete`.
Computer-use attempts to keep backups failed with `computer-use request timed out: click_element` and `foreground window did not report a process id`.
No backup deletion, source edit, or runtime measurement occurred. The prompt needs user interaction before validation can continue.
