using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

[CommandPrefix("planet")]
public class Planet : MonoBehaviour, IPlanet, IPlanetSurfaceSampler, IPlanetSurfaceRaycaster,
    IGrassRuntimeControl, ILateInitialize, IProgressReporter
{
    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256), FormerlySerializedAs("Resolution"),
     Tooltip("Per-face vertex resolution when PlanetSettings.Resolution == Low.")]
    public int PerFaceResolution = 10;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    [SerializeField] PlanetSettings _planetSettings;

    public PlanetSettings PlanetSettingsAsset => _planetSettings;

    // Read reflectively by PlanetEditor through SerializedObject.FindProperty.
#pragma warning disable CS0414
    [SerializeField, HideInInspector] bool _settingsFoldout = true;
#pragma warning restore CS0414
    [SerializeField, HideInInspector] float _lastGeneratedRadius;
    [SerializeField, HideInInspector] float _lastSeaLevelRadius;
    [SerializeField, HideInInspector] float _lastElevationMin;
    [SerializeField, HideInInspector] float _lastElevationMax;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    IPlanetSurfaceProvider _surfaceProvider;
    // Typed reference to the Low-mode provider for legacy color iteration over TerrainFaces.
    // Null when running under chunked or GPU surface providers.
    PerFaceSurfaceProvider _perFaceProvider;
    GrassPlacementController _grassController;
    GrassNearFieldController _grassNearFieldController;
    bool _grassEnabled = true;
    bool _nearFieldGrassEnabled = true;
    bool _chunkGrassEnabled = true;
    bool _grassBlanketEnabled = true;
    GameObject _waterObject;
    Material _waterMaterial;

    static readonly int _shallowColorId = Shader.PropertyToID("_ShallowColor");
    static readonly int _deepColorId = Shader.PropertyToID("_DeepColor");
    static readonly int _foamColorId = Shader.PropertyToID("_FoamColor");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveScaleId = Shader.PropertyToID("_WaveScale");
    static readonly int _waveSpeedId = Shader.PropertyToID("_WaveSpeed");
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _sunGlitterPowerId = Shader.PropertyToID("_SunGlitterPower");
    static readonly int _shoreFoamIntensityId = Shader.PropertyToID("_ShoreFoamIntensity");
    static readonly int _whitecapIntensityId = Shader.PropertyToID("_WhitecapIntensity");
    static readonly int _wakeFoamIntensityId = Shader.PropertyToID("_WakeFoamIntensity");
    static readonly int _wakeNormalStrengthId = Shader.PropertyToID("_WakeNormalStrength");
    static readonly int _oceanFocusModeId = Shader.PropertyToID("_OceanFocusMode");
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");
    static readonly int _freezingEnabledId = Shader.PropertyToID("_FreezingEnabled");
    static readonly int _lakeFreezeStartId = Shader.PropertyToID("_LakeFreezeStart");
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID("_LakeFreezeComplete");
    static readonly int _oceanFreezeStartId = Shader.PropertyToID("_OceanFreezeStart");
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID("_OceanFreezeComplete");
    static readonly int _iceTintId = Shader.PropertyToID("_IceTint");
    static readonly int _iceOpacityId = Shader.PropertyToID("_IceOpacity");
    static readonly int _iceRoughnessId = Shader.PropertyToID("_IceRoughness");
    static readonly int _iceNormalStrengthId = Shader.PropertyToID("_IceNormalStrength");
    static readonly int _iceBreakupScaleId = Shader.PropertyToID("_IceBreakupScale");
    static readonly int _frozenWaterBodiesId = Shader.PropertyToID("_FrozenWaterBodies");
    static readonly int _partiallyFrozenWaterBodiesId = Shader.PropertyToID("_PartiallyFrozenWaterBodies");
    static readonly int _liquidWaterBodiesId = Shader.PropertyToID("_LiquidWaterBodies");
    static readonly int _grassFarOverlayStrengthId = Shader.PropertyToID("_GrassFarOverlayStrength");
    static readonly int _grassFarOverlayStartId = Shader.PropertyToID("_GrassFarOverlayStart");
    static readonly int _grassFarOverlayEndId = Shader.PropertyToID("_GrassFarOverlayEnd");
    static readonly int _grassFarOverlayNoiseScaleId = Shader.PropertyToID("_GrassFarOverlayNoiseScale");
    static readonly int _grassFarOverlayOrbitStrengthId = Shader.PropertyToID("_GrassFarOverlayOrbitStrength");
    static readonly int _grassFarOverlayAltitudeStartId = Shader.PropertyToID("_GrassFarOverlayAltitudeStart");
    static readonly int _grassFarOverlayAltitudeEndId = Shader.PropertyToID("_GrassFarOverlayAltitudeEnd");
    static readonly int _grassFarOverlayFiberStrengthId = Shader.PropertyToID("_GrassFarOverlayFiberStrength");
    static readonly int _grassFarOverlayColorBlendId = Shader.PropertyToID("_GrassFarOverlayColorBlend");
    static readonly int _grassMidOverlayTerrainStrengthId = Shader.PropertyToID("_GrassMidOverlayTerrainStrength");
    static readonly int _terrainOverrideEnabledId = Shader.PropertyToID("_TerrainOverrideEnabled");
    static readonly int _coastSliceId = Shader.PropertyToID("_CoastSlice");
    static readonly int _coastBelowSeaDepthId = Shader.PropertyToID("_CoastBelowSeaDepth");
    static readonly int _coastStartHeightId = Shader.PropertyToID("_CoastStartHeight");
    static readonly int _coastEndHeightId = Shader.PropertyToID("_CoastEndHeight");
    static readonly int _coastTilingId = Shader.PropertyToID("_CoastTiling");
    static readonly int _slopeSliceId = Shader.PropertyToID("_SlopeSlice");
    static readonly int _slopeStartDegreesId = Shader.PropertyToID("_SlopeStartDegrees");
    static readonly int _slopeFullDegreesId = Shader.PropertyToID("_SlopeFullDegrees");
    static readonly int _slopeTilingId = Shader.PropertyToID("_SlopeTiling");
    static readonly int _snowSliceId = Shader.PropertyToID("_SnowSlice");
    static readonly int _snowFullTemperatureId = Shader.PropertyToID("_SnowFullTemperature");
    static readonly int _snowFadeEndTemperatureId = Shader.PropertyToID("_SnowFadeEndTemperature");
    static readonly int _snowTilingId = Shader.PropertyToID("_SnowTiling");

    static Shader _vcShader;
    static Shader _oceanShader;
    static Shader _urpLitShader;
    static Shader _standardShader;

    static readonly Color _waterShallowBaseColor = new Color(0.20f, 0.76f, 0.82f, 1f);
    static readonly Color _waterDeepBaseColor = new Color(0.00f, 0.018f, 0.065f, 1f);
    static readonly Color _waterFoamColor = new Color(0.88f, 0.98f, 0.94f, 0.9f);

    const float WaterReferenceRadius = 5000f;
    const float WaterShallowDepth = 28f;
    const float WaterDeepDepth = 360f;
    const float WaterShoreFoamDepth = 32f;
    const float WaterShoreRange = 125f;
    const float WaterWaveAmplitude = 3.4f;
    const float WaterWaveScale = 480f;
    const float WaterWaveSpeed = 0.58f;
    const float WaterWaveNormalStrength = 4.5f;
    const float WaterMotionStrength = 0.24f;
    const float WaterSunGlitterIntensity = 1.45f;
    const float WaterSunGlitterPower = 1400f;
    const float WaterShoreFoamIntensity = 1.0f;
    const float WaterWhitecapIntensity = 1.08f;
    const float WaterWakeFoamIntensity = 1.0f;
    const float WaterWakeNormalStrength = 1.0f;
    const float WaterAlpha = 0.36f;
    const float WaterShallowColorBlend = 0.68f;
    const float WaterDeepColorBlend = 0.88f;
    const float WaterShallowAlphaFactor = 0.14f;
    const float WaterShallowAlphaMin = 0.10f;
    const float WaterDeepAlphaMin = 0.96f;
    const float GrassFarOverlayStrength = 1.0f;
    const float GrassFarOverlayStart = 35f;
    const float GrassFarOverlayEnd = 260f;
    const float GrassFarOverlayNoiseScale = 0.055f;
    const float GrassFarOverlayOrbitStrength = 0.42f;
    const float GrassFarOverlayAltitudeStart = 750f;
    const float GrassFarOverlayAltitudeEnd = 2600f;
    const float GrassFarOverlayFiberStrength = 0.65f;
    const float GrassFarOverlayColorBlend = 0.98f;
    const float GrassMidOverlayTerrainStrength = 0.92f;
    const float NearFieldGrassActivationAltitude = 350f;
    const float NearFieldGrassDeactivationAltitude = 500f;

    CancellationTokenSource _cts;
    bool _isGenerating;
    readonly ProgressHandle _progressHandle = new ProgressHandle();

    public bool IsGenerating => _isGenerating;
    public ShapeGenerator ShapeGenerator => _shapeGenerator;
    public float LastGeneratedRadius => _lastGeneratedRadius;
    public float LastSeaLevelRadius => _lastSeaLevelRadius;
    public int Seed { get; private set; }
    public Transform Transform => transform;
    public string ReporterName => "Planet";
    public int StepCount => 5;
    public IProgressHandle ProgressHandle => _progressHandle;

    ILogger Logger => LoggerProvider.Get();

    public int LatePriority => 0;

    void Awake()
    {
        if (_vcShader == null) _vcShader = Shader.Find("Planet/VertexColor");
        if (_oceanShader == null) _oceanShader = Shader.Find("Planet/Ocean");
        if (_urpLitShader == null) _urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (_standardShader == null) _standardShader = Shader.Find("Standard");

        ServiceLocator.Register<IPlanet>(this);
        ServiceLocator.Register<IPlanetSurfaceSampler>(this);
        ServiceLocator.Register<IPlanetSurfaceRaycaster>(this);
        ServiceLocator.Register<IGrassRuntimeControl>(this);
    }

    void OnDestroy()
    {
        ServiceLocator.Unregister<IPlanet>(this);
        ServiceLocator.Unregister<IPlanetSurfaceSampler>(this);
        ServiceLocator.Unregister<IPlanetSurfaceRaycaster>(this);
        ServiceLocator.Unregister<IGrassRuntimeControl>(this);
        _cts?.Cancel();
        _cts?.Dispose();
        _grassNearFieldController?.Dispose();
        _grassNearFieldController = null;
        _grassController?.Dispose();
        _grassController = null;
        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
        _colorGenerator?.Dispose();
        if (_waterMaterial != null)
        {
            Destroy(_waterMaterial);
            _waterMaterial = null;
        }
    }

    // Camera reference cached so we don't pay Camera.main's GameObject.FindWithTag every frame.
    Camera _observerCamera;

    void Update()
    {
        if (_surfaceProvider == null || _isGenerating) return;
        if (_observerCamera == null || !_observerCamera.isActiveAndEnabled)
            _observerCamera = Camera.main;
        if (_observerCamera == null) return;
        _surfaceProvider.Tick(_observerCamera.transform.position, _observerCamera);
        UpdateGrassControllerActivation(_observerCamera);
        _grassController?.Tick(_observerCamera);
        _grassNearFieldController?.Tick(_observerCamera);
    }

    void Initialize()
    {
        _grassNearFieldController?.Dispose();
        _grassNearFieldController = null;
        _grassController?.Dispose();
        _grassController = null;
        DestroyChildren();

        ISeedProvider seedProvider = ServiceLocator.Get<ISeedProvider>();
        Seed = seedProvider.GetSeedForSystem("Planet");

        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
        _waterObject = null;

        var shapeSettings = _planetSettings.BuildShapeSettings();
        _shapeGenerator.Configure(shapeSettings);
        _shapeGenerator.Initialize(Seed);
        _colorGenerator.Configure(_planetSettings.BiomeSettings);
        _colorGenerator.Initialize(
            Seed,
            seedProvider.GetSeedForSystem("BiomeVoronoi"));

        ConfigureMaterial();

        switch (_planetSettings.Resolution)
        {
            case PlanetResolution.Low:
                _perFaceProvider = new PerFaceSurfaceProvider(
                    transform, _shapeGenerator, PerFaceResolution, _planetSettings.PlanetMaterial, RenderMask);
                _surfaceProvider = _perFaceProvider;
                break;
            case PlanetResolution.High:
                _surfaceProvider = new ChunkedSurfaceProvider(
                    transform, _shapeGenerator, _planetSettings.PlanetMaterial, RenderMask,
                    _planetSettings.MaxChunkDepth);
                // Pre-cache mode: all chunks at all depths <= MaxChunkDepth are generated up
                // front during the loading bar. Runtime Tick is a cheap visibility filter; no
                // mesh jobs run at runtime. Per-vertex colors stay disabled until Phase B.
                break;
            default:
                throw new System.ArgumentOutOfRangeException();
        }
    }

    void DestroyChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }


    void ConfigureMaterial()
    {
        var mat = _planetSettings.PlanetMaterial;
        if (mat == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "PlanetMaterial is not assigned in PlanetSettings.");
            return;
        }
        if (mat.shader.name != "Planet/VertexColor")
        {
            if (_vcShader != null) mat.shader = _vcShader;
        }
        mat.SetFloat("_Smoothness", 0f);
        ApplyGrassBlanketState(mat);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayStartId, GrassFarOverlayStart);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayEndId, GrassFarOverlayEnd);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayNoiseScaleId, GrassFarOverlayNoiseScale);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayOrbitStrengthId, GrassFarOverlayOrbitStrength);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeStartId, GrassFarOverlayAltitudeStart);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeEndId, GrassFarOverlayAltitudeEnd);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayFiberStrengthId, GrassFarOverlayFiberStrength);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayColorBlendId, GrassFarOverlayColorBlend);
        SetMaterialFloatIfPresent(mat, _grassMidOverlayTerrainStrengthId, GrassMidOverlayTerrainStrength);
        ConfigureTerrainSurfaceOverrides(mat);
    }

    void ConfigureTerrainSurfaceOverrides(Material mat)
    {
        PlanetSettings.TerrainSurfaceOverrideSettings settings = _planetSettings.SurfaceOverrides;
        BiomeRegistry registry = _planetSettings.BiomeSettings != null
            ? _planetSettings.BiomeSettings.Registry
            : null;

        SetMaterialAndGlobalFloat(mat, _terrainOverrideEnabledId,
            settings != null && settings.Enabled && registry != null ? 1f : 0f);
        if (settings == null || registry == null)
            return;

        SetMaterialAndGlobalInt(mat, _coastSliceId, registry.GetSliceIdForBiomeType(BiomeType.Beach));
        SetMaterialAndGlobalFloat(mat, _coastBelowSeaDepthId, Mathf.Max(0f, settings.CoastBelowSeaDepth));
        SetMaterialAndGlobalFloat(mat, _coastStartHeightId, Mathf.Max(0f, settings.CoastStartHeight));
        SetMaterialAndGlobalFloat(mat, _coastEndHeightId,
            Mathf.Max(settings.CoastEndHeight, settings.CoastStartHeight + 0.01f));
        SetMaterialAndGlobalFloat(mat, _coastTilingId, Mathf.Max(0.001f, settings.CoastTiling));

        SetMaterialAndGlobalInt(mat, _slopeSliceId, registry.GetSliceIdForBiomeType(BiomeType.Mountain));
        float slopeStart = Mathf.Clamp(settings.SlopeStartDegrees, 0f, 89.99f);
        SetMaterialAndGlobalFloat(mat, _slopeStartDegreesId,
            slopeStart);
        SetMaterialAndGlobalFloat(mat, _slopeFullDegreesId,
            Mathf.Clamp(Mathf.Max(settings.SlopeFullDegrees, slopeStart + 0.01f), slopeStart + 0.01f, 90f));
        SetMaterialAndGlobalFloat(mat, _slopeTilingId, Mathf.Max(0.001f, settings.SlopeTiling));

        SetMaterialAndGlobalInt(mat, _snowSliceId, registry.GetSliceIdForBiomeType(BiomeType.Snow));
        float snowFull = Mathf.Clamp(settings.SnowFullTemperature01, 0f, 0.999f);
        SetMaterialAndGlobalFloat(mat, _snowFullTemperatureId,
            snowFull);
        SetMaterialAndGlobalFloat(mat, _snowFadeEndTemperatureId,
            Mathf.Clamp01(Mathf.Max(settings.SnowFadeEndTemperature01,
                snowFull + 0.001f)));
        SetMaterialAndGlobalFloat(mat, _snowTilingId, Mathf.Max(0.001f, settings.SnowTiling));
    }

    static void SetMaterialFloatIfPresent(Material mat, int propertyId, float value)
    {
        if (!mat.HasProperty(propertyId)) return;
        mat.SetFloat(propertyId, value);
    }

    static void SetMaterialAndGlobalFloat(Material mat, int propertyId, float value)
    {
        SetMaterialFloatIfPresent(mat, propertyId, value);
        Shader.SetGlobalFloat(propertyId, value);
    }

    static void SetMaterialAndGlobalInt(Material mat, int propertyId, int value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetInt(propertyId, value);
        Shader.SetGlobalInt(propertyId, value);
    }

    public async Awaitable LateInitialize(CancellationToken cancellationToken)
    {
        await GeneratePlanetAsync(cancellationToken);
    }

    /// <summary>
    /// Generates the planet mesh, colors, and water. Safe to call directly for editor/runtime
    /// regeneration; LoadingManager calls this via <see cref="LateInitialize"/>.
    /// </summary>
    public async Awaitable GeneratePlanetAsync(CancellationToken externalToken = default)
    {
        if (_planetSettings == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "PlanetSettings is not assigned.");
            return;
        }

        if (_isGenerating) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        CancellationTokenSource linkedCts = null;
        CancellationToken ct = _cts.Token;
        if (externalToken.CanBeCanceled)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _cts.Token);
            ct = linkedCts.Token;
        }

        try
        {
            _isGenerating = true;
            _progressHandle.Report(0f, "Initializing planet...");
            Initialize();
            _progressHandle.Report(0.1f, "Generating terrain...");
            await GenerateMeshAsync(new ProgressRangeHandle(_progressHandle, 0.1f, 0.7f), ct);
            if (this == null) return;
            _shapeGenerator.CommitElevationRange();
            _progressHandle.Report(0.8f, "Applying colors...");
            await GenerateColorsAsync(new ProgressRangeHandle(_progressHandle, 0.8f, 0.1f), ct);
            if (this == null) return;
            _progressHandle.Report(0.9f, "Generating water...");
            await GenerateWaterAsync(new ProgressRangeHandle(_progressHandle, 0.9f, 0.1f), ct);
            ConfigureGrassController();
            // Atmosphere is rendered by AtmosphereController + AtmosphereRenderFeature (post-process).

            float scaledRadius = _planetSettings.PlanetRadius * (1 + _shapeGenerator.ElevationMax);
            float seaLevelRadius = _planetSettings.PlanetRadius * (1 + _planetSettings.OceanLevel);
            _lastGeneratedRadius = scaledRadius;
            _lastSeaLevelRadius = seaLevelRadius;
            _lastElevationMin = _shapeGenerator.ElevationMin;
            _lastElevationMax = _shapeGenerator.ElevationMax;
            _progressHandle.Report(1f, "Planet ready");
            await Awaitable.NextFrameAsync(ct);
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius, seaLevelRadius, _lastElevationMin, _lastElevationMax));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, mode {_planetSettings.Resolution}, perFaceResolution {PerFaceResolution}, radius {scaledRadius:F1}");
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            Logger.LogException("Planet", ex);
            throw;
        }
        finally
        {
            _isGenerating = false;
            linkedCts?.Dispose();
        }
    }

    async Awaitable GenerateMeshAsync(IProgressHandle progress, CancellationToken ct)
    {
        await _surfaceProvider.GenerateAsync(progress, ct);
    }

    void GenerateColors()
    {
        if (_perFaceProvider == null) return;
        foreach (var face in _perFaceProvider.TerrainFaces)
            face.UpdateColors(_colorGenerator);
    }

    async Awaitable GenerateColorsAsync(IProgressHandle progress, CancellationToken ct)
    {
        if (_surfaceProvider == null) return;
        await _surfaceProvider.GenerateColorsAsync(_colorGenerator, progress, ct);
    }

    void ConfigureGrassController()
    {
        _grassNearFieldController?.Dispose();
        _grassNearFieldController = null;
        _grassController?.Dispose();
        _grassController = null;

        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return;

        float waterRadius = _planetSettings.HasOceans
            ? _planetSettings.PlanetRadius * (1f + _planetSettings.OceanLevel)
            : -1f;
        if (_grassEnabled && _chunkGrassEnabled)
            CreateChunkGrassController(chunkedProvider, waterRadius);

        if (_grassEnabled && _nearFieldGrassEnabled
            && _observerCamera != null
            && ShouldActivateNearFieldGrass(_observerCamera.transform.position, false))
        {
            CreateNearFieldGrassController(chunkedProvider, waterRadius);
        }

        ApplyGrassBlanketState(_planetSettings.PlanetMaterial);
    }

    void UpdateGrassControllerActivation(Camera camera)
    {
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider || camera == null)
            return;

        float waterRadius = _planetSettings.HasOceans
            ? _planetSettings.PlanetRadius * (1f + _planetSettings.OceanLevel)
            : -1f;

        if (_grassEnabled && _chunkGrassEnabled)
        {
            if (_grassController == null)
                CreateChunkGrassController(chunkedProvider, waterRadius);
        }
        else if (_grassController != null)
        {
            _grassController.Dispose();
            _grassController = null;
        }

        bool nearFieldShouldBeActive = _grassEnabled
            && _nearFieldGrassEnabled
            && ShouldActivateNearFieldGrass(camera.transform.position, _grassNearFieldController != null);
        if (nearFieldShouldBeActive)
        {
            if (_grassNearFieldController == null)
                CreateNearFieldGrassController(chunkedProvider, waterRadius);
        }
        else if (_grassNearFieldController != null)
        {
            _grassNearFieldController.Dispose();
            _grassNearFieldController = null;
        }
    }

    bool ShouldActivateNearFieldGrass(Vector3 cameraPosition, bool currentlyActive)
    {
        Vector3 fromCenter = cameraPosition - transform.position;
        if (fromCenter.sqrMagnitude < 0.0001f)
            return true;

        if (!TryGetSurfaceRadius(fromCenter.normalized, out float surfaceRadius))
            return currentlyActive;

        float altitude = Mathf.Max(0f, fromCenter.magnitude - surfaceRadius);
        float threshold = currentlyActive
            ? NearFieldGrassDeactivationAltitude
            : NearFieldGrassActivationAltitude;
        return altitude <= threshold;
    }

    void CreateChunkGrassController(ChunkedSurfaceProvider chunkedProvider, float waterRadius)
    {
        _grassController = new GrassPlacementController(transform, chunkedProvider,
            _colorGenerator.SurfaceArrays.GrassParamsBuffer, _colorGenerator.SurfaceArrays.SliceCount,
            waterRadius, Seed, Logger);
    }

    void CreateNearFieldGrassController(ChunkedSurfaceProvider chunkedProvider, float waterRadius)
    {
        _grassNearFieldController = new GrassNearFieldController(transform, chunkedProvider,
            _colorGenerator.SurfaceArrays.GrassParamsBuffer, _colorGenerator.SurfaceArrays.SliceCount,
            waterRadius, _planetSettings.PlanetRadius, Seed, Logger);
    }

    void ApplyGrassBlanketState(Material mat)
    {
        if (mat == null)
            return;
        float strength = _grassEnabled && _grassBlanketEnabled ? GrassFarOverlayStrength : 0f;
        SetMaterialFloatIfPresent(mat, _grassFarOverlayStrengthId, strength);
    }

    public GrassRuntimeState GetGrassRuntimeState()
    {
        return new GrassRuntimeState
        {
            MasterEnabled = _grassEnabled,
            NearFieldRequested = _nearFieldGrassEnabled,
            NearFieldActive = _grassNearFieldController != null,
            ChunkPathRequested = _chunkGrassEnabled,
            ChunkPathActive = _grassController != null,
            BlanketRequested = _grassBlanketEnabled,
            BlanketActive = _grassEnabled && _grassBlanketEnabled,
        };
    }

    public void SetGrassEnabled(bool enabled)
    {
        _grassEnabled = enabled;
        ApplyGrassBlanketState(_planetSettings != null ? _planetSettings.PlanetMaterial : null);
        if (!enabled)
        {
            _grassNearFieldController?.Dispose();
            _grassNearFieldController = null;
            _grassController?.Dispose();
            _grassController = null;
        }
    }

    public void SetGrassLayerEnabled(GrassRenderLayer layer, bool enabled)
    {
        switch (layer)
        {
            case GrassRenderLayer.Near:
                _nearFieldGrassEnabled = enabled;
                if (!enabled)
                {
                    _grassNearFieldController?.Dispose();
                    _grassNearFieldController = null;
                }
                break;
            case GrassRenderLayer.Chunk:
                _chunkGrassEnabled = enabled;
                if (!enabled)
                {
                    _grassController?.Dispose();
                    _grassController = null;
                }
                break;
            case GrassRenderLayer.Blanket:
                _grassBlanketEnabled = enabled;
                ApplyGrassBlanketState(_planetSettings != null ? _planetSettings.PlanetMaterial : null);
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(layer), layer, null);
        }
    }

    public bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius)
    {
        surfaceRadius = 0f;

        if (_surfaceProvider == null || worldUnitDirection.sqrMagnitude < 0.0001f)
            return false;

        // Provider operates in planet-local space; Planet wraps with transform math so the
        // provider stays Unity-transform-agnostic.
        Vector3 localDirection = transform.InverseTransformDirection(worldUnitDirection).normalized;
        if (!_surfaceProvider.TryGetLocalSurfaceRadius(localDirection, out float localRadius))
            return false;

        float scale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        surfaceRadius = localRadius * Mathf.Max(scale, 0.0001f);
        return true;
    }

    public bool TryRaycastSurface(Ray worldRay, float maxDistance, out PlanetSurfaceRaycastHit hit)
    {
        hit = default;
        if (_surfaceProvider is not ChunkedSurfaceProvider chunkedProvider)
            return false;

        float scale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        scale = Mathf.Max(scale, 0.0001f);
        Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
        Vector3 localDirection = transform.InverseTransformDirection(worldRay.direction).normalized;
        if (localDirection.sqrMagnitude < 0.0001f)
            return false;

        float localMaxDistance = Mathf.Max(0f, maxDistance) / scale;
        if (!chunkedProvider.TryRaycastVisibleSurface(new Ray(localOrigin, localDirection), localMaxDistance,
                out Vector3 localPoint, out Vector3 localNormal, out _))
            return false;

        Vector3 worldPoint = transform.TransformPoint(localPoint);
        float worldDistance = Vector3.Distance(worldRay.origin, worldPoint);
        if (maxDistance > 0f && worldDistance > maxDistance)
            return false;

        Vector3 worldNormal = transform.TransformDirection(localNormal).normalized;
        if (worldNormal.sqrMagnitude < 0.0001f)
            worldNormal = (worldPoint - transform.position).normalized;

        hit = new PlanetSurfaceRaycastHit
        {
            Point = worldPoint,
            Normal = worldNormal,
            Distance = worldDistance,
            SurfaceRadius = Vector3.Distance(worldPoint, transform.position),
        };
        return true;
    }

    async Awaitable GenerateWaterAsync(IProgressHandle progress, CancellationToken ct)
    {
        Shader.SetGlobalInt(_frozenWaterBodiesId, 0);
        Shader.SetGlobalInt(_partiallyFrozenWaterBodiesId, 0);
        Shader.SetGlobalInt(_liquidWaterBodiesId, 0);

        if (!_planetSettings.HasOceans)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }

        if (_waterObject == null)
        {
            _waterObject = new GameObject("Water");
            _waterObject.transform.parent = transform;
            _waterObject.transform.localPosition = Vector3.zero;
            var waterRenderer = _waterObject.AddComponent<MeshRenderer>();
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            _waterObject.AddComponent<MeshFilter>();
        }

        _waterObject.SetActive(true);
        _waterObject.transform.localScale = Vector3.one;
        _waterObject.transform.localPosition = Vector3.zero;

        var meshFilter = _waterObject.GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "WaterBodies" };

        float waterScale = GetWaterDistanceScale();
        PlanetSettings.FrozenWaterSettings frozenWater = _planetSettings.FrozenWater
            ?? new PlanetSettings.FrozenWaterSettings();
        var buildSettings = new WaterMeshBuilder.Settings
        {
            PlanetRadius = _planetSettings.PlanetRadius,
            OceanLevel = _planetSettings.OceanLevel,
            DeepDepth = WaterDeepDepth * waterScale,
            ShoreRange = WaterShoreRange * waterScale,
            SurfaceOffset = Mathf.Max(_planetSettings.PlanetRadius * 0.00003f, 0.02f),
            OceanBodyVertexThreshold = Mathf.Max(48, PerFaceResolution * PerFaceResolution / 28),
            ClimateProvider = _colorGenerator.ClimateProvider,
            EnableFreezing = frozenWater.Enabled,
            LakeFreezeStartTemperature01 = frozenWater.LakeFreezeStartTemperature01,
            LakeFreezeCompleteTemperature01 = frozenWater.LakeFreezeCompleteTemperature01,
            OceanFreezeStartTemperature01 = frozenWater.OceanFreezeStartTemperature01,
            OceanFreezeCompleteTemperature01 = frozenWater.OceanFreezeCompleteTemperature01
        };
        // Water builder reads per-face vertex/elevation grids via IFaceMeshSampler. Both
        // resolution modes (Low/High) supply this view; chunked path wraps each root chunk.
        var faceSamplers = _surfaceProvider?.GetFaceMeshSamplers();
        if (faceSamplers == null || faceSamplers.Count == 0)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }
        var terrainFaces = new IFaceMeshSampler[faceSamplers.Count];
        for (int i = 0; i < faceSamplers.Count; i++) terrainFaces[i] = faceSamplers[i];
        var waterMesh = meshFilter.sharedMesh;

        // Run the (pure-CPU) water build on a worker and poll its progress each frame on the main
        // thread so the loading bar advances through the heavy global-graph + per-face phases.
        progress?.Report(0f, "Building water bodies...");
        float buildProgress = 0f;
        var computeTask = Task.Run(
            () => WaterMeshBuilder.Compute(terrainFaces, buildSettings,
                p => System.Threading.Volatile.Write(ref buildProgress, p)), ct);
        while (!computeTask.IsCompleted)
        {
            progress?.Report(0.6f * System.Threading.Volatile.Read(ref buildProgress), "Building water bodies...");
            await Awaitable.NextFrameAsync(ct);
        }
        var waterMeshData = await computeTask;
        if (this == null) return;
        progress?.Report(0.7f, "Uploading water mesh...");

        if (waterMeshData.Stats.Triangles == 0)
        {
            _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }

        WaterMeshBuilder.Apply(waterMesh, null, waterMeshData);
        Shader.SetGlobalInt(_frozenWaterBodiesId, waterMeshData.Stats.FrozenBodies);
        Shader.SetGlobalInt(_partiallyFrozenWaterBodiesId, waterMeshData.Stats.PartiallyFrozenBodies);
        Shader.SetGlobalInt(_liquidWaterBodiesId, waterMeshData.Stats.LiquidBodies);
        progress?.Report(0.9f, "Configuring water...");

        Logger.Log(LogLevel.Debug, "Water",
            $"Generated water mesh: {waterMeshData.Stats.MeshVertices} verts, {waterMeshData.Stats.Triangles} tris, " +
            $"wet terrain verts {waterMeshData.Stats.WetVertices}, ocean bodies {waterMeshData.Stats.OceanBodies}, " +
            $"small bodies {waterMeshData.Stats.SmallBodies}, frozen/partial/liquid " +
            $"{waterMeshData.Stats.FrozenBodies}/{waterMeshData.Stats.PartiallyFrozenBodies}/{waterMeshData.Stats.LiquidBodies}, " +
            $"water temp {waterMeshData.Stats.MinWaterTemperature01:F3}-{waterMeshData.Stats.MaxWaterTemperature01:F3} " +
            $"avg {waterMeshData.Stats.AverageWaterTemperature01:F3}, max depth {waterMeshData.Stats.MaxDepth:F1}");

        var renderer = _waterObject.GetComponent<Renderer>();
        if (_waterMaterial == null ||
            _waterMaterial.name == "Default-Material" ||
            (_oceanShader != null && _waterMaterial.shader != _oceanShader))
        {
            if (_waterMaterial != null) Destroy(_waterMaterial);
            _waterMaterial = CreateWaterMaterial();
        }
        renderer.sharedMaterial = _waterMaterial;
        UpdateWaterMaterial(_waterMaterial);
        progress?.Report(1f, "Water ready.");
    }

    Material CreateWaterMaterial()
    {
        var shader = _oceanShader != null ? _oceanShader
                   : _urpLitShader != null ? _urpLitShader
                   : _standardShader;
        var mat = new Material(shader) { name = "Water" };
        return mat;
    }

    void UpdateWaterMaterial(Material mat)
    {
        var color = _planetSettings.WaterColor;
        float waterScale = GetWaterDistanceScale();
        if (mat.HasProperty(_shallowColorId))
        {
            Color shallow = Color.Lerp(color, _waterShallowBaseColor, WaterShallowColorBlend);
            shallow.a = Mathf.Clamp01(Mathf.Max(color.a * WaterShallowAlphaFactor, WaterShallowAlphaMin));
            Color deep = Color.Lerp(color, _waterDeepBaseColor, WaterDeepColorBlend);
            deep.a = Mathf.Clamp01(Mathf.Max(color.a, WaterDeepAlphaMin));

            mat.SetColor(_shallowColorId, shallow);
            mat.SetColor(_deepColorId, deep);
            mat.SetColor(_foamColorId, _waterFoamColor);
            mat.SetFloat(_shallowDepthId, WaterShallowDepth * waterScale);
            mat.SetFloat(_deepDepthId, WaterDeepDepth * waterScale);
            mat.SetFloat(_shoreFoamDepthId, WaterShoreFoamDepth * waterScale);
            mat.SetFloat(_shoreFoamSoftnessId, WaterShoreRange * waterScale);
            mat.SetFloat(_waveAmplitudeId, WaterWaveAmplitude * waterScale);
            mat.SetFloat(_waveScaleId, WaterWaveScale * waterScale);
            mat.SetFloat(_waveSpeedId, WaterWaveSpeed);
            mat.SetFloat(_waveNormalStrengthId, WaterWaveNormalStrength);
            mat.SetFloat(_waterMotionStrengthId, WaterMotionStrength);
            mat.SetFloat(_sunGlitterIntensityId, WaterSunGlitterIntensity);
            mat.SetFloat(_sunGlitterPowerId, WaterSunGlitterPower);
            mat.SetFloat(_shoreFoamIntensityId, WaterShoreFoamIntensity);
            mat.SetFloat(_whitecapIntensityId, WaterWhitecapIntensity);
            mat.SetFloat(_wakeFoamIntensityId, WaterWakeFoamIntensity);
            mat.SetFloat(_wakeNormalStrengthId, WaterWakeNormalStrength);
            mat.SetFloat(_oceanFocusModeId, 1f);
            Shader.SetGlobalFloat(_waterFocusModeId, 0f);
            mat.SetFloat(_alphaId, WaterAlpha);
            PlanetSettings.FrozenWaterSettings frozenWater = _planetSettings.FrozenWater
                ?? new PlanetSettings.FrozenWaterSettings();
            mat.SetFloat(_freezingEnabledId, frozenWater.Enabled ? 1f : 0f);
            mat.SetFloat(_lakeFreezeStartId, frozenWater.LakeFreezeStartTemperature01);
            mat.SetFloat(_lakeFreezeCompleteId, frozenWater.LakeFreezeCompleteTemperature01);
            mat.SetFloat(_oceanFreezeStartId, frozenWater.OceanFreezeStartTemperature01);
            mat.SetFloat(_oceanFreezeCompleteId, frozenWater.OceanFreezeCompleteTemperature01);
            mat.SetColor(_iceTintId, frozenWater.IceTint);
            mat.SetFloat(_iceOpacityId, frozenWater.IceOpacity);
            mat.SetFloat(_iceRoughnessId, frozenWater.IceRoughness);
            mat.SetFloat(_iceNormalStrengthId, frozenWater.IceNormalStrength);
            mat.SetFloat(_iceBreakupScaleId, frozenWater.IceBreakupScale * waterScale);
            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            Logger.Log(LogLevel.Debug, "Water", "Applied integrated ocean mode: clouds, rain, and terrain cloud shadows enabled; focused water rendering retained.");
            return;
        }

        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetColor("_BaseColor", color);
        mat.renderQueue = 3000;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
    }

    float GetWaterDistanceScale()
    {
        if (_planetSettings == null)
            return 1f;

        return Mathf.Max(_planetSettings.PlanetRadius / WaterReferenceRadius, 0.0001f);
    }

    sealed class ProgressRangeHandle : IProgressHandle
    {
        readonly IProgressHandle _inner;
        readonly float _start;
        readonly float _length;

        public float CurrentProgress { get; private set; }
        public string CurrentMessage { get; private set; } = string.Empty;

        public ProgressRangeHandle(IProgressHandle inner, float start, float length)
        {
            _inner = inner;
            _start = start;
            _length = length;
        }

        public void Report(float progress, string message = "")
        {
            CurrentProgress = _start + Mathf.Clamp01(progress) * _length;
            CurrentMessage = message ?? string.Empty;
            _inner?.Report(CurrentProgress, CurrentMessage);
        }
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("seed", "Get the current world seed, or set a new one. Does NOT auto-regenerate — run 'planet.generate' to apply.", MonoTargetType.Single)]
    string SeedCmd(int? newSeed = null)
    {
        if (newSeed == null) return $"current planet seed: {Seed} (world seed: {ServiceLocator.Get<ISeedProvider>().WorldSeed})";
        ServiceLocator.Get<ISeedProvider>().SetWorldSeed(newSeed.Value);
        return $"world seed set to {newSeed.Value}. Run 'planet.generate' to apply.";
    }

    [ConsoleCommand("resolution", "Get or set per-face vertex resolution (range 2-256, low-mode only). Does NOT auto-regenerate — run 'planet.generate' to apply.", MonoTargetType.Single)]
    string ResolutionCmd(int? value = null)
    {
        if (value == null) return $"per-face resolution: {PerFaceResolution}";
        PerFaceResolution = Mathf.Clamp(value.Value, 2, 256);
        return $"per-face resolution: {PerFaceResolution}. Run 'planet.generate' to apply.";
    }

    [ConsoleCommand("generate", "Regenerate the planet (async, cancellable). Optionally set seed and/or radius first.", MonoTargetType.Single)]
    async Awaitable GenerateCmd(int? seed = null, float? radius = null, CancellationToken ct = default)
    {
        if (IsGenerating)
            throw new System.InvalidOperationException("planet generation already in progress");

        if (seed.HasValue)
            ServiceLocator.Get<ISeedProvider>().SetWorldSeed(seed.Value);

        if (radius.HasValue && _planetSettings != null)
            _planetSettings.PlanetRadius = Mathf.Max(100f, radius.Value);

        await GeneratePlanetAsync(ct);
    }
}
