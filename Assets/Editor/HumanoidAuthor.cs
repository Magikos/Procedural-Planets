using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Copies selected owned art only, then authors one controller-free Humanoid prefab.</summary>
public static class HumanoidAuthor
{
    const string Source = "D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyHeroCharacters/";
    public const string Folder = "Assets/Art/Characters/Baseline";
    public const string PrefabPath = Folder + "/Baseline.prefab";
    static readonly string[] Parts =
    {
        "Chr_Head_Male_01", "Chr_Eyebrow_Male_01", "Chr_Hair_01",
        "Chr_Torso_Male_01", "Chr_Hips_Male_01",
        "Chr_ArmUpperLeft_Male_01", "Chr_ArmUpperRight_Male_01",
        "Chr_ArmLowerLeft_Male_01", "Chr_ArmLowerRight_Male_01",
        "Chr_HandLeft_Male_01", "Chr_HandRight_Male_01",
        "Chr_LegLeft_Male_01", "Chr_LegRight_Male_01",
    };

    [MenuItem("Tools/Actors/Author Humanoid")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        if (File.Exists(PrefabPath)) throw new IOException("The destination exists but is not an imported prefab: " + PrefabPath);
        var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Creatures/Wolf/Wolf.mat");
        if (reference == null) throw new InvalidOperationException("The project animal material is required for the hero art.");
        Directory.CreateDirectory(Folder);
        string modelPath = CopyArt("Models/FixedScale/ModularCharacters.fbx", "ModularCharacters.fbx");
        string texturePath = CopyArt("Textures/Textures/PolygonFantasyHero_Texture_01_A.png", "HeroAlbedo.png");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("The source model did not import.");
        importer.animationType = ModelImporterAnimationType.Human;
        importer.optimizeGameObjects = false;
        importer.isReadable = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Hero.mat");
        if (material == null)
        {
            material = new Material(reference) { name = "Source Hero" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            AssetDatabase.CreateAsset(material, Folder + "/Hero.mat");
        }
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
        string sourcePrefabPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/SourceModularCharacter.prefab");
        bool createdSourcePrefab = !File.Exists(sourcePrefabPath);
        if (createdSourcePrefab)
        {
            string text = File.ReadAllText(Source + "Prefabs/FixedScale/ModularCharacter_01.prefab");
            text = text.Replace("014fbaa82701c3f4bbd9c27c7a0650e5", AssetDatabase.AssetPathToGUID(Folder + "/Hero.mat"));
            File.WriteAllText(sourcePrefabPath, text);
            AssetDatabase.ImportAsset(sourcePrefabPath, ImportAssetOptions.ForceSynchronousImport);
        }
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
        if (sourcePrefab == null) throw new InvalidOperationException("The source prefab did not import.");
        var instance = UnityEngine.Object.Instantiate(sourcePrefab);
        try
        {
            instance.name = "Source Hero";
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var selected = new HashSet<string>(Parts);
            foreach (string part in Parts)
                if (renderers.Count(r => r.name == part) != 1) throw new InvalidOperationException("Expected one source mesh part: " + part);
            var retained = new HashSet<Transform> { instance.transform };
            void Keep(Transform bone)
            {
                while (bone != null && bone != instance.transform) { retained.Add(bone); bone = bone.parent; }
            }
            foreach (var skin in renderers)
            {
                Keep(skin.rootBone);
                foreach (var bone in skin.bones) Keep(bone);
            }
            foreach (var skin in renderers.Where(r => selected.Contains(r.name)))
            {
                Keep(skin.transform); Keep(skin.rootBone);
                foreach (var bone in skin.bones) Keep(bone);
                skin.sharedMaterials = Enumerable.Repeat(material, skin.sharedMaterials.Length).ToArray();
                skin.enabled = true;
            }
            foreach (var skin in renderers.Where(r => !selected.Contains(r.name))) UnityEngine.Object.DestroyImmediate(skin);
            foreach (var bone in instance.GetComponentsInChildren<Transform>(true).Reverse())
                if (!retained.Contains(bone)) UnityEngine.Object.DestroyImmediate(bone.gameObject);
            foreach (var bone in retained) bone.gameObject.SetActive(true);
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (component != null && component is not Transform && component is not Animator && component is not SkinnedMeshRenderer)
                    UnityEngine.Object.DestroyImmediate(component);
            var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault(a => a.isHuman && a.isValid);
            if (animator.avatar == null) throw new InvalidOperationException("The source model has no valid Humanoid Avatar.");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            if (saved == null) throw new InvalidOperationException("The trimmed prefab could not be saved.");
            if (createdSourcePrefab) AssetDatabase.DeleteAsset(sourcePrefabPath);
            File.WriteAllText(Folder + "/SOURCE.md", "Owned Synty Polygon Fantasy Hero Characters art.\nSource: " + Source +
                "\nFixedScale ModularCharacter_01.prefab and ModularCharacters.fbx; one Male_01 outfit, face, eyebrows and hair.\n" +
                "Texture: PolygonFantasyHero_Texture_01_A.png. Material uses the existing project animal shader.\n" +
                "No vendor scripts, shaders, controllers, or other presets imported. Full source FBX retains the shared modular meshes and Humanoid Avatar.\n");
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    static string CopyArt(string relativeSource, string fileName)
    {
        string source = Source + relativeSource, target = Folder + "/" + fileName;
        string meta = File.ReadAllText(source + ".meta");
        string guid = System.Text.RegularExpressions.Regex.Match(meta, @"(?m)^guid: (\w+)").Groups[1].Value;
        string existing = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(existing) && existing != target)
            return existing;
        if (!File.Exists(target))
        {
            File.Copy(source, target);
            File.Copy(source + ".meta", target + ".meta");
        }
        else if (!File.Exists(target + ".meta") ||
            System.Text.RegularExpressions.Regex.Match(File.ReadAllText(target + ".meta"), @"(?m)^guid: (\w+)").Groups[1].Value != guid)
            throw new IOException("The destination contains different art: " + target);
        return target;
    }
}
