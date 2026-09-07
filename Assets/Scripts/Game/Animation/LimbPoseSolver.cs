using System;
using UnityEngine;

/// <summary>Stable two-bone solve for limbs, with bounded CCD for longer chains. Never changes bone lengths.</summary>
public sealed class LimbPoseSolver
{
    readonly Transform[] _bones;
    readonly Quaternion[] _pose;
    public Transform Tip => _bones[^1];
    public Transform Base => _bones[0];

    public LimbPoseSolver(Transform[] bones)
    {
        ValidateChain(bones);
        _bones = (Transform[])bones.Clone();
        _pose = new Quaternion[bones.Length];
    }

    public static void ValidateChain(Transform[] bones)
    {
        if (bones == null || bones.Length < 2) throw new ArgumentException("A bone chain needs at least two transforms.");
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) throw new ArgumentException("Bone chain contains a missing transform.");
            if (i > 0 && (bones[i] == bones[i - 1] || !bones[i].IsChildOf(bones[i - 1])))
                throw new ArgumentException("Bone chain must follow the hierarchy from base to tip.");
        }
    }

    public float Solve(Vector3 target, float weight, float jointLimit, Quaternion? tipRotation = null,
        Vector3? bendDirection = null)
    {
        if (!CharacterMath.IsFinite(target)) throw new ArgumentException("Limb target must be finite.");
        weight = Mathf.Clamp01(weight);
        if (weight <= 0f) return Vector3.Distance(Tip.position, target);
        target = Vector3.Lerp(Tip.position, target, weight);
        for (int i = 0; i < _bones.Length; i++) _pose[i] = _bones[i].localRotation;
        Quaternion originalTip = Tip.rotation;
        if (_bones.Length == 3) SolveTwoBone(target, jointLimit, bendDirection);
        else for (int iteration = 0; iteration < 24; iteration++)
        {
            if ((Tip.position - target).sqrMagnitude < 0.000001f) break;
            for (int i = _bones.Length - 2; i >= 0; i--)
            {
                Transform bone = _bones[i];
                Vector3 from = Tip.position - bone.position;
                Vector3 to = target - bone.position;
                if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) continue;
                bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
                bone.localRotation = Quaternion.RotateTowards(_pose[i], bone.localRotation, Mathf.Max(0f, jointLimit));
            }
        }
        Tip.rotation = tipRotation.HasValue
            ? Quaternion.Slerp(originalTip, tipRotation.Value, weight) : originalTip;
        return Vector3.Distance(Tip.position, target);
    }

    void SolveTwoBone(Vector3 target, float jointLimit, Vector3? bendDirection)
    {
        Transform upper = _bones[0], joint = _bones[1];
        Vector3 start = upper.position;
        float a = Vector3.Distance(start, joint.position);
        float b = Vector3.Distance(joint.position, Tip.position);
        Vector3 toTarget = target - start;
        if (a < 0.0001f || b < 0.0001f || toTarget.sqrMagnitude < 1e-10f) return;
        Vector3 direction = toTarget.normalized;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.00001f, a + b - 0.00001f);
        Vector3 pole = Vector3.ProjectOnPlane(bendDirection ?? (joint.position - start), direction);
        if (pole.sqrMagnitude < 1e-10f) pole = Vector3.ProjectOnPlane(upper.forward, direction);
        if (pole.sqrMagnitude < 1e-10f) pole = Vector3.ProjectOnPlane(upper.right, direction);
        float along = (a * a - b * b + distance * distance) / (2f * distance);
        float across = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 bend = start + direction * along + pole.normalized * across;
        upper.rotation = Quaternion.FromToRotation(joint.position - start, bend - start) * upper.rotation;
        upper.localRotation = Quaternion.RotateTowards(_pose[0], upper.localRotation, Mathf.Max(0f, jointLimit));
        joint.rotation = Quaternion.FromToRotation(Tip.position - joint.position,
            start + direction * distance - joint.position) * joint.rotation;
        joint.localRotation = Quaternion.RotateTowards(_pose[1], joint.localRotation, Mathf.Max(0f, jointLimit));
    }
}
