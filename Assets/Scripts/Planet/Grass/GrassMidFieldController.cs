using UnityEngine;
using UnityEngine.Rendering;

// Camera-centered, face-space-stable impostor cards bridging near blades and the
// terrain blanket. One shared allocation covers every active cube face.
sealed class GrassMidFieldController : System.IDisposable, IGrassMidFieldStatsProvider
{
    const string ComputeResource = "GrassMidFieldPlace";
    const int StatsCount = 9;
    const int StatCandidateCells = 0;
    const int StatDensityRejected = 1;
    const int StatWaterRejected = 2;
    const int StatSlopeRejected = 3;
    const int StatDistanceRejected = 4;
    const int StatDistanceFadeRejected = 5;
    const int StatFaceAreaRejected = 6;
    const int StatEmitted = 7;
    const int StatOverflow = 8;
    const int ThreadGroupSize = 8;
    const int CardStride = sizeof(float) * 12;
    const int CardVertexCount = 6;

    const float DefaultSpacing = 2f;
    const float DefaultFullDensityDistance = 260f;
    const float DefaultDrawDistance = 450f;
    const float DefaultInnerFadeStart = 75f;
    const float DefaultInnerFadeEnd = 125f;
    const float DefaultOuterFadeBand = 100f;
    const float DefaultPageSizeMeters = 8f;
    const int DefaultCapacityInstances = 200_000;

    static readonly int[] BiomeIdsIds = new int[6];
    static readonly int[] BiomeWeightsIds = new int[6];
    static readonly int[] SurfaceRadiusIds = new int[6];
    static readonly int[] SurfaceNormalIds = new int[6];

    static readonly int BiomeGrassParamsId = Shader.PropertyToID("_MidFieldBiomeGrassParams");
    static readonly int InstancesId = Shader.PropertyToID("_MidFieldGrassInstances");
    static readonly int DrawArgsId = Shader.PropertyToID("_MidFieldDrawArgs");
    static readonly int StatsId = Shader.PropertyToID("_MidFieldStats");
    static readonly int BiomeAtlasResolutionId = Shader.PropertyToID("_MidFieldBiomeAtlasResolution");
    static readonly int SurfaceAtlasResolutionId = Shader.PropertyToID("_MidFieldSurfaceAtlasResolution");
    static readonly int BiomeParamCountId = Shader.PropertyToID("_MidFieldBiomeParamCount");
    static readonly int CapacityId = Shader.PropertyToID("_MidFieldCapacity");
    static readonly int SeedId = Shader.PropertyToID("_MidFieldSeed");
    static readonly int FaceIndexId = Shader.PropertyToID("_MidFieldFaceIndex");
    static readonly int GridStartCellUVId = Shader.PropertyToID("_MidFieldGridStartCellUV");
    static readonly int GridSizeId = Shader.PropertyToID("_MidFieldGridSize");
    static readonly int CellUvWidthId = Shader.PropertyToID("_MidFieldCellUvWidth");
    static readonly int SpacingId = Shader.PropertyToID("_MidFieldSpacing");
    static readonly int FullDensityDistanceId = Shader.PropertyToID("_MidFieldFullDensityDistance");
    static readonly int DrawDistanceId = Shader.PropertyToID("_MidFieldDrawDistance");
    static readonly int InnerFadeStartId = Shader.PropertyToID("_MidFieldInnerFadeStart");
    static readonly int InnerFadeEndId = Shader.PropertyToID("_MidFieldInnerFadeEnd");
    static readonly int OuterFadeBandId = Shader.PropertyToID("_MidFieldOuterFadeBand");
    static readonly int WaterRadiusId = Shader.PropertyToID("_MidFieldWaterRadius");
    static readonly int PlanetWorldScaleId = Shader.PropertyToID("_MidFieldPlanetWorldScale");
    static readonly int PlanetLocalToWorldId = Shader.PropertyToID("_MidFieldPlanetLocalToWorld");
    static readonly int PlanetCenterWsId = Shader.PropertyToID("_MidFieldPlanetCenterWs");
    static readonly int CameraPositionWsId = Shader.PropertyToID("_MidFieldCameraPositionWs");
    static readonly int InstancesShaderId = Shader.PropertyToID("_MidFieldGrassInstances");
    static readonly int PlanetCenterShaderId = Shader.PropertyToID("_PlanetCenter");

    static GrassMidFieldController()
    {
        for (int i = 0; i < 6; i++)
        {
            BiomeIdsIds[i] = Shader.PropertyToID($"_MidFieldBiomeIds_F{i}");
            BiomeWeightsIds[i] = Shader.PropertyToID($"_MidFieldBiomeWeights_F{i}");
            SurfaceRadiusIds[i] = Shader.PropertyToID($"_MidFieldSurfaceRadius_F{i}");
            SurfaceNormalIds[i] = Shader.PropertyToID($"_MidFieldSurfaceNormal_F{i}");
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
    readonly int _capacity = DefaultCapacityInstances;
    readonly float _spacing = DefaultSpacing;
    readonly float _fullDensityDistance = DefaultFullDensityDistance;
    readonly float _drawDistance = DefaultDrawDistance;
    readonly float _innerFadeStart = DefaultInnerFadeStart;
    readonly float _innerFadeEnd = DefaultInnerFadeEnd;
    readonly float _outerFadeBand = DefaultOuterFadeBand;
    readonly float _cellUvWidth;
    readonly int _pageCellSize;
    readonly Material _material;
    readonly ComputeShader _compute;
    readonly int _kernel;
    readonly GraphicsBuffer _instancesBuffer;
    readonly GraphicsBuffer _argsBuffer;
    readonly GraphicsBuffer _statsBuffer;
    readonly MaterialPropertyBlock _props;
    readonly bool _available;
    readonly long _bufferBytes;
    readonly uint[] _argsScratch = new uint[4];
    readonly uint[] _statsScratch = new uint[StatsCount];
    readonly uint[] _statsReadback = new uint[StatsCount];
    readonly FaceSpaceCell[] _rangeScratch = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];
    readonly FaceSpaceCell[] _lastRanges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    bool _disposed;
    bool _hasLastDispatch;
    bool _hasStats;
    bool _lastSeamRisk;
    bool _dispatchedThisFrame;
    int _lastRangeCount;
    int _lastFace;
    int _lastPageOriginU;
    int _lastPageOriginV;
    int _lastGridWidth;
    int _lastGridHeight;
    int _dispatchesTotal;
    int _lastEmittedInstances;
    GrassNearFieldDispatchReason _lastDispatchReason;

    public GrassMidFieldController(Transform planetTransform, ChunkedSurfaceProvider surfaceProvider,
        ComputeBuffer grassParamsBuffer, int grassParamCount,
        float waterRadius, float planetRadius, int seed, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceProvider = surfaceProvider;
        _grassParamsBuffer = grassParamsBuffer;
        _grassParamCount = Mathf.Max(0, grassParamCount);
        _waterRadius = waterRadius;
        _planetRadius = planetRadius;
        _seed = seed;
        _logger = logger ?? LoggerProvider.Get();

        float referenceMetersPerUv = Mathf.Max(0.0001f,
            2f * planetRadius * FaceSpaceCellRangeBuilder.GetUniformWorldScale(planetTransform));
        _cellUvWidth = Mathf.Max(1e-8f, _spacing / referenceMetersPerUv);
        _pageCellSize = Mathf.Max(1, Mathf.CeilToInt(DefaultPageSizeMeters / _spacing));

        Shader shader = Shader.Find("Planet/GrassMidField");
        if (shader == null)
        {
            _logger.Log(LogLevel.Warning, "GrassMid", "Planet/GrassMidField shader not found; mid-field disabled.");
            return;
        }
        _material = new Material(shader)
        {
            name = "Runtime MidField Grass Material",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (!SystemInfo.supportsComputeShaders)
        {
            _logger.Log(LogLevel.Warning, "GrassMid", "Compute shaders not supported; mid-field disabled.");
            return;
        }

        _compute = Resources.Load<ComputeShader>(ComputeResource);
        if (_compute == null)
        {
            _logger.Log(LogLevel.Warning, "GrassMid", $"Compute shader '{ComputeResource}' not found.");
            return;
        }

        try
        {
            _kernel = _compute.FindKernel("PlaceAndCullMidField");
        }
        catch (System.Exception ex)
        {
            _logger.Log(LogLevel.Warning, "GrassMid", $"Kernel lookup failed: {ex.Message}");
            return;
        }

        if (_grassParamsBuffer == null || _grassParamCount <= 0)
        {
            _logger.Log(LogLevel.Warning, "GrassMid", "Biome grass params unavailable; mid-field disabled.");
            return;
        }

        _instancesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, CardStride);
        _argsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
            4, sizeof(uint));
        _statsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, StatsCount, sizeof(uint));
        _props = new MaterialPropertyBlock();
        _props.SetBuffer(InstancesShaderId, _instancesBuffer);
        _props.SetVector(PlanetCenterShaderId, _planetTransform.position);
        _bufferBytes = (long)_capacity * CardStride
            + GraphicsBuffer.IndirectDrawArgs.size
            + (long)StatsCount * sizeof(uint);
        _available = true;
        ServiceLocator.Register<IGrassMidFieldStatsProvider>(this);

        _logger.Log(LogLevel.Debug, "GrassMid",
            $"Initialized: capacity={_capacity}, spacing={_spacing:F2}, draw={_drawDistance:F1}, "
            + $"innerFade={_innerFadeStart:F1}-{_innerFadeEnd:F1}, outerFade={_outerFadeBand:F1}, "
            + $"buffer={_bufferBytes / (1024f * 1024f):F1} MB");
    }

    public void Tick(Camera camera)
    {
        if (_disposed || !_available || camera == null || _material == null)
            return;

        _dispatchedThisFrame = false;
        FaceSpaceRangeResult result = FaceSpaceCellRangeBuilder.BuildRanges(
            camera, _planetTransform, _planetRadius,
            _drawDistance, _cellUvWidth, _pageCellSize, _rangeScratch);
        if (result.Count == 0)
            return;

        bool shouldDispatch = !_hasLastDispatch || result.Count != _lastRangeCount;
        if (!shouldDispatch)
        {
            for (int i = 0; i < result.Count; i++)
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
            : result.Count != _lastRangeCount || _rangeScratch[0].FaceIndex != _lastRanges[0].FaceIndex
                ? GrassNearFieldDispatchReason.FaceChanged
                : GrassNearFieldDispatchReason.PageChanged;

        if (shouldDispatch)
        {
            DispatchAllRanges(camera, result.Count);
            for (int i = 0; i < result.Count; i++)
                _lastRanges[i] = _rangeScratch[i];
            _hasLastDispatch = true;
            _lastRangeCount = result.Count;
            _lastFace = _rangeScratch[0].FaceIndex;
            _lastPageOriginU = _rangeScratch[0].PageOriginCellUV.x;
            _lastPageOriginV = _rangeScratch[0].PageOriginCellUV.y;
            _lastGridWidth = _rangeScratch[0].GridSize.x;
            _lastGridHeight = _rangeScratch[0].GridSize.y;
            _lastDispatchReason = reason;
            _lastSeamRisk = result.UncoveredCornerStraddle;
            _dispatchesTotal++;
            _dispatchedThisFrame = true;
        }
        else
        {
            _lastSeamRisk = result.UncoveredCornerStraddle;
        }

        Render(camera);
    }

    void DispatchAllRanges(Camera camera, int count)
    {
        ResetArgsAndStats();
        BindGlobalInputs();

        for (int i = 0; i < count; i++)
        {
            FaceSpaceCell range = _rangeScratch[i];
            if (range.GridSize.x <= 0 || range.GridSize.y <= 0)
                continue;
            DispatchRange(camera, range);
        }

        RequestReadbacks();
    }

    void BindGlobalInputs()
    {
        GrassSurfaceAtlasGpuData grassAtlases = _surfaceProvider.GrassSurfaceAtlases;
        int surfaceAtlasResolution = grassAtlases != null ? grassAtlases.AtlasResolution : 0;
        int biomeAtlasResolution = 0;

        for (int face = 0; face < 6; face++)
        {
            Texture2D radius = null;
            Texture2D normal = null;
            grassAtlases?.TryGetFace(face, out radius, out normal);
            _compute.SetTexture(_kernel, SurfaceRadiusIds[face],
                radius != null ? (Texture)radius : Texture2D.blackTexture);
            _compute.SetTexture(_kernel, SurfaceNormalIds[face],
                normal != null ? (Texture)normal : Texture2D.normalTexture);

            _surfaceProvider.TryGetFaceBiomeAtlases(face, out _, out Texture2D ids, out Texture2D weights);
            _compute.SetTexture(_kernel, BiomeIdsIds[face],
                ids != null ? (Texture)ids : Texture2D.blackTexture);
            _compute.SetTexture(_kernel, BiomeWeightsIds[face],
                weights != null ? (Texture)weights : Texture2D.blackTexture);
            if (ids != null && biomeAtlasResolution == 0)
                biomeAtlasResolution = ids.width;
        }

        _compute.SetBuffer(_kernel, BiomeGrassParamsId, _grassParamsBuffer);
        _compute.SetBuffer(_kernel, InstancesId, _instancesBuffer);
        _compute.SetBuffer(_kernel, DrawArgsId, _argsBuffer);
        _compute.SetBuffer(_kernel, StatsId, _statsBuffer);
        _compute.SetInt(BiomeAtlasResolutionId, Mathf.Max(1, biomeAtlasResolution));
        _compute.SetInt(SurfaceAtlasResolutionId, Mathf.Max(1, surfaceAtlasResolution));
        _compute.SetInt(BiomeParamCountId, _grassParamCount);
        _compute.SetInt(CapacityId, _capacity);
        _compute.SetInt(SeedId, _seed);
        _compute.SetFloat(SpacingId, _spacing);
        _compute.SetFloat(FullDensityDistanceId, _fullDensityDistance);
        _compute.SetFloat(DrawDistanceId, _drawDistance);
        _compute.SetFloat(InnerFadeStartId, _innerFadeStart);
        _compute.SetFloat(InnerFadeEndId, _innerFadeEnd);
        _compute.SetFloat(OuterFadeBandId, _outerFadeBand);
        _compute.SetFloat(WaterRadiusId, _waterRadius);
        _compute.SetFloat(PlanetWorldScaleId, FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform));
        _compute.SetMatrix(PlanetLocalToWorldId, _planetTransform.localToWorldMatrix);
    }

    void DispatchRange(Camera camera, FaceSpaceCell range)
    {
        _compute.SetInt(FaceIndexId, range.FaceIndex);
        _compute.SetInts(GridStartCellUVId, range.PageOriginCellUV.x, range.PageOriginCellUV.y);
        _compute.SetInts(GridSizeId, range.GridSize.x, range.GridSize.y);
        _compute.SetFloat(CellUvWidthId, range.CellUvWidth);
        _compute.SetVector(PlanetCenterWsId, _planetTransform.position);
        _compute.SetVector(CameraPositionWsId, camera.transform.position);

        int groupsX = Mathf.CeilToInt(range.GridSize.x / (float)ThreadGroupSize);
        int groupsY = Mathf.CeilToInt(range.GridSize.y / (float)ThreadGroupSize);
        _compute.Dispatch(_kernel, groupsX, groupsY, 1);
    }

    void ResetArgsAndStats()
    {
        _argsScratch[0] = CardVertexCount;
        _argsScratch[1] = 0;
        _argsScratch[2] = 0;
        _argsScratch[3] = 0;
        _argsBuffer.SetData(_argsScratch);

        for (int i = 0; i < StatsCount; i++)
            _statsScratch[i] = 0;
        _statsBuffer.SetData(_statsScratch);
        _hasStats = false;
    }

    void RequestReadbacks()
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
            return;

        AsyncGPUReadback.Request(_argsBuffer, request =>
        {
            if (_disposed || request.hasError)
                return;
            var data = request.GetData<uint>();
            if (data.Length >= 2)
                _lastEmittedInstances = Mathf.Min(_capacity, Mathf.Max(0, (int)data[1]));
        });

        AsyncGPUReadback.Request(_statsBuffer, request =>
        {
            if (_disposed || request.hasError)
                return;
            var data = request.GetData<uint>();
            int count = Mathf.Min(data.Length, _statsReadback.Length);
            for (int i = 0; i < count; i++)
                _statsReadback[i] = data[i];
            for (int i = count; i < _statsReadback.Length; i++)
                _statsReadback[i] = 0;
            _hasStats = true;
        });
    }

    void Render(Camera camera)
    {
        _props.SetVector(PlanetCenterShaderId, _planetTransform.position);
        var bounds = new Bounds(camera.transform.position, Vector3.one * (_drawDistance * 2f + 256f));
        var renderParams = new RenderParams(_material)
        {
            camera = camera,
            layer = _planetTransform.gameObject.layer,
            matProps = _props,
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
        };
        Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _argsBuffer, 1, 0);
    }

    public GrassMidFieldStats GetGrassMidFieldStats()
    {
        return new GrassMidFieldStats
        {
            ControllerActive = !_disposed,
            ShaderAvailable = _available && _material != null,
            GridWidth = _lastGridWidth,
            GridHeight = _lastGridHeight,
            Spacing = _spacing,
            FullDensityDistance = _fullDensityDistance,
            DrawDistance = _drawDistance,
            InnerFadeStart = _innerFadeStart,
            InnerFadeEnd = _innerFadeEnd,
            OuterFadeBand = _outerFadeBand,
            PageCellSize = _pageCellSize,
            PageOriginCellU = _lastPageOriginU,
            PageOriginCellV = _lastPageOriginV,
            FaceIndex = _lastFace,
            FacesActive = _lastRangeCount,
            SeamRisk = _lastSeamRisk,
            LastDispatchReason = _lastDispatchReason,
            DispatchedThisFrame = _dispatchedThisFrame,
            DispatchesTotal = _dispatchesTotal,
            EmittedInstances = _lastEmittedInstances,
            CandidateCells = _hasStats ? _statsReadback[StatCandidateCells] : 0,
            DensityRejectedCells = _hasStats ? _statsReadback[StatDensityRejected] : 0,
            WaterRejectedCells = _hasStats ? _statsReadback[StatWaterRejected] : 0,
            SlopeRejectedCells = _hasStats ? _statsReadback[StatSlopeRejected] : 0,
            DistanceRejectedCells = _hasStats ? _statsReadback[StatDistanceRejected] : 0,
            DistanceFadeRejectedCells = _hasStats ? _statsReadback[StatDistanceFadeRejected] : 0,
            FaceAreaRejectedCells = _hasStats ? _statsReadback[StatFaceAreaRejected] : 0,
            OverflowDropped = _hasStats ? _statsReadback[StatOverflow] : 0,
            CapacityInstances = _capacity,
            BufferMegabytes = _bufferBytes / (1024f * 1024f),
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ServiceLocator.Unregister<IGrassMidFieldStatsProvider>(this);
        _instancesBuffer?.Dispose();
        _argsBuffer?.Dispose();
        _statsBuffer?.Dispose();

        if (_material != null)
        {
            if (Application.isPlaying)
                Object.Destroy(_material);
            else
                Object.DestroyImmediate(_material);
        }
    }
}
