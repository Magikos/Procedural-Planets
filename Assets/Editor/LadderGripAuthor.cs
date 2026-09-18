using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Bakes an experimental authored rung grip into an editable clip without changing its body motion.</summary>
public static class LadderGripAuthor
{
    public static AnimationClip Create(AnimationClip source, string outputPath, AnimationClip gripSource, AnimationCurve gripWeight,
        AnimationCurve rightGripWeight = null, bool fingersOnly = false)
    {
        ValidateClip(source, nameof(source));
        ValidateClip(gripSource, nameof(gripSource));
        if (gripWeight == null || gripWeight.length == 0) throw new ArgumentException("A grip weight curve is required.", nameof(gripWeight));
        foreach (var weightCurve in new[] { gripWeight, rightGripWeight ?? gripWeight })
        foreach (var key in weightCurve.keys)
            if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || key.value < 0f || key.value > 1f ||
                float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent) ||
                !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight))
                throw new ArgumentException("Grip weights must be finite and between zero and one.", nameof(gripWeight));
        if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/", StringComparison.Ordinal) ||
            !outputPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || outputPath.Contains("..") || outputPath.Contains('\\') ||
            !AssetDatabase.IsValidFolder(Path.GetDirectoryName(outputPath)?.Replace('\\', '/')))
            throw new ArgumentException("Choose an .anim path inside an existing Assets folder.", nameof(outputPath));
        if (File.Exists(outputPath) || AssetDatabase.LoadMainAssetAtPath(outputPath) != null)
            throw new IOException("Animation destination already exists: " + outputPath);

        // Universal ladder Up_Loop phase zero supplies the donor grip. The caller retains its exact provenance.
        // Bake only finger muscles and wrist orientation; never replace arm stretch, body, root, or contact-goal curves.
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        int count = Mathf.CeilToInt(source.length * 60f);
        if (rightGripWeight != null && rightGripWeight.length == 0)
            throw new ArgumentException("A right-hand grip weight curve cannot be empty.", nameof(rightGripWeight));
        foreach (string property in GripProperties(fingersOnly))
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            var donor = AnimationUtility.GetEditorCurve(gripSource, binding);
            if (donor == null) throw new ArgumentException("Grip source lacks required curve: " + property, nameof(gripSource));
            float target = donor.Evaluate(0f);
            if (!float.IsFinite(target)) throw new ArgumentException("Grip source has a non-finite value: " + property, nameof(gripSource));
            var original = AnimationUtility.GetEditorCurve(source, binding);
            var keys = new Keyframe[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float time = Mathf.Min(i / 60f, source.length);
                var weightCurve = property.StartsWith("Right", StringComparison.Ordinal) ? rightGripWeight ?? gripWeight : gripWeight;
                float weight = weightCurve.Evaluate(time / source.length);
                if (!float.IsFinite(weight) || weight < 0f || weight > 1f)
                    throw new ArgumentException("Sampled grip weights must be finite and between zero and one.", nameof(gripWeight));
                float value = original?.Evaluate(time) ?? 0f;
                if (!float.IsFinite(value)) throw new ArgumentException("Source has a non-finite value: " + property, nameof(source));
                keys[i] = new Keyframe(time, Mathf.Lerp(value, target, weight));
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            bindings.Add(binding);
            curves.Add(curve);
        }
        var result = new AnimationClip();
        try
        {
            // CopySerialized retains clip settings, events, object curves, and untouched animation curves.
            EditorUtility.CopySerialized(source, result);
            result.name = Path.GetFileNameWithoutExtension(outputPath);
            AnimationUtility.SetEditorCurves(result, bindings.ToArray(), curves.ToArray());
            AssetDatabase.CreateAsset(result, outputPath);
            AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }
        catch
        {
            if (!EditorUtility.IsPersistent(result)) UnityEngine.Object.DestroyImmediate(result);
            throw;
        }
    }

    static IEnumerable<string> GripProperties(bool fingersOnly)
    {
        foreach (string side in new[] { "Left", "Right" })
        {
            if (!fingersOnly)
            {
                yield return side + " Forearm Twist In-Out";
                yield return side + " Hand Down-Up";
                yield return side + " Hand In-Out";
            }
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                for (int joint = 1; joint <= 3; joint++) yield return side + "Hand." + finger + "." + joint + " Stretched";
                yield return side + "Hand." + finger + ".Spread";
            }
        }
    }

    static void ValidateClip(AnimationClip clip, string parameter)
    {
        if (clip == null || !clip.isHumanMotion || !float.IsFinite(clip.length) || clip.length <= 0f)
            throw new ArgumentException("A non-empty humanoid clip is required.", parameter);
    }
}
