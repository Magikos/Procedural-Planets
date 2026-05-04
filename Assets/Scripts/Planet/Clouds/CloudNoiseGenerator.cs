using UnityEngine;

/// <summary>
/// Generates 3D Worley noise textures for cloud rendering via compute shader.
/// Shape texture (128³ RGBA): 4 frequency layers for base cloud form.
/// Detail texture (32³ RGBA): 4 frequency layers for edge erosion.
/// Generated once at startup from a deterministic seed.
/// </summary>
public static class CloudNoiseGenerator
{
    // Worley cell counts per channel (increasing frequency)
    static readonly int[] ShapeCells = { 2, 4, 8, 16 };
    static readonly int[] DetailCells = { 4, 8, 16, 32 };

    public static RenderTexture GenerateShapeNoise(ComputeShader compute, int resolution, int seed)
    {
        var texture = Create3DTexture(resolution);
        GenerateWorleyChannels(compute, texture, resolution, ShapeCells, seed);
        return texture;
    }

    public static RenderTexture GenerateDetailNoise(ComputeShader compute, int resolution, int seed)
    {
        var texture = Create3DTexture(resolution);
        GenerateWorleyChannels(compute, texture, resolution, DetailCells, seed + 1000);
        return texture;
    }

    static RenderTexture Create3DTexture(int resolution)
    {
        var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGBHalf, 0)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = resolution,
            enableRandomWrite = true,
            msaaSamples = 1,
            useMipMap = false
        };
        var tex = new RenderTexture(desc)
        {
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Repeat,
            name = $"CloudNoise3D_{resolution}"
        };
        tex.Create();
        return tex;
    }

    static void GenerateWorleyChannels(ComputeShader compute, RenderTexture texture, int resolution, int[] cellCounts, int baseSeed)
    {
        int kernel = compute.FindKernel("CSWorley");

        for (int channel = 0; channel < 4; channel++)
        {
            int numCells = cellCounts[channel];
            int totalPoints = numCells * numCells * numCells;
            var points = GeneratePoints(totalPoints, numCells, baseSeed + channel * 100);

            var pointBuffer = new ComputeBuffer(totalPoints, sizeof(float) * 3);
            pointBuffer.SetData(points);

            compute.SetTexture(kernel, "_Result", texture);
            compute.SetBuffer(kernel, "_Points", pointBuffer);
            compute.SetInt("_Resolution", resolution);
            compute.SetInt("_NumCells", numCells);
            compute.SetInt("_Channel", channel);
            compute.SetInt("_Tile", 1);

            int groups = Mathf.CeilToInt(resolution / 8f);
            compute.Dispatch(kernel, groups, groups, groups);

            pointBuffer.Release();
        }
    }

    static Vector3[] GeneratePoints(int totalPoints, int numCells, int seed)
    {
        var rand = new System.Random(seed);
        var points = new Vector3[totalPoints];

        for (int i = 0; i < totalPoints; i++)
        {
            int z = i / (numCells * numCells);
            int y = (i / numCells) % numCells;
            int x = i % numCells;

            // Random point within this cell, normalized to [0,1] space
            float px = (x + (float)rand.NextDouble()) / numCells;
            float py = (y + (float)rand.NextDouble()) / numCells;
            float pz = (z + (float)rand.NextDouble()) / numCells;

            points[i] = new Vector3(px, py, pz);
        }

        return points;
    }
}
