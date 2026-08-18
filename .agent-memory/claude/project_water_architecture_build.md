---
name: project_water_architecture_build
description: 2026-08-17/18 water redesign — plan doc location, W1-W4a shipped, and the gotchas each one surfaced
metadata:
  type: project
---

Branch `harvest-vertical-slice`, uncommitted. Plan: **`docs/design/2026-08-17-water-architecture-plan.md`**
(v5 body + appendix holding four review rounds: 3 agent passes, Codex on v2 and v4). Tasks W1-W20,
defects D1-D12, build order in §5, immediate work in §9. Goal set: waves, ripples, splashes, buoyancy,
swimming, rivers, waterfalls, oceans and lakes, with caustics/reflections/foam/whitecaps/glint.

**The structural finding** (why a redesign at all): `isWet = elevations[i] < settings.OceanLevel` means a
raised basin has ZERO wet vertices. A lake above sea level is not representable. Per-body level needs a
basin/spill solver (W5), not an aggregation over the existing predicate.

## Shipped 2026-08-17/18

- **W1** `WaterSettings` SO → `WaterDto` (38 fields) via `IWorldSettingsRegistrar`. `Planet` throws if
  absent. Asset at `Assets/Game Data/Planet Settings/Water.asset`.
- **W2** `water.list` / `water.set` / `water.setcolor` / `water.reset` / `water.bodies`, reflective over
  the record's primary constructor so new DTO fields need no command change. Baked-vs-live is labelled.
- **W3** `LakeMask` → **`WaterBodyMap`** (`Assets/Scripts/Planet/Biomes/WaterBodyMap.cs`). Whole-sphere
  flood fill across cube seams (was six independent per-face fills), `ushort` body ids, versioned
  `WaterBodyCatalog`. `Sample()` keeps the old None/Water/Shore contract so consumers were a rename only.
- **W4a** `Assets/Graphics/Shaders/Includes/WaterDisplacement.hlsl` — one displacement + one freeze curve,
  included by `Ocean.shader` AND `WaterVolumePrepass.shader`. The prepass now rasterises the displaced
  surface (D9, surface half). `WaterVolume.shader` still intersects an analytic sphere — D9's third
  surface, unfixed, owned by W18/W20.
- **D8** reflection orphan removed as a unit (`LakePlanarReflection.cs`, shader branch, both globals).

## Gotchas worth keeping

- **A material property SHADOWS a shader global of the same name.** Promoting `_SwellAmplitude`,
  `_SwellWavelength`, `_WaveSpeed` + the 5 freeze params to globals required DELETING them from both
  shaders' `Properties` blocks and the `UnityPerMaterial` CBUFFER. Leave one behind and the two surfaces
  silently desync again. Names now in `ShaderGlobalIds.Water.cs`.
- **Unset global = 0, which is the safe direction here**: `_SwellAmplitude 0` is flat water,
  `_FreezingEnabled 0` is liquid. A domain reload before the first generate degrades to calm water.
- **Reading a global mid-generation reads 0 and means nothing.** Water params publish at
  `Planet.GenerateAsync` step ~431; `WaterBodyMap.Current` lands at ~417. Confirm `_isGenerating == false`
  before believing any global.
- **Codex's W3 "ordering blocker" was wrong** — I verified `BakeChunkMap` is called inside
  `ChunkedSurfaceProvider.GenerateColorsAsync` (:1397/:1472), which runs at `Planet.cs:422`, AFTER the
  mask is assigned at :417. No phase hoist was needed. Check review claims against the tree, see
  [[feedback_adversarial_review_verification]].
- **Cube-face seam adjacency is exactly 1:1** at equal per-face resolution — `SeamAsymmetryCount`
  measured **0**. So a seam-crossing neighbour needs no adjacency table: step one cell past the face edge
  in face-uv and re-classify through the same `CubeFaceToUnitSphere`/`DirectionToFaceUv` pair.
- **`LakeMaxCells = 1400` changed meaning** in W3 — it now bounds the whole body, not its per-face slice.
  A seam-straddling lake that used to split into two sub-threshold halves is now one full-size body. No
  before/after lake count was captured; that constant is the knob if lakes read sparse.
- Test world measures 10 bodies: 3 ocean (101432 / 9796 / 2404 cells), 7 lake (325 down to 2). Bodies #2
  and #4 are **inland seas** — the concrete case W5's per-body spill level must handle.

Next per §5: W4 (full data contract) → W5 (basin/spill solver) → W6 (query service).
Related: [[project_water_tech_research]], [[feedback_goal_first_scoping]], [[reference_unity_mcp]].
