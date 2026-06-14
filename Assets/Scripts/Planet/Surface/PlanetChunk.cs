using UnityEngine;

// Quadtree node for a single chunk of a planet face. Identity lives in (FaceIndex, HashValue);
// geometry in UV space (face-local) is (UvCenter, UvHalfExtent). World/sphere data is derived
// lazily — Phase A step 3 only needs identity + tree links; mesh data lands in step 4-5.
//
// Hash bit encoding (step 3 final form):
//   - Face root: HashValue = 1 (leading 1 sentinel preserves leading zeros in children)
//   - Child: HashValue = (parent.HashValue << 2) | quadrant
//   - DetailLevel = (Log2(HashValue) >> 1)
//   - Max depth = 15 (32-bit hash: 1 sentinel + 30 quadrant bits)
//
// Quadrant encoding (NOTE: differs from LOD-Planets CCW convention for clean bit math):
//   bit 0 = u-bit  (1 = East half of parent)
//   bit 1 = v-bit  (1 = South half of parent)
//   NW = 0b00 = 0   NE = 0b01 = 1
//   SW = 0b10 = 2   SE = 0b11 = 3
public sealed class PlanetChunk
{
    public const int MaxDetailLevel = 15;

    public readonly uint HashValue;
    public readonly int DetailLevel;
    public readonly int FaceIndex;
    public readonly byte Quadrant;        // 0..3, NW/NE/SW/SE; 0 for face root

    public readonly Vector2 UvCenter;     // face UV space, [0,1]²
    public readonly float UvHalfExtent;   // half-size in face UV (root = 0.5)

    public PlanetChunk Parent;            // null for face root
    public PlanetChunk[] Children;        // null for leaf, length 4 when subdivided

    public ChunkLifecycle State;
    public uint Generation;               // bumped on every state-affecting transition

    // ---- CPU mesh data (populated when a chunk job completes; step 5+) ---------------------
    public Vector3[] CpuVertices;
    public Vector3[] CpuUnitSpherePoints;
    public float[] CpuElevations;
    public float[] CpuVertexRadii;        // |CpuVertices[i]| — bilinear surface sampler input
    public Bounds CpuLocalBounds;         // Local-space bounds of CpuVertices for LOD/culling diagnostics.
    public Color[] CpuColors;
    // Compact retained vertex colors. The heavy Color[] is freed after the bake, but the pooled
    // render path rebuilds chunk meshes on page-in, so the colors must survive in a cheap form.
    // Color32 is what the GPU stores anyway, so this is lossless for rendering.
    public Color32[] CpuColors32;
    public Vector4[] CpuBiomeData;
    // Compact retained copy of the normalized biome diagnostics. The initial bake uses the
    // full Vector4 data, then pooled mesh uploads expand this RGBA8 source through one shared
    // scratch buffer. All four channels are authored in [0,1].
    public Color32[] CpuBiomeData32;
    // True terrain-aware vertex normals from PlanetChunkNormalsJob (cross-product of neighbor
    // tangents). Preferred over CpuUnitSpherePoints when uploading mesh normals — gives
    // proper lighting on terrain elevation features rather than smooth-sphere shading.
    public Vector3[] CpuNormals;

    // ---- Phase B step 5b: per-chunk top-K biome blending ------------------------------------
    // Pre-blended albedo per texel (RGBA8, bilinear filter). Cheap-path shader input: one
    // SAMPLE_TEXTURE2D gives the correct multi-biome blended color. Computed at bake time
    // from BiomeIdsTexture + BiomeWeightsTexture via the per-planet flat-color LUT. Bilinear
    // is correct here because color values interpolate meaningfully across texels.
    public Texture2D BiomeBlendedColorTexture;
    // Top-K (= 4) biome slice ids per texel (RGBA8, POINT filter — ids are categorical and
    // do not interpolate). Channels R,G,B,A = ids[0..3]. Consumed by Phase B step 6 (texture
    // array surface sampling) and Phase C grass (density / clump base color per blade).
    public Texture2D BiomeIdsTexture;
    // Top-K weights matching BiomeIdsTexture slot-for-slot (RGBA8, POINT filter). Weights
    // sum to 255 per texel. Filter is Point because the step 6 shader does its own manual
    // 4-corner bilinear (slot weights cannot interpolate across texels with different ids).
    public Texture2D BiomeWeightsTexture;
    // Surface state mask (RGBA8). Channels reserved for Phase E (paving/scorching/snow/wetness).
    // Allocated empty in Phase B step 3 just to prove the lifecycle path; bound but unread.
    public Texture2D SurfaceStateTexture;
    // Transient per-bake pixel buffers — written on the background bake thread, uploaded +
    // nulled on the main thread. Re-allocated by the bake on each planet regen.
    [System.NonSerialized] public Color32[] PendingBiomeBlendedColorPixels;
    [System.NonSerialized] public Color32[] PendingBiomeIdsPixels;
    [System.NonSerialized] public Color32[] PendingBiomeWeightsPixels;

    // The edge-fan mask used by the most recently scheduled mesh job. Stored so we can
    // detect "mask changed → re-mesh" cases when a neighbor's LOD shifts.
    public byte EdgeFanMaskAtSchedule;

    public PlanetChunk(uint hashValue, int detailLevel, int faceIndex, byte quadrant,
        Vector2 uvCenter, float uvHalfExtent, PlanetChunk parent)
    {
        HashValue = hashValue;
        DetailLevel = detailLevel;
        FaceIndex = faceIndex;
        Quadrant = quadrant;
        UvCenter = uvCenter;
        UvHalfExtent = uvHalfExtent;
        Parent = parent;
        State = ChunkLifecycle.Pending;
    }

    public bool IsLeaf => Children == null;
    public bool ContainsUv(Vector2 uv) =>
        uv.x >= UvCenter.x - UvHalfExtent && uv.x <= UvCenter.x + UvHalfExtent &&
        uv.y >= UvCenter.y - UvHalfExtent && uv.y <= UvCenter.y + UvHalfExtent;

    // Returns NW/NE/SW/SE for a UV that falls inside one of this chunk's four sub-quadrants.
    // Caller is responsible for checking ContainsUv first; this only inspects relative position.
    public byte QuadrantForUv(Vector2 uv)
    {
        byte q = 0;
        if (uv.x > UvCenter.x) q |= 0b01;
        if (uv.y > UvCenter.y) q |= 0b10;
        return q;
    }

    // Returns the UV center of a hypothetical child in the given quadrant.
    public Vector2 ChildUvCenter(byte quadrant)
    {
        float half = UvHalfExtent * 0.5f;
        float dx = (quadrant & 0b01) != 0 ?  half : -half;
        float dy = (quadrant & 0b10) != 0 ?  half : -half;
        return UvCenter + new Vector2(dx, dy);
    }

    // Bilinear sample of the per-vertex radius at the chunk's local UV (both axes in [0,1]
    // within this chunk's UV sub-region). Returns false if CPU data isn't populated yet.
    // Counterpart to TerrainFace.TrySampleSurfaceRadius for the chunked path.
    public bool TrySampleRadius(Vector2 chunkLocalUv, out float radius)
    {
        radius = 0f;
        if (CpuVertexRadii == null || CpuVertexRadii.Length == 0) return false;

        int r = (int)Mathf.Sqrt(CpuVertexRadii.Length);
        if (r * r != CpuVertexRadii.Length) return false; // not a square grid

        float gx = Mathf.Clamp01(chunkLocalUv.x) * (r - 1);
        float gy = Mathf.Clamp01(chunkLocalUv.y) * (r - 1);
        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, r - 1);
        int y1 = Mathf.Min(y0 + 1, r - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float r00 = CpuVertexRadii[x0 + y0 * r];
        float r10 = CpuVertexRadii[x1 + y0 * r];
        float r01 = CpuVertexRadii[x0 + y1 * r];
        float r11 = CpuVertexRadii[x1 + y1 * r];
        radius = Mathf.Lerp(Mathf.Lerp(r00, r10, fx), Mathf.Lerp(r01, r11, fx), fy);
        return radius > 0f;
    }

    public void ReleaseCpuDataAfterBake(
        bool retainSurfaceSamplingData,
        bool retainUnitSphereForWaterSampler)
    {
        // Heavy bake arrays are released after compact mesh sources have been retained.
        CpuColors = null;
        CpuBiomeData = null;

        if (!retainUnitSphereForWaterSampler)
            CpuUnitSpherePoints = null;

        if (retainSurfaceSamplingData)
            return;

        CpuElevations = null;
        CpuVertexRadii = null;
    }

    public long GetRetainedCpuDataBytes()
    {
        long bytes = 0;
        bytes += ArrayBytes(CpuVertices, sizeof(float) * 3);
        bytes += ArrayBytes(CpuUnitSpherePoints, sizeof(float) * 3);
        bytes += ArrayBytes(CpuElevations, sizeof(float));
        bytes += ArrayBytes(CpuVertexRadii, sizeof(float));
        bytes += ArrayBytes(CpuColors32, 4);
        bytes += ArrayBytes(CpuBiomeData, sizeof(float) * 4);
        bytes += ArrayBytes(CpuBiomeData32, 4);
        bytes += ArrayBytes(CpuNormals, sizeof(float) * 3);
        return bytes;
    }

    static long ArrayBytes(System.Array array, int elementBytes)
        => array == null ? 0L : (long)array.Length * elementBytes;

    // ---- Hash helpers ---------------------------------------------------------------------

    public static uint ChildHash(uint parentHash, byte quadrant) => (parentHash << 2) | quadrant;

    // Detail level of a hash = (position of leading 1 / 2). Hash=1 (root) → 0.
    public static int HashDetailLevel(uint hash)
    {
        if (hash == 0) return -1;
        int bit = 31;
        while ((hash & (1u << bit)) == 0) bit--;
        return bit >> 1;
    }
}

public enum ChunkLifecycle
{
    Pending,             // created, mesh/state not yet built
    Generating,          // mesh job in flight (step 4+)
    Active,              // leaf, mesh available, visible
    ActiveWithChildren,  // subdivided; this chunk's mesh is hidden in favor of children
    Subdividing,         // children Generating; this chunk's mesh still shown
    Merging,             // children being released; this chunk re-shown
    Unloading            // mesh disposed, chunk releasable
}

// Lifecycle helpers for the Phase B per-chunk biome map + surface state textures owned by
// PlanetChunk. Kept in this file because the current Unity-generated project explicitly
// includes source files; colocating it with PlanetChunk keeps local builds in sync.
public static class PlanetChunkTextures
{
    public const int BiomeMapResolution = 64;
    const int TexelCount = BiomeMapResolution * BiomeMapResolution;
    const int BytesPerRgba32Texel = 4;
    const int BiomeTexturesPerSet = 3;

    public static int LiveTextureSets { get; private set; }
    public static int LiveBiomeTextureSets { get; private set; }
    public static int LiveSurfaceStateTextures { get; private set; }

    // Top-K width: 4 biomes per texel, matching RGBA8 channel count. Pick any other K and
    // the textures change format / the shader's weighted sum loop has to change.
    public const int TopK = 4;

    public static void Allocate(PlanetChunk chunk)
    {
        if (chunk == null) return;
        Dispose(chunk);

        string suffix = $"F{chunk.FaceIndex}_D{chunk.DetailLevel}_H{chunk.HashValue}";

        // Pre-blended color: bilinear so neighbor-texel color values interpolate smoothly
        // across the grid (this is the cheap-path used by step 5).
        chunk.BiomeBlendedColorTexture = new Texture2D(BiomeMapResolution, BiomeMapResolution,
            TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = $"BiomeBlendedColor_{suffix}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Top-K biome ids: POINT filter — interpolating categorical ids would produce
        // nonsense. The shader does manual bilinear over 4 corner texels for ID-aware
        // sampling (step 6+).
        chunk.BiomeIdsTexture = new Texture2D(BiomeMapResolution, BiomeMapResolution,
            TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"BiomeIds_{suffix}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // Top-K weights: POINT filter. The step 6 shader samples weights at 4 corner texels
        // explicitly and combines them with manual bilinear corner weights — letting Unity's
        // bilinear sampler interpolate weights here would mix slot weights across texels
        // that have DIFFERENT ids in those slots (nonsense). Point gives raw texel weights;
        // the shader does the smoothing correctly.
        chunk.BiomeWeightsTexture = new Texture2D(BiomeMapResolution, BiomeMapResolution,
            TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"BiomeWeights_{suffix}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        chunk.SurfaceStateTexture = new Texture2D(BiomeMapResolution, BiomeMapResolution,
            TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = $"SurfaceState_{suffix}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        var blank = new Color32[TexelCount];
        chunk.BiomeBlendedColorTexture.SetPixels32(blank);
        chunk.BiomeBlendedColorTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        chunk.BiomeIdsTexture.SetPixels32(blank);
        chunk.BiomeIdsTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        chunk.BiomeWeightsTexture.SetPixels32(blank);
        chunk.BiomeWeightsTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        chunk.SurfaceStateTexture.SetPixels32(blank);
        chunk.SurfaceStateTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        LiveTextureSets++;
        LiveBiomeTextureSets++;
        LiveSurfaceStateTextures++;
        ReportCounters();
    }

    public static bool ReleaseBiomeTextures(PlanetChunk chunk)
    {
        if (chunk == null) return false;
        bool hadBiomeTextures = chunk.BiomeBlendedColorTexture != null
            || chunk.BiomeIdsTexture != null
            || chunk.BiomeWeightsTexture != null;
        DestroyTexture(ref chunk.BiomeBlendedColorTexture);
        DestroyTexture(ref chunk.BiomeIdsTexture);
        DestroyTexture(ref chunk.BiomeWeightsTexture);
        chunk.PendingBiomeBlendedColorPixels = null;
        chunk.PendingBiomeIdsPixels = null;
        chunk.PendingBiomeWeightsPixels = null;
        if (hadBiomeTextures && LiveBiomeTextureSets > 0)
        {
            LiveBiomeTextureSets--;
            ReportCounters();
        }
        return hadBiomeTextures;
    }

    public static void Dispose(PlanetChunk chunk)
    {
        if (chunk == null) return;
        bool hadTextures = chunk.BiomeBlendedColorTexture != null
            || chunk.BiomeIdsTexture != null
            || chunk.BiomeWeightsTexture != null
            || chunk.SurfaceStateTexture != null;
        ReleaseBiomeTextures(chunk);
        bool hadSurfaceState = chunk.SurfaceStateTexture != null;
        DestroyTexture(ref chunk.SurfaceStateTexture);
        if (hadSurfaceState && LiveSurfaceStateTextures > 0)
            LiveSurfaceStateTextures--;
        if (hadTextures && LiveTextureSets > 0)
            LiveTextureSets--;
        ReportCounters();
    }

    static void ReportCounters()
    {
        long bytesPerTexture = (long)TexelCount * BytesPerRgba32Texel;
        MemoryDebugCounters.ReportLiveChunkTextureSets(LiveTextureSets);
        MemoryDebugCounters.ReportChunkBiomeTextures(
            LiveBiomeTextureSets,
            LiveBiomeTextureSets * BiomeTexturesPerSet * bytesPerTexture);
        MemoryDebugCounters.ReportChunkSurfaceStateTextures(
            LiveSurfaceStateTextures,
            LiveSurfaceStateTextures * bytesPerTexture);
    }

    static void DestroyTexture(ref Texture2D tex)
    {
        if (tex == null) return;
        if (Application.isPlaying) Object.Destroy(tex);
        else Object.DestroyImmediate(tex);
        tex = null;
    }
}
