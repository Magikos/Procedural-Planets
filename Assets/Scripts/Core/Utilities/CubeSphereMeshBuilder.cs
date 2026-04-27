using UnityEngine;

public static class CubeSphereMeshBuilder
{
    static readonly Vector3[] Directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

    public static void Build(Mesh mesh, int resolution, float radius)
    {
        int vertsPerFace = resolution * resolution;
        int trisPerFace = (resolution - 1) * (resolution - 1) * 6;

        var vertices = new Vector3[vertsPerFace * 6];
        var triangles = new int[trisPerFace * 6];

        for (int face = 0; face < 6; face++)
        {
            Vector3 localUp = Directions[face];
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
}
