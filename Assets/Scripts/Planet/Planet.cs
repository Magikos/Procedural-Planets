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

    public ShapeSettings _shapeSettings;
    public ColorSettings _colorSettings;

    [Header("Deterministic Generation")]
    public int Seed = 12345;

    [SerializeField, HideInInspector] public bool ShapeSettingsFoldout = true;
    [SerializeField, HideInInspector] public bool ColorSettingsFoldout = true;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    TerrainFace[] _terrainFaces;
    [SerializeField, HideInInspector] MeshFilter[] _meshFilters;

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

        _shapeGenerator.Configure(_shapeSettings);
        _shapeGenerator.Initialize(Seed);
        _colorGenerator.Configure(_colorSettings);
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

            _meshFilters[i].GetComponent<MeshRenderer>().sharedMaterial = _colorSettings.PlanetMaterial;
            _terrainFaces[i] = new TerrainFace(TerrainProvider, _meshFilters[i].sharedMesh, Resolution, directions[i]);

            bool renderFace = RenderMask == FaceRenderMask.All || (int)RenderMask - 1 == i;
            _meshFilters[i].gameObject.SetActive(renderFace);
        }
    }

    public async void GeneratePlanetAsync()
    {
        if (_shapeSettings == null || _colorSettings == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "ShapeSettings or ColorSettings is not assigned.");
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

            float scaledRadius = _shapeSettings.PlanetRadius * (1 + TerrainProvider.ElevationMax);
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, resolution {Resolution}, radius {scaledRadius:F1}");
            Logger.Log(LogLevel.Debug, "Planet", $"Elevation range: {TerrainProvider.ElevationMin:F6} to {TerrainProvider.ElevationMax:F6}");
            LogBiomeDiagnostics();
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

    public void OnShapeSettingsChanged()
    {
        if (!AutoUpdate) return;
        GeneratePlanetAsync();
    }

    public void OnColorSettingsChanged()
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

    void LogBiomeDiagnostics()
    {
        var points = new (string name, Vector3 dir)[]
        {
            ("North Pole",  Vector3.up),
            ("75N",         new Vector3(0, 0.966f, 0.259f).normalized),
            ("60N",         new Vector3(0, 0.866f, 0.5f).normalized),
            ("45N",         new Vector3(0, 0.707f, 0.707f).normalized),
            ("30N",         new Vector3(0, 0.5f, 0.866f).normalized),
            ("15N",         new Vector3(0, 0.259f, 0.966f).normalized),
            ("Equator",     Vector3.forward),
            ("15S",         new Vector3(0, -0.259f, 0.966f).normalized),
            ("30S",         new Vector3(0, -0.5f, 0.866f).normalized),
            ("South Pole",  Vector3.down),
        };

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== BIOME DIAGNOSTICS ===");
        sb.AppendLine($"{"Location",-14} {"Elev",8} {"Temp",6} {"Moist",6} {"UV.x",6} {"Biome",-12}");

        foreach (var (name, dir) in points)
        {
            float elev = TerrainProvider.EvaluateElevation(dir);
            var biome = BiomeProvider.EvaluateBiome(dir, elev);
            float uvx = BiomeProvider.BiomePercentFromPoint(dir, elev);
            sb.AppendLine($"{name,-14} {elev,8:F5} {biome.Temperature,6:F3} {biome.Moisture,6:F3} {uvx,6:F3} {biome.PrimaryBiome,-12}");
        }

        Logger.Log(LogLevel.Info, "Planet", sb.ToString());
    }
}
