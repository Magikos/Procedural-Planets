using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Bakes the selected basic fence vault in the same retargeted frame used by the review actor.</summary>
public static class HumanoidVaultMotionAuthor
{
    public static void Bake(ActorTraversalMotionAsset asset, GameObject prefab)
    {
        if (asset == null || asset.Clip == null || prefab == null) throw new ArgumentException("Vault authoring requires an asset, clip, and actor.");
        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = actor.GetComponent<Animator>();
            using var graph = new ActorAnimationGraph(animator, "Vault motion authoring", 1);
            var clip = graph.AddBaseClip(0, asset.Clip);
            graph.BaseMixer.SetInputWeight(0, 1f);
            const int intervals = 128;
            var frames = new ActorTraversalMotion.Frame[intervals + 1];
            Vector3 start = Vector3.zero, contact = Vector3.zero;
            int contacts = 0;
            for (int i = 0; i <= intervals; i++)
            {
                float phase = (float)i / intervals;
                clip.SetTime(asset.Clip.length * phase); graph.Evaluate();
                Vector3 hip = Local(HumanBodyBones.Hips), head = Local(HumanBodyBones.Head);
                if (i == 0) start = hip;
                Vector3 root = Vector3.ProjectOnPlane(hip - start, Vector3.up);
                frames[i] = new ActorTraversalMotion.Frame
                {
                    Root = root, CapsuleA = hip - root, CapsuleB = head - root,
                    Radius = .15f,
                    PlanarFitWeight = Mathf.Clamp01(root.z / .55f),
                    AnchorWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.14f, .25f, phase)) *
                        (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.40f, .55f, phase)))
                };
                if (phase >= .25f && phase <= .4f) { contact += Local(HumanBodyBones.LeftHand); contacts++; }
            }
            asset.Frames = frames; asset.ReferenceEdge = contact / contacts;
            asset.SupportContact = "LeftHand";
            asset.CreateMotion();
            EditorUtility.SetDirty(asset);

            Vector3 Local(HumanBodyBones bone) => actor.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); }
    }
}
