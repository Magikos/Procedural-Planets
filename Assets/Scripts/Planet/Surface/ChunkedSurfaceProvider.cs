using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

public interface IChunkVisibilitySource
{
    IReadOnlyList<PlanetChunk> GetVisibleChunksSnapshot();
    event System.Action<PlanetChunk> ChunkShown;
    event System.Action<PlanetChunk> ChunkHidden;
}

// Phase A "High" resolution provider — pre-cache + visibility filter.
//
// At Planet.GenerateAsync time, the provider builds 6 quadtrees to a fixed MaxChunkDepth and
// schedules Burst mesh jobs for EVERY chunk (internal + leaves) in batches. All chunk CPU
// vertex data is populated by the time GenerateAsync returns. At runtime, Tick is a cheap
// visibility filter: for each non-leaf, decide "render this OR recurse into children" based
// on camera distance. Rebuilds happen only when a face's visible leaf set actually changes,
// capped at 1 face per Tick.
//
// Pivoted to this design 2026-05-30 after step-7 perf testing showed dynamic subdivision
// hitched too hard during fly-through. See docs/design/2026-05-30-chunk-skeleton.md.
public sealed class ChunkedSurfaceProvider : IPlanetSurfaceProvider, IChunkVisibilitySource
{
    // Per-chunk vertex grid resolution. 97 = 9,409 vertices and 18,432 triangles per chunk
    // (vs 65 = 4,225 verts / 8,192 tris). The bump improves biome-color sharpness and terrain
    // detail at the cost of ~2.2× memory and mesh-gen time. True per-pixel biome boundaries
    // need shader-based biome sampling, which is Phase B work — this is the best we can do at
    // the vertex-color level.
    const int ChunkResolution = 97;
    // Schedule chunks in batches during initial gen to bound transient NativeArray memory.
    // 128 chunks × ~135 KB/chunk ≈ 17 MB transient — comfortable on PC.
    const int InitialGenBatchSize = 128;
    // LOD target: a chunk refines when its conservative projected diameter exceeds this.
    // This is a screen-space contract, not a terrain-value tuning knob.
    const float TargetChunkScreenPixels = 220f;
    const float LodSplitHysteresis = 1.10f;
    const float LodMergeHysteresis = 0.85f;
    const float FrustumBoundsPadding = 1.08f;
    const float HorizonRadiusScale = 0.98f;
    const float HorizonMarginScale = 2f;
    const float DiagnosticsLogIntervalSeconds = 1f;
    const int ChunkMeshUploadBatchSize = 24;
    // Depth at which we aggregate chunk vertex data for the WaterMeshBuilder face sampler.
    // 2 → 385² per face (with R=97), 16× finer shorelines than the root chunk. Bounded by
    // MaxChunkDepth at construction time. Memory: ~14 MB total across 6 faces at depth 2.
    const int WaterAggregateDepth = 2;
    // Flip to true (or define the symbol below) to log per-tick chunk visibility breakdowns.
    // Default off — was spamming the console once visibility changes were happening every frame
    // during fly-through. Useful when diagnosing LOD/culling behavior.
#if PLANET_CHUNK_DIAGNOSTICS
    const bool EnableChunkDiagnosticsLog = true;
#else
    static readonly bool EnableChunkDiagnosticsLog = false;
#endif

    readonly Transform _planetTransform;
    readonly ShapeGenerator _shapeGenerator;
    readonly Material _faceMaterial;
    readonly Planet.FaceRenderMask _renderMask;
    readonly int _maxChunkDepth;

    Transform[] _faceRoots;
    TerrainQuadtree[] _quadtrees;
    bool[] _faceVisible;
    IFaceMeshSampler[] _rootSamplers;
    Texture2D[] _faceBiomeBlendedAtlases;
    Texture2D[] _faceBiomeIdAtlases;
    Texture2D[] _faceBiomeWeightAtlases;
    GrassSurfaceAtlasGpuData _grassSurfaceAtlases;

    // Per-face visible-leaf snapshot — compared each Tick to detect changes.
    readonly List<PlanetChunk>[] _visibleLeavesPerFace = new List<PlanetChunk>[6];
    readonly List<PlanetChunk> _tmpVisibleLeaves = new();
    readonly HashSet<PlanetChunk> _tmpVisibleSet = new();
    readonly List<PlanetChunk> _allChunks = new();
    readonly Dictionary<PlanetChunk, ChunkRenderHandle> _chunkRenderers = new();
    // Step 5b: per-bake thread-local scratch buffer used by BiomeMapBaker for the high-res
    // biome id grid (HighResolutionSize²). Reused across all chunks on a thread to keep the
    // bake GC-free. Sized lazily on first use.
    [System.ThreadStatic] static byte[] _tlsBakeHighResBuffer;

    readonly Plane[] _lodFrustumPlanes = new Plane[6];
    Camera _lodCamera;
    bool _hasLodCamera;
    float _lodFocalLengthPixels = 935f;
    float _nextDiagnosticsLogTime;

    // In-flight chunk jobs — only used during initial gen; empty at runtime.
    readonly List<PendingChunkJob> _pendingJobs = new();

    NativeArray<NoiseFilterData> _filters;
    bool _initialized;

    static readonly string ProfTick = "ChunkedSurfaceProvider.Tick";
    static readonly string ProfVisibility = "ChunkedSurfaceProvider.UpdateVisibility";

    public event System.Action<PlanetChunk> ChunkShown;
    public event System.Action<PlanetChunk> ChunkHidden;
    public GrassSurfaceAtlasGpuData GrassSurfaceAtlases => _grassSurfaceAtlases;
    public int MaxChunkDepth => _maxChunkDepth;

    public ChunkedSurfaceProvider(
        Transform planetTransform,
        ShapeGenerator shapeGenerator,
        Material faceMaterial,
        Planet.FaceRenderMask renderMask,
        int maxChunkDepth)
    {
        _planetTransform = planetTransform;
        _shapeGenerator = shapeGenerator;
        _faceMaterial = faceMaterial;
        _renderMask = renderMask;
        _maxChunkDepth = Mathf.Clamp(maxChunkDepth, 0, PlanetChunk.MaxDetailLevel);
    }

    public async Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct)
    {
        EnsureFaceObjects();

        progress?.Report(0f, "Building chunk quadtrees...");

        // Keep texture-mode terrain disabled until GenerateColorsAsync has baked and bound
        // real biome maps. Otherwise newly visible chunks sample blank startup textures.
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);

        if (_filters.IsCreated) _filters.Dispose();
        _filters = _shapeGenerator.BuildNoiseFilterData(Allocator.Persistent);

        // 1) Build the full quadtree to max depth on every face.
        for (int f = 0; f < 6; f++)
            _quadtrees[f].BuildToFixedDepth(_maxChunkDepth);

        // 2) Gather every chunk (internal + leaf) — all are rendering candidates depending
        //    on camera distance. Sort leaves-first / coarse-to-fine isn't necessary; the
        //    Burst scheduler handles arbitrary order well.
        var allChunks = new List<PlanetChunk>(1024);
        for (int f = 0; f < 6; f++) CollectAllChunks(_quadtrees[f].Root, allChunks);
        _allChunks.Clear();
        _allChunks.AddRange(allChunks);

        // Phase B step 3: allocate per-chunk biome map + surface state textures up front.
        // Bake population happens in step 4; for now they're cleared to zero so the lifecycle
        // path is exercised regardless of whether the bake exists.
        for (int i = 0; i < _allChunks.Count; i++)
            PlanetChunkTextures.Allocate(_allChunks[i]);

        int total = allChunks.Count;
        progress?.Report(0.05f, $"Generating {total} chunks...");

        // 3) Schedule + drain in batches to bound transient memory.
        for (int batchStart = 0; batchStart < total; batchStart += InitialGenBatchSize)
        {
            int batchEnd = Mathf.Min(batchStart + InitialGenBatchSize, total);
            int batchSize = batchEnd - batchStart;

            var handles = new NativeArray<JobHandle>(batchSize, Allocator.Temp);
            try
            {
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ScheduleChunkJob(allChunks[i]);
                    handles[i - batchStart] = _pendingJobs[_pendingJobs.Count - 1].State.Handle;
                }
                var combined = JobHandle.CombineDependencies(handles);
                handles.Dispose();
                JobHandle.ScheduleBatchedJobs();

                while (!combined.IsCompleted)
                {
                    if (ct.IsCancellationRequested)
                    {
                        combined.Complete();
                        DisposeAllPendingJobs();
                        ct.ThrowIfCancellationRequested();
                    }
                    await Awaitable.NextFrameAsync();
                }
                combined.Complete();
            }
            catch
            {
                if (handles.IsCreated) handles.Dispose();
                DisposeAllPendingJobs();
                throw;
            }

            DrainCompletedJobs();
            float pct = (float)batchEnd / total;
            progress?.Report(0.05f + 0.80f * pct, $"Generated chunks {batchEnd}/{total}");
        }

        // 4) Upload cached Unity meshes once. Runtime LOD toggles renderers instead of
        // rebuilding combined face meshes.
        await UploadChunkRenderersAsync(allChunks, progress, ct);

        // 5) Build face-sampler views for the water builder. Aggregating depth-N chunks into
        //    a single per-face grid raises water-mesh resolution from ChunkResolution (root)
        //    to 2^N × (ChunkResolution-1) + 1 — at depth 2 with R=97 that's 385² per face,
        //    16× finer shorelines than the root sampler.
        int waterAggregateDepth = Mathf.Clamp(WaterAggregateDepth, 0, _maxChunkDepth);
        _rootSamplers = new IFaceMeshSampler[6];
        for (int f = 0; f < 6; f++)
        {
            if (waterAggregateDepth == 0)
            {
                _rootSamplers[f] = new ChunkedFaceMeshSampler(_quadtrees[f].Root, ChunkResolution);
            }
            else
            {
                var chunksAtDepth = new List<PlanetChunk>(1 << (waterAggregateDepth * 2));
                CollectChunksAtDepth(_quadtrees[f].Root, waterAggregateDepth, chunksAtDepth);
                _rootSamplers[f] = new ChunkedFaceMeshSampler(chunksAtDepth, ChunkResolution, waterAggregateDepth);
            }
        }

        // 6) Build face-space radius + normal atlases for Phase C grass placement. The
        // renderer is added later; this data must exist before grass compute can place roots.
        progress?.Report(0.92f, "Building grass surface atlases...");
        BuildGrassSurfaceAtlases();

        // 7) Defer initial visibility until Planet finishes color/water generation.
        // The biome textures exist here but are still blank; showing chunks now can render a
        // cyan/teal placeholder surface if the player jumps to ground during loading.
        progress?.Report(0.94f, "Preparing visibility...");
        for (int f = 0; f < 6; f++)
            _visibleLeavesPerFace[f]?.Clear();

        _initialized = true;
        LogChunkDiagnostics("initial");
        progress?.Report(1f, "Chunked planet ready.");
    }

    public void Tick(Vector3 observerWorldPosition, Camera observerCamera)
    {
        if (!_initialized) return;
        Profiler.BeginSample(ProfTick);

        PrepareLodContext(observerCamera);
        bool anyVisibilityChanged = false;

        // Per-face visibility filter. Changes only toggle cached chunk renderers.
        Profiler.BeginSample(ProfVisibility);
        for (int f = 0; f < 6; f++)
        {
            if (UpdateVisibleLeavesForFace(f, observerWorldPosition))
            {
                anyVisibilityChanged = true;
            }
        }
        Profiler.EndSample();

        if (anyVisibilityChanged)
            LogChunkDiagnostics("tick");

        Profiler.EndSample();
    }

    public IReadOnlyList<PlanetChunk> GetVisibleChunksSnapshot()
    {
        var snapshot = new List<PlanetChunk>(128);
        if (_visibleLeavesPerFace == null) return snapshot;

        for (int f = 0; f < _visibleLeavesPerFace.Length; f++)
        {
            var leaves = _visibleLeavesPerFace[f];
            if (leaves == null) continue;

            for (int i = 0; i < leaves.Count; i++)
            {
                PlanetChunk chunk = leaves[i];
                if (chunk != null && IsChunkActuallyVisible(chunk))
                    snapshot.Add(chunk);
            }
        }

        return snapshot;
    }

    public bool TryGetLocalSurfaceRadius(Vector3 localUnitDirection, out float localRadius)
    {
        localRadius = 0f;
        if (_quadtrees == null) return false;

        var (face, faceUv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection);
        if (face < 0 || face >= 6 || _quadtrees[face] == null) return false;

        var leaf = _quadtrees[face].FindLeafContaining(faceUv);
        while (leaf != null && leaf.CpuVertices == null) leaf = leaf.Parent;
        if (leaf == null) return false;

        float chunkSize = leaf.UvHalfExtent * 2f;
        Vector2 chunkLocalUv = new(
            (faceUv.x - (leaf.UvCenter.x - leaf.UvHalfExtent)) / chunkSize,
            (faceUv.y - (leaf.UvCenter.y - leaf.UvHalfExtent)) / chunkSize);

        return leaf.TrySampleRadius(chunkLocalUv, out localRadius) && localRadius > 0f;
    }

    public bool TryRaycastVisibleSurface(Ray localRay, float maxDistance,
        out Vector3 localPoint, out Vector3 localNormal, out float localDistance)
    {
        localPoint = default;
        localNormal = default;
        localDistance = 0f;

        if (_visibleLeavesPerFace == null || localRay.direction.sqrMagnitude < 0.0001f)
            return false;

        localRay.direction = localRay.direction.normalized;
        float bestDistance = maxDistance > 0f ? maxDistance : float.PositiveInfinity;
        bool hit = false;
        int[] triangles = ChunkTriangleTemplate.Get(ChunkResolution);

        for (int face = 0; face < _visibleLeavesPerFace.Length; face++)
        {
            List<PlanetChunk> leaves = _visibleLeavesPerFace[face];
            if (leaves == null) continue;

            for (int i = 0; i < leaves.Count; i++)
            {
                PlanetChunk chunk = leaves[i];
                if (!IsChunkActuallyVisible(chunk) || chunk.CpuVertices == null)
                    continue;
                if (!chunk.CpuLocalBounds.IntersectRay(localRay, out float boundsDistance))
                    continue;
                if (boundsDistance > bestDistance)
                    continue;

                if (RaycastChunkTriangles(localRay, chunk, triangles, bestDistance,
                        out float chunkDistance, out Vector3 chunkPoint, out Vector3 chunkNormal)
                    && chunkDistance < bestDistance)
                {
                    bestDistance = chunkDistance;
                    localPoint = chunkPoint;
                    localNormal = chunkNormal;
                    hit = true;
                }
            }
        }

        if (!hit)
            return false;

        localDistance = bestDistance;
        if (localNormal.sqrMagnitude < 0.0001f)
            localNormal = localPoint.sqrMagnitude > 0.0001f ? localPoint.normalized : Vector3.up;
        localNormal.Normalize();
        if (Vector3.Dot(localNormal, localPoint) < 0f)
            localNormal = -localNormal;
        return true;
    }

    // Phase B step 9: cached biome provider so RebakeBiomeMapsAt (Phase E entry point) can
    // re-run the bake without the caller plumbing the provider through again. Set on each
    // GenerateColorsAsync call.
    IBiomeProvider _cachedBiomeProvider;

    public async Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct)
    {
        if (biomeProvider == null || _allChunks.Count == 0) return;
        _cachedBiomeProvider = biomeProvider;

        progress?.Report(0f, "Applying biome colors...");

        // Build the Burst-compatible biome lookup snapshot + flat color LUT once for the
        // whole bake pass. ColorGenerator is the only IBiomeProvider that owns a registry +
        // color table; other providers fall through with bakeLookupBuilt=false so the chunk
        // biome textures stay at their step-3 zero-init state.
        BiomeLookupData lookup = default;
        Color[] lutColors = null;
        bool bakeLookupBuilt = false;
        if (biomeProvider is ColorGenerator cg && cg.Registry != null && cg.BiomeColors != null)
        {
            lookup = cg.Registry.BuildLookupData(Allocator.Persistent);
            lutColors = cg.BiomeColors;
            bakeLookupBuilt = true;
        }

        try
        {
            const int colorBatchSize = 96;
            int total = _allChunks.Count;
            for (int batchStart = 0; batchStart < total; batchStart += colorBatchSize)
            {
                int batchEnd = Mathf.Min(batchStart + colorBatchSize, total);

                await Awaitable.BackgroundThreadAsync();
                ct.ThrowIfCancellationRequested();
                BiomeLookupData lookupCopy = lookup; // captured by closure
                Color[] lutCopy = lutColors;
                bool bakeEnabled = bakeLookupBuilt;
                System.Threading.Tasks.Parallel.For(batchStart, batchEnd, i =>
                {
                    var chunk = _allChunks[i];
                    CalculateChunkColors(chunk, biomeProvider);
                    if (bakeEnabled) BakeChunkBiomeMap(chunk, lookupCopy, lutCopy);
                });
                ct.ThrowIfCancellationRequested();

                await Awaitable.MainThreadAsync();
                for (int i = batchStart; i < batchEnd; i++)
                {
                    var chunk = _allChunks[i];
                    ApplyChunkColors(chunk);
                    if (bakeEnabled) UploadChunkBiomeMap(chunk, releasePendingPixels: !chunk.IsLeaf);
                }

                float pct = (float)batchEnd / total;
                progress?.Report(pct, $"Applied biome colors {batchEnd}/{total}");
                await Awaitable.NextFrameAsync(ct);
            }

            if (bakeLookupBuilt)
            {
                BuildFaceBiomeAtlases();
                RebindAllChunkBiomeProperties();
                if (_faceMaterial != null) _faceMaterial.EnableKeyword(BiomeTextureModeKeyword);
            }
        }
        finally
        {
            if (bakeLookupBuilt)
            {
                LogBiomeMapBakeSummary(biomeProvider);
                ReleasePendingBiomeMapPixels();
                lookup.Dispose();
            }
        }
    }

    // One-shot diagnostic: scan all chunks, pick the leaf with the most distinct biomes (more
    // informative than the polar/corner default), and report its texel histogram. Also reports
    // any chunks where the biome map is uniformly zero (which would render as Ocean).
    void LogBiomeMapBakeSummary(IBiomeProvider biomeProvider)
    {
        var registry = (biomeProvider as ColorGenerator)?.Registry;

        PlanetChunk best = null;
        int bestUnique = 0;
        int allZeroCount = 0;
        int totalLeafCount = 0;

        // BiomeIdsTexture has 4 ids per texel in RGBA channels — top-K by weight, R is the
        // dominant biome. GetPixels32 round-trips through the GPU readback (textures stay
        // CPU-readable: makeNoLongerReadable was false on Apply).
        for (int i = 0; i < _allChunks.Count; i++)
        {
            var c = _allChunks[i];
            if (c == null || !c.IsLeaf || c.BiomeIdsTexture == null) continue;
            totalLeafCount++;
            var pixels = c.BiomeIdsTexture.GetPixels32();
            var seen = new System.Collections.Generic.HashSet<byte>();
            for (int j = 0; j < pixels.Length; j++) seen.Add(pixels[j].r);
            if (seen.Count == 1 && pixels[0].r == 0) allZeroCount++;
            if (seen.Count > bestUnique) { best = c; bestUnique = seen.Count; }
        }

        if (best == null)
        {
            LoggerProvider.Log(LogLevel.Warning, "PhaseB", "Bake: no leaf chunks have populated BiomeIdsTexture.");
            return;
        }

        var counts = new int[256];
        var bestPixels = best.BiomeIdsTexture.GetPixels32();
        for (int i = 0; i < bestPixels.Length; i++) counts[bestPixels[i].r]++;

        var sb = new System.Text.StringBuilder();
        sb.Append($"Bake: {_allChunks.Count} chunks ({allZeroCount}/{totalLeafCount} leaves are uniformly Ocean). Most-diverse chunk F{best.FaceIndex} D{best.DetailLevel} H{best.HashValue} dominant-id distribution: ");
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

    // Step 5b: bake top-K biome textures for one chunk on a worker thread. Allocates the
    // pending Color32 buffers lazily (per chunk, GC'd after upload) and reuses a thread-local
    // scratch buffer for the high-res biome id grid (no per-chunk GC pressure for that).
    static void BakeChunkBiomeMap(PlanetChunk chunk, in BiomeLookupData lookup, Color[] lutColors)
    {
        if (chunk?.BiomeBlendedColorTexture == null) return;
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

        BiomeMapBaker.Bake(chunk, lookup, lutColors,
            chunk.PendingBiomeBlendedColorPixels,
            chunk.PendingBiomeIdsPixels,
            chunk.PendingBiomeWeightsPixels,
            _tlsBakeHighResBuffer);
    }

    // Step 5b: upload the 3 baked Color32 buffers to their GPU textures. Leaf pending arrays
    // stay alive until the face-space biome atlases are stitched, then ReleasePendingBiomeMapPixels
    // drops them all at once.
    static void UploadChunkBiomeMap(PlanetChunk chunk, bool releasePendingPixels)
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

    void BuildFaceBiomeAtlases()
    {
        DisposeFaceBiomeAtlases();

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

        _faceBiomeBlendedAtlases = new Texture2D[6];
        _faceBiomeIdAtlases = new Texture2D[6];
        _faceBiomeWeightAtlases = new Texture2D[6];

        int expectedLeafCount = leafsPerAxis * leafsPerAxis;
        for (int face = 0; face < 6; face++)
        {
            var blendedPixels = new Color32[atlasResolution * atlasResolution];
            var idPixels = new Color32[blendedPixels.Length];
            var weightPixels = new Color32[blendedPixels.Length];
            int copiedLeaves = 0;

            for (int i = 0; i < _allChunks.Count; i++)
            {
                PlanetChunk chunk = _allChunks[i];
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

            _faceBiomeBlendedAtlases[face] = CreateBiomeAtlasTexture(
                $"BiomeBlendedAtlas_F{face}", atlasResolution, blendedPixels, FilterMode.Bilinear, linear: false);
            _faceBiomeIdAtlases[face] = CreateBiomeAtlasTexture(
                $"BiomeIdsAtlas_F{face}", atlasResolution, idPixels, FilterMode.Point, linear: true);
            _faceBiomeWeightAtlases[face] = CreateBiomeAtlasTexture(
                $"BiomeWeightsAtlas_F{face}", atlasResolution, weightPixels, FilterMode.Point, linear: true);

            LoggerProvider.Log(LogLevel.Debug, "PhaseB",
                $"Biome atlas face {face}: {atlasResolution}x{atlasResolution}, copied {copiedLeaves}/{expectedLeafCount} max-depth leaves.");
        }
    }

    static Texture2D CreateBiomeAtlasTexture(string name, int resolution, Color32[] pixels, FilterMode filterMode, bool linear)
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

        float minU = chunk.UvCenter.x - chunk.UvHalfExtent;
        float minV = chunk.UvCenter.y - chunk.UvHalfExtent;
        int leafX = Mathf.Clamp(Mathf.RoundToInt(minU * leafsPerAxis), 0, leafsPerAxis - 1);
        int leafY = Mathf.Clamp(Mathf.RoundToInt(minV * leafsPerAxis), 0, leafsPerAxis - 1);
        int dstX0 = leafX * leafStride;
        int dstY0 = leafY * leafStride;

        for (int y = 0; y < mapResolution; y++)
        {
            int srcRow = y * mapResolution;
            int dstRow = (dstY0 + y) * atlasResolution + dstX0;
            System.Array.Copy(source, srcRow, atlas, dstRow, mapResolution);
        }
    }

    void BuildGrassSurfaceAtlases()
    {
        DisposeGrassSurfaceAtlases();

        int leafsPerAxis = 1 << Mathf.Max(_maxChunkDepth, 0);
        int leafStride = PlanetChunkTextures.BiomeMapResolution - 1;
        int atlasResolution = leafsPerAxis * leafStride + 1;
        if (atlasResolution <= 1 || atlasResolution > SystemInfo.maxTextureSize)
        {
            LoggerProvider.Log(LogLevel.Warning, "PhaseC",
                $"Grass surface atlas skipped: requested {atlasResolution}x{atlasResolution}, max texture size is {SystemInfo.maxTextureSize}.");
            return;
        }

        var radiusPixelsByFace = new float[6][];
        var radiusTextures = new Texture2D[6];
        var normalTextures = new Texture2D[6];
        int expectedLeafCount = leafsPerAxis * leafsPerAxis;

        for (int face = 0; face < 6; face++)
        {
            var radiusPixels = new float[atlasResolution * atlasResolution];
            int copiedLeaves = 0;

            for (int i = 0; i < _allChunks.Count; i++)
            {
                PlanetChunk chunk = _allChunks[i];
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

        _grassSurfaceAtlases = new GrassSurfaceAtlasGpuData(radiusTextures, normalTextures, atlasResolution);
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

    void ReleasePendingBiomeMapPixels()
    {
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk == null) continue;
            chunk.PendingBiomeBlendedColorPixels = null;
            chunk.PendingBiomeIdsPixels = null;
            chunk.PendingBiomeWeightsPixels = null;
        }
    }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers()
        => _rootSamplers ?? (IReadOnlyList<IFaceMeshSampler>)System.Array.Empty<IFaceMeshSampler>();

    // Phase B step 9: Phase E entry point. Walks the chunk tree on the face containing the
    // direction, finds the leaf, re-runs the bake + GPU upload for that single chunk.
    // Returns the count rebaked (0 if pre-init, no registry available, etc.). Phase E callers
    // will typically expand this to a radius — for now single-chunk is sufficient plumbing.
    public int RebakeBiomeMapsAt(Vector3 localUnitDirection)
    {
        if (_cachedBiomeProvider is not ColorGenerator cg || cg.Registry == null || cg.BiomeColors == null)
            return 0;
        if (_quadtrees == null || _quadtrees.Length != 6) return 0;
        if (localUnitDirection.sqrMagnitude < 0.0001f) return 0;

        var (face, faceUv) = CoordinateConverter.UnitSphereToCubeFace(localUnitDirection.normalized);
        if (face < 0 || face >= _quadtrees.Length || _quadtrees[face] == null) return 0;

        PlanetChunk leaf = _quadtrees[face].FindLeafContaining(faceUv);
        if (leaf == null) return 0;

        BiomeLookupData lookup = cg.Registry.BuildLookupData(Allocator.TempJob);
        try
        {
            BakeChunkBiomeMap(leaf, lookup, cg.BiomeColors);
            UploadChunkBiomeMap(leaf, releasePendingPixels: true);
            // Re-bind in case the renderer was created before the bake finished (paranoid —
            // BiomeBlendedColorTexture itself is the same Texture2D, just with new content).
            if (_chunkRenderers.TryGetValue(leaf, out var handle))
                BindChunkBiomeProperties(handle, leaf);
        }
        finally
        {
            lookup.Dispose();
        }
        return 1;
    }

    const string BiomeTextureModeKeyword = "_BIOME_COLOR_MODE_TEXTURE";

    public void Dispose()
    {
        DisposeAllPendingJobs();
        if (_filters.IsCreated) _filters.Dispose();
        DisposeFaceBiomeAtlases();
        DisposeGrassSurfaceAtlases();
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);
        foreach (var pair in _chunkRenderers)
        {
            var handle = pair.Value;
            if (handle == null) continue;
            if (handle.Mesh != null) Object.Destroy(handle.Mesh);
        }
        _chunkRenderers.Clear();

        // Phase B step 3: dispose per-chunk biome + surface-state textures so we don't leak
        // GPU memory across planet regen.
        int chunkTexturesBefore = PlanetChunkTextures.LiveTextureSets;
        int chunkCount = _allChunks.Count;
        for (int i = 0; i < _allChunks.Count; i++)
            PlanetChunkTextures.Dispose(_allChunks[i]);
        _allChunks.Clear();
        LoggerProvider.Log(LogLevel.Debug, "ChunkLOD",
            $"Dispose: disposed {chunkCount} chunk texture sets. LiveTextureSets {chunkTexturesBefore} -> {PlanetChunkTextures.LiveTextureSets}");
    }

    // ---- Initial gen helpers ----------------------------------------------------------------

    static void CollectAllChunks(PlanetChunk chunk, List<PlanetChunk> output)
    {
        if (chunk == null) return;
        output.Add(chunk);
        if (chunk.Children != null)
            for (int i = 0; i < chunk.Children.Length; i++)
                CollectAllChunks(chunk.Children[i], output);
    }

    static void CollectChunksAtDepth(PlanetChunk chunk, int targetDepth, List<PlanetChunk> output)
    {
        if (chunk == null) return;
        if (chunk.DetailLevel == targetDepth) { output.Add(chunk); return; }
        if (chunk.DetailLevel > targetDepth) return;
        if (chunk.Children != null)
            for (int i = 0; i < chunk.Children.Length; i++)
                CollectChunksAtDepth(chunk.Children[i], targetDepth, output);
    }

    void EnsureFaceObjects()
    {
        if (_faceRoots != null) return;

        _faceRoots = new Transform[6];
        _quadtrees = new TerrainQuadtree[6];
        _faceVisible = new bool[6];

        for (int f = 0; f < 6; f++)
        {
            var go = new GameObject($"chunk-face-{f}");
            go.transform.parent = _planetTransform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _faceRoots[f] = go.transform;
            _quadtrees[f] = new TerrainQuadtree(f);

            _faceVisible[f] = _renderMask == Planet.FaceRenderMask.All || (int)_renderMask - 1 == f;
            go.SetActive(_faceVisible[f]);
        }
    }

    async Awaitable UploadChunkRenderersAsync(IReadOnlyList<PlanetChunk> chunks, IProgressHandle progress, CancellationToken ct)
    {
        if (chunks == null || chunks.Count == 0) return;

        int uploaded = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            EnsureChunkRenderer(chunks[i]);
            uploaded++;

            if (uploaded % ChunkMeshUploadBatchSize == 0)
            {
                float pct = (float)uploaded / chunks.Count;
                progress?.Report(0.85f + 0.09f * pct, $"Uploading chunk meshes {uploaded}/{chunks.Count}");
                await Awaitable.NextFrameAsync(ct);
            }
        }

        progress?.Report(0.94f, $"Uploaded chunk meshes {uploaded}/{chunks.Count}");
    }

    ChunkRenderHandle EnsureChunkRenderer(PlanetChunk chunk)
    {
        if (chunk == null) return null;
        if (_chunkRenderers.TryGetValue(chunk, out var existing)) return existing;
        if (chunk.CpuVertices == null || chunk.CpuVertices.Length == 0) return null;
        if (_faceRoots == null || chunk.FaceIndex < 0 || chunk.FaceIndex >= _faceRoots.Length) return null;

        var go = new GameObject($"chunk-f{chunk.FaceIndex}-d{chunk.DetailLevel}-{chunk.HashValue:X}");
        go.transform.parent = _faceRoots[chunk.FaceIndex];
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _faceMaterial;

        var filter = go.AddComponent<MeshFilter>();
        var mesh = new Mesh { name = go.name };
        mesh.SetVertices(chunk.CpuVertices);
        // Prefer the Burst-computed terrain-aware normals; fall back to unit-sphere directions
        // if the normals job hasn't populated them (e.g., legacy paths that didn't chain it).
        if (chunk.CpuNormals != null && chunk.CpuNormals.Length == chunk.CpuVertices.Length)
            mesh.SetNormals(chunk.CpuNormals);
        else if (chunk.CpuUnitSpherePoints != null && chunk.CpuUnitSpherePoints.Length == chunk.CpuVertices.Length)
            mesh.SetNormals(chunk.CpuUnitSpherePoints);
        mesh.SetUVs(0, ChunkUvTemplate.Get(ChunkResolution));
        mesh.SetTriangles(ChunkTriangleTemplate.Get(ChunkResolution), 0, false);
        mesh.bounds = chunk.CpuLocalBounds;
        filter.sharedMesh = mesh;

        go.SetActive(false);

        var handle = new ChunkRenderHandle
        {
            GameObject = go,
            Renderer = renderer,
            Filter = filter,
            Mesh = mesh,
        };
        _chunkRenderers.Add(chunk, handle);

        // Phase B step 5: bind the per-chunk biome map as a MaterialPropertyBlock so the
        // shared planet material can sample it without a per-chunk material instance. The
        // map texture itself was allocated in step 3 and populated by the step 4 bake.
        BindChunkBiomeProperties(handle, chunk);
        return handle;
    }

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
    MaterialPropertyBlock _chunkPropertyBlock;

    void BindChunkBiomeProperties(ChunkRenderHandle handle, PlanetChunk chunk)
    {
        if (handle?.Renderer == null || chunk == null) return;
        bool usingAtlas = TryGetFaceBiomeAtlases(chunk.FaceIndex,
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
        // to Phase E — for now the binding just exercises the upload path.
        if (chunk.SurfaceStateTexture != null)
            _chunkPropertyBlock.SetTexture(SurfaceStateMaskShaderId, chunk.SurfaceStateTexture);
        // _TexelSize is (1/w, 1/h, w, h) — matches Unity's built-in texture layout so the
        // shader can do neighbor-texel lookups without external math.
        int res = Mathf.Max(blendedTexture.width, 1);
        _chunkPropertyBlock.SetVector(BiomeMapTexelSizeShaderId,
            new Vector4(1f / res, 1f / res, res, res));
        // Chunk-local UV maps either into the face atlas or into the chunk-local fallback map.
        _chunkPropertyBlock.SetVector(BiomeMapUvScaleShaderId,
            usingAtlas ? GetFaceAtlasUvScale(chunk) : new Vector4(1f, 1f, 0f, 0f));
        handle.Renderer.SetPropertyBlock(_chunkPropertyBlock);
    }

    public bool TryGetFaceBiomeAtlases(int face, out Texture2D blended, out Texture2D ids, out Texture2D weights)
    {
        blended = null;
        ids = null;
        weights = null;
        if (face < 0 || face >= 6) return false;
        if (_faceBiomeBlendedAtlases == null || _faceBiomeIdAtlases == null || _faceBiomeWeightAtlases == null)
            return false;

        blended = _faceBiomeBlendedAtlases[face];
        ids = _faceBiomeIdAtlases[face];
        weights = _faceBiomeWeightAtlases[face];
        return blended != null && ids != null && weights != null;
    }

    static Vector4 GetFaceAtlasUvScale(PlanetChunk chunk)
    {
        float size = chunk.UvHalfExtent * 2f;
        return new Vector4(size, size,
            chunk.UvCenter.x - chunk.UvHalfExtent,
            chunk.UvCenter.y - chunk.UvHalfExtent);
    }

    void RebindAllChunkBiomeProperties()
    {
        foreach (var pair in _chunkRenderers)
            BindChunkBiomeProperties(pair.Value, pair.Key);
    }

    void DisposeFaceBiomeAtlases()
    {
        DestroyTextureArray(ref _faceBiomeBlendedAtlases);
        DestroyTextureArray(ref _faceBiomeIdAtlases);
        DestroyTextureArray(ref _faceBiomeWeightAtlases);
    }

    void DisposeGrassSurfaceAtlases()
    {
        _grassSurfaceAtlases?.Dispose();
        _grassSurfaceAtlases = null;
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

    void SetChunkVisible(PlanetChunk chunk, bool visible)
    {
        var handle = EnsureChunkRenderer(chunk);
        if (handle == null || handle.GameObject == null) return;

        bool active = visible && _faceVisible != null && _faceVisible[chunk.FaceIndex];
        if (handle.Visible == active) return;

        handle.Visible = active;
        handle.GameObject.SetActive(active);
        if (active)
            ChunkShown?.Invoke(chunk);
        else
            ChunkHidden?.Invoke(chunk);
    }

    bool IsChunkActuallyVisible(PlanetChunk chunk)
    {
        return chunk != null
            && _chunkRenderers.TryGetValue(chunk, out var handle)
            && handle != null
            && handle.Visible;
    }

    static bool RaycastChunkTriangles(Ray ray, PlanetChunk chunk, int[] triangles, float maxDistance,
        out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitDistance = 0f;
        hitPoint = default;
        hitNormal = default;

        Vector3[] vertices = chunk?.CpuVertices;
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        Vector3[] normals = chunk.CpuNormals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        float bestDistance = maxDistance;
        float bestU = 0f;
        float bestV = 0f;
        int bestA = -1;
        int bestB = -1;
        int bestC = -1;

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int ia = triangles[i];
            int ib = triangles[i + 1];
            int ic = triangles[i + 2];
            if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length)
                continue;

            if (!RaycastTriangle(ray, vertices[ia], vertices[ib], vertices[ic], bestDistance,
                    out float distance, out float u, out float v))
                continue;

            bestDistance = distance;
            bestU = u;
            bestV = v;
            bestA = ia;
            bestB = ib;
            bestC = ic;
        }

        if (bestA < 0)
            return false;

        hitDistance = bestDistance;
        hitPoint = ray.origin + ray.direction * bestDistance;
        float w = 1f - bestU - bestV;
        if (hasNormals)
        {
            hitNormal = (normals[bestA] * w + normals[bestB] * bestU + normals[bestC] * bestV).normalized;
        }
        else
        {
            Vector3 a = vertices[bestA];
            Vector3 b = vertices[bestB];
            Vector3 c = vertices[bestC];
            hitNormal = Vector3.Cross(b - a, c - a).normalized;
        }

        if (hitNormal.sqrMagnitude < 0.0001f && hitPoint.sqrMagnitude > 0.0001f)
            hitNormal = hitPoint.normalized;
        if (Vector3.Dot(hitNormal, hitPoint) < 0f)
            hitNormal = -hitNormal;
        return true;
    }

    static bool RaycastTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, float maxDistance,
        out float distance, out float u, out float v)
    {
        const float epsilon = 1e-6f;
        distance = 0f;
        u = 0f;
        v = 0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 pvec = Vector3.Cross(ray.direction, edge2);
        float det = Vector3.Dot(edge1, pvec);
        if (Mathf.Abs(det) < epsilon)
            return false;

        float invDet = 1f / det;
        Vector3 tvec = ray.origin - a;
        u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
            return false;

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        v = Vector3.Dot(ray.direction, qvec) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, qvec) * invDet;
        return distance > epsilon && distance <= maxDistance;
    }

    static void CalculateChunkColors(PlanetChunk chunk, IBiomeProvider biomeProvider)
    {
        if (chunk == null || biomeProvider == null || chunk.CpuUnitSpherePoints == null || chunk.CpuElevations == null)
            return;

        int count = chunk.CpuUnitSpherePoints.Length;
        if (chunk.CpuElevations.Length != count) return;

        var colors = chunk.CpuColors;
        if (colors == null || colors.Length != count) colors = new Color[count];

        var biomeData = chunk.CpuBiomeData;
        if (biomeData == null || biomeData.Length != count) biomeData = new Vector4[count];

        for (int i = 0; i < count; i++)
            colors[i] = biomeProvider.GetBiomeColorAndData(chunk.CpuUnitSpherePoints[i], chunk.CpuElevations[i], out biomeData[i]);

        chunk.CpuColors = colors;
        chunk.CpuBiomeData = biomeData;
    }

    void ApplyChunkColors(PlanetChunk chunk)
    {
        if (chunk == null || chunk.CpuColors == null) return;
        if (!_chunkRenderers.TryGetValue(chunk, out var handle) || handle?.Mesh == null) return;

        handle.Mesh.SetColors(chunk.CpuColors);
        if (chunk.CpuBiomeData != null)
            handle.Mesh.SetUVs(2, chunk.CpuBiomeData);
        handle.Mesh.UploadMeshData(true);
    }

    void ScheduleChunkJob(PlanetChunk chunk)
    {
        int vertexCount = ChunkTriangleTemplate.VertexCount(ChunkResolution);

        var state = new PlanetChunkMeshJobState
        {
            Resolution = ChunkResolution,
            Vertices = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            UnitSpherePoints = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Elevations = new NativeArray<float>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
            Normals = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
        };

        // Pre-cache builds every chunk with EdgeFanMask = 0 (no vertex snapping). At runtime
        // the visibility filter renders chunks at varying depths, so any given chunk's effective
        // neighbor LOD can change between frames — pre-baking masks would require 16 mesh
        // variants per chunk. For Phase A we accept small cracks at LOD transitions; fix later.
        Vector3 localUp = GetFaceLocalUp(chunk.FaceIndex);
        GetFaceAxes(chunk.FaceIndex, out Vector3 axisA, out Vector3 axisB);

        var meshJob = new PlanetChunkMeshJob
        {
            Resolution = ChunkResolution,
            FaceLocalUp = new float3(localUp.x, localUp.y, localUp.z),
            FaceAxisA = new float3(axisA.x, axisA.y, axisA.z),
            FaceAxisB = new float3(axisB.x, axisB.y, axisB.z),
            UvOrigin = new float2(chunk.UvCenter.x - chunk.UvHalfExtent, chunk.UvCenter.y - chunk.UvHalfExtent),
            UvExtent = chunk.UvHalfExtent * 2f,
            PlanetRadius = _shapeGenerator.Settings.PlanetRadius,
            EdgeFanMask = 0,
            Filters = _filters,
            Vertices = state.Vertices,
            UnitSpherePoints = state.UnitSpherePoints,
            Elevations = state.Elevations,
        };

        JobHandle meshHandle = meshJob.Schedule(vertexCount, 256);

        // Chain the normals pass after the mesh job — it reads Vertices written above.
        var normalsJob = new PlanetChunkNormalsJob
        {
            Resolution = ChunkResolution,
            Vertices = state.Vertices,
            Normals = state.Normals,
        };
        state.Handle = normalsJob.Schedule(vertexCount, 256, meshHandle);

        chunk.State = ChunkLifecycle.Generating;
        chunk.EdgeFanMaskAtSchedule = 0;

        _pendingJobs.Add(new PendingChunkJob
        {
            Chunk = chunk,
            State = state,
        });
        JobHandle.ScheduleBatchedJobs();
    }

    void DrainCompletedJobs()
    {
        // Pre-cache: no chunks are released between schedule and completion, so the per-job
        // stale guard from the dynamic-subdivision path is unnecessary. Just complete each
        // job, copy its output, and free its NativeArrays.
        for (int i = _pendingJobs.Count - 1; i >= 0; i--)
        {
            var pending = _pendingJobs[i];
            if (!pending.State.Handle.IsCompleted) continue;

            pending.State.Handle.Complete();
            _pendingJobs.RemoveAt(i);

            CopyJobOutputToChunk(pending.Chunk, pending.State);
            pending.State.Dispose();
            pending.Chunk.State = ChunkLifecycle.Active;

            var elevs = pending.Chunk.CpuElevations;
            if (elevs != null)
                for (int v = 0; v < elevs.Length; v++)
                    _shapeGenerator.RecordElevationSample(elevs[v]);
        }
    }

    void DisposeAllPendingJobs()
    {
        for (int i = 0; i < _pendingJobs.Count; i++)
        {
            _pendingJobs[i].State.Handle.Complete();
            _pendingJobs[i].State.Dispose();
        }
        _pendingJobs.Clear();
    }

    static void CopyJobOutputToChunk(PlanetChunk chunk, PlanetChunkMeshJobState state)
    {
        int vc = state.Vertices.Length;
        chunk.CpuVertices = new Vector3[vc];
        chunk.CpuUnitSpherePoints = new Vector3[vc];
        chunk.CpuElevations = new float[vc];
        chunk.CpuVertexRadii = new float[vc];
        chunk.CpuNormals = new Vector3[vc];

        var vAsV3 = state.Vertices.Reinterpret<Vector3>(sizeof(float) * 3);
        var sAsV3 = state.UnitSpherePoints.Reinterpret<Vector3>(sizeof(float) * 3);
        var nAsV3 = state.Normals.Reinterpret<Vector3>(sizeof(float) * 3);
        NativeArray<Vector3>.Copy(vAsV3, chunk.CpuVertices, vc);
        NativeArray<Vector3>.Copy(sAsV3, chunk.CpuUnitSpherePoints, vc);
        NativeArray<float>.Copy(state.Elevations, chunk.CpuElevations, vc);
        NativeArray<Vector3>.Copy(nAsV3, chunk.CpuNormals, vc);

        if (vc <= 0) return;

        var bounds = new Bounds(chunk.CpuVertices[0], Vector3.zero);
        for (int i = 0; i < vc; i++)
        {
            chunk.CpuVertexRadii[i] = chunk.CpuVertices[i].magnitude;
            bounds.Encapsulate(chunk.CpuVertices[i]);
        }
        chunk.CpuLocalBounds = bounds;
    }

    // ---- Visibility filter -----------------------------------------------------------------

    void PrepareLodContext(Camera camera)
    {
        _lodCamera = camera != null && camera.isActiveAndEnabled ? camera : null;
        _hasLodCamera = _lodCamera != null;
        if (!_hasLodCamera) return;

        GeometryUtility.CalculateFrustumPlanes(_lodCamera, _lodFrustumPlanes);

        float pixelHeight = _lodCamera.pixelHeight > 0 ? _lodCamera.pixelHeight : Mathf.Max(Screen.height, 1);
        float halfFovRad = Mathf.Max(_lodCamera.fieldOfView, 1f) * Mathf.Deg2Rad * 0.5f;
        _lodFocalLengthPixels = pixelHeight / (2f * Mathf.Tan(halfFovRad));
    }

    bool UpdateVisibleLeavesForFace(int faceIdx, Vector3 observerPos)
    {
        _tmpVisibleLeaves.Clear();
        GatherVisibleLeaves(_quadtrees[faceIdx].Root, observerPos, _tmpVisibleLeaves);

        var current = _visibleLeavesPerFace[faceIdx];
        if (current == null) current = _visibleLeavesPerFace[faceIdx] = new List<PlanetChunk>(64);

        bool changed = current.Count != _tmpVisibleLeaves.Count;
        if (!changed)
            for (int i = 0; i < current.Count; i++)
                if (!ReferenceEquals(current[i], _tmpVisibleLeaves[i])) { changed = true; break; }

        if (changed)
        {
            _tmpVisibleSet.Clear();
            for (int i = 0; i < _tmpVisibleLeaves.Count; i++)
                _tmpVisibleSet.Add(_tmpVisibleLeaves[i]);

            for (int i = 0; i < current.Count; i++)
            {
                var oldLeaf = current[i];
                if (!_tmpVisibleSet.Contains(oldLeaf))
                    SetChunkVisible(oldLeaf, false);
            }

            for (int i = 0; i < _tmpVisibleLeaves.Count; i++)
            {
                var newLeaf = _tmpVisibleLeaves[i];
                if (!ContainsReference(current, newLeaf))
                    SetChunkVisible(newLeaf, true);
            }

            current.Clear();
            current.AddRange(_tmpVisibleLeaves);
        }
        return changed;
    }

    static bool ContainsReference(List<PlanetChunk> list, PlanetChunk chunk)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], chunk))
                return true;
        return false;
    }

    void GatherVisibleLeaves(PlanetChunk chunk, Vector3 observerPos, List<PlanetChunk> output)
    {
        if (chunk == null || chunk.CpuVertices == null) return;
        if (!IsChunkVisibleCandidate(chunk, observerPos)) return;

        bool hasChildren = chunk.Children != null && chunk.Children.Length > 0;
        if (hasChildren && ShouldSubdivide(chunk, observerPos))
        {
            for (int i = 0; i < chunk.Children.Length; i++)
                GatherVisibleLeaves(chunk.Children[i], observerPos, output);
        }
        else
        {
            output.Add(chunk);
        }
    }

    bool ShouldSubdivide(PlanetChunk chunk, Vector3 observerPos)
    {
        if (chunk.DetailLevel >= _maxChunkDepth) return false;
        if (!_hasLodCamera) return false;

        float projectedPixels = ProjectedChunkDiameterPixels(chunk, observerPos);
        float threshold = WasRefinedInPreviousVisibility(chunk)
            ? TargetChunkScreenPixels * LodMergeHysteresis
            : TargetChunkScreenPixels * LodSplitHysteresis;
        return projectedPixels > threshold;
    }

    bool WasRefinedInPreviousVisibility(PlanetChunk chunk)
    {
        var leaves = _visibleLeavesPerFace[chunk.FaceIndex];
        if (leaves == null || leaves.Count == 0) return false;

        for (int i = 0; i < leaves.Count; i++)
            if (!ReferenceEquals(leaves[i], chunk) && IsDescendantOf(leaves[i], chunk))
                return true;
        return false;
    }

    static bool IsDescendantOf(PlanetChunk node, PlanetChunk ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = node.Parent;
        }
        return false;
    }

    float DistanceToChunkCenterSquared(PlanetChunk chunk, Vector3 observerPos)
    {
        Vector3 sphereCenter = CoordinateConverter.CubeFaceToUnitSphere(chunk.FaceIndex, chunk.UvCenter);
        Vector3 worldCenter = _planetTransform.position
            + _planetTransform.TransformDirection(sphereCenter) * _shapeGenerator.Settings.PlanetRadius;
        return (observerPos - worldCenter).sqrMagnitude;
    }

    float ProjectedChunkDiameterPixels(PlanetChunk chunk, Vector3 observerPos)
    {
        Bounds worldBounds = EstimateWorldBounds(chunk);
        float diameter = worldBounds.extents.magnitude * 2f;
        float distance = Mathf.Sqrt(Mathf.Max(DistanceToChunkCenterSquared(chunk, observerPos), 0.0001f));
        float safeDistance = Mathf.Max(Mathf.Max(distance, diameter * 0.25f), 1f);
        return diameter * _lodFocalLengthPixels / safeDistance;
    }

    bool IsChunkVisibleCandidate(PlanetChunk chunk, Vector3 observerPos)
    {
        if (!_hasLodCamera) return true;

        Bounds worldBounds = EstimateWorldBounds(chunk);
        if (!GeometryUtility.TestPlanesAABB(_lodFrustumPlanes, worldBounds))
            return false;

        return IsChunkAboveHorizon(chunk, observerPos, worldBounds);
    }

    Bounds EstimateWorldBounds(PlanetChunk chunk)
    {
        Bounds local = chunk.CpuLocalBounds;
        Vector3 center = _planetTransform.TransformPoint(local.center);
        Vector3 lossyScale = _planetTransform.lossyScale;
        float scale = Mathf.Max(Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y)), Mathf.Abs(lossyScale.z));
        float radius = Mathf.Max(local.extents.magnitude * scale * FrustumBoundsPadding, 1f);
        return new Bounds(center, Vector3.one * (radius * 2f));
    }

    bool IsChunkAboveHorizon(PlanetChunk chunk, Vector3 observerPos, Bounds worldBounds)
    {
        Vector3 planetCenter = _planetTransform.position;
        Vector3 observer = observerPos - planetCenter;
        float observerDistance = observer.magnitude;
        float planetRadius = _shapeGenerator.Settings.PlanetRadius * Mathf.Max(HorizonRadiusScale, 0.01f);
        if (observerDistance <= planetRadius) return true;

        Vector3 chunkCenter = worldBounds.center - planetCenter;
        if (chunkCenter.sqrMagnitude < 0.0001f) return true;

        float dot = Vector3.Dot(chunkCenter.normalized, observer / observerDistance);
        float horizonThreshold = Mathf.Clamp(planetRadius / observerDistance, -1f, 1f);
        float angularMargin = Mathf.Clamp01(worldBounds.extents.magnitude / observerDistance) * HorizonMarginScale;
        return dot >= horizonThreshold - angularMargin;
    }

    void LogChunkDiagnostics(string reason)
    {
        if (!EnableChunkDiagnosticsLog) return;

        float now = Time.realtimeSinceStartup;
        if (reason == "tick" && now < _nextDiagnosticsLogTime) return;
        _nextDiagnosticsLogTime = now + DiagnosticsLogIntervalSeconds;

        int totalLeaves = 0;
        int totalVertices = 0;
        int totalTriangles = 0;
        int[] depthCounts = new int[Mathf.Max(_maxChunkDepth + 1, 1)];

        for (int f = 0; f < 6; f++)
        {
            var leaves = _visibleLeavesPerFace[f];
            if (leaves == null) continue;
            for (int i = 0; i < leaves.Count; i++)
            {
                var leaf = leaves[i];
                if (leaf == null || leaf.CpuVertices == null) continue;
                totalLeaves++;
                totalVertices += leaf.CpuVertices.Length;
                totalTriangles += ChunkTriangleTemplate.TriangleCount(ChunkResolution) / 3;
                if (leaf.DetailLevel >= 0 && leaf.DetailLevel < depthCounts.Length)
                    depthCounts[leaf.DetailLevel]++;
            }
        }

        LoggerProvider.Log(LogLevel.Debug, "ChunkLOD",
            $"{reason}: visibleLeaves={totalLeaves} verts={totalVertices:n0} tris={totalTriangles:n0} depthCounts={FormatDepthCounts(depthCounts)} camera={(_hasLodCamera ? _lodCamera.name : "none")}");
    }

    static string FormatDepthCounts(int[] depthCounts)
    {
        if (depthCounts == null || depthCounts.Length == 0) return "[]";

        string result = "[";
        bool first = true;
        for (int i = 0; i < depthCounts.Length; i++)
        {
            if (depthCounts[i] == 0) continue;
            if (!first) result += ",";
            result += i.ToString() + ":" + depthCounts[i].ToString();
            first = false;
        }
        return result + "]";
    }

    // ---- Face frame helpers (match CubeFaceToUnitSphere convention) ------------------------

    static Vector3 GetFaceLocalUp(int faceIndex) => faceIndex switch
    {
        0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.left,
        3 => Vector3.right, 4 => Vector3.forward, 5 => Vector3.back,
        _ => Vector3.up,
    };

    static void GetFaceAxes(int faceIndex, out Vector3 axisA, out Vector3 axisB)
    {
        Vector3 up = GetFaceLocalUp(faceIndex);
        axisA = new Vector3(up.y, up.z, up.x);
        axisB = Vector3.Cross(up, axisA);
    }

    struct PendingChunkJob
    {
        public PlanetChunk Chunk;
        public PlanetChunkMeshJobState State;
    }

    sealed class ChunkRenderHandle
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public MeshFilter Filter;
        public Mesh Mesh;
        public bool Visible;
    }

#if false
    // ---- Boundary normal smoothing ---------------------------------------------------------
    //
    // Within-face: chunks A and B that share an edge have separate vertex indices in the
    //   combined mesh, even though their edge vertices land at identical world positions.
    //   Mesh.RecalculateNormals computes per-vertex normals from incident triangles only,
    //   so each side's edge normal looks "into its own chunk" and misses the other.
    //
    // Cross-face: A's edge vertex on face F and B's edge vertex on face F' are at the same
    //   world position but live in different combined meshes. Same problem at the cube edge.
    //
    // Fix: for each rebuilt face, pair each chunk-edge vertex with its position-matched
    // counterpart on the matching neighbor and average their normals. This face's normals are
    // written; the neighbor's stay untouched (its own rebuild will converge symmetrically).
    //
    // In pre-cache mode this runs once per face at initial gen, plus once per face whenever
    // visibility changes mid-game (cheap since all CPU data is already populated).

    void SmoothFaceNormals(int faceIdx)
    {
        var faceMesh = _combinedMeshes[faceIdx].Mesh;
        var faceOffsets = _combinedMeshes[faceIdx].ChunkVertexOffsets;
        if (faceOffsets.Count == 0) return;

        _smoothFaceNormalsScratch.Clear();
        faceMesh.GetNormals(_smoothFaceNormalsScratch);
        bool anyModified = false;

        for (int i = 0; i < 6; i++) _smoothNeighborNormalsValid[i] = false;
        var neighborPosToNormal = _smoothPosToNormalScratch;

        foreach (var pair in faceOffsets)
        {
            var chunk = pair.Key;
            int chunkOffset = pair.Value;

            for (int e = 0; e < 4; e++)
            {
                CubeEdge edge = (CubeEdge)e;

                PlanetChunk neighborChunk;
                int neighborFaceIdx;
                if (TerrainQuadtree.IsFaceBoundaryEdge(chunk, edge))
                {
                    Vector2 edgeMidUv = EdgeMidpointUv(chunk, edge);
                    if (!CubeFaceTopology.TryMirrorUv(edgeMidUv, chunk.FaceIndex, edge, out neighborFaceIdx, out Vector2 mirroredUv))
                        continue;
                    mirroredUv = NudgeInsideUnitSquare(mirroredUv);
                    neighborChunk = _quadtrees[neighborFaceIdx].FindLeafContaining(mirroredUv);
                }
                else
                {
                    neighborFaceIdx = chunk.FaceIndex;
                    float eps = chunk.UvHalfExtent * 0.5f; if (eps < 1e-5f) eps = 1e-5f;
                    Vector2 queryUv = edge switch
                    {
                        CubeEdge.East  => new Vector2(chunk.UvCenter.x + chunk.UvHalfExtent + eps, chunk.UvCenter.y),
                        CubeEdge.West  => new Vector2(chunk.UvCenter.x - chunk.UvHalfExtent - eps, chunk.UvCenter.y),
                        CubeEdge.North => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y - chunk.UvHalfExtent - eps),
                        CubeEdge.South => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y + chunk.UvHalfExtent + eps),
                        _ => chunk.UvCenter,
                    };
                    neighborChunk = _quadtrees[neighborFaceIdx].FindLeafContaining(queryUv);
                }

                if (neighborChunk == null || neighborChunk.CpuVertices == null) continue;
                // FindLeafContaining returns the deepest chunk at that UV — for pre-cache that
                // may be deeper than our visibility filter would render. Walk up to the chunk
                // actually in the neighbor face's visible set so we read the right normals.
                while (neighborChunk != null
                    && !_combinedMeshes[neighborFaceIdx].ChunkVertexOffsets.ContainsKey(neighborChunk))
                    neighborChunk = neighborChunk.Parent;
                if (neighborChunk == null) continue;
                int neighborChunkOffset = _combinedMeshes[neighborFaceIdx].ChunkVertexOffsets[neighborChunk];

                if (!_smoothNeighborNormalsValid[neighborFaceIdx])
                {
                    var buf = _smoothNeighborNormalsBuffers[neighborFaceIdx];
                    if (buf == null) buf = _smoothNeighborNormalsBuffers[neighborFaceIdx] = new List<Vector3>();
                    buf.Clear();
                    _combinedMeshes[neighborFaceIdx].Mesh.GetNormals(buf);
                    _smoothNeighborNormalsValid[neighborFaceIdx] = true;
                }
                var neighborNormals = _smoothNeighborNormalsBuffers[neighborFaceIdx];

                CubeEdge neighborEdge = edge;
                if (neighborFaceIdx != chunk.FaceIndex)
                    neighborEdge = CubeFaceTopology.GetNeighbor(chunk.FaceIndex, edge).NeighborEdge;

                int[] neighborEdgeIndices = EdgeVertexIndices[(int)neighborEdge];
                int[] thisEdgeIndices = EdgeVertexIndices[(int)edge];

                neighborPosToNormal.Clear();
                for (int k = 0; k < neighborEdgeIndices.Length; k++)
                {
                    int vi = neighborEdgeIndices[k];
                    if (vi < 0 || vi >= neighborChunk.CpuVertices.Length) continue;
                    long key = PackPosition(neighborChunk.CpuVertices[vi]);
                    neighborPosToNormal[key] = neighborNormals[neighborChunkOffset + vi];
                }

                for (int k = 0; k < thisEdgeIndices.Length; k++)
                {
                    int vi = thisEdgeIndices[k];
                    if (vi < 0 || vi >= chunk.CpuVertices.Length) continue;
                    long key = PackPosition(chunk.CpuVertices[vi]);
                    if (!neighborPosToNormal.TryGetValue(key, out Vector3 neighborNrm)) continue;
                    int meshIdx = chunkOffset + vi;
                    Vector3 averaged = (_smoothFaceNormalsScratch[meshIdx] + neighborNrm).normalized;
                    _smoothFaceNormalsScratch[meshIdx] = averaged;
                    anyModified = true;
                }
            }
        }

        if (anyModified) faceMesh.SetNormals(_smoothFaceNormalsScratch);
    }

    static Vector2 EdgeMidpointUv(PlanetChunk chunk, CubeEdge edge) => edge switch
    {
        CubeEdge.East  => new Vector2(chunk.UvCenter.x + chunk.UvHalfExtent, chunk.UvCenter.y),
        CubeEdge.West  => new Vector2(chunk.UvCenter.x - chunk.UvHalfExtent, chunk.UvCenter.y),
        CubeEdge.North => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y - chunk.UvHalfExtent),
        CubeEdge.South => new Vector2(chunk.UvCenter.x, chunk.UvCenter.y + chunk.UvHalfExtent),
        _ => chunk.UvCenter,
    };

    static Vector2 NudgeInsideUnitSquare(Vector2 uv)
    {
        const float eps = 1e-4f;
        return new Vector2(
            Mathf.Clamp(uv.x, eps, 1f - eps),
            Mathf.Clamp(uv.y, eps, 1f - eps));
    }

    static long PackPosition(Vector3 p)
    {
        int xi = Mathf.RoundToInt(p.x * SpatialHashScale.x);
        int yi = Mathf.RoundToInt(p.y * SpatialHashScale.y);
        int zi = Mathf.RoundToInt(p.z * SpatialHashScale.z);
        long h = (long)(uint)xi;
        h = h * 1000003L + (uint)yi;
        h = h * 1000003L + (uint)zi;
        return h;
    }
#endif
}
