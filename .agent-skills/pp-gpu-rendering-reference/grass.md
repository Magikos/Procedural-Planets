# GPU Grass — placement, indirect draw, and the blade shader

Part of `pp-gpu-rendering-reference`. Verified against the working tree 2026-07-06.
Primary files: `Assets/Resources/GrassNearFieldPlace.compute` (camera-centered placement),
`Assets/Resources/BiomeGrassPlace.compute` (per-chunk placement),
`Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl` +
`GrassPlacementParamBlend.hlsl` (shared placement structs/hashes/biome blend),
`Assets/Graphics/Shaders/Grass.shader` (procedural blade geometry + lighting),
`Assets/Graphics/Shaders/Includes/GrassColor.hlsl` / `GrassDither.hlsl` / `GrassInteractors.hlsl`,
`Assets/Scripts/Planet/Grass/GrassNearFieldController.cs`,
`Assets/Scripts/Planet/Grass/GrassPlacementController.cs` + `GrassChunkDispatcher.cs` + `GrassChunkRuntime.cs`,
`Assets/Scripts/Planet/PlanetGrassCoordinator.cs` (layer flags + altitude gates).

There is **no grass mesh asset anywhere**. A compute shader writes `GrassBladeInstance`
structs (48 bytes: root+height, up+width, color — `GrassPlacementCommon.hlsl`) into a
`GraphicsBuffer` and bumps the instance count in an indirect-args buffer;
`Grass.shader` then builds every vertex from `SV_VertexID`/`SV_InstanceID`.

## 1. The three-layer LOD state (as of 2026-07-06)

`PlanetGrassCoordinator` owns three layers behind enable flags:

| Layer | Implementation | Flag | State 2026-07-06 |
|---|---|---|---|
| Near field | `GrassNearFieldController` + `GrassNearFieldPlace.compute`, camera-centered | `_nearFieldGrassEnabled` | **true (live)** |
| Chunk | `GrassPlacementController` + `BiomeGrassPlace.compute`, per resident terrain chunk | `_chunkGrassEnabled = false` | **off** |
| Blanket | terrain-material paint pass (`_GrassFarOverlayStrength` on the terrain material) | `_grassBlanketEnabled = false` | **off** |

Consequence: beyond the near-field draw distance there is bare terrain. The far-field
story is an open DECISION owned by `pp-visual-migration-campaign` — do not "fix" it by
flipping flags. `grass.*` console commands toggle layers at runtime
(`SetGrassLayerEnabled`).

Near-field lifecycle is altitude-gated (`DefaultGrassQualitySettings`,
`Assets/Scripts/Core/QualityController.cs`): created below **500 m** camera altitude,
disposed above **550 m** (hysteresis), with blade alpha fading to zero over
**350→500 m** (`SetAltitudeFade`) so create/dispose never pops. Distance band:
**144 m full density, 200 m draw distance** (`NearFieldFullDensityDistance` /
`NearFieldDrawDistance` — the single source of truth; the blade shader's perceptual
ramps below all derive from these two numbers).

## 2. Near-field placement (`GrassNearFieldPlace.compute`, kernel `PlaceAndCullNearField`)

One thread per **stable face-space cell** (8×8 groups). The controller
(`FaceSpaceCellRangeBuilder.BuildRanges`, see [cube-sphere.md](cube-sphere.md)) turns the
camera's position into 1–5 `(face, cell-rect)` ranges using Pair-C cube-face math, snapped
to 4 m pages so sub-cell camera motion doesn't re-dispatch. Cell width is fixed at
construction (`spacing 0.35 m / (2 × planetRadius × worldScale)`), so the same
`(face, cellU, cellV)` is the same world spot forever:

```hlsl
// Stable face-space cell. Hash binds (face, cellU, cellV) to a deterministic seed,
// so the same world position yields the same blade properties across frames. THIS is
// what makes the grass attached to the planet instead of swimming with the camera.
uint cellHash = HashUint((uint)_NearFieldSeed
    ^ ((uint)_NearFieldFaceIndex * 0x9e3779b9u)
    ^ ((uint)cellIndex.x * 73856093u)
    ^ ((uint)cellIndex.y * 19349663u));
```

Every probabilistic decision below is `Hash01(cellHash ^ constant)` — deterministic,
per-cell, re-derivable. The root position comes from sub-cell jitter → face UV →
`CubeFaceToUnitSphere` → **bilinear sample of the per-face surface-radius atlas**
(`SampleSurfaceRadius`, 4-texel manual bilinear) so blades sit exactly on the terrain.

### Rejection gates, in order, each with a stat counter

| Gate | Counter (`_NearFieldStats` index) |
|---|---|
| Cell outside face UV square | `NF_STAT_FACE_AREA_REJECTED` (8) |
| Cube-face area keep (`CubeFaceAreaKeep`, `(1+u²+v²)^(-3/2)`) | `NF_STAT_FACE_AREA_REJECTED` |
| Path/scorch mask (`SamplePathMask` = max(surface-state reject, path wear)) | `NF_STAT_DENSITY_REJECTED` (1) |
| Hard distance cut at `_NearFieldDrawDistance` | `NF_STAT_DISTANCE_REJECTED` (4) |
| Stochastic distance thinning over the fade band | `NF_STAT_DISTANCE_FADE_REJECTED` (5) |
| Biome density ≤ 0.001, or zero height/width | `NF_STAT_DENSITY_REJECTED` |
| Water clearance (`surfaceRadius ≤ waterRadius + biome clearance`) | `NF_STAT_WATER_REJECTED` (2) |
| Slope vs biome max-slope with smoothstep fade | `NF_STAT_SLOPE_REJECTED` (3) |
| Per-blade density roll (partial-density biomes) | `NF_STAT_DENSITY_REJECTED` |
| Range quota exceeded | `NF_STAT_RANGE_BUDGET_REJECTED` (10) |
| Instance buffer full | `NF_STAT_OVERFLOW` (9) |

`NF_STAT_CANDIDATE_CELLS` (0) counts every thread; `NF_STAT_EMITTED` (7) counts
survivors. Counters read back async via `AsyncGPUReadback` and surface through
`GetGrassNearFieldStats` — these are the F10/console evidence for placement debugging
(`pp-diagnostics-and-tooling`). Note `NF_STAT_FRUSTUM_REJECTED` (6) exists but the
near-field kernel has no frustum gate — only `BiomeGrassPlace` culls by frustum.

Path wear thins **probabilistically** and shortens survivors (`height *= grassScale`),
so the grass boundary dithers along the worn shape instead of following the placement
grid — same pattern in both computes.

### Slot claim + rollback (the indirect-draw contract)

**Indirect draw** = the GPU draws N instances where N lives in a GPU buffer
(`GraphicsBuffer.Target.IndirectArguments`), never read back for the draw itself. The
compute *is* the count author:

```hlsl
uint slot;
InterlockedAdd(_NearFieldDrawArgs[1], 1u, slot);
if (slot >= (uint)_NearFieldCapacity)
{
    AddStat(NF_STAT_OVERFLOW, 1u);
    // Roll back so the indirect args count stays accurate (capacity-clamped).
    InterlockedAdd(_NearFieldDrawArgs[1], 0xFFFFFFFFu);
    return;
}
```

`_DrawArgs[1]` is `instanceCount` in the standard 4-uint indirect layout
(`vertexCountPerInstance, instanceCount, startVertex, startInstance` — reset each
dispatch in `GrassNearFieldController.ResetArgsAndStats` with
`args[0] = GrassChunkRuntime.BladeVertexCount`). Adding `0xFFFFFFFF` is an unsigned −1:
losers of the capacity race undo their increment so the count never exceeds what was
written. **Both computes have this rollback** — `BiomeGrassPlace.compute` line ~327
does the identical `InterlockedAdd(_GrassDrawArgs[1], 0xFFFFFFFFu)` then quits the whole
lane. No `CopyCount`, no CPU sync. Draw call
(`GrassNearFieldController.cs:522`, `GrassChunkRuntime.cs:140`):

```csharp
Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _argsBuffer, 1, 0);
```

Additionally, each face range claims from a per-range quota
(`_NearFieldRangeCounts[_NearFieldRangeIndex]` vs `_NearFieldRangeBudget`) before
touching the shared args — budgets are computed CPU-side to sum exactly to capacity
(`BuildRangeBudgets`), so a big primary-face dispatch cannot starve a narrow seam-strip.
Capacity: 1,000,000 instances ≈ 48 MB (`DefaultCapacityInstances`).

## 3. Chunk placement (`BiomeGrassPlace.compute`, kernel `PlaceAndCull`)

Same include, same gates, different iteration shape: one thread per **lane** (a texel of
the chunk's biome map, `_LaneResolution = PlanetChunkTextures.BiomeMapResolution`), and an
inner loop emitting up to `_MaxBladesPerLane` (quality default 24) blades with sub-lane
jitter (`_LaneJitterMagnitude = 1.1` — deliberately > 1 so adjacent lanes' blades overlap
visually). Expensive samples (biome params, radius, normal, slope, water) happen **once
per lane**; per-blade rolls (density, inner fade, slope keep, path mask at the jittered
position) are hash-only. Stats split into `*_LANES` (0–9) and `*_BLADES` (10–15)
counters. Extra gates the near field lacks: `_GrassDensityMultiplier`, lane frustum test
against `_CameraFrustumPlanes` (`STAT_FRUSTUM_REJECTED_LANES`), and an inner fade
(`_ChunkInnerFadeStart/End`) that removes chunk blades where the near field owns
coverage. `GrassPlacementController` keeps one `GrassChunkRuntime` (blade buffer from a
pool + args + stats) per resident chunk and re-dispatches when the camera moves > 25 m.
Layer disabled as of 2026-07-06, but the code path is maintained.

## 4. Biome parameters: `BlendGrassParams` and the categorical-id fix

Biome atlases store, per texel, up to 4 biome **ids** (RGBA × 255) and 4 **weights**.
Ids are categorical — you cannot bilinearly filter them. So both computes do a manual
4-corner blend (`GrassPlacementBilinearTexels` → `BlendGrassParamCorners`): evaluate the
top-K blend **per corner texel** (`BlendGrassParams`), then bilinear the four *results*.
This matches the terrain shader's blend, so grass density/color agrees with the ground
it stands on.

Inside `BlendGrassParams` (`GrassPlacementParamBlend.hlsl`):
`density += grassDensity * pow(weight, blendPower)` — per-biome `blendPower`
(`Placement.w`) shapes how fast a biome's grass fades across a boundary — while
height/width/tint average with `weight × grassDensity` so grassless biomes don't drag
parameters. `BiomeGrassParams` fields (see struct comments): `Shape` = density, height,
width, clump strength; `Placement` = maxSlopeDeg, slopeFadeDeg, waterClearance,
blendPower; `Tint` + `TintDry`/`TintLush` — the dry↔lush multipliers are lerped by the
climate map's moisture channel (`SampleClimateMoisture`, `_ClimateMap.g`), giving
moisture-graded grass color at zero per-frame cost.

## 5. Blade geometry (`Grass.shader`, vertex stage)

Each instance is a **tuft of 3 blades**; each blade is 3 segments × 6 vertices (two
triangles per segment) = `TUFT_BLADE_VERTEX_COUNT 18`, so 54 vertices per instance
(`GrassChunkRuntime.BladeVertexCount`). From `vertexID` the shader derives
(tuft, segment, side) and builds a tapered ribbon along the instance's `up` with
per-blade yaw, lean (`t²` bend), lateral curl, and root jitter inside the tuft spread.

Determinism is **position-hash only** (`BladeSeed`) — the code comment records the
incident: the near field re-dispatches every ~4 m page shift and the same world cell
lands at a different `instanceID` each dispatch, so mixing `instanceID` into the seed
re-rolled every blade's yaw/height/color as the camera moved ("the whole field
redrawing"). Hash `rootWS` + `tuftIndex`, never `instanceID`.

Perceptual distance ramps (all driven off the 144/200 band by the controller):

- **Width inflation** ×1.42 over `_GrassWidthInflateStart.._End` — preserves projected
  coverage as placement thins, cheaper than more roots.
- **Billboard turn**: blades rotate up to 78% toward camera-facing over
  `_GrassBillboardStart.._End`.
- **Cluster cards**: past a per-root staggered threshold (`_GrassClusterStartDistance..
  _GrassClusterEndDistance`, hashed per root so the handoff can't form a camera-centered
  ring), the ribbon widens ×3.4 and `clusterMode` flips; the **fragment** stage then
  carves `CLUSTER_BLADE_COLUMNS 5` fake blades out of the card by `clip()` on hashed
  per-column height/lean/width (`GrassChunkRuntime.VisualBladesPerInstance = 15` visual
  blades per instance). Close grass must remain real geometry because the camera can
  enter it; `_GrassGeometryMode` (0 blades / 1 auto / 2 cards) forces either mode for
  debugging.
- **Canopy color handoff**: blade albedo lerps to `GrassCanopyAlbedo(blade.Color.rgb)`
  over `_GrassCanopyColorStart.._End`. `GrassColor.hlsl` is the single source for that
  color (`GRASS_CANOPY_ALBEDO_SCALE 0.76`) — the terrain grass overlay paints with the
  same function so the 3D canopy and painted ground meet at one brightness. Change it in
  one place or the far blend seams.

### Wind (`ComputeWindOffset`)

Wind globals (`_WindDirection`, `_WindSpeedMps`, `_GameTime`) are declared by
`CloudShadows.hlsl` and populated by the weather side — grass shares the exact wind the
clouds advect with. The model is force-vs-stiffness:
`bend01 = windForce / (windForce + GRASS_WIND_STIFFNESS)` (stiffness 6.0 = wind speed at
half bend), giving a **steady lean** (0.7 × bend01) plus a wiggle whose envelope
`bend01 × (1 − 0.55 × bend01)` peaks at moderate wind and tapers as the blade pins over
— a gale is a held bend with a shiver, not a wide sway. The travelling gust
`sin(_GameTime · ω − dot(relRoot, windTangent) · 0.18)` is phased on planet-relative
position (precision-safe at large world coordinates) and propagates along +wind. All
offsets are projected to the tangent plane and scaled `t²` so roots stay planted.
Displacement clamps to `[−0.12, 0.72] × height`.

### Interactors (`GrassInteractors.hlsl`)

`_GrassInteractors` is a buffer of up to 8 (`GrassInteractorRegistry.MaxInteractors`)
position+radius+strength entries packed by C# each frame (debug sphere today; player /
projectiles / animals later). `SampleGrassInteractorBend` sums tangent-plane push-away
falloffs, caps total magnitude at `maxBend` (0.85 × height at the call site), applied
`t²` tip-weighted. Returns 0 when count is 0; count is defensively clamped 0..8 in HLSL.

## 6. Blade lighting (fragment) and the CloudShadowFactor cost note

The fragment path (`GrassFragment`): planet-space sun from `PlanetSunLighting.hlsl`,
double-sided wrap diffuse using `abs(dot(normalWS, sunDir))` — ribbons are two-sided, a
signed diffuse would flip when the camera crosses a blade plane (comment records this) —
plus a backlit rim (`pow(dot(view, −sun), 3)` gated to low sun), a desaturated
night path, and fog. **`CloudShadowFactor(positionWS, sunDir, localSun)` runs
per-fragment** (`Grass.shader` line ~357). That is 3 shape-FBM 3D texture samples per
grass pixel (see [clouds.md](clouds.md) §7) — the single most expensive term in the
shader, and the grass migration plan's Phase 1 moves it to the vertex stage. Don't do
that move independently of the campaign.

### IGN dither LOD fade (`GrassDither.hlsl`)

**LOD** (level of detail) fades here are alpha-free: the fragment does
`clip(fadeAlpha − SampleGrassDither(positionCS.xy) − 0.001)` where the dither is
**interleaved gradient noise** (IGN, Jimenez 2014) — a one-line screen-space hash whose
threshold distribution is effectively continuous. The include comment records why: the
previous 3×3 Bayer matrix had 9 discrete levels that quantized the fade band into
visible step arcs. IGN takes no time/camera input, so the stipple is stable frame to
frame (no shimmer). `fadeAlpha` combines the near/far visual fade bands and the
controller's altitude fade (`_GrassChunkFade`); geometry also shrinks with the same fade
(`geometryFade`) so faded blades get smaller, dimmer, *and* stippled.

Draw state: `Queue = Transparent-10, ZWrite On, Cull Off` — grass draws right after the
water-volume composite and writes depth so the ocean surface depth-tests against blades
(pass-order details in [SKILL.md](SKILL.md)). Shadow casting is off in `RenderParams`;
`receiveShadows = true`.

## Provenance and maintenance

```
# Layer flags + altitude gates
grep -n "_chunkGrassEnabled\|_grassBlanketEnabled\|NearFieldActivationAltitude" Assets/Scripts/Planet/PlanetGrassCoordinator.cs Assets/Scripts/Core/QualityController.cs
# Distance band (144/200)
grep -n "NearFieldFullDensityDistance\|NearFieldDrawDistance" Assets/Scripts/Core/QualityController.cs
# Slot claim + rollback in BOTH computes
grep -n "0xFFFFFFFFu" Assets/Resources/GrassNearFieldPlace.compute Assets/Resources/BiomeGrassPlace.compute
# Indirect draw call sites
grep -rn "RenderPrimitivesIndirect" Assets/Scripts/Planet/Grass
# Stat counter tables
grep -n "define NF_STAT\|define STAT_" Assets/Resources/GrassNearFieldPlace.compute Assets/Resources/BiomeGrassPlace.compute
# Blade/tuft/card constants
grep -n "TUFT_BLADE\|CLUSTER_BLADE_COLUMNS" Assets/Graphics/Shaders/Grass.shader
grep -n "VisualBladesPerInstance\|BladeVertexCount" Assets/Scripts/Planet/Grass/GrassChunkRuntime.cs
# Per-fragment cloud shadow (migration Phase 1 target)
grep -n "CloudShadowFactor" Assets/Graphics/Shaders/Grass.shader
# Interactor cap
grep -n "MaxInteractors" Assets/Scripts/Planet/Grass/GrassInteractorRegistry.cs
```

The instanceID-reroll and Bayer-quantization incidents are restated from code comments
in `Grass.shader` (`BladeSeed`) and `GrassDither.hlsl` — the comments are the primary
record; this file just makes them findable.
