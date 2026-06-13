using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

public interface IChunkVisibilitySource
{
    IReadOnlyList<PlanetChunk> GetVisibleChunksSnapshot();
    event System.Action<PlanetChunk> ChunkShown;
    event System.Action<PlanetChunk> ChunkHidden;
}

// Phase A "High" resolution provider â€” pre-cache + visibility filter.
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
    // detail at the cost of ~2.2Ã— memory and mesh-gen time. True per-pixel biome boundaries
    // need shader-based biome sampling, which is Phase B work â€” this is the best we can do at
    // the vertex-color level.
    const int ChunkResolution = 97;
    // LOD target: a chunk refines when its conservative projected diameter exceeds this.
    // This is a screen-space contract, not a terrain-value tuning knob.
    const float TargetChunkScreenPixels = 220f;
    const float LodSplitHysteresis = 1.10f;
    const float LodMergeHysteresis = 0.85f;
    const float FrustumBoundsPadding = 1.08f;
    const float HorizonRadiusScale = 0.98f;
    const float HorizonMarginScale = 2f;
    const float DiagnosticsLogIntervalSeconds = 1f;
    // Depth at which we aggregate chunk vertex data for the WaterMeshBuilder face sampler.
    // 2 â†’ 385Â² per face (with R=97), 16Ã— finer shorelines than the root chunk. Bounded by
    // MaxChunkDepth at construction time. Memory: ~14 MB total across 6 faces at depth 2.
    const int WaterAggregateDepth = 2;
    // Flip to true (or define the symbol below) to log per-tick chunk visibility breakdowns.
    // Default off â€” was spamming the console once visibility changes were happening every frame
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
    readonly IBiomeAtlasService _biomeAtlas;
    readonly ChunkSurfaceGenerator _generator;
    readonly IChunkMeshCache _meshCache;
    GrassSurfaceAtlasGpuData _grassSurfaceAtlases;

    // Per-face visible-leaf snapshot â€” compared each Tick to detect changes.
    readonly List<PlanetChunk>[] _visibleLeavesPerFace = new List<PlanetChunk>[6];
    readonly List<PlanetChunk> _tmpVisibleLeaves = new();
    readonly HashSet<PlanetChunk> _tmpVisibleSet = new();
    readonly List<PlanetChunk> _allChunks = new();

    readonly Plane[] _lodFrustumPlanes = new Plane[6];
    readonly Plane[] _grassResidencyFrustumPlanes = new Plane[6];
    Camera _lodCamera;
    bool _hasLodCamera;
    float _lodFocalLengthPixels = 935f;
    float _nextDiagnosticsLogTime;

    bool _initialized;

    static readonly string ProfTick = "ChunkedSurfaceProvider.Tick";
    static readonly string ProfVisibility = "ChunkedSurfaceProvider.UpdateVisibility";

    // The mesh cache owns the visible-set toggling, so it raises these; forward subscriptions
    // straight through so external observers (grass) bind once to the provider.
    public event System.Action<PlanetChunk> ChunkShown
    {
        add => _meshCache.ChunkShown += value;
        remove => _meshCache.ChunkShown -= value;
    }
    public event System.Action<PlanetChunk> ChunkHidden
    {
        add => _meshCache.ChunkHidden += value;
        remove => _meshCache.ChunkHidden -= value;
    }
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
        _biomeAtlas = new BiomeAtlasService(_maxChunkDepth);
        _generator = new ChunkSurfaceGenerator(_shapeGenerator, ChunkResolution);
        _meshCache = new ChunkMeshCache(_faceMaterial, _biomeAtlas, ChunkResolution);
    }

    public async Awaitable GenerateAsync(IProgressHandle progress, CancellationToken ct)
    {
        EnsureFaceObjects();

        progress?.Report(0f, "Building chunk quadtrees...");

        // Keep texture-mode terrain disabled until GenerateColorsAsync has baked and bound
        // real biome maps. Otherwise newly visible chunks sample blank startup textures.
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);

        // 1) Build the full quadtree to max depth on every face.
        for (int f = 0; f < 6; f++)
            _quadtrees[f].BuildToFixedDepth(_maxChunkDepth);

        // 2) Gather every chunk (internal + leaf) â€” all are rendering candidates depending
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

        // 3) Schedule + drain mesh jobs in bounded batches (owned by the generator).
        await _generator.GenerateMeshesAsync(allChunks, progress, 0.05f, 0.80f, ct);

        // 4) Meshes are no longer pre-built for every node. Render handles are pooled by the
        // mesh cache and built on demand as chunks become visible; the retained compact CPU
        // source rebuilds them on page-in. Visibility (step 7) drives the initial uploads.

        // 5) Build face-sampler views for the water builder. Aggregating depth-N chunks into
        //    a single per-face grid raises water-mesh resolution from ChunkResolution (root)
        //    to 2^N Ã— (ChunkResolution-1) + 1 â€” at depth 2 with R=97 that's 385Â² per face,
        //    16Ã— finer shorelines than the root sampler.
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
        ReportRetainedChunkCpuMemory();
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
                if (chunk != null && _meshCache.IsActuallyVisible(chunk))
                    snapshot.Add(chunk);
            }
        }

        return snapshot;
    }

    public bool IsChunkTerrainVisible(PlanetChunk chunk)
    {
        return _meshCache.IsActuallyVisible(chunk);
    }

    public void GetGrassResidencyChunks(
        Camera camera,
        float frustumPaddingDegrees,
        List<PlanetChunk> output)
    {
        if (output == null)
            return;

        output.Clear();
        if (!_initialized || camera == null || !camera.isActiveAndEnabled || _quadtrees == null)
            return;

        CalculateExpandedFrustumPlanes(camera, frustumPaddingDegrees, _grassResidencyFrustumPlanes);
        Vector3 observerPosition = camera.transform.position;
        for (int face = 0; face < _quadtrees.Length; face++)
        {
            if (_faceVisible != null && !_faceVisible[face])
                continue;
            GatherGrassResidencyLeaves(
                _quadtrees[face].Root,
                observerPosition,
                _grassResidencyFrustumPlanes,
                output);
        }
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
                if (!_meshCache.IsActuallyVisible(chunk) || chunk.CpuVertices == null)
                    continue;
                if (!chunk.CpuLocalBounds.IntersectRay(localRay, out float boundsDistance))
                    continue;
                if (boundsDistance > bestDistance)
                    continue;

                if (ChunkSurfaceQueries.RaycastChunkTriangles(localRay, chunk, triangles, bestDistance,
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
        VoronoiBiomeField voronoiField = null;
        bool bakeLookupBuilt = false;
        bool releaseChunkBiomeTextures = false;
        if (biomeProvider is ColorGenerator cg && cg.Registry != null && cg.BiomeColors != null)
        {
            lookup = cg.Registry.BuildLookupData(Allocator.Persistent);
            lutColors = cg.BiomeColors;
            voronoiField = cg.VoronoiBiomeField;
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
                VoronoiBiomeField voronoiCopy = voronoiField;
                bool bakeEnabled = bakeLookupBuilt;
                System.Threading.Tasks.Parallel.For(batchStart, batchEnd, i =>
                {
                    var chunk = _allChunks[i];
                    CalculateChunkColors(chunk, biomeProvider);
                    if (bakeEnabled)
                        BiomeAtlasService.BakeChunkMap(chunk, lookupCopy, voronoiCopy, lutCopy);
                });
                ct.ThrowIfCancellationRequested();

                await Awaitable.MainThreadAsync();
                for (int i = batchStart; i < batchEnd; i++)
                {
                    var chunk = _allChunks[i];
                    if (_meshCache.RetainColorSource(chunk))
                    {
                        chunk.ReleaseCpuDataAfterColorUpload(
                            retainSurfaceSamplingAndRebakeData: chunk.IsLeaf,
                            retainUnitSphereForWaterSampler: _maxChunkDepth == 0);
                    }
                    if (bakeEnabled) BiomeAtlasService.UploadChunkMap(chunk, releasePendingPixels: !chunk.IsLeaf);
                }

                float pct = (float)batchEnd / total;
                progress?.Report(pct, $"Applied biome colors {batchEnd}/{total}");
                await Awaitable.NextFrameAsync(ct);
            }

            if (bakeLookupBuilt)
            {
                _biomeAtlas.BuildFaceAtlases(_allChunks);
                _meshCache.RebindAll();
                if (_faceMaterial != null) _faceMaterial.EnableKeyword(BiomeTextureModeKeyword);
                releaseChunkBiomeTextures = _biomeAtlas.HasCompleteAtlases();
            }
        }
        finally
        {
            if (bakeLookupBuilt)
            {
                BiomeAtlasService.LogBakeSummary(_allChunks, biomeProvider);
                BiomeAtlasService.ReleasePendingPixels(_allChunks);
                if (releaseChunkBiomeTextures)
                    _biomeAtlas.ReleasePerChunkBiomeTextures(_allChunks);
                lookup.Dispose();
            }
            ReportRetainedChunkCpuMemory();
        }
    }

    void BuildGrassSurfaceAtlases()
    {
        DisposeGrassSurfaceAtlases();
        _grassSurfaceAtlases = GrassSurfaceAtlasBuilder.Build(_allChunks, _maxChunkDepth);
    }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers()
        => _rootSamplers ?? (IReadOnlyList<IFaceMeshSampler>)System.Array.Empty<IFaceMeshSampler>();

    // Phase B step 9: Phase E entry point. Walks the chunk tree on the face containing the
    // direction, finds the leaf, re-runs the bake + GPU upload for that single chunk.
    // Returns the count rebaked (0 if pre-init, no registry available, etc.). Phase E callers
    // will typically expand this to a radius â€” for now single-chunk is sufficient plumbing.
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
        bool updated = false;
        try
        {
            BiomeAtlasService.BakeChunkMap(leaf, lookup, cg.VoronoiBiomeField, cg.BiomeColors);
            updated = _biomeAtlas.UpdateFaceAtlasRegion(leaf);
            if (!updated && leaf.BiomeBlendedColorTexture != null)
            {
                BiomeAtlasService.UploadChunkMap(leaf, releasePendingPixels: true);
                updated = true;
            }
            _meshCache.Rebind(leaf);
        }
        finally
        {
            leaf.PendingBiomeBlendedColorPixels = null;
            leaf.PendingBiomeIdsPixels = null;
            leaf.PendingBiomeWeightsPixels = null;
            lookup.Dispose();
        }
        return updated ? 1 : 0;
    }

    const string BiomeTextureModeKeyword = "_BIOME_COLOR_MODE_TEXTURE";

    public void Dispose()
    {
        // Reverse construction order: mesh cache, generator, biome atlas.
        _meshCache.Dispose();
        _generator.Dispose();
        _biomeAtlas.Dispose();
        DisposeGrassSurfaceAtlases();
        if (_faceMaterial != null) _faceMaterial.DisableKeyword(BiomeTextureModeKeyword);

        // Phase B step 3: dispose per-chunk biome + surface-state textures so we don't leak
        // GPU memory across planet regen.
        int chunkTexturesBefore = PlanetChunkTextures.LiveTextureSets;
        int chunkBiomeTexturesBefore = PlanetChunkTextures.LiveBiomeTextureSets;
        int surfaceStateTexturesBefore = PlanetChunkTextures.LiveSurfaceStateTextures;
        int chunkCount = _allChunks.Count;
        for (int i = 0; i < _allChunks.Count; i++)
            PlanetChunkTextures.Dispose(_allChunks[i]);
        _allChunks.Clear();
        MemoryDebugCounters.ReportRetainedChunkCpuBytes(0);
        LoggerProvider.Log(LogLevel.Debug, "ChunkLOD",
            $"Dispose: disposed {chunkCount} chunk texture sets. " +
            $"Any {chunkTexturesBefore} -> {PlanetChunkTextures.LiveTextureSets}, " +
            $"biome {chunkBiomeTexturesBefore} -> {PlanetChunkTextures.LiveBiomeTextureSets}, " +
            $"surface-state {surfaceStateTexturesBefore} -> {PlanetChunkTextures.LiveSurfaceStateTextures}");
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

        _meshCache.AttachFaceObjects(_faceRoots, _faceVisible);
    }

    // Public surface contract (grass placement reads the face atlases). Delegates to the service.
    public bool TryGetFaceBiomeAtlases(int face, out Texture2D blended, out Texture2D ids, out Texture2D weights)
        => _biomeAtlas.TryGetFaceAtlases(face, out blended, out ids, out weights);

    void DisposeGrassSurfaceAtlases()
    {
        _grassSurfaceAtlases?.Dispose();
        _grassSurfaceAtlases = null;
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

    void ReportRetainedChunkCpuMemory()
    {
        long retainedBytes = 0;
        for (int i = 0; i < _allChunks.Count; i++)
        {
            PlanetChunk chunk = _allChunks[i];
            if (chunk != null)
                retainedBytes += chunk.GetRetainedCpuDataBytes();
        }

        MemoryDebugCounters.ReportRetainedChunkCpuBytes(retainedBytes);
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
                    _meshCache.SetVisible(oldLeaf, false);
            }

            for (int i = 0; i < _tmpVisibleLeaves.Count; i++)
            {
                var newLeaf = _tmpVisibleLeaves[i];
                if (!ContainsReference(current, newLeaf))
                    _meshCache.SetVisible(newLeaf, true);
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

    void GatherGrassResidencyLeaves(
        PlanetChunk chunk,
        Vector3 observerPos,
        Plane[] residencyFrustumPlanes,
        List<PlanetChunk> output)
    {
        if (chunk == null || chunk.CpuVertices == null)
            return;

        Bounds worldBounds = EstimateWorldBounds(chunk);
        if (!GeometryUtility.TestPlanesAABB(residencyFrustumPlanes, worldBounds)
            || !IsChunkAboveHorizon(chunk, observerPos, worldBounds))
        {
            return;
        }

        bool hasChildren = chunk.Children != null && chunk.Children.Length > 0;
        if (hasChildren && ShouldSubdivide(chunk, observerPos))
        {
            for (int i = 0; i < chunk.Children.Length; i++)
                GatherGrassResidencyLeaves(
                    chunk.Children[i],
                    observerPos,
                    residencyFrustumPlanes,
                    output);
            return;
        }

        output.Add(chunk);
    }

    static void CalculateExpandedFrustumPlanes(
        Camera camera,
        float paddingDegrees,
        Plane[] outputPlanes)
    {
        float near = Mathf.Max(camera.nearClipPlane, 0.01f);
        float far = Mathf.Max(camera.farClipPlane, near + 1f);
        float aspect = Mathf.Max(camera.aspect, 0.01f);
        float padding = Mathf.Clamp(paddingDegrees, 0f, 60f);
        Matrix4x4 projection;

        if (camera.orthographic)
        {
            float scale = 1f + padding / 45f;
            float halfHeight = Mathf.Max(camera.orthographicSize * scale, 0.01f);
            float halfWidth = halfHeight * aspect;
            projection = Matrix4x4.Ortho(
                -halfWidth,
                halfWidth,
                -halfHeight,
                halfHeight,
                near,
                far);
        }
        else
        {
            float verticalFov = Mathf.Clamp(camera.fieldOfView + padding * 2f, 1f, 175f);
            projection = Matrix4x4.Perspective(verticalFov, aspect, near, far);
        }

        GeometryUtility.CalculateFrustumPlanes(
            projection * camera.worldToCameraMatrix,
            outputPlanes);
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
}
