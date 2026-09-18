using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Editable standing jump variant; the motor supplies the airborne root arc.</summary>
public static class HumanoidStandingHopAuthor
{
    public const string SourcePath = "Assets/Art/Characters/Animations/ReviewCandidates/Traversal_Movement_Jump_InPlace_WholeSequence.fbx";
    const string VariantPath = "Assets/Art/Characters/Animations/Standing Hop.anim";

    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring the standing hop.");
        var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
            .First(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(VariantPath);
        if (clip == null)
        {
            clip = UnityEngine.Object.Instantiate(source);
            clip.name = "Standing Hop";
            var binding = AnimationUtility.GetCurveBindings(clip).Single(b => b.propertyName == "RootT.y");
            var height = AnimationUtility.GetEditorCurve(clip, binding);
            const float takeoff = 14f / 30f, landing = 27f / 30f, apex = 20f / 30f;
            float arch = height.Evaluate(apex) - Mathf.Lerp(height.Evaluate(takeoff), height.Evaluate(landing),
                (apex - takeoff) / (landing - takeoff));
            var adjusted = new AnimationCurve();
            for (int frame = 0; frame <= 100; frame++)
            {
                float time = frame / 60f;
                float phase = Mathf.Clamp01((time - takeoff) / (landing - takeoff));
                adjusted.AddKey(time, height.Evaluate(time) - arch * 4f * phase * (1f - phase));
            }
            AnimationUtility.SetEditorCurve(clip, binding, adjusted);
            AssetDatabase.CreateAsset(clip, VariantPath);
        }
        var library = AssetDatabase.LoadAssetAtPath<ActorAnimationPerformanceLibrary>(HumanoidRunningJumpAuthor.LibraryPath);
        var entry = new ActorAnimationPerformanceLibrary.Entry {
            Id = "humanoid.jump.stationary", Action = "Jump", RigFamily = "Humanoid", Condition = "Stationary",
            Phases = new[] { Phase("Preparation", 0f, 14f), Phase("Ascent", 14f, 19f), Phase("Apex", 19f, 21f),
                Phase("Descent", 21f, 27f), Phase("Landing", 27f, 50f) } };
        library.Entries = library.Entries.Where(item => item.Id != entry.Id).Append(entry).ToArray();
        library.Snapshot();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);

        ActorAnimationPerformanceLibrary.Phase Phase(string name, float first, float last) => new() {
            Name = name, Clip = clip, StartNormalized = first / 50f, EndNormalized = last / 50f, BlendSeconds = .1f };
    }
}
