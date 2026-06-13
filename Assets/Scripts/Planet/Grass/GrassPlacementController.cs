using System.Collections.Generic;
using UnityEngine;

sealed class GrassPlacementController : System.IDisposable, IGrassDebugStatsProvider
{
    const int VerticesPerVisualBlade = 18;
    const int ClusterCardsPerInstance = 3;
    const int VisualBladesPerCard = 5;
    const int VisualBladesPerInstance = ClusterCardsPerInstance * VisualBladesPerCard;
    const int BladeVertexCount = VerticesPerVisualBlade * ClusterCardsPerInstance;
    const int LaneResolution = PlanetChunkTextures.BiomeMapResolution;
    const int ThreadGroupSize = 8;
    const float LaneJitterMagnitude = 1.1f; // > 1 = blades from adjacent lanes overlap visually
    const float GrassBoundsPaddingMeters = 8f;
    const float AllocationReleasePaddingMeters = 50f;
    const string PlacementComputeResource = "BiomeGrassPlace";

    static readonly int BladeInstancesId = Shader.PropertyToID("_GrassBladeInstances");
    static readonly int GrassDrawArgsId = Shader.PropertyToID("_GrassDrawArgs");
    static readonly int BiomeIdsId = Shader.PropertyToID("_BiomeIds");
    static readonly int BiomeWeightsId = Shader.PropertyToID("_BiomeWeights");
    static readonly int SurfaceStateMaskId = Shader.PropertyToID("_SurfaceStateMask");
    static readonly int GrassSurfaceRadiusId = Shader.PropertyToID("_GrassSurfaceRadius");
    static readonly int GrassSurfaceNormalId = Shader.PropertyToID("_GrassSurfaceNormal");
    static readonly int BiomeGrassParamsId = Shader.PropertyToID("_BiomeGrassParams");
    static readonly int BiomeGrassParamCountId = Shader.PropertyToID("_BiomeGrassParamCount");
    static readonly int BiomeAtlasResolutionId = Shader.PropertyToID("_BiomeAtlasResolution");
    static readonly int GrassSurfaceAtlasResolutionId = Shader.PropertyToID("_GrassSurfaceAtlasResolution");
    static readonly int LaneResolutionId = Shader.PropertyToID("_LaneResolution");
    static readonly int MaxBladeInstancesId = Shader.PropertyToID("_MaxBladeInstances");
    static readonly int MaxBladesPerLaneId = Shader.PropertyToID("_MaxBladesPerLane");
    static readonly int GrassDensityMultiplierId = Shader.PropertyToID("_GrassDensityMultiplier");
    static readonly int FaceIndexId = Shader.PropertyToID("_FaceIndex");
    static readonly int ChunkHashId = Shader.PropertyToID("_ChunkHash");
    static readonly int ChunkUvScaleOffsetId = Shader.PropertyToID("_ChunkUvScaleOffset");
    static readonly int PlanetLocalToWorldId = Shader.PropertyToID("_PlanetLocalToWorld");
    static readonly int PlanetWorldScaleId = Shader.PropertyToID("_PlanetWorldScale");
    static readonly int WaterRadiusId = Shader.PropertyToID("_WaterRadius");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int CameraPositionWsId = Shader.PropertyToID("_CameraPositionWs");
    static readonly int MaxRenderDistanceId = Shader.PropertyToID("_MaxRenderDistance");
    static readonly int DistanceFadeStartId = Shader.PropertyToID("_DistanceFadeStart");
    static readonly int CullDistanceJitter01Id = Shader.PropertyToID("_CullDistanceJitter01");
    static readonly int LaneJitterMagnitudeId = Shader.PropertyToID("_LaneJitterMagnitude");
    static readonly int ChunkInnerFadeStartId = Shader.PropertyToID("_ChunkInnerFadeStart");
    static readonly int ChunkInnerFadeEndId = Shader.PropertyToID("_ChunkInnerFadeEnd");
    static readonly int FrustumCullEnabledId = Shader.PropertyToID("_FrustumCullEnabled");
    static readonly int GrassStatsId = Shader.PropertyToID("_GrassStats");
    readonly Transform _planetTransform;
    readonly ChunkedSurfaceProvider _surfaceProvider;
    readonly IGrassQualitySettings _qualitySettings;
    readonly ILogger _logger;
    readonly Material _material;
    readonly ComputeShader _placementCompute;
    readonly ComputeBuffer _grassParamsBuffer;
    readonly int _placeKernel;
    readonly bool _placementAvailable;
    readonly Dictionary<PlanetChunk, GrassChunkRuntime> _chunks = new();
    readonly List<PlanetChunk> _residencyScratch = new(128);
    readonly HashSet<PlanetChunk> _residencyChunks = new();
    readonly List<PlanetChunk> _chunksToRelease = new();
    readonly Plane[] _renderFrustumPlanes = new Plane[6];
    readonly int _minChunkDepthForBlades;
    readonly int _maxChunkDepth;
    readonly int _maxCoarseLodOffsetForBlades;
    readonly int _surfaceAtlasResolution;
    readonly int _grassParamCount;
    readonly float _waterRadius;
    readonly int _seed;
    readonly int _maxBladesPerLane;
    readonly int _maxBladeInstancesPerChunk;
    readonly float _grassDensityMultiplier;
    readonly float _maxRenderDistance;
    readonly float _distanceFadeStart;
    readonly float _cullDistanceJitter01;
    readonly float _residencyFrustumPaddingDegrees;
    readonly GrassPlacementStats _stats = new();
    bool _disposed;

    public GrassPlacementController(Transform planetTransform, ChunkedSurfaceProvider surfaceProvider,
        ComputeBuffer grassParamsBuffer, int grassParamCount,
        float waterRadius, int seed, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceProvider = surfaceProvider;
        _grassParamsBuffer = grassParamsBuffer;
        _grassParamCount = Mathf.Max(grassParamCount, 0);
        _waterRadius = waterRadius;
        _seed = seed;
        _logger = logger ?? LoggerProvider.Get();
        if (!ServiceLocator.TryGet(out _qualitySettings))
            _qualitySettings = new DefaultGrassQualitySettings();
        _maxChunkDepth = surfaceProvider.MaxChunkDepth;
        _maxCoarseLodOffsetForBlades = Mathf.Clamp(
            _qualitySettings.MaxCoarseLodOffsetForBlades, 0, _maxChunkDepth);
        _minChunkDepthForBlades = Mathf.Max(0, _maxChunkDepth - _maxCoarseLodOffsetForBlades);
        _surfaceAtlasResolution = surfaceProvider.GrassSurfaceAtlases?.AtlasResolution ?? 0;
        // Quality knobs read once at controller construction. Per-chunk buffers are sized
        // for max possible blades = lanes x MaxBladesPerLane; indirect args decide how
        // many of those allocated slots are drawn.
        _maxBladesPerLane = Mathf.Clamp(_qualitySettings.MaxBladesPerLane, 1, 32);
        _maxBladeInstancesPerChunk = LaneResolution * LaneResolution * _maxBladesPerLane;
        _grassDensityMultiplier = Mathf.Max(0f, _qualitySettings.DensityMultiplier);
        _maxRenderDistance = Mathf.Max(0f, _qualitySettings.MaxRenderDistance);
        _distanceFadeStart = Mathf.Clamp(_qualitySettings.LowLodDistance, 0f, _maxRenderDistance);
        _cullDistanceJitter01 = Mathf.Clamp01(_qualitySettings.CullDistanceJitter01);
        _residencyFrustumPaddingDegrees = Mathf.Clamp(
            _qualitySettings.ResidencyFrustumPaddingDegrees, 0f, 60f);
        ServiceLocator.Register<IGrassDebugStatsProvider>(this);

        Shader shader = Shader.Find("Planet/Grass");
        if (shader == null)
        {
            _logger.Log(LogLevel.Warning, "Grass", "Planet/Grass shader was not found; grass renderer is disabled.");
            return;
        }

        _material = new Material(shader)
        {
            name = "Runtime Grass Material",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (SystemInfo.supportsComputeShaders)
        {
            _placementCompute = Resources.Load<ComputeShader>(PlacementComputeResource);
            if (_placementCompute != null)
            {
                try
                {
                    _placeKernel = _placementCompute.FindKernel("PlaceAndCull");
                    _placementAvailable = true;
                }
                catch (System.Exception ex)
                {
                    _logger.Log(LogLevel.Warning, "Grass", $"BiomeGrassPlace.compute is missing PlaceAndCull: {ex.Message}");
                }
            }
        }

        bool warnedPlacementUnavailable = false;
        if (_placementAvailable && (_grassParamsBuffer == null || _grassParamCount <= 0))
        {
            _placementAvailable = false;
            warnedPlacementUnavailable = true;
            _logger.Log(LogLevel.Warning, "Grass", "Biome grass parameter buffer was not available; no production grass will be placed.");
        }

        if (!_placementAvailable && !warnedPlacementUnavailable)
            _logger.Log(LogLevel.Warning, "Grass", "BiomeGrassPlace compute shader was not available; no production grass will be placed.");

        _logger.Log(LogLevel.Debug, "Grass",
            $"Initialized placement renderer: chunks={_chunks.Count}, minDepth={_minChunkDepthForBlades}, residencyPadding={_residencyFrustumPaddingDegrees:F1}deg, seed={seed}, waterRadius={waterRadius:F2}, surfaceAtlas={_surfaceAtlasResolution}.");
    }

    Camera _lastTickCamera;
    Vector3 _lastDispatchCameraPosition;
    float _chunkInnerFadeStart;
    float _chunkInnerFadeEnd;
    const float CameraRedispatchDistance = 25f; // re-place when camera moves > 25m

    public void Tick(Camera camera)
    {
        if (_disposed || _material == null || camera == null) return;
        _lastTickCamera = camera;
        RefreshResidency(camera);
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
            RedispatchAllPlacements();
        }

        _stats.ResetPerTick();
        int chunksWithInstanceReadback = 0;
        int chunkInstanceMin = int.MaxValue;

        // Cache near-field suppression radius once per tick. Inside that radius the
        // near-field renderer produces dense carpet grass, so the medium-distance
        // chunk blades would double up wastefully. Placement remains warm so chunk
        // grass is ready as soon as the camera crosses the handoff boundary.
        float suppressionRadiusSq = 0f;
        if (ServiceLocator.TryGet(out IGrassNearFieldStatsProvider nfProvider))
        {
            GrassNearFieldStats nfStats = nfProvider.GetGrassNearFieldStats();
            if (nfStats.ControllerActive && nfStats.ShaderAvailable && nfStats.SuppressionRadius > 0f)
                suppressionRadiusSq = nfStats.SuppressionRadius * nfStats.SuppressionRadius;
        }
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

    void RedispatchAllPlacements()
    {
        if (!_placementAvailable) return;
        _stats.PlacementDispatches = 0;
        foreach (var pair in _chunks)
        {
            PlanetChunk chunk = pair.Key;
            GrassChunkRuntime runtime = pair.Value;
            if (runtime == null || !runtime.IsValid) continue;
            if (!_surfaceProvider.TryGetFaceBiomeAtlases(chunk.FaceIndex,
                    out _, out Texture2D biomeIds, out Texture2D biomeWeights)) continue;
            if (!_surfaceProvider.GrassSurfaceAtlases.TryGetFace(chunk.FaceIndex,
                    out Texture2D surfaceRadius, out Texture2D surfaceNormal)) continue;
            if (chunk.SurfaceStateTexture == null) continue;
            DispatchPlacement(chunk, runtime, biomeIds, biomeWeights, surfaceRadius, surfaceNormal);
            _stats.PlacementDispatches++;
            runtime.RequestReadbacks();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ServiceLocator.Unregister<IGrassDebugStatsProvider>(this);

        foreach (var pair in _chunks)
            pair.Value?.Dispose();
        _chunks.Clear();
        _residencyChunks.Clear();
        _residencyScratch.Clear();

        if (_material != null)
        {
            if (Application.isPlaying) Object.Destroy(_material);
            else Object.DestroyImmediate(_material);
        }
    }

    void RefreshResidency(Camera camera)
    {
        _surfaceProvider.GetGrassResidencyChunks(
            camera,
            _residencyFrustumPaddingDegrees,
            _residencyScratch);

        _residencyChunks.Clear();
        _stats.BufferedResidencyChunks = 0;
        for (int i = 0; i < _residencyScratch.Count; i++)
        {
            PlanetChunk chunk = _residencyScratch[i];
            if (chunk == null || chunk.DetailLevel < _minChunkDepthForBlades)
                continue;

            _residencyChunks.Add(chunk);
            if (!_surfaceProvider.IsChunkTerrainVisible(chunk))
                _stats.BufferedResidencyChunks++;
        }
        _stats.VisibleChunks = _residencyChunks.Count - _stats.BufferedResidencyChunks;
    }

    void ReconcileRuntimeAllocations(Vector3 cameraPosition)
    {
        if (_material == null || !_placementAvailable)
            return;

        float allocationDistanceSq = _maxRenderDistance * _maxRenderDistance;
        float releaseDistance = _maxRenderDistance + AllocationReleasePaddingMeters;
        float releaseDistanceSq = releaseDistance * releaseDistance;
        foreach (PlanetChunk chunk in _residencyChunks)
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
                GrassChunkRuntime runtime = CreateRuntime(chunk);
                if (runtime != null)
                    _chunks.Add(chunk, runtime);
            }
        }

        foreach (var pair in _chunks)
        {
            if (!_residencyChunks.Contains(pair.Key))
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

    GrassChunkRuntime CreateRuntime(PlanetChunk chunk)
    {
        if (!_surfaceProvider.TryGetFaceBiomeAtlases(chunk.FaceIndex,
                out _, out Texture2D biomeIds, out Texture2D biomeWeights))
            return null;
        if (!_surfaceProvider.GrassSurfaceAtlases.TryGetFace(chunk.FaceIndex,
                out Texture2D surfaceRadius, out Texture2D surfaceNormal))
            return null;
        if (chunk.SurfaceStateTexture == null)
            return null;

        Bounds worldBounds = EstimateGrassWorldBounds(chunk);
        var runtime = GrassChunkRuntime.Create(_maxBladeInstancesPerChunk, BladeVertexCount,
            BladeInstancesId, GrassChunkRuntime.StatsCount, worldBounds);
        if (runtime == null)
            return null;

        DispatchPlacement(chunk, runtime, biomeIds, biomeWeights, surfaceRadius, surfaceNormal);
        _stats.PlacementDispatches++;
        runtime.RequestReadbacks();
        return runtime;
    }

    void DispatchPlacement(PlanetChunk chunk, GrassChunkRuntime runtime,
        Texture2D biomeIds, Texture2D biomeWeights, Texture2D surfaceRadius, Texture2D surfaceNormal)
    {
        runtime.ResetArgsAndStats(BladeVertexCount);

        _placementCompute.SetTexture(_placeKernel, BiomeIdsId, biomeIds);
        _placementCompute.SetTexture(_placeKernel, BiomeWeightsId, biomeWeights);
        _placementCompute.SetTexture(_placeKernel, SurfaceStateMaskId, chunk.SurfaceStateTexture);
        _placementCompute.SetTexture(_placeKernel, GrassSurfaceRadiusId, surfaceRadius);
        _placementCompute.SetTexture(_placeKernel, GrassSurfaceNormalId, surfaceNormal);
        _placementCompute.SetBuffer(_placeKernel, BladeInstancesId, runtime.BladeBuffer);
        _placementCompute.SetBuffer(_placeKernel, GrassDrawArgsId, runtime.ArgsBuffer);
        _placementCompute.SetBuffer(_placeKernel, GrassStatsId, runtime.StatsBuffer);

        _placementCompute.SetInt(BiomeGrassParamCountId, _grassParamCount);
        _placementCompute.SetBuffer(_placeKernel, BiomeGrassParamsId, _grassParamsBuffer);
        _placementCompute.SetInt(BiomeAtlasResolutionId, Mathf.Max(biomeIds.width, 1));
        _placementCompute.SetInt(GrassSurfaceAtlasResolutionId, Mathf.Max(surfaceRadius.width, 1));
        _placementCompute.SetInt(LaneResolutionId, LaneResolution);
        _placementCompute.SetInt(MaxBladeInstancesId, runtime.Capacity);
        _placementCompute.SetInt(MaxBladesPerLaneId, _maxBladesPerLane);
        _placementCompute.SetFloat(GrassDensityMultiplierId, _grassDensityMultiplier);
        _placementCompute.SetInt(FaceIndexId, chunk.FaceIndex);
        _placementCompute.SetInt(ChunkHashId, unchecked((int)chunk.HashValue));
        _placementCompute.SetVector(ChunkUvScaleOffsetId, _surfaceProvider.GrassSurfaceAtlases.GetUvScaleOffset(chunk));
        _placementCompute.SetMatrix(PlanetLocalToWorldId, _planetTransform.localToWorldMatrix);
        _placementCompute.SetFloat(PlanetWorldScaleId, GetUniformWorldScale(_planetTransform));
        _placementCompute.SetFloat(WaterRadiusId, _waterRadius);
        _placementCompute.SetInt(SeedId, _seed);
        // Level-1 grass tuning: camera-distance gate + sub-lane jitter spread. Camera position
        // comes from whichever camera last called Tick; falls back to planet origin so
        // initial placement before first Tick still produces blades (gated off by
        // MaxRenderDistance=0 if Bryan disables this entirely).
        Vector3 cameraPos = _lastTickCamera != null ? _lastTickCamera.transform.position : _planetTransform.position;
        _placementCompute.SetVector(CameraPositionWsId, cameraPos);
        _placementCompute.SetFloat(MaxRenderDistanceId, _maxRenderDistance);
        _placementCompute.SetFloat(DistanceFadeStartId, _distanceFadeStart);
        _placementCompute.SetFloat(CullDistanceJitter01Id, _cullDistanceJitter01);
        _placementCompute.SetFloat(LaneJitterMagnitudeId, LaneJitterMagnitude);
        _placementCompute.SetFloat(ChunkInnerFadeStartId, _chunkInnerFadeStart);
        _placementCompute.SetFloat(ChunkInnerFadeEndId, _chunkInnerFadeEnd);
        // Placement buffers persist while the camera turns, so baking the current
        // view frustum into them leaves square holes until the next movement-driven
        // redispatch. Rendering still culls each chunk by its world bounds.
        _placementCompute.SetInt(FrustumCullEnabledId, 0);

        int groups = Mathf.CeilToInt(LaneResolution / (float)ThreadGroupSize);
        _placementCompute.Dispatch(_placeKernel, groups, groups, 1);
    }

    public GrassDebugStats GetGrassDebugStats()
    {
        return new GrassDebugStats
        {
            ControllerActive = !_disposed,
            ShaderAvailable = _material != null,
            VisibleChunks = _stats.VisibleChunks,
            ResidencyChunks = _residencyChunks.Count,
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
            VisualBladesPerInstance = VisualBladesPerInstance,
            BladeVertexCount = BladeVertexCount,
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

    static void ResolveChunkInnerFade(out float start, out float end)
    {
        start = 0f;
        end = 0f;
        if (!ServiceLocator.TryGet(out IGrassNearFieldStatsProvider provider))
            return;

        GrassNearFieldStats near = provider.GetGrassNearFieldStats();
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
