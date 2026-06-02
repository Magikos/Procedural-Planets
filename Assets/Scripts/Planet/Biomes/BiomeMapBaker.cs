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
    const int KernelRadius = 6;                          // window = (2*r + 1)^2 = 13x13 = 169 samples
    const int KernelSamples = (KernelRadius * 2 + 1) * (KernelRadius * 2 + 1);
    const int TexelCount = MapResolution * MapResolution;
    const int HighResCount = HighResolution * HighResolution;

    // Bake top-K biome maps for one chunk. All three output buffers must be Color32[TexelCount].
    // lutColors must be at least (max biome id + 1) entries. tempHighRes is a scratch byte buffer
    // of length HighResCount; caller can pool one per worker thread to eliminate GC pressure.
    public static void Bake(PlanetChunk chunk, in BiomeLookupData lookup, Color[] lutColors,
        Color32[] blendedColors, Color32[] ids, Color32[] weights, byte[] tempHighRes)
    {
        if (chunk == null || chunk.CpuBiomeData == null || chunk.CpuElevations == null) return;
        if (blendedColors == null || blendedColors.Length != TexelCount) return;
        if (ids == null || ids.Length != TexelCount) return;
        if (weights == null || weights.Length != TexelCount) return;
        if (tempHighRes == null || tempHighRes.Length != HighResCount) return;
        if (lutColors == null || lutColors.Length == 0) return;

        int vertCount = chunk.CpuBiomeData.Length;
        if (vertCount == 0 || chunk.CpuElevations.Length != vertCount) return;
        int vertRes = (int)Mathf.Sqrt(vertCount);
        if (vertRes * vertRes != vertCount) return;

        BuildHighResIdGrid(chunk, lookup, vertRes, tempHighRes);
        SampleTopKPerTexel(tempHighRes, lutColors, blendedColors, ids, weights);
    }

    // Pass 1: fill the HighResolution² id grid by sampling temperature/moisture/elevation at
    // each high-res cell center and running the lookup. ~16K samples per chunk.
    static void BuildHighResIdGrid(PlanetChunk chunk, in BiomeLookupData lookup, int vertRes, byte[] outIds)
    {
        for (int hy = 0; hy < HighResolution; hy++)
        {
            float v = HighResolution > 1 ? (float)hy / (HighResolution - 1) : 0.5f;
            for (int hx = 0; hx < HighResolution; hx++)
            {
                float u = HighResolution > 1 ? (float)hx / (HighResolution - 1) : 0.5f;
                Vector2 tm = BilinearSampleXY(chunk.CpuBiomeData, vertRes, u, v);
                float elev = BilinearSample(chunk.CpuElevations, vertRes, u, v);
                BiomeLookupEvaluator.Resolve(lookup, tm.x, tm.y, elev,
                    out byte primary, out _, out _);
                outIds[hy * HighResolution + hx] = primary;
            }
        }
    }

    // Pass 2: per output texel, scan a kernel of the high-res id grid, count biome occurrences,
    // pick top K, normalize weights, compute pre-blended color.
    static void SampleTopKPerTexel(byte[] hrIds, Color[] lutColors,
        Color32[] blendedColors, Color32[] ids, Color32[] weights)
    {
        // 256-slot accumulator buffer (biome ids are bytes). Stack-allocated would be ideal
        // but C# arrays GC-allocate; we keep this as a single shared buffer per Bake call.
        // Resetting via Array.Clear is essentially free for 256 ints.
        var counts = new int[256];

        for (int ty = 0; ty < MapResolution; ty++)
        {
            // Map output texel (ty) to high-res center: each output texel covers 2 hr cells.
            int hrCenterY = ty * (HighResolution / MapResolution) + (HighResolution / MapResolution) / 2;
            for (int tx = 0; tx < MapResolution; tx++)
            {
                int hrCenterX = tx * (HighResolution / MapResolution) + (HighResolution / MapResolution) / 2;

                System.Array.Clear(counts, 0, counts.Length);

                // Kernel scan: every texel gets exactly (2r+1)² samples. Out-of-range cells
                // are clamped to the nearest valid coord (texture-style edge replication)
                // rather than cropped. Cropping biased edge texels to whatever's inside the
                // chunk only — across chunk boundaries each side biased to its own interior,
                // producing visible seams. Edge replication keeps the sample count stable so
                // the bias is at least uniform on both sides of a shared boundary. (True
                // seamless blending requires the bake to sample outside the chunk's UV
                // bounds — direct noise eval or parent-chunk fallback — both deferred.)
                for (int dy = -KernelRadius; dy <= KernelRadius; dy++)
                {
                    int hy = hrCenterY + dy;
                    if (hy < 0) hy = 0;
                    else if (hy > HighResolution - 1) hy = HighResolution - 1;
                    int rowBase = hy * HighResolution;
                    for (int dx = -KernelRadius; dx <= KernelRadius; dx++)
                    {
                        int hx = hrCenterX + dx;
                        if (hx < 0) hx = 0;
                        else if (hx > HighResolution - 1) hx = HighResolution - 1;
                        counts[hrIds[rowBase + hx]]++;
                    }
                }

                // Pick top K by count. Linear sweep of 256 is cheap; max-K-heap would be
                // overkill at K=4.
                PickTopK(counts, out byte id0, out int c0, out byte id1, out int c1,
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

    static void PickTopK(int[] counts,
        out byte i0, out int c0,
        out byte i1, out int c1,
        out byte i2, out int c2,
        out byte i3, out int c3)
    {
        i0 = i1 = i2 = i3 = 0;
        c0 = c1 = c2 = c3 = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            int c = counts[i];
            if (c == 0) continue;
            if (c > c0) { i3 = i2; c3 = c2; i2 = i1; c2 = c1; i1 = i0; c1 = c0; i0 = (byte)i; c0 = c; }
            else if (c > c1) { i3 = i2; c3 = c2; i2 = i1; c2 = c1; i1 = (byte)i; c1 = c; }
            else if (c > c2) { i3 = i2; c3 = c2; i2 = (byte)i; c2 = c; }
            else if (c > c3) { i3 = (byte)i; c3 = c; }
        }
    }

    public static int HighResolutionSize => HighResolution;

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

    static Vector2 BilinearSampleXY(Vector4[] grid, int res, float u, float v)
    {
        float fx = Mathf.Clamp01(u) * (res - 1);
        float fy = Mathf.Clamp01(v) * (res - 1);
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int x1 = Mathf.Min(x0 + 1, res - 1);
        int y1 = Mathf.Min(y0 + 1, res - 1);
        float tx = fx - x0;
        float ty = fy - y0;
        Vector4 a = grid[x0 + y0 * res];
        Vector4 b = grid[x1 + y0 * res];
        Vector4 c = grid[x0 + y1 * res];
        Vector4 d = grid[x1 + y1 * res];
        float xResult = Mathf.Lerp(Mathf.Lerp(a.x, b.x, tx), Mathf.Lerp(c.x, d.x, tx), ty);
        float yResult = Mathf.Lerp(Mathf.Lerp(a.y, b.y, tx), Mathf.Lerp(c.y, d.y, tx), ty);
        return new Vector2(xResult, yResult);
    }
}
