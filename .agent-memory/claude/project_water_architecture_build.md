---
name: project_water_architecture_build
description: "2026-08-17/19 water redesign — plan doc, W1-W5c shipped, per-body lake levels, and the gotchas each surfaced"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b891b08-5204-4996-a855-0a2e74630e19
  modified: 2026-08-29T21:54:39.042Z
---

Branch `harvest-vertical-slice`, committed through `2549d8e`. Plan:
**`docs/design/2026-08-17-water-architecture-plan.md`**
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

## 2026-08-19: per-body lake levels, end to end

**W5 shipped.** `WaterSpillSolver` (priority-flood, own binary min-heap — Unity's profile predates
`PriorityQueue`). Every basin gets its real spill height. **The planet has ~92 lakes, not 7** — the old
global wet predicate could only see basins already below sea level. Highest sits **96.9 m above sea level**;
7.79% of water vertices are now above the sea radius.

**The one rule that explains every bug in this arc:** a consumer asking "where is sea level" globally when
it should ask "where is water HERE". Fixed in the mesh, biome (D10), scatter, grass, and the volume. When
something looks wrong near a raised lake, look for `_SeaLevelRadius` / `OceanLevel` / `SeaRadiusLocal`
first.

**Gotchas worth keeping:**
- **The level field is not the solver's `filled` array.** `filled == ground` on draining land, which is
  right for the solve and catastrophic as a wet test: the mesh samples the 38 m grid at ~half its cell
  size, so any vertex below its cell's sampled height reads submerged and *half of every hillside floods*.
  `BuildLevelField` emits a level only where water stands, dilated one ring, `NoWater` elsewhere.
- **Never hand a Burst job an unassigned `NativeArray`.** Its pointer is null and the safety check faults
  at the first memory touch — which was `Out.BeginForEachIndex`, so the stack blamed the stream writer.
  Allocate a 1-element placeholder and gate on a separate resolution field.
- **The 38 m mask grid is coarser than the mesh, so the true waterline sits INSIDE a mask cell.** Deciding
  Lake/LakeShore by mask state left dry ground inside `Water` cells reading as plain land — which is
  exactly the band reeds need. Decide by elevation vs the local level instead, in any lake-adjacent cell.
- Cube-face projection is mirrored in three places now (C# `WaterLevelGrid`, HLSL `WaterLevelField.hlsl`,
  and `WaterBodyMap`'s own indexer). A texture fetch cannot share C#, so all sites say "change one, change
  both".
- `planet.generate` is async and **`CommandExecutor.ExecuteImmediate` rejects it**; call
  `Planet.GeneratePlanetAsync` by reflection instead. Firing it before scene bootstrap finishes throws
  `Service ISeedProvider not registered`.
- Forcing daylight via `time.set-local` / `TrySetLocalTimeOfDay` frequently leaves the sun below the
  *chosen location's* horizon. Pick a lake by `dot(bodyDirection, _SunParams)` instead of fighting time.

## 2026-08-29: "lake under the lake" was the CHARACTER, not the water

Bryan's Lake1 screenshot — bed with caustics at the feet, a second water sheet higher up — was the same
one rule again, this time in gameplay code. `character.spawn` grounded the capsule at **5001.00** =
`IPlanet.LastSeaLevelRadius` (5000.15) + `FootOffset`, while lake #48's solved surface is **5041.26**. The
player stood on an invisible planet-wide sea plane inside every raised lake. Ruled out first and not worth
re-walking: a second body nearby, holes in the water mesh, dry terrain under the sea sphere, the
`FarTerrainWaterlineMask` wash, prepass kind-decode. The free camera was never affected.

Fix: `CharacterWaterFloor` (a `IWaterQueryService` + center pair) replaced the `float seaLevelRadius`
argument on `PlanetSurfaceGrounding` and `PlanetRaycastGrounding`, so the floor is queried per position.
Same swap in `PlanetCharacterController` (spawn clamp too), `CreatureResidencyService`, `AssetBenchService`
— those three all passed the global radius. Verified: charRadius **5042.26**, bodyId 48, terrain 4992.15.
267/267 EditMode tests pass, two of them new and locking this.

**Where the global sea radius is still CORRECT** and must not be swapped: altitude reporting
(`CreatureResidencyService` altitude, `ScaleReferenceMarkers`) and the AssetBench "you are over ocean"
warning. The rule is about *grounding*, not about every use of the number.

**Closed defects:** D4, D5, D6, D7, D8, D9, D10, D11. D5's re-encode is
`round(shore01*511)*4 + kind` in `Includes/WaterVolumeData.hlsl` — 9 bits shore, 4 body kinds, exact in
fp16. Signing channel R is forbidden: `Atmosphere.shader` uses `step(0.0001, forwardDepth)` as its
water-validity test.

**Still open:** W6 query service onward (buoyancy, swimming, rivers, waterfalls). `WaterVolume.shader`
keeps the analytic sphere only for pixels the water mesh never covered.

Next per §5: W6 (query service) → W7/W8 (physics, buoyancy) → W9 (swim).
Related: [[project_water_tech_research]], [[feedback_goal_first_scoping]], [[reference_unity_mcp]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Water architecture build](project_water_architecture_build.md) — 2026-08-17/19 water redesign, plan at `docs/design/2026-08-17-water-architecture-plan.md`. **W1-W5c SHIPPED: the planet now has ~92 lakes at their own spill heights, highest 96.9 m above sea level.** One rule explains every bug in the arc: a consumer asking where sea level is *globally* when it should ask where water is *here* — check `_SeaLevelRadius`/`OceanLevel`/`SeaRadiusLocal` first. Closed D4-D11. **Traps: the solver's `filled` array is NOT the wet test (floods half of every hillside); never hand Burst an unassigned `NativeArray`; the 38 m mask is coarser than the mesh so the waterline sits INSIDE a cell.** **Structural finding: `elevations[i] < OceanLevel` means a raised lake has ZERO wet vertices — per-body level needs a spill solver (W5).** SHIPPED: W1 WaterSettings→WaterDto, W2 `water.*` console, W3 `LakeMask`→**`WaterBodyMap`** (whole-sphere cross-seam flood fill + versioned `WaterBodyCatalog`), W4a shared `WaterDisplacement.hlsl` so the volume prepass finally displaces (D9 surface half), D8 reflection orphan deleted. **Gotcha: a material property SHADOWS a same-named shader global — promoting the 8 wave/freeze params meant deleting them from both `Properties` blocks.** Cube-seam adjacency measured exactly 1:1 (asymmetry 0), so no adjacency table is needed. Codex's "biome bake runs before the mask" blocker was verified FALSE.
