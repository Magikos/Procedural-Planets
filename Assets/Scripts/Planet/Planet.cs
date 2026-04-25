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

    void Initialize()
    {
        if (_meshFilters == null || _meshFilters.Length == 0) { _meshFilters = new MeshFilter[6]; }
        _terrainFaces = new TerrainFace[6];

        TerrainProvider.Initialize(_shapeSettings, Seed);
        _colorGenerator.Initialize(_colorSettings, Seed);

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
        Logger.Log(LogLevel.Debug, "Planet", $"Generated planet with seed {Seed}, resolution {Resolution}");
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

        ColorProvider.UpdateElevation(TerrainProvider.ElevationRange);
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
