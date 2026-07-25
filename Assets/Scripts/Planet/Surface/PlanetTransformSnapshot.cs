using UnityEngine;

// Immutable capture of a planet Transform, taken on the main thread so placement math can run on a
// background thread (Unity Transform access is main-thread only). Exposes the point/direction
// conversions the gather needs without touching the live Transform. Assumes uniform scale, like the
// rest of the planet surface code.
public readonly struct PlanetTransformSnapshot
{
    public readonly Vector3 Center;
    public readonly Quaternion Rotation;
    public readonly float UniformScale;
    readonly Matrix4x4 _localToWorld;

    PlanetTransformSnapshot(Vector3 center, Quaternion rotation, float uniformScale, Matrix4x4 localToWorld)
    {
        Center = center;
        Rotation = rotation;
        UniformScale = uniformScale;
        _localToWorld = localToWorld;
    }

    public static PlanetTransformSnapshot Capture(Transform t)
    {
        Vector3 s = t.lossyScale;
        float uniform = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        return new PlanetTransformSnapshot(t.position, t.rotation, uniform, t.localToWorldMatrix);
    }

    public Vector3 TransformPoint(Vector3 localPoint) => _localToWorld.MultiplyPoint3x4(localPoint);

    // Direction only (matches Transform.InverseTransformDirection semantics: rotation, not scale).
    public Vector3 InverseTransformDirection(Vector3 worldDirection) => Quaternion.Inverse(Rotation) * worldDirection;
}
