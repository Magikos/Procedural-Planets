using System.Collections.Generic;
using UnityEngine;

sealed class GrassPlacementController : System.IDisposable, IGrassDebugStatsProvider
{
    const float GrassBoundsPaddingMeters = 8f;
    const float AllocationReleasePaddingMeters = 50f;
    const float CameraRedispatchDistance = 25f; // re-place when camera moves > 25m

    readonly Transform _planetTransform;
    readonly IGrassNearFieldStatsProvider _nearFieldStatsProvider;
    readonly ILogger _logger;
    readonly Material _material;
    readonly GrassChunkResidencyResolver _resolver;
    readonly GrassChunkDispatcher _dispatcher;
    readonly Dictionary<PlanetChunk, GrassChunkRuntime> _chunks = new();
    readonly List<PlanetChunk> _chunksToRelease = new();
    readonly Plane[] _renderFrustumPlanes = new Plane[6];
    readonly int _minChunkDepthForBlades;
    readonly int _maxChunkDepth;
    readonly int _maxCoarseLodOffsetForBlades;
    readonly int _surfaceAtlasResolution;
    readonly float _maxRenderDistance;
    readonly float _distanceFadeStart;
    readonly float _cullDistanceJitter01;
    readonly float _residencyFrustumPaddingDegrees;
    readonly int _maxBladesPerLane;
    readonly float _grassDensityMultiplier;
    readonly GrassPlacementStats _stats = new();

    Camera _lastTickCamera;
    Vector3 _lastDispatchCameraPosition;
    float _chunkInnerFadeStart;
    float _chunkInnerFadeEnd;
    bool _disposed;

    public GrassPlacementController(Transform planetTransform, ChunkedSurfaceProvider surfaceProvider,
        ComputeBuffer grassParamsBuffer, int grassParamCount,
        float waterRadius, int seed, IGrassNearFieldStatsProvider nearFieldStatsProvider,
        ILogger logger)
    {
        _planetTransform = planetTransform;
        _logger = logger ?? LoggerProvider.Get();
        _nearFieldStatsProvider = nearFieldStatsProvider
            ?? throw new System.ArgumentNullException(nameof(nearFieldStatsProvider));
        _maxChunkDepth = surfaceProvider.MaxChunkDepth;

        var quality = ServiceLocator.Get<IGrassQualitySettings>();
        _maxCoarseLodOffsetForBlades = Mathf.Clamp(quality.MaxCoarseLodOffsetForBlades, 0, _maxChunkDepth);
        _minChunkDepthForBlades = Mathf.Max(0, _maxChunkDepth - _maxCoarseLodOffsetForBlades);
        _surfaceAtlasResolution = surfaceProvider.GrassSurfaceAtlases?.AtlasResolution ?? 0;
        _maxBladesPerLane = Mathf.Clamp(quality.MaxBladesPerLane, 1, 32);
        int maxBladeInstancesPerChunk = PlanetChunkTextures.BiomeMapResolution * PlanetChunkTextures.BiomeMapResolution * _maxBladesPerLane;
        _grassDensityMultiplier = Mathf.Max(0f, quality.DensityMultiplier);
        _maxRenderDistance = Mathf.Max(0f, quality.MaxRenderDistance);
        _distanceFadeStart = Mathf.Clamp(quality.LowLodDistance, 0f, _maxRenderDistance);
        _cullDistanceJitter01 = Mathf.Clamp01(quality.CullDistanceJitter01);
        _residencyFrustumPaddingDegrees = Mathf.Clamp(quality.ResidencyFrustumPaddingDegrees, 0f, 60f);

        _resolver = new GrassChunkResidencyResolver(surfaceProvider, _minChunkDepthForBlades, _residencyFrustumPaddingDegrees);
        _dispatcher = new GrassChunkDispatcher(
            surfaceProvider, planetTransform,
            grassParamsBuffer, grassParamCount,
            _maxBladesPerLane, maxBladeInstancesPerChunk,
            _grassDensityMultiplier, _maxRenderDistance, _distanceFadeStart, _cullDistanceJitter01,
            waterRadius, seed, _logger);

        ServiceLocator.RegisterWorld<IGrassDebugStatsProvider>(this);

        Shader shader = Shader.Find("Planet/Grass");
        if (shader == null)
        {
            _logger.Log(LogLevel.Info, "Grass", "Planet/Grass shader was not found; grass renderer is disabled.");
            return;
        }

        _material = new Material(shader)
        {
            name = "Runtime Grass Material",
            hideFlags = HideFlags.HideAndDontSave,
        };

        _logger.Log(LogLevel.Debug, "Grass",
            $"Initialized placement renderer: chunks={_chunks.Count}, minDepth={_minChunkDepthForBlades}, residencyPadding={_residencyFrustumPaddingDegrees:F1}deg, seed={seed}, waterRadius={waterRadius:F2}, surfaceAtlas={_surfaceAtlasResolution}.");
    }

    public void Tick(Camera camera)
    {
        if (_disposed || _material == null || camera == null) return;
        _lastTickCamera = camera;
        _dispatcher.SetTickCamera(camera);
        _resolver.Refresh(camera);
        _stats.VisibleChunks = _resolver.VisibleChunks;
        _stats.BufferedResidencyChunks = _resolver.BufferedResidencyChunks;
        ReconcileRuntimeAllocations(camera.transform.position);
        GeometryUtility.CalculateFrustumPlanes(camera, _renderFrustumPlanes);

        ResolveChunkInnerFade(out float innerFadeStart, out float innerFadeEnd);
        bool transitionChanged = !Mathf.Approximately(_chunkInnerFadeStart, innerFadeStart)
            || !Mathf.Approximately(_chunkInnerFadeEnd, innerFadeEnd);
        _chunkInnerFadeStart = innerFadeStart;
        _chunkInnerFadeEnd = innerFadeEnd;

        // Re-dispatch placement on any chunk whose distance-LOD result may have changed
        // because the camera moved enough to materially shift which chunks are in range.
        // Cheap heuristic: if camera moved >25m, re-run placement on all tracked chunks.
        if (transitionChanged
            || (camera.transform.position - _lastDispatchCameraPosition).sqrMagnitude
                > CameraRedispatchDistance * CameraRedispatchDistance)
        {
            _lastDispatchCameraPosition = camera.transform.position;
            _stats.PlacementDispatches = _dispatcher.RedispatchAll(_chunks, innerFadeStart, innerFadeEnd);
        }

        _stats.ResetPerTick();
        int chunksWithInstanceReadback = 0;
        int chunkInstanceMin = int.MaxValue;

        // Cache near-field suppression radius once per tick. Inside that radius the
        // near-field renderer produces dense carpet grass, so the medium-distance
        // chunk blades would double up wastefully. Placement remains warm so chunk
        // grass is ready as soon as the camera crosses the handoff boundary.
        float suppressionRadiusSq = 0f;
        GrassNearFieldStats nfStats = _nearFieldStatsProvider.GetGrassNearFieldStats();
        if (nfStats.ControllerActive && nfStats.ShaderAvailable && nfStats.SuppressionRadius > 0f)
            suppressionRadiusSq = nfStats.SuppressionRadius * nfStats.SuppressionRadius;
        Vector3 cameraPos = camera.transform.position;

        foreach (var pair in _chunks)
        {
            PlanetChunk chunk = pair.Key;
            GrassChunkRuntime runtime = pair.Value;
            if (runtime == null || !runtime.IsValid) continue;

            bool isFineChunk = chunk.DetailLevel == _maxChunkDepth;
            if (isFineChunk)
                _stats.FineTrackedChunks++;
            else
                _stats.CoarseTrackedChunks++;

            bool suppress = false;
            if (suppressionRadiusSq > 0f && chunk != null
                && chunk.CpuVertices != null && chunk.CpuVertices.Length > 0)
            {
                Vector3 chunkCenterWs = _planetTransform.TransformPoint(chunk.CpuLocalBounds.center);
                if ((chunkCenterWs - cameraPos).sqrMagnitude < suppressionRadiusSq)
                    suppress = true;
            }

            bool renderChunk = !suppress
                && GeometryUtility.TestPlanesAABB(_renderFrustumPlanes, runtime.WorldBounds);
            if (suppress)
            {
                _stats.OldChunkSuppressedCount++;
            }
            else if (renderChunk)
            {
                runtime.Render(_material, camera, _planetTransform.gameObject.layer);
                _stats.DrawCalls++;
            }

            int instanceCount = runtime.ReportedInstanceCount;
            _stats.ResidentBladeInstances += instanceCount;
            if (instanceCount > 0)
            {
                _stats.ResidentChunksWithInstances++;
                if (isFineChunk)
                    _stats.FineChunksWithInstances++;
                else
                    _stats.CoarseChunksWithInstances++;

                if (renderChunk)
                    _stats.ChunksWithInstances++;
            }
            if (renderChunk)
                _stats.BladeInstances += instanceCount;
            chunksWithInstanceReadback++;
            chunkInstanceMin = Mathf.Min(chunkInstanceMin, instanceCount);
            _stats.ChunkInstanceMax = Mathf.Max(_stats.ChunkInstanceMax, instanceCount);
            _stats.BufferBytes += runtime.BufferBytes;
            _stats.Accumulate(runtime);
        }

        _stats.ChunkInstanceMin = chunksWithInstanceReadback > 0 ? chunkInstanceMin : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ServiceLocator.UnregisterWorld<IGrassDebugStatsProvider>(this);

        foreach (var pair in _chunks)
            pair.Value?.Dispose();
        _chunks.Clear();
        _dispatcher.Dispose();
        _resolver.Chunks.Clear();

        if (_material != null)
        {
            if (Application.isPlaying) Object.Destroy(_material);
            else Object.DestroyImmediate(_material);
        }
    }

    void ReconcileRuntimeAllocations(Vector3 cameraPosition)
    {
        if (_material == null || !_dispatcher.IsAvailable)
            return;

        float allocationDistanceSq = _maxRenderDistance * _maxRenderDistance;
        float releaseDistance = _maxRenderDistance + AllocationReleasePaddingMeters;
        float releaseDistanceSq = releaseDistance * releaseDistance;
        foreach (PlanetChunk chunk in _resolver.Chunks)
        {
            if (chunk == null || chunk.DetailLevel < _minChunkDepthForBlades)
                continue;

            Bounds bounds = EstimateGrassWorldBounds(chunk);
            float distanceSq = bounds.SqrDistance(cameraPosition);
            if (_chunks.ContainsKey(chunk))
            {
                if (distanceSq > releaseDistanceSq)
                    _chunksToRelease.Add(chunk);
            }
            else if (distanceSq <= allocationDistanceSq)
            {
                GrassChunkRuntime runtime = _dispatcher.CreateAndDispatch(chunk, bounds, _chunkInnerFadeStart, _chunkInnerFadeEnd);
                if (runtime != null)
                {
                    _chunks.Add(chunk, runtime);
                    _stats.PlacementDispatches++;
                }
            }
        }

        foreach (var pair in _chunks)
        {
            if (!_resolver.Chunks.Contains(pair.Key))
                _chunksToRelease.Add(pair.Key);
        }

        for (int i = 0; i < _chunksToRelease.Count; i++)
        {
            PlanetChunk chunk = _chunksToRelease[i];
            if (!_chunks.TryGetValue(chunk, out GrassChunkRuntime runtime))
                continue;
            runtime.Dispose();
            _chunks.Remove(chunk);
        }
        _chunksToRelease.Clear();
    }

    public GrassDebugStats GetGrassDebugStats()
    {
        return new GrassDebugStats
        {
            ControllerActive = !_disposed,
            ShaderAvailable = _material != null,
            VisibleChunks = _stats.VisibleChunks,
            ResidencyChunks = _resolver.Chunks.Count,
            BufferedResidencyChunks = _stats.BufferedResidencyChunks,
            TrackedChunks = _chunks.Count,
            FineTrackedChunks = _stats.FineTrackedChunks,
            CoarseTrackedChunks = _stats.CoarseTrackedChunks,
            DrawCalls = _stats.DrawCalls,
            BladeInstances = _stats.BladeInstances,
            ResidentChunksWithInstances = _stats.ResidentChunksWithInstances,
            ResidentBladeInstances = _stats.ResidentBladeInstances,
            MaxChunkDepth = _maxChunkDepth,
            MinChunkDepthForBlades = _minChunkDepthForBlades,
            MaxCoarseLodOffsetForBlades = _maxCoarseLodOffsetForBlades,
            MaxBladesPerLane = _maxBladesPerLane,
            VisualBladesPerInstance = GrassChunkRuntime.VisualBladesPerInstance,
            BladeVertexCount = GrassChunkRuntime.BladeVertexCount,
            DensityMultiplier = _grassDensityMultiplier,
            MaxRenderDistance = _maxRenderDistance,
            DistanceFadeStart = _distanceFadeStart,
            CullDistanceJitter01 = _cullDistanceJitter01,
            ResidencyFrustumPaddingDegrees = _residencyFrustumPaddingDegrees,
            PlacementFrustumCullEnabled = false,
            SurfaceAtlasResolution = _surfaceAtlasResolution,
            BufferMegabytes = _stats.BufferBytes / (1024f * 1024f),
            PlacementDispatches = _stats.PlacementDispatches,
            ChunksWithInstances = _stats.ChunksWithInstances,
            FineChunksWithInstances = _stats.FineChunksWithInstances,
            CoarseChunksWithInstances = _stats.CoarseChunksWithInstances,
            ChunksWithStats = _stats.ChunksWithStats,
            ChunkInstanceMin = _stats.ChunkInstanceMin,
            ChunkInstanceMax = _stats.ChunkInstanceMax,
            ChunkInstanceAverage = _chunks.Count > 0
                ? _stats.ResidentBladeInstances / (float)_chunks.Count
                : 0f,
            CandidateLanes = _stats.CandidateLanes,
            DensityRejectedLanes = _stats.DensityRejectedLanes,
            ShapeRejectedLanes = _stats.ShapeRejectedLanes,
            StateRejectedLanes = _stats.StateRejectedLanes,
            WaterRejectedLanes = _stats.WaterRejectedLanes,
            SlopeRejectedLanes = _stats.SlopeRejectedLanes,
            DistanceRejectedLanes = _stats.DistanceRejectedLanes,
            DistanceFadeRejectedLanes = _stats.DistanceFadeRejectedLanes,
            FrustumRejectedLanes = _stats.FrustumRejectedLanes,
            VisibleLanes = _stats.VisibleLanes,
            CandidateBlades = _stats.CandidateBlades,
            DensityRejectedBlades = _stats.DensityRejectedBlades,
            SlopeRejectedBlades = _stats.SlopeRejectedBlades,
            InnerFadeRejectedBlades = _stats.InnerFadeRejectedBlades,
            EmittedBlades = _stats.EmittedBlades,
            OverflowRejectedBlades = _stats.OverflowRejectedBlades,
            OldChunkSuppressedCount = _stats.OldChunkSuppressedCount,
            RegisteredInteractors = GrassInteractorRegistry.RegisteredCount,
            UploadedInteractors = GrassInteractorRegistry.LastActiveCount,
            ActiveInteractorSources = GrassInteractorRegistry.LastActiveSourceCount,
            UploadedReleaseSamples = GrassInteractorRegistry.LastReleaseSampleCount,
            RetainedReleaseSamples = GrassInteractorRegistry.RetainedReleaseSampleCount,
        };
    }

    void ResolveChunkInnerFade(out float start, out float end)
    {
        start = 0f;
        end = 0f;

        GrassNearFieldStats near = _nearFieldStatsProvider.GetGrassNearFieldStats();
        if (!near.ControllerActive || !near.ShaderAvailable || near.DrawDistance <= 0f)
            return;

        end = near.DrawDistance;
        start = Mathf.Clamp(
            Mathf.Max(near.FullDensityDistance, end - near.FadeBand),
            0f,
            end);
    }

    Bounds EstimateGrassWorldBounds(PlanetChunk chunk)
    {
        if (chunk == null || chunk.CpuVertices == null || chunk.CpuVertices.Length == 0)
            return new Bounds(_planetTransform != null ? _planetTransform.position : Vector3.zero, Vector3.one);

        Bounds local = chunk.CpuLocalBounds;
        Vector3 center = _planetTransform.TransformPoint(local.center);
        float scale = GetUniformWorldScale(_planetTransform);
        float radius = Mathf.Max(local.extents.magnitude * scale + GrassBoundsPaddingMeters * scale, 1f);
        return new Bounds(center, Vector3.one * (radius * 2f));
    }

    static float GetUniformWorldScale(Transform transform)
    {
        if (transform == null) return 1f;
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }
}
