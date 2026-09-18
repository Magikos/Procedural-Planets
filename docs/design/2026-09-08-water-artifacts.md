# Water artifact fixes — 2026-09-08

Bryan identified repeated lines, patchy highlights, and terrain appearing through the water horizon.
The reference GIF is recorded in the task. Evidence lives in `local-only/water-artifacts-2026-09-08/`.

## Findings and changes

- Disabling the local reflection cube removed dark patches. Ocean now fades that capture within its 350 m range and rejects downward reflection rays.
- Disabling short ripple normals removed the repeated fine rows. Their resolve filter now accounts for reflected highlights, fading across 4–12 pixels per wavelength.
- Water faces previously overwrote each other without nearest-depth selection. A private Depth32 attachment now selects the nearest displaced face. Ocean uses that depth to reject hidden water faces. Opaque camera depth remains unchanged.
- Radial backface clipping estimated a smooth sphere while the mesh contained waves. The depth buffer replaces that clipping.
- Disabling atmospheric interface dilation removed the thin dark horizon outline in a matched capture. Atmosphere now reads exact water coverage.

The shared wave field, water absorption, water colors, and transparency settings remain unchanged.
A short-wave domain-warp experiment did not resolve the artifact and was removed.

## Evidence and limits

The original paused viewpoint was `(3696.63, 1645.24, 2945.62)`, 4.71 m above ocean water.
The fresh run uses planet seed `1691104419`, world seed `12345`, and High quality.
`baseline.png`, `no-probe.png`, and `no-detail-normal.png` record stage isolation.
`depth-final-baseline.png` and `no-dilation-matched.png` isolate the atmospheric seam with matching wave time and lighting.
`final-surface.png` and `final-motion-1.png` through `final-motion-3.png` record the corrected surface over 1.2 seconds.

The fresh run used local noon after restarting Play mode. Do not compare its lighting directly with the original baseline.
Manual camera movement did not refresh all Unity camera globals. The early underwater captures are invalid regression evidence.
`final-underwater-runtime.png` follows a live camera teleport and settled immersion. It confirms the underwater haze and surface window remain active.
`final-down-runtime.png` checks deep-water opacity. `final-lake-shore-runtime.png` checks lake coverage after a settled teleport to Lake1.
The lake still shows a visible reflection transition. This capture does not establish that every reflection artifact is resolved.
The original surface viewpoint was restored, with Play mode running at local noon.

Planet and Core code-health builds passed. The initial no-restore build failed with `NETSDK1004`; the normal build restored its generated assets and passed.
Unity reports no C# compilation failure. The initial fresh-run console check contained no errors.
The later import reported `Import Error Code:(4)` because Unity held an older modification time for WaterDisplacement.hlsl. The include and dependent shaders were imported explicitly afterward.
Ocean retains the existing shader warning: `use of potentially uninitialized variable (WeatherCloudConvectivity)`.
Atmosphere reports `gradient instruction used in a loop with varying iteration; partial derivatives may have undefined value` in its existing iterative sampling path.
The private depth target adds four bytes per render pixel before MSAA. No performance improvement is claimed.
Bryan's visual review remains pending.

## Follow-up: ocean horizon strip

Bryan supplied another ocean GIF and PNG. The dark strip reproduced at the live shore viewpoint `(5.33, 4642.31, 1894.52)`.
Evidence is in `local-only/water-horizon-2026-09-08/`.

The new surface depth rejection caused this regression. It compared linearized device depth with eye depth reconstructed from interpolated world position.
The small meter-space tolerance rejected valid water pixels. Disabling that rejection removed the strip. Flipping its sample UV did not fix it.
Comparing rasterized device depth on both sides fixes the precision mismatch while retaining nearest-face rejection.
The comparison handles reversed and conventional depth directions, with a relative floating-point tolerance.

`before.png` and `after.png` use the same camera, lighting, and wave time. Additional captures at 0.3, 1, 3, and 10 second wave offsets show continuous water coverage.
Unity compiled Ocean without errors. Its existing WeatherCloudConvectivity warning remains.
This follow-up changes only the Ocean fragment depth comparison. It does not alter water clarity or wave settings.
