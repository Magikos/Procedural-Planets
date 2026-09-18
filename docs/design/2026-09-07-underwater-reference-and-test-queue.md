# Water surface and underwater reference validation

Status: Implemented and tested in Unity. Visual acceptance remains with Bryan.

Bryan released Unity for testing, then expanded the request to include the upper water surface.
The work covers wind-driven waves, ripples, reflection, distortion, foam, sun glint, downward transparency, depth, and underwater rays.
The screenshot supplies a visual target. Its character and UI are reference content, not implementation instructions.
Bryan then requested that clarity remain limited so the surface still reads as water.
The implementation retains colored absorption, deep-water opacity, grazing reflection, and underwater haze.

## Implementation

- `WaterVolumeData.hlsl` shares submerged extinction and ambient color between the receiver and atmosphere passes.
- `Atmosphere.shader` integrates sunlight shafts over 24 segments. A world-space envelope follows refracted sunlight through the surface.
- Sun intensity and freezing suppress shafts. Geometry receives shafts after terrain-detail preservation.
- `WaterDisplacement.hlsl` owns geometry displacement and ripple motion. Crossing waves now travel downwind under the shared wind convention.
- `WaterMicroSlope` supplies small moving detail to the surface, submerged interface, and bottom distortion. Unresolved detail fades with distance.
- `Ocean.shader` adds nearby opaque geometry reflections through a bounded screen-space trace. Misses fade into the existing sky reflection.
- Fresnel reflection applies to lakes and oceans. Downward transmission retains the existing depth extinction and shoreline handling.
- Foam stays tied to shoreline contacts and displaced crests. Its breakup drifts downwind.
- Sun glint now respects actual sun intensity. Frozen water suppresses liquid effects.
- The top sheet fades for all fully submerged camera views. The previous facing-only condition exposed distant surface patches underwater.
- `WaterVolume.shader` derives bottom distortion from the shared wave field at the interface. Distortion scales with screen height and respects freezing.
- `WaterVolumeRenderFeature` requests the native opaque color input required by surface reflections.

The existing spherical water mesh remains the geometry owner. Surface and depth prepass use the same displacement function.
The initial surface pass required no new dependency. The interaction follow-up adds a native reflection probe.
See [water interaction and quality validation](2026-09-07-water-interactions-and-quality.md) for the current implementation and measurements.

| Setting | Authored value |
| --- | --- |
| `UnderwaterFogColor` | `(0.03, 0.14, 0.20, 1)` |
| `UnderwaterVisibility` | 45 metres |
| `UnderwaterShaftWidth` | 8 metres |
| `UnderwaterSurfaceDetail` | 0.45 |
| `UnderwaterShaftIntensity` | 3 |
| `UnderwaterNightScale` | 1 |

Settings follow WaterSettings → WaterDto → PlanetWaterSurface. Console commands reject invalid values before replacing the DTO.

## Validation evidence

Local evidence root: `local-only/underwater-reference-2026-09-07/`.
Unity version: `6000.7.0a5`. Planet seed: `1691104419`. Ocean radius: 5000 metres.
The tested raised lake is body 48, with a surface radius of 5041.261 metres and a body depth of 13.48535 metres.

| Check | Result |
| --- | --- |
| Core and Planet code-health builds | Passed. The final Planet build reported 18 existing warnings and zero errors. |
| Unity import and shader compilation | Passed without shader errors. Atmosphere retains five gradient warnings from existing sampling helpers. |
| Fresh play generation | Passed again after the surface extension. |
| ConsoleRegressionTests | 43/43 passed after the settings changes; job `d2e921bfcb954887a01cc670e6a199d8`. |
| Invalid console values | Nine rejections passed. The DTO remained unchanged. See `rejection-results.txt`. |
| Settings reset | Passed. Authored underwater values restored. |
| Ocean depths | Captured at approximately 3, 8, and 30 metres below the surface. |
| Raised lake | Captured above and below its local surface after terrain streaming settled. |
| Sun and ice isolation | Zero shaft intensity, zero sun intensity, and frozen surface captures recorded. |
| Actual reflection | Temporary magenta geometry produced a distorted reflection. See `surface-reflection-proof.png`. Temporary objects were removed. |
| Wind and foam | Breeze and 17 m/s gale captures recorded. Two-second motion pair and FoamOnSwell proof recorded. Wind restored to 2.5 m/s. |
| Downward clarity | Lake captures retain submerged features and caustics. A 0.8-metre-deep shoreline capture also shows contact foam. |

Build warnings are CS9057 analyzer/compiler version mismatch, CS0162 in biome code, CS4014 in ScatterLodSweep, and CS0649 in legacy scatter data.
An existing WeatherSampling warning appeared during some shader variants: "use of potentially uninitialized variable (WeatherCloudConvectivity)".
The Atmosphere warning text is "gradient instruction used in a loop with varying iteration; partial derivatives may have undefined value".

Hot Reload reported "Import Error Code:(4)" during rapid shader edits because its source timestamp differed from disk.
Explicit synchronous reimport refreshed the current water sources. Subsequent shader inspection found no shader errors.
The reflection trace uses explicit LOD sampling to avoid introducing a derivative warning inside its loop.

## Capture pairs

- `surface-comparison-before.png` and `surface-comparison-after.png`: matched surface pose, lighting, wave time, world, and render size.
- `comparison-underwater-before.png` and `comparison-underwater-after.png`: initial underwater candidate against archived source.
- `comparison-above-before.png` and `comparison-above-after.png`: initial underwater-only change, zero differing pixels.
- `comparison-orbit-before.png` and `comparison-orbit-after.png`: initial underwater-only change, zero differing pixels.
- `surface-gale-final.png` and `surface-gale-motion.png`: wind-driven water separated by two seconds of shader time.
- `surface-lake-final.png`: close raised-lake view with downward clarity.
- `underwater-surface-fix.png`: updated submerged interface after the top-sheet suppression fix.
- `preview-surface-settled.png` and `preview-underwater-final.png`: final fresh-session previews.
- `surface-shallow-final.png`: shallow lake bed, distortion, caustics, and contact foam.
- `waterline-*` and `preview-underwater-lateral-*`: waterline and lateral camera checks.

The earlier above-water invariance result applies only to the underwater-only stage.
The requested surface extension intentionally changes above-water pixels.

Captures use a paused camera rendered into a 960 × 720 target. Sidecars record camera pose, sun, wave time, water depth, settings, seed, and quality.
Archived shader variants load only into temporary in-memory materials. Material shaders restore after each comparison.
The initial eight source files remain under `before/`. The original surface shader and displacement include remain under `surface-before/`.
This method avoids overwriting another agent's shared source files.

## Scope and limits

Screen-space reflections cover visible opaque geometry within 200 metres of the surface sample.
The interaction follow-up adds nearby off-screen geometry through a bounded native reflection probe on Medium and High quality.
Low quality retains the screen-space trace and sky fallback. This is not a full reflected render of the world.
The sky fallback remains an approximate weather-aware palette; it does not trace the volumetric cloud renderer.

Frame timing files record whole-editor GPU cost. They do not isolate water from terrain, clouds, or other shared changes.
Invalid GPU samples can leave fewer than 120 valid values in the 120-frame rolling window.
Do not interpret these measurements as a controlled performance improvement.

| Surface shader | Average GPU | p95 GPU | Valid window samples | Completed valid frames |
| --- | --- | --- | --- | --- |
| Archived baseline | 16.441 ms | 21.366 ms | 96 | 3471 |
| Current | 16.431 ms | 21.560 ms | 101 | 2777 |

The game view measured 2160 × 771. Average cost was similar; p95 increased by 0.195 ms.
The comparison swaps the surface shader and displacement only. Both runs retain the current volume and atmosphere.
Shader compilation, Unity tests, source whitespace checks, and `graphify update .` completed successfully.

Early `final-lake-3m` and `final-lake-12m` captures preceded terrain streaming settlement.
Use settled lake captures for review. Earlier lighting captures require their sidecars to confirm published sun state.
The first `preview-surface.png` also preceded settled rendering; use `preview-surface-settled.png`.
`final-midnight.txt` and `final-low-sun.txt` confirm the published night and low-sun directions.

Bryan has not accepted the final look. The captures provide the reviewable result.

## Reproduce a capture

Select a saved water viewpoint, set lighting and wind, then allow frames to publish the settings.

```text
camera.teleport OceanShoreStudy
time.set-local 0.5
time.freeze true
weather.wind-speed 2.5
water.reset
script.run "Underwater Reference Capture"
```

For wind comparison, repeat at 17 m/s, then restore 2.5 m/s.
For deterministic manual comparisons, pause simulation too. `time.freeze` freezes only celestial motion.
