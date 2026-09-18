using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ActorPerformanceReviewAuthor
{
    public const string LibraryPath = "Assets/Art/Characters/Motion/Basic Performances.asset";
    const string FullJumpPath = "Assets/Art/Characters/Animations/Jump Full.fbx";

    [MenuItem("Tools/Actors/Configure Review Performances")]
    public static void ConfigureScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before authoring performances.");
        var host = UnityEngine.Object.FindAnyObjectByType<HumanoidAnimationPrototype>();
        if (host == null) throw new InvalidOperationException("Open HumanoidAnimationReview before configuring performances.");
        Undo.RecordObject(host, "Assign actor performances");
        host.Performances = CreateLibrary();
        EditorUtility.SetDirty(host);
        EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    public static ActorAnimationPerformanceLibrary CreateLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(LibraryPath);
        if (library != null) return library;
        if (AssetImporter.GetAtPath(FullJumpPath) == null &&
            !AssetDatabase.CopyAsset("Assets/Art/Characters/Animations/Jump.fbx", FullJumpPath))
            throw new InvalidOperationException("The owned jump source could not be copied.");
        var importer = (ModelImporter)AssetImporter.GetAtPath(FullJumpPath);
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = "Basic Jump Full";
            clip.events = Array.Empty<AnimationEvent>();
            clip.loopTime = false;
            clip.keepOriginalPositionY = false;
            clip.heightFromFeet = true;
            clip.lockRootHeightY = false;
            clip.lockRootPositionXZ = false;
        }
        importer.clipAnimations = clips;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var motion = AssetDatabase.LoadAllAssetsAtPath(FullJumpPath).OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview__"));
        // Source frames 20..74 include preparation. Flight starts after the motor has committed takeoff.
        ActorAnimationPerformanceLibrary.Phase Phase(string name, float first, float last) => new()
        {
            Name = name, Clip = motion, StartNormalized = (first - 20f) / 54f,
            EndNormalized = (last - 20f) / 54f, BlendSeconds = .12f
        };
        library = ScriptableObject.CreateInstance<ActorAnimationPerformanceLibrary>();
        library.Entries = new[]
        {
            new ActorAnimationPerformanceLibrary.Entry
            {
                Id = "humanoid.jump.basic", Action = "Jump", RigFamily = "Humanoid",
                Phases = new[] { Phase("Ascent", 32f, 43f), Phase("Apex", 43f, 45f),
                    Phase("Descent", 45f, 56f), Phase("Landing", 56f, 74f) }
            }
        };
        library.Snapshot();
        AssetDatabase.CreateAsset(library, LibraryPath);
        AssetDatabase.SaveAssets();
        return library;
    }
}
