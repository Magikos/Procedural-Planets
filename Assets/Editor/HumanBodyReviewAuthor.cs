using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanBodyReviewAuthor
{
    public const string Folder = HumanTrialAuthor.Folder + "/BodyReview";
    public const string ScenePath = "Assets/Scenes/Tests/HumanBodyReview.unity";
    const string Parts = HumanTrialAuthor.Folder + "/BaseParts";

    [MenuItem("Tools/Actors/Human/Create Body Parts Review")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and save scene changes before creating the body review.");
        if (File.Exists(ScenePath) || Directory.Exists(Folder))
            throw new IOException("Body review already exists. Open it to preserve fitting edits.");
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.OpenScene(HumanStyleReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanStyleReview>();
        var source = host.Characters[1];
        UnityEngine.Object.DestroyImmediate(host.Characters[0].gameObject);
        foreach (var root in scene.GetRootGameObjects())
            if (root.name.Contains("Legacy") || root.GetComponent<TextMesh>() != null)
                UnityEngine.Object.DestroyImmediate(root);
        host.Accessories = Array.Empty<GameObject>();

        var bare = Assemble();
        PrefabUtility.SaveAsPrefabAsset(bare, Folder + "/BareBody.prefab");
        var hybrid = UnityEngine.Object.Instantiate(bare);
        hybrid.name = "BODY WITH SOURCE TORSO AND ARMS";
        // Use a fresh prefab instance: the comparison actor has already been posed in the saved scene.
        var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath));
        try
        {
            var map = new Dictionary<string, string> {
                {"Hips","pelvis"}, {"Spine_01","spine_01"}, {"Spine_02","spine_02"}, {"Spine_03","spine_03"}, {"Neck","neck_01"},
                {"Clavicle_L","clavicle_l"}, {"Clavicle_R","clavicle_r"}, {"Shoulder_L","upperarm_l"}, {"Shoulder_R","upperarm_r"},
                {"Elbow_L","lowerarm_l"}, {"Elbow_R","lowerarm_r"}, {"Hand_L","hand_l"}, {"Hand_R","hand_r"} };
            var targetBones = hybrid.GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);
            foreach (var skin in hybrid.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR" }.Any(id => skin.name.Contains(id)))
                    UnityEngine.Object.DestroyImmediate(skin.gameObject);
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (skin.name.Contains("Torso") || skin.name.Contains("ArmUpper") || skin.name.Contains("ArmLower"))
                    ConvertPart(skin, hybrid.transform, targetBones, map);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        HumanBodyShapeAuthor.BakeParts(hybrid);
        PrefabUtility.SaveAsPrefabAsset(hybrid, Folder + "/SourcePartsBody.prefab");
        bare.transform.SetPositionAndRotation(new Vector3(-2.4f, 0, 0), Quaternion.Euler(0, 180, 0));
        hybrid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 180, 0));
        source.transform.SetPositionAndRotation(new Vector3(2.4f, 0, 0), Quaternion.Euler(0, 180, 0));
        host.Characters = new[] { bare.GetComponent<Animator>(), hybrid.GetComponent<Animator>(), source };
        var controls = host.gameObject.AddComponent<HumanBodyReview>();
        controls.NativeParts = bare.GetComponentsInChildren<SkinnedMeshRenderer>().Concat(hybrid.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => !r.name.StartsWith("SOURCE"))).ToArray();
        controls.ConvertedParts = hybrid.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.StartsWith("SOURCE")).ToArray();
        Label("BARE BODY", new Vector3(-2.4f, 2.5f, 0));
        Label("SOURCE TORSO + ARMS\nFitted head, hands and legs", new Vector3(0, 2.5f, 0));
        Label("SOURCE ORIGINAL", new Vector3(2.4f, 2.5f, 0));
        foreach (var view in host.Views) UnityEngine.Object.DestroyImmediate(view.gameObject);
        Transform View(string name, Vector3 position, Vector3 focus)
        {
            var view = new GameObject(name).transform;
            view.position = position; view.rotation = Quaternion.LookRotation(focus - position); return view;
        }
        host.Views = new[] {
            View("Body comparison", new Vector3(0, 2.2f, -9), new Vector3(0, 1.15f, 0)),
            View("Hybrid front", new Vector3(0, 1.8f, -3.4f), new Vector3(0, 1.15f, 0)),
            View("Hybrid back", new Vector3(0, 1.8f, 3.4f), new Vector3(0, 1.15f, 0)),
            View("Bare body", new Vector3(-2.4f, 1.8f, -3.4f), new Vector3(-2.4f, 1.15f, 0)) };
        host.SelectView(0);
        foreach (var animator in host.Characters)
        {
            var p = animator.transform.position; var q = animator.transform.rotation;
            host.Motions[0].SampleAnimation(animator.gameObject, .4f);
            animator.transform.SetPositionAndRotation(p, q);
        }
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
    }

    static GameObject Assemble()
    {
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanTrialAuthor.PrefabPath));
        root.name = "BARE BODY";
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) UnityEngine.Object.DestroyImmediate(renderer);
        var bones = root.GetComponentsInChildren<Transform>(true).GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
        var material = AssetDatabase.LoadAssetAtPath<Material>(HumanTrialAuthor.Folder + "/Human.mat");
        foreach (var path in Directory.GetFiles(Parts, "*.fbx").OrderBy(p => p))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
            foreach (var source in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var skin = new GameObject(source.name).AddComponent<SkinnedMeshRenderer>();
                skin.transform.SetParent(root.transform, false);
                skin.sharedMesh = source.sharedMesh;
                skin.bones = source.bones.Select(b => bones.TryGetValue(b.name, out var target) ? target : throw new InvalidOperationException("Missing body bone: " + b.name)).ToArray();
                skin.rootBone = bones["pelvis"];
                skin.sharedMaterials = Enumerable.Repeat(material, source.sharedMaterials.Length).ToArray();
                skin.localBounds = new Bounds(new Vector3(0, 1, 0), Vector3.one * 4);
                skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true;
            }
        }
        return root;
    }

    public static void ConvertPart(SkinnedMeshRenderer source, Transform target, Dictionary<string, Transform> targetBones, Dictionary<string, string> map, string outputFolder = Folder)
    {
        var bones = source.bones.Select(b => map.TryGetValue(b.name, out var name) && targetBones.TryGetValue(name, out var bone)
            ? bone : throw new InvalidOperationException("Unmapped source bone: " + b.name)).ToArray();
        var original = source.sharedMesh;
        var vertices = original.vertices; var normals = original.normals; var weights = original.boneWeights;
        if (weights.Length != vertices.Length) throw new InvalidOperationException("Expected four-weight skin data: " + source.name);
        var baseMatrix = target.worldToLocalMatrix * source.transform.localToWorldMatrix;
        var sourceRig = source.transform.root.GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);
        var sourceHip = target.InverseTransformPoint(sourceRig["Hips"].position);
        var sourceNeck = target.InverseTransformPoint(sourceRig["Neck"].position);
        var targetHip = target.InverseTransformPoint(targetBones["pelvis"].position);
        var targetNeck = target.InverseTransformPoint(targetBones["neck_01"].position);
        if (Mathf.Abs(sourceNeck.y - sourceHip.y) < .001f) throw new InvalidOperationException("Invalid torso landmarks.");
        // Bone axes differ between these rigs. Translate anatomical anchors in a common rest frame.
        // Numbered spine bones are not equivalent landmarks: preserve their relative hip-to-neck height.
        var matrices = bones.Select((bone, i) => {
            var origin = baseMatrix.MultiplyPoint3x4(original.bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero));
            var anchor = target.InverseTransformPoint(bone.position);
            if (source.bones[i].name.StartsWith("Spine_"))
                anchor = Vector3.LerpUnclamped(targetHip, targetNeck, (origin.y - sourceHip.y) / (sourceNeck.y - sourceHip.y));
            var parent = source.bones[i].parent;
            if (parent != null && map.TryGetValue(parent.name, out var parentTarget) && parentTarget == map[source.bones[i].name])
                anchor += target.InverseTransformVector(source.bones[i].position - parent.position);
            return Matrix4x4.Translate(anchor - origin) * baseMatrix;
        }).ToArray();
        var rest = new Vector3[vertices.Length]; var fitted = new Vector3[vertices.Length]; var fittedNormals = new Vector3[vertices.Length];
        var delta = new Vector3[vertices.Length]; var normalDelta = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            rest[i] = baseMatrix.MultiplyPoint3x4(vertices[i]);
            fitted[i] = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1
                + matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
            fittedNormals[i] = (matrices[w.boneIndex0].inverse.transpose.MultiplyVector(normals[i]) * w.weight0 + matrices[w.boneIndex1].inverse.transpose.MultiplyVector(normals[i]) * w.weight1
                + matrices[w.boneIndex2].inverse.transpose.MultiplyVector(normals[i]) * w.weight2 + matrices[w.boneIndex3].inverse.transpose.MultiplyVector(normals[i]) * w.weight3).normalized;
            normals[i] = baseMatrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
            delta[i] = fitted[i] - rest[i]; normalDelta[i] = fittedNormals[i] - normals[i];
        }
        string meshPath = outputFolder + "/" + source.name + "_LandmarkFit.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool exists = mesh != null;
        if (!exists) mesh = new Mesh();
        mesh.Clear(); mesh.ClearBlendShapes(); mesh.name = source.name + "_LandmarkFit"; mesh.indexFormat = original.indexFormat;
        mesh.vertices = rest; mesh.normals = normals; mesh.uv = original.uv; mesh.uv2 = original.uv2;
        mesh.tangents = original.tangents; mesh.colors = original.colors;
        mesh.boneWeights = weights;
        mesh.subMeshCount = original.subMeshCount;
        for (int i = 0; i < original.subMeshCount; i++) mesh.SetTriangles(original.GetTriangles(i), i);
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * target.localToWorldMatrix).ToArray();
        mesh.AddBlendShapeFrame("SkeletonFit", 100, delta, normalDelta, null);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        if (exists) EditorUtility.SetDirty(mesh);
        else AssetDatabase.CreateAsset(mesh, meshPath);
        var renderer = target.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == "SOURCE / " + source.name)
            ?? new GameObject("SOURCE / " + source.name).AddComponent<SkinnedMeshRenderer>();
        renderer.transform.SetParent(target, false); renderer.sharedMesh = mesh;
        renderer.sharedMaterials = source.sharedMaterials; renderer.bones = bones; renderer.rootBone = targetBones["pelvis"];
        renderer.localBounds = new Bounds(new Vector3(0, 1, 0), Vector3.one * 4);
        renderer.updateWhenOffscreen = true; renderer.forceMatrixRecalculationPerRender = true;
        renderer.SetBlendShapeWeight(0, 100);
    }

    [MenuItem("Tools/Actors/Human/Refit Review Source Parts")]
    public static void Refit()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before refitting.");
        var target = PrefabUtility.LoadPrefabContents(Folder + "/SourcePartsBody.prefab");
        var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath));
        try
        {
            var bones = target.GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var converted = target.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == "SOURCE / " + skin.name);
                if (converted == null) continue;
                var map = skin.bones.Select((b, i) => new { b.name, targetName = converted.bones[i].name }).ToDictionary(p => p.name, p => p.targetName);
                ConvertPart(skin, target.transform, bones, map);
            }
            HumanBodyShapeAuthor.BakeParts(target);
            PrefabUtility.SaveAsPrefabAsset(target, Folder + "/SourcePartsBody.prefab");
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(target); UnityEngine.Object.DestroyImmediate(source); }
    }

    static void Label(string text, Vector3 position)
    {
        var label = new GameObject(text).AddComponent<TextMesh>(); label.text = text;
        label.fontSize = 64; label.characterSize = .019f; label.anchor = TextAnchor.MiddleCenter;
        label.transform.position = position;
    }
}
