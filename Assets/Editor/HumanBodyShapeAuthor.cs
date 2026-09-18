using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HumanBodyShapeAuthor
{
    static readonly string[] Shapes = { "defaultBuff", "defaultHeavy", "defaultSkinny", "masculineFeminine" };
    static readonly string[] BodyParts = { "01HEAD", "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS" };

    [MenuItem("Tools/Actors/Human/Bake Body Shapes")]
    public static void Bake()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before baking body shapes.");
        string path = HumanBodyReviewAuthor.Folder + "/SourcePartsBody.prefab";
        var target = PrefabUtility.LoadPrefabContents(path);
        try
        {
            BakeParts(target);
            PrefabUtility.SaveAsPrefabAsset(target, path);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(target); }
    }

    public static void BakeParts(GameObject target, bool fullBody = false, int? expectedPartCount = null)
    {
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(HumanBodyReviewAuthor.Folder + "/BareBody.prefab");
        if (reference == null) throw new InvalidOperationException("The bare body prefab is required.");
        var positions = new List<Vector3>();
        var triangles = new List<int>();
        var movements = Shapes.Select(_ => new List<Vector3>()).ToArray();
        var partNames = fullBody ? BodyParts.Concat(new[] { "18LEGL", "19LEGR", "20FOTL", "21FOTR" }).ToArray() : BodyParts;
        var referenceParts = reference.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => partNames.Any(id => r.name.Contains(id))).ToArray();
        if (referenceParts.Length != partNames.Length) throw new InvalidOperationException("Missing or duplicate body reference parts.");
        foreach (var skin in referenceParts)
        {
            var mesh = skin.sharedMesh;
            var matrix = reference.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            int offset = positions.Count;
            positions.AddRange(mesh.vertices.Select(matrix.MultiplyPoint3x4));
            triangles.AddRange(mesh.triangles.Select(i => i + offset));
            for (int s = 0; s < Shapes.Length; s++)
            {
                int index = Enumerable.Range(0, mesh.blendShapeCount).Where(i => mesh.GetBlendShapeName(i).EndsWith(Shapes[s])).DefaultIfEmpty(-1).First();
                if (index < 0 || mesh.GetBlendShapeFrameCount(index) != 1 || Mathf.Abs(mesh.GetBlendShapeFrameWeight(index, 0) - 100) > .001f)
                    throw new InvalidOperationException("Expected a single 100% body-shape frame: " + skin.name + " / " + Shapes[s]);
                var delta = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(index, 0, delta, null, null);
                movements[s].AddRange(delta.Select(matrix.MultiplyVector));
            }
        }
        if (triangles.Count == 0) throw new InvalidOperationException("No body reference surface found.");
        var surface = positions.ToArray(); var indices = triangles.ToArray();
        var deltas = movements.Select(m => m.ToArray()).ToArray();
        var outputs = new List<(SkinnedMeshRenderer renderer, Mesh mesh, Vector3[] fit, Vector3[] fitNormals, Vector3[] fitTangents, Vector3[][] shapes, Vector3[][] normals, float maxDistance)>();

        // Compute every part before editing assets. A missing source shape cannot leave a partial bake.
        foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.StartsWith("SOURCE / ")))
        {
            var mesh = skin.sharedMesh;
            int fitIndex = mesh.GetBlendShapeIndex("SkeletonFit");
            if (fitIndex < 0) throw new InvalidOperationException("Missing fitted reference: " + skin.name);
            var fit = new Vector3[mesh.vertexCount]; var fitNormals = new Vector3[mesh.vertexCount]; var fitTangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(fitIndex, 0, fit, fitNormals, fitTangents);
            var vertices = mesh.vertices;
            var matrix = target.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var inverse = matrix.inverse;
            var frames = Shapes.Select(_ => new Vector3[vertices.Length]).ToArray();
            float maxDistance = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                var point = matrix.MultiplyPoint3x4(vertices[v] + fit[v]);
                float best = float.PositiveInfinity; int triangle = -1; Vector3 weights = default;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = surface[indices[t]], b = surface[indices[t + 1]], c = surface[indices[t + 2]];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-14f) continue;
                    var w = ClosestWeights(point, a, b, c);
                    float distance = (a * w.x + b * w.y + c * w.z - point).sqrMagnitude;
                    if (distance < best) { best = distance; triangle = t; weights = w; }
                }
                if (triangle < 0 || !float.IsFinite(best)) throw new InvalidOperationException("Cannot project garment vertex: " + skin.name);
                maxDistance = Mathf.Max(maxDistance, Mathf.Sqrt(best));
                for (int s = 0; s < Shapes.Length; s++)
                    frames[s][v] = inverse.MultiplyVector(deltas[s][indices[triangle]] * weights.x
                        + deltas[s][indices[triangle + 1]] * weights.y + deltas[s][indices[triangle + 2]] * weights.z);
            }
            var normals = new Vector3[Shapes.Length][];
            var scratch = new Mesh { indexFormat = mesh.indexFormat };
            try
            {
                scratch.vertices = vertices; scratch.triangles = mesh.triangles;
                var baseNormals = mesh.normals;
                for (int s = 0; s < Shapes.Length; s++)
                {
                    scratch.vertices = vertices.Select((p, i) => p + fit[i] + frames[s][i]).ToArray();
                    scratch.RecalculateNormals();
                    var shapedNormals = scratch.normals;
                    normals[s] = shapedNormals.Select((n, i) => n - (baseNormals[i] + fitNormals[i]).normalized).ToArray();
                    if (frames[s].Any(d => !float.IsFinite(d.x) || !float.IsFinite(d.y) || !float.IsFinite(d.z)))
                        throw new InvalidOperationException("Invalid body-shape movement: " + skin.name);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(scratch); }
            outputs.Add((skin, mesh, fit, fitNormals, fitTangents, frames, normals, maxDistance));
        }
        if (outputs.Count != (expectedPartCount ?? (fullBody ? 10 : 5))) throw new InvalidOperationException("Unexpected number of reviewed source body parts.");
        foreach (var output in outputs)
        {
            output.mesh.ClearBlendShapes();
            output.mesh.AddBlendShapeFrame("SkeletonFit", 100, output.fit, output.fitNormals, output.fitTangents);
            for (int s = 0; s < Shapes.Length; s++)
            {
                output.mesh.AddBlendShapeFrame("BodyBlends." + Shapes[s], 100, output.shapes[s], output.normals[s], null);
                output.renderer.SetBlendShapeWeight(s + 1, 0);
            }
            output.mesh.UploadMeshData(false);
            EditorUtility.SetDirty(output.mesh);
            Debug.Log($"Body shape bake: {output.mesh.name}; {output.mesh.vertexCount} vertices; maximum surface distance {output.maxDistance:F4} m.");
        }
    }

    // Closest-point barycentric coordinates, including the triangle's edge and vertex regions.
    public static Vector3 ClosestWeights(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return new Vector3(1, 0, 0);
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return new Vector3(0, 1, 0);
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); return new Vector3(1 - v, v, 0); }
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return new Vector3(0, 0, 1);
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); return new Vector3(1 - w, 0, w); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0, 1 - w, w); }
        float inverse = 1 / (va + vb + vc);
        return new Vector3(va * inverse, vb * inverse, vc * inverse);
    }
}
