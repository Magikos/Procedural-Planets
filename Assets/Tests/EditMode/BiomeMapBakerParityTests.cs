using System;
using System.Reflection;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
public sealed class BiomeMapBakerParityTests
{
    const int MapResolution = 64, HighResolution = 127, KernelRadius = 12, PaddedResolution = 151;
    [ThreadStatic] static int[] _tlsTopKCounts;
    delegate void Sample(byte[] grid, Color[] lut, int active, Color32[] colors, Color32[] ids, Color32[] weights);
    static readonly Sample Actual = (Sample)typeof(BiomeMapBaker).GetMethod("SampleTopKPerTexel", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Sample));

    [TestCase(1, 0)]
    [TestCase(4, 1)]
    [TestCase(17, 2)]
    [TestCase(256, 3)]
    [TestCase(5, 4)]
    public void SlidingWindowMatchesExhaustiveBytes(int active, int pattern)
    {
        Assert.That(BiomeMapBaker.MapResolution, Is.EqualTo(MapResolution));
        Assert.That(BiomeMapBaker.HighResolutionSize, Is.EqualTo(PaddedResolution));
        var random = new System.Random(718);
        var grid = new byte[PaddedResolution * PaddedResolution];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = pattern switch { 0 => (byte)0, 1 => (byte)(i % 4), 2 => (byte)((i / PaddedResolution + i % PaddedResolution) % 17), _ => (byte)random.Next(256) };
        var lut = new Color[pattern == 4 ? 2 : 256];
        for (int i = 0; i < lut.Length; i++) lut[i] = new Color(i / 255f, (255-i) / 255f, (i % 19) / 18f, 1f);
        var expected = new Color32[4096]; var ids = new Color32[4096]; var weights = new Color32[4096];
        var actual = new Color32[4096]; var actualIds = new Color32[4096]; var actualWeights = new Color32[4096];
        Reference(grid, lut, active, expected, ids, weights);
        Actual(grid, lut, active, actual, actualIds, actualWeights);
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEqual(ids, actualIds);
        CollectionAssert.AreEqual(weights, actualWeights);
        // Reusing worker storage with a smaller active range must not leak the previous bake.
        Reference(grid, lut, 1, expected, ids, weights);
        Actual(grid, lut, 1, actual, actualIds, actualWeights);
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEqual(ids, actualIds);
        CollectionAssert.AreEqual(weights, actualWeights);
    }

    [TestCase(18)]
    [TestCase(256)]
    public void WeightedSmoothingMatchesExhaustiveBytes(int active)
    {
        var random = new System.Random(8912);
        var primary = new byte[PaddedResolution * PaddedResolution];
        var secondary = new byte[primary.Length];
        var blend = new byte[primary.Length];
        random.NextBytes(primary); random.NextBytes(secondary); random.NextBytes(blend);
        var lut = new Color[256];
        for (int i = 0; i < lut.Length; i++) lut[i] = new Color(i / 255f, 1f - i / 255f, .5f, 1f);
        var expected = new Color32[4096]; var ids = new Color32[4096]; var weights = new Color32[4096];
        var actual = new Color32[4096]; var actualIds = new Color32[4096]; var actualWeights = new Color32[4096];
        Reference(primary, lut, active, expected, ids, weights, secondary, blend);
        typeof(BiomeMapBaker).GetMethod("SampleWeightedTopKPerTexel", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { primary, secondary, blend, lut, active, actual, actualIds, actualWeights });
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEqual(ids, actualIds);
        CollectionAssert.AreEqual(weights, actualWeights);
    }

    [Test]
    public void ParallelWorkersKeepIndependentHistograms()
    {
        System.Threading.Tasks.Parallel.For(0, 8, i => SlidingWindowMatchesExhaustiveBytes(256, 3));
    }

    [Test]
    public void ReportMatchedSmoothingTimings()
    {
        var random = new System.Random(12345);
        var grid = new byte[PaddedResolution * PaddedResolution]; random.NextBytes(grid);
        var lut = new Color[256];
        var colors = new Color32[4096]; var ids = new Color32[4096]; var weights = new Color32[4096];
        Reference(grid, lut, 64, colors, ids, weights); Actual(grid, lut, 64, colors, ids, weights);
        for (int run = 0; run < 3; run++)
        {
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 8; i++) Reference(grid, lut, 64, colors, ids, weights);
            double baseline = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            for (int i = 0; i < 8; i++) Actual(grid, lut, 64, colors, ids, weights);
            TestContext.WriteLine($"Smoothing run {run}: exhaustive={baseline:F3}ms optimized={timer.Elapsed.TotalMilliseconds:F3}ms (8 chunks)");
        }
    }
    static void Reference(byte[] hrIds, Color[] lutColors, int activeBiomeCount,
        Color32[] blendedColors, Color32[] ids, Color32[] weights, byte[] secondary = null, byte[] blend = null)
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
            int hrCenterY = KernelRadius + ty * 2;
            for (int tx = 0; tx < MapResolution; tx++)
            {
                int hrCenterX = KernelRadius + tx * 2;

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
                        int index = rowBase + hx;
                        int w = blend == null ? 0 : blend[index];
                        if (id < activeBiomeCount) counts[id] += 255 - w;
                        if (w > 0 && secondary[index] < activeBiomeCount) counts[secondary[index]] += w;
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


}
}

