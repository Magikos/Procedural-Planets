# Mountain snow and terrain integration — 2026-09-10

Status: implemented candidate; final Unity tests and captures in progress.

Bryan authorized snow changes alongside Rivers and Grass. Procedural Generation owns snow and altitude climate settings. Grass owns G01 property interpolation. Rivers owns its corridor experiment and will preserve the base mountain recipe.

## Cause and correction

Snow incorrectly consumed the rock mask, which reaches full coverage at 48 degrees. The active biome asset also disabled altitude cooling with AltitudeTemperatureDrop = 0.

Terrain climate snow and accumulated weather snow now use the same SnowSlopeRetention helper. Separate material settings retain slope coverage through 55 degrees and fade it to zero at 80 degrees. Temperature still controls climate snow; accumulated weather snow still requires surface weather snow and retains its existing upward-facing factor. Grass and rock thresholds remain unchanged.

The active biome asset and new-asset default use the existing Earthlike altitude coefficient, 2.5. TemperatureProvider already subtracts elevation above the biome ocean threshold times this coefficient. This uses normalized planet elevation, so it scales with planet size. At 250 metres above a 5,000-metre radius, cooling is 0.125 normalized temperature. This is stylized climate scaling, not a physical metres-to-Celsius lapse rate. It does not force all summits to freeze. No new moisture/snowfall simulation was added.

## Checks

Acceptance: climate cools elevated land, warm high terrain stays warm, below-sea terrain receives no altitude cooling. Cold rock slopes retain snow independently of grass cutoff, and near-vertical faces shed it. Both climate and weather snow share the same retention function.

Core build passed with zero warnings/errors. Planet build passed with 19 warnings and zero errors. Initial shader import returned no messages; compilation then refreshed the new climate tests. Logs and captures are under local-only/snow-review.

Before capture: before.png and camera-review.json. Camera original pose: camera-original.json. Baseline latitude-selected mountain view is vegetated. Final captures must use the saved pose. Weather is not frozen, so capture comparisons cannot establish pixel invariance. Grass G01 is a concurrent visual change and must be identified in comparisons.

## Validation update

Unity job 53cebc11dcee4a2fb4ccfd39aea95e4f passed 18/18: ClimateAltitudeTests, NoiseFilterEvaluatorGoldenTests, ScatterGatherParityTests. New climate cases cover cold uplands, warm uplands, and below-sea behavior.

Grass G01 landed concurrently. Its four standalone numerical tests passed, according to Grass. Both GrassNearFieldPlace.compute and BiomeGrassPlace.compute imported without messages. Terrain shader compilation reported no errors but two warnings: `use of potentially uninitialized variable (WeatherCloudConvectivity)` and `use of potentially uninitialized variable (EvaluateGrassOverlay)`. Grass is checking the latter's origin. Initial no-message inspection preceded final variant compilation and is not a claim of warning-free final compilation.

Graphify refresh completed. Final generation/capture review remains in progress. Rivers has made no product source changes during this window; its bounded erosion prototype remains outside Assets pending a real catchment experiment.

Capture follow-up: initial cyan terrain was transient and cleared once ShaderUtil.anythingCompiling became false. GPU readback of snow array slice3 confirms white snow texture. The settled capture exposed a separate layering issue: the grass overlay repainted climate snow on gentle slopes. ApplyGrassSurfaceAlbedo now receives max(rockMask, snowMask), preserving both exposed rock and snow. G01 interpolation remains intact. Weather snow still applies afterward.

Grass inspected the warning at EvaluateGrassOverlay: its initialized helper and early return precede G01. No uninitialized G01 path was found. Baseline warning provenance remains unproven; no unrelated warning cleanup was applied.

## Final result and handoff

Fresh generated world reports AltitudeTemperatureDrop = 2.5. At the selected upland direction, elevation0.02947871 gives temperature0.2702021, below the full-snow threshold0.28. after-final.png visibly shows retained snow after the overlay correction. This is an upland spot check, not full six-face slope/climate validation. The final capture uses local noon with time frozen for visibility; the baseline used running time, so lighting is not controlled between them. Source weather and snow colors were not retuned. Camera and previous time/freeze state were restored successfully.

Final terrain shader error query returned an empty array. The two warning messages above remain recorded. One in-memory diagnostic failed because SettingsProvider.Get is not generic; it was corrected to SettingsProvider.GetSettings<BiomeDto> and succeeded. No product compilation failed.

Unity handed to Rivers with the planet playing, seed1691104419. Its read-only export produced network177,103segments,217x100samples,1852editable cells. Its real-data erosion probe is not adopted into runtime terrain. Grass-specific GPU boundary/slope captures remain queued with Grass and Rivers. Bryan's visual approval remains pending.
