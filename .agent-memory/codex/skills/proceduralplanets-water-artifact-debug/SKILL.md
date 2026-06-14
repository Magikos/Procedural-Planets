---
name: proceduralplanets-water-artifact-debug
description: Diagnose ProceduralPlanets water rendering failures when Bryan reports thin shore lines, underwater edge bleed, water drawing over terrain, or a washed-out final `Off` image. Use this for the repo's F10 WaterArtifact capture workflow, shader triage, terrain-contact checks, and the layer-first water rebuild branch.
user-invocable: false
allowed-tools:
  - Read
  - Grep
  - Bash
---

# ProceduralPlanets Water Artifact Debug

## When to use

Use this when work is happening in `C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` and the symptom is a shoreline line, underwater edge bleed, a foam band, low-horizon far-shore color showing through water, water appearing to sit on top of terrain, or the final `Off` image looking like a washed transparent sheet even though debug modes show richer water behavior.

Do not use this as a generic Unity rendering checklist. It is repo-specific to the F10 water debug modes, `Ocean.shader`, `WaterVolume.shader`, `Planet.cs`, and `WaterMeshBuilder`.

## Inputs / context to gather

1. Confirm the current symptom and viewpoint: above water, underwater, or close-up at the shoreline.
2. Check whether the latest workflow is still the targeted F10 capture set:
   - `FreeCameraController.F10CaptureSet`
   - `DebugScreenshotMaxRuns`
   - `local-only/debug-screenshots`
3. Gather the most recent F10 capture bundle and inspect which modes still show the artifact:
   - `VolumeOnly`
   - `SurfaceOnly`
   - `WaterOff`
   - `FoamParts`
   - `SurfaceAlpha`
   - `SurfaceBlend`
   - `SurfaceContact`
   - `VolumeBoundary`
   - `VolumeOptical`
   - `VolumeMask`
   - `VolumeContact`
   - `VolumeDilation`
   - `VolumeNoRefraction`
   - `VolumeOcclusion`
   - `TerrainSourcePink`
   - `FoamPink`
   - `VolumeSphere`
   - `TerrainFaceId`
   - `SeaRay`
   - `SeaVsMesh`
   - `SeaPath`
   - `SeaMatte`
   - `SeaSourceMatte`
   - `WaterNoPost`
   - `SurfaceRawOpaque`
   - `SurfaceFxProof`
4. If the current symptom is "the final `Off` view still looks wrong" rather than a single contour, compare:
   - `Off`
   - `WaterNoPost`
   - `SurfaceOnly`
   - `SurfaceRawOpaque`
   - `SurfaceFxProof`
   - latest F10 sidecar metadata for quality, FPS, mode, focus, weather, wave, and surface effects
   - whether clouds were recently fixed by quality classification rather than by water-shader work
5. Check whether recent edits touched:
   - `Ocean.shader`
   - `WaterVolume.shader`
   - `Planet.cs`
   - `WaterMeshBuilder`
   - `WaterVolumeRenderFeature.cs`
6. If the symptom is an underwater shoreline gap or a water-volume lip experiment, check:
   - `WaterVolumeLip` sidecar stats in the latest F10 `.txt`
   - whether the camera was above or below sea level
   - whether a relaxed lip prepass is gated to only run when the camera is inside the water mesh
7. Check whether the latest notes say recent F10 runs produced no visible progress.
   - If yes, plan a hard isolation test before any more tuning.
   - Gather which subsystem the next extreme/binary test is meant to prove or eliminate.
8. If the latest notes explicitly say Bryan chose to "start over" from visible render layers, treat that as the active direction and do not resume the abandoned tuning loop.

## Procedure

1. Start from the practical capture workflow.
   - Prefer `WaterArtifact` over asking Bryan to select individual modes during play.
   - Use retention-pruned screenshots in `local-only/debug-screenshots` so the latest run is easy to inspect.
2. Split the diagnosis by the modes that light up.
   - If the latest tuning pass left the `Off` image effectively unchanged, stop tuning values and isolate the source first with `VolumeOnly`, `SurfaceOnly`, and `WaterOff`.
   - If `Off` is washed out or transparent while `WaterNoPost`, `SurfaceOnly`, `SurfaceRawOpaque`, or `SurfaceFxProof` prove the shader can still generate color, detail, or effects, treat the active failure as final-stack/composite presentation rather than missing effect generation.
   - Do not respond to an unchanged result with another small constants tweak. Design a hard diagnostic line first: forced colors, forced opacity, bypassed blending/composite, disabled passes, or another extreme test that should visibly move the artifact if that branch is responsible.
   - If the line remains in `WaterOff`, investigate non-water rendering before touching water shaders again.
   - If the line is strongest in `VolumeBoundary` or `VolumeOptical`, treat it as a volume coverage/contact problem first.
   - If it tracks `FoamParts` or `SurfaceAlpha` exactly at the shoreline, treat it as a surface foam/alpha problem first.
   - If both surface and volume modes still show it, keep both paths in play and avoid overcommitting to one theory.
3. Use the newer isolation modes to confirm the active branch.
   - If `Off` and `VolumeOnly` match while `SurfaceOnly` and `WaterOff` do not, keep the fix in `WaterVolume.shader`.
   - Compare `VolumeOnly` and `VolumeNoRefraction` before assuming refraction is the cause.
   - If `TerrainSourcePink` marks the contour hot pink while `FoamPink` does not, treat the remaining artifact as terrain/source color bleeding through the volume composite rather than foam.
   - Use `VolumeOcclusion` and `VolumeSphere` when the rasterized water edge is not the whole story and you need to verify source-color suppression or analytic sea-sphere fallback coverage.
   - Use `TerrainFaceId` before changing topology again when a square or straight-edged shoreline shape suggests cube-face or per-face water classification boundaries.
4. Triage the volume path.
   - Check whether the current mask is too strict near shore.
   - If an interior gate is clipping too much above-water contribution, prefer the softer `volumeEdgeMask * volumeBodyMask` style fix over a hard interior cutoff.
   - Use `VolumeMask` to confirm whether the effective coverage still matches visible water.
   - If `VolumeContact` still leaves a bright line, check for narrow untreated source-color slivers and boundary coverage issues before revisiting foam.
   - If `VolumeDilation` does not explain the contour, test source-color paths next instead of repeatedly expanding the mask.
   - Prefer source-color suppression terms such as `sourceOcclusion`, `sourcePathOcclusion`, `sourceMatte`, and `brightSourceBleed` when the contour is really shoreline/terrain source showing through water.
   - If clipped shoreline overlap and small boundary-value changes do not move the line, stop tweaking those constants and check `TerrainFaceId` or global water-body continuity instead.
   - If a global water-graph or continuity patch regenerates the mesh but the low-horizon line remains, treat that as evidence that face-local classification was not the root cause.
5. Triage low-camera far-shore artifacts separately from close contact edges.
   - If Bryan reports the line is only visible from a low camera near the water surface while looking along the curved planet, prioritize the analytic sea-path branch over foam or simple shoreline overlap.
   - Use `SeaRay`, `SeaVsMesh`, and `SeaPath` to test whether the camera ray is behind the sea-level sphere and whether the curved sea path is reaching the visible contour.
   - If `SeaRay` lights the visible contour but `SeaVsMesh` or `SeaPath` stays weak there, the analytic/raster coverage gate is too strict.
   - If `SeaSourceMatte` lights a broad magenta/green candidate region but the normal `Off` image still keeps the line, stop stacking matte, opacity, or transmittance tweaks in `WaterVolume.shader` and pivot to water-volume coverage or geometry.
6. Route washed-out final-stack failures into the layer-first rebuild branch.
   - If the latest note says to "start over" and the debug bundle proves effects exist but the production `Off` view is still wrong, stop the current polish loop.
   - Add or use a hard `BottomDistortionOnly` debug/proof mode first.
   - Suppress blue surface color, foam, wakes, glint, and top-wave styling while proving only shallow-water bottom distortion/refraction/caustic-like movement where terrain sits under the water mask.
   - Keep this first layer in `WaterVolume.shader` / the refraction path unless new evidence disproves that ownership.
   - Only after bottom distortion is unmistakable in normal view should the stack add: base water tint/depth transparency, then top-surface normals/ripples, then foam/shore wash/wakes one by one, and glint last.
   - If any layer is visible only in proof/debug mode and disappears in normal `Off` view, stop the sequence there and debug that layer only.
7. Triage the surface path.
   - Inspect shoreline foam width, `WaterShoreFoamDepth`, and any `edgeClear` or `ShoreContactVisibility` logic.
   - If `SurfaceBlend` shows a broad grazing-alpha band while `FoamParts` is mostly clean, reduce grazing reflection/fresnel contribution before changing foam again.
   - If close-up captures show the visible edge on the exact terrain-water intersection, move the fade toward raw terrain-contact measurements rather than only stylized shoreline masks.
8. Check terrain-contact behavior explicitly.
   - Use `SurfaceContact` mode when available.
   - Prefer raw water-surface-to-opaque-scene gap and low-`shore01` contact logic when the water appears drawn on top of terrain at shallow angles.
9. If shoreline gaps are visible after reducing the artifact, review mesh overlap and lip coverage.
   - A small under-terrain overlap in `WaterMeshBuilder` is acceptable when terrain depth occludes it and it hides seams.
   - If the workflow is using `WaterVolumeLip`, confirm whether the relaxed lip prepass is being drawn only when the camera is inside the water mesh.
   - Use the F10 sidecar `VolumeLipMesh: active=..., verts=..., tris=...` line to confirm that the lip path is really active before blaming shader math.
   - Do not leave a `ZTest Always` lip pass enabled globally; above-water cameras can see through-planet artifacts even when the underwater view improves.
10. Verify in the right order.
   - Run the targeted `.csproj` builds for code health.
   - Then reimport shaders in Unity and regenerate any dependent planet/water data before judging the visual result.

## Efficiency plan

- Read memory first: search `MEMORY.md` for `WaterArtifact`, `SurfaceContact`, `VolumeBoundary`, or `sheet/shelf`.
- Inspect the newest F10 bundle before changing code again.
- Use the modes that still light up to narrow the next edit instead of rereading every shader path.
- When a pass leaves `Off` unchanged, switch immediately to binary isolation and source-color confirmation instead of making another constant tweak.
- When `Off` looks washed out but `SurfaceRawOpaque` or `SurfaceFxProof` still looks promising, skip more polish tuning and route directly into the layer-first rebuild branch.
- Prefer an extreme test that can clearly falsify a branch over incremental knob changes; if the extreme test does not move the artifact, leave that branch quickly.
- Reuse the layer order instead of reinventing it: bottom distortion first, then tint/depth body, then normals/ripples, then foam/wakes, then glint.
- When the low-horizon contour survives `SeaSourceMatte`, stop exploring more production matte tuning and switch to coverage/geometry hypotheses.
- Stop exploring a pure volume theory once close-up `FoamParts` and `SurfaceAlpha` prove the visible edge is surface-local.
- Stop exploring foam if `TerrainSourcePink` marks the contour and `FoamPink` does not.
- Stop exploring cube-face continuity once the mesh regenerates with the expected vertex-count change but the visible line is unchanged.
- Stop after code-only verification if Unity has not reimported the shaders yet; visual conclusions are low confidence until then.

## Pitfalls and fixes

- Symptom: too many screenshots to review.
  - Likely cause: using the full mode loop instead of the targeted capture set.
  - Fix: switch to `WaterArtifact` and rely on retention pruning.
- Symptom: underwater edge improves but above-water water color turns into a shelf.
  - Likely cause: the volume mask is too aggressive near shore.
  - Fix: soften the volume gate and compare `VolumeMask` against real above-water views.
- Symptom: volume debug still shows activity after a surface fix.
  - Likely cause: shared shoreline data is still feeding the volume diagnostics.
  - Fix: prioritize the modes that match the visible artifact, especially close-up `FoamParts` and `SurfaceAlpha`.
- Symptom: repeated tuning changes debug values but the visible `Off` image stays the same.
  - Likely cause: the source has not been isolated yet.
  - Fix: compare `Off`, `VolumeOnly`, `SurfaceOnly`, and `WaterOff` before making more shader-value adjustments, then use a hard isolation test such as forced opacity, forced colors, bypassed blending/composite, or disabled passes to prove or eliminate the suspected branch.
- Symptom: `Off` looks like a washed transparent sheet, but `WaterNoPost`, `SurfaceOnly`, `SurfaceRawOpaque`, or `SurfaceFxProof` still show strong raw water behavior.
  - Likely cause: the production water stack/composite is mispresenting effects that are already being generated.
  - Fix: stop polishing the full stack; rebuild from an isolated `BottomDistortionOnly` layer in normal view, then add tint/depth, normals/ripples, foam/wakes, and glint in that order.
- Symptom: multiple F10 runs show no visible progress even after trying different constants.
  - Likely cause: tuning started before the responsible subsystem was identified.
  - Fix: separate fault-finding from tuning; redesign the next pass around a binary/extreme test and move to another subsystem if that test does not visibly change the artifact.
- Symptom: a new water layer only looks correct in a proof/debug mode and vanishes in normal `Off` view.
  - Likely cause: the layer is not yet integrated strongly enough into the production stack.
  - Fix: stop the rebuild at that layer and debug it in isolation instead of adding more upper layers.
- Symptom: a square or straight shoreline shape shows up most clearly in `VolumeMask`.
  - Likely cause: cube-face or per-face water classification boundaries.
  - Fix: compare against `TerrainFaceId` before changing foam or matte behavior again.
- Symptom: the contour survives volume-contact fading and dilation.
  - Likely cause: bright shoreline/terrain source color is still bleeding through the full-screen volume composite.
  - Fix: use `VolumeNoRefraction`, `VolumeOcclusion`, `TerrainSourcePink`, `FoamPink`, and `VolumeSphere` to confirm source-color bleed and keep the fix in `WaterVolume.shader`.
- Symptom: the contour is only visible from a low near-surface camera looking along the planet.
  - Likely cause: curved sea-path coverage, analytic sea occlusion, or horizon contact coverage is too weak.
  - Fix: inspect `SeaRay`, `SeaVsMesh`, and `SeaPath` before changing foam or mesh overlap again.
- Symptom: `SeaSourceMatte` clearly marks the contour but `Off` still looks unchanged.
  - Likely cause: the shader can classify the region, but the real problem is water coverage/geometry rather than another matte threshold.
  - Fix: pivot to a feathered coverage band, analytic sea-sphere coverage independent of the raster edge, or mesh/prepass shoreline overlap.
- Symptom: underwater gap experiments help underwater views but create through-planet or above-water artifacts.
  - Likely cause: a relaxed lip prepass is being drawn globally.
  - Fix: gate the relaxed lip pass so it only runs when the camera is inside the water mesh; do not re-enable a global `ZTest Always` lip.
- Symptom: builds pass but the scene still looks unchanged.
  - Likely cause: Unity has not reimported the shaders or regenerated the planet/water data.
  - Fix: reimport first, then reassess.

## Verification checklist

- The latest F10 capture bundle is from the targeted `WaterArtifact` workflow.
- The active diagnosis names which modes still show the artifact.
- If the failure is a washed-out final `Off` view, the comparison includes `WaterNoPost`, `SurfaceOnly`, `SurfaceRawOpaque`, and `SurfaceFxProof` before more polishing is proposed.
- If the result is still ambiguous, the capture bundle includes `VolumeOnly`, `SurfaceOnly`, and `WaterOff` evidence before another tuning pass is proposed.
- If recent passes showed no visible progress, the next proposed step is a hard isolation test rather than another small shader-value adjustment.
- If the layer-first rebuild branch is active, the current layer is visible in normal view before the next layer is added.
- If the result is a low-horizon contour, the capture bundle includes `SeaRay`, `SeaVsMesh`, `SeaPath`, or `SeaSourceMatte` before another matte tweak is proposed.
- If a volume-side change was made, `VolumeMask` or `VolumeBoundary` changed in the expected direction.
- If a surface-side change was made, `FoamParts`, `SurfaceAlpha`, or `SurfaceContact` changed in the expected direction.
- If source-color bleed is the working theory, `TerrainSourcePink` versus `FoamPink` or `VolumeOnly` versus `VolumeNoRefraction` is checked before the theory is treated as confirmed.
- If a continuity or lip-coverage change was made, the latest sidecar stats confirm the regenerated mesh or lip path is active before the result is interpreted.
- `dotnet build ProceduralPlanets.Core.csproj` passes.
- `dotnet build ProceduralPlanets.Planet.csproj` passes.
- Any remaining `Assembly-CSharp.csproj` failure is checked against the known Shapes-source issue before treating it as a regression.
- Unity shader reimport and any required planet/water regeneration have happened before the visual result is declared fixed.
