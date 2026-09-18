using UnityEngine;

/// <summary>Presentation-only world-space limb target. Gameplay owns interaction success.</summary>
public readonly struct InteractionPoseTarget
{
    public readonly Vector3 Position;
    public readonly Quaternion? Rotation;
    public readonly float Weight;
    public readonly bool UseContact;
    public readonly bool FollowAuthoredMotion;
    public readonly bool PreserveAuthoredContactJoints;
    public readonly float GripRadius;
    public readonly Vector3? BendDirection;
    public InteractionPoseTarget(Vector3 position, Quaternion? rotation = null, float weight = 1f, bool useContact = false, float gripRadius = 0f,
        Vector3? bendDirection = null, bool followAuthoredMotion = false, bool preserveAuthoredContactJoints = false)
    {
        if (!CharacterMath.IsFinite(position) || !float.IsFinite(weight) || !float.IsFinite(gripRadius) || gripRadius < 0f ||
            (gripRadius > 0f && (!useContact || !rotation.HasValue)))
            throw new System.ArgumentException("Interaction target must be finite.");
        if (rotation.HasValue)
        {
            Quaternion q = rotation.Value;
            float magnitude = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (!float.IsFinite(magnitude) || magnitude < 1e-8f)
                throw new System.ArgumentException("Interaction rotation must be finite and nonzero.");
            rotation = q.normalized;
        }
        Position = position; Rotation = rotation; Weight = Mathf.Clamp01(weight); UseContact = useContact;
        GripRadius = gripRadius; FollowAuthoredMotion = followAuthoredMotion;
        PreserveAuthoredContactJoints = preserveAuthoredContactJoints;
        if (bendDirection.HasValue && (!CharacterMath.IsFinite(bendDirection.Value) || bendDirection.Value.sqrMagnitude < 1e-8f))
            throw new System.ArgumentException("Interaction bend direction must be finite and nonzero.");
        BendDirection = bendDirection?.normalized;
    }
}
