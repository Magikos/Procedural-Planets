using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

public class Planet : MonoBehaviour, IPlanet, IPlanetSurfaceSampler, ILateInitialize, IProgressReporter
{
    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256), FormerlySerializedAs("Resolution"),
     Tooltip("Per-face vertex resolution when PlanetSettings.Resolution == Low.")]
    public int PerFaceResolution = 10;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    [SerializeField] PlanetSettings _planetSettings;

    public PlanetSettings PlanetSettingsAsset => _planetSettings;

    [SerializeField, HideInInspector] bool _settingsFoldout = true;
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
    }

    void OnDestroy()
    {
        ServiceLocator.Unregister<IPlanet>(this);
        ServiceLocator.Unregister<IPlanetSurfaceSampler>(this);
        _cts?.Cancel();
        _cts?.Dispose();
        _surfaceProvider?.Dispose();
        _surfaceProvider = null;
        _perFaceProvider = null;
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
    }

    void Initialize()
    {
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
        _colorGenerator.Initialize(Seed);

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
            await GenerateMeshAsync(ct);
            if (this == null) return;
            _shapeGenerator.CommitElevationRange();
            _progressHandle.Report(0.8f, "Applying colors...");
            await GenerateColorsAsync(ct);
            if (this == null) return;
            _progressHandle.Report(0.9f, "Generating water...");
            await GenerateWaterAsync(ct);
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
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Logger.LogException("Planet", ex);
        }
        finally
        {
            _isGenerating = false;
            linkedCts?.Dispose();
        }
    }

    async Awaitable GenerateMeshAsync(CancellationToken ct)
    {
        await _surfaceProvider.GenerateAsync(_progressHandle, ct);
    }

    void GenerateColors()
    {
        if (_perFaceProvider == null) return;
        foreach (var face in _perFaceProvider.TerrainFaces)
            face.UpdateColors(_colorGenerator);
    }

    async Awaitable GenerateColorsAsync(CancellationToken ct)
    {
        if (_surfaceProvider == null) return;
        await _surfaceProvider.GenerateColorsAsync(_colorGenerator, _progressHandle, ct);
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

    async Awaitable GenerateWaterAsync(CancellationToken ct)
    {
        if (!_planetSettings.HasOceans)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
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
        var buildSettings = new WaterMeshBuilder.Settings
        {
            PlanetRadius = _planetSettings.PlanetRadius,
            OceanLevel = _planetSettings.OceanLevel,
            DeepDepth = WaterDeepDepth * waterScale,
            ShoreRange = WaterShoreRange * waterScale,
            SurfaceOffset = Mathf.Max(_planetSettings.PlanetRadius * 0.00003f, 0.02f),
            OceanBodyVertexThreshold = Mathf.Max(48, PerFaceResolution * PerFaceResolution / 28)
        };
        // Water builder reads per-face vertex/elevation grids via IFaceMeshSampler. Both
        // resolution modes (Low/High) supply this view; chunked path wraps each root chunk.
        var faceSamplers = _surfaceProvider?.GetFaceMeshSamplers();
        if (faceSamplers == null || faceSamplers.Count == 0)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
            return;
        }
        var terrainFaces = new IFaceMeshSampler[faceSamplers.Count];
        for (int i = 0; i < faceSamplers.Count; i++) terrainFaces[i] = faceSamplers[i];
        var waterMesh = meshFilter.sharedMesh;

        // Run the (pure-CPU) water build on a worker and poll its progress each frame on the main
        // thread so the loading bar advances through the heavy global-graph + per-face phases.
        _progressHandle.Report(0.90f, "Building water bodies...");
        float buildProgress = 0f;
        var computeTask = Task.Run(
            () => WaterMeshBuilder.Compute(terrainFaces, buildSettings,
                p => System.Threading.Volatile.Write(ref buildProgress, p)), ct);
        while (!computeTask.IsCompleted)
        {
            _progressHandle.Report(0.90f + 0.06f * System.Threading.Volatile.Read(ref buildProgress), "Building water bodies...");
            await Awaitable.NextFrameAsync(ct);
        }
        var waterMeshData = await computeTask;
        if (this == null) return;
        _progressHandle.Report(0.97f, "Uploading water mesh...");

        if (waterMeshData.Stats.Triangles == 0)
        {
            _waterObject.SetActive(false);
            return;
        }

        WaterMeshBuilder.Apply(waterMesh, null, waterMeshData);

        Logger.Log(LogLevel.Debug, "Water",
            $"Generated water mesh: {waterMeshData.Stats.MeshVertices} verts, {waterMeshData.Stats.Triangles} tris, " +
            $"wet terrain verts {waterMeshData.Stats.WetVertices}, ocean bodies {waterMeshData.Stats.OceanBodies}, " +
            $"small bodies {waterMeshData.Stats.SmallBodies}, max depth {waterMeshData.Stats.MaxDepth:F1}");

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

}
