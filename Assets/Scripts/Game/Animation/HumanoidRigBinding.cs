using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Maps Unity's validated Humanoid avatar onto the existing shared procedural solvers.</summary>
public static class HumanoidRigBinding
{
    public static void Bind(Animator animator, ProceduralRigDefinition rig)
    {
        if (animator == null) throw new ArgumentNullException(nameof(animator));
        if (rig == null) throw new ArgumentNullException(nameof(rig));
        if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new ArgumentException("Binding requires a valid Unity Humanoid avatar.", nameof(animator));
        float scale = rig.transform.lossyScale.x;
        if (!float.IsFinite(scale) || scale <= 0f)
            throw new ArgumentException("The procedural rig requires a positive finite scale.", nameof(rig));
        Transform Required(HumanBodyBones bone)
        {
            var value = animator.GetBoneTransform(bone);
            if (value == null) throw new ArgumentException("Humanoid avatar is missing required bone: " + bone);
            if (value != rig.transform && !value.IsChildOf(rig.transform))
                throw new ArgumentException("The procedural rig must contain humanoid bone: " + bone);
            return value;
        }
        Transform[] Chain(HumanBodyBones first, HumanBodyBones second, HumanBodyBones third)
        {
            var result = new[] { Required(first), Required(second), Required(third) };
            LimbPoseSolver.ValidateChain(result); return result;
        }
        var body = Required(HumanBodyBones.Hips);
        var spine = new List<Transform> { Required(HumanBodyBones.Spine) };
        foreach (var bone in new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest })
        {
            var value = animator.GetBoneTransform(bone);
            if (value != null) spine.Add(value);
        }
        var head = Required(HumanBodyBones.Head);
        var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        var feet = new FootDefinition[2];
        var hands = new InteractionLimbDefinition[2];
        Vector3 up = animator.transform.up;
        float skinMinimum = SkinMinimum(animator.transform, up);
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            var leg = left ? Chain(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot)
                : Chain(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            float length = (Vector3.Distance(leg[0].position, leg[1].position) + Vector3.Distance(leg[1].position, leg[2].position)) / scale;
            float sole = float.IsFinite(skinMinimum)
                ? Mathf.Clamp((Vector3.Dot(leg[2].position, up) - skinMinimum) / scale, 0f, length * .3f)
                : length * .025f;
            feet[side] = new FootDefinition { Bones = leg, Contact = leg[2],
                BendDirection = rig.transform.InverseTransformDirection(animator.transform.forward), SoleOffset = sole,
                MaxCorrection = Mathf.Max(.01f, length * .35f), JointLimit = 65f,
                WalkContact = ContactCurve(left, false), RunContact = ContactCurve(left, true) };
            hands[side] = new InteractionLimbDefinition { Id = left ? "LeftHand" : "RightHand", JointLimit = 150f,
                Bones = left ? Chain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand)
                    : Chain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand) };
            BindHandContact(animator, rig.transform, hands[side], left);
        }
        // Assign only after every required chain validates. Existing secondary chains remain intact.
        rig.Body = body; rig.Spine = spine.ToArray();
        rig.Look = neck != null ? new[] { neck, head } : new[] { head };
        rig.LookWeights = neck != null ? new[] { 1f, 2f } : new[] { 1f };
        rig.Feet = feet; rig.Interactions = hands;
    }

    static void BindHandContact(Animator animator, Transform frame, InteractionLimbDefinition hand, bool left)
    {
        Transform wrist = hand.Bones[^1];
        Transform index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        Transform outer = null;
        if (index != null && index.IsChildOf(wrist))
        {
            float separation = 0f;
            foreach (var finger in left
                ? new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal }
                : new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal })
            {
                Transform candidate = animator.GetBoneTransform(finger);
                if (candidate == null || candidate == index || !candidate.IsChildOf(wrist)) continue;
                float distance = (candidate.position - index.position).sqrMagnitude;
                if (distance <= separation) continue;
                separation = distance;
                outer = candidate;
            }
        }
        Vector3 fingers = wrist.position - hand.Bones[^2].position;
        Vector3 palm = wrist.position;
        Vector3 normal = Vector3.ProjectOnPlane(animator.transform.forward, fingers);
        if (index != null && outer != null)
        {
            Vector3 knuckles = (index.position + outer.position) * .5f;
            Vector3 fingerDirection = knuckles - wrist.position;
            Vector3 palmNormal = Vector3.Cross(fingerDirection, index.position - outer.position) * (left ? -1f : 1f);
            if (fingerDirection.sqrMagnitude > 1e-10f && palmNormal.sqrMagnitude > 1e-10f)
            {
                fingers = fingerDirection;
                normal = palmNormal;
                palm = Vector3.Lerp(wrist.position, knuckles, .5f);
                hand.ContactThickness = .35f * Vector3.Distance(index.position, outer.position) / frame.lossyScale.x;
            }
        }
        if (fingers.sqrMagnitude < 1e-10f) fingers = animator.transform.up;
        if (normal.sqrMagnitude < 1e-10f) normal = CharacterMath.ArbitraryTangent(fingers.normalized);
        hand.ContactPosition = wrist.InverseTransformPoint(palm);
        hand.ContactRotation = Quaternion.Inverse(wrist.rotation) * Quaternion.LookRotation(normal.normalized, fingers.normalized);
        hand.ReachConeDegrees = 85f;
        hand.MaximumExtension = .97f;
        hand.WristLimitDegrees = 60f;
        hand.ForearmTwistLimitDegrees = 90f;
        Vector3 elbow = animator.transform.right * (left ? -.25f : .25f) - animator.transform.up - animator.transform.forward * .1f;
        hand.BendDirection = frame.InverseTransformDirection(elbow.normalized);

        var joints = new List<InteractionContactJoint>();
        // Unity groups each hand's fifteen finger joints contiguously. Their local pose defines the contact shape.
        int first = (int)(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        int last = (int)(left ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal);
        for (int i = first; i <= last; i++)
        {
            Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (bone == null || !bone.IsChildOf(wrist)) continue;
            joints.Add(new InteractionContactJoint { Bone = bone, LocalRotation = bone.localRotation, MaxCorrectionDegrees = 60f });
        }
        hand.ContactJoints = joints.ToArray();
    }

    static float SkinMinimum(Transform root, Vector3 up)
    {
        float minimum = float.PositiveInfinity;
        var mesh = new Mesh();
        var vertices = new List<Vector3>();
        try
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.sharedMesh == null) continue;
                renderer.BakeMesh(mesh, true);
                mesh.GetVertices(vertices);
                foreach (var vertex in vertices)
                    minimum = Mathf.Min(minimum, Vector3.Dot(renderer.transform.TransformPoint(vertex), up));
            }
            return minimum;
        }
        finally
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    static AnimationCurve ContactCurve(bool left, bool running)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i <= 16; i++)
        {
            float phase = Mathf.Repeat(i / 16f + (left ? 0f : .5f), 1f);
            float stance = running ? .32f : .55f;
            float contact = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stance - .1f, stance, phase));
            contact *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .08f, phase));
            curve.AddKey(new Keyframe(i / 16f, contact, 0f, 0f));
        }
        curve.preWrapMode = curve.postWrapMode = WrapMode.Loop;
        return curve;
    }
}

