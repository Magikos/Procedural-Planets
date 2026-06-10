using UnityEngine;
using UnityEngine.Rendering;

// Dense camera-centered near-field grass renderer, slice 2.
// Pairs with [Assets/Resources/GrassNearFieldPlace.compute].
//
// SLICE 2 vs SLICE 1:
//  - Face-space stable cells. Camera no longer defines where blades live, only which
//    cells are visible. Fixes the swimming/teleporting bug.
//  - Fixed face-space cell width derived from planet scale. Roots stay stable across
//    dispatch pages; local cube-face distortion is handled as density variation for now.
//  - Range paging at ~4m world equivalent: re-dispatch only when the page origin moves,
//    not every sub-cell camera motion. Should drop dispatchesTotal from ~843 to ~5-20.
//  - Explicit slot allocation via InterlockedAdd on the indirect args buffer. Real
//    overflow accounting; no CopyCount needed.
//  - Stable per-root distance thinning crossfades the dense near path into chunk grass
//    without a camera-centered opacity ring at drawDistance.
//  - Multi-face dispatch with proportional range quotas. Neighbor-face strips are rendered
//    at cube seams without allowing the primary face to consume the shared output buffer.
sealed class GrassNearFieldController : System.IDisposable, IGrassNearFieldStatsProvider
{
    const string ComputeResource = "GrassNearFieldPlace";
    const int StatsCount = 11;
    const int StatCandidateCells = 0;
    const int StatDensityRejected = 1;
    const int StatWaterRejected = 2;
    const int StatSlopeRejected = 3;
    const int StatDistanceRejected = 4;
    const int StatDistanceFadeRejected = 5;
    const int StatFrustumRejected = 6;
    const int StatEmitted = 7;
    const int StatFaceAreaRejected = 8;
    const int StatOverflow = 9;
    const int StatRangeBudgetRejected = 10;
    const int ThreadGroupSize = 8;
    const int BladeStride = sizeof(float) * 12;
    const int VerticesPerVisualBlade = 18;
    const int ClusterCardsPerInstance = 3;
    const int VisualBladesPerCard = 5;
    const int VisualBladesPerInstance = ClusterCardsPerInstance * VisualBladesPerCard;
    const int BladeVertexCount = VerticesPerVisualBlade * ClusterCardsPerInstance;

    // Keep the dense field fully populated through 144m, then use a broad stochastic
    // overlap with chunk grass. The previous 16m handoff exposed a visible density ring.
    const float DefaultSpacing = 0.35f;
    const float DefaultFullDensityDistance = 144f;
    const float DefaultDrawDistance = 200f;
    const float DefaultFadeBand = DefaultDrawDistance - DefaultFullDensityDistance;
    const float DefaultPageSizeMeters = 4f;     // re-dispatch only when page origin moves
    // Chunk-center suppression creates coarse ownership shapes. Keep chunk grass under
    // the near path and let stable per-root thinning perform the visual crossfade.
    const float SuppressionRadiusFraction = 0f;
    const int DefaultCapacityInstances = 1_000_000; // ~48 MB at 48 bytes per blade
    const bool EnableMultiFaceDispatch = true;

    static readonly int[] BiomeIdsIds = new int[6];
    static readonly int[] BiomeWeightsIds = new int[6];
    static readonly int[] SurfaceRadiusIds = new int[6];
    static readonly int[] SurfaceNormalIds = new int[6];

    static readonly int BiomeGrassParamsId = Shader.PropertyToID("_NearFieldBiomeGrassParams");
    static readonly int InstancesId = Shader.PropertyToID("_NearFieldGrassInstances");
    static readonly int DrawArgsId = Shader.PropertyToID("_NearFieldDrawArgs");
    static readonly int StatsId = Shader.PropertyToID("_NearFieldStats");
    static readonly int RangeCountsId = Shader.PropertyToID("_NearFieldRangeCounts");
    static readonly int BiomeAtlasResolutionId = Shader.PropertyToID("_NearFieldBiomeAtlasResolution");
    static readonly int SurfaceAtlasResolutionId = Shader.PropertyToID("_NearFieldSurfaceAtlasResolution");
    static readonly int BiomeParamCountId = Shader.PropertyToID("_NearFieldBiomeParamCount");
    static readonly int CapacityId = Shader.PropertyToID("_NearFieldCapacity");
    static readonly int SeedId = Shader.PropertyToID("_NearFieldSeed");
    static readonly int FaceIndexId = Shader.PropertyToID("_NearFieldFaceIndex");
    static readonly int RangeIndexId = Shader.PropertyToID("_NearFieldRangeIndex");
    static readonly int RangeBudgetId = Shader.PropertyToID("_NearFieldRangeBudget");
    static readonly int GridStartCellUVId = Shader.PropertyToID("_NearFieldGridStartCellUV");
    static readonly int GridSizeId = Shader.PropertyToID("_NearFieldGridSize");
    static readonly int CellUvWidthId = Shader.PropertyToID("_NearFieldCellUvWidth");
    static readonly int SpacingId = Shader.PropertyToID("_NearFieldSpacing");
    static readonly int FullDensityDistanceId = Shader.PropertyToID("_NearFieldFullDensityDistance");
    static readonly int DrawDistanceId = Shader.PropertyToID("_NearFieldDrawDistance");
    static readonly int FadeBandId = Shader.PropertyToID("_NearFieldFadeBand");
    static readonly int WaterRadiusId = Shader.PropertyToID("_NearFieldWaterRadius");
    static readonly int PlanetRadiusId = Shader.PropertyToID("_NearFieldPlanetRadius");
    static readonly int PlanetWorldScaleId = Shader.PropertyToID("_NearFieldPlanetWorldScale");
    static readonly int PlanetLocalToWorldId = Shader.PropertyToID("_NearFieldPlanetLocalToWorld");
    static readonly int PlanetCenterWsId = Shader.PropertyToID("_NearFieldPlanetCenterWs");
    static readonly int CameraPositionWsId = Shader.PropertyToID("_NearFieldCameraPositionWs");
    static readonly int BladeInstancesShaderId = Shader.PropertyToID("_GrassBladeInstances");

    static GrassNearFieldController()
    {
        for (int i = 0; i < 6; i++)
        {
            BiomeIdsIds[i] = Shader.PropertyToID($"_NearFieldBiomeIds_F{i}");
            BiomeWeightsIds[i] = Shader.PropertyToID($"_NearFieldBiomeWeights_F{i}");
            SurfaceRadiusIds[i] = Shader.PropertyToID($"_NearFieldSurfaceRadius_F{i}");
            SurfaceNormalIds[i] = Shader.PropertyToID($"_NearFieldSurfaceNormal_F{i}");
        }
    }

    readonly Transform _planetTransform;
    readonly ChunkedSurfaceProvider _surfaceProvider;
    readonly ComputeBuffer _grassParamsBuffer;
    readonly int _grassParamCount;
    readonly float _waterRadius;
    readonly float _planetRadius;
    readonly int _seed;
    readonly ILogger _logger;
    readonly Material _material;
    readonly ComputeShader _compute;
    readonly int _kernel;
    readonly bool _available;
    readonly int _capacity;
    readonly float _spacing;
    readonly float _fullDensityDistance;
    readonly float _drawDistance;
    readonly float _fadeBand;
    readonly float _cellUvWidth;
    readonly int _pageCellSize; // page width in cell units
    readonly GraphicsBuffer _instancesBuffer;
    readonly GraphicsBuffer _argsBuffer;
    readonly GraphicsBuffer _statsBuffer;
    readonly GraphicsBuffer _rangeCountsBuffer;
    readonly MaterialPropertyBlock _props;
    readonly uint[] _argsScratch = new uint[4];
    readonly uint[] _statsScratch = new uint[StatsCount];
    readonly uint[] _statsReadback = new uint[StatsCount];
    readonly uint[] _rangeCountsScratch = new uint[FaceSpaceCellRangeBuilder.MaxRanges];
    readonly int[] _rangeBudgets = new int[FaceSpaceCellRangeBuilder.MaxRanges];
    readonly long _bufferBytes;
    bool _disposed;

    // Last-dispatch state for change detection.
    bool _hasLastDispatch;
    int _lastFace;
    int _lastPagedStartU;
    int _lastPagedStartV;
    int _lastGridWidth;
    int _lastGridHeight;
    bool _lastSeamRisk;
    int _lastFaceCount;
    int _dispatchesTotal;
    GrassNearFieldDispatchReason _lastDispatchReason;
    bool _dispatchedThisFrame;
    int _lastEmittedInstances;
    bool _hasStats;

    public float SuppressionRadius => _drawDistance * SuppressionRadiusFraction;

    public GrassNearFieldController(Transform planetTransform, ChunkedSurfaceProvider surfaceProvider,
        ComputeBuffer grassParamsBuffer, int grassParamCount,
        float waterRadius, float planetRadius, int seed, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceProvider = surfaceProvider;
        _grassParamsBuffer = grassParamsBuffer;
        _grassParamCount = Mathf.Max(grassParamCount, 0);
        _waterRadius = waterRadius;
        _planetRadius = planetRadius;
        _seed = seed;
        _logger = logger ?? LoggerProvider.Get();
        _capacity = DefaultCapacityInstances;
        _spacing = DefaultSpacing;
        _fullDensityDistance = DefaultFullDensityDistance;
        _drawDistance = DefaultDrawDistance;
        _fadeBand = DefaultFadeBand;
        float referenceMetersPerUv = Mathf.Max(0.0001f, 2f * _planetRadius * FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform));
        _cellUvWidth = Mathf.Max(1e-8f, _spacing / referenceMetersPerUv);
        _pageCellSize = Mathf.Max(1, Mathf.CeilToInt(DefaultPageSizeMeters / _spacing));

        Shader shader = Shader.Find("Planet/Grass");
        if (shader == null)
        {
            _logger.Log(LogLevel.Warning, "GrassNF", "Planet/Grass shader not found; near-field renderer disabled.");
            return;
        }
        _material = new Material(shader)
        {
            name = "Runtime NearField Grass Material",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (!SystemInfo.supportsComputeShaders)
        {
            _logger.Log(LogLevel.Warning, "GrassNF", "Compute shaders not supported; near-field disabled.");
            return;
        }

        _compute = Resources.Load<ComputeShader>(ComputeResource);
        if (_compute == null)
        {
            _logger.Log(LogLevel.Warning, "GrassNF", $"Compute shader '{ComputeResource}' not found.");
            return;
        }

        try
        {
            _kernel = _compute.FindKernel("PlaceAndCullNearField");
        }
        catch (System.Exception ex)
        {
            _logger.Log(LogLevel.Warning, "GrassNF", $"Kernel lookup failed: {ex.Message}");
            return;
        }

        if (_grassParamsBuffer == null || _grassParamCount <= 0)
        {
            _logger.Log(LogLevel.Warning, "GrassNF", "Biome grass params buffer unavailable; near-field disabled.");
            return;
        }

        _instancesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, BladeStride);
        _argsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
            4, sizeof(uint));
        _statsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, StatsCount, sizeof(uint));
        _rangeCountsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, FaceSpaceCellRangeBuilder.MaxRanges, sizeof(uint));
        _props = new MaterialPropertyBlock();
        _props.SetBuffer(BladeInstancesShaderId, _instancesBuffer);
        _bufferBytes = (long)_capacity * BladeStride
            + GraphicsBuffer.IndirectDrawArgs.size
            + (long)(StatsCount + FaceSpaceCellRangeBuilder.MaxRanges) * sizeof(uint);
        _available = true;
        ServiceLocator.Register<IGrassNearFieldStatsProvider>(this);

        _logger.Log(LogLevel.Debug, "GrassNF",
            $"Initialized: capacity={_capacity}, spacing={_spacing}, cellUvWidth={_cellUvWidth:E3}, fullDensity={_fullDensityDistance}, draw={_drawDistance}, fadeBand={_fadeBand}, pageCellSize={_pageCellSize}, buffer={_bufferBytes / (1024f * 1024f):F1} MB");
    }

    // Slice 4b range construction is shared with the deprecated mid-field experiment.
    // Near field now dispatches every valid range into one quota-protected output buffer.
    readonly FaceSpaceCell[] _rangeScratch = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];
    readonly FaceSpaceCell[] _lastRanges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];
    int _lastRangeCount;

    public void Tick(Camera camera)
    {
        if (_disposed || !_available || camera == null || _material == null) return;
        _dispatchedThisFrame = false;

        var result = FaceSpaceCellRangeBuilder.BuildRanges(
            camera, _planetTransform, _planetRadius,
            _drawDistance, _cellUvWidth, _pageCellSize, _rangeScratch);

        if (result.Count == 0)
            return;

        int activeRangeCount = EnableMultiFaceDispatch ? result.Count : 1;
        bool shouldDispatch = !_hasLastDispatch || activeRangeCount != _lastRangeCount;
        if (!shouldDispatch)
        {
            for (int i = 0; i < activeRangeCount; i++)
            {
                FaceSpaceCell now = _rangeScratch[i];
                FaceSpaceCell last = _lastRanges[i];
                if (now.FaceIndex != last.FaceIndex
                    || now.PageOriginCellUV != last.PageOriginCellUV
                    || now.GridSize != last.GridSize)
                {
                    shouldDispatch = true;
                    break;
                }
            }
        }

        GrassNearFieldDispatchReason reason = !_hasLastDispatch
            ? GrassNearFieldDispatchReason.Initial
            : (activeRangeCount != _lastRangeCount || _rangeScratch[0].FaceIndex != _lastRanges[0].FaceIndex)
                ? GrassNearFieldDispatchReason.FaceChanged
                : GrassNearFieldDispatchReason.PageChanged;

        if (shouldDispatch)
        {
            DispatchAllRanges(camera, _planetTransform.position, _rangeScratch, activeRangeCount);
            _hasLastDispatch = true;
            for (int i = 0; i < activeRangeCount; i++) _lastRanges[i] = _rangeScratch[i];
            _lastRangeCount = activeRangeCount;
            _lastFace = _rangeScratch[0].FaceIndex;
            _lastPagedStartU = _rangeScratch[0].PageOriginCellUV.x;
            _lastPagedStartV = _rangeScratch[0].PageOriginCellUV.y;
            _lastGridWidth = _rangeScratch[0].GridSize.x;
            _lastGridHeight = _rangeScratch[0].GridSize.y;
            _lastFaceCount = activeRangeCount;
            _lastSeamRisk = result.UncoveredCornerStraddle || (!EnableMultiFaceDispatch && result.Count > 1);
            _lastDispatchReason = reason;
            _dispatchesTotal++;
            _dispatchedThisFrame = true;
        }
        else
        {
            // Range geometry can change without page boundary cross (e.g. camera rotates
            // inside a page but the seam-risk indicator should still update).
            _lastSeamRisk = result.UncoveredCornerStraddle || (!EnableMultiFaceDispatch && result.Count > 1);
        }

        Render(camera);
    }

    // Dispatch all face ranges into the shared args + instances + stats buffers. Reset
    // happens ONCE before the loop; each face appends via InterlockedAdd.
    void DispatchAllRanges(Camera camera, Vector3 planetCenter, FaceSpaceCell[] ranges, int count)
    {
        ResetArgsAndStats();
        BindGlobalTextures();
        BuildRangeBudgets(ranges, count);

        for (int i = 0; i < count; i++)
        {
            FaceSpaceCell range = ranges[i];
            if (range.GridSize.x <= 0 || range.GridSize.y <= 0) continue;
            DispatchOneFaceRange(camera, planetCenter, range, i, _rangeBudgets[i]);
        }

        RequestReadbacks();
    }

    // Per-dispatch globals: shared across all faces in one Tick. Bound once before the
    // first face dispatch; faces only re-bind their per-face FaceIndex + grid start/size.
    void BindGlobalTextures()
    {
        GrassSurfaceAtlasGpuData grassAtlases = _surfaceProvider.GrassSurfaceAtlases;
        int surfaceAtlasResolution = grassAtlases != null ? grassAtlases.AtlasResolution : 0;
        int biomeAtlasResolution = 0;
        for (int f = 0; f < 6; f++)
        {
            Texture2D radius = null, normal = null;
            if (grassAtlases != null)
                grassAtlases.TryGetFace(f, out radius, out normal);
            _compute.SetTexture(_kernel, SurfaceRadiusIds[f], radius != null ? (Texture)radius : Texture2D.blackTexture);
            _compute.SetTexture(_kernel, SurfaceNormalIds[f], normal != null ? (Texture)normal : Texture2D.normalTexture);

            Texture2D ids = null, weights = null;
            _surfaceProvider.TryGetFaceBiomeAtlases(f, out _, out ids, out weights);
            _compute.SetTexture(_kernel, BiomeIdsIds[f], ids != null ? (Texture)ids : Texture2D.blackTexture);
            _compute.SetTexture(_kernel, BiomeWeightsIds[f], weights != null ? (Texture)weights : Texture2D.blackTexture);
            if (ids != null && biomeAtlasResolution == 0)
                biomeAtlasResolution = ids.width;
        }

        _compute.SetBuffer(_kernel, BiomeGrassParamsId, _grassParamsBuffer);
        _compute.SetBuffer(_kernel, InstancesId, _instancesBuffer);
        _compute.SetBuffer(_kernel, DrawArgsId, _argsBuffer);
        _compute.SetBuffer(_kernel, StatsId, _statsBuffer);
        _compute.SetBuffer(_kernel, RangeCountsId, _rangeCountsBuffer);
        _compute.SetInt(BiomeAtlasResolutionId, Mathf.Max(biomeAtlasResolution, 1));
        _compute.SetInt(SurfaceAtlasResolutionId, Mathf.Max(surfaceAtlasResolution, 1));
        _compute.SetInt(BiomeParamCountId, _grassParamCount);
        _compute.SetInt(CapacityId, _capacity);
        _compute.SetInt(SeedId, _seed);
        _compute.SetFloat(SpacingId, _spacing);
        _compute.SetFloat(FullDensityDistanceId, _fullDensityDistance);
        _compute.SetFloat(DrawDistanceId, _drawDistance);
        _compute.SetFloat(FadeBandId, _fadeBand);
        _compute.SetFloat(WaterRadiusId, _waterRadius);
        _compute.SetFloat(PlanetRadiusId, _planetRadius);
        _compute.SetFloat(PlanetWorldScaleId, FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform));
        _compute.SetMatrix(PlanetLocalToWorldId, _planetTransform.localToWorldMatrix);
    }

    void DispatchOneFaceRange(
        Camera camera,
        Vector3 planetCenter,
        FaceSpaceCell range,
        int rangeIndex,
        int rangeBudget)
    {
        _compute.SetInt(FaceIndexId, range.FaceIndex);
        _compute.SetInt(RangeIndexId, rangeIndex);
        _compute.SetInt(RangeBudgetId, rangeBudget);
        _compute.SetInts(GridStartCellUVId, range.PageOriginCellUV.x, range.PageOriginCellUV.y);
        _compute.SetInts(GridSizeId, range.GridSize.x, range.GridSize.y);
        _compute.SetFloat(CellUvWidthId, range.CellUvWidth);
        _compute.SetVector(PlanetCenterWsId, planetCenter);
        _compute.SetVector(CameraPositionWsId, camera.transform.position);

        int groupsX = Mathf.CeilToInt(range.GridSize.x / (float)ThreadGroupSize);
        int groupsY = Mathf.CeilToInt(range.GridSize.y / (float)ThreadGroupSize);
        _compute.Dispatch(_kernel, groupsX, groupsY, 1);
    }

    void BuildRangeBudgets(FaceSpaceCell[] ranges, int count)
    {
        long totalCells = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2Int size = ranges[i].GridSize;
            totalCells += System.Math.Max(0L, (long)size.x * size.y);
            _rangeBudgets[i] = 0;
        }

        if (count <= 0 || totalCells <= 0)
            return;

        // Give every active face one guaranteed slot, then divide the rest by candidate
        // area. Quotas sum exactly to capacity, so the global append cannot overflow.
        int reserved = Mathf.Min(count, _capacity);
        int distributable = _capacity - reserved;
        int allocated = 0;
        for (int i = 0; i < count; i++)
        {
            int budget;
            if (i == count - 1)
            {
                budget = _capacity - allocated;
            }
            else
            {
                Vector2Int size = ranges[i].GridSize;
                long cells = System.Math.Max(0L, (long)size.x * size.y);
                budget = 1 + (int)((long)distributable * cells / totalCells);
                budget = Mathf.Min(budget, _capacity - allocated);
            }

            _rangeBudgets[i] = Mathf.Max(0, budget);
            allocated += _rangeBudgets[i];
        }
    }

    void ResetArgsAndStats()
    {
        // vertexCountPerInstance, instanceCount, startVertex, startInstance.
        // Kernel increments [1] via InterlockedAdd; no CopyCount needed.
        _argsScratch[0] = BladeVertexCount;
        _argsScratch[1] = 0;
        _argsScratch[2] = 0;
        _argsScratch[3] = 0;
        _argsBuffer.SetData(_argsScratch);
        for (int i = 0; i < StatsCount; i++) _statsScratch[i] = 0u;
        _statsBuffer.SetData(_statsScratch);
        for (int i = 0; i < _rangeCountsScratch.Length; i++) _rangeCountsScratch[i] = 0u;
        _rangeCountsBuffer.SetData(_rangeCountsScratch);
        _hasStats = false;
    }

    void RequestReadbacks()
    {
        if (!SystemInfo.supportsAsyncGPUReadback) return;

        AsyncGPUReadback.Request(_argsBuffer, request =>
        {
            if (_disposed || request.hasError) return;
            var data = request.GetData<uint>();
            if (data.Length >= 2)
                _lastEmittedInstances = Mathf.Min(_capacity, Mathf.Max(0, (int)data[1]));
        });
        AsyncGPUReadback.Request(_statsBuffer, request =>
        {
            if (_disposed || request.hasError) return;
            var data = request.GetData<uint>();
            int count = Mathf.Min(data.Length, _statsReadback.Length);
            for (int i = 0; i < count; i++) _statsReadback[i] = data[i];
            for (int i = count; i < _statsReadback.Length; i++) _statsReadback[i] = 0u;
            _hasStats = true;
        });
    }

    void Render(Camera camera)
    {
        if (_argsBuffer == null) return;
        var bounds = new Bounds(camera.transform.position, Vector3.one * (_drawDistance * 2f + 256f));
        var renderParams = new RenderParams(_material)
        {
            camera = camera,
            layer = _planetTransform != null ? _planetTransform.gameObject.layer : 0,
            matProps = _props,
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
        };
        Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _argsBuffer, 1, 0);
    }

    public GrassNearFieldStats GetGrassNearFieldStats()
    {
        return new GrassNearFieldStats
        {
            ControllerActive = !_disposed,
            ShaderAvailable = _material != null && _available,
            GridWidth = _lastGridWidth,
            GridHeight = _lastGridHeight,
            Spacing = _spacing,
            FullDensityDistance = _fullDensityDistance,
            DrawDistance = _drawDistance,
            FadeBand = _fadeBand,
            SuppressionRadius = SuppressionRadius,
            PageCellSize = _pageCellSize,
            PageOriginCellU = _lastPagedStartU,
            PageOriginCellV = _lastPagedStartV,
            FaceIndex = _lastFace,
            FacesActive = _lastFaceCount,
            MultiFaceDispatchEnabled = EnableMultiFaceDispatch,
            SeamRisk = _lastSeamRisk,
            LastDispatchReason = _lastDispatchReason,
            DispatchedThisFrame = _dispatchedThisFrame,
            DispatchesTotal = _dispatchesTotal,
            EmittedInstances = _lastEmittedInstances,
            VisualBladesPerInstance = VisualBladesPerInstance,
            BladeVertexCount = BladeVertexCount,
            CandidateCells = _hasStats ? _statsReadback[StatCandidateCells] : 0L,
            DensityRejectedCells = _hasStats ? _statsReadback[StatDensityRejected] : 0L,
            WaterRejectedCells = _hasStats ? _statsReadback[StatWaterRejected] : 0L,
            SlopeRejectedCells = _hasStats ? _statsReadback[StatSlopeRejected] : 0L,
            DistanceRejectedCells = _hasStats ? _statsReadback[StatDistanceRejected] : 0L,
            DistanceFadeRejectedCells = _hasStats ? _statsReadback[StatDistanceFadeRejected] : 0L,
            FrustumRejectedCells = _hasStats ? _statsReadback[StatFrustumRejected] : 0L,
            FaceAreaRejectedCells = _hasStats ? _statsReadback[StatFaceAreaRejected] : 0L,
            RangeBudgetRejectedCells = _hasStats ? _statsReadback[StatRangeBudgetRejected] : 0L,
            OverflowDropped = _hasStats ? _statsReadback[StatOverflow] : 0L,
            CapacityInstances = _capacity,
            BufferMegabytes = _bufferBytes / (1024f * 1024f),
        };
    }

    // Helpers moved to FaceSpaceCellRangeBuilder (slice 4b) - public statics:
    //   FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv)
    //   FaceSpaceCellRangeBuilder.DirectionToFaceUv(dir, out face, out uv)
    //   FaceSpaceCellRangeBuilder.ComputeMetersPerUV(face, faceUv, planetWorldRadius)
    //   FaceSpaceCellRangeBuilder.GetUniformWorldScale(transform)

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ServiceLocator.Unregister<IGrassNearFieldStatsProvider>(this);

        _instancesBuffer?.Dispose();
        _argsBuffer?.Dispose();
        _statsBuffer?.Dispose();
        _rangeCountsBuffer?.Dispose();

        if (_material != null)
        {
            if (Application.isPlaying) Object.Destroy(_material);
            else Object.DestroyImmediate(_material);
        }
    }
}
