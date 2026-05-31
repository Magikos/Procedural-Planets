using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

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
public sealed class ChunkedSurfaceProvider : IPlanetSurfaceProvider
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
    const bool EnableChunkDiagnosticsLog = false;
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

    // Per-face visible-leaf snapshot — compared each Tick to detect changes.
    readonly List<PlanetChunk>[] _visibleLeavesPerFace = new List<PlanetChunk>[6];
    readonly List<PlanetChunk> _tmpVisibleLeaves = new();
    readonly HashSet<PlanetChunk> _tmpVisibleSet = new();
    readonly List<PlanetChunk> _allChunks = new();
    readonly Dictionary<PlanetChunk, ChunkRenderHandle> _chunkRenderers = new();

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

        // 6) Initial visibility. Camera.main is available by load time; if not, default to
        //    "render coarsest" (root chunks visible everywhere) and Tick fixes it next frame.
        progress?.Report(0.94f, "Initial visibility...");
        var cam = Camera.main;
        Vector3 observerPos = cam != null ? cam.transform.position : _planetTransform.position;
        PrepareLodContext(cam);
        for (int f = 0; f < 6; f++) UpdateVisibleLeavesForFace(f, observerPos);

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

    public async Awaitable GenerateColorsAsync(IBiomeProvider biomeProvider, IProgressHandle progress, CancellationToken ct)
    {
        if (biomeProvider == null || _allChunks.Count == 0) return;

        const int colorBatchSize = 96;
        int total = _allChunks.Count;
        for (int batchStart = 0; batchStart < total; batchStart += colorBatchSize)
        {
            int batchEnd = Mathf.Min(batchStart + colorBatchSize, total);

            await Awaitable.BackgroundThreadAsync();
            ct.ThrowIfCancellationRequested();
            System.Threading.Tasks.Parallel.For(batchStart, batchEnd, i => CalculateChunkColors(_allChunks[i], biomeProvider));
            ct.ThrowIfCancellationRequested();

            await Awaitable.MainThreadAsync();
            for (int i = batchStart; i < batchEnd; i++)
                ApplyChunkColors(_allChunks[i]);

            float pct = (float)batchEnd / total;
            progress?.Report(0.80f + 0.10f * pct, $"Applied biome colors {batchEnd}/{total}");
            await Awaitable.NextFrameAsync(ct);
        }
    }

    public IReadOnlyList<IFaceMeshSampler> GetFaceMeshSamplers()
        => _rootSamplers ?? (IReadOnlyList<IFaceMeshSampler>)System.Array.Empty<IFaceMeshSampler>();

    public void Dispose()
    {
        DisposeAllPendingJobs();
        if (_filters.IsCreated) _filters.Dispose();
        foreach (var pair in _chunkRenderers)
        {
            var handle = pair.Value;
            if (handle == null) continue;
            if (handle.Mesh != null) Object.Destroy(handle.Mesh);
        }
        _chunkRenderers.Clear();
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
        return handle;
    }

    void SetChunkVisible(PlanetChunk chunk, bool visible)
    {
        var handle = EnsureChunkRenderer(chunk);
        if (handle == null || handle.GameObject == null) return;

        bool active = visible && _faceVisible != null && _faceVisible[chunk.FaceIndex];
        if (handle.Visible == active) return;

        handle.Visible = active;
        handle.GameObject.SetActive(active);
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
