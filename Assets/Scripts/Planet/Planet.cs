using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class Planet : MonoBehaviour
{
    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256)]
    public int Resolution = 10;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    public PlanetSettings _planetSettings;

    [Header("Deterministic Generation")]
    public int Seed = 12345;

    [SerializeField, HideInInspector] public bool SettingsFoldout = true;
    [SerializeField, HideInInspector] float _lastGeneratedRadius;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    TerrainFace[] _terrainFaces;
    MeshFilter[] _meshFilters;
    GameObject _waterObject;

    CancellationTokenSource _cts;
    bool _isGenerating;

    public bool IsGenerating => _isGenerating;
    public ShapeGenerator ShapeGenerator => _shapeGenerator;
    public float LastGeneratedRadius => _lastGeneratedRadius;

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
        if (_lastGeneratedRadius > 0f)
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, _lastGeneratedRadius));
        else
            GeneratePlanetAsync();
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    void Initialize()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

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
            _lastGeneratedRadius = scaledRadius;
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius));
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
            _waterObject.AddComponent<MeshRenderer>();
            _waterObject.AddComponent<MeshFilter>();
        }

        _waterObject.SetActive(true);
        _waterObject.transform.localScale = Vector3.one;
        _waterObject.transform.localPosition = Vector3.zero;

        float waterRadius = _planetSettings.PlanetRadius * (1 + _planetSettings.OceanLevel);

        var meshFilter = _waterObject.GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "WaterSphere" };
        CubeSphereMeshBuilder.Build(meshFilter.sharedMesh, 32, waterRadius);

        var renderer = _waterObject.GetComponent<Renderer>();
        if (renderer.sharedMaterial == null || renderer.sharedMaterial.name == "Default-Material")
            renderer.sharedMaterial = CreateWaterMaterial();
        UpdateWaterMaterial(renderer.sharedMaterial);
    }

    Material CreateWaterMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = "Water" };
        return mat;
    }

    void UpdateWaterMaterial(Material mat)
    {
        var color = _planetSettings.WaterColor;
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
}
