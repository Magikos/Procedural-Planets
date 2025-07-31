using UnityEngine;

public class Planet : MonoBehaviour
{
    [Range(2, 256)]
    public int _resolution = 10;
    public bool AutoUpdate = true;

    public ShapeSettings _shapeSettings;
    public ColorSettings _colorSettings;

    [SerializeField, HideInInspector] public bool ShapeSettingsFoldout = true;
    [SerializeField, HideInInspector] public bool ColorSettingsFoldout = true;

    ShapeGenerator _shapeGenerator = new ShapeGenerator();
    ColorGenerator _colorGenerator = new ColorGenerator();
    TerrainFace[] _terrainFaces;
    [SerializeField, HideInInspector] MeshFilter[] _meshFilters;

    void OnValidate()
    {
        GeneratePlanet();
    }

    void Initialize()
    {
        if (_meshFilters == null || _meshFilters.Length == 0) { _meshFilters = new MeshFilter[6]; }
        _terrainFaces = new TerrainFace[6];

        _shapeGenerator.UpdateSettings(_shapeSettings);
        _colorGenerator.UpdateSettings(_colorSettings);

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
            _terrainFaces[i] = new TerrainFace(_shapeGenerator, _meshFilters[i].sharedMesh, _resolution, directions[i]);
        }
    }

    public void GeneratePlanet()
    {
        Initialize();
        GenerateMesh();
        GenerateColors();
    }

    public void OnShapeSettingsChanged()
    {
        if (!AutoUpdate) return;

        Initialize();
        GenerateMesh();
    }

    public void OnColorSettingsChanged()
    {
        if (!AutoUpdate) return;

        Initialize();
        GenerateColors();
    }

    void GenerateMesh()
    {
        foreach (var terrainFace in _terrainFaces)
        {
            terrainFace.ConstructMesh();
        }

        _colorGenerator.UpdateElevation(_shapeGenerator._elevationMinMax);
    }

    void GenerateColors()
    {
        _colorGenerator.UpdateColors();
        foreach (var terrainFace in _terrainFaces)
        {
            terrainFace.UpdateUVs(_colorGenerator);
        }
    }
}