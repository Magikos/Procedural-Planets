using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanOutfitReviewAuthor
{
    public const string Folder = HumanBodyReviewAuthor.Folder + "/Outfit02";
    public const string PrefabPath = Folder + "/Outfit02Body.prefab";
    public const string ScenePath = "Assets/Scenes/Tests/HumanOutfitReview.unity";

    [MenuItem("Tools/Actors/Human/Create Outfit Comparison")]
    public static void Create()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene edits before creating the outfit comparison.");
        if (File.Exists(ScenePath) || Directory.Exists(Folder)) throw new IOException("Outfit comparison already exists.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var candidate = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanFullBodyReviewAuthor.PrefabPath));
        try
        {
            foreach (var skin in candidate.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.name.StartsWith("SOURCE / ")))
                UnityEngine.Object.DestroyImmediate(skin.gameObject);
            HumanFullBodyReviewAuthor.Fit(candidate, Folder, "02");
            candidate.name = "OUTFIT 02 / FITTED";
            PrefabUtility.SaveAsPrefabAsset(candidate, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(candidate); }

        var scene = EditorSceneManager.OpenScene(HumanFullBodyReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanStyleReview>();
        var paths = new[] { HumanFullBodyReviewAuthor.PrefabPath, PrefabPath, HumanoidAuthor.PrefabPath };
        for (int i = 1; i < 4; i++)
        {
            var old = host.Characters[i];
            var replacement = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i - 1]));
            replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            if (i == 3) HumanFullBodyReviewAuthor.SelectOutfit(replacement, "02");
            host.Characters[i] = replacement.GetComponent<Animator>();
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        var body = host.GetComponent<HumanBodyReview>();
        var skins = host.Characters.Take(3).SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>()).ToArray();
        body.NativeParts = skins.Where(r => !r.name.StartsWith("SOURCE / ")).ToArray();
        body.ConvertedParts = skins.Where(r => r.name.StartsWith("SOURCE / ")).ToArray();
        var labels = UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None).OrderBy(t => t.transform.position.x).ToArray();
        var names = new[] { "BARE BODY", "OUTFIT 01 / FITTED", "OUTFIT 02 / FITTED", "OUTFIT 02 / SOURCE" };
        if (labels.Length != names.Length) throw new InvalidOperationException("Expected four comparison labels.");
        for (int i = 0; i < labels.Length; i++) labels[i].text = names[i];
        host.Views[1].name = "Compare outfits"; host.SelectView(0);
        foreach (var actor in host.Characters)
        {
            var p = actor.transform.position; var q = actor.transform.rotation;
            host.Motions[0].SampleAnimation(actor.gameObject, .4f); actor.transform.SetPositionAndRotation(p, q);
        }
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
    }
}
