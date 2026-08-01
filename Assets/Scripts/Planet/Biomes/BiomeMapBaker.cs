using UnityEngine;

// Phase B step 5b: per-chunk top-K biome bake. Replaces the earlier primary+secondary+blend
// approach with an AutoBiomes-style area-weighted scheme:
//
//   1. Build a higher-resolution biome ID grid for the chunk (HighResolution²) by
//      bilinear-sampling temperature / moisture / elevation from the chunk vertex grid and
//      running each sample through the Burst lookup snapshot.
//   2. For each output texel (MapResolution²), run a KernelRadius-wide window over the
//      high-res grid centered on the texel. Count occurrences of each biome id in the window.
//   3. Take the top-K biomes by count, normalize their weights to sum to 255, and write:
//        - Ids texture (RGBA8): id per slot, channel order = sorted by weight descending.
//        - Weights texture (RGBA8): byte weight per slot, summing to 255.
//        - Pre-blended color texture (RGBA8): Σ(weight/255 × LUT[id]) — the cheap shader path.
//
// This naturally smooths multi-biome junctions because adjacent output texels share most of
// their kernel samples, so weights vary continuously across the grid. The window-sample
// count is also a tunable smoothness knob (bigger kernel → wider blend zone).
public static class BiomeMapBaker
{
    public const int MapResolution = PlanetChunkTextures.BiomeMapResolution;
    public const int TopK = PlanetChunkTextures.TopK;

    // High-res grid is 2x the output. Each output texel covers a 2x2 block of high-res cells,
    // and a wider sample window gives biome boundaries enough distance to read as gradual
    // transitions instead of thin dark lines in DEBUG_BIOME_MAP_BLEND.
    const int HighResolution = MapResolution * 2;       // 128 at MapResolution=64
    const int KernelRadius = 12;                         // window = (2*r + 1)^2 = 25x25 = 625 samples (wider = softer biome borders)
    const int KernelSamples = (KernelRadius * 2 + 1) * (KernelRadius * 2 + 1);
    const int PaddedResolution = HighResolution + KernelRadius * 2;
    const int TexelCount = MapResolution * MapResolution;
    const int HighResCount = PaddedResolution * PaddedResolution;

    [System.ThreadStatic] static int[] _tlsTopKCounts;

    // Bake top-K biome maps for one chunk. All three output buffers must be Color32[TexelCount].
    // lutColors must be at least (max biome id + 1) entries. tempHighRes is a scratch byte buffer
    // of length HighResCount; caller can pool one per worker thread to eliminate GC pressure.
    internal static void Bake(
        PlanetChunk chunk,
        in BiomeLookupData lookup,
        IBiomeAssignmentField assignmentField,
        Color[] lutColors,
        Color32[] blendedColors, Color32[] ids, Color32[] weights, byte[] tempHighRes)
    {
        if (chunk == null || chunk.CpuElevations == null) return;
        if (chunk.CpuBiomeData == null && chunk.CpuBiomeData32 == null) return;
        if (blendedColors == null || blendedColors.Length != TexelCount) return;
        if (ids == null || ids.Length != TexelCount) return;
        if (weights == null || weights.Length != TexelCount) return;
        if (tempHighRes == null || tempHighRes.Length != HighResCount) return;
        if (lutColors == null || lutColors.Length == 0) return;

        int vertCount = chunk.CpuBiomeData != null
            ? chunk.CpuBiomeData.Length
            : chunk.CpuBiomeData32.Length;
        if (vertCount == 0 || chunk.CpuElevations.Length != vertCount) return;
        int vertRes = (int)Mathf.Sqrt(vertCount);
        if (vertRes * vertRes != vertCount) return;

        int activeBiomeCount = GetActiveBiomeCount(lookup, lutColors);
        BuildHighResIdGrid(chunk, lookup, assignmentField, vertRes, tempHighRes);
        SampleTopKPerTexel(tempHighRes, lutColors, activeBiomeCount, blendedColors, ids, weights);
    }

    static int GetActiveBiomeCount(in BiomeLookupData lookup, Color[] lutColors)
    {
        if (lookup.BiomeCount > 0 && lookup.BiomeCount <= lutColors.Length)
            return Mathf.Min(lookup.BiomeCount, 256);

        // Preserve previous behavior for inconsistent lookup/LUT snapshots: scan the full
        // byte range so invalid IDs still surface through SafeLut's magenta fallback.
        return 256;
    }

    // Pass 1: fill the padded high-resolution id grid. Padding samples beyond this chunk's
    // face-space bounds, giving both sides of a chunk boundary the same neighborhood.
    static void BuildHighResIdGrid(
        PlanetChunk chunk,
        in BiomeLookupData lookup,
        IBiomeAssignmentField assignmentField,
        int vertRes,
        byte[] outIds)
    {
        for (int py = 0; py < PaddedResolution; py++)
        {
            float localV = (float)(py - KernelRadius) / (HighResolution - 1);
            for (int px = 0; px < PaddedResolution; px++)
            {
                float localU = (float)(px - KernelRadius) / (HighResolution - 1);
                Vector2 tm = BilinearSampleXY(chunk, vertRes, localU, localV);
                float elevation = BilinearSample(
                    chunk.CpuElevations, vertRes, localU, localV);

                byte primary;
                if (assignmentField != null)
                {
                    Vector2 faceUv = chunk.UvCenter + new Vector2(
                        (localU - 0.5f) * chunk.UvHalfExtent * 2f,
                        (localV - 0.5f) * chunk.UvHalfExtent * 2f);
                    Vector3 direction = CoordinateConverter.CubeFaceToUnitSphere(
                        chunk.FaceIndex, faceUv);
                    byte landPrimary = assignmentField.EvaluatePrimaryId(direction);
                    BiomeLookupEvaluator.ResolveFromLandBiomes(
                        lookup,
                        tm.x,
                        elevation,
                        landPrimary,
                        landPrimary,
                        0f,
                        out primary,
                        out _,
                        out _);
                }
                else
                {
                    BiomeLookupEvaluator.Resolve(
                        lookup, tm.x, tm.y, elevation,
                        out primary, out _, out _);
                }

                outIds[py * PaddedResolution + px] = primary;
            }
        }
    }

    // Pass 2: per output texel, scan a kernel of the high-res id grid, count biome occurrences,
    // pick top K, normalize weights, compute pre-blended color.
    static void SampleTopKPerTexel(byte[] hrIds, Color[] lutColors, int activeBiomeCount,
        Color32[] blendedColors, Color32[] ids, Color32[] weights)
    {
        // Biome ids are bytes, but valid ids are normally contiguous [0, BiomeCount).
        // Keep the backing buffer at 256 for the fallback path while scanning only active ids.
        var counts = _tlsTopKCounts;
        if (counts == null || counts.Length != 256)
        {
            counts = new int[256];
            _tlsTopKCounts = counts;
        }

        for (int ty = 0; ty < MapResolution; ty++)
        {
            // Map output texel (ty) to high-res center: each output texel covers 2 hr cells.
            int hrCenterY = KernelRadius
                + ty * (HighResolution / MapResolution)
                + (HighResolution / MapResolution) / 2;
            for (int tx = 0; tx < MapResolution; tx++)
            {
                int hrCenterX = KernelRadius
                    + tx * (HighResolution / MapResolution)
                    + (HighResolution / MapResolution) / 2;

                System.Array.Clear(counts, 0, activeBiomeCount);

                // Every texel scans exactly (2r+1)^2 samples from the padded grid. The grid
                // already contains samples outside this chunk, so no edge clamping is needed.
                for (int dy = -KernelRadius; dy <= KernelRadius; dy++)
                {
                    int hy = hrCenterY + dy;
                    int rowBase = hy * PaddedResolution;
                    for (int dx = -KernelRadius; dx <= KernelRadius; dx++)
                    {
                        int hx = hrCenterX + dx;
                        byte id = hrIds[rowBase + hx];
                        if (id < activeBiomeCount)
                            counts[id]++;
                    }
                }

                // Pick top K by count. Linear sweep over the active biome slots is cheaper
                // than scanning the whole byte range for every texel.
                PickTopK(counts, activeBiomeCount, out byte id0, out int c0, out byte id1, out int c1,
                    out byte id2, out int c2, out byte id3, out int c3);

                // Normalize weights to sum = 255. Assign rounding remainder only to a slot
                // that actually participated so empty top-K slots never contribute color.
                int total = c0 + c1 + c2 + c3;
                if (total <= 0) total = 1; // degenerate: all kernel samples were biome 0
                int w0 = (c0 * 255) / total;
                int w1 = (c1 * 255) / total;
                int w2 = (c2 * 255) / total;
                int w3 = (c3 * 255) / total;
                int remainder = 255 - w0 - w1 - w2 - w3;
                if (remainder > 0)
                {
                    if (c0 > 0) w0 += remainder;
                    else if (c1 > 0) w1 += remainder;
                    else if (c2 > 0) w2 += remainder;
                    else if (c3 > 0) w3 += remainder;
                }

                int idx = ty * MapResolution + tx;
                ids[idx] = new Color32(id0, id1, id2, id3);
                weights[idx] = new Color32((byte)w0, (byte)w1, (byte)w2, (byte)w3);

                // Pre-blended color: Σ(weight/255 × LUT[id]). The LUT is sRGB Color (gamma-
                // space), and we want the result to match what a per-pixel shader doing the
                // same lerp in gamma space would produce. Using `Color` (float) keeps precision.
                Color blended = SafeLut(lutColors, id0) * (w0 / 255f)
                              + SafeLut(lutColors, id1) * (w1 / 255f)
                              + SafeLut(lutColors, id2) * (w2 / 255f)
                              + SafeLut(lutColors, id3) * (w3 / 255f);
                blended.a = 1f;
                blendedColors[idx] = blended;
            }
        }
    }

    static Color SafeLut(Color[] lut, byte id) =>
        id < lut.Length ? lut[id] : Color.magenta;

    static void PickTopK(int[] counts, int activeBiomeCount,
        out byte i0, out int c0,
        out byte i1, out int c1,
        out byte i2, out int c2,
        out byte i3, out int c3)
    {
        i0 = i1 = i2 = i3 = 0;
        c0 = c1 = c2 = c3 = 0;
        for (int i = 0; i < activeBiomeCount; i++)
        {
            int c = counts[i];
            if (c == 0) continue;
            if (c > c0) { i3 = i2; c3 = c2; i2 = i1; c2 = c1; i1 = i0; c1 = c0; i0 = (byte)i; c0 = c; }
            else if (c > c1) { i3 = i2; c3 = c2; i2 = i1; c2 = c1; i1 = (byte)i; c1 = c; }
            else if (c > c2) { i3 = i2; c3 = c2; i2 = (byte)i; c2 = c; }
            else if (c > c3) { i3 = (byte)i; c3 = c; }
        }
    }

    public static int HighResolutionSize => PaddedResolution;

    static float BilinearSample(float[] grid, int res, float u, float v)
    {
        float fx = Mathf.Clamp01(u) * (res - 1);
        float fy = Mathf.Clamp01(v) * (res - 1);
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int x1 = Mathf.Min(x0 + 1, res - 1);
        int y1 = Mathf.Min(y0 + 1, res - 1);
        float tx = fx - x0;
        float ty = fy - y0;
        float a = grid[x0 + y0 * res];
        float b = grid[x1 + y0 * res];
        float c = grid[x0 + y1 * res];
        float d = grid[x1 + y1 * res];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    static Vector2 BilinearSampleXY(PlanetChunk chunk, int res, float u, float v)
    {
        float fx = Mathf.Clamp01(u) * (res - 1);
        float fy = Mathf.Clamp01(v) * (res - 1);
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int x1 = Mathf.Min(x0 + 1, res - 1);
        int y1 = Mathf.Min(y0 + 1, res - 1);
        float tx = fx - x0;
        float ty = fy - y0;

        int i00 = x0 + y0 * res;
        int i10 = x1 + y0 * res;
        int i01 = x0 + y1 * res;
        int i11 = x1 + y1 * res;
        Vector2 a = GetTemperatureMoisture(chunk, i00);
        Vector2 b = GetTemperatureMoisture(chunk, i10);
        Vector2 c = GetTemperatureMoisture(chunk, i01);
        Vector2 d = GetTemperatureMoisture(chunk, i11);
        float xResult = Mathf.Lerp(Mathf.Lerp(a.x, b.x, tx), Mathf.Lerp(c.x, d.x, tx), ty);
        float yResult = Mathf.Lerp(Mathf.Lerp(a.y, b.y, tx), Mathf.Lerp(c.y, d.y, tx), ty);
        return new Vector2(xResult, yResult);
    }

    static Vector2 GetTemperatureMoisture(PlanetChunk chunk, int index)
    {
        if (chunk.CpuBiomeData != null)
        {
            Vector4 data = chunk.CpuBiomeData[index];
            return new Vector2(data.x, data.y);
        }

        Color32 packed = chunk.CpuBiomeData32[index];
        const float byteToFloat = 1f / 255f;
        return new Vector2(packed.r * byteToFloat, packed.g * byteToFloat);
    }
}
