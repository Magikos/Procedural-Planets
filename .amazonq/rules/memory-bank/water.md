# Water Shader Current Context

## Current Focus

Work on one water feature at a time. The active focus is **visible waves**, with accepted shoreline foam, depth color, and sunset reflection preserved.

Latest visual status from Bryan: shoreline foam is visible, no longer glows at night, and "looks pretty good." Treat the current shoreline foam behavior as a keeper unless Bryan calls out a specific foam issue.

Latest depth-color status from Bryan: depth readability looks good, deeper water reads darker, and restored foam contrast works with the depth pass. Treat the current foam + depth-color combination as the accepted baseline.

Water isolation is complete; the current pass is integration with the rest of the scene:
- low-sun ocean shimmer is enabled in focus mode and has been accepted as improved
- no broad Fresnel/reflection tuning
- clouds, rain, and terrain cloud shadows are back on via global `_WaterFocusMode = 0`
- `_OceanFocusMode = 1` remains active so the accepted water render path is retained while scene systems are reintroduced

## Active Files

| File | Role |
|------|------|
| `Assets/Graphics/Shaders/Ocean.shader` | Current ocean shader. Has `_OceanFocusMode`; mode `1` renders depth-colored water, accepted shoreline foam, focus-mode vertex wave displacement, and a low-sun shimmer path when F10 debug is off. |
| `Assets/Graphics/Shaders/Star.shader` | Background stars/sun pass. Masks sky objects where the camera ray hits the sea-level planet sphere so the sun disc is clipped by the horizon instead of showing through transparent water. |
| `Assets/Scripts/Planet/Planet.cs` | Regenerates the planet on start so `WaterMeshBuilder` has runtime terrain data. Creates the water material and sets `_OceanFocusMode = 1`, `_WaterFocusMode = 0`, visible wave amplitude/speed, wave normals on, and low-sun shimmer intensity. |
| `Assets/Scripts/Planet/WaterMeshBuilder.cs` | Builds clipped water bodies and encodes water data into vertex color: R=depth, G=shore distance, B=ocean body factor. |
| `Assets/Scripts/Core/Services/FreeCameraController.cs` | F10 cycles ocean debug modes and the left overlay shows live water material/mesh diagnostics. Modes: Off, Depth, Shore, Body, Lighting, Glint, Normals, Foam, MotionMask, WaveHeight, WaveSlope, WaterData. |
| `Assets/Scripts/Planet/Clouds/CloudRenderFeature.cs` | Skips the volumetric cloud pass only while `_WaterFocusMode` is active. Current integration keeps the global focus gate off so clouds render again. |
| `Assets/Scripts/Planet/PrecipitationRenderFeature.cs` | Skips rain shafts/precipitation only while `_WaterFocusMode` is active. Current integration keeps the global focus gate off so rain renders again. |
| `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl` | Returns no cloud shadow only while `_WaterFocusMode` is active. Current integration keeps the global focus gate off so terrain cloud shadows render again. |
| `Assets/Scripts/Planet/CelestialManager.cs` | Drives sun/moon time and now hides the moon mesh when the main camera is below the local horizon or the ray to the moon intersects the planet sphere. This prevents the moon object from reading as a stray sphere inside/through the planet-water volume. |

## Shore Foam Data Contract

`WaterMeshBuilder` encodes:
- `color.r`: normalized water depth
- `color.g`: normalized distance from shore, where `0` is shoreline and `1` is far from shore
- `color.b`: large ocean body factor

For the current foam pass, `Ocean.shader` should rely mainly on `color.g`.

If the scene starts from serialized cached geometry instead of regenerating, shoreline foam will not work: the old serialized `WaterSphere` has no shore-distance vertex colors, and the shader sees no useful foam input.

The first visible foam pass used `_ShoreFoamDepth = 80` to prove the data path worked. It was intentionally too wide and solid. The current tuning uses `_ShoreFoamDepth = 48`, a lower foam alpha, and shader-side breakup so the first water-cell strip does not read as a flat white ribbon.

The focus renderer must still apply simple sun-side lighting. An earlier version returned `_FoamColor` directly and made shorelines glow on the night side. `RenderWaterFocus` now uses `_SunParams`, the planet normal, and `_NightAmbientIntensity` to dim both water and foam at night.

Current depth-color pass: `RenderWaterFocus` uses `color.r` as water depth, blends `_ShallowColor` to `_DeepColor`, and varies alpha by depth. This is the only new active water layer after accepted foam; waves/glints/reflection tuning remain off. The current ramp is intentionally diagnostic: it darkens earlier so Bryan can clearly judge whether deeper water is reading darker. Bryan confirmed darker deeper waters are now visible; precipitation was still visible and has been disabled for water focus.

After the depth pass, shoreline foam became too subtle because shallow water and foam were too similar. Foam is now applied as a distinct lit overlay in `RenderWaterFocus` so it remains visible without changing the depth ramp.

Latest optical-depth pass: shallow-water opacity is no longer a fixed material alpha. `Ocean.shader` uses `WaterAbsorption01(depth, shore, body, position, normal, viewDir)`, which approximates both vertical depth and camera view path. Very shallow near-shore water remains mostly transparent; deep water, grazing views, and longer camera-to-water paths become more opaque blue, closer to the atmosphere-style "more medium means more color" behavior. This is intended to prevent seeing the planet limb/terrain edge too clearly through deep ocean while preserving shore transparency. `Planet.cs` now sets shallow alpha around 0.10 and deep alpha around 0.96 so the shader's optical depth controls the transition. The latest tuning uses a gated S-curve: shallow/short paths stay transparent, then longer/deeper paths ramp quickly toward near-opaque blue and stronger path darkening before foam/glint are added.

Current motion pass: `_WaterMotionStrength` controls only broad focus-mode vertex swell. The water mesh has ~220k vertices over a radius-5000 planet, so vertex displacement cannot represent small visible waves without much higher tessellation or a local near-camera water patch. Do not scale the whole planet to fix this. The current split is:
- broad mesh swell from `EvaluateOceanWaves`
- visible short waves from fragment-stage procedural normals via `EvaluateRippleGradient`
- no texture normal map yet

After Bryan's debug screenshots showed a huge grid/band moving across the ocean, the extreme diagnostic values were reverted and the method was separated into broad swell plus smaller surface ripple normals. Material defaults are `_WaveAmplitude = 3.4`, `_WaveScale = 480`, `_WaveSpeed = 0.58`, `_WaterMotionStrength = 0.24`, `_WaveNormalStrength = 4.5`.

Latest wave diagnosis: Bryan's F10 debug screenshots did not show credible wave fronts in `WaveHeight`, `WaveSlope`, `Normals`, or normal view. The root issue was the spherical phase coordinate. The shader was deriving a local tangent direction from the same per-point normal, then using `dot(positionWS, tangentDirection)`; on a sphere the radial position is nearly perpendicular to that tangent, so the wave phase collapsed locally and produced either flat output or broad/global artifacts. `EvaluateWaveLayer` now uses fixed global phase seed axes, computes `surfaceCoord = dot(normalWS, seedWS) * surfaceRadius`, and projects the seed into the local tangent plane only for the gradient. After Unity recompiles `Ocean.shader`, `WaveSlope` is the first debug mode to inspect; it should show repeated bands/dashes instead of a flat wash.

Bryan confirmed the wave signal is now visible but too flat and not obviously moving. The latest pass keeps broad mesh swell slow, speeds up normal-only ripples independently, strengthens focus-mode wave normals, and adds subtle moving crest/trough row shading in `RenderWaterFocus`. This is still shader-normal motion, not dense vertex waves; the current global water mesh remains too coarse for small vertex waves.

Bryan confirmed movement is visible and closer to real waves, and the glint looks better. The current target is larger waves in deeper water plus wind/storm coupling. `Ocean.shader` now uses:
- `DeepWaterFactor(depth01)` to boost broad swell and ripple detail offshore while tapering visible wave rows in shallow water.
- `_WindSpeed`/`_WindDirection` from `WeatherManager` to align wind-driven wave layers and increase speed/chop as wind approaches 5.
- `SampleCloudShadowWeather(normal).g` only as a foam/whitecap signal. Do not feed local storm masks into wave height or normal strength; that stamped storm-cell shapes into the water and read as circular cyclone artifacts.

`FreeCameraController` F9 water stats now prints sampled wind, derived wave state, derived foam state, storm, rain, and state at the sea-level point below the camera. Wave state is intentionally wind-only; foam state includes local storm intensity.

Latest feedback: waves are visible and moving, but difficult to read in normal rendering; Bryan also saw odd circular/swirl water patterns that did not behave like water waves. The shader no longer uses the spherical `dot(normal, seed) * radius` phase coordinate for waves because it can create bullseye/ring artifacts around wave-axis poles. `EvaluateWaveLayer` now evaluates each wave with triplanar planar projections and blends by the planet normal. `EvaluatePlanarWave` must keep the projected wave direction unnormalized: normalizing it near projection poles turns a naturally-zero gradient into an arbitrary full-strength tangent and creates circular/cyclone normal artifacts. `RenderWaterFocus` also adds subtle open-water whitecaps and wave-normal specular highlights, gated by deep water, shore distance, foam state, and daylight. F10 `Foam` should now include open-water crest foam in addition to shoreline foam.

Scale note: `Planet.cs` now derives water distance tuning from `PlanetRadius / 5000`. Depth thresholds, shore range, shore foam width, broad wave amplitude, and broad wave scale all preserve the current look at radius 5000 and scale proportionally if the planet radius changes. Wave speed, normal strength, motion strength, shimmer, and alpha are intentionally not distance-scaled.

Current sunset reflection pass: `Star.shader` now clips the sun/stars against the sea-level planet sphere, which prevents the lower half of the sun from showing through transparent water at the horizon. `RenderWaterFocus` uses `_SunGlitterIntensity = 1.45` for a low-sun-only shimmer path: a screen-space elongated reflection band centered under the sun, broken by wave rows/noise, plus a smaller normal-based specular component. This is intentionally not the full Fresnel/reflection stack.

Latest integration artifact pass:
- `Star.shader` now uses a local horizon-plane mask when the camera is near/inside the sea-level sphere instead of returning full sky visibility. This should prevent the sun disc/stars from showing through the planet or under water at low shoreline views.
- `Atmosphere.shader` multiplies light shafts by the local sun horizon visibility, so below-horizon sun shafts should not leak up through water/terrain.
- `Ocean.shader` now samples `_CameraDepthTexture` in the transparent water pass and reconstructs the opaque scene position behind each water pixel. That scene path is folded into `WaterAbsorption01` so shore water can remain transparent while longer/deeper camera rays become darker and more opaque. This is still a surface-shader approximation; a fully correct underwater volume would be a dedicated depth-aware fog/transmission pass.
- `Cloud.shader` and `Precipitation.shader` now jitter individual raymarch steps. Rain also has additional fine and height breakup in its curtain density to reduce horizontal banding close to the camera.
- `CelestialManager.cs` separately culls the serialized moon mesh against the main camera's local horizon/planet sphere. The procedural sun and stars are handled by `Star.shader`, but the moon is normal scene geometry and needs this renderer-level visibility gate.

Latest low-angle water/cloud artifact pass:
- `Cloud.shader` now uses stratified per-pixel view samples instead of marching every pixel through the same fixed slice positions, and `LightMarch` also jitters its sun-ray samples. Cloud defaults were raised to 72 view steps and 8 light steps with stronger ray offset jitter. The goal is to turn close-cloud horizontal strata into fine stable noise instead of visible bands.
- `Ocean.shader` now has a camera-medium absorption term in addition to vertical depth, grazing view distance, and scene-depth path. When the camera is near/inside the sea-level surface and looking across deeper water, absorption ramps harder so water reads as volume instead of a transparent blue sheet. F10 now has an `Absorption` debug view; deep/grazing body water should turn blue while shallow shore should remain dark/low.

Current water debug pass: `FreeCameraController` can print the live water shader name, focus/debug flags, wave material values, depth/foam settings, water mesh vertex/triangle counts, vertex color ranges, and derived motion/normal mask stats. This scan is expensive, so it is off by default and can be toggled with F9. F10 debug modes `MotionMask`, `WaveHeight`, `WaveSlope`, and `WaterData` should be used before more tuning if water still reads smooth. `WaveHeight` uses a higher-contrast blue/gray/yellow signed display. The most important readout is `Camera sample` plus `Motion mask`: if motion mask is near zero at the viewed water, the wave shader is being intentionally suppressed by depth/shore/body data rather than failing to run.

F10 now also auto-saves a compact diagnostic capture when `FreeCameraController.SaveF10DebugScreenshots` is enabled. Files are written to `local-only/debug-screenshots` by default as a low-resolution PNG plus a `.txt` sidecar. The sidecar records the F10 mode, camera transform, planet/sea radii, frame settings, sun state, water material values, weather sample, and water mesh vertex-color stats so future agents can diagnose the current view without relying only on screenshot OCR.

Latest event/debug maintenance:
- `IGameEvent` is back in its own file under `Assets/Scripts/Core/Events`; keep it with the event system for now rather than moving it to `Core/Interfaces`.
- `EventBus<T>` now stores weak method targets instead of strong delegate references, isolates/logs subscriber exceptions, supports deferred one-shot listeners, and clears deferred calls in `ClearAll`.
- `EventBusAutoBinder` was fixed for the non-generic Unity-compatible `[HandleEventBus(typeof(...))]` attribute and now has `UnbindEvents()` for OnEnable/OnDisable symmetry.

Latest water-volume pass:
- Bryan captured full F10 loops from shore and underwater. The captures proved the volume pass and debug autosave work, but underwater/long path pixels still had too much bright scatter.
- `WaterVolume.shader` now increases density/extinction for underwater and long low-angle surface paths, reduces submerged sunlight scatter, and darkens long water paths toward deep color. Surface wave/glint/foam code was intentionally left unchanged in this pass.
- F10 `VolumeLight` debug changed to RGB = scatter light, extinction boost, volume blend. After Unity recompiles `WaterVolume.shader`, retest normal Off, Absorption, VolumePath, VolumeMask, and VolumeLight from the same shore/underwater bad angles.

Latest test feedback from Bryan:
- Underwater fade-to-black is closer, but needs a blue tint over everything and the black fade can be dialed back slightly.
- From the surface, a sphere-like shape appears near the bottom of the screen in water and follows the camera, reading as a camera-relative artifact.
- Follow-up `WaterVolume.shader` pass removed the broad above-water camera-proximity fallback path, gates low-angle surface volume to actual open/deeper water, adds an underwater blue tint before final extinction, and reduces extinction/scatter suppression slightly. Retest the same F10 Off/Absorption/VolumePath/VolumeMask/VolumeLight modes after shader recompile.

Latest follow-up feedback:
- Things are generally looking good, but there is still a visible line from above water and somewhat underwater.
- There is a sea-level "sweet spot" where the underwater blue effect drops out.
- `WaterVolume.shader` now uses an analytic sea-level sphere exit distance for underwater path length instead of switching between water-mesh coverage and fallback full-screen volume. This should reduce the hard line where underwater pixels cross from mesh data to fallback data.
- The underwater threshold was widened from "several units below sea level" to "at/just below sea level" so cameras around `DistanceToCenter ~= SeaLevelRadius` still receive the underwater blue tint.
- The water mask is softened with `smoothstep` instead of a hard step to reduce thin volume seams at water coverage edges.

Latest shoreline-line pass:
- Latest F10 loop looked good overall: above/underwater color and volume are now solid, but a thin line remained from above water and somewhat underwater, likely shoreline data/foam bleeding through.
- The line is visible in normal Off and also reads in `WaterData`, `Shore`, and `Foam`, so the likely source is the ocean surface shoreline/lip data rather than only the water-volume pass.
- `Ocean.shader` now breaks up and distance-fades the continuous shoreline lip foam, and suppresses shore foam more aggressively when the camera is submerged/near submerged. This keeps close shoreline foam available but should prevent distant or underwater shore edges from drawing as a clean white line.
- `FreeCameraController` now keeps F10 debug captures bounded with `DebugScreenshotMaxRuns = 6`. Retention is implemented as newest `maxRuns * F10ModeCount * 2` files, covering PNG plus TXT sidecars. Current captures are not manually purged until the next F10 save runs the pruning path.

Latest F10 workflow/debug isolation pass:
- Bryan clarified that he cannot choose specific debug modes during testing; pressing F10 through every mode is the practical workflow, but it creates too many captures.
- `FreeCameraController.F10CaptureSet` now defaults to `WaterArtifact`. One F10 press automatically captures the targeted artifact set and restores the debug mode to Off afterward. The old one-step behavior is still available by setting `F10CaptureSet = CurrentModeOnly`, and the all-mode loop is available with `FullLoop`.
- The targeted set is: Off, Shore, Foam, WaterData, Absorption, VolumeMask, VolumePath, FoamParts, SurfaceAlpha, VolumeBoundary, VolumeOptical.
- F10 pruning now sizes retention by the active capture set: six targeted runs for `WaterArtifact`, six full loops for `FullLoop`, or six manual full cycles for `CurrentModeOnly`. With the default targeted set, the next F10 run should prune toward 132 PNG/TXT files.
- `WaterVolume.shader` now bypasses the post-volume pass for surface debug modes 1-11 plus 18/19. This is important because underwater volume tint was making surface debug captures look nearly identical to normal Off, hiding whether a line comes from surface water or the volume pass.
- New debug modes:
  - `FoamParts` (18): RGB = shore foam, runup foam, open-water crest foam.
  - `SurfaceAlpha` (19): RGB = final surface alpha, optical alpha, scene path.
  - `VolumeBoundary` (20): RGB = waterVisible, scene depth valid, scene-behind-water amount.
  - `VolumeOptical` (21): RGB = optical, volume blend, deep extinction.
- Latest underwater captures around `20260520-2342` still show dotted/shoreline-like lines on terrain in Off. They appear separate from the earlier above-water line; the next targeted F10 set should make it clear whether these correlate with `FoamParts`, `SurfaceAlpha`, or `VolumeBoundary/VolumeOptical`.

Latest targeted capture diagnosis:
- Bryan ran the targeted F10 set successfully. The folder pruned from 238 files to 154 files, confirming the capture-set retention path is active.
- Two latest sets were captured: `20260520-235748` above sea level (`DistanceToCenter=5034.60`) and `20260520-235803` just below sea level (`DistanceToCenter=4998.30`).
- Above water: the thin far-shore line is visible in `VolumeBoundary`/`VolumeOptical`, so it is primarily a water-volume coverage/edge artifact rather than only surface foam.
- Underwater/near-waterline: dotted shoreline-like marks remain visible in surface-isolated debug modes, so that is a separate surface shoreline/alpha artifact.
- `WaterVolume.shader` now uses `volumeWaterMask = waterMask * smoothstep(0.030, 0.115, waterMaskBasis)` for volume contribution. The raw water mask is still shown in debug red, but the volume uses the eroded green mask to avoid compositing on subpixel shoreline fringe pixels.
- `Ocean.shader` now suppresses submerged shore foam harder (`ShoreFoamSubmergedVisibility` bottoms at 0.06) and multiplies shoreline-edge surface alpha/foam by `UnderwaterShoreEdgeVisibility`. This only targets low-`shore01` shoreline edges when the camera is at/under the water surface; open water should remain visible.

Latest edge-bleed follow-up:
- Bryan still sees an edge bleeding through after the first split fix. The newest captures around `20260521-000447` and `20260521-000458` show the line still correlating with `VolumeBoundary`/`VolumeOptical`.
- The remaining problem is likely not just subpixel coverage, but the volume pass accepting water mesh data too close to terrain-water intersections. At those pixels, the water surface is technically between the camera and terrain, but visually it reads as a terrain/shore edge bleeding through the water.
- `WaterVolume.shader` now defines `volumeInteriorMask = smoothstep(0.035, 0.16, depth01Raw) * smoothstep(0.080, 0.32, shore01Raw) * smoothstep(0.20, 0.55, body01Raw)`. `volumeWaterMask = waterMask * volumeInteriorMask`, and the volume pass uses `volumeWaterMask` for visibility, normal selection, and raw water-data blending.
- This means shallow/shore fringe pixels should fall back to the continuous underwater/low-angle volume instead of injecting hard shoreline data into the composite. F10 `VolumeMask` now shows RGB = raw water coverage, volume interior coverage, volume interior gate.

Latest above-water shelf regression:
- Bryan reported the edge may be fixed underwater, but above water got worse and reads like a surface sheet/shelf: the top of the water is colored but the water below it does not have enough body.
- Diagnosis: the previous `volumeInteriorMask` was too strict for above-water low-angle shore views. It removed too much volume contribution near shore, which exposed the water surface as a thin sheet.
- `WaterVolume.shader` now uses a less aggressive edge-only volume gate: `volumeEdgeMask = smoothstep(0.010, 0.060, waterMaskBasis)` and `volumeBodyMask = lerp(0.65, 1.0, smoothstep(0.10, 0.45, body01Raw))`; `volumeWaterMask = waterMask * volumeEdgeMask * volumeBodyMask`. F10 `VolumeMask` now shows RGB = raw water coverage, effective volume coverage, edge gate.
- Bryan asked whether the water mesh should bleed into terrain slightly. Yes: a small overlap is reasonable because terrain depth should occlude the under-terrain water while the overlap hides raster/generation gaps. `WaterMeshBuilder` now pushes clipped shoreline vertices a small distance toward the dry endpoint (`shoreRange * 0.08`, clamped by planet scale) so the generated water mesh has a subtle under-terrain lip.

Latest close-up artifact pass:
- Bryan repeated the above/below F10 captures and also flew closer to the artifact. The close-up set around `20260521-002432` was useful: the visible bright edge tracks `FoamParts`/`SurfaceAlpha` at the exact shoreline, not only the volume modes. This means the above-water artifact is now mainly a hard shoreline foam/surface band.
- `Ocean.shader` now clears foam away from the exact terrain-water intersection: `ComputeShoreFoam` starts the foam band slightly inside the water (`edgeClear` plus a shifted `shorelineBand`) and reduces the old `lipFoam` term. This should make shoreline foam read as broken water-side wash instead of a continuous white/yellow terrain edge.
- `Planet.cs` reduced `WaterShoreFoamDepth` from 48 to 32 at radius 5000 scale. This narrows the generated shore foam band while keeping a visible foam zone available for later polish.

Latest drawing-order/depth-contact pass:
- Bryan still sees the above-water edge when looking toward another shore and asked whether it is drawing order. It is partly a transparent/depth-contact artifact rather than a simple render-queue bug: opaque terrain renders first, then transparent water blends over it when shoreline water pixels still pass depth.
- New F10 set around `20260521-003259` still shows the line in `FoamParts`, `SurfaceAlpha`, and `VolumeBoundary`, so both the transparent surface shader and the volume composite need to back off at terrain-contact pixels.
- `Ocean.shader` now has `ShoreContactVisibility(scenePath, shore01)`, using the scene-depth water path to fade shoreline foam/alpha only where opaque terrain is immediately behind the water surface. This is multiplied with the underwater shore edge visibility in both focus and normal paths.
- `WaterVolume.shader` now adds a softer above-water shoreline contact fade based on `aboveScenePath`: it starts with `waterVisibleRaw`, computes `terrainClearance`, and fades only low-`shore01` contact pixels. This should avoid drawing a volume line over terrain without reintroducing the too-strict interior mask that caused the sheet/shelf look.

Latest surface-contact diagnostic pass:
- Bryan's latest F10 captures still show the artifact in `FoamParts`, `SurfaceAlpha`, and `VolumeBoundary`, and he correctly described it as terrain/shoreline detail being drawn on top through transparent spherical water at certain angles.
- `Ocean.shader` now measures the raw camera-ray gap between the transparent water surface and the opaque scene depth with `WaterSceneGapMeters`. `ShoreContactVisibility` uses that raw gap, not the previously gated water path, so shoreline surface alpha/foam can fade when terrain is effectively right behind the water surface.
- Surface fresnel alpha and focus-mode sunset shimmer alpha now also respect the terrain-contact fade. This targets the "drawn on top" sheet look where the base water alpha was reduced but fresnel/glint alpha could still overlay shoreline terrain.
- F10 `WaterArtifact` now includes `SurfaceContact` (mode 22). In that debug view, red = low-`shore01` shoreline contact pressure, green = terrain clearance from raw scene-depth gap, and blue = raw water-to-scene gap scaled for inspection. `WaterVolume.shader` bypasses the volume composite for mode 22 so the view isolates the surface shader.
- `WaterVolume.shader` also widened its above-water low-shore terrain-contact fade by using low `shore01Raw`, valid scene depth, and a broader `aboveScenePath` clearance range. This should reduce remaining volume contribution over terrain-contact shoreline pixels without returning to the overly strict interior mask.

Latest volume-edge/fresnel pass:
- Bryan ran another targeted F10 set around `20260521-005059`, `20260521-005110`, and `20260521-005120`. The sea-level set `005110` was most useful: `FoamParts` was basically clean, while `SurfaceAlpha`, `VolumeBoundary`, and `VolumeOptical` still showed the contour. This points away from shore foam and toward the water prepass/volume coverage edge plus grazing surface blending.
- `SurfaceContact` mode 22 helped but did not fully explain the sea-level artifact because the line can occur where `shore01` is high/open-water-like, not only at low-shore contact pixels.
- `WaterVolume.shader` now computes `WaterScreenEdgeFade` from neighboring `_WaterVolumeData` coverage samples and multiplies it into `volumeWaterMask`. This softens the full-screen volume composite at the rasterized water prepass edge instead of letting one hard coverage row create a line.
- F10 `VolumeMask` mode 14 now outputs RGB = raw water coverage, effective volume coverage, screen-space edge fade. This makes it easier to see whether the volume edge fade is catching the artifact.
- `Ocean.shader` now reduces grazing reflection strength and fresnel alpha when `_WaterVolumeEnabled` is active. This targets the thin bright horizon/silhouette line while leaving the volume pass responsible for the main body of the water.
- F10 `WaterArtifact` now includes `SurfaceBlend` mode 23. RGB = final surface alpha, base surface alpha, fresnel alpha boosted for inspection. `WaterVolume.shader` bypasses the composite for mode 23.

Latest near-surface silhouette pass:
- Bryan reported the artifact is still visible near the water surface while looking toward shore. F10 sets around `20260521-072953` and `20260521-073005` show the issue clearly.
- `SurfaceBlend` still showed a broad blue/purple grazing-alpha band and `VolumeOptical` still showed a yellow contour. `FoamParts` was not the primary source, so the active problem is a near-surface grazing silhouette, not shore foam.
- `Ocean.shader` now makes the transparent surface much lighter when `_WaterVolumeEnabled` is active: `WaterFinalAlpha`'s volume surface alpha is lower, grazing reflection fresnel is reduced more aggressively, and surface alpha gets an additional grazing fade.
- `WaterVolume.shader` now computes `horizonOcclusion` for above-water, near-surface, grazing, open-water view paths. It increases density/extinction, lowers scatter light/strength, increases volume blend, and contributes to `deepExtinction` so bright terrain/shore pixels behind the water horizon get tinted/darkened instead of preserved as a white line.
- Existing F10 modes should validate this pass: `SurfaceBlend` should show much less blue grazing alpha; `VolumeOptical` should show more blue/deep-extinction contribution at the problematic contour.

Latest binary-isolation pass:
- Bryan ran another F10 set around `20260521-093744` and reported the artifact still looks unchanged. That is credible: the final `Off` view still has the same white/bright line near the water surface looking toward shore, despite prior surface/volume tuning.
- Diagnosis is now explicitly uncertain. Stop tuning values until the renderer path is isolated.
- New F10 modes were added:
  - `VolumeOnly` (24): `Ocean.shader` returns transparent alpha, but `WaterVolume.shader` still composites normally. If the line remains here, the volume composite or its prepass data is responsible.
  - `SurfaceOnly` (25): `Ocean.shader` renders normal water, while `WaterVolume.shader` bypasses and returns `_Source`. If the line remains here, the transparent ocean surface pass is responsible.
  - `WaterOff` (26): `Ocean.shader` returns transparent alpha and `WaterVolume.shader` bypasses. If the line remains here, the artifact is not coming from water; investigate terrain/atmosphere/cloud/depth ordering instead.
- The next F10 run should compare `Off`, `VolumeOnly`, `SurfaceOnly`, and `WaterOff` first. The older debug modes remain useful, but binary isolation now takes priority.

Latest volume-only confirmation:
- Bryan's F10 set around `20260521-095054` showed the line clearly in `Off` and `VolumeOnly`. It did not appear the same way in `SurfaceOnly` or `WaterOff`. This confirms the artifact is in the full-screen water volume composite/prepass path, not the transparent ocean surface pass.
- `WaterVolume.shader` now has a broader scene-contact fade for above-water near-surface grazing views. It computes `grazingSceneContact` from water visibility, valid scene depth, surface proximity, grazing angle, and short `aboveScenePath`, then combines it with the prior low-shore `shoreContact` into `contactRisk`.
- `waterVisible` now fades by `terrainClearance` whenever `contactRisk` is high, regardless of `shore01Raw`. This targets the open-water-looking contour that the old low-shore-only contact fade missed.
- Added `VolumeContact` debug mode 27. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = contact risk, terrain clearance, resulting water visibility. The next F10 should compare `Off`, `VolumeOnly`, `WaterOff`, and `VolumeContact`.

Latest volume-edge dilation pass:
- Bryan's F10 set around `20260521-111938` still showed the line in `Off` and `VolumeOnly`. `WaterOff` did not show the same line. `VolumeContact` showed the mask near the same contour, but fading contact away still left a bright source-color sliver.
- Updated diagnosis: the artifact is likely a narrow untreated source-color/terrain sliver at the edge of `_WaterVolumeData`, made visible by contrast with the tinted water volume. Fading/eroding the volume edge is the wrong direction for this case.
- `WaterVolume.shader` now samples neighboring `_WaterVolumeData` pixels via `WaterExpandedData` and uses the best nearby water sample to expand volume coverage by about one screen pixel at the boundary.
- `dilationMask` fills pixels where center water coverage is low but nearby water coverage is high. It contributes to `waterMask`, `screenEdgeFade`, `waterVisible`, and `horizonOcclusion`, so edge pixels receive a light water-volume tint instead of preserving a white terrain/source line.
- Added F10 `VolumeDilation` mode 28. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = center water coverage, expanded coverage, dilation-only mask. The next F10 should compare `Off`, `VolumeOnly`, `WaterOff`, `VolumeContact`, and `VolumeDilation`.

Latest volume-refraction isolation pass:
- Bryan's F10 set around `20260521-114748` showed the line still present in `Off`/`VolumeOnly`. `VolumeDilation` did not strongly mark the same contour as a missing-coverage strip, so dilation is not the full explanation.
- Current likely culprit is refraction in `WaterVolume.shader`: the composite may be sampling a bright terrain/shore source pixel across the volume boundary and pulling it into the water.
- `WaterVolume.shader` now suppresses refraction near contact/horizon/dilation pixels with `contactRefractionFade`, using `contactRisk`, `horizonOcclusion`, and `edgeDilation`.
- Added F10 `VolumeNoRefraction` mode 29. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` still runs the volume composite but forces `debugRefractionEnabled = 0`. The next F10 should compare `VolumeOnly` and `VolumeNoRefraction` first. If the line disappears in mode 29, refraction is confirmed as the cause.

Latest volume source-occlusion pass:
- Bryan's F10 set around `20260521-121853` showed `VolumeOnly` and `VolumeNoRefraction` looking effectively the same, so refraction is not the cause.
- Confirmed practical diagnosis: the volume pass is compositing over an already-rendered bright shoreline/terrain source pixel, but the source scene remains too visible through the water. This looks like draw-order because terrain is rendered before the full-screen volume composite and the composite was too transparent at the shoreline contour.
- `WaterVolume.shader` no longer fades contact pixels almost away. `contactVisibilityFloor` keeps at least partial water visibility when contact risk is high, so the volume can cover/tint the offending source pixel.
- Added `sourceOcclusion` for above-water near-surface grazing rays with valid scene depth. It uses `contactRisk`, `horizonOcclusion`, and `edgeDilation` to suppress transmittance, raise `volumeBlend`, and increase `deepExtinction`. This should hide the shoreline source color instead of showing it through the water.
- Added F10 `VolumeOcclusion` mode 30. `Ocean.shader` is transparent in this mode; `WaterVolume.shader` outputs RGB = source occlusion, final volume blend, transmittance suppression. The next F10 should compare `Off`, `VolumeOnly`, `VolumeNoRefraction`, and `VolumeOcclusion`.

Latest terrain/source false-color pass:
- Bryan's F10 sets around `20260521-124410`, `20260521-124437`, and `20260521-124456` were taken from above shore, high altitude, and under terrain but above sea level. The contour is still visible in the source scene at some positions, especially `WaterOff`, so the volume pass needs to prove whether it is exposing terrain/shore source color rather than foam.
- Added F10 `TerrainSourcePink` mode 31. `PlanetVertexColor.shader` paints terrain hot pink for this mode, `Ocean.shader` is transparent, and `WaterVolume.shader` composites normally. If the artifact turns hot pink through the water, the source is terrain/shore scene color.
- Added F10 `FoamPink` mode 32. `Ocean.shader` paints only computed foam hot pink and `WaterVolume.shader` bypasses the volume composite. If the artifact does not turn hot pink here, foam is not the primary source.
- Added F10 `VolumeSphere` mode 33. `Ocean.shader` is transparent and `WaterVolume.shader` outputs RGB = analytic sea-sphere fallback, scene-behind-sea gate, sea path length. This shows whether a ray is covered by sea-level water even when the rasterized water prepass missed the pixel.
- `VolumeOcclusion` mode 30 now returns black for no-water pixels instead of falling back to `_Source`, making the diagnostic honest. If the line stays bright in mode 30, it is true volume output; if it goes black, it is a missing-water/source fallback.
- `WaterVolume.shader` now has a guarded analytic sea-sphere fallback for above-water, near-surface, grazing rays with valid scene depth behind the sea sphere and weak rasterized water coverage. It uses the sea-level sphere entry point as the water depth and supplies conservative depth/shore/body defaults.
- `WaterVolume.shader` also adds `sourcePathOcclusion`, which uses the actual water-to-scene path for grazing above-water pixels. This broadens source suppression beyond low-shore contact and should better tint/darken distant shore source color seen through water.
- Next F10 review should compare `Off`, `VolumeOnly`, `WaterOff`, `VolumeOcclusion`, `TerrainSourcePink`, `FoamPink`, and `VolumeSphere` first. If `TerrainSourcePink` marks the contour but `FoamPink` does not, stop chasing foam and keep the fix in the full-screen volume/source occlusion path.

Latest source-matte pass:
- Bryan's new F10 sets around `20260521-131038`, `20260521-131133`, `20260521-131146`, and `20260521-131201` confirmed the line is terrain source color. `TerrainSourcePink` turns the offending contour hot pink in the above-water sets, while `FoamPink` does not mark the same line.
- The line remains most visible in `Off`/`VolumeOnly` near the water horizon or shoreline, and `WaterOff`/`SurfaceOnly` show the underlying terrain/shore source directly. This confirms the active fix belongs in `WaterVolume.shader`, not foam.
- `WaterVolume.shader` now strengthens `sourcePathOcclusion`, raises the source-occlusion contribution, increases density for source-occluded pixels, clamps source transmittance much lower, uses `sourceMatte` for full volume blend, and pushes deep extinction higher for source-color pixels.
- `WaterVolume.shader` also detects bright source pixels with luminance and applies `brightSourceBleed`, which mattes thin white/yellow shoreline highlights toward deep water color. The goal is to kill the remaining bright strip without globally darkening normal open water.
- Next F10 review should compare the same modes again: `Off`, `VolumeOnly`, `WaterOff`, `VolumeOcclusion`, `TerrainSourcePink`, `FoamPink`, and `VolumeSphere`. The expected result is that the pink/white shoreline strip is either gone or much more blue/dark in `Off` and `VolumeOnly`.

Latest full-bundle re-read:
- Bryan's F10 set around `20260521-135717` showed the missing clue: the visible line is not only bright terrain source color. It also tracks the water/shore boundary in `Absorption`, `VolumeMask`, `VolumeBoundary`, `VolumeOptical`, `VolumeContact`, and `VolumeDilation`.
- `TerrainSourcePink` still proves the underlying color is terrain, and `FoamPink` still does not mark the contour, but the consistent volume-mask correlation means the clipped shoreline/prepass edge is exposing that terrain source.
- Do not keep tuning only source-color matte values if this persists. Treat it as a mesh/prepass coverage seam at the generated shoreline edge.
- `WaterMeshBuilder` now pushes clipped shoreline vertices farther under dry terrain: overlap is `shoreRange * 0.22`, clamped by planet scale. Boundary vertices also encode a small non-zero depth and shore value instead of exact `0,0`, so any edge pixel that survives terrain depth no longer creates a hard water-data line.
- This mesh change requires planet/water regeneration. Because Bryan removed the baked-in planet and the scene regenerates at game start, a fresh play session should pick it up automatically.
- Next F10 review should first compare `Off`, `VolumeOnly`, `VolumeMask`, `VolumeBoundary`, `VolumeOptical`, `VolumeDilation`, `TerrainSourcePink`, and `FoamPink`. Expected result: the diagnostic line should shift under terrain or soften; if it remains identical, the next target is the water-volume prepass depth test/coverage rather than source matte or foam.

Latest square/face-boundary diagnostic:
- Bryan noticed an odd square-like shore geometry in the very latest set. This is visible in the post-regeneration captures around `20260521-141525` and `20260521-141543`, especially `VolumeMask`, where a large straight-edged/square-ish boundary appears.
- The square-ish shape appears faintly in `Off`/`WaterOff` and strongly in water-data/volume modes, so it is likely playing a role. It points toward cube-sphere face/grid boundary data or per-face water classification rather than foam or only source-color matte.
- Code review supports that suspicion: `WaterMeshBuilder` processes each `TerrainFace` independently. `ClassifyWaterBodies` and `ComputeShoreDistance` do not propagate across cube-face edges, so shoreline/water data can show straight face-local boundaries even though terrain elevation itself is sampled continuously by direction.
- Added F10 `TerrainFaceId` mode 34. `PlanetVertexColor.shader` colors terrain by dominant cube-sphere face, `Ocean.shader` hides the water surface, and `WaterVolume.shader` bypasses. If the square edge aligns with a color boundary in `TerrainFaceId`, the next fix should connect/derive water classification across face boundaries or compute shore distance in a global direction-space pass.
- Next F10 review should compare `Off`, `WaterOff`, `VolumeMask`, `VolumeBoundary`, and `TerrainFaceId` first. If `TerrainFaceId` lines match the square/shore artifact, no more broad screenshots are needed before changing `WaterMeshBuilder` topology.

Latest global water graph pass:
- Bryan's F10 sets around `20260521-142601` and `20260521-142628` still looked effectively unchanged. Treat that as confirmation that more source-matte/overlap constant tuning is low value.
- `WaterMeshBuilder` now builds a global direction-space graph for all six cube-sphere terrain faces. Duplicate seam vertices are keyed by quantized unit direction, and body classification plus shore-distance BFS now run once globally instead of once per `TerrainFace`.
- The generated water mesh now also shares original water vertices and clipped shoreline edge vertices by global direction/edge keys, so cube-face borders should not get independent water-data/mesh values.
- This needs a fresh play session so the planet and water mesh regenerate. The next F10 should compare `Off`, `WaterOff`, `VolumeMask`, `VolumeBoundary`, and `TerrainFaceId` first. The previous mesh count was `219813` vertices and `419257` tris; a changed vertex count is a quick sign the global sharing path is active.

## Reference Material

Useful local references:
- `local-only/ocean_wave_foam_halftoning_unity_guide.md`
- `local-only/GDWaterKart-main/water/water_surface.gdshader`
- `local-only/GDWaterKart-main/water/waves/water_foam.gd`

Do not copy the full reference water stack. The project is intentionally validating one visual layer at a time.

## Verification Notes

`dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed after the shoreline foam isolation change.

Full `dotnet build ProceduralPlanets.sln` currently fails because generated Shapes projects reference missing Shapes plugin source files. That failure is unrelated to the water change.

Unity shader compiler logs showed `Assets/Graphics/Shaders/Ocean.shader` preprocessing with `ok=1` after the focus-mode change.

After the F10 retention and shoreline-line pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. `git diff --check` still reports unrelated trailing whitespace in the dirty `Assets/Scenes/Planet.unity` at lines 617 and 659; the touched C# and water memory files pass scoped diff-check, and no trailing whitespace was found in `Ocean.shader`, `WaterVolume.shader`, `FreeCameraController.cs`, or this water memory note. Unity still needs to reimport/compile `Ocean.shader` and `WaterVolume.shader` to validate shader edits.

After the F10 workflow/debug isolation pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed again. Scoped `git diff --check` passed for `FreeCameraController.cs` and the water memory note; untracked shader files were checked separately for trailing whitespace.

After the above-water shelf regression pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. No trailing whitespace was found in `WaterVolume.shader`, `WaterMeshBuilder.cs`, or this water memory note. Unity needs to reimport/compile `WaterVolume.shader` and regenerate the planet/water mesh so the shoreline overlap is visible.

After the surface-contact diagnostic pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. A broad `dotnet build Assembly-CSharp.csproj` still fails because generated Shapes project files reference missing `Assets/Plugins/Shapes/...` sources; this is unrelated to the water shader changes. Scoped `git diff --check` passed for the touched water/controller files, but Unity still needs to reimport/compile `Ocean.shader` and `WaterVolume.shader`.

After the volume-edge/fresnel pass, `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed when run serially. The first parallel build attempt hit the known shared intermediate DLL write collision; rerunning serially passed. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the near-surface silhouette pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`. Unity still needs to reimport/compile the shader edits.

After the binary-isolation pass, `dotnet build ProceduralPlanets.Planet.csproj` and a serial `dotnet build ProceduralPlanets.Core.csproj` passed. The first parallel `Core` build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the volume-only confirmation pass, `dotnet build ProceduralPlanets.Core.csproj` and serial `dotnet build ProceduralPlanets.Planet.csproj` passed. The first parallel Planet build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the volume-edge dilation pass, `dotnet build ProceduralPlanets.Core.csproj` and serial `dotnet build ProceduralPlanets.Planet.csproj` passed. The first parallel Planet build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the volume-refraction isolation pass, `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. The first parallel Core build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the volume source-occlusion pass, `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. The first parallel Core build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. Scoped `git diff --check` passed for `Ocean.shader`, `WaterVolume.shader`, and `FreeCameraController.cs`.

After the terrain/source false-color pass, `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` passed. Scoped `git diff --check` passed for tracked touched files, and no trailing whitespace was found in `Ocean.shader`, `WaterVolume.shader`, `PlanetVertexColor.shader`, or `FreeCameraController.cs`. Unity still needs to reimport/compile the shader edits before the next F10 run.

After the source-matte pass, `dotnet build ProceduralPlanets.Planet.csproj` passed and no trailing whitespace was found in `WaterVolume.shader`. Unity still needs to reimport/compile `WaterVolume.shader`.

After the full-bundle re-read mesh pass, `dotnet build ProceduralPlanets.Planet.csproj` passed and no trailing whitespace was found in `WaterMeshBuilder.cs` or `WaterVolume.shader`. Unity needs to recompile C# and regenerate the planet/water mesh.

After the square/face-boundary diagnostic pass, `dotnet build ProceduralPlanets.Planet.csproj` and serial `dotnet build ProceduralPlanets.Core.csproj` passed. A parallel Core/Planet build attempt hit the known shared intermediate DLL write collision, then passed when rerun serially. No trailing whitespace was found in `FreeCameraController.cs`, `PlanetVertexColor.shader`, `Ocean.shader`, `WaterVolume.shader`, or `WaterMeshBuilder.cs`.

After the global water graph pass, `dotnet build ProceduralPlanets.Planet.csproj` and `dotnet build ProceduralPlanets.Core.csproj` passed. No trailing whitespace was found in `WaterMeshBuilder.cs`. Unity needs to recompile C# and regenerate the planet/water mesh before the F10 result can be judged.
