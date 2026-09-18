using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanTownsfolkReviewAuthor
{
    public const string Folder = HumanBodyReviewAuthor.Folder + "/Townsfolk";
    public const string ScenePath = "Assets/Scenes/Tests/HumanTownsfolkReview.unity";
    const string ModelPath = HumanTrialAuthor.Folder + "/Review/Townsfolk_Characters.fbx";
    static readonly string[] Parts = { "SM_Chr_Monk_01", "SM_Chr_Peasant_Male_01" };

    [MenuItem("Tools/Actors/Human/Create Townsfolk Clothing Review")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene edits before creating the Townsfolk review.");
        if (Directory.Exists(Folder) || File.Exists(ScenePath)) throw new IOException("Townsfolk review already exists.");
        ConfigureAvatar();
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        foreach (var part in Parts) Build(part);
        var scene = EditorSceneManager.OpenScene(HumanOutfitReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanStyleReview>();
        var paths = new[] { Parts[0] + "_Original", Parts[0] + "_Fit", Parts[1] + "_Fit", Parts[1] + "_Original" };
        for (int i = 0; i < paths.Length; i++)
        {
            var old = host.Characters[i];
            var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + paths[i] + ".prefab"));
            actor.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            host.Characters[i] = actor.GetComponent<Animator>();
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var body = host.GetComponent<HumanBodyReview>();
        var skins = host.Characters.Skip(1).Take(2).SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>()).ToArray();
        body.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
        body.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
        var labels = UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None).OrderBy(t => t.transform.position.x).ToArray();
        var names = new[] { "MONK / ORIGINAL", "MONK / FITTED", "PEASANT / FITTED", "PEASANT / ORIGINAL" };
        if (labels.Length != names.Length) throw new InvalidOperationException("Expected four comparison labels.");
        for (int i = 0; i < labels.Length; i++) labels[i].text = names[i];
        host.Views[1].name = "Compare Townsfolk clothing";
        host.Views[2].name = "Peasant front"; host.Views[3].name = "Peasant back";
        var monkFront = new GameObject("Monk front").transform;
        monkFront.position = new Vector3(-1.2f, 1.7f, -3.4f); monkFront.LookAt(new Vector3(-1.2f, 1.05f, 0));
        var monkBack = new GameObject("Monk back").transform;
        monkBack.position = new Vector3(-1.2f, 1.7f, 3.4f); monkBack.LookAt(new Vector3(-1.2f, 1.05f, 0));
        host.Views = host.Views.Concat(new[] { monkFront, monkBack }).ToArray(); host.SelectView(0);
        foreach (var actor in host.Characters)
        {
            var p = actor.transform.position; var q = actor.transform.rotation;
            host.Motions[0].SampleAnimation(actor.gameObject, .4f); actor.transform.SetPositionAndRotation(p, q);
        }
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
    }

    public static void ConfigureAvatar()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        var description = importer.humanDescription;
        int hips = Array.FindIndex(description.human, h => h.humanName == "Hips");
        if (hips < 0) throw new InvalidOperationException("The Townsfolk avatar has no hips mapping.");
        // The source metadata assigns the stationary root as the Humanoid pelvis.
        description.human[hips].boneName = "Hips";
        importer.humanDescription = description; importer.SaveAndReimport();
    }

    public static void Build(string part, string outputFolder = Folder, UnityEngine.SceneManagement.Scene? workingScene = null, bool preserveMageHood = false)
    {
        var scene = workingScene ?? UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var source = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
        GameObject target = null; Mesh bodyMesh = null;
        try
        {
            source.name = part + " / ORIGINAL";
            foreach (var r in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name != part) UnityEngine.Object.DestroyImmediate(r.gameObject);
            var skin = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            skin.gameObject.SetActive(true); skin.enabled = true; skin.updateWhenOffscreen = true;
            skin.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(HumanTrialAuthor.Folder + "/Review/Townsfolk.mat") };
            var animator = source.GetComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().Single(a => a.isValid && a.isHuman);
            animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false;
            PrefabUtility.SaveAsPrefabAsset(source, outputFolder + "/" + part + "_Original.prefab");
            bodyMesh = RemoveHead(skin, preserveMageHood); skin.sharedMesh = bodyMesh;

            target = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanFullBodyReviewAuthor.PrefabPath), scene);
            target.name = part + " / FITTED";
            foreach (var r in target.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.StartsWith("SOURCE / ")))
                UnityEngine.Object.DestroyImmediate(r.gameObject);
            var bones = target.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            var map = BoneMap();
            map["Root"] = target.name;
            foreach (var name in new[] { "Head", "Eyes", "Eyebrows", "Jaw" }) map[name] = "head";
            HumanBodyReviewAuthor.ConvertPart(skin, target.transform, bones, map, outputFolder);
            HumanBodyShapeAuthor.BakeParts(target, true, 1);
            HumanSkinAuthor.Apply(target, source);
            PrefabUtility.SaveAsPrefabAsset(target, outputFolder + "/" + part + "_Fit.prefab");
        }
        finally
        {
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(source);
            if (bodyMesh != null) UnityEngine.Object.DestroyImmediate(bodyMesh);
        }
    }

    public static Dictionary<string, string> BoneMap()
    {
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath);
        var fitted = AssetDatabase.LoadAssetAtPath<GameObject>(HumanFullBodyReviewAuthor.PrefabPath);
        var result = new Dictionary<string, string>();
        foreach (var part in original.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var match = fitted.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == "SOURCE / " + part.name);
            if (match == null) continue;
            for (int i = 0; i < part.bones.Length; i++) result[part.bones[i].name] = match.bones[i].name;
        }
        return result;
    }

    public static Mesh RemoveHead(SkinnedMeshRenderer skin, bool preserveMageHood = false)
    {
        if (preserveMageHood && skin.name != "SM_Chr_Mage_01")
            throw new ArgumentException("The reviewed hood palette applies only to SM_Chr_Mage_01.");
        var hood = new bool[skin.sharedMesh.vertexCount];
        if (preserveMageHood)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                string path = AssetDatabase.GetAssetPath(skin.sharedMaterial.GetTexture("_BaseMap"));
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new IOException("Cannot read hood palette: " + path);
                var uv = skin.sharedMesh.uv;
                for (int i = 0; i < uv.Length; i++)
                {
                    string color = ColorUtility.ToHtmlStringRGB(texture.GetPixel(
                        Mathf.Clamp((int)(uv[i].x * texture.width), 0, texture.width - 1),
                        Mathf.Clamp((int)(uv[i].y * texture.height), 0, texture.height - 1)));
                    // Reviewed Mage cloth and trim. Skin, hair, eyes, and facial features stay excluded.
                    hood[i] = color == "497D7E" || color == "C59E60";
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        var head = new HashSet<string> { "Head", "Eyes", "Eyebrows", "Jaw" };
        float HeadWeight(BoneWeight w) =>
            (head.Contains(skin.bones[w.boneIndex0].name) ? w.weight0 : 0) +
            (head.Contains(skin.bones[w.boneIndex1].name) ? w.weight1 : 0) +
            (head.Contains(skin.bones[w.boneIndex2].name) ? w.weight2 : 0) +
            (head.Contains(skin.bones[w.boneIndex3].name) ? w.weight3 : 0);
        var weights = skin.sharedMesh.boneWeights.Select(HeadWeight).ToArray();
        var source = skin.sharedMesh.triangles; var kept = new List<int>();
        // Combined characters have no interchangeable head mesh. Cut at the reviewed head-weight boundary.
        for (int i = 0; i < source.Length; i += 3)
            if ((weights[source[i]] <= .5f && weights[source[i + 1]] <= .5f && weights[source[i + 2]] <= .5f)
                || (hood[source[i]] && hood[source[i + 1]] && hood[source[i + 2]]))
            { kept.Add(source[i]); kept.Add(source[i + 1]); kept.Add(source[i + 2]); }
        if (kept.Count == 0 || kept.Count == source.Length) throw new InvalidOperationException("Cannot isolate the source head: " + skin.name);
        var mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
        mesh.name = skin.name + "_BodyOnly"; mesh.triangles = kept.ToArray(); return mesh;
    }
}
