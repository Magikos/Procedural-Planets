# Shader audit implementation — 2026-09-05

This implements the 26 prioritized findings in `2026-09-05-shader-audit.md`.
The working tree contained extensive earlier changes. No files were staged or committed.
Unrelated creature changes continued during validation and are not part of this report.

## Changes

| ID | Implementation |
| --- | --- |
| S01 | Impostor surface reconstruction weights geometry by sampled coverage. Empty atlas views cannot pull the surface toward background depth. |
| S02 | Impostor daylight uses planet-radial up instead of the tilted object's billboard up. |
| S03 | Impostors sample screen-space ambient occlusion with the mesh material family's strength. |
| S04 | Foliage, impostors, and procedural grass have motion-vector passes. Grass records previous wind, camera position, time, and interaction samples. Both renderer assets include the grass motion feature. |
| S05 | Terrain, grass, and props use the main-light shadow helper that includes shadow-distance fading. |
| S06 | Foliage wind follows the planet tangent plane. Baking disables deformation. All mesh LODs fade wind to neutral before the card starts. |
| S07 | Water compositing preserves HDR values and source alpha. |
| S08 | Direct water sunlight and caustics receive geometric shadows. The fullscreen pass declares its shadow texture dependency. |
| S09 | Ice uses a nondegenerate triplanar field. Terrain fibres use a planet-local 3D field. |
| S10 | The optical-depth LUT stores normalized finite values. Analytic planet occlusion replaces the overflowing half-float sentinel. |
| S11 | Atmospheric midpoint samples use half of the current segment's optical depth. |
| S12 | Cloud and virga light integration use segment transmittance, including a stable thin-segment limit. |
| S13 | Cloud shadow density follows the visible cloud density multiplier. Zero density removes the shadow. |
| S14 | Rain computes lighting averages before applying camera fades and opacity caps. |
| S15 | Weather particles use fixed cube-face lattices and world-anchored vertical layers. The lattice covers the poles. |
| S16 | Rain spawning chooses a nonsingular fallback axis. |
| S17 | Rain curtains use continuous planet-local 3D noise across cube faces. The shared math include owns the reused noise function. |
| S18 | A shared weather threshold helper defines collapsed intervals, including the disabled endpoint at one. |
| S19 | The cloud-type diagnostic overrides convectivity in both visible clouds and shadow sampling. |
| S20 | Cloud blur returns before neighbor samples when its output must equal the center. |
| S21 | Hidden caustics skip pattern evaluation while retaining depth and water-body metadata. |
| S22 | Terrain reuses identical corner material results. Explicit gradients preserve sampling across divergent branches. |
| S23 | Texture arrays initialize all fallback mips and preserve GPU-copied slices. Compressed inputs convert when neutral fallback slices require it. Null biome entries are handled. |
| S24 | SDF text outputs straight-alpha edge color for its straight-alpha blend mode. |
| S25 | Bench conversion transfers vendor albedo properties, texture scale, and texture offset. |
| S26 | Both bake paths restore globals, ambient state, and the active render target after failures. Temporary objects and buffers have scoped cleanup. Local interactor bindings support isolated D3D12 baking. |

Packed water interface data also uses point filtering to avoid interpolating identifiers and validity data.

## Verification

- Core and Planet C# builds passed serially. Core reported two analyzer-version warnings; Planet reported 18 existing warnings.
- Unity imported the changed shaders without reported compile errors.
- All 23 selected EditMode tests passed: eight LOD fade tests, ten scatter rendering tests, and five shader audit cases.
- GPU readback verified every mip of compressed and uncompressed biome fallback arrays.
- GPU readback verified finite optical-depth values in the actual RGHalf target.
- Two foliage bakes produced identical albedo and surface-data pixels under calm and 30 m/s wind.
- A deliberately failed bake restored global state and removed its temporary rig.
- All 184 generated impostors were rebaked, producing 368 atlas images.
- Four sweeps captured every tier of 185 prototypes at 0, 90, 180, and 270 degrees.
- A fresh Planet run produced finite motion vectors. At a fixed camera, calm wind produced 16,779 nonzero pixels; 30 m/s wind produced 128,223. Neither case contained NaN or infinity. This checks live motion output, not all temporal artifacts.
- The archived 20:23:59 shore capture matches the baseline camera, sun direction, PC quality, and emitted grass count of 614,065. A separate 20:22:30 capture shows local noon.

| Sweep yaw | Prototypes passing existing thresholds | Earlier reference |
| --- | --- | --- |
| 0 | 178 / 185 | 178 / 185 |
| 90 | 180 / 185 | 180 / 185 |
| 180 | 178 / 185 | 178 / 185 |
| 270 | 180 / 185 | 180 / 185 |

The sweeps report remaining Golden Forest Tree color/brightness and thin coral differences.
They also flag the intentionally card-free lily and nearly empty coral silhouettes.
These results do not establish complete LOD parity or identify the original rock-shadow recording's cause.

Evidence lives in `local-only/shader-fixes/`: build logs, `tests-final.json`, `motion-wind-check.txt`, four `lod-*` directories, and archived baseline sources/captures.
`shore-after.png` is an invalid black capture and must not be used as visual evidence.
The 20:08 F10 set captured an orbital view, not the requested shore baseline.
Valid later shore sets and sidecars are in `captures-after/`. The baseline sun direction was restored to `(-0.0540, 0.9157, 0.3982)`.

The baseline reported GPU average/p95 of 17.00/24.16 ms. The matched shore capture reported 16.98/23.83 ms.
CPU average/p95 changed from 18.51/24.62 ms to 18.91/27.03 ms.
Weather evolved between runs and other code changed concurrently. These samples do not establish a performance improvement.

## Validation limits

The four sweeps use the existing 36-pixel handover thresholds. They do not test every sun angle, weather state, cascade boundary, or moving-camera trajectory.
The wind probe verifies live motion output. Moving-camera temporal quality and the weather particle distribution still need broader scenario coverage.
The cube lattice changes particle distribution and draws six bounded candidate grids. No frame-time saving is claimed.
HDR, ice, cloud integration, text, and water lighting have source fixes; the tests above do not certify their final appearance.
Concurrent creature edits caused test-result replacement and a Play Mode domain reload. The shader-specific rerun was checked by test names.
The reload produced `InvalidOperationException: No active world context.` and subsequent controller null references. These did not occur during the isolated LOD sweeps.
The fresh shore run also reported the existing compute warning: `Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`.
The inspected helper returns a texture load on every branch. This warning remains unclassified; it is not a proven new shader defect.
The audit's lower-priority conditional observations were not all included in these 26 fixes.
The final test attempt initially reported `Test job failed to initialize (tests did not start within timeout)` during a domain reload. The idle-editor retry passed all 23 tests.
Unity validated both renderer feature lists. The project was left in Edit Mode with the Planet scene open.
`graphify update .` completed after the source changes.
