using UnityEngine;

public class TerrainFace
{
    Mesh _mesh;
    int _resolution;
    Vector3 _localUp;
    Vector3 _axisA;
    Vector3 _axisB;

    ITerrainProvider _terrainProvider;
    Vector3[] _unitSpherePoints;
    float[] _elevations;

    Vector3[] _pendingVertices;
    int[] _pendingTriangles;
    Color[] _pendingColors;

    public int Resolution => _resolution;
    public Vector3[] UnitSpherePoints => _unitSpherePoints;
    public float[] Elevations => _elevations;

    public bool TryGetNearestSurfaceRadius(Vector3 unitDirection, out float radius, out float alignment)
    {
        radius = 0f;
        alignment = -1f;

        if (_unitSpherePoints == null || _pendingVertices == null)
            return false;

        for (int i = 0; i < _unitSpherePoints.Length; i++)
        {
            float dot = Vector3.Dot(unitDirection, _unitSpherePoints[i]);
            if (dot <= alignment)
                continue;

            alignment = dot;
            radius = _pendingVertices[i].magnitude;
        }

        return alignment > -0.5f && radius > 0f;
    }

    public TerrainFace(ITerrainProvider terrainProvider, Mesh mesh, int resolution, Vector3 localUp)
    {
        _terrainProvider = terrainProvider;
        _mesh = mesh;
        _resolution = resolution;
        _localUp = localUp;

        _axisA = new Vector3(_localUp.y, _localUp.z, _localUp.x);
        _axisB = Vector3.Cross(_localUp, _axisA);
    }

    public void CalculateMeshData()
    {
        int vertexCount = _resolution * _resolution;
        _pendingVertices = new Vector3[vertexCount];
        _pendingTriangles = new int[(_resolution - 1) * (_resolution - 1) * 6];
        _unitSpherePoints = new Vector3[vertexCount];
        _elevations = new float[vertexCount];
        int triIndex = 0;

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                int i = x + y * _resolution;
                Vector2 percent = new Vector2(x, y) / (_resolution - 1);
                Vector3 pointOnUnitCube = _localUp + (percent.x - 0.5f) * 2 * _axisA + (percent.y - 0.5f) * 2 * _axisB;
                Vector3 pointOnUnitSphere = pointOnUnitCube.normalized;
                _unitSpherePoints[i] = pointOnUnitSphere;

                float unscaledElevation = _terrainProvider.EvaluateElevation(pointOnUnitSphere);
                _elevations[i] = unscaledElevation;
                _pendingVertices[i] = pointOnUnitSphere * _terrainProvider.GetScaledElevation(unscaledElevation);

                if (x < _resolution - 1 && y < _resolution - 1)
                {
                    _pendingTriangles[triIndex] = i;
                    _pendingTriangles[triIndex + 1] = i + _resolution + 1;
                    _pendingTriangles[triIndex + 2] = i + _resolution;

                    _pendingTriangles[triIndex + 3] = i;
                    _pendingTriangles[triIndex + 4] = i + 1;
                    _pendingTriangles[triIndex + 5] = i + _resolution + 1;

                    triIndex += 6;
                }
            }
        }
    }

    public void ApplyMeshData()
    {
        _mesh.Clear();
        _mesh.vertices = _pendingVertices;
        _mesh.triangles = _pendingTriangles;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    /// <summary>Computes vertex colors from biome data. Safe to call from a background thread.</summary>
    public void CalculateColors(IBiomeProvider biomeProvider)
    {
        if (_unitSpherePoints == null || _elevations == null) return;

        _pendingColors = new Color[_unitSpherePoints.Length];
        for (int i = 0; i < _unitSpherePoints.Length; i++)
            _pendingColors[i] = biomeProvider.GetBiomeColor(_unitSpherePoints[i], _elevations[i]);
    }

    /// <summary>Uploads previously computed colors to the mesh. Must be called on the main thread.</summary>
    public void ApplyColors()
    {
        if (_pendingColors != null)
            _mesh.colors = _pendingColors;
    }

    public void UpdateColors(IBiomeProvider biomeProvider)
    {
        CalculateColors(biomeProvider);
        ApplyColors();
    }
}
