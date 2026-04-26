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

    // Cached results from async generation
    Vector3[] _pendingVertices;
    Vector2[] _pendingUVs;
    int[] _pendingTriangles;

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
        _pendingUVs = new Vector2[vertexCount];
        _pendingTriangles = new int[(_resolution - 1) * (_resolution - 1) * 6];
        _unitSpherePoints = new Vector3[vertexCount];
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
                _pendingVertices[i] = pointOnUnitSphere * _terrainProvider.GetScaledElevation(unscaledElevation);
                _pendingUVs[i].y = unscaledElevation;

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
        _mesh.uv = _pendingUVs;
    }

    public void ConstructMesh()
    {
        CalculateMeshData();
        ApplyMeshData();
    }

    public void CalculateUVData(IBiomeProvider biomeProvider)
    {
        if (_unitSpherePoints == null) return;
        _pendingUVs = _mesh.uv;
        for (int i = 0; i < _unitSpherePoints.Length; i++)
        {
            _pendingUVs[i].x = biomeProvider.BiomePercentFromPoint(_unitSpherePoints[i], _pendingUVs[i].y);
        }
    }

    public void ApplyUVData()
    {
        if (_pendingUVs == null) return;
        _mesh.uv = _pendingUVs;
    }

    public void UpdateUVs(IBiomeProvider biomeProvider)
    {
        CalculateUVData(biomeProvider);
        ApplyUVData();
    }
}
