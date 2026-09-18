using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Samples step-up body travel and its torso envelope without changing the editable clip.</summary>
public static class HumanoidStepUpMotionAuthor
{
    public static void Bake(ActorTraversalMotionAsset asset, GameObject prefab)
    {
        if (asset == null || asset.Clip == null || prefab == null) throw new ArgumentException("Step-up authoring requires an asset, clip, and actor.");
        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = actor.GetComponent<Animator>();
            using var graph = new ActorAnimationGraph(animator, "Step-up motion authoring", 1);
            var clip = graph.AddBaseClip(0, asset.Clip);
            clip.SetApplyFootIK(false); clip.SetApplyPlayableIK(false);
            graph.BaseMixer.SetInputWeight(0, 1f);
            // This importer extracts root translation. Evaluate real elapsed time with root
            // motion enabled on this disposable preview to retain the artist trajectory.
            animator.applyRootMotion = true;
            clip.SetTime(0d);
            graph.Evaluate();
            Vector3 startPosition = actor.transform.position;
            Quaternion startRotation = actor.transform.rotation;
            const int intervals = 128;
            var frames = new ActorTraversalMotion.Frame[intervals + 1];
            var feet = new Vector3[intervals + 1, 2];
            Vector3 startHip = Vector3.zero;
            float startFloor = 0f;
            for (int i = 0; i <= intervals; i++)
            {
                float phase = (float)i / intervals;
                if (i > 0) clip.GetGraph().Evaluate((asset.Clip.length - .00001f) / intervals);
                Vector3 hip = Local(HumanBodyBones.Hips), head = Local(HumanBodyBones.Head);
                float floor = Mathf.Min(Local(HumanBodyBones.LeftFoot).y, Local(HumanBodyBones.RightFoot).y);
                if (i == 0) { startHip = hip; startFloor = floor; }
                Vector3 poseOffset = Vector3.ProjectOnPlane(hip - startHip, Vector3.up) + Vector3.up * (floor - startFloor);
                Vector3 extracted = Quaternion.Inverse(startRotation) * (actor.transform.position - startPosition);
                Vector3 root = extracted + poseOffset;
                feet[i, 0] = extracted + Local(HumanBodyBones.LeftFoot);
                feet[i, 1] = extracted + Local(HumanBodyBones.RightFoot);
                frames[i] = new ActorTraversalMotion.Frame { Root = root, PoseOffset = poseOffset,
                    CapsuleA = hip - poseOffset, CapsuleB = head - poseOffset, Radius = .15f,
                    PlanarFitWeight = Mathf.SmoothStep(0f, 1f, phase), AnchorWeight = 0f };
            }
            // The selected one-metre step clip ends on the platform. Reject an in-place import
            // instead of inventing a replacement trajectory when its travel is unavailable.
            var last = frames[intervals].Root;
            if (last.y < .2f || last.z < .2f)
                throw new InvalidOperationException($"Step-up source lacks usable baked travel: {last}.");
            // Find the first sustained elevated foot plant in the sampled source, rather
            // than deriving an obstacle edge from the final pelvis location.
            int plant = -1, plantedFoot = -1;
            float platform = Mathf.Min(feet[intervals, 0].y, feet[intervals, 1].y);
            const int window = 6;
            for (int i = intervals / 6; i <= intervals - window && plant < 0; i++)
                for (int foot = 0; foot < 2; foot++)
                {
                    bool stable = Mathf.Abs(feet[i, foot].y - platform) < .04f;
                    for (int j = 1; j <= window && stable; j++)
                        stable &= Vector3.Distance(feet[i + j, foot], feet[i, foot]) < .025f;
                    if (stable) { plant = i; plantedFoot = foot; break; }
                }
            if (plant < 0) throw new InvalidOperationException("Step-up source has no sustained elevated foot plant.");
            for (int i = 0; i <= intervals; i++)
                frames[i].PlanarFitWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)i / plant));
            asset.ElevatedLanding = true; asset.Frames = frames;
            asset.ReferenceEdge = new Vector3(0f, last.y, feet[plant, plantedFoot].z);
            Debug.Log($"Step-up source plant: phase={(float)plant / intervals:F3}, foot={plantedFoot}, position={feet[plant, plantedFoot]}.");
            asset.MaxHeightAdjustment = .3f; asset.MaxPlanarAdjustment = .7f;
            asset.CreateMotion(); EditorUtility.SetDirty(asset);
            Vector3 Local(HumanBodyBones bone) => actor.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); }
    }
}
