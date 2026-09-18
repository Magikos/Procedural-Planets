using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Editable source reversals and native root trajectories for the matched ladder family.</summary>
public static class LadderGaitAuthor
{
    public const string SourcePath = "Assets/Art/Characters/Animations/Ladder/GaitCandidates/CCP Character-ladder.fbx";
    public static AnimationClip Source(string name) => AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().First(c => c.name == name);

    public static AnimationClip Mirror(AnimationClip source, string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        EditorUtility.CopySerialized(source, clip);
        clip.name = source.name + " Mirrored"; clip.hideFlags = HideFlags.None;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.mirror = !settings.mirror;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    public static AnimationClip Reverse(AnimationClip source, string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        EditorUtility.CopySerialized(source, clip); clip.name = source.name + " Reversed"; clip.hideFlags = HideFlags.None;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            var keys = curve.keys.Reverse().Select(key => new Keyframe(source.length - key.time, key.value,
                -key.outTangent, -key.inTangent, key.outWeight, key.inWeight) { weightedMode = (WeightedMode)(((int)key.weightedMode & 1) << 1 | ((int)key.weightedMode & 2) >> 1) }).ToArray();
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        }
        var events = AnimationUtility.GetAnimationEvents(source);
        foreach (var entry in events) entry.time = source.length - entry.time;
        AnimationUtility.SetAnimationEvents(clip, events.OrderBy(entry => entry.time).ToArray());
        EditorUtility.SetDirty(clip); return clip;
    }

    public static ActorTraversalMotionAsset Bake(AnimationClip clip, GameObject prefab, string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<ActorTraversalMotionAsset>(); AssetDatabase.CreateAsset(asset, path); }
        var model = UnityEngine.Object.Instantiate(prefab); model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = model.GetComponentInChildren<Animator>();
            foreach (var behaviour in model.GetComponentsInChildren<Behaviour>()) behaviour.enabled = behaviour == animator;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            animator.runtimeAnimatorController = null;
            using var graph = new ActorAnimationGraph(animator, "Bake native ladder motion", 1);
            graph.AddBaseClip(0, clip); graph.BaseMixer.SetInputWeight(0, 1f); animator.applyRootMotion = true;
            var native = graph.BaseMixer.GetGraph(); native.Evaluate(0f);
            Vector3 start = animator.transform.position;
            int count = Mathf.CeilToInt(clip.length * 120f);
            var frames = new ActorTraversalMotion.Frame[count + 1];
            for (int i = 0; i <= count; i++)
            {
                if (i > 0) native.Evaluate(clip.length / count);
                Vector3 Bone(HumanBodyBones bone) => animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                frames[i] = new ActorTraversalMotion.Frame { Root = animator.transform.position - start,
                    CapsuleA = Bone(HumanBodyBones.Hips), CapsuleB = Bone(HumanBodyBones.Head), Radius = .14f,
                    RootYawDegrees = Mathf.DeltaAngle(0f, animator.transform.eulerAngles.y), PlanarFitWeight = i / (float)count };
            }
            asset.Clip = clip; asset.Frames = frames; asset.ElevatedLanding = true;
            asset.MaxHeightAdjustment = .15f; asset.MaxPlanarAdjustment = .15f;
            asset.CreateMotion(); EditorUtility.SetDirty(asset); return asset;
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }
}
