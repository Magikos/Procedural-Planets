using UnityEngine;

public class TerrainFace
{
    Mesh _mesh;
    int _resolution;
    Vector3 _localUp;
    Vector3 _axisA;
    Vector3 _axisB;

    ShapeGenerator _shapeGenerator;
    Vector3[] _unitSpherePoints;

    public TerrainFace(ShapeGenerator shapeGenerator, Mesh mesh, int resolution, Vector3 localUp)
    {
        _shapeGenerator = shapeGenerator;
        _mesh = mesh;
        _resolution = resolution;
        _localUp = localUp;

        _axisA = new Vector3(_localUp.y, _localUp.z, _localUp.x);
        _axisB = Vector3.Cross(_localUp, _axisA);
    }

    public void ConstructMesh()
    {
        int vertexCount = _resolution * _resolution;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvCache = (_mesh.uv.Length == vertexCount) ? _mesh.uv : new Vector2[vertexCount];
        int[] triangles = new int[(_resolution - 1) * (_resolution - 1) * 6];
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

                float unscaledElevation = _shapeGenerator.CalculateUnscaledElevation(pointOnUnitSphere);
                vertices[i] = pointOnUnitSphere * _shapeGenerator.GetScaledElevation(unscaledElevation);
                uvCache[i].y = unscaledElevation;

                if (x < _resolution - 1 && y < _resolution - 1)
                {
                    triangles[triIndex] = i;
                    triangles[triIndex + 1] = i + _resolution + 1;
                    triangles[triIndex + 2] = i + _resolution;

                    triangles[triIndex + 3] = i;
                    triangles[triIndex + 4] = i + 1;
                    triangles[triIndex + 5] = i + _resolution + 1;

                    triIndex += 6;
                }
            }
        }

        _mesh.Clear();
        _mesh.vertices = vertices;
        _mesh.triangles = triangles;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.uv = uvCache;
    }

    public void UpdateUVs(ColorGenerator colorGenerator)
    {
        Vector2[] uv = _mesh.uv;
        for (int i = 0; i < _unitSpherePoints.Length; i++)
        {
            uv[i].x = colorGenerator.BiomePercentFromPoint(_unitSpherePoints[i]);
        }

        _mesh.uv = uv;
    }
}
