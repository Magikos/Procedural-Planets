using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Stage 3 (foliage) of the tree generator (plan 006): place a small leaf cluster at each sprout anchor and weld
// them into one foliage mesh (separate from bark, its own material). A cluster is a Cross of two double-sided
// quads hinged at the stem — cheap toon volume readable from any angle. `leafScale` lets LODs shrink/thin
// foliage. Plain C#; Burst-ready for T7. Visual (leaf shape/size) is a first guess to tune.
public static class TreeLeafMesher
{
    public static Mesh Build(TreeSkeleton sk, float leafScale = 1f)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        if (sk != null)
            foreach (TreeSprout s in sk.Sprouts)
                AddCluster(s, leafScale, verts, uvs, tris);

        var mesh = new Mesh
        {
            name = "Tree foliage (generated)",
            indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddCluster(TreeSprout s, float leafScale, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        float size = Mathf.Max(0.01f, s.Size * leafScale);
        Vector3 fwd = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector3.up;
        Vector3 nrm = s.Normal.sqrMagnitude > 1e-6f ? s.Normal.normalized : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(fwd, nrm);

        // Two crossed quads (local: one in XY, one in XZ), pivot at the stem (local origin), extending along +Z.
        AddQuad(s.Position, rot, size, planeB: false, verts, uvs, tris);
        AddQuad(s.Position, rot, size, planeB: true, verts, uvs, tris);
    }

    static void AddQuad(Vector3 center, Quaternion rot, float size, bool planeB,
        List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        float h = size * 0.5f;
        // Leaf card lies along +Z (the cluster direction), spread in X (planeA) or Y (planeB).
        Vector3 a, b, c, d;
        if (!planeB)
        {
            a = new Vector3(-h, 0f, 0f); b = new Vector3(h, 0f, 0f);
            c = new Vector3(h, 0f, size); d = new Vector3(-h, 0f, size);
        }
        else
        {
            a = new Vector3(0f, -h, 0f); b = new Vector3(0f, h, 0f);
            c = new Vector3(0f, h, size); d = new Vector3(0f, -h, size);
        }

        int i0 = verts.Count;
        verts.Add(center + rot * a); uvs.Add(new Vector2(0, 0));
        verts.Add(center + rot * b); uvs.Add(new Vector2(1, 0));
        verts.Add(center + rot * c); uvs.Add(new Vector2(1, 1));
        verts.Add(center + rot * d); uvs.Add(new Vector2(0, 1));

        // Double-sided (both windings) so the leaves show from any angle.
        tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
        tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
    }
}
