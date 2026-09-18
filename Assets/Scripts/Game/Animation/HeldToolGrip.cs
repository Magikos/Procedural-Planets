using UnityEngine;

/// <summary>Prop-local palm anchors. The authored primary hand owns the tool pose.</summary>
public sealed class HeldToolGrip : MonoBehaviour
{
    public bool LeftHandPrimary;
    public Transform Primary;
    public Transform Secondary;
    public Transform SecondarySlideEnd;
    public Transform WorkingPoint;
    [Min(.001f)] public float MaximumSupportCorrection = .12f;
    public string PrimaryId => LeftHandPrimary ? "LeftHand" : "RightHand";
    public string SecondaryId => LeftHandPrimary ? "RightHand" : "LeftHand";

    public bool TryFit(Vector3 palm, Quaternion rotation, out Pose pose)
    {
        pose = default;
        if (Primary == null || !Primary.IsChildOf(transform) || !CharacterMath.IsFinite(palm)) return false;
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (!float.IsFinite(magnitude) || magnitude < .000001f) return false;
        rotation = rotation.normalized;
        Quaternion localRotation = Quaternion.Inverse(transform.rotation) * Primary.rotation;
        Quaternion fitted = rotation * Quaternion.Inverse(localRotation);
        Vector3 offset = Quaternion.Inverse(transform.rotation) * (Primary.position - transform.position);
        pose = new Pose(palm - fitted * offset, fitted);
        return true;
    }

    public Vector3 FittedPoint(Transform anchor, Pose pose) => pose.position + pose.rotation *
        (Quaternion.Inverse(transform.rotation) * (anchor.position - transform.position));

    public Vector3 SupportPoint(Vector3 authoredPalm, Pose pose)
    {
        Vector3 start = FittedPoint(Secondary, pose);
        if (SecondarySlideEnd == null) return start;
        Vector3 segment = FittedPoint(SecondarySlideEnd, pose) - start;
        return start + segment * Mathf.Clamp01(Vector3.Dot(authoredPalm - start, segment) / Mathf.Max(segment.sqrMagnitude, .000001f));
    }
}
