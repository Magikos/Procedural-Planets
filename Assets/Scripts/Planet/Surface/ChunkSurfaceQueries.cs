using UnityEngine;

// CPU raycasts over retained chunk geometry, with lazily cached triangle bounds.
public static class ChunkSurfaceQueries
{
    // Retained geometry is immutable between generation passes. Bounds prune triangle groups without
    // changing the intersection or normal math used by footsteps, picking, and surface edits.
    internal sealed class TriangleBoundsIndex
    {
        internal struct Node
        {
            internal Bounds Bounds;
            internal int Start, Count, End;
        }

        internal readonly Vector3[] Vertices;
        internal readonly int[] Triangles;
        internal readonly Node[] Nodes;
        internal int Count => _count;
        int _count;

        internal TriangleBoundsIndex(Vector3[] vertices, int[] triangles)
        {
            Vertices = vertices;
            Triangles = triangles;
            int leaves = 1;
            while (leaves * 32 < triangles.Length / 3) leaves *= 2;
            Nodes = new Node[leaves * 2 - 1];
            Build(0, triangles.Length / 3);
        }

        int Build(int start, int count)
        {
            int index = _count++;
            Bounds bounds;
            if (count > 32)
            {
                int left = Build(start, count / 2);
                int right = Build(start + count / 2, count - count / 2);
                bounds = Nodes[left].Bounds;
                bounds.Encapsulate(Nodes[right].Bounds);
            }
            else
            {
                bounds = default;
                bool first = true;
                for (int i = start * 3; i < (start + count) * 3; i++)
                {
                    int vertex = Triangles[i];
                    if ((uint)vertex >= (uint)Vertices.Length) continue;
                    if (first) { bounds = new Bounds(Vertices[vertex], Vector3.zero); first = false; }
                    else bounds.Encapsulate(Vertices[vertex]);
                }
                // Keep boundary hits conservative at planet-scale floating-point precision.
                bounds.Expand(0.002f);
            }
            Nodes[index] = new Node { Bounds = bounds, Start = start * 3,
                Count = count <= 32 ? count * 3 : 0, End = _count };
            return index;
        }
    }

    public static bool RaycastChunkTriangles(Ray ray, PlanetChunk chunk, int[] triangles, float maxDistance,
        out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitDistance = 0f;
        hitPoint = default;
        hitNormal = default;

        Vector3[] vertices = chunk?.CpuVertices;
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        Vector3[] normals = chunk.CpuNormals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        float bestDistance = maxDistance;
        float bestU = 0f;
        float bestV = 0f;
        int bestA = -1;
        int bestB = -1;
        int bestC = -1;

        var index = chunk.RaycastIndex;
        if (index == null || !ReferenceEquals(index.Vertices, vertices) || !ReferenceEquals(index.Triangles, triangles))
            chunk.RaycastIndex = index = new TriangleBoundsIndex(vertices, triangles);

        for (int nodeIndex = 0; nodeIndex < index.Count;)
        {
            var node = index.Nodes[nodeIndex];
            if (!node.Bounds.IntersectRay(ray, out float entry) || entry > bestDistance)
            {
                nodeIndex = node.End;
                continue;
            }
            nodeIndex++;
            for (int i = node.Start; i < node.Start + node.Count; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length)
                    continue;

                if (!RaycastTriangle(ray, vertices[ia], vertices[ib], vertices[ic], bestDistance,
                        out float distance, out float u, out float v))
                    continue;

                bestDistance = distance;
                bestU = u;
                bestV = v;
                bestA = ia;
                bestB = ib;
                bestC = ic;
            }
        }

        if (bestA < 0)
            return false;

        hitDistance = bestDistance;
        hitPoint = ray.origin + ray.direction * bestDistance;
        float w = 1f - bestU - bestV;
        if (hasNormals)
        {
            hitNormal = (normals[bestA] * w + normals[bestB] * bestU + normals[bestC] * bestV).normalized;
        }
        else
        {
            Vector3 a = vertices[bestA];
            Vector3 b = vertices[bestB];
            Vector3 c = vertices[bestC];
            hitNormal = Vector3.Cross(b - a, c - a).normalized;
        }

        if (hitNormal.sqrMagnitude < 0.0001f && hitPoint.sqrMagnitude > 0.0001f)
            hitNormal = hitPoint.normalized;
        if (Vector3.Dot(hitNormal, hitPoint) < 0f)
            hitNormal = -hitNormal;
        return true;
    }

    static bool RaycastTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, float maxDistance,
        out float distance, out float u, out float v)
    {
        const float epsilon = 1e-6f;
        distance = 0f;
        u = 0f;
        v = 0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 pvec = Vector3.Cross(ray.direction, edge2);
        float det = Vector3.Dot(edge1, pvec);
        if (Mathf.Abs(det) < epsilon)
            return false;

        float invDet = 1f / det;
        Vector3 tvec = ray.origin - a;
        u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
            return false;

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        v = Vector3.Dot(ray.direction, qvec) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, qvec) * invDet;
        return distance > epsilon && distance <= maxDistance;
    }
}
