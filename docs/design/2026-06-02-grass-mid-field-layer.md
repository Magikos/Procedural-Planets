# 2026-06-02 — Grass Mid-Field Layer + Three-Layer LOD Stack (Slice 4 Design)

**Status:** Design proposal awaiting approval. Author: Claude Code (Opus 4.7). Reviewer: Codex + Bryan.

**Discussion thread:** [docs/agent-conversation/2026-06-02-grass-lighting-midfield-feedback.md](../agent-conversation/2026-06-02-grass-lighting-midfield-feedback.md)

## Goals

Land the missing "geometric distant grass" band between near-field blades (currently 0-120m) and the painted terrain blanket overlay (currently 65-160m). Distant grassy hills currently read as flat tint until the camera gets close enough that near-field blades pop in. Bridge that with **camera-facing impostor cards** at coarser spacing, faded against both neighbors.

Three concrete goals:

1. Distant hills read as geometric grass at ~80-300m, not just tinted terrain.
2. No visible seam between near-field blades and mid-field cards (dither overlap).
3. No visible cube-face seams in the mid-field disc (multi-face dispatch from day one).

## Non-goals

- Wind animation (slice 5).
- Character/entity interactor bend (slice 6, but architectural hooks reserved here).
- Path cutting / building foundations (slice 7, but `_SurfaceStateMask` sampling baked in).
- Tree/foliage rendering (out of scope; separate system).
- Performance below console-tier GPUs (PC default tier first; quality knobs later).

## Architecture: Three Rendering Paths

```text
viewDistance →
  ┌──────────────────────────────────────────────────────────────────┐
  │ Far terrain coverage (PlanetVertexColor.shader overlay)         │
  │   altitude-driven intensity; ~150m → infinity                    │
  ├──────────────────────────────────────────────────────────────────┤
  │ Mid-field impostor cards (NEW: GrassMidField.shader + compute)  │
  │   stable face-space cells; ~80-300m; clump-per-cell             │
  ├──────────────────────────────────────────────────────────────────┤
  │ Near-field blades (existing: Grass.shader + GrassNearFieldPlace)│
  │   stable face-space cells; ~0-100m; per-blade tuft              │
  └──────────────────────────────────────────────────────────────────┘
```

Note: the old chunk-grass path (`GrassPlacementController` + `BiomeGrassPlace.compute`) stays alive behind a toggle until F10 validates that mid + near + blanket replace it visually. **Cleanup deferred to post-slice-4.**

## Shared Concepts

These are extracted/created in **Slice 4a** so all three rendering paths consume the same source of truth before slice 4b/4c add the third call site.

### Shared HLSL include: `Assets/Graphics/Shaders/Includes/PlanetSunLighting.hlsl`

Single source for day/night lighting math. Currently duplicated between [Grass.shader](../../Assets/Graphics/Shaders/Grass.shader) (analytic grass lighting) and [PlanetVertexColor.shader](../../Assets/Graphics/Shaders/PlanetVertexColor.shader) (terrain analytic lighting). Mid-field would make it three. Extract to one place.

Exposes (HLSL):

```hlsl
float3 _SunParams;            // already global
float _NightAmbientIntensity; // already global
float3 _PlanetCenter;         // already global

// Compute the planet-relative day/night gate.
// localSun = dot(planetNormal, sunDir); daylight = smoothstep(-0.08, 0.18, localSun).
struct PlanetSunInfo
{
    float3 planetNormal;
    float3 sunDir;
    float  localSun;
    float  daylight;
    float  nightSide;        // 1 - daylight
    float  horizonFactor;    // saturate(1 - abs(localSun)*3) — peaks at terminator
};

PlanetSunInfo SamplePlanetSun(float3 positionWS);

// Day color: surface diffuse + ambient lerp, matches terrain.
float3 ApplyDayLighting(float3 albedo, float3 normalWS, PlanetSunInfo info);

// Cool night palette: lerp(albedo, cool blue, 0.65) * _NightAmbientIntensity * 0.65
float3 ApplyNightLighting(float3 albedo, PlanetSunInfo info);

// Backlight/translucency term (gated by daylight * horizonFactor); call only for thin
// foliage (grass blades, cards). Caller adds it into the day color.
float3 ApplyBacklight(float3 albedo, float3 viewDirWS, float strength, PlanetSunInfo info);
```

Both `Grass.shader` and the terrain overlay's grass-tint mix get refactored to call these in slice 4a. Mid-field shader uses them in slice 4c.

### Shared HLSL include: `Assets/Graphics/Shaders/Includes/GrassCoverage.hlsl`

Single function for the "vegetation coverage" environmental signal. Currently the chunk and near-field compute kernels each call `BlendGrassParams` directly; the terrain overlay has its own `SampleGrassOverlayParams`. They all do roughly the same biome × slope × water blend.

Coverage is a **scalar in [0,1] plus a blended `BiomeGrassParams`**:

```hlsl
struct VegetationCoverage
{
    float          density;        // [0,1] biome-blended grass density (after slope/water gates)
    float          slopeKeep;      // [0,1] slope falloff factor
    float          waterKeep;      // [0,1] water clearance factor
    BiomeGrassParams blendedBiome; // blended Shape/Placement/Tint across top-K biome weights
};

// Same 4-corner bilinear biome sample used by terrain + grass today, hoisted to one
// implementation so all three paths agree at biome transitions.
VegetationCoverage SampleVegetationCoverage(float2 faceUv, int faceIndex);
```

Each layer maps coverage to its own primitive:

- **Near-field**: per-blade emission gated by `density * slopeKeep * waterKeep` (already does this).
- **Mid-field**: per-cell impostor card emission gated by the same product. Card tint = `blendedBiome.Tint`.
- **Far blanket**: terrain albedo lerped by `density * slopeKeep * waterKeep * farDistanceMask` toward grass tint.

### Shared C# library: `Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs`

Extracts the cell-range math currently inlined in `GrassNearFieldController.Tick` and makes it reusable + multi-face.

**API:**

```csharp
public struct FaceSpaceCell
{
    public int FaceIndex;
    public Vector2Int PageOriginCellUV;
    public Vector2Int GridSize;
    public float CellUvWidth;
}

public static class FaceSpaceCellRangeBuilder
{
    /// Computes the set of face-space cell ranges (1-3 entries: primary face plus optional
    /// adjacent faces for seam straddling) that cover a disc of `worldRadius` around the
    /// camera's surface anchor, snapped to `pageSize` world-equivalent pages.
    public static int BuildRanges(
        Camera camera,
        Transform planetTransform,
        float planetRadius,
        float worldSpacing,
        float worldRadius,
        float pageSizeMeters,
        Span<FaceSpaceCell> outRanges);   // returns count actually written
}
```

**Multi-face behavior:** when the camera surface anchor's `faceUv` is within `worldRadius / metersPerUV` of any face edge, the builder emits additional ranges for the adjacent face(s) covering the overlap region. Corner straddling (3 faces) is handled by emitting 3 entries.

**Used by:**
- `GrassNearFieldController` (slice 4c retrofit — eliminates current `SeamRisk` limitation as a bonus)
- `GrassMidFieldController` (new in slice 4c)
- Potentially future renderers that need camera-centered face-space dispatch

### Shared `StructuredBuffer<GrassInteractor>` (architectural reservation, populated in slice 6)

Hooks reserved in slice 4 even though the runtime side ships later. Cost is ~5 lines per shader, zero perf when count = 0.

```hlsl
// In PlanetSunLighting.hlsl or new Includes/GrassInteractors.hlsl
struct GrassInteractor
{
    float4 PositionRadius;  // xyz = world position, w = radius
    float4 StrengthType;    // x = strength [0,1], y = type (0=transient bend, 1=persistent), z/w reserved
};

StructuredBuffer<GrassInteractor> _GrassInteractors;
int _GrassInteractorCount;     // 0 = no interactors (skip the loop)

// Returns world-space bend vector to add to blade tip / card lean.
// Empty stub for slice 4 — implementation lands in slice 6.
float3 SampleGrassInteractorBend(float3 rootWs);
```

All three grass shaders call `SampleGrassInteractorBend` in their vertex shader near where blade tips are computed. The function returns `float3(0,0,0)` until slice 6 wires up the real implementation. No shader rewrite needed when interactors ship.

### Path-cut / state-mask sampling (latent bug fix)

**Currently:** `BiomeGrassPlace.compute` (chunk path) consults `_SurfaceStateMask` at [line 219-224](../../Assets/Resources/BiomeGrassPlace.compute#L219-L224) to suppress grass on paved/scorched cells. `GrassNearFieldPlace.compute` does NOT. Mid-field doesn't either.

**Slice 4a action:** retrofit near-field compute to sample `_SurfaceStateMask` from the per-face state atlases (same `_NearFieldSurfaceStateMask_F0..F5` pattern as the other near-field bindings). Apply the same `state.r > 0.5 || state.g > 0.5` reject. This unblocks future path-cutting / building-foundation features. Cheap fix; ride along with slice 4a.

**Slice 4c action:** mid-field compute does the same sampling.

## Multi-Face Cell-Range Library — Detailed Design

The core math: given a camera world position, a planet center, planet radius, world spacing, draw radius, and page size — produce a small set of face-space integer cell ranges covering the visible disc, snapped to pages so dispatches don't fire on sub-cell motion.

**Algorithm:**

1. Compute camera surface anchor direction: `dir = normalize(camera.position - planetCenter)`.
2. `DirectionToFaceUv(dir) -> (primaryFace, primaryFaceUv)` (existing helper in `GrassNearFieldController`).
3. Sample local meters-per-UV at primary face via finite differences on `CubeFaceToUnitSphere` (existing math in `GrassNearFieldController.Tick`).
4. `cellUvWidth = worldSpacing / metersPerUV`.
5. `discRadiusUV = worldRadius / metersPerUV`.
6. Compute raw cell range covering `[primaryFaceUv ± discRadiusUV]`.
7. Snap each axis to page boundaries via `FloorDivToMultiple` / `CeilDivToMultiple`.
8. Emit primary range as `outRanges[0]`.
9. **Seam check:** for each of the 4 face edges (u=0, u=1, v=0, v=1), check if `primaryFaceUv` is within `discRadiusUV` of that edge. If yes:
   - Compute the adjacent face index using existing `CubeFaceTopology` helpers (already in repo at [Assets/Scripts/Core/Utilities/CubeFaceTopology.cs](../../Assets/Scripts/Core/Utilities/CubeFaceTopology.cs)).
   - Project the overlapping region into the adjacent face's UV space.
   - Emit as `outRanges[1+]`.
10. Cap output count at 4 (camera at a face corner straddles 3 faces; plus primary = 4 max). Caller pre-allocates `Span<FaceSpaceCell>` of size 4.

**Caller pattern:**

```csharp
Span<FaceSpaceCell> ranges = stackalloc FaceSpaceCell[4];
int count = FaceSpaceCellRangeBuilder.BuildRanges(
    camera, _planetTransform, _planetRadius,
    _spacing, _drawDistance, _pageSizeMeters, ranges);

for (int i = 0; i < count; i++)
    DispatchOneFace(ranges[i]);
```

Each face dispatch is independent (separate `_compute.Dispatch` call, separate stats slot per face). The instance buffer is shared across faces (append into one buffer; one indirect draw renders all).

## Mid-Field Layer — Detailed Design

### New files

- `Assets/Resources/GrassMidFieldPlace.compute` — placement kernel
- `Assets/Scripts/Planet/Grass/GrassMidFieldController.cs` — runtime controller, mirrors `GrassNearFieldController` pattern
- `Assets/Graphics/Shaders/GrassMidField.shader` — camera-facing card shader
- `Assets/Scripts/Core/Interfaces/IGrassMidFieldStatsProvider.cs` — F10 stats

### Compute kernel

**One thread per face-space cell** (same dispatch pattern as near-field). For each cell:

1. Compute `(face, cellU, cellV)` from `_GridStartCellUV + id.xy`.
2. Hash by `(seed, face, cellU, cellV)` for stable cross-frame placement.
3. Sub-cell jitter using stable hash.
4. `faceUv = (cell + 0.5 + jitter) * _CellUvWidth`.
5. `dirWs = CubeFaceToUnitSphere(face, faceUv)`.
6. `SampleVegetationCoverage(faceUv, face)` — gives density, blendedBiome, slopeKeep, waterKeep.
7. Reject if `coverage.density * coverage.slopeKeep * coverage.waterKeep < threshold`.
8. Sample `_NearFieldSurfaceStateMask_F<n>` — reject if paved/scorched (forward-compat for path cutting).
9. Sample surface radius → world position.
10. Distance gate against `_MidFieldDrawDistance`.
11. Per-cell stochastic thinning between `_MidFieldFullDensityDistance` and `_MidFieldDrawDistance`.
12. Per-cell fade alpha for the outer fade band (packed into `MidFieldCardInstance.Color.a`).
13. Loose frustum cull (1.1× / 1.5× overshoot, same as near-field).
14. Explicit `InterlockedAdd` slot allocation on `_MidFieldDrawArgs[1]` with capacity guard.
15. Write card instance: position, up vector, blended biome height, blended biome tint, fade alpha.

### Card shape

**Single camera-facing quad** (2 triangles, 6 verts) — simplest, cheapest, sufficient.

Why not crossed cards (4 verts × 2 quads = 8 verts)?
- 2× geometry per card with no clear visual win at distance — at 200m+ the camera-facing trick already presents full width.
- Can add later as a quality tier knob if needed.

Why not low-vertex procedural strip?
- Then it's basically a smaller version of near-field tuft. Visual identity gets blurred.
- Cards intentionally look distinct from blades — they're meant to be impostors.

**Per-card geometry** (in vertex shader):

```hlsl
// Camera-facing billboard centered on root position.
float3 cameraRightWs = UNITY_MATRIX_V[0].xyz;
float3 cameraUpWs    = UNITY_MATRIX_V[1].xyz;

// Card sized by biome height + small per-card jitter; width = height * cardAspect.
float cardHeight = card.HeightWidth.x;
float cardWidth  = card.HeightWidth.y;
float3 cornerOffset = cameraRightWs * (vertexU - 0.5) * cardWidth
                    + cameraUpWs    *  vertexV         * cardHeight;
float3 positionWs   = card.RootWs + cornerOffset;
```

### Texture

**No texture in first slice — procedural alpha mask.** Card shader does an analytic vertical falloff with hash-based per-card detail (matches the "fiber" look). If first F10 reads as obvious flat sprites, add a grayscale grass-clump texture in a follow-up slice.

Why no texture day 1:
- No asset pipeline dependency
- Easier to validate the placement architecture in isolation
- Can A/B texture vs procedural by toggling a `#pragma multi_compile`

### Shader: lighting

Uses shared `PlanetSunLighting.hlsl`. Same day/night gate as near-field and terrain. No URP PBR.

```hlsl
PlanetSunInfo sun = SamplePlanetSun(positionWs);
float3 albedo = saturate(card.Tint.rgb * tintJitter);

// Card "diffuse" uses an approximated normal: lerp between planet up (lit by surface)
// and camera direction (so the card responds to grazing light).
float3 cardNormal = normalize(lerp(planetNormalWs, viewDirWs, 0.3));
float3 dayColor = ApplyDayLighting(albedo, cardNormal, sun);
dayColor += ApplyBacklight(albedo, viewDirWs, 0.15, sun); // subtler than near-field

float3 nightColor = ApplyNightLighting(albedo, sun);
float3 finalColor = lerp(nightColor, dayColor, sun.daylight);
finalColor = MixFog(finalColor, fogFactor);
```

### Fade

Per-card alpha packed into `Color.a` by compute (same pattern as near-field):

- Inner fade (overlap with near-field): linear ramp from 0 at near-end to 1 at mid-start
- Outer fade (overlap with far blanket): linear ramp from 1 at mid-end - fadeBand to 0 at mid-end

Fragment shader uses the **same Bayer 3×3 dither** from `Grass.shader` (extract to `Includes/GrassDither.hlsl` so both shaders share). `clip(input.color.a - dither)`.

### Distance bands — configurable

New `IGrassMidFieldQualitySettings` interface (parallels `IGrassQualitySettings`):

```csharp
public interface IGrassMidFieldQualitySettings
{
    float Spacing { get; }              // default 1.5m
    float FullDensityDistance { get; }  // default 150m
    float DrawDistance { get; }         // default 300m
    float InnerFadeStart { get; }       // default 80m (overlap with near-field)
    float InnerFadeEnd { get; }         // default 100m
    float OuterFadeBand { get; }        // default 50m (last 50m of DrawDistance)
    int MaxCardsTotal { get; }          // default 200_000 (~10 MB at 48 bytes)
}
```

Defaults are **first guesses for tuning**, not locked. F10 iteration drives final values.

### F10 stats

New `--- GrassMidField ---` block in F10 sidecar:

```
--- GrassMidField ---
Controller: active=True, shader=True
Quality: spacing=1.50, fullDensity=150.0, draw=300.0, innerFade=80-100, outerFadeBand=50.0
Page: cellSize=8, facesActive=2, primaryFace=0, originCellUV=(...), seamRisk=False
Grid: <perFaceGridDimensions>, reason=PageChanged, dispatchedThisFrame=False, dispatchesTotal=37
Draw: emitted=42180, capacity=200000, buffer=10.0 MB
Cull: candidates=156000, coverage=89234, water=423, slope=812, distance=21340, distanceFade=1989, frustum=22
```

### Memory budget

Per card: `RootHeight (16) + UpTint (16) + Color (16) = 48 bytes`. At 200k cap = 9.6 MB. Plus `4-byte stats × ~10 = 40 bytes`. Plus indirect args = 16 bytes. Total ~10 MB. Trivial.

Compare with the chunk path's 348 MB ceiling that we're trying to retire. Mid-field at 10 MB is the right shape.

## Far Blanket Layer — Changes

Already exists in [PlanetVertexColor.shader](../../Assets/Graphics/Shaders/PlanetVertexColor.shader). Slice 4a changes:

1. **Refactor to use shared `SampleVegetationCoverage`** (currently does its own `CornerGrassOverlayParams` + bilinear). Same math, single implementation.
2. **Use shared `PlanetSunLighting.hlsl`** for the grass-tint lighting term (currently runs raw terrain lighting; mid and near both use the dedicated grass lighting).
3. **Adjust distance band to start past mid-field draw distance**: `_GrassFarOverlayStart = 200m` (current 65m), `_GrassFarOverlayEnd = 500m` (current 160m). The blanket's job is the very-far falloff; mid-field cards carry 80-300m.
4. **Replace screen-space dither (if any) with stable world-space noise** for breakup. Codex's existing `ValueNoise3D` calls are correct; keep them.
5. **Add altitude-driven intensity ramp** so from orbit the blanket is subtle, and from low altitude it strengthens to fill gaps between mid cards.

## Near-Field — Retrofits (Slice 4a)

While extracting shared concepts, retrofit near-field to:

1. Use `Includes/PlanetSunLighting.hlsl` instead of inline math.
2. Use `Includes/GrassCoverage.hlsl` `SampleVegetationCoverage` instead of inline `BlendGrassParams`.
3. Use `Includes/GrassDither.hlsl` for fade clip.
4. **Sample `_NearFieldSurfaceStateMask_F0..F5`** — new per-face bindings (atlas already exists; just bind it). Reject paved/scorched cells. **Latent bug fix; required for future path cutting.**
5. Use `FaceSpaceCellRangeBuilder` (slice 4c) — eliminates current `SeamRisk` limitation.
6. **Reserve interactor hook**: vertex shader calls `SampleGrassInteractorBend(rootWs)` and adds it into the tip offset. Stub returns zero for now; slice 6 implements.

## Forward Compatibility

### Wind (slice 5)

Add `_WindParams` global (direction + strength + time) to `PlanetSunLighting.hlsl` or a sibling `WindAmbient.hlsl`. Each grass shader's vertex pass adds a tip displacement proportional to `_WindParams.strength * sin(time + worldHash)`. Mid-field cards bend at the top vertex; near blades bend at the tip.

No infrastructure change beyond shader work. Already covered.

### Character interactor bend (slice 6)

`_GrassInteractors` buffer reserved (above). `SampleGrassInteractorBend` stub returns zero now; slice 6 fills in:

```hlsl
float3 SampleGrassInteractorBend(float3 rootWs)
{
    float3 bend = float3(0, 0, 0);
    for (int i = 0; i < _GrassInteractorCount; i++)
    {
        float3 toRoot = rootWs - _GrassInteractors[i].PositionRadius.xyz;
        float dist = length(toRoot);
        float radius = _GrassInteractors[i].PositionRadius.w;
        if (dist < radius)
        {
            float falloff = 1.0 - smoothstep(0, radius, dist);
            bend += normalize(toRoot) * falloff * _GrassInteractors[i].StrengthType.x;
        }
    }
    return bend;
}
```

C# side adds an `IGrassInteractorRegistry` service that aggregates player + nearby NPCs each frame, uploads to the global buffer. No grass shader rewrite needed.

### Path cutting / building foundations (slice 7)

`_SurfaceStateMask` sampling in slice 4a/4c provides the read-side. Slice 7 adds the write-side: a runtime API that paints into the state atlas (paved channel = path, scorched channel = building foundation). Grass placement compute already filters by these channels, so newly-painted regions immediately become bare.

This is why retrofitting near-field to sample `_SurfaceStateMask` is on slice 4a — otherwise slice 7 would touch all three grass paths simultaneously. Pay the small cost now.

## Implementation Slicing

| Slice | Scope | Est. lines | Risk |
|---|---|---|---|
| 4a | Extract shared includes (`PlanetSunLighting.hlsl`, `GrassCoverage.hlsl`, `GrassDither.hlsl`); near-field retrofit to use them; near-field `_SurfaceStateMask` sampling; refactor far blanket to use shared includes | ~250 | Low — refactor, no new architecture |
| 4b | `FaceSpaceCellRangeBuilder` library + retrofit near-field to use it (multi-face dispatch); near-field `SeamRisk` flag retired | ~200 | Medium — multi-face seam edge cases need debug viz |
| 4c | New mid-field controller + compute + shader + F10 stats + interactor stub | ~700 | Medium — biggest single ship |
| 4d | Post-validation: toggle off chunk path; if F10 proves clean swap, delete `GrassPlacementController` and `BiomeGrassPlace.compute` | ~50 (deletions) | Low — pure cleanup |

**Order:** 4a → 4b → 4c → Bryan F10 → 4d.

Each slice ships and F10-validates independently. 4a doesn't introduce any visible change (refactor-only); F10 should be identical to current. 4b should eliminate near-field seam risk; visible only if Bryan was near a face edge. 4c is the big visual unblock. 4d is opportunistic cleanup if F10 supports it.

## F10 Stats — Full List

The grass section of the F10 sidecar after slice 4 will have four blocks. Existing two stay; two are new.

```
--- Grass ---             (chunk path — eventually removed by 4d)
--- GrassNearField ---    (existing, expanded with multi-face per-range counts)
--- GrassMidField ---     (NEW in 4c)
--- GrassCoverage ---     (NEW in 4a — terrain blanket diagnostic; complements existing LOD coverage debug mode)
```

`--- GrassCoverage ---` includes:
- Average vegetation coverage in visible chunks
- Far blanket intensity at camera surface anchor
- Whether the LOD coverage debug visualization is enabled

## Risks / Open Questions

### Risks

1. **Card shape may read as obvious billboards at certain angles.** Camera-facing quads have a known weakness when the camera is high above the surface (cards lay flat from below). Mitigation: tilt cards slightly toward planet up; if still bad, switch to crossed cards in a follow-up.
2. **Multi-face cell-range builder edge cases.** Corners of the cube are 3-face boundaries; the algorithm needs to handle them. Test cases: camera over face corner; camera moving along a face edge; camera transitioning between faces at low altitude. Will need a debug viz that colors cards by source face.
3. **Page-snap interaction with multi-face.** Each face has its own page grid. When a face crosses, the new face's page snapping may not align with the old face's. Could produce a one-frame popping. Mitigation: snap the camera anchor to a planet-relative grid before face conversion, so adjacent faces' page origins line up. (Detail TBD; address during 4b implementation.)
4. **Performance scaling.** Mid-field at `spacing=1.5m, draw=300m` = ~125k candidate cells per dispatch. Multiplied by 4 face dispatches when straddling = 500k. Compare with near-field's ~1.3M per dispatch. Mid-field's per-cell work is lighter (simpler card vs. tuft of 3 blades). Should be cheaper than near-field, but worth measuring.
5. **The shared HLSL include refactor (slice 4a) touches 3 shaders.** Risk of subtle visual regression. Mitigate by F10-comparing before/after with the same camera angle.

### Open questions

1. **Should the mid-field card have a texture in the first ship?** Procedural alpha mask is simpler but may read as flat. Could ship both via `#pragma multi_compile _GRASS_MID_TEXTURED` and let Bryan toggle in F10. Recommended: ship procedural first, add textured variant if needed.
2. **Should the chunk-path toggle be a runtime quality switch or a compile-time `#define`?** Runtime quality switch allows Bryan to A/B during testing without rebuilding. Recommended: runtime quality bool exposed via `IGrassQualitySettings.EnableChunkPath` (default true until 4d validates).
3. **Should `IGrassMidFieldQualitySettings` be a separate interface or merged into `IGrassQualitySettings`?** Separate keeps each renderer's knobs in its own surface. Recommended: separate interface, both consumed by quality-tier presets.

## Acceptance Criteria (for slice 4 validation)

Slice 4 is complete when an F10 from a low-altitude grassland view shows:

1. **No visible disc of cutoff** between near-field blade fade and mid-field cards.
2. **No visible disc of cutoff** between mid-field card fade and far blanket overlay.
3. **No bare arcs along cube-face seams** — multi-face dispatch working.
4. **Distant hills read as geometric grass** at 150-300m, not just tinted terrain.
5. **Night view** — all three layers go dark together (shared lighting include verified).
6. **F10 numbers** — `MidField emitted` between 20k-80k, `buffer` under 15 MB.
7. **FPS** — within 10% of current pre-slice-4 baseline (mid-field cost offset by chunk-path toggle-off if 4d also lands).

## Approvals needed

- [ ] Bryan: greenlight implementation order 4a → 4b → 4c → 4d
- [ ] Codex: review architecture, push back on anything missed
- [ ] Bryan: pick texture vs procedural default for cards (open question 1)
- [ ] Bryan: pick chunk-path toggle mechanism (open question 2)

Once aligned, slice 4a starts.
