using UnityEngine;

/// <summary>
/// Gravity that points from a world position toward a fixed planet center (a sphere world). The center is
/// captured at construction; rebuild the provider on planet regeneration rather than mutating it, so a
/// driver never straddles two centers within a frame.
/// </summary>
public sealed class RadialGravityProvider : IGravityProvider
{
    readonly Vector3 _center;
    readonly float _magnitude;

    public RadialGravityProvider(Vector3 center, float magnitude = 9.81f)
    {
        _center = center;
        _magnitude = magnitude;
    }

    public bool TryGetGravity(Vector3 worldPos, out Vector3 acceleration)
    {
        acceleration = Vector3.zero;
        if (_magnitude <= 0f || !float.IsFinite(_magnitude) || !CharacterMath.IsFinite(worldPos))
            return false;

        Vector3 toCenter = _center - worldPos;
        if (toCenter.sqrMagnitude < 1e-8f) // sampled at the center: down is undefined
            return false;

        acceleration = toCenter.normalized * _magnitude;
        return true;
    }
}
