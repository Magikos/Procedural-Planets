# Biome textures + surface state — Phase B

> **Updated 2026-06-06:** This doc is a **historical draft**. The actual shipped Phase B implementation went further than this draft proposed — it shipped at **K=4** (not K=2), with three separate per-chunk textures (`BiomeBlendedColorTexture`, `BiomeIdsTexture`, `BiomeWeightsTexture`) instead of the single primary+secondary+blend RGBA8 map drafted below. Per-biome `Texture2DArray` triplanar sampling for albedo/normal/ARM is also live. See [biome-climate-overhaul §0.1](2026-06-05-biome-climate-overhaul.md#01-top-k--4-already-shipped--confirmed-k4-not-k2-as-the-phase-b-draft-suggested) for the as-shipped reality.

**Date:** 2026-05-31
**Status:** Historical draft. Code shipped past this; biome-climate-overhaul doc captures the as-built state.
**Source-of-truth this implements:** [docs/research/2026-05-30-grass-and-chunks.md](../research/2026-05-30-grass-and-chunks.md) — Phase B row of "Phase plan (recap, decisions in bold)" + "Locked-in additions" notes.
**Predecessor:** [docs/design/2026-05-30-chunk-skeleton.md](2026-05-30-chunk-skeleton.md) — Phase A chunk system, now live.

---

## 1. Purpose and scope

Replace per-vertex biome colors with **per-pixel biome sampling** so biome boundaries are crisp even at low LOD, and **establish the per-chunk surface-state plumbing** that Phases C-E (grass placement, modification, snow) will all consume.

**In scope (this phase):**

- Per-chunk **biome map** texture (RGBA8: primary biome id + secondary biome id + blend weight). Baked at chunk gen time, uploaded as a per-chunk `Texture2D` set on the chunk's renderer via a `MaterialPropertyBlock`.
- Per-planet **biome surface texture array** (`Texture2DArray`) — one slice per biome with albedo + normal + ARM (ambient occlusion / roughness / metallic). Built once at planet init from `BiomeRegistry`.
- Per-chunk **surface state mask** (RGBA8, same resolution as biome map). Allocated empty at gen, reserved for Phase E (paving/scorching/snow accumulation). Bound but unread by the shader in Phase B — proves the upload + binding path works without depending on Phase E content.
- Terrain shader path: sample biome map → fetch albedo/normal/ARM slices from the texture array via `Texture2DArray.SampleLevel` → triplanar blend (cube-sphere has no good single-axis UV).
- New `IBiomeMapBuilder` interface + `BiomeMapBuilder` Burst job that produces the biome map for a chunk. Single source of truth — grass placement (Phase C) and biome debug shaders (already exist) both sample it.
- Migration story for the existing `CalculateColors` / `CpuColors` / `CpuBiomeData` per-vertex path — kept as a fallback (low-resolution path / debug overlays still need it), but the terrain material switches to the texture path by default.

**Out of scope (later phases):**

- Reading the surface state mask in the shader (Phase E).
- Grass density sampling from the biome map (Phase C consumes it but the consumer is Phase C's responsibility).
- Procedural detail texturing (e.g. macro variation noise that breaks up triplanar repeat). Defer to a "Phase B polish" follow-up if needed.
- Biome editor UX (the `BiomeRegistryEditor` already exists; updates to expose texture slots are tracked in §8 but not the core design).

**Non-goals:**

- Replacing the `IBiomeProvider` evaluation logic. Biome assignment per point on the unit sphere stays as today — Phase B just rasterizes that into a texture per chunk instead of evaluating per vertex.
- Changing `BiomeType` enum or `BiomeRegistry`'s temperature/moisture grid layout.
- Touching the per-face `Low` resolution provider beyond what's needed to keep it compiling. Optional shader switching for low-res path is a §8 item.

---

## 2. Decisions (locked in 2026-05-31)

| # | Question | Decision |
|---|----------|----------|
| 1 | Per-chunk biome map storage format | **RGBA8: R = primary biome id (0-255), G = secondary biome id (0-255), B = blend weight to secondary biome (0-255).** This avoids bilinear-filtered or neighbor-inferred IDs producing invalid intermediate biome slots. |
| 2 | Shader sampling approach | **`Texture2DArray` indexed by biome id, sampled triplanar.** One slice per biome carries albedo / normal / ARM. Per-pixel crisp boundaries with proper surface texture detail. |
| 3 | Bake cadence | **Bake at planet gen AND maintain a separate per-chunk runtime override mask.** Biome map is permanent after gen; the surface state mask (Phase E content) is the runtime-mutable channel. Two textures, separate roles, no conflict. |

These three decisions came from the AskUserQuestion exchange in the 2026-05-31 session.

---

## 3. Data model additions

### 3.1 `PlanetChunk` new fields

```csharp
// Phase B: GPU biome map (RGBA8). Baked at chunk gen time. Lifetime tied to the chunk.
public Texture2D BiomeMapTexture;

// Phase B: secondary biome id per texel (CPU-side copy of the map's G channel). Kept as a byte[] for cheap
// re-baking after Phase E modifications. Length = BiomeMapResolution * BiomeMapResolution.
public byte[] BiomeMapSecondaryIds;

// Phase B: GPU surface state mask (RGBA8). Allocated empty at gen, written to in Phase E.
// Channels (Phase E spec, here just for plumbing): R = paved alpha, G = scorched alpha,
// B = snow depth, A = wetness. Phase B binds but does not sample.
public Texture2D SurfaceStateTexture;
```

`BiomeMapResolution` (compile-time const): **64** — matches the research doc's recommendation and gives ~10 cm/texel at max LOD on Earth-sized planets.

### 3.2 Per-chunk Material binding

Phase A's per-chunk MeshRenderer path already exists. Phase B sets per-chunk material properties via `MaterialPropertyBlock`:

```csharp
_propertyBlock.SetTexture("_BiomeMap", chunk.BiomeMapTexture);
_propertyBlock.SetTexture("_SurfaceStateMask", chunk.SurfaceStateTexture);
_propertyBlock.SetVector("_BiomeMapUvScale", new Vector4(1, 1, 0, 0));
// Texture array is bound globally on the material — same for every chunk.
renderer.SetPropertyBlock(_propertyBlock);
```

The `Texture2DArray` is bound on the **shared material**, not the property block — every chunk uses the same per-planet array.

### 3.3 `IPlanetSurfaceProvider` — new method

```csharp
// Re-bakes a chunk's biome map (e.g. after world-action-driven biome shift). Idempotent
// if state hasn't changed. Returns false if the chunk is not currently active or its CPU
// vertex data is missing. Phase B does NOT call this during normal play — it's the Phase E
// entry point for modifications that should permanently change biome assignment.
bool TryRebakeBiomeMap(PlanetChunk chunk);
```

Phase B only invokes the bake from the gen pass; this method is the Phase E hook, declared now so the interface doesn't churn.

---

## 4. Bake pipeline (planet gen path)

### 4.1 When it runs

Inserted into `ChunkedSurfaceProvider.GenerateAsync` between **step 4 (chunk mesh jobs complete)** and **step 5 (water sampler builds)**. The water sampler reads `CpuElevations`; biome map bake reads `CpuUnitSpherePoints` + `CpuElevations` — same prerequisites as water, can run in parallel.

Pseudo-flow per chunk:

```
1. Allocate NativeArray<byte4> map (64×64).  // R=primary id, G=secondary id, B=blend weight
2. Allocate NativeArray<byte> secondaryIds (64×64).
3. Schedule BiomeMapBakeJob (Burst IJobParallelFor over 4096 texels):
     - For each texel uv ∈ [0,1]² in chunk space:
         localPoint = ChunkUvToUnitSphere(chunk, uv);
         elevation = BilinearSample(chunk.CpuElevations, uv);
         biomeResult = IBiomeProvider.EvaluateBiome(localPoint, elevation);
         map[i] = (byte)biomeResult.PrimaryBiome
         map[i].g = (byte)biomeResult.SecondaryBiome
         secondaryIds[i] = map[i].g
         blend = clamp01(biomeResult.BlendWeight) * 255
         map[i].g = (byte)blend
4. JobHandle chain: all 6 face roots' descendants in flight at once, batched same as mesh jobs.
5. On main thread, upload each chunk's map to a Texture2D (TextureFormat.RGBA32,
    filterMode = Bilinear, wrapMode = Clamp, mipMapBitmask = false).
6. Allocate SurfaceStateTexture: 64×64 RGBA8, cleared to zero. No upload needed beyond Apply.
```

The `IBiomeProvider.EvaluateBiome` call is **not Burst-friendly** as it stands (it goes through ScriptableObject lookups). Options for the job:

- **Option A (recommended):** Build a flat `NativeArray<BiomeRegistryData>` snapshot at gen start (analogous to `ShapeGenerator.BuildNoiseFilterData`). Eliminate the SO indirection inside the job.
- **Option B:** Run the bake on a `Parallel.For` background thread (like `PerFaceSurfaceProvider.GenerateColorsAsync` does today). Simpler, slightly slower, no Burst marshaling.

**Lean toward Option A** because the biome lookup is hot — every texel × every chunk × 6 faces. At depth 4 with 64² maps that's 4096 × 1364 chunks/planet = ~5.6 million `EvaluateBiome` calls. Burst Option A is probably ~10× faster than Parallel.For Option B.

### 4.2 Biome lookup data snapshot

```csharp
// Burst-compatible mirror of BiomeRegistry. Built once per gen.
public struct BiomeLookupData
{
    public int TemperatureSteps;
    public int MoistureSteps;
    public float OceanThreshold;
    public float BeachWidth;
    public float MountainThreshold;
    public float BlendWidth;
    public byte OceanBiomeId;
    public byte BeachBiomeId;
    public byte MountainBiomeId;
    public byte SnowyMountainBiomeId;
    // Grid: flat array of biome ids, length = TemperatureSteps * MoistureSteps.
    public NativeArray<byte> GridBiomeIds;
}
```

Biome ids in the snapshot use the same indexing as `BiomeRegistry.GetDefinitionByIndex`: 0=Ocean, 1=Beach, 2..N+1=grid, N+2=Mountain, N+3=SnowyMountain. The shader uses **this index** for its `Texture2DArray` slice lookup.

`TemperatureSteps` and `MoistureSteps` are cell counts, not control-point counts. A 4x3 registry has four reachable temperature rows and three reachable moisture columns; lookup uses `floor(value * steps)` with clamping at `1.0`.

Also needs a temperature/moisture sampler — the current `IBiomeProvider` implementation samples noise. The simplest path is to add `(temperature, moisture)` to `PlanetChunk.CpuBiomeData` (already populated by Phase A's color pass) and consume those for the bake. Done — no new noise eval needed.

### 4.3 Secondary biome id encoding

The biome map stores **primary id + secondary id + blend weight** explicitly. Biome IDs are point-sampled, while the blend weight is produced by the bake and manually bilinear-sampled in the shader:

```hlsl
float3 bm = SAMPLE_TEXTURE2D(_BiomeMap, sampler_BiomeMap, uv).rgb;
uint primaryId = (uint)(bm.r * 255.0 + 0.5);
uint secondaryId = (uint)(bm.g * 255.0 + 0.5);
float blendWeight = bm.b;
```

This costs one extra byte per texel compared to the two-channel map, but it removes the fragile 4-tap secondary inference path and keeps the terrain shader, grass placement, and future modification systems on the same IDs. The blend weight is capped at 0.5 on either side of a grid-cell boundary so adjacent cells meet at the same 50/50 color instead of swapping fully to the opposite biome. Elevation overrides use the same contract: ocean/beach/land and land/mountain thresholds write a secondary biome plus a bounded blend weight instead of returning hard single-biome overrides.

Biome-map texels are baked as an edge-inclusive grid (`u/v = x/(resolution-1)`) instead of texel centers. This makes neighboring chunks agree on shared edges. The shader still treats R/G as discrete ids, but it resolves each sampled texel to a biome color first and then bilinear-filters the resolved colors. That avoids interpolating raw ids while also preventing visible id stair-steps in terrain albedo.

Rendered terrain samples a **face-space biome atlas** built from max-depth leaf chunk maps, not the currently rendered chunk's local map. This is important for LOD: a parent chunk and a child chunk must sample the same world-space biome field or climate/elevation contours visibly kink at the LOD boundary. Per-chunk biome maps remain useful as CPU-side data products for grass/placement and future rebakes, but the material property block binds `_BiomeMap` to the face atlas and uses `_BiomeMapUvScale` to map chunk-local UVs into face UVs.

`BiomeMapSecondaryIds` is kept on the CPU because the BakeJob computes it for free during the EvaluateBiome call — useful for grass placement (Phase C samples primary AND secondary biome to weight density) without needing the shader's neighbor-tap trick.

---

## 5. Biome surface texture array

### 5.1 Asset structure

Each `BiomeDefinition` gains:

```csharp
public Texture2D SurfaceAlbedo;       // sRGB, expected ~512×512
public Texture2D SurfaceNormal;       // linear, BC5 or RG normal map
public Texture2D SurfaceARM;          // linear, R=AO, G=Roughness, B=Metallic
public float SurfaceTiling = 1.0f;    // world-space tiling per meter
```

Authoring constraint: every biome's three textures must share dimensions + format (else they can't go into a single `Texture2DArray`). The editor validates this on import.

### 5.2 Array build at planet init

Done once when the planet acquires its `IBiomeProvider`:

```
foreach biome id (in BiomeRegistry index order):
    slice ← GetDefinitionByIndex(id)
    if slice.SurfaceAlbedo == null: use _missingAlbedoFallback (magenta)
    Graphics.CopyTexture(slice.SurfaceAlbedo, 0, 0, _biomeAlbedoArray, id, 0)
    // same for normal + ARM
```

Three `Texture2DArray`s total per planet:

- `_BiomeAlbedoArray` — `RGBA32` or compressed `DXT1`
- `_BiomeNormalArray` — `BC5` (or `RG16` if BC5 unavailable on platform)
- `_BiomeArmArray` — `RGBA32`

Memory (estimate, 17 biomes × 512²):
- Albedo DXT1: 17 × 128 KB = ~2.1 MB
- Normal BC5: 17 × 256 KB = ~4.3 MB
- ARM uncompressed: 17 × 1 MB = ~17 MB

Total per planet: ~24 MB. ARM is the costly one — option to switch to BC1 for a 6× reduction at the cost of metallic precision (we don't use metallic much for natural surfaces, so likely fine — defer to perf testing).

### 5.3 Shader binding

```hlsl
// In the terrain shader's _Properties block:
TEXTURE2D_ARRAY(_BiomeAlbedoArray);   SAMPLER(sampler_BiomeAlbedoArray);
TEXTURE2D_ARRAY(_BiomeNormalArray);   SAMPLER(sampler_BiomeNormalArray);
TEXTURE2D_ARRAY(_BiomeArmArray);      SAMPLER(sampler_BiomeArmArray);
TEXTURE2D(_BiomeMap);                  SAMPLER(sampler_BiomeMap);
TEXTURE2D(_SurfaceStateMask);          SAMPLER(sampler_SurfaceStateMask);
float4 _BiomeMap_TexelSize;
float4 _BiomeMapUvScale;
```

Bound globally:
- The 3 arrays — on `Material` (set once at planet init via `Material.SetTexture`).
- `_BiomeMap`, `_SurfaceStateMask`, `_BiomeMapUvScale` — per chunk via `MaterialPropertyBlock`.

---

## 6. Terrain shader changes

Two passes: the **color pass** (samples biome map, fetches texture array, triplanar blends) and the **fallback** (existing vertex-color path, used at low resolution / for debug visualization).

### 6.1 Sample biome map → resolve biomes

```hlsl
float3 bm = SAMPLE_TEXTURE2D(_BiomeMap, sampler_BiomeMap, chunkUv).rgb;
uint primaryId = (uint)(bm.r * 255.0 + 0.5);
uint secondaryId = (uint)(bm.g * 255.0 + 0.5);
float blendT = bm.b;  // [0,1]
```

### 6.2 Triplanar fetch per biome

The cube-sphere has no good single-axis UV — UV from cube face → unit sphere distortion is too uneven for big triplanar tiles. Solution: world-space triplanar driven by `worldPos` and `worldNormal`.

```hlsl
float3 blendWeights = pow(abs(worldNormal), 4);
blendWeights /= dot(blendWeights, 1);

float3 albedoPrimary = TriplanarSampleArray(_BiomeAlbedoArray, worldPos, blendWeights, primaryId, tilingFactor);
float3 albedoSecondary = (blendT > 0.001)
    ? TriplanarSampleArray(_BiomeAlbedoArray, worldPos, blendWeights, secondaryId, tilingFactor)
    : albedoPrimary;
float3 albedo = lerp(albedoPrimary, albedoSecondary, blendT);
// Same pattern for normal + ARM.
```

That's 3 triplanar samples × 2 biomes × 3 arrays = up to **18 texture reads per fragment** in the worst case (both biomes different, full triplanar). At typical screen coverage this is acceptable on a 2026-era GPU but should be measured. Optimizations available:

- **Biaxial planar** when surface normal is dominantly aligned (skip the cheapest axis): cuts ~33% of fetches.
- **Skip secondary sample** when `blendT < 0.05`: dominant when biomes are far from boundaries.
- **Lower-mip sampling** at distance via SAMPLE_TEXTURE2D_ARRAY_LOD with computed mip — bandwidth saving on far chunks.

All three are profile-driven; ship the basic path first.

### 6.3 Fallback path for Low resolution / debug

Keep the existing per-vertex color path live behind a shader feature `_BIOME_COLOR_MODE_VERTEX`. The `PerFaceSurfaceProvider` (low res) keeps using it — no behavior change. The `ChunkedSurfaceProvider` (high res) switches to `_BIOME_COLOR_MODE_TEXTURE`.

Debug modes for biome IDs / temperature / moisture (already registered in `WaterDebugModule`) keep working — they read `CpuBiomeData`, which Phase B still populates.

---

## 7. Memory budget

Per-chunk new cost at `BiomeMapResolution = 64`:

| Asset | Format | Size |
|-------|--------|------|
| BiomeMap (GPU) | RGBA8 | 16 KB |
| BiomeMapSecondaryIds (CPU) | byte[4096] | 4 KB |
| SurfaceStateMask (GPU) | RGBA8 (RGBA32) | 16 KB |
| **Per-chunk total** | | **~28 KB** |

At `MaxChunkDepth = 4` (current default), 1364 chunks/planet × 28 KB = **~38 MB additional**. Combined with Phase A's ~660 MB chunk vertex data = **~700 MB total**.

At `MaxChunkDepth = 3` (lighter config): 340 chunks × 28 KB = ~9.5 MB. Combined: ~175 MB total.

Per-planet biome array: ~24 MB (see §5.2). Independent of chunk count.

**Conclusion:** Phase B adds ~60 MB at depth 4, ~35 MB at depth 3. Well within budget.

---

## 8. Migration / compatibility

### 8.1 Existing code that breaks

- `BiomeDefinition` gains 3 nullable texture fields + 1 float. Existing assets continue to load (Unity initializes new fields to default). All current biome definitions need texture authoring before Phase B looks correct in play — until then they render the magenta `_missingAlbedoFallback`.
- `BiomeRegistry` is unchanged structurally.
- Terrain shader keyword change: `_BIOME_COLOR_MODE_TEXTURE` becomes the default. The vertex-color path is kept (set the keyword off) for the Low resolution provider.

### 8.2 Editor work

- `BiomeRegistryEditor` already exists. Phase B adds:
  - Texture preview tile per biome (albedo).
  - Validation warning when biome definitions in the registry have mismatched texture sizes/formats.
  - "Bake biome texture array" button (forces rebuild of the per-planet `Texture2DArray` and serializes to a `.asset` for runtime load).
- `BiomeDefinition` inspector gains the 4 new fields with a help box "Required for Phase B (Texture-mode terrain rendering)".

### 8.3 Cleanup of `CpuColors`

`CpuColors` stays populated for now — debug modes use it. Remove in a Phase B polish pass once the texture path is validated and the debug visualization is moved to a separate path that samples the biome map directly. Tracked as a TODO in the implementation, not blocking Phase B sign-off.

---

## 9. Implementation steps (rough order, each independently mergeable)

| Step | Description | Verifiable outcome |
|------|-------------|--------------------|
| 1 | Add `BiomeLookupData` snapshot + `BiomeRegistry.BuildLookupData()`. | Unit-style sanity check: snapshot returns identical biome ids as `Resolve()` for random temp/moisture/elevation samples. |
| 2 | Add `BiomeDefinition` texture fields. Build per-planet `Texture2DArray`s at planet init. | In play, inspect `Material._BiomeAlbedoArray` — non-null, slice count = `BiomeCount`. |
| 3 | Add `PlanetChunk.BiomeMapTexture` + `SurfaceStateTexture`. Allocate empty on chunk creation, dispose on chunk unload (Phase A's `ReleaseChunk` path). | No GPU leak across regen; chunk count = texture count via diagnostic readout. |
| 4 | `BiomeMapBakeJob` (Burst) + integration into `GenerateAsync`. | Generated chunks have `BiomeMapTexture` with non-zero R channel matching expected biome distribution. Add a debug capture mode "BiomeMapPrimary" that draws raw `bm.r * 255 / BiomeCount` as a heatmap. |
| 5 | Terrain shader: keyword `_BIOME_COLOR_MODE_TEXTURE`, sample biome map, single primary triplanar (no blend / no array yet — just primary biome's albedo). | Planet renders with crisp per-pixel biome boundaries but flat-colored within each biome. |
| 6 | Wire `Texture2DArray` sampling into the shader. Triplanar primary albedo. | Surfaces show biome texture detail. |
| 7 | Add secondary biome blend lerp. | Biome boundaries gradient-blend over the bake's `BlendWidth`. |
| 8 | Add normal + ARM array sampling. PBR lighting matches biome surfaces. | Lighting picks out per-biome roughness/normal detail. |
| 9 | `SurfaceStateTexture` binding (no shader reads yet) + Phase E API stub (`TryRebakeBiomeMap`). | Texture allocated, bound to material, profiler shows ~16 KB per chunk for it. |
| 10 | Editor polish: array build button, validation, biome preview tiles. | Authoring workflow exists; missing textures call out clearly. |

Steps 1-4 can land in one PR (data + bake plumbing, no shader work). Steps 5-8 should each be standalone PRs to ease bisecting visual regressions. Step 9 is small; step 10 is editor-only.

---

## 10. Open questions

1. **Biome texture authoring** — does Bryan have surface textures ready, or is Phase B blocked on art creation? If blocked: ship steps 1-4 + 9 (data path + plumbing) and stub the shader path with per-biome flat tint colors lifted from `BiomeDefinition.TintColor`. That gives crisp boundaries today without needing textures, and steps 5-8 land when art is ready.
2. **Triplanar tiling factor** — fixed per biome (`SurfaceTiling` on `BiomeDefinition`), or computed dynamically from camera distance for LOD anti-tile? Recommend fixed for v1, dynamic if visible repeating becomes a problem.
3. **Shader keyword vs always-on** — should the vertex-color fallback path be removed entirely once `ChunkedSurfaceProvider` is the only resolution we ship? Reduces keyword permutations. Keep both for Phase B; reconsider before Phase F.
4. **GPU upload cadence** — Step 4 uploads all chunk biome textures during the loading pass. At depth 4 that's 1364 × 8 KB = ~11 MB of upload across the gen pass. Batched via `Graphics.CopyTexture` per chunk should be fine, but worth confirming on slow GPUs.

---

## 11. Phase C readiness (placement preview)

Phase C (grass renderer) consumes from Phase B as follows:
- Reads the per-chunk biome map (RGBA8) to gate placement: `if (biomeDensity[primaryId] < threshold) skip`.
- Reads `BiomeMapSecondaryIds` (CPU side) at clump construction to seed clump color.
- Reads `SurfaceStateTexture.r` (paved mask) and `.g` (scorched mask) to skip placement on modified texels (Phase E feeds it; Phase C consumes whatever's there).

No interface coupling needed today — Phase C can read these fields off `PlanetChunk` directly when the time comes.
