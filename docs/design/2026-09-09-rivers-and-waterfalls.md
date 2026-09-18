# Rivers and waterfalls implementation

Date: 2026-09-09. Status: implemented in the working tree; visual approval remains with Bryan.

## Behavior

Planet generation now solves drainage before building terrain meshes.
The ocean-seeded priority flood retains downstream receivers, deterministic settlement order, and original terrain elevations.
Contributing surface area selects river segments. Tributaries inherit their downstream network identity.
The existing lake solve supplies receiving water levels.
Ordinary bends use sampled quadratic curves before terrain carving.
Headwaters taper from a narrower source. Endpoint widths match across connected segments.
Quiet reaches and waterfall receiving runs can widen into deeper river pools.
These pools retain the downhill profile; they are not separate lake simulation objects.

`RiverField` owns an indexed, immutable channel dataset for the generated world.
Managed terrain, Burst terrain, Burst scatter, water queries, and GPU water occupancy consume that dataset.
Channel profiles lower the bed and blend into banks. Intersecting channels combine their cuts through a minimum operation.
The minimum operation prevents tributaries from adding duplicate depth at a junction.

`RiverRenderer` builds spatially grouped water meshes with downstream texture coordinates.
The shader moves normal detail and foam downstream.
Steep drops receive separate falling sheets, receiving channels, and impact foam.
Waterfall effects use 12 pooled groups, each containing splash, mist, and lip spray.
Each group caps splash and lip spray at 128 particles each, and mist at 96 particles.
The pool activates only near the observer, within 250 world metres.

`WaterSurfaceRegistry` replaces the prepass's single named-object lookup.
The existing ocean/lake mesh and ordinary river meshes participate in the nearest-water interface pass.
Falling sheets do not participate in that volume pass or the gameplay water-volume query.
The packed interface uses kind 2 for rivers and preserves the existing shoreline precision.
The river vertex marker bypasses ocean swell displacement in the prepass.

`WaterSample.Velocity` exposes the downstream current in world-space metres per second.
Existing still-water constructors retain a zero-current default.
This change does not add current-driven character movement or a fluid simulation.

## Controls

| Command | Result |
|---|---|
| `river.status` | Prints generated segment and waterfall counts. |
| `river.visit 0` | Moves the camera to a generated segment. Change the index to inspect another segment. |
| `river.visit 0 true` | Moves the camera downstream of a generated waterfall. |
| `water.list River` | Lists river generation settings. |
| `water.set RiverHalfWidth 8` | Changes the runtime width setting. Run `planet.generate` afterwards. |
| `water.set RiverCatchmentFraction 0.0006` | Raises the required catchment area, reducing stream density. Regenerate afterwards. |
| `water.set WaterfallMinDrop 12` | Changes the minimum drop. Regenerate afterwards. |

The WaterSettings asset also exposes `RiversEnabled`, `RiverDepth`, and the settings listed above.
Runtime consumers read their WaterDto snapshot.
Distance settings use the generator's 5,000-metre radius reference.
The commands remain development-only and reject invalid input through `ConsoleCommandResult.Fail`.

With rivers present, `planet.rebuild-water` performs a full planet regeneration.
A water-only re-solve could otherwise detach river outlets from their carved terrain.
The old fast path remains available when the generated world has no rivers.

## Validation

Unity version: `6000.7.0a5`. Planet seed: `1691104419`.

- Core build: passed.
- Planet build: passed, with 19 warnings in the current shared tree.
- EditMode assembly build: passed, with 2 warnings in the current shared tree.
- Combined Unity job `dc9948b0d29a49558c7e73b1e108fd04`: 92 completed, one reported failure.
- Final river/terrain/water job `f63987892d4540d384d7601ba821a2da`: 46 passed, zero failed, zero skipped, after the channel-step criterion change.
- RiverTests, NoiseFilterEvaluatorGoldenTests, ScatterGatherParityTests, WaterBodyCombinedSampleTests, WaterPresentationTests, and ChunkSurfaceRaycastTests reported no failures.
- ConsoleRegressionTests.PastePreservesTabsAndRejectsMultilineInput failed with `Expected: "echo alpha"` and `But was: <string.Empty>`.
- The clipboard test also failed in an isolated retry. Its cause was not established; the combined suite is not green.
- Before the final additions, job `358e819c9e374ed9831d2c60c8197f5a` passed all 77 focused tests.
- Unity accepted the River shader without shader errors.
- Clean planet generations completed with the new pipeline.
- The supported-drop version generated 1,818 segments and 61 waterfall sections on this seed.
- Curve refinement generated 4,626 segments, including 61 waterfall sections and 61 widened receiving pools.
- Final corrected-terrain generation produced 5,535 segments, 13 waterfalls, and 13 widened receiving pools.
- The final vertex audit checked 158,070 ordinary water vertices: zero outside the field, maximum height error 0.003028 metres.
- The largest final waterfall drops 22.278 metres. Its queried lip and pool heights matched their planned levels exactly.
- An interior-vertex audit checked 58,737 points. Maximum mesh/query surface-height difference was 0.001075 metres.
- Runtime inspection confirmed 36 waterfall particle systems in 12 groups. A waterfall view had 181 live particles.
- Graphify updated successfully; the latest log records its graph counts.

The vertex audit excludes the outer bank band. It does not prove every rasterized pixel matches an analytic query.
The first audit also exposed edge points outside the strict wetness test through floating-point rounding.
The final C# and GPU occupancy tests share a five-millimetre edge tolerance.
The follow-up audit found all 93,868 ordinary river vertices inside the shared field before bend refinement.
Their maximum radius difference was 0.150879 metres, including the intentional 0.15-metre rendering offset.

Logs and captures live under `local-only/river-validation/`.
Build logs are `core-build.log`, `planet-build.log`, and `tests-build.log`.
The graph update log is `graphify-update.log`.
Baseline F10 captures were copied into `before/` before further capture pruning.
The final captures use corrected terrain settings. They are not a controlled same-recipe before/after comparison.

Final captures:
- [Waterfall and particles](../../local-only/river-validation/waterfall-final-corrected.png).
- [Curved channel and receiving water](../../local-only/river-validation/river-channel-final.png).
- [Corrected-terrain river network](../../local-only/river-validation/river-curves-corrected-terrain.png).

Use `camera.teleport RiverWaterfallFinal` or `camera.teleport RiverChannelFinal` to revisit the captured locations on this recipe.
The waterfall sheet still looks geometric. Terrain intersection and transition appearance need further visual refinement.
The final DX12 rain compute import reports register-pressure warning X4714. Performance has not been isolated or approved.
The last shader-only import removed a loop-variable warning. Subsequent Unity MCP code calls returned failure with no message.
The separate mountain silhouette capture and camera restoration were not completed. Unity ownership passed to the terrain task.
After handoff, the terrain task reported that MCP recovered and the camera was restored.
Its `local-only/mountain-scale-probe/mountain-close.png` capture shows rocky ridges beneath clouds and vegetation on gentler slopes.
That follow-up required no source or scene edits.

## Corrections made during validation

The first generation produced 4,342 segments and 914 waterfall sections.
Its capture exposed suspended platforms at river lips and flat junction caps.
The initial tests passed because they checked drainage and sampling, not those pixels.

The corrected generator excludes unresolved depressions that the lake policy intentionally drained.
It propagates that exclusion upstream instead of drawing discarded spill planes as river surfaces.
The initial geometry correction used a drop-to-run ratio above 1.2, in addition to the minimum drop.
After terrain amplitude correction, the river generator uses 0.35 and concentrates qualifying descents into carved steps.
This produces local waterfall relief without raising the base mountains.
Their short lip follows the incoming grade.
Junction vertices sample the shared water profile; ordinary two-segment joints no longer receive flat circular caps.

The branching capture exposed square drainage-grid turns.
Generation now rounds ordinary two-edge bends before building the shared field.
Six curve segments replace each corner. Junctions and waterfall endpoints remain fixed.
The added test checks connected endpoints, descending heights, and field coverage.
Width-profile tests cover narrow starts, wide ends, pool centres, and matching carved/wet footprints.
The reference-driven shader pass uses opaque scene depth for shallow-bed visibility and shoreline blending.
The curved-profile audit exposed discontinuities when nearest-segment selection changed at overlapping bends.
CPU and GPU queries now blend nearby surface heights with continuous bank-distance weights.
The overlap regression test checks continuity across the selection boundary.
Visible river edges sit up to six centimetres inside their analytic footprint to absorb join-tangent projection differences.
Blending stays within connected reaches and fades out at waterfall endpoints.
Clipped receiving-pool footprints prevent downstream carving from cutting beneath the upstream lip.
The waterfall regression test checks separate lip/pool levels and supporting bed height.

The terrain agent corrected excessive mountain amplitude before the final capture run.
River code preserves that shared evaluator and adds no mountain amplification.
The corrected generation measured 288.126 metres of maximum relief on the 5,000-metre planet.
Its outer terrain radius was 5,288.126 metres; the atmosphere radius was 6,081.345 metres.

The initial test build failed with `error CS0103: The name 'Array' does not exist in the current context`.
Qualifying `System.Array` fixed that fixture error. Subsequent builds and the final tests passed.

Hot Reload produced transient missing-type and patching errors during structural edits.
Validation used explicit imports and fresh play-mode runs after those edits.
No commit was made.

## Limits and follow-up coverage

This is a generated drainage and presentation system, not a rainfall-driven fluid simulation.
Downstream deltas, braided rivers, erosion, live floods, physical current forces, and river-specific fish spawning are not implemented.
Rivers use liquid-water presentation; seasonal river freezing and waterfall audio remain follow-up work.

The current routing retains the coarse drainage grid's direction choices, with rounded ordinary bends.
Junction shapes and straight waterfall edges still need appearance review.
Unsupported small-basin routes are omitted rather than forcing elevated channels through them.
That choice reduces river coverage in some catchments.

Both active CPU terrain backends consume the channel field.
The unused `GpuPlanetTerrain.compute` prototype has no active C# caller and was not activated or modified.
Extended cancellation stress, multiple seeds, every quality tier, low-resolution terrain appearance, and cross-platform deterministic generation remain unverified.
The interior-vertex audit is not a triangle-interior, shoreline, or full swimming acceptance test.
Performance figures from intermediate runs are observations, not an isolated benchmark.

Bryan should review the final appearance before the plan is marked visually complete.
