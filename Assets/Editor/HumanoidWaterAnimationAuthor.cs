using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Authors a separate first-pass wading motion from an owned humanoid walk.</summary>
public static class HumanoidWaterAnimationAuthor
{
    public const string WadePath = "Assets/Art/Characters/Animations/Basic Wade.anim";

    public static AnimationClip CreateWade(AnimationClip walk, bool replaceExisting = false)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(WadePath);
        if (existing != null && !replaceExisting) return existing;
        if (existing == null && (File.Exists(WadePath) || AssetDatabase.LoadMainAssetAtPath(WadePath) != null))
            throw new IOException("Wading animation destination already exists: " + WadePath);
        if (walk == null) throw new ArgumentNullException(nameof(walk));
        if (!walk.isHumanMotion || walk.length <= 0f)
            throw new ArgumentException("Wading authoring requires a nonempty humanoid walk clip.", nameof(walk));
        if (!AssetDatabase.IsValidFolder("Assets/Art/Characters/Animations"))
            throw new DirectoryNotFoundException("The humanoid animation folder is missing.");

        var clip = UnityEngine.Object.Instantiate(walk);
        clip.name = "Basic Wade";
        try
        {
            // Keep key times and root travel intact. The motor supplies water resistance.
            Adjust(clip, "Left Arm Down-Up", .6f, .22f);
            Adjust(clip, "Right Arm Down-Up", .6f, .22f);
            Adjust(clip, "Left Forearm Stretch", .8f, -.12f);
            Adjust(clip, "Right Forearm Stretch", .8f, -.12f);
            Adjust(clip, "Left Upper Leg Front-Back", 1.08f, .02f);
            Adjust(clip, "Right Upper Leg Front-Back", 1.08f, .02f);
            Adjust(clip, "Left Lower Leg Stretch", .95f, -.02f);
            Adjust(clip, "Right Lower Leg Stretch", .95f, -.02f);
            Adjust(clip, "Spine Left-Right", 1.25f, 0f);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else AssetDatabase.CreateAsset(clip, WadePath);
            AssetDatabase.SaveAssets();
            return clip;
        }
        catch
        {
            if (!AssetDatabase.Contains(clip)) UnityEngine.Object.DestroyImmediate(clip);
            throw;
        }
    }

    static void Adjust(AnimationClip clip, string muscle, float scale, float offset)
    {
        var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), muscle);
        AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
        if (curve == null || curve.length == 0)
            throw new ArgumentException("Walk clip lacks required muscle curve: " + muscle);
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].value = keys[i].value * scale + offset;
            keys[i].inTangent *= scale;
            keys[i].outTangent *= scale;
        }
        curve.keys = keys;
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }
}
