using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Splits the authored jump into matching actor travel and local pose translation.</summary>
public static class HumanoidLedgeJumpMotionAuthor
{
    public static void Bake(ActorTraversalMotionAsset asset, GameObject prefab)
    {
        if (asset == null || asset.Clip == null || prefab == null) throw new ArgumentException("Ledge jump requires a clip and actor.");
        using var reference = new AuthoredAnimationReference(prefab, asset.Clip, false);
        reference.Root.transform.GetChild(0).localScale = Vector3.one;
        var animator = reference.Animator;
        Vector3 Local(HumanBodyBones bone) => reference.Root.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
        reference.Sample(asset.Clip.length - .00001f);
        Vector3 edge = (Local(HumanBodyBones.LeftHand) + Local(HumanBodyBones.RightHand)) * .5f;
        // Match the existing hanging capsule origin. The pose retains the original body motion.
        Vector3 end = new Vector3(0f, edge.y - 1.55f, edge.z - .33f);
        float finalHip = Local(HumanBodyBones.Hips).y;
        reference.Sample(asset.Clip.length * .3f);
        float launchHip = Local(HumanBodyBones.Hips).y;
        const int intervals = 128;
        var frames = new ActorTraversalMotion.Frame[intervals + 1];
        for (int i = 0; i <= intervals; i++)
        {
            float phase = (float)i / intervals;
            reference.Sample(Mathf.Min(asset.Clip.length - .00001f, phase * asset.Clip.length));
            Vector3 hip = Local(HumanBodyBones.Hips), head = Local(HumanBodyBones.Head);
            float travel = phase <= .3f ? 0f : Mathf.Clamp01((hip.y - launchHip) / (finalHip - launchHip));
            if (phase >= .6f) travel = 1f;
            Vector3 root = end * travel;
            frames[i] = new ActorTraversalMotion.Frame { Root = root, PoseOffset = root,
                CapsuleA = hip - root, CapsuleB = head - root, Radius = .15f,
                PlanarFitWeight = travel, AnchorWeight = 0f };
        }
        asset.ReferenceEdge = edge; asset.ElevatedLanding = true; asset.Frames = frames;
        asset.MaxHeightAdjustment = .2f; asset.MaxPlanarAdjustment = .4f;
        asset.CreateMotion(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
    }
}
