# ProceduralPlanets water handoff - 2026-05-22

Repo: `C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`.

Use this note to recover context for Bryan's water artifact workflow. The active workflow is evidence-led F10 debugging of underwater shoreline gaps, low-sun/atmosphere-water ownership, and water completion work in `Ocean.shader`, `WaterVolume.shader`, `WaterVolumePrepass.shader`, `WaterMeshBuilder`, `WaterVolumeRenderFeature`, `Planet`, and `FreeCameraController`.

Current uncommitted state: many files are intentionally modified from the cleanup/fix chain, including `.amazonq/rules/memory-bank/water.md`, atmosphere/ocean/planet/precipitation/water shaders, `FreeCameraController.cs`, atmosphere and precipitation render features, `Planet.cs`, `WaterMeshBuilder.cs`, `WaterVolumeRenderFeature.cs`, and a new `IPrecipitationDebugControl` interface. Do not revert unrelated edits when resuming.

Validated decisions:
- The earlier underwater glow artifacts were precipitation/debug ownership problems, not water surface problems. The correct direction was to suppress precipitation/debug contribution underwater rather than requiring manual `P`/`Y` toggles.
- Light shafts are treated architecturally as an atmosphere camera effect. They should stop/fade at water; future water work owns glints, shimmer, underwater shafts, caustics, refraction, distortion, wakes, foam, and waves.
- Underwater shoreline bleed/gaps are a water-volume coverage problem, not foam, atmosphere, precipitation, or sky. F10 repeatedly showed the issue in `Off`, `VolumeOnly`, and `VolumeOcclusion`; `FoamPink` did not identify it.
- Bryan's Scene view screenshot of the selected water mesh aligned with the gap shapes, supporting the water mesh/prepass boundary diagnosis.

Current water-volume lip design:
- `WaterMeshBuilder` generates a separate `WaterVolumeLip` mesh along wet/dry shoreline edges.
- `Planet` creates a `WaterVolumeLip` child under `Water` with only a `MeshFilter`; the visible water renderer remains the normal `Water` mesh.
- `WaterVolumeRenderFeature` draws the normal water mesh into `WaterVolumePrepass`, and can draw the lip mesh into `_WaterInterfaceTexture`.
- F10 sidecars now print `VolumeLipMesh: active=..., verts=..., tris=...`.

Recent F10 evidence:
- F10 around `20260522-175229`: `VolumeLipMesh` was active and nonzero (`33282` verts/tris), but underwater gaps still showed in `Off`, `VolumeOnly`, and `VolumeOcclusion`.
- A second `WaterVolumeLipPrepass` pass using `ZTest Always` was added to test whether terrain depth was rejecting the lip.
- Bryan then captured three F10 sets around `20260522-181748`, `20260522-181812`, and `20260522-181843`: one showed artifacts through the entire planet, one above the shore, and one underwater looking at the shore.
- The first two were above sea level (`DistanceToCenter` about `5236.62` and `5347.97`, `SeaLevelRadius` `5000.00`) while the lip mesh was active, proving the unconditional always-depth lip was causing a new above-water through-planet regression.
- The underwater set (`DistanceToCenter` about `4973.66`) still matters for the original shore-gap issue.

Current fix after those F10s:
- Keep `WaterVolumeLipPrepass` available, but do not draw it globally.
- `WaterVolumeRenderFeature` now estimates sea radius from the visible water mesh bounds and draws the relaxed lip pass only when the camera is inside that water mesh.
- Validation after this change passed: `dotnet build ProceduralPlanets.Core.csproj`, `dotnet build ProceduralPlanets.Planet.csproj`, and `git diff --check`. `git diff --check` only reported existing CRLF warnings.

Next steps:
- Rerun the same three F10 viewpoints: through-planet artifact view, above-shore view, and underwater looking at shore.
- Expected result: above-water through-planet artifacts should be gone.
- If underwater gaps remain, do not re-enable a global `ZTest Always` lip. Investigate tighter manual depth rejection, lip coverage width/data, or focused `WaterVolumeDeepDive`/`VolumeMask` diagnostics.
- Keep the next change small and validate with F10 before moving on to water completion features.
