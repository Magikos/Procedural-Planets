using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanFullBodyReviewAuthor
{
    public const string Folder = HumanBodyReviewAuthor.Folder + "/FullBody";
    public const string PrefabPath = Folder + "/FullSourceBody.prefab";
    public const string ScenePath = "Assets/Scenes/Tests/HumanFullBodyReview.unity";

    [MenuItem("Tools/Actors/Human/Create Full Body Review")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene changes before creating the full-body review.");
        if (File.Exists(ScenePath) || Directory.Exists(Folder)) throw new IOException("Full-body review already exists. Open it to preserve fitting work.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var full = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanBodyReviewAuthor.Folder + "/BareBody.prefab"));
        full.name = "FULL SOURCE BODY / FITTED HEAD";
        foreach (var skin in full.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }.Any(id => skin.name.Contains(id)))
                UnityEngine.Object.DestroyImmediate(skin.gameObject);
        try
        {
            Fit(full);
            PrefabUtility.SaveAsPrefabAsset(full, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(full); }

        var scene = EditorSceneManager.OpenScene(HumanBodyReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanStyleReview>();
        full = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        full.name = "FULL SOURCE BODY / FITTED HEAD";
        host.Characters = new[] { host.Characters[0], host.Characters[1], full.GetComponent<Animator>(), host.Characters[2] };
        for (int i = 0; i < host.Characters.Length; i++)
            host.Characters[i].transform.SetPositionAndRotation(new Vector3(-3.6f + i * 2.4f, 0, 0), Quaternion.Euler(0, 180, 0));
        var body = host.GetComponent<HumanBodyReview>();
        var skins = host.Characters.Take(3).SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>()).ToArray();
        body.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
        body.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
        foreach (var label in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(label.gameObject);
        var names = new[] { "BARE BODY", "UPPER-BODY TRIAL", "FULL SOURCE BODY\nFitted head", "SOURCE ORIGINAL" };
        for (int i = 0; i < names.Length; i++)
        {
            var label = new GameObject(names[i]).AddComponent<TextMesh>(); label.text = names[i];
            label.fontSize = 64; label.characterSize = .017f; label.anchor = TextAnchor.MiddleCenter;
            label.transform.position = new Vector3(-3.6f + i * 2.4f, 2.5f, 0);
        }
        host.Animals[0].transform.position = new Vector3(-5.2f, 0, -1.2f);
        host.Animals[1].transform.position = new Vector3(5.2f, 0, -1.2f);
        foreach (var view in host.Views) UnityEngine.Object.DestroyImmediate(view.gameObject);
        Transform View(string name, Vector3 position, Vector3 focus)
        {
            var view = new GameObject(name).transform;
            view.position = position; view.rotation = Quaternion.LookRotation(focus - position); return view;
        }
        host.Views = new[] {
            View("All four characters", new Vector3(0, 2.2f, -10.5f), new Vector3(0, 1.15f, 0)),
            View("Full vs upper-body", new Vector3(0, 1.9f, -6.5f), new Vector3(0, 1.15f, 0)),
            View("Full body front", new Vector3(1.2f, 1.7f, -3.4f), new Vector3(1.2f, 1.05f, 0)),
            View("Full body back", new Vector3(1.2f, 1.7f, 3.4f), new Vector3(1.2f, 1.05f, 0)),
            View("Hands and wrists", new Vector3(1.2f, 1.3f, -1.9f), new Vector3(1.2f, 1.05f, 0)),
            View("Feet and knees", new Vector3(1.2f, .7f, -2.5f), new Vector3(1.2f, .4f, 0)) };
        host.SelectView(0);
        foreach (var animator in host.Characters)
        {
            var p = animator.transform.position; var q = animator.transform.rotation;
            host.Motions[0].SampleAnimation(animator.gameObject, .4f); animator.transform.SetPositionAndRotation(p, q);
        }
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Actors/Human/Refit Full Body Review")]
    public static void Refit()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before refitting.");
        var full = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Fit(full); PrefabUtility.SaveAsPrefabAsset(full, PrefabPath); AssetDatabase.SaveAssets(); }
        finally { PrefabUtility.UnloadPrefabContents(full); }
    }

    public static void Fit(GameObject full, string outputFolder = Folder, string outfit = "01")
    {
        var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.PrefabPath));
        try
        {
            var previous = AssetDatabase.LoadAssetAtPath<GameObject>(HumanBodyReviewAuthor.Folder + "/SourcePartsBody.prefab");
            var map = new Dictionary<string, string>();
            foreach (var part in source.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var old = previous.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name == "SOURCE / " + part.name);
                if (old != null) for (int i = 0; i < part.bones.Length; i++) map[part.bones[i].name] = old.bones[i].name;
            }
            foreach (var side in new[] { "L", "R" })
            {
                string suffix = side.ToLowerInvariant(); string fingerSuffix = side == "R" ? " 1" : "";
                map["UpperLeg_" + side] = "thigh_" + suffix;
                map["LowerLeg_" + side] = "calf_" + suffix;
                map["Ankle_" + side] = "foot_" + suffix;
                map["Ball_" + side] = "ball_" + suffix;
                map["Toes_" + side] = "ball_" + suffix;
                for (int i = 1; i <= 4; i++)
                {
                    string segment = Mathf.Min(i, 3).ToString("00");
                    map["IndexFinger_" + i.ToString("00") + fingerSuffix] = "index_" + segment + "_" + suffix;
                    map["Finger_" + i.ToString("00") + fingerSuffix] = "middle_" + segment + "_" + suffix;
                    if (i <= 3) map["Thumb_" + i.ToString("00") + fingerSuffix] = "thumb_" + segment + "_" + suffix;
                }
            }
            var bones = full.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            SelectOutfit(source, outfit);
            var parts = source.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.Contains("Torso") || r.name.Contains("Arm") || r.name.Contains("Hand") || r.name.Contains("Hips") || r.name.Contains("Leg")).ToArray();
            if (parts.Length != 10) throw new InvalidOperationException("Expected ten source body sections.");
            foreach (var part in parts) HumanBodyReviewAuthor.ConvertPart(part, full.transform, bones, map, outputFolder);
            HumanBodyShapeAuthor.BakeParts(full, true);
            HumanSkinAuthor.Apply(full, source);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    public static void SelectOutfit(GameObject source, string outfit)
    {
        if (outfit == "01") return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidAuthor.Folder + "/ModularCharacters.fbx");
        var available = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => r.name);
        var bones = source.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        foreach (var part in source.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!(part.name.Contains("Torso") || part.name.Contains("Arm") || part.name.Contains("Hand") || part.name.Contains("Hips") || part.name.Contains("Leg"))) continue;
            string name = part.name.Replace("_Male_01", "_Male_" + outfit);
            if (!available.TryGetValue(name, out var replacement)) throw new InvalidOperationException("Missing outfit part: " + name);
            part.sharedMesh = replacement.sharedMesh;
            part.bones = replacement.bones.Select(b => bones.TryGetValue(b.name, out var mapped) ? mapped : throw new InvalidOperationException("Missing outfit bone: " + b.name)).ToArray();
            part.name = name;
        }
    }
}
