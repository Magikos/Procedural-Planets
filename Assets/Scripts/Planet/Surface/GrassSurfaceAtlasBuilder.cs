using System.Collections.Generic;
using UnityEngine;

// Builds the per-face grass surface atlases (radius + normal) consumed by the grass placement
// compute. Pure transform over max-depth leaf CPU data into GPU textures; owns no state. Split
// out of ChunkedSurfaceProvider (perf-maintainability plan slice 4).
public static class GrassSurfaceAtlasBuilder
{
    public static GrassSurfaceAtlasGpuData Build(IReadOnlyList<PlanetChunk> allChunks, int maxChunkDepth)
    {
        int leafsPerAxis = 1 << Mathf.Max(maxChunkDepth, 0);
        int leafStride = PlanetChunkTextures.BiomeMapResolution - 1;
        int atlasResolution = leafsPerAxis * leafStride + 1;
        if (atlasResolution <= 1 || atlasResolution > SystemInfo.maxTextureSize)
        {
            LoggerProvider.Log(LogLevel.Warning, "PhaseC",
                $"Grass surface atlas skipped: requested {atlasResolution}x{atlasResolution}, max texture size is {SystemInfo.maxTextureSize}.");
            return null;
        }

        var radiusPixelsByFace = new float[6][];
        var radiusTextures = new Texture2D[6];
        var normalTextures = new Texture2D[6];
        int expectedLeafCount = leafsPerAxis * leafsPerAxis;

        for (int face = 0; face < 6; face++)
        {
            var radiusPixels = new float[atlasResolution * atlasResolution];
            int copiedLeaves = 0;

            for (int i = 0; i < allChunks.Count; i++)
            {
                PlanetChunk chunk = allChunks[i];
                if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face) continue;
                if (CopyLeafSurfaceRadiusIntoAtlas(chunk, radiusPixels, atlasResolution, leafsPerAxis, leafStride))
                    copiedLeaves++;
            }

            if (copiedLeaves <= 0)
            {
                LoggerProvider.Log(LogLevel.Warning, "PhaseC", $"Grass surface atlas face {face}: no leaf surface data available.");
                continue;
            }

            radiusPixelsByFace[face] = radiusPixels;
            LoggerProvider.Log(LogLevel.Debug, "PhaseC",
                $"Grass surface radius face {face}: {atlasResolution}x{atlasResolution}, copied {copiedLeaves}/{expectedLeafCount} max-depth leaves.");
        }

        for (int face = 0; face < 6; face++)
        {
            float[] radiusPixels = radiusPixelsByFace[face];
            if (radiusPixels == null) continue;

            var normalPixels = BuildGrassSurfaceNormalPixels(face, radiusPixelsByFace, atlasResolution);
            radiusTextures[face] = CreateGrassRadiusTexture($"GrassSurfaceRadius_F{face}", atlasResolution, radiusPixels);
            normalTextures[face] = CreateGrassNormalTexture($"GrassSurfaceNormal_F{face}", atlasResolution, normalPixels);
        }

        return new GrassSurfaceAtlasGpuData(radiusTextures, normalTextures, atlasResolution);
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

    static Color32[] BuildGrassSurfaceNormalPixels(int face, float[][] radiusPixelsByFace, int atlasResolution)
    {
        var normalPixels = new Color32[atlasResolution * atlasResolution];
        float invMax = 1f / (atlasResolution - 1);

        for (int y = 0; y < atlasResolution; y++)
        {
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
}
