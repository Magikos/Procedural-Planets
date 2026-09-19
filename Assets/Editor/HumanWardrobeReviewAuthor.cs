using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanWardrobeReviewAuthor
{
    public const string Folder = HumanBodyReviewAuthor.Folder + "/Wardrobe";
    public const string ScenePath = "Assets/Scenes/Tests/HumanWardrobeReview.unity";
    public static readonly string[] Parts = { "SM_Chr_Rider_01", "SM_Chr_Soldier_Male_01", "SM_Chr_Blacksmith_Female_01", "SM_Chr_Priest_01" };
    public const string Helmet = "Chr_HeadCoverings_No_Hair_01";

    public static void BuildOutfit(int index)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building outfits.");
        if (index < 0 || index >= Parts.Length) throw new ArgumentOutOfRangeException(nameof(index));
        string part = Parts[index];
        string role = HumanOutfitConverter.RoleName(part);
        if (File.Exists(Folder + "/" + role + "_Original.prefab") || File.Exists(Folder + "/" + role + "_Fit.prefab"))
            throw new IOException("Wardrobe outfit already exists: " + part);
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        HumanTownsfolkReviewAuthor.Build(part, Folder);
        AssetDatabase.SaveAssets();
    }

    public static void BuildHelmet()
    {
        string path = Folder + "/SoldierHelmet_Fit.prefab";
        if (EditorApplication.isPlaying || File.Exists(path)) throw new InvalidOperationException("Stop Play mode and preserve the existing helmet trial.");
        var target = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + HumanOutfitConverter.RoleName(Parts[1]) + "_Fit.prefab"));
        var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.Folder + "/ModularCharacters.fbx"));
        try
        {
            var skin = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == Helmet);
            skin.sharedMaterials = AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath)
                .GetComponentsInChildren<SkinnedMeshRenderer>().First().sharedMaterials;
            var bones = target.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            var map = HumanTownsfolkReviewAuthor.BoneMap();
            map["Root"] = target.name;
            map["Head_Attachment"] = "head";
            map["Shoulder_Attachment_R"] = map["Clavicle_R"];
            map["Chest_Attachment"] = map["Spine_03"];
            foreach (string name in new[] { "Head", "Eyes", "Eyebrows", "Jaw" }) map[name] = "head";
            HumanBodyReviewAuthor.ConvertPart(skin, target.transform, bones, map, Folder);
            // Bake only the new helmet. The soldier body is already a shared, cached asset.
            var body = target.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "SOURCE / " + Parts[1]);
            string bodyName = body.name; body.name = "Cached soldier body";
            try { HumanBodyShapeAuthor.BakeParts(target, true, 1); }
            finally { body.name = bodyName; }
            PrefabUtility.SaveAsPrefabAsset(target, path);
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(source); }
    }

    [MenuItem("Tools/Actors/Human/Create Wardrobe Review Scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty || File.Exists(ScenePath))
            throw new InvalidOperationException("Stop Play mode and preserve scene edits before creating the wardrobe scene.");
        foreach (var part in Parts)
            foreach (string suffix in new[] { "_Original", "_Fit" })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + HumanOutfitConverter.RoleName(part) + suffix + ".prefab") == null)
                    throw new InvalidOperationException("Build all wardrobe outfits first.");
        var scene = EditorSceneManager.OpenScene(HumanTownsfolkReviewAuthor.ScenePath);
        GameObject.Find("Neutral backdrop").transform.position = new Vector3(0, 4, 22);
        GameObject.Find("HouseWallDoor_01").transform.position = new Vector3(-7, 0, 4);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanStyleReview>();
        foreach (var actor in host.Characters) UnityEngine.Object.DestroyImmediate(actor.gameObject);
        foreach (var label in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(label.gameObject);
        foreach (var view in host.Views) UnityEngine.Object.DestroyImmediate(view.gameObject);
        var actors = new System.Collections.Generic.List<Animator>();
        var views = new System.Collections.Generic.List<Transform>();
        string[] labels = { "RIDER", "SOLDIER / ARMOR", "BLACKSMITH / APRON", "PRIEST / ROBE" };
        for (int i = 0; i < Parts.Length; i++)
        {
            for (int side = 0; side < 2; side++)
            {
                string suffix = side == 0 ? "_Original" : "_Fit";
                var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + HumanOutfitConverter.RoleName(Parts[i]) + suffix + ".prefab"));
                actor.name = Parts[i] + suffix;
                actor.transform.SetPositionAndRotation(new Vector3(side == 0 ? -1.2f : 1.2f, 0, i * 5), Quaternion.Euler(0, 180, 0));
                actors.Add(actor.GetComponent<Animator>());
                var label = new GameObject(labels[i] + suffix).AddComponent<TextMesh>();
                label.text = labels[i] + (side == 0 ? " / SOURCE" : " / FITTED");
                label.fontSize = 64; label.characterSize = .014f; label.anchor = TextAnchor.MiddleCenter;
                label.transform.position = actor.transform.position + Vector3.up * 2.5f;
            }
            AddView(labels[i] + " pair", new Vector3(0, 1.8f, i * 5 - 5), new Vector3(0, 1.1f, i * 5));
            AddView(labels[i] + " detail", new Vector3(1.2f, 1.5f, i * 5 - 3), new Vector3(1.2f, 1, i * 5));
            AddView(labels[i] + " back", new Vector3(1.2f, 1.5f, i * 5 + 3), new Vector3(1.2f, 1, i * 5));
        }
        var helmet = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/SoldierHelmet_Fit.prefab"));
        helmet.name = "Modular helmet / FITTED";
        helmet.transform.SetPositionAndRotation(new Vector3(3.6f, 0, 5), Quaternion.Euler(0, 180, 0));
        actors.Add(helmet.GetComponent<Animator>());
        AddView("Modular helmet", new Vector3(3.6f, 1.75f, 3.2f), new Vector3(3.6f, 1.6f, 5));
        host.Characters = actors.ToArray(); host.Views = views.ToArray();
        host.Accessories = Array.Empty<GameObject>();
        var bodyReview = host.GetComponent<HumanBodyReview>();
        var skins = actors.Where(a => a.name.Contains("_Fit") || a.name.Contains("FITTED"))
            .SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>()).ToArray();
        bodyReview.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
        bodyReview.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
        host.SelectView(0);
        // Add display flooring for the extra rows without changing the previous scene.
        for (int i = 1; i < Parts.Length; i++)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Wardrobe floor " + i;
            floor.transform.position = new Vector3(0, -.1f, i * 5);
            floor.transform.localScale = new Vector3(12, .2f, 5);
        }
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();

        void AddView(string name, Vector3 position, Vector3 focus)
        {
            var view = new GameObject(name).transform;
            view.position = position; view.LookAt(focus); views.Add(view);
        }
    }
}
