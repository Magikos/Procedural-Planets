using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class HumanoidWalkArmAuthor
{
    const int Samples = 128;

    public static float MeasurePhaseOffset(AnimationClip legs, AnimationClip arms, GameObject humanoidPrefab)
    {
        ValidateClip(legs); ValidateClip(arms);
        if (humanoidPrefab == null) throw new ArgumentNullException(nameof(humanoidPrefab));
        Vector2[] target = SampleFeet(legs, humanoidPrefab), donor = SampleFeet(arms, humanoidPrefab);
        Normalize(target); Normalize(donor);
        float best = float.PositiveInfinity;
        int bestOffset = 0;
        for (int offset = 0; offset < Samples; offset++)
        {
            float cost = 0f;
            for (int i = 0; i < Samples; i++) cost += (target[i] - donor[(i + offset) % Samples]).sqrMagnitude;
            if (cost < best) { best = cost; bestOffset = offset; }
        }
        return (float)bestOffset / Samples;
    }

    public static AnimationClip Create(AnimationClip legs, AnimationClip arms, string destination,
        float sampledPhaseOffset, bool replaceExisting = false, float upperBodySway = 1f)
    {
        ValidateClip(legs); ValidateClip(arms);
        if (!float.IsFinite(sampledPhaseOffset)) throw new ArgumentOutOfRangeException(nameof(sampledPhaseOffset));
        if (!float.IsFinite(upperBodySway) || upperBodySway < 0f || upperBodySway > 1f)
            throw new ArgumentOutOfRangeException(nameof(upperBodySway));
        if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) ||
            !destination.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || destination.Contains("..") || destination.Contains('\\') ||
            !AssetDatabase.IsValidFolder(Path.GetDirectoryName(destination)?.Replace('\\', '/')))
            throw new ArgumentException("Choose an .anim path inside an existing Assets folder.", nameof(destination));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
        if ((File.Exists(destination) || AssetDatabase.LoadMainAssetAtPath(destination) != null) && (existing == null || !replaceExisting))
            throw new IOException("Animation destination already exists: " + destination);
        var clip = UnityEngine.Object.Instantiate(legs);
        clip.name = Path.GetFileNameWithoutExtension(destination);
        try
        {
            var targetSettings = AnimationUtility.GetAnimationClipSettings(legs);
            var donorSettings = AnimationUtility.GetAnimationClipSettings(arms);
            // Measured offsets include import cycle offsets. Curves are authored in the unshifted clip time domain.
            float rawOffset = sampledPhaseOffset + donorSettings.cycleOffset - targetSettings.cycleOffset;
            int copied = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(arms))
            {
                if (binding.type != typeof(Animator) || !IsUpperBody(binding.propertyName)) continue;
                var source = AnimationUtility.GetEditorCurve(arms, binding);
                var keys = new Keyframe[Samples + 1];
                for (int i = 0; i <= Samples; i++)
                {
                    float phase = (float)i / Samples;
                    keys[i] = new Keyframe(phase * legs.length, source.Evaluate(Mathf.Repeat(phase + rawOffset, 1f) * arms.length));
                }
                if (IsSway(binding.propertyName))
                {
                    float mean = 0f;
                    for (int i = 0; i < Samples; i++) mean += keys[i].value / Samples;
                    for (int i = 0; i <= Samples; i++) keys[i].value = mean + (keys[i].value - mean) * upperBodySway;
                }
                var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
                for (int i = 0; i <= Samples; i++)
                {
                    int before = (i + Samples - 1) % Samples, after = (i + 1) % Samples;
                    var key = curve[i];
                    key.inTangent = key.outTangent = (keys[after].value - keys[before].value) / (2f * legs.length / Samples);
                    curve.MoveKey(i, key);
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                copied++;
            }
            if (copied == 0) throw new InvalidOperationException("The donor has no readable humanoid upper-body curves.");
            if (existing == null) { AssetDatabase.CreateAsset(clip, destination); return clip; }
            EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(clip); return existing;
        }
        catch { UnityEngine.Object.DestroyImmediate(clip); throw; }
    }

    static bool IsSway(string property) => property.StartsWith("Spine ", StringComparison.Ordinal) ||
        property.StartsWith("Chest ", StringComparison.Ordinal) || property.StartsWith("UpperChest ", StringComparison.Ordinal) ||
        property.StartsWith("Left Shoulder ", StringComparison.Ordinal) || property.StartsWith("Right Shoulder ", StringComparison.Ordinal);

    static bool IsUpperBody(string property)
    {
        if (property.StartsWith("Spine ", StringComparison.Ordinal) ||
            property.StartsWith("Chest ", StringComparison.Ordinal) ||
            property.StartsWith("UpperChest ", StringComparison.Ordinal)) return true;
        foreach (string side in new[] { "Left", "Right" })
            if (property.StartsWith(side + " Shoulder", StringComparison.Ordinal) ||
                property.StartsWith(side + " Arm", StringComparison.Ordinal) ||
                property.StartsWith(side + " Forearm", StringComparison.Ordinal) ||
                property.StartsWith(side + " Hand", StringComparison.Ordinal) ||
                property.StartsWith(side + "Hand.", StringComparison.Ordinal)) return true;
        return false;
    }

    static Vector2[] SampleFeet(AnimationClip clip, GameObject prefab)
    {
        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        var graph = PlayableGraph.Create("Walk arm phase measurement");
        try
        {
            var animator = actor.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) throw new ArgumentException("A Humanoid prefab is required.");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetSpeed(0d);
            var output = AnimationPlayableOutput.Create(graph, "Phase measurement", animator);
            output.SetSourcePlayable(playable); graph.Play();
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var values = new Vector2[Samples];
            for (int i = 0; i < Samples; i++)
            {
                playable.SetTime((double)i / Samples * clip.length); graph.Evaluate(0f);
                values[i] = new Vector2(actor.transform.InverseTransformPoint(left.position).y, actor.transform.InverseTransformPoint(right.position).y);
            }
            return values;
        }
        finally { graph.Destroy(); UnityEngine.Object.DestroyImmediate(actor); }
    }

    static void Normalize(Vector2[] samples)
    {
        Vector2 min = samples[0], max = samples[0];
        foreach (var value in samples) { min = Vector2.Min(min, value); max = Vector2.Max(max, value); }
        Vector2 range = max - min;
        if (range.x < .005f || range.y < .005f) throw new InvalidOperationException("Foot motion is too small to infer arm phase reliably.");
        for (int i = 0; i < samples.Length; i++) samples[i] = new Vector2((samples[i].x - min.x) / range.x, (samples[i].y - min.y) / range.y);
    }

    static void ValidateClip(AnimationClip clip)
    {
        if (clip == null || !clip.isHumanMotion || clip.length <= 0f) throw new ArgumentException("A positive-duration Humanoid clip is required.");
    }
}

