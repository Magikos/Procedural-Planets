using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Editor tool: for every harvestable (Interaction != None) scatter prototype, cut its trunk mesh to the lower
// trunk and save it as a stump mesh asset, assigning it to the prototype's StumpMesh (+ StumpMaterial = trunk
// material). This is the "copy the tree geometry, keep just the lower trunk" step, done as mesh-asset
// generation (FBX binaries can't be edited directly). Re-runnable. Stop + re-enter play afterwards so the
// ScatterLibraryDto snapshots the new stumps.
//
// The cut is a horizontal planar clip at a fraction of the mesh height, with the top capped by a fan. It
// assumes the FIRST part's LOD0 mesh is the trunk (Synty scatter-tree convention). If a tree's stump comes
// out wrong, reassign StumpMesh by hand or adjust KeepFraction / the trunk-part pick.
public static class TreeStumpGenerator
{
    const string OutFolder = "Assets/Generated/Stumps";
    const float KeepFraction = 0.16f; // keep the bottom 16% of the trunk mesh height

    [MenuItem("Tools/ProceduralPlanets/Generate Tree Stumps")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutFolder);
        string[] guids = AssetDatabase.FindAssets("t:ScatterPrototype");
        int made = 0, skipped = 0;
        try
        {
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var proto = AssetDatabase.LoadAssetAtPath<ScatterPrototype>(path);
                if (proto == null || proto.Interaction == ScatterInteraction.None) continue;

                Mesh trunk = TrunkMesh(proto);
                if (trunk == null || trunk.vertexCount == 0) { skipped++; continue; }
                EditorUtility.DisplayProgressBar("Generating tree stumps", proto.name, made / (float)guids.Length);

                Mesh stump = CutLowerTrunk(trunk, KeepFraction);
                if (stump == null || stump.vertexCount == 0)
                {
                    Debug.LogWarning($"[Stumps] '{proto.name}' produced an empty stump; skipped.");
                    skipped++;
                    continue;
                }
                stump.name = proto.name + " Stump";

                string outPath = $"{OutFolder}/{SafeName(proto.name)}_Stump.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(outPath);
                if (existing != null)
                {
                    existing.Clear();
                    EditorUtility.CopySerialized(stump, existing);
                    Object.DestroyImmediate(stump);
                    stump = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(stump, outPath);
                }

                proto.StumpMesh = AssetDatabase.LoadAssetAtPath<Mesh>(outPath);
                if (proto.StumpMaterial == null) proto.StumpMaterial = TrunkMaterial(proto);
                EditorUtility.SetDirty(proto);
                made++;
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorUtility.ClearProgressBar(); }

        Debug.Log($"[Stumps] generated {made}, skipped {skipped}. Meshes in {OutFolder}. " +
                  "Stop and re-enter play so the scatter library snapshots the new stumps.");
    }

    static string SafeName(string s) => s.Replace(' ', '_');

    static Mesh TrunkMesh(ScatterPrototype p)
    {
        if (p.Parts != null && p.Parts.Length > 0)
            foreach (ScatterPart part in p.Parts)
                if (part?.LodMeshes != null && part.LodMeshes.Length > 0 && part.LodMeshes[0] != null)
                    return part.LodMeshes[0]; // first drawable part = trunk (convention)
        if (p.LodMeshes != null && p.LodMeshes.Length > 0) return p.LodMeshes[0];
        return null;
    }

    static Material TrunkMaterial(ScatterPrototype p)
    {
        if (p.Parts != null && p.Parts.Length > 0)
            foreach (ScatterPart part in p.Parts)
                if (part?.Material != null) return part.Material;
        return p.Material;
    }

    struct V { public Vector3 p; public Vector3 n; public Vector2 uv; }

    // Keep the bottom `fraction` of the mesh (local Y). Straddling triangles are planar-clipped at the cut
    // plane; the resulting open top is capped by an angle-sorted fan so the stump is solid.
    static Mesh CutLowerTrunk(Mesh src, float fraction)
    {
        Vector3[] sv = src.vertices;
        Vector3[] sn = (src.normals != null && src.normals.Length == sv.Length) ? src.normals : null;
        Vector2[] su = (src.uv != null && src.uv.Length == sv.Length) ? src.uv : null;
        int[] tris = src.triangles; // flattens all submeshes into one

        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < sv.Length; i++) { minY = Mathf.Min(minY, sv[i].y); maxY = Mathf.Max(maxY, sv[i].y); }
        float cutY = minY + Mathf.Clamp01(fraction) * (maxY - minY);

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var outTris = new List<int>();
        var ringPts = new List<Vector3>();  // cut-plane boundary points, to cap
        var ringUvs = new List<Vector2>();

        V Get(int i) => new V { p = sv[i], n = sn != null ? sn[i] : Vector3.up, uv = su != null ? su[i] : Vector2.zero };
        V Lerp(V x, V y, float t) => new V
        {
            p = Vector3.Lerp(x.p, y.p, t),
            n = Vector3.Slerp(x.n, y.n, t),
            uv = Vector2.Lerp(x.uv, y.uv, t),
        };
        int Add(V v) { verts.Add(v.p); norms.Add(v.n.normalized); uvs.Add(v.uv); return verts.Count - 1; }

        for (int t = 0; t < tris.Length; t += 3)
        {
            V[] tri = { Get(tris[t]), Get(tris[t + 1]), Get(tris[t + 2]) };
            // Sutherland-Hodgman clip of one triangle against the half-space y <= cutY.
            var poly = new List<V>(4);
            var crossed = new List<V>(2);
            for (int e = 0; e < 3; e++)
            {
                V cur = tri[e], nxt = tri[(e + 1) % 3];
                bool curIn = cur.p.y <= cutY, nxtIn = nxt.p.y <= cutY;
                if (curIn) poly.Add(cur);
                if (curIn != nxtIn)
                {
                    float k = Mathf.Approximately(nxt.p.y, cur.p.y) ? 0f : (cutY - cur.p.y) / (nxt.p.y - cur.p.y);
                    V ip = Lerp(cur, nxt, Mathf.Clamp01(k));
                    poly.Add(ip);
                    crossed.Add(ip);
                }
            }
            if (poly.Count >= 3)
            {
                int i0 = Add(poly[0]);
                for (int k = 1; k + 1 < poly.Count; k++)
                {
                    outTris.Add(i0);
                    outTris.Add(Add(poly[k]));
                    outTris.Add(Add(poly[k + 1]));
                }
            }
            if (crossed.Count == 2) { ringPts.Add(crossed[0].p); ringUvs.Add(crossed[0].uv); ringPts.Add(crossed[1].p); ringUvs.Add(crossed[1].uv); }
        }

        if (verts.Count == 0) return null;

        // Cap the top: dedup ring points, sort by angle around their centroid (in XZ, cut plane is horizontal),
        // fan from the centroid. A rough bark-coloured cap — a proper cut-wood top comes later.
        CapTop(cutY, ringPts, ringUvs, verts, norms, uvs, outTris);

        var mesh = new Mesh { indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(outTris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static void CapTop(float cutY, List<Vector3> ringPts, List<Vector2> ringUvs,
        List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> outTris)
    {
        if (ringPts.Count < 3) return;

        // Unique-ish points (weld close ones).
        var pts = new List<Vector3>();
        var puv = new List<Vector2>();
        for (int i = 0; i < ringPts.Count; i++)
        {
            bool dup = false;
            for (int j = 0; j < pts.Count; j++)
                if ((pts[j] - ringPts[i]).sqrMagnitude < 1e-6f) { dup = true; break; }
            if (!dup) { pts.Add(ringPts[i]); puv.Add(ringUvs[i]); }
        }
        if (pts.Count < 3) return;

        Vector3 c = Vector3.zero; Vector2 cuv = Vector2.zero;
        for (int i = 0; i < pts.Count; i++) { c += pts[i]; cuv += puv[i]; }
        c /= pts.Count; cuv /= pts.Count;

        int[] order = new int[pts.Count];
        float[] ang = new float[pts.Count];
        for (int i = 0; i < pts.Count; i++) { order[i] = i; ang[i] = Mathf.Atan2(pts[i].z - c.z, pts[i].x - c.x); }
        System.Array.Sort(ang, order);

        int center = verts.Count;
        verts.Add(c); norms.Add(Vector3.up); uvs.Add(cuv);
        for (int i = 0; i < order.Length; i++)
        {
            Vector3 pa = pts[order[i]], pb = pts[order[(i + 1) % order.Length]];
            int ia = verts.Count; verts.Add(pa); norms.Add(Vector3.up); uvs.Add(puv[order[i]]);
            int ib = verts.Count; verts.Add(pb); norms.Add(Vector3.up); uvs.Add(puv[order[(i + 1) % order.Length]]);
            // Wind CCW seen from +Y (up-facing cap).
            outTris.Add(center); outTris.Add(ib); outTris.Add(ia);
        }
    }
}
