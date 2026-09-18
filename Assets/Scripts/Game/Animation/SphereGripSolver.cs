using System;
using UnityEngine;

/// <summary>Fits articulated contact chains around a sphere without crossing its surface.</summary>
public sealed class SphereGripSolver
{
    readonly Transform _wrist;
    readonly Transform[] _bones;
    readonly int[] _children;
    readonly float[] _limits;
    readonly Vector3[] _tipOffsets;
    readonly Quaternion _contactRotation;
    readonly float _thickness;
    public float MaxPenetration { get; private set; }

    public SphereGripSolver(Transform wrist, InteractionContactJoint[] joints, Quaternion contactRotation, float thickness = .005f)
    {
        if (wrist == null || joints == null) throw new ArgumentNullException(nameof(joints));
        _wrist = wrist;
        if (!float.IsFinite(thickness) || thickness < 0f) throw new ArgumentOutOfRangeException(nameof(thickness));
        _thickness = thickness;
        float magnitude = contactRotation.x * contactRotation.x + contactRotation.y * contactRotation.y +
            contactRotation.z * contactRotation.z + contactRotation.w * contactRotation.w;
        if (!float.IsFinite(magnitude) || magnitude < 1e-8f) throw new ArgumentException("Grip contact rotation must be finite and nonzero.");
        _contactRotation = contactRotation.normalized;
        _bones = new Transform[joints.Length]; _children = new int[joints.Length];
        _limits = new float[joints.Length];
        _tipOffsets = new Vector3[joints.Length];
        for (int i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            if (joint == null || joint.Bone == null || joint.Bone == wrist || !joint.Bone.IsChildOf(wrist) ||
                !float.IsFinite(joint.MaxCorrectionDegrees) || joint.MaxCorrectionDegrees < 0f)
                throw new ArgumentException("Grip joints must be valid wrist descendants with finite limits.");
            _bones[i] = joint.Bone; _limits[i] = Mathf.Min(90f, joint.MaxCorrectionDegrees); _children[i] = -1;
            for (int j = 0; j < i; j++) if (_bones[j] == joint.Bone) throw new ArgumentException("Grip joints must be unique.");
        }
        for (int i = 0; i < _bones.Length; i++)
        {
            for (int j = 0; j < _bones.Length; j++)
                if (_bones[j].parent == _bones[i]) { _children[i] = j; break; }
            if (_children[i] < 0)
                _tipOffsets[i] = _bones[i].InverseTransformVector((_bones[i].position - _bones[i].parent.position) * .65f);
        }
    }

    public void Solve(Vector3 center, float radius, float weight)
    {
        if (!CharacterMath.IsFinite(center) || !float.IsFinite(radius) || radius <= 0f || !float.IsFinite(weight))
            throw new ArgumentException("Sphere grip requires a finite center, positive radius, and finite weight.");
        weight = Mathf.Clamp01(weight);
        float clearance = radius + _thickness * Mathf.Abs(_wrist.lossyScale.x);
        Vector3 normal = _wrist.rotation * _contactRotation * Vector3.forward;
        for (int i = 0; i < _bones.Length && weight > 0f; i++)
        {
            Transform bone = _bones[i];
            Vector3 direction = End(i) - bone.position;
            Vector3 axis = Vector3.Cross(direction, normal);
            if (axis.sqrMagnitude < 1e-10f) continue;
            axis = bone.parent.InverseTransformDirection(axis.normalized);
            Quaternion original = bone.localRotation, best = original;
            float bestScore = Score(center, clearance, out float baselinePenetration);
            if (baselinePenetration > .0001f) bestScore = float.PositiveInfinity;
            int steps = Mathf.CeilToInt(_limits[i] / 5f);
            for (int step = 1; step <= steps; step++)
            {
                float angle = Mathf.Min(step * 5f, _limits[i]) * weight;
                bone.localRotation = Quaternion.AngleAxis(angle, axis) * original;
                float score = Score(center, clearance, out float penetration);
                if (penetration <= .0001f && score < bestScore) { bestScore = score; best = bone.localRotation; }
            }
            bone.localRotation = best;
        }
        Score(center, clearance, out float finalPenetration);
        MaxPenetration = finalPenetration;
    }

    Vector3 End(int index) => _children[index] >= 0 ? _bones[_children[index]].position : _bones[index].TransformPoint(_tipOffsets[index]);

    float Score(Vector3 center, float radius, out float penetration)
    {
        float score = 0f; penetration = 0f;
        for (int i = 0; i < _bones.Length; i++)
        {
            Vector3 start = _bones[i].position, end = End(i), segment = end - start;
            float t = segment.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / segment.sqrMagnitude) : 0f;
            penetration = Mathf.Max(penetration, radius - Vector3.Distance(center, start + segment * t));
            float gap = Vector3.Distance(center, end) - radius;
            score += gap * gap;
        }
        return score;
    }
}
