using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

sealed class GrassPlacementController : System.IDisposable, IGrassDebugStatsProvider
{
    const int VerticesPerVisualBlade = 18;
    const int VisualBladesPerInstance = 3;
    const int BladeVertexCount = VerticesPerVisualBlade * VisualBladesPerInstance;
    const int LaneResolution = PlanetChunkTextures.BiomeMapResolution;
    const int ThreadGroupSize = 8;
    const float LaneJitterMagnitude = 1.1f; // > 1 = blades from adjacent lanes overlap visually
    const float GrassBoundsPaddingMeters = 8f;
    const float AllocationReleasePaddingMeters = 50f;
    const string PlacementComputeResource = "BiomeGrassPlace";
    const string CameraFrustumPlanesName = "_CameraFrustumPlanes";
    const int GrassStatsCount = 15;
    const int StatCandidateLanes = 0;
    const int StatDensityRejectedLanes = 1;
    const int StatShapeRejectedLanes = 2;
    const int StatStateRejectedLanes = 3;
    const int StatWaterRejectedLanes = 4;
    const int StatSlopeRejectedLanes = 5;
    const int StatDistanceRejectedLanes = 6;
    const int StatDistanceFadeRejectedLanes = 7;
    const int StatFrustumRejectedLanes = 8;
    const int StatVisibleLanes = 9;
    const int StatCandidateBlades = 10;
    const int StatDensityRejectedBlades = 11;
    const int StatSlopeRejectedBlades = 12;
    const int StatEmittedBlades = 13;
    const int StatOverflowRejectedBlades = 14;

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
    static readonly int FrustumCullEnabledId = Shader.PropertyToID("_FrustumCullEnabled");
    static readonly int GrassStatsId = Shader.PropertyToID("_GrassStats");
    static readonly Plane[] FrustumPlanes = new Plane[6];
    static readonly Vector4[] FrustumPlaneVectors = new Vector4[6];

    readonly Transform _planetTransform;
    readonly ChunkedSurfaceProvider _surfaceProvider;
    readonly IChunkVisibilitySource _visibilitySource;
    readonly IGrassQualitySettings _qualitySettings;
    readonly ILogger _logger;
    readonly Material _material;
    readonly ComputeShader _placementCompute;
    readonly ComputeBuffer _grassParamsBuffer;
    readonly int _placeKernel;
    readonly bool _placementAvailable;
    readonly Dictionary<PlanetChunk, GrassChunkRuntime> _chunks = new();
    readonly HashSet<PlanetChunk> _visibleChunks = new();
    readonly List<PlanetChunk> _chunksToRelease = new();
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
    int _lastDrawCalls;
    int _lastBladeInstances;
    int _lastPlacementDispatches;
    int _lastChunksWithInstances;
    int _lastChunksWithStats;
    int _lastChunkInstanceMin;
    int _lastChunkInstanceMax;
    long _lastCandidateLanes;
    long _lastDensityRejectedLanes;
    long _lastShapeRejectedLanes;
    long _lastStateRejectedLanes;
    long _lastWaterRejectedLanes;
    long _lastSlopeRejectedLanes;
    long _lastDistanceRejectedLanes;
    long _lastDistanceFadeRejectedLanes;
    long _lastFrustumRejectedLanes;
    long _lastVisibleLanes;
    long _lastCandidateBlades;
    long _lastDensityRejectedBlades;
    long _lastSlopeRejectedBlades;
    long _lastEmittedBlades;
    long _lastOverflowRejectedBlades;
    long _lastBufferBytes;
    int _lastOldChunkSuppressedCount;
    bool _disposed;

    public GrassPlacementController(Transform planetTransform, ChunkedSurfaceProvider surfaceProvider,
        ComputeBuffer grassParamsBuffer, int grassParamCount,
        float waterRadius, int seed, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceProvider = surfaceProvider;
        _visibilitySource = surfaceProvider;
        _grassParamsBuffer = grassParamsBuffer;
        _grassParamCount = Mathf.Max(grassParamCount, 0);
        _waterRadius = waterRadius;
        _seed = seed;
        _logger = logger ?? LoggerProvider.Get();
        if (!ServiceLocator.TryGet(out _qualitySettings))
            _qualitySettings = new DefaultGrassQualitySettings();
        _maxChunkDepth = surfaceProvider.MaxChunkDepth;
        _maxCoarseLodOffsetForBlades = _qualitySettings.MaxCoarseLodOffsetForBlades;
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

        _visibilitySource.ChunkShown += HandleChunkShown;
        _visibilitySource.ChunkHidden += HandleChunkHidden;

        var visible = _visibilitySource.GetVisibleChunksSnapshot();
        for (int i = 0; i < visible.Count; i++)
            TrackVisibleChunk(visible[i]);

        _logger.Log(LogLevel.Debug, "Grass",
            $"Initialized placement renderer: chunks={_chunks.Count}, minDepth={_minChunkDepthForBlades}, seed={seed}, waterRadius={waterRadius:F2}, surfaceAtlas={_surfaceAtlasResolution}.");
    }

    Camera _lastTickCamera;
    Vector3 _lastDispatchCameraPosition;
    const float CameraRedispatchDistance = 25f; // re-place when camera moves > 25m

    public void Tick(Camera camera)
    {
        if (_disposed || _material == null || camera == null) return;
        _lastTickCamera = camera;
        ReconcileRuntimeAllocations(camera.transform.position);

        // Re-dispatch placement on any chunk whose distance-LOD result may have changed
        // because the camera moved enough to materially shift which chunks are in range.
        // Cheap heuristic: if camera moved >25m, re-run placement on all tracked chunks.
        if ((camera.transform.position - _lastDispatchCameraPosition).sqrMagnitude > CameraRedispatchDistance * CameraRedispatchDistance)
        {
            _lastDispatchCameraPosition = camera.transform.position;
            RedispatchAllPlacements();
        }

        _lastDrawCalls = 0;
        _lastBladeInstances = 0;
        _lastChunksWithInstances = 0;
        _lastChunksWithStats = 0;
        _lastChunkInstanceMin = 0;
        _lastChunkInstanceMax = 0;
        _lastCandidateLanes = 0;
        _lastDensityRejectedLanes = 0;
        _lastShapeRejectedLanes = 0;
        _lastStateRejectedLanes = 0;
        _lastWaterRejectedLanes = 0;
        _lastSlopeRejectedLanes = 0;
        _lastDistanceRejectedLanes = 0;
        _lastDistanceFadeRejectedLanes = 0;
        _lastFrustumRejectedLanes = 0;
        _lastVisibleLanes = 0;
        _lastCandidateBlades = 0;
        _lastDensityRejectedBlades = 0;
        _lastSlopeRejectedBlades = 0;
        _lastEmittedBlades = 0;
        _lastOverflowRejectedBlades = 0;
        _lastBufferBytes = 0L;
        _lastOldChunkSuppressedCount = 0;
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

            bool suppress = false;
            if (suppressionRadiusSq > 0f && chunk != null
                && chunk.CpuVertices != null && chunk.CpuVertices.Length > 0)
            {
                Vector3 chunkCenterWs = _planetTransform.TransformPoint(chunk.CpuLocalBounds.center);
                if ((chunkCenterWs - cameraPos).sqrMagnitude < suppressionRadiusSq)
                    suppress = true;
            }

            if (suppress)
            {
                _lastOldChunkSuppressedCount++;
            }
            else
            {
                runtime.Render(_material, camera, _planetTransform.gameObject.layer);
                _lastDrawCalls++;
            }

            int instanceCount = runtime.ReportedInstanceCount;
            _lastBladeInstances += instanceCount;
            if (instanceCount > 0)
                _lastChunksWithInstances++;
            chunksWithInstanceReadback++;
            chunkInstanceMin = Mathf.Min(chunkInstanceMin, instanceCount);
            _lastChunkInstanceMax = Mathf.Max(_lastChunkInstanceMax, instanceCount);
            _lastBufferBytes += runtime.BufferBytes;
            AccumulateRuntimeStats(runtime);
        }

        _lastChunkInstanceMin = chunksWithInstanceReadback > 0 ? chunkInstanceMin : 0;
    }

    void RedispatchAllPlacements()
    {
        if (!_placementAvailable) return;
        _lastPlacementDispatches = 0;
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
            _lastPlacementDispatches++;
            runtime.RequestReadbacks();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_visibilitySource != null)
        {
            _visibilitySource.ChunkShown -= HandleChunkShown;
            _visibilitySource.ChunkHidden -= HandleChunkHidden;
        }
        ServiceLocator.Unregister<IGrassDebugStatsProvider>(this);

        foreach (var pair in _chunks)
            pair.Value?.Dispose();
        _chunks.Clear();
        _visibleChunks.Clear();

        if (_material != null)
        {
            if (Application.isPlaying) Object.Destroy(_material);
            else Object.DestroyImmediate(_material);
        }
    }

    void HandleChunkShown(PlanetChunk chunk)
    {
        TrackVisibleChunk(chunk);
    }

    void TrackVisibleChunk(PlanetChunk chunk)
    {
        if (_disposed || chunk == null) return;
        _visibleChunks.Add(chunk);
    }

    void HandleChunkHidden(PlanetChunk chunk)
    {
        if (chunk == null) return;
        _visibleChunks.Remove(chunk);
        if (!_chunks.TryGetValue(chunk, out GrassChunkRuntime runtime)) return;
        runtime.Dispose();
        _chunks.Remove(chunk);
    }

    void ReconcileRuntimeAllocations(Vector3 cameraPosition)
    {
        if (_material == null || !_placementAvailable)
            return;

        float allocationDistanceSq = _maxRenderDistance * _maxRenderDistance;
        float releaseDistance = _maxRenderDistance + AllocationReleasePaddingMeters;
        float releaseDistanceSq = releaseDistance * releaseDistance;
        foreach (PlanetChunk chunk in _visibleChunks)
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
            if (!_visibleChunks.Contains(pair.Key))
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
            BladeInstancesId, GrassStatsCount, worldBounds);
        if (runtime == null)
            return null;

        DispatchPlacement(chunk, runtime, biomeIds, biomeWeights, surfaceRadius, surfaceNormal);
        _lastPlacementDispatches++;
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
        SetFrustumCullInputs(_lastTickCamera);

        int groups = Mathf.CeilToInt(LaneResolution / (float)ThreadGroupSize);
        _placementCompute.Dispatch(_placeKernel, groups, groups, 1);
    }

    public GrassDebugStats GetGrassDebugStats()
    {
        return new GrassDebugStats
        {
            ControllerActive = !_disposed,
            ShaderAvailable = _material != null,
            SmokeRenderer = false,
            VisibleChunks = _visibleChunks.Count,
            TrackedChunks = _chunks.Count,
            DrawCalls = _lastDrawCalls,
            BladeInstances = _lastBladeInstances,
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
            SurfaceAtlasResolution = _surfaceAtlasResolution,
            BufferMegabytes = _lastBufferBytes / (1024f * 1024f),
            PlacementDispatches = _lastPlacementDispatches,
            ChunksWithInstances = _lastChunksWithInstances,
            ChunksWithStats = _lastChunksWithStats,
            ChunkInstanceMin = _lastChunkInstanceMin,
            ChunkInstanceMax = _lastChunkInstanceMax,
            ChunkInstanceAverage = _lastDrawCalls > 0 ? _lastBladeInstances / (float)_lastDrawCalls : 0f,
            CandidateLanes = _lastCandidateLanes,
            DensityRejectedLanes = _lastDensityRejectedLanes,
            ShapeRejectedLanes = _lastShapeRejectedLanes,
            StateRejectedLanes = _lastStateRejectedLanes,
            WaterRejectedLanes = _lastWaterRejectedLanes,
            SlopeRejectedLanes = _lastSlopeRejectedLanes,
            DistanceRejectedLanes = _lastDistanceRejectedLanes,
            DistanceFadeRejectedLanes = _lastDistanceFadeRejectedLanes,
            FrustumRejectedLanes = _lastFrustumRejectedLanes,
            VisibleLanes = _lastVisibleLanes,
            CandidateBlades = _lastCandidateBlades,
            DensityRejectedBlades = _lastDensityRejectedBlades,
            SlopeRejectedBlades = _lastSlopeRejectedBlades,
            EmittedBlades = _lastEmittedBlades,
            OverflowRejectedBlades = _lastOverflowRejectedBlades,
            OldChunkSuppressedCount = _lastOldChunkSuppressedCount,
        };
    }

    void AccumulateRuntimeStats(GrassChunkRuntime runtime)
    {
        if (runtime == null || !runtime.HasStats)
            return;

        _lastChunksWithStats++;
        _lastCandidateLanes += runtime.GetStat(StatCandidateLanes);
        _lastDensityRejectedLanes += runtime.GetStat(StatDensityRejectedLanes);
        _lastShapeRejectedLanes += runtime.GetStat(StatShapeRejectedLanes);
        _lastStateRejectedLanes += runtime.GetStat(StatStateRejectedLanes);
        _lastWaterRejectedLanes += runtime.GetStat(StatWaterRejectedLanes);
        _lastSlopeRejectedLanes += runtime.GetStat(StatSlopeRejectedLanes);
        _lastDistanceRejectedLanes += runtime.GetStat(StatDistanceRejectedLanes);
        _lastDistanceFadeRejectedLanes += runtime.GetStat(StatDistanceFadeRejectedLanes);
        _lastFrustumRejectedLanes += runtime.GetStat(StatFrustumRejectedLanes);
        _lastVisibleLanes += runtime.GetStat(StatVisibleLanes);
        _lastCandidateBlades += runtime.GetStat(StatCandidateBlades);
        _lastDensityRejectedBlades += runtime.GetStat(StatDensityRejectedBlades);
        _lastSlopeRejectedBlades += runtime.GetStat(StatSlopeRejectedBlades);
        _lastEmittedBlades += runtime.GetStat(StatEmittedBlades);
        _lastOverflowRejectedBlades += runtime.GetStat(StatOverflowRejectedBlades);
    }

    void SetFrustumCullInputs(Camera camera)
    {
        if (camera == null)
        {
            _placementCompute.SetInt(FrustumCullEnabledId, 0);
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(camera, FrustumPlanes);
        for (int i = 0; i < FrustumPlanes.Length; i++)
        {
            Plane plane = FrustumPlanes[i];
            Vector3 normal = plane.normal;
            FrustumPlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
        }

        _placementCompute.SetInt(FrustumCullEnabledId, 1);
        _placementCompute.SetVectorArray(CameraFrustumPlanesName, FrustumPlaneVectors);
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

    sealed class GrassChunkRuntime : System.IDisposable
    {
        const int BladeStride = sizeof(float) * 12;
        static readonly uint[] ArgsScratch = new uint[4];
        static readonly uint[] StatsScratch = new uint[GrassStatsCount];

        readonly GraphicsBuffer _bladeBuffer;
        readonly GraphicsBuffer _argsBuffer;
        readonly GraphicsBuffer _statsBuffer;
        readonly MaterialPropertyBlock _props;
        readonly Bounds _worldBounds;
        readonly uint[] _stats = new uint[GrassStatsCount];
        bool _disposed;
        int _readbackInstanceCount;
        bool _hasStats;

        public GraphicsBuffer BladeBuffer => _bladeBuffer;
        public GraphicsBuffer ArgsBuffer => _argsBuffer;
        public GraphicsBuffer StatsBuffer => _statsBuffer;
        public bool IsValid => !_disposed && _bladeBuffer != null && _argsBuffer != null && _statsBuffer != null;
        public int Capacity { get; }
        public int ReportedInstanceCount => _readbackInstanceCount;
        public long BufferBytes { get; }
        public bool HasStats => _hasStats;

        GrassChunkRuntime(GraphicsBuffer bladeBuffer, GraphicsBuffer argsBuffer, GraphicsBuffer statsBuffer,
            MaterialPropertyBlock props, int capacity, Bounds worldBounds)
        {
            _bladeBuffer = bladeBuffer;
            _argsBuffer = argsBuffer;
            _statsBuffer = statsBuffer;
            _props = props;
            _worldBounds = worldBounds;
            Capacity = Mathf.Max(0, capacity);
            BufferBytes = (long)Capacity * BladeStride + GraphicsBuffer.IndirectDrawArgs.size + (long)GrassStatsCount * sizeof(uint);
        }

        public static GrassChunkRuntime Create(int capacity, int vertexCount, int bladeInstancesId,
            int statsCount, Bounds worldBounds)
        {
            if (capacity <= 0) return null;

            var bladeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, BladeStride);
            var argsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
                4,
                sizeof(uint));
            var statsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(statsCount, 1), sizeof(uint));

            var props = new MaterialPropertyBlock();
            props.SetBuffer(bladeInstancesId, bladeBuffer);

            var runtime = new GrassChunkRuntime(bladeBuffer, argsBuffer, statsBuffer, props, capacity, worldBounds);
            runtime.ResetArgsAndStats(vertexCount);
            return runtime;
        }

        public void ResetArgsAndStats(int vertexCount)
        {
            if (_argsBuffer == null) return;
            ArgsScratch[0] = (uint)Mathf.Max(vertexCount, 0);
            ArgsScratch[1] = 0;
            ArgsScratch[2] = 0;
            ArgsScratch[3] = 0;
            _argsBuffer.SetData(ArgsScratch);
            if (_statsBuffer != null)
                _statsBuffer.SetData(StatsScratch);
            _readbackInstanceCount = 0;
            _hasStats = false;
        }

        public void RequestReadbacks()
        {
            if (_argsBuffer == null || !SystemInfo.supportsAsyncGPUReadback)
                return;

            AsyncGPUReadback.Request(_argsBuffer, request =>
            {
                if (_disposed || request.hasError)
                    return;
                var data = request.GetData<uint>();
                if (data.Length >= 2)
                    _readbackInstanceCount = Mathf.Max(0, (int)data[1]);
            });

            if (_statsBuffer == null)
                return;

            AsyncGPUReadback.Request(_statsBuffer, request =>
            {
                if (_disposed || request.hasError)
                    return;
                var data = request.GetData<uint>();
                int count = Mathf.Min(data.Length, _stats.Length);
                for (int i = 0; i < count; i++)
                    _stats[i] = data[i];
                for (int i = count; i < _stats.Length; i++)
                    _stats[i] = 0;
                _hasStats = true;
            });
        }

        public uint GetStat(int index)
        {
            return index >= 0 && index < _stats.Length ? _stats[index] : 0u;
        }

        public void Render(Material material, Camera camera, int layer)
        {
            if (_disposed || _argsBuffer == null) return;

            var renderParams = new RenderParams(material)
            {
                camera = camera,
                layer = layer,
                matProps = _props,
                worldBounds = _worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
            };
            Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _argsBuffer, 1, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bladeBuffer?.Dispose();
            _argsBuffer?.Dispose();
            _statsBuffer?.Dispose();
        }
    }
}
