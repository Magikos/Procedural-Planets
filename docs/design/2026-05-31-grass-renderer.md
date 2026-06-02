# Grass renderer — Phase C

**Date:** 2026-05-31
**Status:** Draft, revised after code review
**Source of truth this implements:** [docs/research/2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md) — "Grass renderer" + "Grass renderer — locked-in additions" sections.
**Predecessors:** Phase A chunk skeleton ([docs/design/2026-05-30-chunk-skeleton.md](2026-05-30-chunk-skeleton.md)), Phase B biome textures ([docs/design/2026-05-31-biome-textures.md](2026-05-31-biome-textures.md)).

---

## 1. Purpose and scope

Ship the **compute-driven grass renderer** as a clean addition to the chunked planet, consuming the top-K biome data Phase B publishes through face-space atlases and per-chunk maps. Per-frame compute regenerates blades from lane-IDs (no persistent per-blade buffer), and indirect draw emits the visible subset.

**In scope (this phase):**

- `IGrassQualitySettings` interface + a default constant-backed implementation (placeholder until a real settings menu exists).
- `BiomeDefinition` gains grass placement/render fields: density, tint, dimensions, clumping, slope rejection, water clearance, and blend response.
- `ChunkGrassHandle` — per-chunk GPU resources (transient blade instance buffer, indirect-args buffer, optional Voronoi clump buffer).
- `GrassPlacementController` — per-planet controller owned by `Planet`; registers visible chunks, owns grass GPU resources, schedules per-frame compute dispatches.
- Compute shader `BiomeGrassPlace.compute` — per-lane sampling of biome ids+weights → density gate → root jitter → 4-test cull → atomic write to instance buffer.
- Vertex/fragment shader `Grass.shader` — vertex-shader strip generation from quadratic Bézier (v0, v1, v2). Vertex-sliding LOD (15 ↔ 7 verts).
- Indirect rendering via `Graphics.RenderPrimitivesIndirect` for procedural blade strips. `Graphics.RenderMeshIndirect` remains an optional fallback only if a Unity 6 smoke test proves the mesh-template path can emit the required per-blade vertex stream cleanly.
- Sub-blade lighting: rounded normals across blade width, glancing-angle widening, clump-color base, per-vertex height-darken.
- Planet-tangent up vector for all blade orientation math (no world-Y references anywhere in the grass path).
- Visual debug modes: `DEBUG_GRASS_DENSITY` (heatmap), `DEBUG_GRASS_CULL_REASON` (color-coded), `DEBUG_GRASS_CLUMP_ID`, `DEBUG_GRASS_SURFACE_SAMPLE`, `DEBUG_GRASS_LOD`.

**Out of scope (later phases):**

- **Wind / animation** — Phase D. v1 ships static grass standing straight; blades are still computed every frame for camera-dependent culling, but `v2 = v0 + up * height` (no bend).
- **Force-map / collider interaction** — Phase D.
- **Modification API** (paving, scorching, footprints) — Phase E.
- **Snow accumulation** — Phase F.
- **Seasonal color shifts** — Phase E (after `SeasonalState` channel exists).
- **Tessellation** — explicitly skipped for v1 to keep platform compatibility simple. Vertex-shader strip generation is the JAHRMANN-validated alternative. Phase D adds tess if Bryan wants the extra quality, after the URP-17 hello-world sanity check.
- **`IWindFieldProvider`** — created in Phase D when wind matters. v1 grass shader takes a constant wind = 0.
- **`IChunkPersistenceProvider`** — Phase E, when modifications exist.
- **Surface state stack expansion** — Phase E. The `SurfaceStateTexture` plumbed in Phase B step 9 is the seed; Phase E adds the `WeatherState` / `TrackMap` / `SeasonalState` companions.

**Non-goals:**

- Per-blade shadow maps (we use terrain shadow + per-vertex height darken).
- CPU per-frame work for blade state (everything compute / shader side).
- Mesh-asset blade authoring (procedural strip).
- Replacing the existing `IPlanetSurfaceProvider` generation flow. Grass adds a narrow visibility/source-data contract for the high-resolution provider.

---

## 2. Decisions (locked in 2026-05-31)

All 10 decisions from the research doc's "Grass renderer" section are inherited. Phase C adds these implementation-level choices:

| # | Question | Decision |
|---|----------|----------|
| 1 | Tessellation vs vertex-shader strip generation | **Vertex shader.** Keeps platform compatibility simple; matches JAHRMANN reference impl; deferring tess to Phase D after a hello-world sanity check. |
| 2 | Lane grid resolution per chunk | **64×64 lanes/chunk = 4096 max blades.** Matches `PlanetChunkTextures.BiomeMapResolution` for simple density/debug alignment, but placement samples the face-space biome atlas through chunk UV scale/offset. Multiplied by `IGrassQualitySettings.MaxBladesPerLane` for sub-lane multi-blade clustering at high quality. |
| 3 | Quality settings ownership | **Internal interface + constant impl now; menu later.** `IGrassQualitySettings` is created in Phase C with a `DefaultGrassQualitySettings` constant-backed impl (medium preset). Phase D / a settings menu can drop in a real impl when ready. No UI work in this phase. |
| 4 | Voronoi clump implementation | **Pre-baked per-chunk on placement compute.** First compute pass writes a `ChunkClumpBuffer` (NumClumps × ClumpData = ~16 KB/chunk) when chunk activates. Re-baked when chunk re-bakes (e.g. biome rebake from Phase B step 9). Per-blade clump lookup is index-based, no Voronoi distance test per blade per frame. |
| 5 | Per-chunk vs per-face dispatch | **Per visible chunk per frame.** One compute dispatch and one indirect draw per visible leaf. Simpler than batched dispatches; instance counts vary too widely between chunks to batch cleanly. |
| 6 | Far-LOD overlay | **Deferred.** Research doc §"Biome texture system" mentions baking grass tint into a chunk-level overlay. Not needed until profiling shows a need. v1 just fades blade count to zero past `IGrassQualitySettings.MaxRenderDistance`. |
| 7 | Cross-chunk seam continuity | **Stable face-space hashing + explicit edge ownership.** Adjacent chunks derive jitter/density from remapped face-space coordinates, not raw chunk-local lane indices. Shared edges must agree after cube-face remap, and only one owner emits the boundary lane. |
| 8 | Surface sample ownership | **Face-space terrain surface atlases from day one.** Radius + normal sampling uses stitched per-face atlases, matching the biome atlas strategy and avoiding a later chunk-local → atlas rewrite. |
| 9 | Controller ownership | **Per-planet, Planet assembly.** `GrassPlacementController` is created and disposed by `Planet`, receives the active `ChunkedSurfaceProvider`, and is ticked after provider visibility updates. It is not a `ServiceLocator` singleton. |
| 10 | Coarse terrain LOD | **Blade grass only on sufficiently detailed visible chunks.** Since roots sample the max-depth surface atlas, v1 skips coarse visible parent chunks by default to avoid roots floating above or sinking into simplified terrain. Far/coarse vegetation is handled later by the deferred overlay path. |

### 2.1 Review corrections (supersede table entries where they conflict)

- **Visible chunk ownership:** grass must not scan private chunk lists or infer visibility from GameObject state. Phase C adds a Planet-assembly chunk visibility contract on the chunked surface provider, e.g. `IChunkVisibilitySource`, so `GrassPlacementController` receives `OnChunkVisible` / `OnChunkHidden` and an initial snapshot after planet generation. Because this contract exposes `PlanetChunk`, it lives in the Planet assembly, not Core.
- **GPU surface source:** compute cannot call `IPlanetSurfaceSampler`. Before any placement compute runs, the planet must expose GPU-readable face-space terrain surface atlases: terrain radius + smoothed terrain normal, generated from the same cached terrain arrays that build the mesh.
- **Biome sampling source:** grass placement uses the same face-space biome atlas that terrain uses, not only the rendered chunk's local biome texture. This keeps parent/child LODs sampling one biome field and prevents density/color kinks at LOD changes.
- **Seam determinism:** lane jitter and density rolls must be derived from stable face-space or remapped cube-face coordinates, not raw chunk-local lane indices. Shared edges must agree after cube-face edge remap, and only one owner may emit a boundary lane.
- **Draw API:** the default implementation uses `Graphics.RenderPrimitivesIndirect` because the grass vertices are procedural and keyed by `SV_VertexID`. `Graphics.RenderMeshIndirect` is allowed only after a small Unity smoke test proves the desired vertex-count semantics.
- **Placement masks:** v1 placement must already reject ocean, underwater, paved/scorched surface-state texels, and fade out on steep slopes when those channels are present. This does not mean full modification gameplay ships in Phase C; it means the renderer has the right gates from the start.

---

## 3. Data model

### 3.1 `BiomeDefinition` new fields

```csharp
[Header("Phase C: Grass")]
public float GrassDensity = 0f;          // [0,1] — fraction of lanes that emit a blade
public Color GrassTintBase = Color.white; // clump base color (per-clump variation on top)
public float GrassHeight = 0.6f;          // max blade tip displacement (world units)
public float GrassWidth = 0.04f;          // base half-width at v0 (world units)
public float GrassClumpStrength = 0.65f;  // 0 = no clumping (uniform random), 1 = strong clumping
[Range(0f, 90f)] public float GrassMaxSlopeDegrees = 35f; // center of steep-slope density fade
public float GrassSlopeFadeDegrees = 5f; // soft fade width around max slope to prevent speckle
public float GrassMinWaterClearance = 0.05f; // world units above water before grass can emit
public float GrassBiomeBlendPower = 1f;   // density response to top-K biome weights
```

Existing biome assets continue to load (Unity defaults preserve all-zero, which means "no grass" — safe).

### 3.2 `IGrassQualitySettings`

```csharp
public interface IGrassQualitySettings
{
    int MaxBladesPerLane { get; }          // 0..4 — multi-blade jitter at one lane
    float MaxRenderDistance { get; }       // world units
    float LowLodDistance { get; }          // beyond this, switch from 15-vert to 7-vert blade
    float CullDistanceJitter01 { get; }    // 0 = hard cull, 1 = id-mod-N stochastic across full distance
    int MaxCoarseLodOffsetForBlades { get; } // 0 = max-depth chunks only, 1 = max-depth and parent chunks
    bool EnableScreenSpaceShadows { get; } // off at Low preset, on at Medium+
}

public sealed class DefaultGrassQualitySettings : IGrassQualitySettings
{
    public int   MaxBladesPerLane          => 1;
    public float MaxRenderDistance         => 120f;
    public float LowLodDistance            => 45f;
    public float CullDistanceJitter01      => 0.6f;
    public int   MaxCoarseLodOffsetForBlades => 0;
    public bool  EnableScreenSpaceShadows  => true;
}
```

`GrassPlacementController` reads from `ServiceLocator.Get<IGrassQualitySettings>()` with `DefaultGrassQualitySettings` as fallback. Phase D / settings menu registers a different impl. The quality settings may be global; the controller and its GPU resources are per planet.

### 3.3 Visible chunk contract

`GrassPlacementController` depends on a narrow public contract from the active high-resolution surface provider. This interface lives in the Planet assembly because it exposes `PlanetChunk`; it should not be placed under Core:

```csharp
public interface IChunkVisibilitySource
{
    IReadOnlyList<PlanetChunk> GetVisibleChunksSnapshot();
    event System.Action<PlanetChunk> ChunkShown;
    event System.Action<PlanetChunk> ChunkHidden;
}
```

`ChunkedSurfaceProvider` is currently pre-cache + visibility-filter based: all chunk meshes are generated during loading, and runtime LOD only toggles cached renderers. Grass should follow that same visible set. It should not scan `_allChunks`, inspect private renderer dictionaries, or infer visibility from GameObject active state.

The low-resolution `PerFaceSurfaceProvider` does not implement this contract; grass is disabled for low-resolution planets until a separate distant-planet vegetation/impostor path exists.

### 3.4 `GrassPlacementController` lifetime

`GrassPlacementController` is created by `Planet` after the active surface provider is selected. In High resolution, `Planet` passes the `ChunkedSurfaceProvider` as an `IChunkVisibilitySource`; in Low resolution, no grass controller is created.

Lifecycle:

1. `Planet.GeneratePlanetAsync` builds terrain and biome data.
2. The chunked provider builds face-space biome atlases and the new face-space grass surface atlases.
3. `Planet` creates `GrassPlacementController` and gives it the provider visibility source, surface atlases, biome atlas references, planet transform, and material/shader settings.
4. Each `Planet.Update` calls `_surfaceProvider.Tick(...)` first, then `GrassPlacementController.Tick(...)`.
5. `Planet.Dispose` / destroy path disposes the grass controller before disposing surface-provider textures and meshes.

The controller is not registered as a global service. Later wind, modification, and snow systems talk to it through explicit planet-owned references or narrow Planet-side interfaces.

### 3.5 GPU surface inputs

Grass compute needs GPU-readable terrain samples. Phase C adds stitched face-space surface resources before placement. This is deliberately atlas-first, matching the biome atlas path, because grass density/root placement must not kink when terrain LOD switches between parent and child chunks.

```csharp
sealed class GrassSurfaceAtlasGpuData : System.IDisposable
{
    public Texture2D[] RadiusByFace; // RHalf/RFloat, face-space atlas UV
    public Texture2D[] NormalByFace; // v1 proof: RGBA32 packed xyz normal, face-space atlas UV
    public int AtlasResolution;

    public Vector4 GetUvScaleOffset(PlanetChunk chunk);
}
```

The source of truth is the cached terrain data already stored on `PlanetChunk` (`CpuVertexRadii`, `CpuNormals`, `CpuUnitSpherePoints`). The atlas stitcher writes radius and a smoothed placement normal into face-space textures using the same max-depth leaf layout as the biome atlases. The first implementation stores radius as `RFloat` and normals as `RGBA32` packed xyz for debug readability; oct/RG compression is a later memory optimization after `DEBUG_GRASS_SURFACE_SAMPLE` proves correctness. `IPlanetSurfaceSampler.TryGetSurfaceRadius` remains CPU-side and is not callable from compute.

Because these atlases represent the detailed surface, `GrassPlacementController` only dispatches blade grass for visible chunks at or near max detail, controlled by `IGrassQualitySettings.MaxCoarseLodOffsetForBlades`. Coarser chunks get no individual blades in v1; a far/coarse grass-color overlay is the later solution for that distance band.

### 3.6 `PlanetChunk` new fields (CPU side)

```csharp
[System.NonSerialized] public GrassChunkRuntime Grass; // null when grass disabled for this chunk
```

Where `GrassChunkRuntime` is the C# wrapper around the GPU buffers:

```csharp
sealed class GrassChunkRuntime
{
    public ComputeBuffer InstanceBuffer;   // BladeInstance[MaxBlades]
    public ComputeBuffer IndirectArgsBuffer; // Unity indirect draw args for RenderPrimitivesIndirect
    public ComputeBuffer ClumpBuffer;      // ClumpData[NumClumps] — baked once per chunk
    public int   MaxBlades;                // 64*64*MaxBladesPerLane
    public int   NumClumps;                // ~16 typically
    public bool  ClumpsBaked;              // false until first dispatch
}
```

### 3.7 GPU structs

```hlsl
struct BladeInstance {
    float3 v0;       // root world pos (planet-relative ok if we pass _PlanetCenter)
    float3 v1;       // mid Bézier control point
    float3 v2;       // tip
    float  height;   // for vertex math
    float  width;    // half-width at v0
    float  rotation; // direction angle in tangent plane (radians)
    uint   clumpId;  // index into ClumpBuffer
    uint   biomeId;  // dominant biome at this blade's lane (for tint / params)
};

struct ClumpData {
    float3 centerWS;          // world-space clump center
    float3 baseColor;         // sRGB tint
    float2 facingDir2D;       // unit vector in tangent plane
    float  radius;             // clump extent
};

// Exact layout follows Unity's indirect argument struct for the selected draw API.
// RenderPrimitivesIndirect uses non-indexed procedural vertices.
struct GrassIndirectArgs { uint vertexCountPerInstance, instanceCount, startVertex, startInstance; };
```

`BladeInstance` is ~52 bytes packed (3×12 + 4×4 + 2×4 = 56 with alignment). At 4096 max blades × 56 bytes = ~224 KB per chunk. At ~80 visible chunks: ~18 MB total. Acceptable.

---

## 4. Compute pipeline

Two compute kernels, dispatched per chunk per frame:

```
BiomeGrassPlace.compute
├── kernel ClumpBake          — one-shot, when chunk's ClumpsBaked=false
└── kernel PlaceAndCull       — every frame chunk is visible
```

### 4.1 ClumpBake kernel (one-shot per chunk)

Threadgroup = (4, 4, 1). One thread per clump (NumClumps = 16 default).

```
threadId = (cx, cy)
clumpUv  = (cx + 0.5, cy + 0.5) / NumClumpsPerSide   // jittered later
clumpUv += hash(chunkHash, cx, cy) * jitterRadius     // Voronoi-ish irregular placement

biomeId, weight = sampleBiomePoint(biomeIdsTexture, biomeWeightsTexture, clumpUv)
baseColor = sampleBiomeFlatColor(biomeFlatColors, biomeId)
biomeDef  = sampleBiomeParamBuffer(biomeId)  // per-biome grass params, pre-uploaded each gen

clumpData[id].centerWS    = chunkUvToWorld(chunkData, clumpUv)
clumpData[id].baseColor   = baseColor * (1 + hash(chunkHash, cx, cy) * 0.2)  // ±10% per clump
clumpData[id].facingDir2D = hashedTangentDir(chunkHash, cx, cy)
clumpData[id].radius      = biomeDef.GrassClumpStrength * baseClumpRadius
```

Output: `ClumpBuffer[16]` populated. Set `ClumpsBaked = true`.

Clump seeds use face-space coordinates and planet seed. `chunkHash` may be included for chunk ownership bookkeeping, but it must not be the only randomness source or clump color/facing can form visible chunk seams.

### 4.2 PlaceAndCull kernel (per frame, per visible chunk)

Required inputs per dispatch:

- `ChunkDispatchData`: face id, chunk hash, chunk UV scale/offset, planet center, water radius, object/world transforms, and the selected blade vertex count.
- Face-space biome atlas textures: `_BiomeIds`, `_BiomeWeights`, and `_BiomeFlatColors` using the same atlas mapping terrain uses.
- Face-space grass surface atlases: radius + smoothed terrain normal generated from cached `PlanetChunk` CPU arrays.
- Optional state texture: current `SurfaceStateTexture` for paved/scorched/wet/snow gates.
- Camera data: planes, position, distance thresholds, and density fade settings.

The implementation sketch below uses `laneUv` for readability, but production code must convert to face-space UV before biome sampling and stable hashing:

```hlsl
faceUv   = chunkUvToFaceUv(chunkData, laneUv);
hashSeed = stableFaceHash(faceId, quantize(faceUv), planetSeed);

ids4 = sampleBiomeIdsAtlasRGBA8(faceUv) * 255;
w4   = sampleBiomeWeightsAtlasRGBA8(faceUv);
dominantBiome = ids4.x;
density = blendedTopKGrassDensity(ids4, w4, biomeParams);
if (hash01(hashSeed, 0) > density) return;

radius = sampleGrassSurfaceRadiusAtlas(faceUv);
normalLocal = sampleGrassSurfaceNormalAtlas(faceUv);
unitSphere = chunkUvToUnitSphere(chunkData, laneUv);

if (radius <= _WaterRadius + biomeParams[dominantBiome].minWaterClearance) return;
float slope = slopeDegrees(unitSphere, normalLocal);
float slopeFade = 1.0 - smoothstep(
    biomeParams[dominantBiome].maxSlopeDegrees - biomeParams[dominantBiome].slopeFadeDegrees,
    biomeParams[dominantBiome].maxSlopeDegrees + biomeParams[dominantBiome].slopeFadeDegrees,
    slope);
density *= slopeFade;
if (density <= 0.0001) return;
if (surfaceState.paved > 0.01 || surfaceState.scorched > 0.95) return;
```

Threadgroup = (8, 8, 1). One thread per lane in the 64×64 grid.

```
threadId = (lx, ly)
laneUv   = (lx + 0.5, ly + 0.5) / 64

// 1. Biome sample — sample the face-space atlas, not the local chunk fallback map
float2 faceUv = chunkUvToFaceUv(chunkData, laneUv)
uint4 ids4 = sampleBiomeIdsAtlasRGBA8(faceUv) * 255
uint4 w4   = sampleBiomeWeightsAtlasRGBA8(faceUv)
uint  dominantBiome = ids4.x
uint  dominantWeight = w4.x

// 2. Density gate — weighted by the top-K biome blend and stable face-space hash
float density = blendedTopKGrassDensity(ids4, w4, biomeParams)
float densityRoll = hash(stableFaceHash(chunkData.faceId, faceUv, planetSeed), 0) / 4294967295.0
if (densityRoll > density) return  // no blade at this lane

// 3. Root computation — sample face-space GPU surface atlases generated from cached terrain
float radius = sampleGrassSurfaceRadiusAtlas(faceUv)
float3 normalLocal = sampleGrassSurfaceNormalAtlas(faceUv)
float3 unitSphere = chunkUvToUnitSphere(chunkData, laneUv)
float3 rootLocal = radius * unitSphere
float3 rootWS    = mul(_ObjectToWorld, float4(rootLocal, 1.0)).xyz
float3 upWS      = normalize(mul((float3x3)_ObjectToWorld, normalLocal))

// 3b. Placement gates
if (radius <= _WaterRadius + biomeParams[dominantBiome].minWaterClearance) return
float slope = slopeDegrees(unitSphere, normalLocal)
float slopeFade = 1.0 - smoothstep(
    biomeParams[dominantBiome].maxSlopeDegrees - biomeParams[dominantBiome].slopeFadeDegrees,
    biomeParams[dominantBiome].maxSlopeDegrees + biomeParams[dominantBiome].slopeFadeDegrees,
    slope)
density *= slopeFade
if (density <= 0.0001) return
if (surfaceState.paved > 0.01 || surfaceState.scorched > 0.95) return

// 4. 4-test cull
//   (a) orientation: skip if planet-tangent angle to camera is too extreme (back-facing chunk)
//   (b) frustum: check rootWS + blade bounding sphere against camera planes
//   (c) distance: discard beyond MaxRenderDistance
//   (d) stochastic distance: id-mod-N drop based on distance (smooth density fall-off)
if (!passesCull(rootWS, upWS, ...)) return

// 5. Bézier control points (no wind in v1)
float h = biomeParams[dominantBiome].GrassHeight * (0.85 + hash01(hashSeed, 7) * 0.3)
float3 v0 = rootWS
float3 v2 = rootWS + upWS * h
float3 v1 = rootWS + upWS * h * 0.5  // straight up in v1; Phase D bends with wind

// 6. Lookup clump for color
uint clumpId = nearestClumpId(laneUv, clumpBuffer)

// 7. Atomic write
uint slot = atomicAdd(indirectArgs.instanceCount, 1)
instanceBuf[slot] = makeBlade(v0, v1, v2, h, biomeParams[dominantBiome].GrassWidth, rot, clumpId, dominantBiome)
```

Output: `IndirectArgsBuffer.instanceCount` = visible blade count; `InstanceBuffer[0..count]` = blade data.

### 4.3 BiomeParams buffer

Per-biome grass params (read by the compute kernel by `biomeId`):

```hlsl
struct BiomeGrassParams {
    float density;
    float height;
    float width;
    float clumpStrength;
    float maxSlopeDegrees;
    float slopeFadeDegrees;
    float minWaterClearance;
    float biomeBlendPower;
    float3 tintBase;
    float _padding1;
};
StructuredBuffer<BiomeGrassParams> _BiomeGrassParams;  // length = registry.BiomeCount
```

Uploaded once at planet init (by extending `BiomeSurfaceTextureArrays.Build()` or in a new sibling class). Re-uploaded if biome definitions change (rare).

---

## 5. Rendering pass

### 5.1 Vertex-shader strip generation

`Graphics.RenderPrimitivesIndirect` is invoked per visible chunk with:
- `vertexCount` = either 15 or 7 (LOD-dependent)
- `instanceCount` = read from `IndirectArgsBuffer`
- no template mesh; the shader builds each blade strip from `SV_VertexID` and `SV_InstanceID`

`Graphics.RenderMeshIndirect` is not the default path here because it is mesh-template driven. We can still smoke-test it in Unity 6, but the design should assume fully procedural non-indexed drawing unless that test proves otherwise.

Each vertex's role determined by `SV_VertexID % NumVertsPerBlade`:
```
vid = 0  → left base of v0
vid = 1  → right base of v0
vid = 2  → left side at Bézier(t=1/7)
vid = 3  → right side at Bézier(t=1/7)
vid = 4  → left side at Bézier(t=2/7)
...
vid = 13 → left at tip area
vid = 14 → tip point (single vertex apex)
```

(15-vert blade = 7 quads stacked + 1 tip = 15 verts; 7-vert LOD = 3 quads + 1 tip = 7 verts.)

The vertex shader:
1. Reads instance from `InstanceBuffer[SV_InstanceID]`
2. Computes t = (vid / 2) / (NumQuads) along the blade
3. Evaluates quadratic Bézier at t → spineWS
4. Computes side offset: `(vid % 2 == 0 ? -1 : +1) * width * widthFalloff(t)`
5. Applies side offset along the tangent-plane right vector (perpendicular to both `upWS` and `forwardWS`)
6. Outputs vertex with proper normal (rounded across blade width: `lerp(spineNormal, sideNormal, abs(sideOffset/width))`)

### 5.2 LOD switch

Per chunk, the indirect-args buffer is pre-populated with `vertexCountPerInstance = 15` OR `7` depending on `cameraDistance < LowLodDistance`. Set during the per-frame dispatch setup (CPU side, before compute).

### 5.3 Fragment shader

- Sample `_BiomeFlatColors[biomeId]` for terrain-matching base color.
- Multiply by clump base color from `ClumpBuffer[clumpId].baseColor`.
- Apply per-vertex height-darken: `multiply by lerp(0.6, 1.0, t)` — bottom is darker, simulating shadow + AO.
- Apply analytic sun (same path as terrain shader) using the rounded vertex normal.
- Output as opaque (no alpha-blend; blades are thin enough that we don't bother with the perf cost of transparency).

---

## 6. Integration with Phase B

| Phase B asset | Phase C use |
|---|---|
| Face-space `_BiomeIds` atlas (RGBA8 point) | Placement compute reads top-4 biome ids per lane to match the same biome field terrain renders |
| Face-space `_BiomeWeights` atlas (RGBA8 point) | Placement compute weights density by the top-K biome blend, avoiding hard dominant-id cutoffs |
| `_BiomeFlatColors` (per-planet 1×N LUT) | Clump baker + fragment shader sample by biome id |
| `ChunkedSurfaceProvider.RebakeBiomeMapsAt` (step 9 stub) | Triggers clump re-bake when biome changes (Phase E) |
| `GrassSurfaceAtlasGpuData` (new Phase C bridge) | Compute samples face-space radius + smoothed normal to place blade roots on the cached terrain surface without LOD kinks |
| `IChunkVisibilitySource` (new Phase C bridge) | `GrassPlacementController` receives the visible chunk set without depending on private provider internals |

Phase C does not modify Phase B's shader path — terrain rendering is unchanged. Grass renders on top via separate draw calls.

---

## 7. Implementation steps

Sequenced for incremental verification.

| Step | Description | Verifiable outcome |
|------|-------------|--------------------|
| 1 | Add `BiomeDefinition` grass fields + serialize. | Inspector shows new fields on every `BiomeDefinition` asset. |
| 2 | Build `BiomeGrassParams` `StructuredBuffer` upload (parallel to flat-color LUT). | Buffer bound globally; `Shader.GetGlobalBuffer` returns non-null. |
| 3 | `IGrassQualitySettings` interface + `DefaultGrassQualitySettings`. Register in `GameBootstrap`. | `ServiceLocator.Get<IGrassQualitySettings>()` returns the default impl. |
| 4 | Add Planet-assembly `IChunkVisibilitySource` to the high-resolution surface provider. | Grass can list the current visible chunks and receives show/hide events during LOD changes without putting `PlanetChunk` in Core. |
| 5 | Build face-space `GrassSurfaceAtlasGpuData` from cached max-depth chunk terrain arrays. | A debug material can show radius/normal atlas samples matching rendered terrain across chunk and cube-face boundaries. |
| 6 | Add `GrassChunkRuntime` allocation/dispose owned by `GrassPlacementController`, not private provider internals. | Runtime grass buffer count tracks visible grass chunks and releases when chunks hide. |
| 7 | Unity draw smoke test: `RenderPrimitivesIndirect` first, `RenderMeshIndirect` only if proven. | One debug blade strip renders from `SV_VertexID`/`SV_InstanceID` without a mesh template. |
| 8 | `BiomeGrassPlace.compute` with `PlaceAndCull` only, constant white blade, face-atlas biome sample, surface-atlas root placement, water/soft-slope/state gates, and coarse-LOD skip. | `IndirectArgsBuffer.instanceCount` is non-zero only on sufficiently detailed valid grass terrain; no underwater grass and steep slopes fade out instead of speckling. |
| 9 | `Grass.shader` vertex-shader strip generation + indirect draw. | **First grass on screen.** White straight triangle-strip blades sit on the terrain surface in valid grass biomes. |
| 10 | Plumb top-K biome density + height + width from `BiomeGrassParams`. | Biome blends produce soft density transitions; different biomes show different blade dimensions. |
| 11 | Per-vertex height-darken + base biome/clump color tint. | Blades are terrain-matched, with darker bases. |
| 12 | `ClumpBake` kernel + `ClumpBuffer`. | Visible clustering; neighboring blades share clump color and facing variation. |
| 13 | Vertex-sliding LOD (15 to 7 verts) + distance density fade. | Far blades simplify and fade with no obvious popping. |
| 14 | 4-test cull (orientation + frustum + distance + stochastic-id distance jitter). | Visible instance counts shrink dramatically away from camera; no bare patches at thresholds. |
| 15 | Rounded normals across blade width + glancing-angle widening. | Lighting looks soft on sides of blades; no flat-shaded appearance. |
| 16 | Debug modes: `DEBUG_GRASS_DENSITY`, `DEBUG_GRASS_CULL_REASON`, `DEBUG_GRASS_CLUMP_ID`, `DEBUG_GRASS_SURFACE_SAMPLE`, `DEBUG_GRASS_LOD`. F10 capture set entries. | Heatmaps render correctly and capture metadata includes visible chunks, instances, dispatches, draw calls, and buffer MB. |

Each step lands as its own commit so bisecting regressions stays cheap. Steps 1-7 ship plumbing and API proof; step 9 is the **first visible-grass moment** and the sanity gate before continuing visual polish.

### 7.1 Verification gates

Do not move from one gate to the next until the current one is visually and diagnostically clean:

1. **API proof:** visible chunk snapshot/events match the terrain chunks that are actually rendered.
2. **Surface proof:** `DEBUG_GRASS_SURFACE_SAMPLE` shows root radius/normal matching terrain, including hills, shorelines, and LOD transitions.
3. **Biome proof:** `DEBUG_GRASS_DENSITY` matches `BiomeMapPrimaryId` / `BiomeMapBlend` F10 captures and has no parent/child LOD kink.
4. **Seam proof:** circle cube-face borders and chunk LOD boundaries; edge density should not form visible grid lines.
5. **Perf proof:** F10 metadata includes visible grass chunks, emitted instances, draw calls, buffer MB, FPS, and frame time.

If a visible artifact survives one pass, add a hard isolation debug mode before tuning constants. Examples: force all grass roots to magenta, force density to 1, bypass biome weighting, bypass surface-state gates, or force one clump id.

---

## 8. Memory budget

Per active chunk (with grass biome dominant):
- `InstanceBuffer`: 4096 × 56 B = 224 KB
- `IndirectArgsBuffer`: 20 B
- `ClumpBuffer`: 16 × 48 B = ~1 KB
- **Per active grass chunk total: ~225 KB**

Shared per-planet grass surface atlas:

- Same atlas resolution as the max-depth biome atlas: `leafsPerAxis * (BiomeMapResolution - 1) + 1`.
- At `MaxChunkDepth=4`: `16 * 63 + 1 = 1009` texels per face.
- Target formats: first pass uses `RFloat` radius + `RGBA32` packed xyz normal; later optimize to `RHalf` radius + oct-encoded normal (`RG16`/`RGBA32`) after proof.
- Budget at depth 4: roughly **37-49 MB total** across six faces, depending selected formats.
- At depth 5 this grows ~4×; grass quality settings should cap grass surface atlas resolution independently if needed.

`BiomeGrassParams` buffer (per planet): 16 biomes × 32 B = 512 B.

At ~80 visible chunks (typical with our `MaxChunkDepth=4` + camera near surface): ~18 MB active grass chunk GPU buffers, plus the shared face-space surface atlases.

Combined with Phase B's ~700 MB total, comfortable on PC. On mobile we'd need to shrink lane resolution; deferred.

---

## 9. Performance budget (rough)

Per frame, per visible grass chunk:
- 1 compute dispatch (`PlaceAndCull`, 64×64 threads = 4096 threads, each does ~10 reads + ~5 writes).
- 1 indirect draw call (up to 4096 blade instances, each 7-15 verts).

At 80 visible grass chunks: 80 dispatches + 80 draws + 327,680 placement lanes before culling. Chunks whose biome/state gates resolve to zero grass should skip dispatch entirely after their density summary is known.

This is the standard JAHRMANN/GoT footprint. Should run at 60 FPS at MaxBladesPerLane=1 on a 2026 PC. If profiling shows draw-call overhead is the bottleneck, the indirect rendering can be batched via `MultiDrawIndirect` (DX12/Vulkan) — deferred.

Required counters for every F10 grass capture:

- visible grass chunks
- dispatched chunks
- emitted blade instances
- indirect draw calls
- grass GPU buffer MB
- CPU frame time + FPS from the normal debug metadata

No per-frame GPU readback for these counters in normal play. Debug capture can read back a small aggregate counter buffer after the frame if needed.

---

## 10. Open questions

1. **Voronoi clump pre-bake vs runtime** — I chose pre-bake (compute writes the clump buffer once per chunk activation). If clumps need to change with seasons (Phase E), we re-bake on demand via the same `RebakeBiomeMapsAt`-style hook. Confirm pre-bake is right?
2. **Settings menu timing** — when does `IGrassQualitySettings` get a real backing implementation? Phase D when wind interaction needs CPU plumbing anyway, or earlier?
3. **Per-biome blade meshes** — research doc doesn't specify, my design assumes a single procedural strip with biome-driven width/height. Should tall jungle grass look mesh-distinct from short prairie grass? If yes, we'd need per-biome mesh templates and the vertex shader picks via `biomeId`. Deferring this unless you say otherwise.
4. **Slope fade defaults** — the design uses smoothed atlas normals and a soft fade band around `GrassMaxSlopeDegrees`. Default `GrassSlopeFadeDegrees = 5` is a starting value; validate with `DEBUG_GRASS_DENSITY` before treating it as final.
5. **Shadow integration** — the per-vertex height-darken plus terrain shadow is plenty for v1, but URP's main-light shadow cascade is currently disabled for terrain (per Phase B step 8 comments). Should grass try to receive cascade shadows, or stick with the cheap analytic path?

---

## 11. Phase D / E readiness (preview)

Phase D (wind + animation):
- Adds `IWindFieldProvider` interface + `ScrollingPerlinWindProvider`.
- Modifies the `PlaceAndCull` kernel: `v1` and `v2` get displaced by `windForce × biome.GrassClumpStrength × (1 - biome.GrassBendStiffness)`.
- Tessellation sanity-check happens here if Bryan wants to enable it later.

Phase E (modification):
- Adds the surface state stack expansion (`WeatherState`, `TrackMap`, `SeasonalState`).
- Compute reads `_SurfaceStateMask.r` (paving) and skips blade placement on paved texels.
- Reads `.g` (scorched) and renders blade with scorch tint or skips entirely.
- Reads `.b` (snow depth) and skips blades buried deeper than `GrassHeight × 0.7`.
- Reads `.a` (wetness) → darkens blade color + droops `v2` toward ground.
