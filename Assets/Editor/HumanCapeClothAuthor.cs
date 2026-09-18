using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanCapeClothAuthor
{
    public const string MeshPath = "Assets/Art/Characters/Human/Review/MageCapeCloth.asset";

    [MenuItem("Tools/Actors/Human/Add Cape Cloth Trial")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before adding cape cloth.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != HumanAccessoryReviewAuthor.ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open the saved accessory review scene before adding cape cloth.");
        var host = UnityEngine.Object.FindAnyObjectByType<HumanStyleReview>();
        if (host.GetComponent<HumanCapeClothReview>() != null)
            throw new InvalidOperationException("The cape cloth trial already exists.");
        var items = host.Accessories.Where(g => g.name.Contains("Mage cape")).ToArray();
        if (items.Length != 4) throw new InvalidOperationException("Expected four cape comparisons.");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            var source = items[0].GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.enabled).sharedMesh;
            var first = Subdivide(source);
            try { mesh = Subdivide(first); }
            finally { UnityEngine.Object.DestroyImmediate(first); }
            mesh.name = "Mage cape / cloth subdivision";
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        var capes = new List<Cloth>();
        foreach (var item in items)
        {
            var actor = host.Characters.Single(a => item.transform.IsChildOf(a.transform));
            var skin = item.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.enabled);
            skin.sharedMesh = mesh;
            var cloth = skin.gameObject.AddComponent<Cloth>();
            var points = cloth.vertices;
            float top = points.Max(p => p.y);
            cloth.coefficients = points.Select(p => new ClothSkinningCoefficient
            {
                maxDistance = top - p.y < .12f ? 0 : Mathf.Min(1.5f, (top - p.y) * 1.5f),
                collisionSphereDistance = 0
            }).ToArray();
            cloth.useGravity = true;
            cloth.damping = .3f;
            cloth.stretchingStiffness = .99f;
            cloth.bendingStiffness = .05f;
            cloth.useTethers = true;
            cloth.clothSolverFrequency = 120;
            cloth.enableContinuousCollision = true;
            cloth.friction = .5f;
            var colliders = new List<CapsuleCollider>();
            void Capsule(HumanBodyBones bone, Vector3 center, float radius, float height)
            {
                var obj = new GameObject("Cape cloth collision / " + bone);
                obj.transform.SetPositionAndRotation(actor.transform.TransformPoint(center), actor.transform.rotation);
                obj.transform.SetParent(actor.GetBoneTransform(bone), true);
                var collider = obj.AddComponent<CapsuleCollider>();
                collider.radius = radius; collider.height = height; collider.isTrigger = true;
                colliders.Add(collider);
            }
            Capsule(HumanBodyBones.UpperChest, new Vector3(0, 1.18f, 0), .22f, .65f);
            Capsule(HumanBodyBones.Hips, new Vector3(0, .86f, 0), .23f, .48f);
            Capsule(HumanBodyBones.LeftUpperLeg, new Vector3(-.1f, .58f, 0), .13f, .65f);
            Capsule(HumanBodyBones.RightUpperLeg, new Vector3(.1f, .58f, 0), .13f, .65f);
            cloth.capsuleColliders = colliders.ToArray();
            item.name = item.name.Replace("(no cloth simulation)", "(cloth trial)");
            capes.Add(cloth);
        }
        var controls = host.gameObject.AddComponent<HumanCapeClothReview>();
        controls.Review = host; controls.Body = host.GetComponent<HumanBodyReview>(); controls.Capes = capes.ToArray();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static Mesh Subdivide(Mesh source)
    {
        if (source == null || source.subMeshCount != 1 || source.blendShapeCount != 0
            || source.normals.Length != source.vertexCount || source.uv.Length != source.vertexCount
            || source.boneWeights.Length != source.vertexCount)
            throw new ArgumentException("Expected the single-material skinned cape mesh.", nameof(source));
        var vertices = source.vertices.ToList();
        var normals = source.normals.ToList();
        var uv = source.uv.ToList();
        var weights = source.boneWeights.ToList();
        var edges = new Dictionary<(int, int), int>();
        int Midpoint(int a, int b)
        {
            var edge = a < b ? (a, b) : (b, a);
            if (edges.TryGetValue(edge, out int cached)) return cached;
            int index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * .5f);
            normals.Add((normals[a] + normals[b]).normalized);
            uv.Add((uv[a] + uv[b]) * .5f);
            var amounts = new Dictionary<int, float>();
            void Add(int bone, float amount)
            {
                amounts[bone] = (amounts.TryGetValue(bone, out var old) ? old : 0) + amount * .5f;
            }
            foreach (var weight in new[] { weights[a], weights[b] })
            {
                Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
            }
            var best = amounts.OrderByDescending(p => p.Value).Take(4).ToArray();
            float sum = best.Sum(p => p.Value);
            if (sum <= 0) throw new InvalidOperationException("Cape vertex has no bone influence.");
            var result = new BoneWeight();
            for (int i = 0; i < best.Length; i++)
            {
                int bone = best[i].Key; float amount = best[i].Value / sum;
                if (i == 0) { result.boneIndex0 = bone; result.weight0 = amount; }
                if (i == 1) { result.boneIndex1 = bone; result.weight1 = amount; }
                if (i == 2) { result.boneIndex2 = bone; result.weight2 = amount; }
                if (i == 3) { result.boneIndex3 = bone; result.weight3 = amount; }
            }
            weights.Add(result); edges.Add(edge, index); return index;
        }
        var triangles = new List<int>();
        var original = source.triangles;
        for (int i = 0; i < original.Length; i += 3)
        {
            int a = original[i], b = original[i + 1], c = original[i + 2];
            int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
            triangles.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
        }
        var mesh = new Mesh { name = source.name + "_Subdivided" };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
        mesh.boneWeights = weights.ToArray(); mesh.bindposes = source.bindposes;
        mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        return mesh;
    }
}
