using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Stage 3 (trunk/branch geometry) of the tree generator (plan 006): sweep a low-N ring along each branch's
// centerline, radius = girth taper, and stitch rings into tubes — the minimal generalized-cylinder mesher the
// Broccoli study distilled. Children are intersecting cylinders (no welding). Smooth normals for now; faceted
// flat-shading is a T3 tuning. One combined mesh. Plain C#; Burst-ready for T7.
public static class TreeTubeMesher
{
    // sidesDelta reduces radial resolution for lower LODs (clamped to >= 3 sides).
    public static Mesh Build(TreeSkeleton sk, int sidesDelta = 0)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        if (sk != null)
            foreach (TreeBranch b in sk.Branches)
                AddBranch(b, sidesDelta, verts, uvs, tris);

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

    // A single capped tube from a centerline + per-point girth (used for the cut-set stump/log). The mesh is
    // built in local space with its pivot at centerline[0] (the base). Both ends are capped (cut surfaces).
    public static Mesh BuildCappedTube(IReadOnlyList<Vector3> centerline, IReadOnlyList<float> girth, int sides)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        int rings = centerline.Count;
        var mesh = new Mesh { name = "Tree cut tube" };
        if (rings < 2) return mesh;
        sides = Mathf.Max(3, sides);
        Vector3 origin = centerline[0];
        float vLen = 0f;

        for (int i = 0; i < rings; i++)
        {
            Vector3 c = centerline[i] - origin;
            Vector3 fwd = i < rings - 1 ? (centerline[i + 1] - centerline[i]) : (centerline[i] - centerline[i - 1]);
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.up;
            Vector3 right = Vector3.Cross(fwd, Mathf.Abs(fwd.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, fwd).normalized;
            float g = girth[i];
            if (i > 0) vLen += (centerline[i] - centerline[i - 1]).magnitude;
            for (int j = 0; j < sides; j++)
            {
                float a = 2f * Mathf.PI * j / sides;
                verts.Add(c + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * g);
                uvs.Add(new Vector2(j / (float)sides, vLen));
            }
        }

        for (int i = 0; i < rings - 1; i++)
        {
            int a = i * sides, b = a + sides;
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                tris.Add(a + j); tris.Add(b + j); tris.Add(a + j1);
                tris.Add(a + j1); tris.Add(b + j); tris.Add(b + j1);
            }
        }

        // Base cap (fan, faces down) + top cap (fan, faces up) — the cut surfaces.
        int cBase = verts.Count; verts.Add(centerline[0] - origin); uvs.Add(new Vector2(0.5f, 0f));
        for (int j = 0; j < sides; j++) { int j1 = (j + 1) % sides; tris.Add(cBase); tris.Add(j1); tris.Add(j); }
        int last = (rings - 1) * sides;
        int cTop = verts.Count; verts.Add(centerline[rings - 1] - origin); uvs.Add(new Vector2(0.5f, 1f));
        for (int j = 0; j < sides; j++) { int j1 = (j + 1) % sides; tris.Add(cTop); tris.Add(last + j); tris.Add(last + j1); }

        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddBranch(TreeBranch b, int sidesDelta, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        int rings = b.Centerline.Count;
        if (rings < 2) return;
        int sides = Mathf.Max(3, b.RadialSides - sidesDelta);

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

            // sides+1 vertices per ring: the last duplicates the first position but carries U=1, so the wrap
            // face samples 1-1/sides -> 1 instead of running the whole texture backwards across one facet.
            // Costs one vertex per ring and is what makes a bark texture (e.g. birch) legible at the seam.
            // V is cumulative length in METRES, so a wrapped bark texture tiles per metre at any tree size.
            for (int j = 0; j <= sides; j++)
            {
                float a = 2f * Mathf.PI * j / sides;
                verts.Add(c + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * girth);
                uvs.Add(new Vector2(j / (float)sides, vLen));
            }
        }

        int stride = sides + 1;
        for (int i = 0; i < rings - 1; i++)
        {
            int a = ring0 + i * stride;
            int b2 = a + stride;
            for (int j = 0; j < sides; j++)
            {
                int a0 = a + j, a1 = a + j + 1, b0 = b2 + j, b1 = b2 + j + 1;
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
                tris.Add(center); tris.Add(ring0 + j + 1); tris.Add(ring0 + j);
            }
        }
    }
}
