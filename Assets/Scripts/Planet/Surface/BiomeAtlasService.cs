using System.Collections.Generic;
using UnityEngine;

// Owns the per-face biome atlas textures and the biome-map bake/stitch pipeline. Split out of
// ChunkedSurfaceProvider (restructure design 2026-06-12). The orchestrator delegates bake/atlas
// work here; the render side reads the finished atlases via TryGetFaceAtlases; runtime rebakes
// are mediated by the orchestrator (bake here, then it rebinds the render handle) so this service
// never depends back on the mesh cache.
public interface IBiomeAtlasService
{
    void BuildFaceAtlases(IReadOnlyList<PlanetChunk> chunks);
    bool TryGetFaceAtlases(int face, out Texture2D blended, out Texture2D ids, out Texture2D weights);
    bool HasCompleteAtlases();
    bool UpdateFaceAtlasRegion(PlanetChunk chunk);
    void ReleasePerChunkBiomeTextures(IReadOnlyList<PlanetChunk> chunks);
    void Dispose();
}

public sealed class BiomeAtlasService : IBiomeAtlasService
{
    readonly int _maxChunkDepth;

    Texture2D[] _faceBlendedAtlases;
    Texture2D[] _faceIdAtlases;
    Texture2D[] _faceWeightAtlases;
    Texture2D _blendedStaging;
    Texture2D _idStaging;
    Texture2D _weightStaging;
    int _reportedTextureCount;
    long _reportedRawBytes;

    [System.ThreadStatic] static byte[] _tlsBakeHighResBuffer;

    public BiomeAtlasService(int maxChunkDepth)
    {
        _maxChunkDepth = maxChunkDepth;
    }

    // Step 5b: bake top-K biome textures for one chunk on a worker thread. Allocates the
    // pending Color32 buffers lazily (per chunk, GC'd after upload) and reuses a thread-local
    // scratch buffer for the high-res biome id grid (no per-chunk GC pressure for that).
    internal static void BakeChunkMap(
        PlanetChunk chunk,
        in BiomeLookupData lookup,
        VoronoiBiomeField voronoiField,
        Color[] lutColors)
    {
        if (chunk == null) return;
        int texelCount = PlanetChunkTextures.BiomeMapResolution * PlanetChunkTextures.BiomeMapResolution;
        int hrCount = BiomeMapBaker.HighResolutionSize * BiomeMapBaker.HighResolutionSize;

        if (chunk.PendingBiomeBlendedColorPixels == null || chunk.PendingBiomeBlendedColorPixels.Length != texelCount)
            chunk.PendingBiomeBlendedColorPixels = new Color32[texelCount];
        if (chunk.PendingBiomeIdsPixels == null || chunk.PendingBiomeIdsPixels.Length != texelCount)
            chunk.PendingBiomeIdsPixels = new Color32[texelCount];
        if (chunk.PendingBiomeWeightsPixels == null || chunk.PendingBiomeWeightsPixels.Length != texelCount)
            chunk.PendingBiomeWeightsPixels = new Color32[texelCount];
        if (_tlsBakeHighResBuffer == null || _tlsBakeHighResBuffer.Length != hrCount)
            _tlsBakeHighResBuffer = new byte[hrCount];

        BiomeMapBaker.Bake(chunk, lookup, voronoiField, lutColors,
            chunk.PendingBiomeBlendedColorPixels,
            chunk.PendingBiomeIdsPixels,
            chunk.PendingBiomeWeightsPixels,
            _tlsBakeHighResBuffer);
    }

    // Step 5b: upload the 3 baked Color32 buffers to their GPU textures. Leaf pending arrays
    // stay alive until the face-space biome atlases are stitched, then ReleasePendingPixels
    // drops them all at once.
    public static void UploadChunkMap(PlanetChunk chunk, bool releasePendingPixels)
    {
        if (chunk == null) return;
        if (chunk.BiomeBlendedColorTexture != null && chunk.PendingBiomeBlendedColorPixels != null)
        {
            chunk.BiomeBlendedColorTexture.SetPixels32(chunk.PendingBiomeBlendedColorPixels);
            chunk.BiomeBlendedColorTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
        if (chunk.BiomeIdsTexture != null && chunk.PendingBiomeIdsPixels != null)
        {
            chunk.BiomeIdsTexture.SetPixels32(chunk.PendingBiomeIdsPixels);
            chunk.BiomeIdsTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
        if (chunk.BiomeWeightsTexture != null && chunk.PendingBiomeWeightsPixels != null)
        {
            chunk.BiomeWeightsTexture.SetPixels32(chunk.PendingBiomeWeightsPixels);
            chunk.BiomeWeightsTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
        if (releasePendingPixels)
        {
            chunk.PendingBiomeBlendedColorPixels = null;
            chunk.PendingBiomeIdsPixels = null;
            chunk.PendingBiomeWeightsPixels = null;
        }
    }

    public void BuildFaceAtlases(IReadOnlyList<PlanetChunk> chunks)
    {
        DisposeAtlases();

        if (_maxChunkDepth <= 0) return;

        int leafsPerAxis = 1 << _maxChunkDepth;
        int leafStride = PlanetChunkTextures.BiomeMapResolution - 1;
        int atlasResolution = leafsPerAxis * leafStride + 1;
        if (atlasResolution <= 1 || atlasResolution > SystemInfo.maxTextureSize)
        {
            LoggerProvider.Log(LogLevel.Warning, "PhaseB",
                $"Biome atlas skipped: requested {atlasResolution}x{atlasResolution}, max texture size is {SystemInfo.maxTextureSize}.");
            return;
        }

        _faceBlendedAtlases = new Texture2D[6];
        _faceIdAtlases = new Texture2D[6];
        _faceWeightAtlases = new Texture2D[6];

        int expectedLeafCount = leafsPerAxis * leafsPerAxis;
        for (int face = 0; face < 6; face++)
        {
            var blendedPixels = new Color32[atlasResolution * atlasResolution];
            var idPixels = new Color32[blendedPixels.Length];
            var weightPixels = new Color32[blendedPixels.Length];
            int copiedLeaves = 0;

            for (int i = 0; i < chunks.Count; i++)
            {
                PlanetChunk chunk = chunks[i];
                if (chunk == null || !chunk.IsLeaf || chunk.FaceIndex != face) continue;
                if (chunk.PendingBiomeBlendedColorPixels == null
                    || chunk.PendingBiomeIdsPixels == null
                    || chunk.PendingBiomeWeightsPixels == null)
                {
                    continue;
                }

                CopyLeafBiomeMapIntoAtlas(chunk, chunk.PendingBiomeBlendedColorPixels,
                    blendedPixels, atlasResolution, leafsPerAxis, leafStride);
                CopyLeafBiomeMapIntoAtlas(chunk, chunk.PendingBiomeIdsPixels,
                    idPixels, atlasResolution, leafsPerAxis, leafStride);
                CopyLeafBiomeMapIntoAtlas(chunk, chunk.PendingBiomeWeightsPixels,
                    weightPixels, atlasResolution, leafsPerAxis, leafStride);
                copiedLeaves++;
            }

            if (copiedLeaves <= 0)
            {
                LoggerProvider.Log(LogLevel.Warning, "PhaseB", $"Biome atlas face {face}: no leaf maps available.");
                continue;
            }

            _faceBlendedAtlases[face] = CreateAtlasTexture(
                $"BiomeBlendedAtlas_F{face}", atlasResolution, blendedPixels, FilterMode.Bilinear, linear: false);
            _faceIdAtlases[face] = CreateAtlasTexture(
                $"BiomeIdsAtlas_F{face}", atlasResolution, idPixels, FilterMode.Point, linear: true);
            _faceWeightAtlases[face] = CreateAtlasTexture(
                $"BiomeWeightsAtlas_F{face}", atlasResolution, weightPixels, FilterMode.Point, linear: true);

            LoggerProvider.Log(LogLevel.Debug, "PhaseB",
                $"Biome atlas face {face}: {atlasResolution}x{atlasResolution}, copied {copiedLeaves}/{expectedLeafCount} max-depth leaves.");
        }
        ReportMemory();
    }

    public bool TryGetFaceAtlases(int face, out Texture2D blended, out Texture2D ids, out Texture2D weights)
    {
        blended = null;
        ids = null;
        weights = null;
        if (face < 0 || face >= 6) return false;
        if (_faceBlendedAtlases == null || _faceIdAtlases == null || _faceWeightAtlases == null)
            return false;

        blended = _faceBlendedAtlases[face];
        ids = _faceIdAtlases[face];
        weights = _faceWeightAtlases[face];
        return blended != null && ids != null && weights != null;
    }

    public bool HasCompleteAtlases()
    {
        for (int face = 0; face < 6; face++)
        {
            if (!TryGetFaceAtlases(face, out _, out _, out _))
                return false;
        }
        return true;
    }

    public void ReleasePerChunkBiomeTextures(IReadOnlyList<PlanetChunk> chunks)
    {
        int released = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (PlanetChunkTextures.ReleaseBiomeTextures(chunks[i]))
                released++;
        }
        LoggerProvider.Log(LogLevel.Debug, "PhaseB",
            $"Released {released} redundant chunk biome texture sets after face-atlas binding.");
    }

    public bool UpdateFaceAtlasRegion(PlanetChunk chunk)
    {
        if (chunk == null
            || chunk.PendingBiomeBlendedColorPixels == null
            || chunk.PendingBiomeIdsPixels == null
            || chunk.PendingBiomeWeightsPixels == null)
        {
            return false;
        }
        if (!TryGetFaceAtlases(chunk.FaceIndex,
            out Texture2D blendedAtlas, out Texture2D idAtlas, out Texture2D weightAtlas))
        {
            return false;
        }

        EnsureStagingTextures();
        _blendedStaging.SetPixels32(chunk.PendingBiomeBlendedColorPixels);
        _blendedStaging.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _idStaging.SetPixels32(chunk.PendingBiomeIdsPixels);
        _idStaging.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        _weightStaging.SetPixels32(chunk.PendingBiomeWeightsPixels);
        _weightStaging.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        int leafsPerAxis = 1 << _maxChunkDepth;
        int leafStride = PlanetChunkTextures.BiomeMapResolution - 1;
        GetLeafAtlasOrigin(chunk, leafsPerAxis, leafStride, out int dstX, out int dstY);
        int resolution = PlanetChunkTextures.BiomeMapResolution;
        CopyAtlasRegion(_blendedStaging, blendedAtlas, resolution, dstX, dstY);
        CopyAtlasRegion(_idStaging, idAtlas, resolution, dstX, dstY);
        CopyAtlasRegion(_weightStaging, weightAtlas, resolution, dstX, dstY);

        chunk.PendingBiomeBlendedColorPixels = null;
        chunk.PendingBiomeIdsPixels = null;
        chunk.PendingBiomeWeightsPixels = null;
        return true;
    }

    public void Dispose() => DisposeAtlases();

    // One-shot diagnostic: scan all chunks, pick the leaf with the most distinct biomes (more
    // informative than the polar/corner default), and report its texel histogram. Also reports
    // any chunks where the biome map is uniformly zero (which would render as Ocean).
    public static void LogBakeSummary(IReadOnlyList<PlanetChunk> chunks, IBiomeProvider biomeProvider)
    {
        var registry = (biomeProvider as ColorGenerator)?.Registry;

        PlanetChunk best = null;
        int bestUnique = 0;
        int allZeroCount = 0;
        int totalLeafCount = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            if (c == null || !c.IsLeaf) continue;
            Color32[] pixels = GetBiomeIdDiagnosticPixels(c);
            if (pixels == null || pixels.Length == 0) continue;
            totalLeafCount++;
            var seen = new HashSet<byte>();
            for (int j = 0; j < pixels.Length; j++) seen.Add(pixels[j].r);
            if (seen.Count == 1 && pixels[0].r == 0) allZeroCount++;
            if (seen.Count > bestUnique) { best = c; bestUnique = seen.Count; }
        }

        if (best == null)
        {
            LoggerProvider.Log(LogLevel.Warning, "PhaseB", "Bake: no leaf chunks have populated biome ID pixels.");
            return;
        }

        var counts = new int[256];
        var bestPixels = GetBiomeIdDiagnosticPixels(best);
        for (int i = 0; i < bestPixels.Length; i++) counts[bestPixels[i].r]++;

        var sb = new System.Text.StringBuilder();
        sb.Append($"Bake: {chunks.Count} chunks ({allZeroCount}/{totalLeafCount} leaves are uniformly Ocean). Most-diverse chunk F{best.FaceIndex} D{best.DetailLevel} H{best.HashValue} dominant-id distribution: ");
        bool first = true;
        for (int id = 0; id < counts.Length; id++)
        {
            if (counts[id] == 0) continue;
            if (!first) sb.Append(", ");
            string biomeName = registry?.GetDefinitionByIndex(id)?.Type.ToString() ?? "?";
            sb.Append($"{biomeName}({id})={counts[id]}");
            first = false;
        }
        LoggerProvider.Log(LogLevel.Debug, "PhaseB", sb.ToString());
    }

    public static void ReleasePendingPixels(IReadOnlyList<PlanetChunk> chunks)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            PlanetChunk chunk = chunks[i];
            if (chunk == null) continue;
            chunk.PendingBiomeBlendedColorPixels = null;
            chunk.PendingBiomeIdsPixels = null;
            chunk.PendingBiomeWeightsPixels = null;
        }
    }

    static Color32[] GetBiomeIdDiagnosticPixels(PlanetChunk chunk)
    {
        if (chunk?.PendingBiomeIdsPixels != null)
            return chunk.PendingBiomeIdsPixels;
        return chunk?.BiomeIdsTexture != null ? chunk.BiomeIdsTexture.GetPixels32() : null;
    }

    void EnsureStagingTextures()
    {
        if (_blendedStaging == null)
            _blendedStaging = CreateStagingTexture("BiomeBlendedAtlasStaging", FilterMode.Bilinear, linear: false);
        if (_idStaging == null)
            _idStaging = CreateStagingTexture("BiomeIdsAtlasStaging", FilterMode.Point, linear: true);
        if (_weightStaging == null)
            _weightStaging = CreateStagingTexture("BiomeWeightsAtlasStaging", FilterMode.Point, linear: true);
    }

    void ReportMemory()
    {
        int textureCount = 0;
        long rawBytes = 0L;
        CountTextureArray(_faceBlendedAtlases, ref textureCount, ref rawBytes);
        CountTextureArray(_faceIdAtlases, ref textureCount, ref rawBytes);
        CountTextureArray(_faceWeightAtlases, ref textureCount, ref rawBytes);
        MemoryDebugCounters.AdjustFaceBiomeAtlases(
            textureCount - _reportedTextureCount,
            rawBytes - _reportedRawBytes);
        _reportedTextureCount = textureCount;
        _reportedRawBytes = rawBytes;
    }

    void DisposeAtlases()
    {
        DestroyTextureArray(ref _faceBlendedAtlases);
        DestroyTextureArray(ref _faceIdAtlases);
        DestroyTextureArray(ref _faceWeightAtlases);
        DestroyTexture(ref _blendedStaging);
        DestroyTexture(ref _idStaging);
        DestroyTexture(ref _weightStaging);
        MemoryDebugCounters.AdjustFaceBiomeAtlases(-_reportedTextureCount, -_reportedRawBytes);
        _reportedTextureCount = 0;
        _reportedRawBytes = 0L;
    }

    static Texture2D CreateAtlasTexture(string name, int resolution, Color32[] pixels, FilterMode filterMode, bool linear)
    {
        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false, linear: linear)
        {
            name = name,
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    static void CopyLeafBiomeMapIntoAtlas(PlanetChunk chunk, Color32[] source,
        Color32[] atlas, int atlasResolution, int leafsPerAxis, int leafStride)
    {
        int mapResolution = PlanetChunkTextures.BiomeMapResolution;
        if (source == null || source.Length != mapResolution * mapResolution) return;

        GetLeafAtlasOrigin(chunk, leafsPerAxis, leafStride, out int dstX0, out int dstY0);

        for (int y = 0; y < mapResolution; y++)
        {
            int srcRow = y * mapResolution;
            int dstRow = (dstY0 + y) * atlasResolution + dstX0;
            System.Array.Copy(source, srcRow, atlas, dstRow, mapResolution);
        }
    }

    static void GetLeafAtlasOrigin(PlanetChunk chunk, int leafsPerAxis, int leafStride, out int dstX, out int dstY)
    {
        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        int leafX = Mathf.Clamp(Mathf.RoundToInt(minU * leafsPerAxis), 0, leafsPerAxis - 1);
        int leafY = Mathf.Clamp(Mathf.RoundToInt(minV * leafsPerAxis), 0, leafsPerAxis - 1);
        dstX = leafX * leafStride;
        dstY = leafY * leafStride;
    }

    static Texture2D CreateStagingTexture(string name, FilterMode filterMode, bool linear)
    {
        return new Texture2D(
            PlanetChunkTextures.BiomeMapResolution,
            PlanetChunkTextures.BiomeMapResolution,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: linear)
        {
            name = name,
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
        };
    }

    static void CopyAtlasRegion(Texture2D source, Texture2D destination, int resolution, int dstX, int dstY)
    {
        Graphics.CopyTexture(
            source, 0, 0, 0, 0, resolution, resolution,
            destination, 0, 0, dstX, dstY);
    }

    static void DestroyTextureArray(ref Texture2D[] textures)
    {
        if (textures == null) return;
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            if (Application.isPlaying) Object.Destroy(textures[i]);
            else Object.DestroyImmediate(textures[i]);
            textures[i] = null;
        }
        textures = null;
    }

    static void DestroyTexture(ref Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) Object.Destroy(texture);
        else Object.DestroyImmediate(texture);
        texture = null;
    }

    static void CountTextureArray(Texture2D[] textures, ref int count, ref long rawBytes)
    {
        if (textures == null) return;
        for (int i = 0; i < textures.Length; i++)
        {
            Texture2D texture = textures[i];
            if (texture == null) continue;
            count++;
            rawBytes += (long)texture.width * texture.height * 4L;
        }
    }
}
