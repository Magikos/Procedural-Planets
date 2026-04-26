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
        GeneratePlanet();
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

    public void GeneratePlanet()
    {
        if (_shapeSettings == null || _colorSettings == null)
        {
            Logger.Log(LogLevel.Warning, "Planet", "ShapeSettings or ColorSettings is not assigned.");
            return;
        }

        Initialize();
        GenerateMesh();
        GenerateColors();

        float scaledRadius = _shapeSettings.PlanetRadius * (1 + TerrainProvider.ElevationMax);
        EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius));
        Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, resolution {Resolution}, radius {scaledRadius:F1}");
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
            Initialize();
            await GenerateMeshAsync(_cts.Token);
            GenerateColors();

            float scaledRadius = _shapeSettings.PlanetRadius * (1 + TerrainProvider.ElevationMax);
            EventBus<PlanetGeneratedEvent>.Raise(new PlanetGeneratedEvent(transform.position, scaledRadius));
            Logger.Log(LogLevel.Debug, "Planet", $"Generated planet async with seed {Seed}, resolution {Resolution}, radius {scaledRadius:F1}");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Logger.LogException("Planet", ex);
        }
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

    public void OnShapeSettingsChanged()
    {
        if (!AutoUpdate) return;
        if (_shapeSettings == null || _colorSettings == null) return;

        Initialize();
        GenerateMesh();
    }

    public void OnColorSettingsChanged()
    {
        if (!AutoUpdate) return;
        if (_shapeSettings == null || _colorSettings == null) return;

        Initialize();
        GenerateColors();
    }

    void GenerateMesh()
    {
        foreach (var terrainFace in _terrainFaces)
        {
            terrainFace.ConstructMesh();
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
}
