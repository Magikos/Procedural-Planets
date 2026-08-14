using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Stage 3 (trunk/branch geometry) of the tree generator (plan 006): sweep a low-N ring along each branch's
// centerline, radius = girth taper, and stitch rings into tubes — the minimal generalized-cylinder mesher the
// Broccoli study distilled. Children are intersecting cylinders (no welding). Smooth normals for now; faceted
// flat-shading is a T3 tuning. One combined mesh. Plain C#; Burst-ready for T7.
public static class TreeTubeMesher
{
    public static Mesh Build(TreeSkeleton sk)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        if (sk != null)
            foreach (TreeBranch b in sk.Branches)
                AddBranch(b, verts, uvs, tris);

        var mesh = new Mesh
        {
            name = "Tree (generated)",
            indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddBranch(TreeBranch b, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        int rings = b.Centerline.Count;
        if (rings < 2) return;
        int sides = Mathf.Max(3, b.RadialSides);

        int ring0 = verts.Count;
        float vLen = 0f;

        for (int i = 0; i < rings; i++)
        {
            Vector3 c = b.Centerline[i];
            Vector3 fwd = i < rings - 1 ? (b.Centerline[i + 1] - c) : (c - b.Centerline[i - 1]);
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.up;
            Vector3 right = Vector3.Cross(fwd, Mathf.Abs(fwd.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, fwd).normalized;
            float girth = b.Girth[i];

            if (i > 0) vLen += (c - b.Centerline[i - 1]).magnitude;

            for (int j = 0; j < sides; j++)
            {
                float a = 2f * Mathf.PI * j / sides;
                verts.Add(c + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * girth);
                uvs.Add(new Vector2(j / (float)sides, vLen));
            }
        }

        // Stitch consecutive rings (wrap around; no seam vertex for now).
        for (int i = 0; i < rings - 1; i++)
        {
            int a = ring0 + i * sides;
            int b2 = a + sides;
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                int a0 = a + j, a1 = a + j1, b0 = b2 + j, b1 = b2 + j1;
                tris.Add(a0); tris.Add(b0); tris.Add(a1);
                tris.Add(a1); tris.Add(b0); tris.Add(b1);
            }
        }

        // Cap the trunk base with a fan so it isn't open at the ground.
        if (b.IsTrunk)
        {
            int center = verts.Count;
            verts.Add(b.Centerline[0]);
            uvs.Add(new Vector2(0.5f, 0f));
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                tris.Add(center); tris.Add(ring0 + j1); tris.Add(ring0 + j);
            }
        }
    }
}
