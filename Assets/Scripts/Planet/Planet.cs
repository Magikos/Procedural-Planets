using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class Planet : MonoBehaviour
{
    public enum FaceRenderMask { All, Top, Bottom, Left, Right, Front, Back }

    [Range(2, 256)]
    public int Resolution = 10;
    public bool AutoUpdate = true;
    public FaceRenderMask RenderMask = FaceRenderMask.All;

    public PlanetSettings _planetSettings;

    [Header("Deterministic Generation")]
    public int Seed = 12345;

    [SerializeField, HideInInspector] public bool SettingsFoldout = true;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    TerrainFace[] _terrainFaces;
    [SerializeField, HideInInspector] MeshFilter[] _meshFilters;
    [SerializeField, HideInInspector] GameObject _waterObject;

    ShapeSettings _builtShapeSettings;
    ColorSettings _builtColorSettings;

    CancellationTokenSource _cts;
    bool _isGenerating;

    public bool IsGenerating => _isGenerating;

    ITerrainProvider TerrainProvider => _shapeGenerator;
    IBiomeProvider BiomeProvider => _colorGenerator;
    IColorProvider ColorProvider => _colorGenerator;

    public ShapeGenerator ShapeGenerator => _shapeGenerator;

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

    void OnValidate()
    {
        if (Application.isPlaying) return;
        GeneratePlanetAsync();
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    void Initialize()
    {
        if (_meshFilters == null || _meshFilters.Length == 0) { _meshFilters = new MeshFilter[6]; }
        _terrainFaces = new TerrainFace[6];

        _builtShapeSettings = _planetSettings.BuildShapeSettings();
        _builtColorSettings = _planetSettings.BuildColorSettings();

        _shapeGenerator.Configure(_builtShapeSettings);
        _shapeGenerator.Initialize(Seed);
        _colorGenerator.Configure(_builtColorSettings);
        _colorGenerator.Initialize(Seed);

        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        for (int i = 0; i < 6; i++)
        {
            if (_meshFilters[i] == null)
            {
                GameObject meshObject = new GameObject("mesh");
                meshObject.transform.parent = transform;

                meshObject.AddComponent<MeshRenderer>();
                _meshFilters[i] = meshObject.AddComponent<MeshFilter>();
                _meshFilters[i].sharedMesh = new Mesh();
            }

            _meshFilters[i].GetComponent<MeshRenderer>().sharedMaterial = _planetSettings.PlanetMaterial;
            _terrainFaces[i] = new TerrainFace(TerrainProvider, _meshFilters[i].sharedMesh, Resolution, directions[i]);

            bool renderFace = RenderMask == FaceRenderMask.All || (int)RenderMask - 1 == i;
            _meshFilters[i].gameObject.SetActive(renderFace);
        }
    }

    public async void GeneratePlanetAsync()
    {
        if (_planetSettings == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "PlanetSettings is not assigned.");
            return;
        }

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

            float scaledRadius = _planetSettings.PlanetRadius * (1 + TerrainProvider.ElevationMax);
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

    public void OnSettingsChanged()
    {
        if (!AutoUpdate) return;
        GeneratePlanetAsync();
    }

    async Awaitable GenerateMeshAsync(CancellationToken ct)
    {
        var faces = _terrainFaces;

        await Awaitable.BackgroundThreadAsync();
        ct.ThrowIfCancellationRequested();

        Parallel.For(0, faces.Length, i =>
        {
            faces[i].CalculateMeshData();
        });

        ct.ThrowIfCancellationRequested();
        await Awaitable.MainThreadAsync();

        for (int i = 0; i < faces.Length; i++)
        {
            faces[i].ApplyMeshData();
        }

        ColorProvider.UpdateElevation(TerrainProvider.ElevationMin, TerrainProvider.ElevationMax);
    }

    void GenerateColors()
    {
        ColorProvider.UpdateColors();
        foreach (var terrainFace in _terrainFaces)
        {
            terrainFace.UpdateUVs(BiomeProvider);
        }
    }

    void GenerateWater()
    {
        if (!_planetSettings.HasOceans)
        {
            if (_waterObject != null)
                _waterObject.SetActive(false);
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

        float waterRadius = _planetSettings.PlanetRadius * (1 + _planetSettings.OceanLevel) + 0.001f * _planetSettings.PlanetRadius;

        // Build a simple sphere mesh
        var meshFilter = _waterObject.GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = CreateSphereMesh(32, waterRadius);
        else
            UpdateSphereMesh(meshFilter.sharedMesh, 32, waterRadius);

        var renderer = _waterObject.GetComponent<Renderer>();
        if (renderer.sharedMaterial == null || renderer.sharedMaterial.name == "Default-Material")
            renderer.sharedMaterial = CreateWaterMaterial();
        UpdateWaterMaterial(renderer.sharedMaterial);
    }

    Mesh CreateSphereMesh(int resolution, float radius)
    {
        var mesh = new Mesh();
        mesh.name = "WaterSphere";
        UpdateSphereMesh(mesh, resolution, radius);
        return mesh;
    }

    void UpdateSphereMesh(Mesh mesh, int resolution, float radius)
    {
        // 6-face cube sphere, same as terrain but simpler
        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        int vertsPerFace = resolution * resolution;
        int trisPerFace = (resolution - 1) * (resolution - 1) * 6;

        var vertices = new Vector3[vertsPerFace * 6];
        var triangles = new int[trisPerFace * 6];

        for (int face = 0; face < 6; face++)
        {
            Vector3 localUp = directions[face];
            Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
            Vector3 axisB = Vector3.Cross(localUp, axisA);

            int vertOffset = face * vertsPerFace;
            int triOffset = face * trisPerFace;
            int triIdx = 0;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = x + y * resolution;
                    Vector2 percent = new Vector2(x, y) / (resolution - 1);
                    Vector3 pointOnCube = localUp + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                    vertices[vertOffset + i] = pointOnCube.normalized * radius;

                    if (x < resolution - 1 && y < resolution - 1)
                    {
                        int vi = vertOffset + i;
                        triangles[triOffset + triIdx]     = vi;
                        triangles[triOffset + triIdx + 1] = vi + resolution + 1;
                        triangles[triOffset + triIdx + 2] = vi + resolution;
                        triangles[triOffset + triIdx + 3] = vi;
                        triangles[triOffset + triIdx + 4] = vi + 1;
                        triangles[triOffset + triIdx + 5] = vi + resolution + 1;
                        triIdx += 6;
                    }
                }
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    Material CreateWaterMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader);
        mat.name = "Water";
        return mat;
    }

    void UpdateWaterMaterial(Material mat)
    {
        var color = _planetSettings.WaterColor;

        // Set surface type to transparent
        mat.SetFloat("_Surface", 1); // 0=Opaque, 1=Transparent
        mat.SetFloat("_Blend", 0);   // 0=Alpha
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
