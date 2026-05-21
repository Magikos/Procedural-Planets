using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class Planet : MonoBehaviour, IPlanetSurfaceSampler
{
    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256)]
    public int Resolution = 10;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    [SerializeField] PlanetSettings _planetSettings;

    public PlanetSettings PlanetSettingsAsset => _planetSettings;

    [SerializeField, HideInInspector] public bool SettingsFoldout = true;
    [SerializeField, HideInInspector] float _lastGeneratedRadius;
    [SerializeField, HideInInspector] float _lastSeaLevelRadius;
    [SerializeField, HideInInspector] float _lastElevationMin;
    [SerializeField, HideInInspector] float _lastElevationMax;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    TerrainFace[] _terrainFaces;
    MeshFilter[] _meshFilters;
    GameObject _waterObject;

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
    static readonly int _oceanFocusModeId = Shader.PropertyToID("_OceanFocusMode");
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");

    const float WaterReferenceRadius = 5000f;
    const float WaterShallowDepth = 28f;
    const float WaterDeepDepth = 360f;
    const float WaterShoreFoamDepth = 32f;
    const float WaterShoreRange = 125f;
    const float WaterWaveAmplitude = 3.4f;
    const float WaterWaveScale = 480f;

    CancellationTokenSource _cts;
    bool _isGenerating;

    public bool IsGenerating => _isGenerating;
    public ShapeGenerator ShapeGenerator => _shapeGenerator;
    public float LastGeneratedRadius => _lastGeneratedRadius;
    public float LastSeaLevelRadius => _lastSeaLevelRadius;
    public int Seed { get; private set; }

    ILogger _logger;

    ILogger Logger
    {
        get
        {
            if (_logger == null && !ServiceLocator.TryGet(out _logger))
                _logger = new UnityLogger();
            return _logger;
        }
    }

    void Start()
    {
        GeneratePlanetAsync();
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    void Initialize()
    {
        DestroyChildrenImmediate();

        Seed = ServiceLocator.TryGet<ISeedProvider>(out var seedProvider)
            ? seedProvider.GetSeedForSystem("Planet")
            : 12345;

        _meshFilters = new MeshFilter[6];
        _terrainFaces = new TerrainFace[6];
        _waterObject = null;

        var shapeSettings = _planetSettings.BuildShapeSettings();
        _shapeGenerator.Configure(shapeSettings);
        _shapeGenerator.Initialize(Seed);
        _colorGenerator.Configure(_planetSettings.BiomeSettings);
        _colorGenerator.Initialize(Seed);

        ConfigureMaterial();

        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        for (int i = 0; i < 6; i++)
        {
            GameObject meshObject = new GameObject("mesh");
            meshObject.transform.parent = transform;

            meshObject.AddComponent<MeshRenderer>().sharedMaterial = _planetSettings.PlanetMaterial;
            _meshFilters[i] = meshObject.AddComponent<MeshFilter>();
            _meshFilters[i].sharedMesh = new Mesh();

            _terrainFaces[i] = new TerrainFace(_shapeGenerator, _meshFilters[i].sharedMesh, Resolution, directions[i]);

            bool renderFace = RenderMask == FaceRenderMask.All || (int)RenderMask - 1 == i;
            _meshFilters[i].gameObject.SetActive(renderFace);
        }
    }

    void DestroyChildrenImmediate()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }


    void ConfigureMaterial()
    {
        var mat = _planetSettings.PlanetMaterial;
        if (mat.shader.name != "Planet/VertexColor")
        {
            var vcShader = Shader.Find("Planet/VertexColor");
            if (vcShader != null) mat.shader = vcShader;
        }
        mat.SetFloat("_Smoothness", 0f);
    }

    public async void GeneratePlanetAsync()
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

        try
        {
            _isGenerating = true;
            Initialize();
            await GenerateMeshAsync(_cts.Token);
            if (this == null) return;
            GenerateColors();
            GenerateWater();
            // Atmosphere is rendered by AtmosphereController + AtmosphereRenderFeature (post-process).

            float scaledRadius = _planetSettings.PlanetRadius * (1 + _shapeGenerator.ElevationMax);
            float seaLevelRadius = _planetSettings.PlanetRadius * (1 + _planetSettings.OceanLevel);
            _lastGeneratedRadius = scaledRadius;
            _lastSeaLevelRadius = seaLevelRadius;
            _lastElevationMin = _shapeGenerator.ElevationMin;
            _lastElevationMax = _shapeGenerator.ElevationMax;
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius, seaLevelRadius, _lastElevationMin, _lastElevationMax));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, resolution {Resolution}, radius {scaledRadius:F1}");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Logger.LogException("Planet", ex);
        }
        finally
        {
            _isGenerating = false;
        }
    }

    async Awaitable GenerateMeshAsync(CancellationToken ct)
    {
        var faces = _terrainFaces;

        await Awaitable.BackgroundThreadAsync();
        ct.ThrowIfCancellationRequested();

        Parallel.For(0, faces.Length, i => { faces[i].CalculateMeshData(); });

        ct.ThrowIfCancellationRequested();
        await Awaitable.MainThreadAsync();

        for (int i = 0; i < faces.Length; i++)
            faces[i].ApplyMeshData();
    }

    void GenerateColors()
    {
        foreach (var face in _terrainFaces)
            face.UpdateColors(_colorGenerator);
    }

    public bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius)
    {
        surfaceRadius = 0f;

        if (_terrainFaces == null || worldUnitDirection.sqrMagnitude < 0.0001f)
            return false;

        Vector3 localDirection = transform.InverseTransformDirection(worldUnitDirection).normalized;
        float bestAlignment = -1f;
        float bestRadius = 0f;

        for (int i = 0; i < _terrainFaces.Length; i++)
        {
            if (_terrainFaces[i] == null)
                continue;

            if (!_terrainFaces[i].TryGetNearestSurfaceRadius(localDirection, out float candidateRadius, out float alignment))
                continue;

            if (alignment <= bestAlignment)
                continue;

            bestAlignment = alignment;
            bestRadius = candidateRadius;
        }

        if (bestRadius <= 0f)
            return false;

        float scale = Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z));
        surfaceRadius = bestRadius * Mathf.Max(scale, 0.0001f);
        return true;
    }

    void GenerateWater()
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
        var waterStats = WaterMeshBuilder.Build(meshFilter.sharedMesh, _terrainFaces, new WaterMeshBuilder.Settings
        {
            PlanetRadius = _planetSettings.PlanetRadius,
            OceanLevel = _planetSettings.OceanLevel,
            DeepDepth = WaterDeepDepth * waterScale,
            ShoreRange = WaterShoreRange * waterScale,
            SurfaceOffset = Mathf.Max(_planetSettings.PlanetRadius * 0.00003f, 0.02f),
            OceanBodyVertexThreshold = Mathf.Max(48, Resolution * Resolution / 28)
        });

        if (waterStats.Triangles == 0)
        {
            _waterObject.SetActive(false);
            return;
        }

        Logger.Log(LogLevel.Debug, "Water",
            $"Generated water mesh: {waterStats.MeshVertices} verts, {waterStats.Triangles} tris, " +
            $"wet terrain verts {waterStats.WetVertices}, ocean bodies {waterStats.OceanBodies}, " +
            $"small bodies {waterStats.SmallBodies}, max depth {waterStats.MaxDepth:F1}");

        var renderer = _waterObject.GetComponent<Renderer>();
        var oceanShader = Shader.Find("Planet/Ocean");
        if (renderer.sharedMaterial == null ||
            renderer.sharedMaterial.name == "Default-Material" ||
            (oceanShader != null && renderer.sharedMaterial.shader != oceanShader))
            renderer.sharedMaterial = CreateWaterMaterial();
        UpdateWaterMaterial(renderer.sharedMaterial);
    }

    Material CreateWaterMaterial()
    {
        var shader = Shader.Find("Planet/Ocean");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = "Water" };
        return mat;
    }

    void UpdateWaterMaterial(Material mat)
    {
        var color = _planetSettings.WaterColor;
        float waterScale = GetWaterDistanceScale();
        if (mat.HasProperty(_shallowColorId))
        {
            Color shallow = Color.Lerp(color, new Color(0.20f, 0.76f, 0.82f, color.a), 0.68f);
            shallow.a = Mathf.Clamp01(Mathf.Max(color.a * 0.14f, 0.10f));
            Color deep = Color.Lerp(color, new Color(0.0f, 0.018f, 0.065f, 1f), 0.88f);
            deep.a = Mathf.Clamp01(Mathf.Max(color.a, 0.96f));

            mat.SetColor(_shallowColorId, shallow);
            mat.SetColor(_deepColorId, deep);
            mat.SetColor(_foamColorId, new Color(0.88f, 0.98f, 0.94f, 0.9f));
            mat.SetFloat(_shallowDepthId, WaterShallowDepth * waterScale);
            mat.SetFloat(_deepDepthId, WaterDeepDepth * waterScale);
            mat.SetFloat(_shoreFoamDepthId, WaterShoreFoamDepth * waterScale);
            mat.SetFloat(_shoreFoamSoftnessId, WaterShoreRange * waterScale);
            mat.SetFloat(_waveAmplitudeId, WaterWaveAmplitude * waterScale);
            mat.SetFloat(_waveScaleId, WaterWaveScale * waterScale);
            mat.SetFloat(_waveSpeedId, 0.58f);
            mat.SetFloat(_waveNormalStrengthId, 4.5f);
            mat.SetFloat(_waterMotionStrengthId, 0.24f);
            mat.SetFloat(_sunGlitterIntensityId, 1.45f);
            mat.SetFloat(_sunGlitterPowerId, 1400f);
            mat.SetFloat(_oceanFocusModeId, 1f);
            Shader.SetGlobalFloat(_waterFocusModeId, 0f);
            mat.SetFloat(_alphaId, 0.36f);
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
