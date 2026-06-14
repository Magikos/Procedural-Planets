using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Builds the per-face grass surface atlases (radius + normal) consumed by the grass placement
// compute. Pure transform over max-depth leaf CPU data into GPU textures; owns no state. Split
// out of ChunkedSurfaceProvider (perf-maintainability plan slice 4).
public static class GrassSurfaceAtlasBuilder
{
    public static async Awaitable<GrassSurfaceAtlasGpuData> BuildAsync(
        IReadOnlyList<PlanetChunk> allChunks,
        int maxChunkDepth,
        IProgressHandle progress,
        CancellationToken ct)
    {
        int leafsPerAxis = 1 << Mathf.Max(maxChunkDepth, 0);
        int leafStride = PlanetChunkTextures.BiomeMapResolution - 1;
        int atlasResolution = leafsPerAxis * leafStride + 1;
        if (atlasResolution <= 1 || atlasResolution > SystemInfo.maxTextureSize)
        {
            throw new System.InvalidOperationException(
                $"Grass surface atlas requires {atlasResolution}x{atlasResolution}, " +
                $"but the maximum texture size is {SystemInfo.maxTextureSize}.");
        }

        progress?.Report(0f, "Calculating grass surface atlases...");
        float computeProgress = 0f;
        Awaitable<GrassAtlasPixels> computeTask = ComputePixelsAsync(
            allChunks,
            atlasResolution,
            leafsPerAxis,
            leafStride,
            ct,
            value => Volatile.Write(ref computeProgress, value));
        var computeAwaiter = computeTask.GetAwaiter();
        float reportedComputeProgress = 0f;
        while (!computeAwaiter.IsCompleted)
        {
            reportedComputeProgress = Mathf.Max(
                reportedComputeProgress,
                Volatile.Read(ref computeProgress));
            progress?.Report(
                reportedComputeProgress * 0.8f,
                "Calculating grass surface atlases...");
            await Awaitable.NextFrameAsync();
        }
        GrassAtlasPixels pixels = computeAwaiter.GetResult();

        var radiusTextures = new Texture2D[6];
        var normalTextures = new Texture2D[6];
        int expectedLeafCount = leafsPerAxis * leafsPerAxis;

        try
        {
            for (int face = 0; face < 6; face++)
            {
                float[] radiusPixels = pixels.RadiusByFace[face];
                Color32[] normalPixels = pixels.NormalByFace[face];
                if (radiusPixels == null || normalPixels == null)
                {
                    LoggerProvider.Log(LogLevel.Warning, "PhaseC", $"Grass surface atlas face {face}: no leaf surface data available.");
                    continue;
                }

                radiusTextures[face] = CreateGrassRadiusTexture(
                    $"GrassSurfaceRadius_F{face}", atlasResolution, radiusPixels);
                normalTextures[face] = CreateGrassNormalTexture(
                    $"GrassSurfaceNormal_F{face}", atlasResolution, normalPixels);
                LoggerProvider.Log(LogLevel.Debug, "PhaseC",
                    $"Grass surface radius face {face}: {atlasResolution}x{atlasResolution}, copied {pixels.CopiedLeavesByFace[face]}/{expectedLeafCount} max-depth leaves.");
                pixels.RadiusByFace[face] = null;
                pixels.NormalByFace[face] = null;
                progress?.Report(
                    0.8f + 0.2f * ((face + 1f) / 6f),
                    $"Uploading grass surface atlas {face + 1}/6...");
                await Awaitable.NextFrameAsync(ct);
            }

            for (int face = 0; face < 6; face++)
            {
                if (radiusTextures[face] == null || normalTextures[face] == null)
                {
                    throw new System.InvalidOperationException(
                        $"Grass surface atlas face {face} was not built.");
                }
            }
        }
        catch
        {
            DestroyTextureArray(radiusTextures);
            DestroyTextureArray(normalTextures);
            throw;
        }

        progress?.Report(1f, "Grass surface atlases ready.");
        return new GrassSurfaceAtlasGpuData(radiusTextures, normalTextures, atlasResolution);
    }

    static async Awaitable<GrassAtlasPixels> ComputePixelsAsync(
        IReadOnlyList<PlanetChunk> allChunks,
        int atlasResolution,
        int leafsPerAxis,
        int leafStride,
        CancellationToken ct,
        System.Action<float> onProgress)
    {
        await Awaitable.BackgroundThreadAsync();
        var pixels = new GrassAtlasPixels();
        int expectedLeafCount = leafsPerAxis * leafsPerAxis;
        int totalLeaves = Mathf.Max(expectedLeafCount * 6, 1);
        int completedLeaves = 0;
        var options = new ParallelOptions { CancellationToken = ct };

        Parallel.For(0, 6, options, face =>
        {
            var radiusPixels = new float[atlasResolution * atlasResolution];
            int copiedLeaves = 0;

            for (int i = 0; i < allChunks.Count; i++)
            {
                PlanetChunk chunk = allChunks[i];
                if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face) continue;
                if (!CopyLeafSurfaceRadiusIntoAtlas(
                    chunk,
                    radiusPixels,
                    atlasResolution,
                    leafsPerAxis,
                    leafStride))
                {
                    continue;
                }

                copiedLeaves++;
                int leaves = Interlocked.Increment(ref completedLeaves);
                if ((leaves & 7) == 0 || leaves == totalLeaves)
                    onProgress?.Invoke(0.45f * Mathf.Clamp01((float)leaves / totalLeaves));
            }

            if (copiedLeaves > 0)
            {
                pixels.RadiusByFace[face] = radiusPixels;
                pixels.CopiedLeavesByFace[face] = copiedLeaves;
            }
        });

        int completedNormalRows = 0;
        int totalNormalRows = Mathf.Max(atlasResolution * 6, 1);
        Parallel.For(0, 6, options, face =>
        {
            if (pixels.RadiusByFace[face] == null)
                return;

            pixels.NormalByFace[face] = BuildGrassSurfaceNormalPixels(
                face,
                pixels.RadiusByFace,
                atlasResolution,
                ct,
                () =>
                {
                    int rows = Interlocked.Increment(ref completedNormalRows);
                    if ((rows & 7) == 0 || rows == totalNormalRows)
                    {
                        onProgress?.Invoke(
                            0.45f + 0.55f * Mathf.Clamp01((float)rows / totalNormalRows));
                    }
                });
        });

        ct.ThrowIfCancellationRequested();
        await Awaitable.MainThreadAsync();
        return pixels;
    }

    sealed class GrassAtlasPixels
    {
        public readonly float[][] RadiusByFace = new float[6][];
        public readonly Color32[][] NormalByFace = new Color32[6][];
        public readonly int[] CopiedLeavesByFace = new int[6];
    }

    static bool CopyLeafSurfaceRadiusIntoAtlas(PlanetChunk chunk, float[] atlas, int atlasResolution, int leafsPerAxis, int leafStride)
    {
        int mapResolution = PlanetChunkTextures.BiomeMapResolution;
        if (chunk == null || atlas == null || chunk.CpuVertexRadii == null) return false;
        int sourceResolution = (int)Mathf.Sqrt(chunk.CpuVertexRadii.Length);
        if (sourceResolution * sourceResolution != chunk.CpuVertexRadii.Length || sourceResolution <= 1) return false;

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        int leafX = Mathf.Clamp(Mathf.RoundToInt(minU * leafsPerAxis), 0, leafsPerAxis - 1);
        int leafY = Mathf.Clamp(Mathf.RoundToInt(minV * leafsPerAxis), 0, leafsPerAxis - 1);
        int dstX0 = leafX * leafStride;
        int dstY0 = leafY * leafStride;
        float invMapMax = 1f / (mapResolution - 1);

        for (int y = 0; y < mapResolution; y++)
        {
            float v = y * invMapMax;
            int dstRow = (dstY0 + y) * atlasResolution + dstX0;
            for (int x = 0; x < mapResolution; x++)
            {
                float u = x * invMapMax;
                atlas[dstRow + x] = SampleFloatGrid(chunk.CpuVertexRadii, sourceResolution, u, v);
            }
        }

        return true;
    }

    static Color32[] BuildGrassSurfaceNormalPixels(
        int face,
        float[][] radiusPixelsByFace,
        int atlasResolution,
        CancellationToken ct,
        System.Action onRowCompleted)
    {
        var normalPixels = new Color32[atlasResolution * atlasResolution];
        float invMax = 1f / (atlasResolution - 1);

        for (int y = 0; y < atlasResolution; y++)
        {
            if ((y & 31) == 0)
                ct.ThrowIfCancellationRequested();
            float v = y * invMax;
            int row = y * atlasResolution;
            for (int x = 0; x < atlasResolution; x++)
            {
                float u = x * invMax;
                Vector2 uv = new(u, v);
                Vector3 pWest = SampleGrassSurfacePoint(radiusPixelsByFace, face, new Vector2(u - invMax, v), atlasResolution);
                Vector3 pEast = SampleGrassSurfacePoint(radiusPixelsByFace, face, new Vector2(u + invMax, v), atlasResolution);
                Vector3 pNorth = SampleGrassSurfacePoint(radiusPixelsByFace, face, new Vector2(u, v - invMax), atlasResolution);
                Vector3 pSouth = SampleGrassSurfacePoint(radiusPixelsByFace, face, new Vector2(u, v + invMax), atlasResolution);

                Vector3 du = pEast - pWest;
                Vector3 dv = pSouth - pNorth;
                Vector3 normal = Vector3.Cross(du, dv);
                Vector3 sphereNormal = CoordinateConverter.CubeFaceToUnitSphere(face, uv);
                if (normal.sqrMagnitude < 1e-10f)
                    normal = sphereNormal;
                else
                    normal.Normalize();
                if (Vector3.Dot(normal, sphereNormal) < 0f)
                    normal = -normal;

                normalPixels[row + x] = PackNormalToColor32(normal);
            }

            onRowCompleted?.Invoke();
        }

        return normalPixels;
    }

    static Vector3 SampleGrassSurfacePoint(float[][] radiusPixelsByFace, int face, Vector2 uv, int atlasResolution)
    {
        RemapFaceUvForAtlasSample(face, uv, out int sampleFace, out Vector2 sampleUv);
        float radius = SampleGrassRadiusAtlas(radiusPixelsByFace, sampleFace, sampleUv, atlasResolution);
        return CoordinateConverter.CubeFaceToUnitSphere(sampleFace, sampleUv) * radius;
    }

    static float SampleGrassRadiusAtlas(float[][] radiusPixelsByFace, int face, Vector2 uv, int atlasResolution)
    {
        if (radiusPixelsByFace == null || face < 0 || face >= radiusPixelsByFace.Length)
            return 0f;
        float[] pixels = radiusPixelsByFace[face];
        if (pixels == null || pixels.Length != atlasResolution * atlasResolution)
            return 0f;

        float gx = Mathf.Clamp01(uv.x) * (atlasResolution - 1);
        float gy = Mathf.Clamp01(uv.y) * (atlasResolution - 1);
        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, atlasResolution - 1);
        int y1 = Mathf.Min(y0 + 1, atlasResolution - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float r00 = pixels[x0 + y0 * atlasResolution];
        float r10 = pixels[x1 + y0 * atlasResolution];
        float r01 = pixels[x0 + y1 * atlasResolution];
        float r11 = pixels[x1 + y1 * atlasResolution];
        return Mathf.Lerp(Mathf.Lerp(r00, r10, fx), Mathf.Lerp(r01, r11, fx), fy);
    }

    static void RemapFaceUvForAtlasSample(int face, Vector2 uv, out int sampleFace, out Vector2 sampleUv)
    {
        sampleFace = face;
        sampleUv = uv;

        if (uv.x < 0f) { RemapOutsideFaceUv(face, CubeEdge.West, uv.y, -uv.x, out sampleFace, out sampleUv); return; }
        if (uv.x > 1f) { RemapOutsideFaceUv(face, CubeEdge.East, uv.y, uv.x - 1f, out sampleFace, out sampleUv); return; }
        if (uv.y < 0f) { RemapOutsideFaceUv(face, CubeEdge.North, uv.x, -uv.y, out sampleFace, out sampleUv); return; }
        if (uv.y > 1f) { RemapOutsideFaceUv(face, CubeEdge.South, uv.x, uv.y - 1f, out sampleFace, out sampleUv); return; }
    }

    static void RemapOutsideFaceUv(int face, CubeEdge edge, float edgeParam, float neighborDepth,
        out int sampleFace, out Vector2 sampleUv)
    {
        CubeFaceEdgeNeighbor neighbor = CubeFaceTopology.GetNeighbor(face, edge);
        sampleFace = neighbor.NeighborFace;

        float s = neighbor.EdgeParamReversed ? 1f - edgeParam : edgeParam;
        float d = Mathf.Clamp01(neighborDepth);
        sampleUv = neighbor.NeighborEdge switch
        {
            CubeEdge.East => new Vector2(1f - d, s),
            CubeEdge.West => new Vector2(d, s),
            CubeEdge.North => new Vector2(s, d),
            CubeEdge.South => new Vector2(s, 1f - d),
            _ => new Vector2(Mathf.Clamp01(s), Mathf.Clamp01(d)),
        };
    }

    static float SampleFloatGrid(float[] values, int resolution, float u, float v)
    {
        float gx = Mathf.Clamp01(u) * (resolution - 1);
        float gy = Mathf.Clamp01(v) * (resolution - 1);
        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int y1 = Mathf.Min(y0 + 1, resolution - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float v00 = values[x0 + y0 * resolution];
        float v10 = values[x1 + y0 * resolution];
        float v01 = values[x0 + y1 * resolution];
        float v11 = values[x1 + y1 * resolution];
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    static Color32 PackNormalToColor32(Vector3 normal)
    {
        normal.Normalize();
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f), 0, 255),
            255);
    }

    static Texture2D CreateGrassRadiusTexture(string name, int resolution, float[] pixels)
    {
        var tex = new Texture2D(resolution, resolution, TextureFormat.RFloat, mipChain: false, linear: true)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixelData(pixels, 0);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    static Texture2D CreateGrassNormalTexture(string name, int resolution, Color32[] pixels)
    {
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    static void DestroyTextureArray(Texture2D[] textures)
    {
        if (textures == null) return;
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            if (Application.isPlaying) Object.Destroy(textures[i]);
            else Object.DestroyImmediate(textures[i]);
            textures[i] = null;
        }
    }
}
