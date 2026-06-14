using System.Collections.Generic;
using UnityEngine;

public interface IChunkMeshCache
{
    event System.Action<PlanetChunk> ChunkShown;
    event System.Action<PlanetChunk> ChunkHidden;

    void AttachFaceObjects(Transform[] faceRoots, bool[] faceVisible);
    void SetVisible(PlanetChunk chunk, bool visible);
    bool IsActuallyVisible(PlanetChunk chunk);
    void Rebind(PlanetChunk chunk);
    void RebindAll();
    bool RetainColorSource(PlanetChunk chunk);
    bool RetainBiomeSource(PlanetChunk chunk);
    void Dispose();
}

// Pooled GPU-mesh layer for the chunked surface. Only the currently visible set plus a small
// LRU reserve hold a live Unity mesh; chunks page in (mesh rebuilt from retained compact CPU
// source) when shown and park in the reserve when hidden, with the oldest reserve handle
// recycled once the cap is reached. Also binds each chunk's biome map (face atlas or per-chunk
// fallback) via a MaterialPropertyBlock and raises the external Chunk shown/hidden events.
//
// Split out of ChunkedSurfaceProvider (restructure step 3). Reads IBiomeAtlasService one-way;
// the provider mediates the rebake feedback (bake, then Rebind here) so this layer never calls
// back into the bake.
public sealed class ChunkMeshCache : IChunkMeshCache
{
    readonly Material _faceMaterial;
    readonly IBiomeAtlasService _biomeAtlas;
    readonly int _chunkResolution;

    Transform[] _faceRoots;
    bool[] _faceVisible;

    // _chunkRenderers maps only the chunks currently holding a handle.
    readonly Dictionary<PlanetChunk, ChunkRenderHandle> _chunkRenderers = new();
    readonly LinkedList<PlanetChunk> _renderReserveLru = new();
    const int MaxRenderHandles = 320;

    MaterialPropertyBlock _chunkPropertyBlock;
    Vector4[] _biomeUploadScratch;

    // Step 5b: per-chunk shader-property ids. Three textures replace the single _BiomeMap
    // of step 5: _BiomeBlendedColor (bilinear, the cheap shader path), _BiomeIds (point,
    // used by step 6+ texture array path and Phase C grass), _BiomeWeights (bilinear).
    // Step 9: _SurfaceStateMask bound here for Phase E (currently bound but unread by shader).
    static readonly int BiomeBlendedColorShaderId = Shader.PropertyToID("_BiomeBlendedColor");
    static readonly int BiomeIdsShaderId = Shader.PropertyToID("_BiomeIds");
    static readonly int BiomeWeightsShaderId = Shader.PropertyToID("_BiomeWeights");
    static readonly int SurfaceStateMaskShaderId = Shader.PropertyToID("_SurfaceStateMask");
    static readonly int BiomeMapTexelSizeShaderId = Shader.PropertyToID("_BiomeMap_TexelSize");
    static readonly int BiomeMapUvScaleShaderId = Shader.PropertyToID("_BiomeMapUvScale");

    public event System.Action<PlanetChunk> ChunkShown;
    public event System.Action<PlanetChunk> ChunkHidden;

    public ChunkMeshCache(Material faceMaterial, IBiomeAtlasService biomeAtlas, int chunkResolution)
    {
        _faceMaterial = faceMaterial;
        _biomeAtlas = biomeAtlas;
        _chunkResolution = chunkResolution;
    }

    // Face roots + per-face visibility are created by the orchestrator (EnsureFaceObjects) after
    // construction, so they are wired in once they exist rather than through the constructor.
    public void AttachFaceObjects(Transform[] faceRoots, bool[] faceVisible)
    {
        _faceRoots = faceRoots;
        _faceVisible = faceVisible;
    }

    public void SetVisible(PlanetChunk chunk, bool visible)
    {
        bool active = visible && _faceVisible != null && _faceVisible[chunk.FaceIndex];
        if (active)
        {
            var handle = AcquireRenderHandle(chunk);
            if (handle == null || handle.GameObject == null) return;
            if (!handle.Visible)
            {
                handle.Visible = true;
                handle.GameObject.SetActive(true);
                ChunkShown?.Invoke(chunk);
            }
        }
        else
        {
            if (!_chunkRenderers.TryGetValue(chunk, out var handle) || handle == null) return;
            if (handle.Visible)
            {
                handle.Visible = false;
                handle.GameObject.SetActive(false);
                ChunkHidden?.Invoke(chunk);
            }
            // Park in the reserve LRU so a quick return reuses the mesh instead of rebuilding it.
            if (handle.ReserveNode == null)
                handle.ReserveNode = _renderReserveLru.AddLast(chunk);
        }
    }

    public bool IsActuallyVisible(PlanetChunk chunk)
    {
        return chunk != null
            && _chunkRenderers.TryGetValue(chunk, out var handle)
            && handle != null
            && handle.Visible;
    }

    public void Rebind(PlanetChunk chunk)
    {
        if (chunk != null && _chunkRenderers.TryGetValue(chunk, out var handle))
            BindChunkBiomeProperties(handle, chunk);
    }

    public void RebindAll()
    {
        foreach (var pair in _chunkRenderers)
            BindChunkBiomeProperties(pair.Value, pair.Key);
    }

    public void Dispose()
    {
        foreach (var pair in _chunkRenderers)
        {
            var handle = pair.Value;
            if (handle == null) continue;
            if (handle.Mesh != null) Object.Destroy(handle.Mesh);
            if (handle.GameObject != null) Object.Destroy(handle.GameObject);
        }
        _chunkRenderers.Clear();
        _renderReserveLru.Clear();
        MemoryDebugCounters.ReportChunkRenderHandles(0, 0);
    }

    // Returns the render handle for a chunk, building it (and its mesh) on demand. Once over the
    // cap, prefers recycling the least-recently-hidden reserve handle; if nothing is parked
    // (every live handle is currently visible) it still builds a new one rather than starve a
    // visible chunk. The cap therefore bounds the *reserve*, not the visible set — total live
    // handles settle at the peak simultaneously-visible count.
    ChunkRenderHandle AcquireRenderHandle(PlanetChunk chunk)
    {
        if (chunk == null) return null;
        if (_chunkRenderers.TryGetValue(chunk, out var existing))
        {
            // Wanted again — pull it back out of the reserve if it was parked there.
            if (existing.ReserveNode != null)
            {
                _renderReserveLru.Remove(existing.ReserveNode);
                existing.ReserveNode = null;
            }
            return existing;
        }
        if (chunk.CpuVertices == null || chunk.CpuVertices.Length == 0) return null;
        if (_faceRoots == null || chunk.FaceIndex < 0 || chunk.FaceIndex >= _faceRoots.Length) return null;

        ChunkRenderHandle handle = _chunkRenderers.Count < MaxRenderHandles
            ? CreateRenderHandle()
            : EvictReserveHandle() ?? CreateRenderHandle();

        PopulateRenderHandle(handle, chunk);
        handle.Chunk = chunk;
        _chunkRenderers[chunk] = handle;
        ReportRenderHandleMemory();
        return handle;
    }

    ChunkRenderHandle CreateRenderHandle()
    {
        var go = new GameObject("chunk-pooled");
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _faceMaterial;
        var filter = go.AddComponent<MeshFilter>();
        go.SetActive(false);
        return new ChunkRenderHandle { GameObject = go, Renderer = renderer, Filter = filter };
    }

    // Recycles the oldest hidden (reserve) handle: frees its GPU mesh and returns the bare
    // GameObject + components for reuse. The retained CPU source rebuilds it on the next page-in.
    ChunkRenderHandle EvictReserveHandle()
    {
        var node = _renderReserveLru.First;
        if (node == null) return null; // everything mapped is currently visible
        _renderReserveLru.RemoveFirst();
        PlanetChunk chunk = node.Value;
        if (!_chunkRenderers.TryGetValue(chunk, out var handle) || handle == null) return null;

        _chunkRenderers.Remove(chunk);
        handle.ReserveNode = null;
        handle.Chunk = null;
        handle.Visible = false;
        if (handle.GameObject != null) handle.GameObject.SetActive(false);
        if (handle.Filter != null) handle.Filter.sharedMesh = null;
        if (handle.Mesh != null) { Object.Destroy(handle.Mesh); handle.Mesh = null; }
        return handle;
    }

    void PopulateRenderHandle(ChunkRenderHandle handle, PlanetChunk chunk)
    {
        var go = handle.GameObject;
        go.name = $"chunk-f{chunk.FaceIndex}-d{chunk.DetailLevel}-{chunk.HashValue:X}";
        go.transform.SetParent(_faceRoots[chunk.FaceIndex], worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mesh = new Mesh { name = go.name };
        mesh.SetVertices(chunk.CpuVertices);
        // Prefer the Burst-computed terrain-aware normals; fall back to unit-sphere directions
        // if the normals job hasn't populated them (e.g., legacy paths that didn't chain it).
        if (chunk.CpuNormals != null && chunk.CpuNormals.Length == chunk.CpuVertices.Length)
            mesh.SetNormals(chunk.CpuNormals);
        else if (chunk.CpuUnitSpherePoints != null && chunk.CpuUnitSpherePoints.Length == chunk.CpuVertices.Length)
            mesh.SetNormals(chunk.CpuUnitSpherePoints);
        mesh.SetUVs(0, ChunkUvTemplate.Get(_chunkResolution));
        mesh.SetTriangles(ChunkTriangleTemplate.Get(_chunkResolution), 0, false);
        if (chunk.CpuColors32 != null && chunk.CpuColors32.Length == chunk.CpuVertices.Length)
            mesh.SetColors(chunk.CpuColors32);
        if (chunk.CpuBiomeData != null && chunk.CpuBiomeData.Length == chunk.CpuVertices.Length)
            mesh.SetUVs(2, chunk.CpuBiomeData);
        else if (chunk.CpuBiomeData32 != null && chunk.CpuBiomeData32.Length == chunk.CpuVertices.Length)
        {
            EnsureBiomeUploadScratch(chunk.CpuBiomeData32.Length);
            const float byteToFloat = 1f / 255f;
            for (int i = 0; i < chunk.CpuBiomeData32.Length; i++)
            {
                Color32 packed = chunk.CpuBiomeData32[i];
                _biomeUploadScratch[i] = new Vector4(
                    packed.r * byteToFloat,
                    packed.g * byteToFloat,
                    packed.b * byteToFloat,
                    packed.a * byteToFloat);
            }
            mesh.SetUVs(2, _biomeUploadScratch);
        }
        mesh.bounds = chunk.CpuLocalBounds;
        // GPU-only; raycasts read CpuVertices, never the mesh, and we always rebuild on page-in.
        mesh.UploadMeshData(true);

        handle.Mesh = mesh;
        handle.Filter.sharedMesh = mesh;

        // Bind the per-chunk biome map (or face atlas) as a MaterialPropertyBlock so the shared
        // planet material can sample it without a per-chunk material instance.
        BindChunkBiomeProperties(handle, chunk);
    }

    void ReportRenderHandleMemory()
    {
        MemoryDebugCounters.ReportChunkRenderHandles(
            _chunkRenderers.Count, _chunkRenderers.Count * EstimateChunkMeshBytes());
    }

    long EstimateChunkMeshBytes()
    {
        int verts = _chunkResolution * _chunkResolution;
        // pos(12) + normal(12) + uv0(8) + color32(4) + uv2(16) = 52 B/vertex.
        long vertexBytes = (long)verts * 52L;
        int quads = (_chunkResolution - 1) * (_chunkResolution - 1);
        long indexBytes = (long)quads * 6L * 2L; // 16-bit indices (verts < 65535)
        return vertexBytes + indexBytes;
    }

    void BindChunkBiomeProperties(ChunkRenderHandle handle, PlanetChunk chunk)
    {
        if (handle?.Renderer == null || chunk == null) return;
        bool usingAtlas = _biomeAtlas.TryGetFaceAtlases(chunk.FaceIndex,
            out Texture2D blendedTexture, out Texture2D idsTexture, out Texture2D weightsTexture);
        if (!usingAtlas)
        {
            blendedTexture = chunk.BiomeBlendedColorTexture;
            idsTexture = chunk.BiomeIdsTexture;
            weightsTexture = chunk.BiomeWeightsTexture;
        }
        if (blendedTexture == null) return;

        _chunkPropertyBlock ??= new MaterialPropertyBlock();
        handle.Renderer.GetPropertyBlock(_chunkPropertyBlock);
        _chunkPropertyBlock.SetTexture(BiomeBlendedColorShaderId, blendedTexture);
        if (idsTexture != null)
            _chunkPropertyBlock.SetTexture(BiomeIdsShaderId, idsTexture);
        if (weightsTexture != null)
            _chunkPropertyBlock.SetTexture(BiomeWeightsShaderId, weightsTexture);
        // Phase B step 9: per-chunk surface-state mask bound here. Always chunk-local
        // (no atlas) because Phase E mutations are per-chunk. Shader sampling is deferred
        // to Phase E â€” for now the binding just exercises the upload path.
        if (chunk.SurfaceStateTexture != null)
            _chunkPropertyBlock.SetTexture(SurfaceStateMaskShaderId, chunk.SurfaceStateTexture);
        // _TexelSize is (1/w, 1/h, w, h) â€” matches Unity's built-in texture layout so the
        // shader can do neighbor-texel lookups without external math.
        int res = Mathf.Max(blendedTexture.width, 1);
        _chunkPropertyBlock.SetVector(BiomeMapTexelSizeShaderId,
            new Vector4(1f / res, 1f / res, res, res));
        // Chunk-local UV maps either into the face atlas or into the chunk-local fallback map.
        _chunkPropertyBlock.SetVector(BiomeMapUvScaleShaderId,
            usingAtlas ? GetFaceAtlasUvScale(chunk) : new Vector4(1f, 1f, 0f, 0f));
        handle.Renderer.SetPropertyBlock(_chunkPropertyBlock);
    }

    static Vector4 GetFaceAtlasUvScale(PlanetChunk chunk)
    {
        float size = chunk.UvHalfExtent * 2f;
        return new Vector4(size, size,
            chunk.UvCenter.x - chunk.UvHalfExtent,
            chunk.UvCenter.y - chunk.UvHalfExtent);
    }

    // Compacts the baked per-vertex colors to Color32 and retains them so the pooled render path
    // can rebuild a chunk's mesh on page-in. The heavy Color[] is freed by the caller afterward.
    public bool RetainColorSource(PlanetChunk chunk)
    {
        if (chunk == null || chunk.CpuColors == null) return false;

        int count = chunk.CpuColors.Length;
        var compact = chunk.CpuColors32;
        if (compact == null || compact.Length != count) compact = new Color32[count];
        for (int i = 0; i < count; i++) compact[i] = chunk.CpuColors[i];
        chunk.CpuColors32 = compact;
        return true;
    }

    public bool RetainBiomeSource(PlanetChunk chunk)
    {
        if (chunk == null)
            return false;
        if (chunk.CpuBiomeData == null)
            return chunk.CpuBiomeData32 != null;

        int count = chunk.CpuBiomeData.Length;
        var compact = chunk.CpuBiomeData32;
        if (compact == null || compact.Length != count)
            compact = new Color32[count];

        for (int i = 0; i < count; i++)
        {
            Vector4 data = chunk.CpuBiomeData[i];
            compact[i] = new Color32(
                ToNormalizedByte(data.x),
                ToNormalizedByte(data.y),
                ToNormalizedByte(data.z),
                ToNormalizedByte(data.w));
        }

        chunk.CpuBiomeData32 = compact;
        return true;
    }

    void EnsureBiomeUploadScratch(int count)
    {
        if (_biomeUploadScratch == null || _biomeUploadScratch.Length != count)
            _biomeUploadScratch = new Vector4[count];
    }

    static byte ToNormalizedByte(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }

    sealed class ChunkRenderHandle
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public MeshFilter Filter;
        public Mesh Mesh;
        public bool Visible;
        public PlanetChunk Chunk;                        // which chunk this handle currently renders
        public LinkedListNode<PlanetChunk> ReserveNode;  // non-null while parked in the reserve LRU
    }
}
