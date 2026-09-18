using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanTrialAuthor
{
    public const string Folder = "Assets/Art/Characters/Human";
    public const string PrefabPath = Folder + "/Human.prefab";
    public const string MotionScene = "Assets/Scenes/Tests/HumanAnimationReview.unity";
    public const string GameplayScene = "Assets/Scenes/Tests/HumanGameplayReview.unity";
    const string Source = "D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/";

    [MenuItem("Tools/Actors/Human/Import Trial Character")]
    public static void Import()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before importing the human.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        string mesh = CopyArt(Source + "Meshes/Starter_01.asset", Folder + "/Starter_01.asset");
        CopyArt(Source + "Meshes/Starter_01-avatar.asset", Folder + "/Starter_01-avatar.asset");
        string texture = CopyArt(Source + "Textures/T_Starter_01ColorMap.png", Folder + "/ColorMap.png");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texture);
        textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
        textureImporter.mipmapEnabled = false; textureImporter.filterMode = FilterMode.Point;
        textureImporter.SaveAndReimport();
        var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Characters/Baseline/Hero.mat");
        if (reference == null) throw new InvalidOperationException("The existing hero material is required.");
        var material = new Material(reference) { name = "Human Hero" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        material.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(material, Folder + "/Human.mat");
        string text = File.ReadAllText(Source + "Starter_01.prefab");
        text = text.Replace("be6a3c9ccc7268245ba1d9880cc23b78", AssetDatabase.AssetPathToGUID(Folder + "/Human.mat"));
        File.WriteAllText(PrefabPath, text);
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The human requires a valid Humanoid avatar.");
            animator.applyRootMotion = false; animator.fireEvents = false; animator.runtimeAnimatorController = null;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null || skin.bones.Any(b => b == null)) throw new InvalidOperationException("The human has missing skin references.");
                skin.forceMatrixRecalculationPerRender = true;
            }
            var rig = animator.gameObject.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(animator, rig);
            UnityEngine.Object.DestroyImmediate(rig);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllText(Folder + "/SOURCE.md", "# Human trial\n\nSource: " + Source +
            "\n\nStarter_01 prefab, combined mesh, saved Humanoid avatar, and ColorMap only. Source mesh, avatar, and texture GUIDs are preserved. " +
            "The project-owned prefab uses Planet/PropLit through the existing hero material convention. No vendor scripts, shaders, or creator tools imported. " +
            "Special source shader effects are not represented by this material conversion.\n");
        AssetDatabase.SaveAssets();
    }

    public static string CopyArt(string source, string destination)
    {
        if (!File.Exists(source) || !File.Exists(source + ".meta")) throw new FileNotFoundException("Required source art or metadata missing.", source);
        string guid = Regex.Match(File.ReadAllText(source + ".meta"), @"(?m)^guid: (\w+)").Groups[1].Value;
        if (guid.Length != 32) throw new InvalidOperationException("Invalid source GUID: " + source);
        string existing = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(existing))
        {
            if (!File.ReadAllBytes(existing).SequenceEqual(File.ReadAllBytes(source)))
                throw new InvalidOperationException("Source GUID identifies different project content: " + existing);
            return existing;
        }
        if (File.Exists(destination) || File.Exists(destination + ".meta")) throw new IOException("Unresolved destination collision: " + destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination); File.Copy(source + ".meta", destination + ".meta");
        return destination;
    }

    [MenuItem("Tools/Actors/Human/Create Animation Trial Scene")]
    public static void CreateMotionScene()
        => CreateMotionSceneAt(MotionScene, PrefabPath);

    [MenuItem("Tools/Actors/Human/Create Gameplay Review")]
    public static void CreateGameplayScene()
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene changes before creating the gameplay review.");
        if (File.Exists(GameplayScene)) throw new IOException("The gameplay review already exists. Open it instead of overwriting it.");
        var outfit = HumanOutfitConverter.Convert("SM_Chr_Rider_01", "v2", false, false);
        CreateMotionSceneAt(GameplayScene, outfit.candidatePrefab);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        // The wrist reaches the full panel swing from this reviewed distance.
        host.Panel.position += host.transform.forward * .05f;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void CreateMotionSceneAt(string scenePath, string characterPath)
    {
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Stop Play mode and preserve scene changes before creating the trial.");
        Import();
        if (File.Exists(scenePath)) throw new IOException("The trial scene already exists. Open it instead of overwriting fitted work.");
        var scene = EditorSceneManager.OpenScene(HumanoidReviewAuthor.ScenePath);
        var host = UnityEngine.Object.FindFirstObjectByType<HumanoidAnimationPrototype>();
        host.CharacterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
        host.gameObject.AddComponent<ActorReviewLighting>().Sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
        host.CrawlCycleDistances = new Vector4(
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(host.TraversalClips[3], host.CharacterPrefab, Vector3.forward),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(host.CrawlSideways[0], host.CharacterPrefab, Vector3.left),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(host.CrawlSideways[1], host.CharacterPrefab, Vector3.right),
            HumanoidCrawlStrafeAuthor.MeasureCycleDistance(host.CrawlBackward, host.CharacterPrefab, Vector3.back));
        EditorSceneManager.SaveScene(scene, scenePath);
    }
}
