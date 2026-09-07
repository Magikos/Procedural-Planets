# Shader audit — 2026-09-05

**Findings only — no code changed.**

## Audit Summary

The review found **26 prioritized findings: 4 High, 19 Medium, and 3 Low**. Performance entries identify source-level opportunities, not measured frame-time savings.

The most urgent LOD findings concern invalid impostor surface blending and inconsistent daylight inputs on tilted rocks. Other gaps include missing AO and motion data. These findings include defects in the recent LOD work. Earlier successful builds and comparison sweeps did not establish complete visual parity.

The reported rock-shadow capture remains unattributed. S01 and S02 are concrete candidates; nearby caster changes and shadow cascades still require reproduction.

Reviewed working tree: detached HEAD `1df21b2e155ff1d3a35d64376c2ae134a7931166`, with 687 pre-existing dirty entries at inventory time. The audit used current files, including uncommitted changes. No commits, staging, source edits, Unity operations, builds, or GPU profiling occurred.

Scope includes **84 shader-related files under Assets**: 55 first-party files and 29 imported/vendor/demo files. This covers shaders, includes, computes, Shader Graphs, and their two subgraphs. Relevant C# producers, importers, render features, settings, and installed URP implementations were inspected.

First-party shaders received source review. Imported generated shaders received custom-expression, pass-contract, compatibility, and binding review. Repeated generated scaffolding and embedded editor records were not independently certified. Package shaders were inspected only where needed to verify project integration. This is not a cross-platform compiler or visual certification.

This report lives in `advisor-plans/` under the Improve skill's artifact boundary. Existing `docs/audit/` history and implementation plans remain unchanged.

## Priority overview

| ID | Severity | Finding | Effort | Fix risk |
| --- | --- | --- | --- | --- |
| S01 | High | Impostor geometry includes empty atlas views | M | MED |
| S02 | High | Tilted rocks use a different daylight gate as impostors | S | LOW |
| S03 | Medium | Mesh ambient occlusion disappears at the impostor handoff | M | MED |
| S04 | Medium | Custom deformation has no matching motion-vector pass | L | HIGH |
| S05 | Medium | Shadow distance fading differs across material families | S | MED |
| S06 | Medium | Foliage wind and impostor baking use inconsistent deformation | M | MED |
| S07 | High | Water compositing clips HDR on dry objects | S | MED |
| S08 | Medium | Water sunlight ignores solid-object shadows | M | MED |
| S09 | Medium | Ice and grass-fibre coordinates collapse to zero | M | MED |
| S10 | Medium | Optical-depth sentinel exceeds its texture format | S–M | MED |
| S11 | Medium | Atmosphere midpoint samples use end-of-step attenuation | S | MED |
| S12 | High | Cloud light integration changes brightness with step count | M | MED |
| S13 | Medium | Invisible clouds can retain ground shadows | S–M | MED |
| S14 | Medium | Rain fades corrupt lighting averages | S | LOW |
| S15 | Medium | Weather particle placement excludes poles and moves with the camera | M | MED |
| S16 | Medium | Rain spawning has a singular tangent fallback | S | LOW |
| S17 | Medium | Rain curtain noise is discontinuous at cube-face borders | M | MED |
| S18 | Low | Accepted weather thresholds create zero-width smoothstep intervals | S | LOW |
| S19 | Low | Cloud-type diagnostic changes the wrong input | S–M | LOW |
| S20 | Medium | Cloud blur samples pixels whose result cannot change | S | LOW |
| S21 | Medium | Caustic patterns run before final visibility gates | M | MED |
| S22 | Medium | Terrain repeats full material sampling across identical biome corners | M | MED |
| S23 | Medium | Biome texture fallbacks leave mip levels uninitialized | S–M | MED |
| S24 | Low | Text edge colour is attenuated twice | S | LOW |
| S25 | Medium | Bench conversion drops imported vegetation textures | S | LOW–MED |
| S26 | Medium | Bake failures can leave the whole scene in bake mode | S | LOW |

Effort: S = hours, M = about a day, L = multiple days, including verification. Estimates are approximate.
Confidence describes the code evidence. Runtime prominence and performance payoff can remain unverified even when the source defect is certain.

## What came back clean

- Project-relative include resolution found no missing include paths in the 60 normally visible shader-related files.
- Rain and optical-depth computes now reject padded dispatch threads.
- Grass categorical biome interpolation, interactor count limits, and placement output overflow handling contain relevant guards.
- Scatter CPU/GPU LOD band calculations share their distance contract.
- Impostor material-buffer layouts agree across passes.
- Packed impostor surface imports avoid silhouette-alpha coverage processing.
- Ocean and its interface prepass share displacement and freezing helpers.
- Cloud rendering and shadow proxies share vertical-profile and gloom helpers. S13 identifies a separate missing multiplier.
- Weather cube-face orientation and rain particle buffer layouts match their inspected producers.
- LoadingOverlay, ConsoleOverlay, PredatorVision, and SwarmParticles had no prioritized defect in this source review.
- Planet.shadergraph targets URP; no material reference was found. GpuPlanetPatch and GpuPlanetTerrain have no current C# consumer found.
- Imported Built-in shaders have a known bench conversion/detection path. Their presence does not prove production pink materials.

These are bounded negative results, not proof that every shader is defect-free.

# Findings

## S01 — Impostor geometry includes empty atlas views

- **Category:** Bug.
- **Severity:** High.
- **Evidence:** `Assets/Graphics/Shaders/ScatterImpostor.shader:153`; `Assets/Graphics/Shaders/ScatterImpostor.shader:165`; `Assets/Graphics/Shaders/ScatterImpostor.shader:343`; `Assets/Graphics/Shaders/ScatterImpostor.shader:527`.
- **Description and impact:** Normals, depth positions, and leaf masks use view weights without sample coverage. An empty neighboring view can affect a fragment that survives the combined alpha test. At silhouette edges, reconstructed shadow receivers and casters can shift toward dilated or background depth. This is a candidate for rock shadow changes, not a reproduction of the reported rock.
- **Recommendation:** Weight surface attributes by valid coverage and normalize their accumulated weight. Preserve the separate silhouette coverage calculation. Check reprojection against empty depth samples too.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Rotate the same rock through atlas-cell boundaries. Compare mesh/card silhouettes, reconstructed depth, and cast shadows with fixed sun. Test thin coral and leaves as well.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S02 — Tilted rocks use a different daylight gate as impostors

- **Category:** Bug.
- **Severity:** High.
- **Evidence:** `Assets/Graphics/Shaders/ScatterImpostor.shader:336`; `Assets/Graphics/Shaders/Scatter.shader:186`; `Assets/Scripts/Planet/Scatter/ScatterPlacementMath.cs:69`.
- **Description and impact:** The mesh computes radial planet-up from position minus planet centre. The impostor uses object-up. Slope-conforming rocks tilt object-up, so a transition can change daylight and night-side gating, especially near sunrise or sunset.
- **Recommendation:** Use radial planet-up for the day/night gate in every representation. Keep reconstructed surface normals for form lighting.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Compare tilted and upright copies at noon, dawn, and dusk. Force mesh/card while holding position and sun fixed.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S03 — Mesh ambient occlusion disappears at the impostor handoff

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Scatter.shader:214`; `Assets/Graphics/Shaders/FoliageLit.shader:401`; `Assets/Graphics/Shaders/ScatterImpostor.shader:330`; `Assets/Settings/PC_Renderer.asset:140`.
- **Description and impact:** The active SSAO configuration runs before opaques. Scatter meshes apply an AO multiplier with strength 0.25; foliage uses 0.5. The impostor forward pass has no corresponding AO sample. Short-distance handoffs inside the configured 100 m AO falloff can brighten.
- **Recommendation:** Apply the same material-specific AO policy across representations. Use the packed foliage data where needed; verify depth/normal inputs before tuning strengths.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Force LOD0/card near a contact shadow with SSAO enabled, then disabled. Compare within and beyond the AO falloff.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S04 — Custom deformation has no matching motion-vector pass

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Scenes/Planet.unity:381`; `Assets/Graphics/Shaders/FoliageLit.shader:209`; `Assets/Graphics/Shaders/ScatterImpostor.shader:1`; `Assets/Graphics/Shaders/Grass.shader:1`.
- **Description and impact:** Planet enables TAA. Custom foliage wind, billboard rotation, and grass deformation do not supply matching previous-frame motion. Installed URP's MotionVectorRenderPass selects the MotionVectors tag and opaque queues; camera motion cannot describe deformation. Ghosting severity remains unmeasured.
- **Recommendation:** Provide previous-frame transforms/deformation for the affected paths using URP motion-vector conventions. Handle grass queue participation deliberately. Do not treat added blur as verification.
- **Effort:** L. **Fix risk:** HIGH. **Confidence:** HIGH on missing data; MED on visible severity.
- **Validation:** Inspect motion-vector output while moving the camera and changing wind. Capture thin silhouettes and disocclusion trails. Measure added GPU cost.
- **Behavior note:** Rendering change.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S05 — Shadow distance fading differs across material families

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/PlanetVertexColor.shader:1224`; `Assets/Graphics/Shaders/Grass.shader:411`; `Assets/Graphics/Shaders/PropLit.shader:106`; `Assets/Graphics/Shaders/Scatter.shader:196`.
- **Description and impact:** Terrain, grass, and PropLit still call MainLightRealtimeShadow directly. Scatter and foliage now use MainLightShadow, which includes distance fading. Materials therefore lose cast shadows differently at the shadow range boundary.
- **Recommendation:** Use a consistent supported shadow sampling policy. Preserve material-specific ambient terms. Validate cascade transitions separately; the helper alone does not guarantee smooth cascade blending.
- **Effort:** S. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Move the same shadow across terrain, grass, props, and scatter through the final shadow cascade/range. Compare receiver attenuation at fixed sun.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S06 — Foliage wind and impostor baking use inconsistent deformation

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/FoliageLit.shader:220`; `Assets/Graphics/Shaders/FoliageLit.shader:221`; `Assets/Graphics/Shaders/Grass.shader:1`; `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs:224`; `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs:278`.
- **Description and impact:** Foliage uses global wind directly and builds cross-wind motion around world Y. Away from the planet's Y pole, displacement can move radially instead of along the local ground. Grass already has planet-tangent wind handling. The baker also renders live foliage materials without disabling wind or interactors. Baked cards can depend on current weather and encode a pose that the static impostor cannot continue.
- **Recommendation:** Project wind onto local planet-up and construct a safe cross-wind basis. Reuse existing tangent-wind logic where its contract matches. Bake an explicit neutral pose and restore prior state in finally. Define how wind fades or continues across the mesh/card handoff.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Compare wind direction and amplitude at all six cardinal planet locations. Check mesh/card motion near handoff. Bake identical inputs under two wind states and compare outputs.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S07 — Water compositing clips HDR on dry objects

- **Category:** Bug.
- **Severity:** High.
- **Evidence:** `Assets/Graphics/Shaders/WaterVolume.shader:637`; `Assets/Graphics/Shaders/WaterVolume.shader:906`; `Assets/Graphics/Shaders/WaterVolume.shader:919`; `Assets/Settings/PC_RPAsset.asset:26`.
- **Description and impact:** The production water pass ends with saturate(color), including dry scene-depth pixels with no water contribution. RGB values above 1 are clipped before later processing. A dry HDR highlight can therefore lose bloom energy because the water feature runs.
- **Recommendation:** Preserve HDR scene colour. Clamp only bounded masks. Return unchanged source colour when no water effects contribute.
- **Effort:** S. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Render a dry HDR object with colour (2,1,0.5). Compare immediately before/after the water pass and check bloom. Zero water masks must preserve source RGB.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S08 — Water sunlight ignores solid-object shadows

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Ocean.shader:601`; `Assets/Graphics/Shaders/Ocean.shader:653`; `Assets/Graphics/Shaders/WaterVolume.shader:655`.
- **Description and impact:** Surface glints, foam direct lighting, and seabed caustics use cloud shadowing but no main-light geometric shadow. A rock or structure can block sunlight without suppressing these direct-light effects.
- **Recommendation:** Apply geometric shadow attenuation to direct water lighting. Keep ambient/volume lighting separate. Bind shadow resources explicitly for the fullscreen pass.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Place a blocker above shallow water. Compare glints, foam, and caustics inside/outside its shadow, including LOD and cascade movement.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S09 — Ice and grass-fibre coordinates collapse to zero

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Ocean.shader:238`; `Assets/Graphics/Shaders/Ocean.shader:252`; `Assets/Graphics/Shaders/Ocean.shader:542`; `Assets/Graphics/Shaders/PlanetVertexColor.shader:846`.
- **Description and impact:** Both shaders project the radial position vector onto tangents constructed from that same radial direction. For p=r*n and tangent t, dot(p,t)=0. Ice breakup/normal samples and grass fibre/fleck coordinates lose their intended spatial variation. Floating-point residue is not a valid coordinate field.
- **Recommendation:** Replace both sites with a nondegenerate planet-local field or a stable chart. Derive normals from the same field. Reuse existing triplanar helpers where suitable.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Inspect fibre coordinates and partial freezing across separated locations and chart boundaries. A numerical probe returned zero within 2.3e-13 at three distinct positions.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S10 — Optical-depth sentinel exceeds its texture format

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/OpticalDepth.compute:60`; `Assets/Scripts/Planet/Atmosphere/AtmosphereController.cs:220`; `Assets/Graphics/Shaders/Includes/Atmosphere.hlsl:142`.
- **Description and impact:** Blocked rays write 1e6 into RGHalf, whose largest finite value is 65504. The representation cannot preserve that sentinel. If conversion produces infinity, zero scattering coefficients and bilinear interpolation can propagate invalid values.
- **Recommendation:** Represent occlusion explicitly or use a finite, justified optical-depth encoding. Verify behaviour at minimum supported scattering coefficients.
- **Effort:** S–M. **Fix risk:** MED. **Confidence:** HIGH on format violation; MED on GPU symptom.
- **Validation:** Read back LUT values and check finiteness. Set Mie scattering to zero and sweep through the horizon. Check terminator pixels for NaNs.
- **Behavior note:** Numerical correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S11 — Atmosphere midpoint samples use end-of-step attenuation

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Includes/Atmosphere.hlsl:116`; `Assets/Graphics/Shaders/Includes/Atmosphere.hlsl:137`; `Assets/Graphics/Shaders/Includes/Atmosphere.hlsl:145`.
- **Description and impact:** Samples sit at segment midpoints, but attenuation includes the entire current segment. Each sample receives an extra half-segment of extinction. Low step counts darken the result unnecessarily.
- **Recommendation:** Evaluate sample attenuation with prior depth plus half the current segment. Accumulate the full segment afterward for the next sample and final transmittance.
- **Effort:** S. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Use a constant-density reference and 4/8/16/32 steps. At optical depth 4 and four steps, current quadrature gives 0.571317 versus midpoint 0.941943 and exact 0.981684.
- **Behavior note:** Numerical correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S12 — Cloud light integration changes brightness with step count

- **Category:** Bug.
- **Severity:** High.
- **Evidence:** `Assets/Graphics/Shaders/Cloud.shader:457`; `Assets/Graphics/Shaders/Cloud.shader:458`.
- **Description and impact:** Cloud source light uses a linear density*step contribution while extinction uses an exponential. Thick segments overestimate scattering. Quality and path-length changes therefore alter brightness beyond normal sampling error.
- **Recommendation:** Integrate source and extinction over the same segment. Use T*lighting*(1-exp(-density*absorption*step))/absorption with a stable zero-absorption limit. Review optional virga separately.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** For a homogeneous 100 m path, density .01 and absorption 1.2, current energy is .627105 at 8 steps and .585985 at 96; exact is .582338. Verify invariance, then capture real clouds.
- **Behavior note:** Numerical and visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S13 — Invisible clouds can retain ground shadows

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Cloud.shader:214`; `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl:67`; `Assets/Scripts/Planet/Clouds/CloudController.cs:362`.
- **Description and impact:** Visible cloud density includes _CloudDensityMultiplier. The shadow proxy omits it. The accepted command cloud.density 0 can remove visible clouds while their shadows remain.
- **Recommendation:** Make the shadow proxy respect density. Preserve intentional proxy approximation and calibration; zero visible density must produce zero cloud extinction.
- **Effort:** S–M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Freeze weather and sun. Compare density zero/default/high across sky, terrain, rocks, and water.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S14 — Rain fades corrupt lighting averages

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Precipitation.shader:335`; `Assets/Graphics/Shaders/Precipitation.shader:351`; `Assets/Graphics/Shaders/Precipitation.shader:356`.
- **Description and impact:** Storm/lightning sums use raw sample weights. Their denominator is later capped and multiplied by camera fade. The resulting averages can exceed their actual values and amplify lightning as rain fades.
- **Recommendation:** Normalize with the untouched accumulated weight. Apply opacity caps and camera fade only to compositing opacity.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Raw weight .4 with weighted storm .2 should average .5. A .1 camera fade currently produces 5. Test a fixed lightning event while crossing the sea-level fade.
- **Behavior note:** Numerical correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S15 — Weather particle placement excludes poles and moves with the camera

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/WeatherParticles.shader:135`; `Assets/Graphics/Shaders/WeatherParticles.shader:151`; `Assets/Graphics/Shaders/WeatherParticles.shader:153`; `Assets/Graphics/Shaders/WeatherParticles.shader:304`.
- **Description and impact:** Latitude clamps to ±1.52 radians. On a 5000 m planet this excludes roughly 254 m around each pole, outside the default 120 m visibility radius. Tile spacing and longitude scaling also depend on camera radius/latitude, so a fixed tile seed does not identify a fixed world position.
- **Recommendation:** Use stable spherical placement coordinates that cover poles. Let the camera select tiles without redefining their geometry. Address both defects through one placement correction.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Freeze animation. Track a tile under north/south and vertical camera motion. Check dust/snow coverage at both poles and chart boundaries.
- **Behavior note:** Placement change.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S16 — Rain spawning has a singular tangent fallback

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Resources/RainParticleUpdate.compute:72`; `Assets/Resources/RainParticleUpdate.compute:75`.
- **Description and impact:** When the camera looks radially at the ±X planet axis, the fallback forward is parallel to cameraNormal. The subsequent cross product is zero. Normalizing it yields an invalid basis and can create non-finite particle positions.
- **Recommendation:** Choose a reference axis that is not parallel to the normal, then construct an orthonormal basis.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** At all six cardinal locations, spawn rain while looking directly up/down. Require finite particle positions and valid tangent lengths.
- **Behavior note:** Numerical correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S17 — Rain curtain noise is discontinuous at cube-face borders

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/Precipitation.shader:179`; `Assets/Graphics/Shaders/Precipitation.shader:185`.
- **Description and impact:** Curtain noise uses face-local UV and an unrelated face-dependent offset. Adjacent spherical points across a cube edge sample unrelated noise even when the weather field is continuous.
- **Recommendation:** Evaluate noise in continuous planet coordinates or blend compatible charts across edges.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH on discontinuity; MED on visual prominence.
- **Validation:** Force uniform rain and inspect all twelve cube edges with wind frozen and moving.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S18 — Accepted weather thresholds create zero-width smoothstep intervals

- **Category:** Bug.
- **Severity:** Low.
- **Evidence:** `Assets/Graphics/Shaders/WeatherEvolution.compute:166`; `Assets/Graphics/Shaders/WeatherEvolution.compute:346`; `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl:68`; `Assets/Scripts/Planet/WeatherManager.cs:114`.
- **Description and impact:** Threshold 1 is accepted, but several consumers evaluate smoothstep(1,1,x). This does not define a portable threshold policy.
- **Recommendation:** Define exact endpoint behaviour and share it across consumers. Keep a positive interval or use an explicit endpoint branch.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Evaluate thresholds 0, just below1, and1 with samples below/equal/above the boundary.
- **Behavior note:** Defines boundary behaviour.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S19 — Cloud-type diagnostic changes the wrong input

- **Category:** Bug.
- **Severity:** Low.
- **Evidence:** `Assets/Graphics/Shaders/WeatherEvolution.compute:223`; `Assets/Graphics/Shaders/Cloud.shader:196`; `Assets/Scripts/Planet/WeatherManager.cs:119`.
- **Description and impact:** weather.test-pattern promises stratus/cumulus bands by changing moisture. Cloud type now derives convectivity from climate temperature. The diagnostic no longer isolates the stated distinction.
- **Recommendation:** Make the diagnostic control actual convectivity, or revise its contract to select known climate regions.
- **Effort:** S–M. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Verify that the first two test bands differ in the input that controls the vertical profile.
- **Behavior note:** Diagnostic correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S20 — Cloud blur samples pixels whose result cannot change

- **Category:** Performance.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/CloudBlur.shader:45`; `Assets/Graphics/Shaders/CloudBlur.shader:59`; `Assets/Graphics/Shaders/CloudBlur.shader:80`.
- **Description and impact:** The shader reads 25 neighbours before determining that centre alpha≤.02 makes the blend weight zero. Terrain and clear-sky pixels pay for a neighbourhood that does not affect their output.
- **Recommendation:** Compute cloud presence after the centre sample. Return early when it is zero.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH on redundant work; unmeasured speedup.
- **Validation:** Compare pixels and CloudBlur GPU time in clear, terrain-heavy, and overcast views. Check avg and p95.
- **Behavior note:** Preserving for zero-presence pixels.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S21 — Caustic patterns run before final visibility gates

- **Category:** Performance.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/WaterVolume.shader:305`; `Assets/Graphics/Shaders/WaterVolume.shader:445`; `Assets/Graphics/Shaders/WaterVolume.shader:688`; `Assets/Graphics/Shaders/WaterVolume.shader:796`.
- **Description and impact:** The chromatic pattern evaluates 243 Voronoi neighbours per eligible pixel. Depth/path/intensity and later freezing/orbital gates can make the contribution negligible or absent. The pattern also drives a small trough-shadow term, so intensity zero alone is not a safe skip.
- **Recommendation:** Separate volume metadata from optional pattern generation. Gate pattern work only after accounting for every visible use, including trough shadows and debug modes.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH on operation structure; unmeasured payoff.
- **Validation:** Measure WaterVolume in shallow/deep/frozen/orbital views. Compare all caustic and volume outputs.
- **Behavior note:** Preserving within a declared visibility threshold.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S22 — Terrain repeats full material sampling across identical biome corners

- **Category:** Performance.
- **Severity:** Medium.
- **Evidence:** `Assets/Graphics/Shaders/PlanetVertexColor.shader:376`; `Assets/Graphics/Shaders/PlanetVertexColor.shader:495`; `Assets/Graphics/Shaders/PlanetVertexColor.shader:593`.
- **Description and impact:** Four corners each evaluate their material set. With secondary albedo enabled, one material uses 6 albedo,3 normal, and3 ARM reads. Identical single-biome corners request 48 material reads; the four-slot worst case requests 192, before overrides and control-map reads. The existing comment counts only albedo.
- **Recommendation:** Reuse samples for repeated slice IDs or add an exact identical-corner fast path. Preserve corner weights and interlock behaviour. Do not replace categorical IDs with interpolated IDs.
- **Effort:** M. **Fix risk:** MED. **Confidence:** HIGH on source structure; MED on compiled cost.
- **Validation:** Inspect compiled shader/sample counts and measure terrain GPU cost at biome interiors and borders. Compare images at fixed pose and seed.
- **Behavior note:** Preserving.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S23 — Biome texture fallbacks leave mip levels uninitialized

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs:195`; `Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs:240`; `Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs:250`; `Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs:263`.
- **Description and impact:** The array inherits the reference texture's mip chain. Placeholder slices receive only mip0, then Apply explicitly disables mip generation. Matching dimensions/format also do not guarantee that another source has the reference's mip count. Missing maps can therefore change appearance at distance or cause invalid copy requests.
- **Recommendation:** Populate every destination mip for fallback slices. Validate source mip counts before copying and define a complete compatible fallback.
- **Effort:** S–M. **Fix risk:** MED. **Confidence:** HIGH.
- **Validation:** Use one mipmapped source, one absent source, and one same-size source without mips. Inspect every array mip and view each slice at distance.
- **Behavior note:** Corrects fallback rendering.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S24 — Text edge colour is attenuated twice

- **Category:** Bug.
- **Severity:** Low.
- **Evidence:** `Assets/Graphics/Shaders/SDFText.shader:38`; `Assets/Graphics/Shaders/SDFText.shader:161`; `Assets/Graphics/Shaders/SDFText.shader:162`.
- **Description and impact:** With a transparent black outline and white face, coverage .5 produces RGB .5 and alpha .5. Straight-alpha blending then contributes .25 over black instead of .5. Outline/face RGB composition and alpha use incompatible conventions.
- **Recommendation:** Use consistent straight-alpha or premultiplied-alpha composition, including outline alpha. Keep the blend state consistent with the result.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Compare white text edges with outline disabled and enabled on dark/light backgrounds at multiple scales.
- **Behavior note:** Visual correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S25 — Bench conversion drops imported vegetation textures

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Scripts/Planet/AssetBench/AssetBenchService.cs:538`; `Assets/Scripts/Planet/AssetBench/AssetBenchService.cs:549`; `Assets/AssetPacks/ToonFantasyNature/Shaders/TFF_CustomToonVegetation.shader:9`; `Assets/AssetPacks/ToonEnchantedMeadow/Shaders/TEM_CustomVegetation.shader:11`.
- **Description and impact:** Default project-shader conversion does not recognize _TextureSample or _MainTexture. Replacement materials can lose albedo and alpha, making asset comparisons misleading. This is a preview conversion defect, not proof of broken production instances.
- **Recommendation:** Extend the existing source-property mapping. Preserve associated scale/offset and supported tint deliberately.
- **Effort:** S. **Fix risk:** LOW–MED. **Confidence:** HIGH.
- **Validation:** Compare one TFF and one TEM tree in vendor/project bench modes. Inspect colour and alpha silhouette separately.
- **Behavior note:** Preview correction.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

## S26 — Bake failures can leave the whole scene in bake mode

- **Category:** Bug.
- **Severity:** Medium.
- **Evidence:** `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs:239`; `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs:278`; `Assets/Scripts/Planet/Scatter/ScatterImpostorBaker.cs:326`.
- **Description and impact:** BakeAtlas changes ambient lighting, global shader modes, and render targets. Cleanup occurs only on the success path. An exception during rendering/readback/processing can leave flat-albedo or normal-bake modes enabled and leak temporary resources.
- **Recommendation:** Save previous state and restore it in finally. Release partially created objects and textures on failure. Apply the same ownership discipline to the older Bake entry point.
- **Effort:** S. **Fix risk:** LOW. **Confidence:** HIGH.
- **Validation:** Inject controlled failures after allocation and each render/readback stage. Verify restored globals, ambient state, active target, and resource counts.
- **Behavior note:** Preserving on success; corrects failures.
- **Refactor option:** None beyond the existing helpers or shared contracts named above.

# Conditional and lower-priority observations

These items are separate from the 26 prioritized findings.

| Item | Evidence | Status and next check |
| --- | --- | --- |
| Explicit impostor shadow depth on OpenGL | `Assets/Graphics/Shaders/ScatterImpostor.shader:533` | Non-reversed branch returns clip z/w directly. GL needs the correct conversion to device depth. Windows reversed-Z is unaffected. Verify supported graphics APIs before scheduling. |
| Packed water kind filtering | `Assets/Scripts/Planet/WaterVolumeRenderFeature.cs:224`; `Assets/Graphics/Shaders/Atmosphere.shader:224` | Integer kind bits can become invalid under bilinear sampling. The inherited active filter mode was not established. Inspect it before declaring a defect. |
| Impostor work before dither rejection | `Assets/Graphics/Shaders/ScatterImpostor.shader:318`; `Assets/Graphics/Shaders/ScatterImpostor.shader:325` | Coverage can reject before surface reconstruction, but moving discard ahead of implicit derivatives needs care. Measure transition cost and preserve sampling derivatives. |
| Periodic cloud-noise search | `Assets/Graphics/Shaders/CloudNoise.compute:39` | Wrapped neighbours search 27 periodic images. A minimum-image displacement can remove that inner loop. Startup-only opportunity; verify texture equivalence and timing. |
| Imported water zero denominators | `Assets/_Bench/ToonFantasyNature/Shaders/TFF_ToonWater.shader:511`; `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_Water.shader:679`; `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_Waterfall.shader:706` | Inspector-supported zero distances reach divisions. Conditional vendor-preview hazard. Define whether zero disables the effect or makes an immediate transition. |
| Imported wind graph zero denominator | `Assets/_Bench/PolyartDreamscape/PolyartStudio/SharedResources/ShaderGraph/Material Functions/WindSway.shadersubgraph:2460` | Graph computes q*length(p)/length(q). An origin vertex with zero wind can reach 0/0. Guard the zero-length case if this imported graph is used. |
| Dormant math helpers | `Assets/Graphics/Shaders/Includes/Math.hlsl` | SmoothMin/SmoothMax divide by k despite comments allowing k=0. Remap clamps arbitrary output ranges to 0–1. Searches found no shader callers; do not blame active rendering on them. |
| Star chart seam and subpixel aliasing | `Assets/Graphics/Shaders/Star.shader:36`; `Assets/Graphics/Shaders/Star.shader:51`; `Assets/Graphics/Shaders/Star.shader:64` | Longitude cell hashes do not wrap at the atan2 seam; tiny stars lack derivative filtering. Inspect a fixed star field at the seam and during camera motion before setting visual priority. |
| Hourly animation phase continuity | `Assets/Graphics/Shaders/Includes/Math.hlsl` and `ShaderGlobalsController` game-time publishing | A wrapped 3600-second time does not itself guarantee periodic sine/noise phases. Verify water/weather consumers at the wrap boundary before accepting the precision policy as seamless. |

The imported `MF_CheapContrast` graph changes a threshold rather than the slope. This is authored behavior, not a defect without a different intended contract.

# Proposed implementation order

This is a proposal, not an applied change.

1. **Repair LOD reference matching:** S01, S02, S03, S06, S26. Save baseline rock/tree captures first. Establish deterministic bakes before regenerating atlases. Compare every tier directly against LOD0.
2. **Repair shared shadow response:** S05, S08, S13. Test rock receivers, nearby trees, terrain, grass, and water in the same scene.
3. **Repair invalid numeric paths:** S07, S09, S10, S14, S16, S18, S23. Use finite-value and identity checks before visual tuning.
4. **Correct volume integration:** S11 and S12. Require homogeneous-medium convergence before evaluating art direction.
5. **Repair temporal and spatial continuity:** S04, S15, S17. Motion vectors require stable previous-instance/deformation data; a shader pass alone is insufficient.
6. **Measure and remove redundant work:** S20, S21, S22. Record avg and p95 GPU time at fixed pose, seed, quality, weather, and resolution.
7. **Repair verification and previews:** S19, S24, S25. These improve the reliability of future visual comparisons.

Reuse the existing ScatterLodCompare and ScatterLodSweep tools. Extend coverage with strong wind, tilted instances, dawn/dusk, SSAO contacts, shadow receivers, and moving-camera sequences. Four static angles without production temporal/AO conditions are not sufficient acceptance criteria.

For implementation, build `ProceduralPlanets.Core.csproj` and `ProceduralPlanets.Planet.csproj` serially. Then check Unity imports and fresh runtime captures. Builds cannot validate HLSL output or GPU performance. Use existing tests where useful; no new test framework is proposed.

# Prior Audit Reconciliation

Only overlapping shader/render-contract findings are reconciled here. This report does not replace the whole-repository ledger.

| Prior item | Status | Current evidence or limit |
| --- | --- | --- |
| July consolidated F01: rain dispatch overrun | RESOLVED | RainParticleUpdate guards _ActiveCount; controller uploads it. |
| July consolidated F02: optical-depth dispatch bounds | RESOLVED | OpticalDepth.compute rejects padded lanes. S10 is a different format defect. |
| July consolidated F04: cancellable weather texture leak | RESOLVED for reported path | SphericalWeatherGrid catches cancellation and releases textures. |
| July consolidated F07: stale weather readback | RESOLVED | WeatherQueryCache checks captured epochs. |
| July consolidated F08: disabled controllers still render | OPEN, adjacent scope | Inspected feature/controller policy still checks liveness without enabled state in relevant paths. No new shader-math finding duplicates it. |
| July consolidated F13: precipitation ownership | OPEN in inspected portion | Public controller settings and duplicated band construction remain. Outside this report's shader fixes. |
| July shared cloud gloom/profile drift | RESOLVED | Shared helpers remain. S13 concerns the missing density multiplier. |
| July duplicated close-rain lanes | RESOLVED | WeatherParticles handles dust/snow; RainParticles owns close rain. |
| July weather-particle sampling cost claim | REJECTED as before | No new timing evidence. S20 is a distinct zero-contribution branch. |
| July rain per-frame static uploads | RESOLVED | Controller change-checks relevant material uploads. |
| 2026-07-26 N1: scatter draw structure | RESOLVED | ScatterRenderer draws prototype buckets; ScatterTileCache owns ScatterDrawBuckets. |
| N2: shared material instancing mutation | OPEN, adjacent scope | ScatterRenderer still sets part.Material.enableInstancing. Keep as ownership debt. |
| N3: missing LOD chains | SUPERSEDED | Generated meshes and impostors changed the premise. S01–S06 describe current discrepancies. |
| N4: pine fringing observation | Not promoted | No fresh runtime proof of that specific old observation. |
| N5: shadow distance | Configuration retained | PC_RPAsset remains 250 m. S05 identifies inconsistent fade consumption. |
| N6: no whole-library verification | SUPERSEDED | Dedicated comparison scene and sweep now exist. Their limits are explicit here. |
| 2026-07-25 F4: absent impostors | RESOLVED | Runtime/bake pipeline exists. |
| 2026-07-25 F5: stippled handoff | PARTIAL | Complementary blue noise and TAA exist; S04 covers missing temporal motion. |
| 2026-07-25 F3: scene-lighting-only explanation | SUPERSEDED for present diagnosis | Current tier-specific lighting discrepancies invalidate that blanket explanation. |
| current.md: duplicated CPU/GPU terrain noise | SUPERSEDED for active-path risk | GPU proof compute has no current consumer found. The two implementations remain; do not unify across languages solely for deduplication. |
| current.md: duplicated shader lighting | OPEN, now concrete | S02, S03, S05 expose drift. Share only the common lighting contract; preserve intentional material differences. |
| current.md: SDFText TODO marker | OPEN | The header still contains the marker. This is separate from S24's compositing defect. |

Startup generation, water cancellation, wake feature planning, surface-provider decomposition, and other non-shader findings were not closed by this review. No historical audit files were removed.

# Questions for the User

None are required to complete the audit. Implementation choices remain open; no shader fixes were applied.

# Coverage inventory

The list below records files reviewed, including inactive and imported assets. It is a coverage record, not a per-file clean bill of health.

- `Assets/_Bench/PolyartDreamscape/PolyartStudio/SharedResources/ShaderGraph/Master Materials/Foliage/Leaves.shadergraph`
- `Assets/_Bench/PolyartDreamscape/PolyartStudio/SharedResources/ShaderGraph/Master Materials/Foliage/Trunk.shadergraph`
- `Assets/_Bench/PolyartDreamscape/PolyartStudio/SharedResources/ShaderGraph/Material Functions/MF_CheapContrast.shadersubgraph`
- `Assets/_Bench/PolyartDreamscape/PolyartStudio/SharedResources/ShaderGraph/Material Functions/WindSway.shadersubgraph`
- `Assets/_Bench/QuirkyAnimals/_Shader/SoftSurface.shader`
- `Assets/_Bench/SyntyFantasyHero/Shaders/POLYGON_CustomCharacters.shadergraph`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomGrass.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomSkybox.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomStoneCylindrical.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomStoneSpherical.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomTerrain.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomToon.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomToonNormal.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_CustomUnlit.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_ToonFire.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_Water.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_Waterfall.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_WaterParticles.shader`
- `Assets/_Bench/ToonEnchantedMeadow/Shaders/TEM_WaterRipples.shader`
- `Assets/_Bench/ToonFantasyNature/Shaders/TFF_Board_Cutout.shader`
- `Assets/_Bench/ToonFantasyNature/Shaders/TFF_CustomGrass.shader`
- `Assets/_Bench/ToonFantasyNature/Shaders/TFF_CustomToon.shader`
- `Assets/_Bench/ToonFantasyNature/Shaders/TFF_ToonFire.shader`
- `Assets/_Bench/ToonFantasyNature/Shaders/TFF_ToonWater.shader`
- `Assets/AssetPacks/Corals/__CubemapsShaders/ASE_Standart.shader`
- `Assets/AssetPacks/PolyartDreamscape/PolyartStudio/SharedResources/Shaders/Deprecated/Dreamscape_FoliageBillboard.shader`
- `Assets/AssetPacks/ToonEnchantedMeadow/Shaders/TEM_CustomVegetation.shader`
- `Assets/AssetPacks/ToonFantasyNature/Shaders/TFF_CustomToonOutline.shader`
- `Assets/AssetPacks/ToonFantasyNature/Shaders/TFF_CustomToonVegetation.shader`
- `Assets/Graphics/Shaders/Atmosphere.shader`
- `Assets/Graphics/Shaders/Cloud.shader`
- `Assets/Graphics/Shaders/CloudBlur.shader`
- `Assets/Graphics/Shaders/CloudNoise.compute`
- `Assets/Graphics/Shaders/FoliageLit.shader`
- `Assets/Graphics/Shaders/GodRayStreaks.shader`
- `Assets/Graphics/Shaders/GpuPlanetPatch.shader`
- `Assets/Graphics/Shaders/Grass.shader`
- `Assets/Graphics/Shaders/Hidden/ConsoleOverlay.shader`
- `Assets/Graphics/Shaders/Hidden/PredatorVision.shader`
- `Assets/Graphics/Shaders/Hidden/SwarmParticles.shader`
- `Assets/Graphics/Shaders/Includes/Atmosphere.hlsl`
- `Assets/Graphics/Shaders/Includes/ClimateSampling.hlsl`
- `Assets/Graphics/Shaders/Includes/CloudDensity.hlsl`
- `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl`
- `Assets/Graphics/Shaders/Includes/Common.hlsl`
- `Assets/Graphics/Shaders/Includes/DebugModes.hlsl`
- `Assets/Graphics/Shaders/Includes/GrassColor.hlsl`
- `Assets/Graphics/Shaders/Includes/GrassDither.hlsl`
- `Assets/Graphics/Shaders/Includes/GrassInteractors.hlsl`
- `Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl`
- `Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl`
- `Assets/Graphics/Shaders/Includes/Math.hlsl`
- `Assets/Graphics/Shaders/Includes/PlanetSunLighting.hlsl`
- `Assets/Graphics/Shaders/Includes/PlanetWind.hlsl`
- `Assets/Graphics/Shaders/Includes/ScatterDither.hlsl`
- `Assets/Graphics/Shaders/Includes/WaterDepth.hlsl`
- `Assets/Graphics/Shaders/Includes/WaterDisplacement.hlsl`
- `Assets/Graphics/Shaders/Includes/WaterLevelField.hlsl`
- `Assets/Graphics/Shaders/Includes/WaterLevelProjection.hlsl`
- `Assets/Graphics/Shaders/Includes/WaterVolumeData.hlsl`
- `Assets/Graphics/Shaders/Includes/WeatherCubeFace.hlsl`
- `Assets/Graphics/Shaders/Includes/WeatherLightning.hlsl`
- `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`
- `Assets/Graphics/Shaders/LoadingOverlay.shader`
- `Assets/Graphics/Shaders/Ocean.shader`
- `Assets/Graphics/Shaders/OpticalDepth.compute`
- `Assets/Graphics/Shaders/Planet.shadergraph`
- `Assets/Graphics/Shaders/PlanetVertexColor.shader`
- `Assets/Graphics/Shaders/Precipitation.shader`
- `Assets/Graphics/Shaders/PropLit.shader`
- `Assets/Graphics/Shaders/RainParticles.shader`
- `Assets/Graphics/Shaders/Scatter.shader`
- `Assets/Graphics/Shaders/ScatterImpostor.shader`
- `Assets/Graphics/Shaders/SDFText.shader`
- `Assets/Graphics/Shaders/Star.shader`
- `Assets/Graphics/Shaders/WaterVolume.shader`
- `Assets/Graphics/Shaders/WaterVolumePrepass.shader`
- `Assets/Graphics/Shaders/WeatherEvolution.compute`
- `Assets/Graphics/Shaders/WeatherParticles.shader`
- `Assets/Resources/BiomeGrassPlace.compute`
- `Assets/Resources/GpuPlanetTerrain.compute`
- `Assets/Resources/GrassNearFieldPlace.compute`
- `Assets/Resources/RainParticleUpdate.compute`
- `Assets/Resources/ScatterCull.compute`

Inventory: 47 .shader, 23 .hlsl, 8 .compute, 4 .shadergraph, 2 .shadersubgraph files.

## Review reproducibility

The normal rg inventory returned60 files. A filesystem inventory found22 additional ignored bench shader/graph files, plus2 referenced subgraphs. All were included in the review coverage described above. Includes in installed Unity packages were resolved through the package cache for contract checks.

Arithmetic examples are synthetic checks of the code formulas. They are not frame captures or hardware benchmarks. No audit finding claims a measured FPS improvement.

