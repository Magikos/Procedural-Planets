using UnityEngine;

/// <summary>
/// A settled character pose: where it stands, which way is up (gravity-negated), and which way it faces.
/// Passed to a locomotion driver as its seed so its stateful position/up/facing never start from
/// <see cref="Vector3.zero"/> (which, for radial gravity, is the invalid planet-center case).
/// </summary>
public readonly struct CharacterPose
{
    public readonly Vector3 Position;
    public readonly Vector3 Up;
    public readonly Vector3 Forward;

    public CharacterPose(Vector3 position, Vector3 up, Vector3 forward)
    {
        Position = position;
        Up = up;
        Forward = forward;
    }

    /// <summary>True only when every component of every axis is finite and up/forward are non-degenerate.</summary>
    public bool IsFinite =>
        CharacterMath.IsFinite(Position) &&
        CharacterMath.IsFinite(Up) && Up.sqrMagnitude > 1e-10f &&
        CharacterMath.IsFinite(Forward) && Forward.sqrMagnitude > 1e-10f;
}

/// <summary>Small finite/tangent helpers shared by the actor-agnostic locomotion pieces.</summary>
public static class CharacterMath
{
    public static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    /// <summary>
    /// Project <paramref name="v"/> onto the plane whose normal is the unit <paramref name="up"/> and
    /// normalize. Returns false (and <paramref name="result"/> = default) when the projection is degenerate
    /// (v parallel to up), so callers can fall back to an arbitrary tangent rather than emit a zero/NaN dir.
    /// </summary>
    public static bool TryProjectOntoTangent(Vector3 v, Vector3 up, out Vector3 result)
    {
        Vector3 projected = v - up * Vector3.Dot(v, up);
        float mag = projected.magnitude;
        if (mag < 1e-5f)
        {
            result = default;
            return false;
        }
        result = projected / mag;
        return true;
    }

    /// <summary>
    /// Signed degrees from <paramref name="forward"/> to the direction of <paramref name="target"/>, both
    /// flattened into the tangent plane of <paramref name="up"/>. Positive turns toward the actor's right.
    /// Returns 0 when either direction is degenerate, which reads as "already facing it" and is the safe
    /// answer for a caller steering by this.
    /// </summary>
    public static float TangentBearing(Vector3 from, Vector3 forward, Vector3 up, Vector3 target)
    {
        if (!TryProjectOntoTangent(target - from, up, out Vector3 toTarget) ||
            !TryProjectOntoTangent(forward, up, out Vector3 face))
            return 0f;
        return Vector3.SignedAngle(face, toTarget, up);
    }

    /// <summary>Any unit vector in the tangent plane of <paramref name="up"/> — the degenerate-forward fallback.</summary>
    public static Vector3 ArbitraryTangent(Vector3 up)
    {
        // Cross with whichever world axis is least parallel to up, so the result is well-conditioned.
        Vector3 seed = Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right;
        return Vector3.Normalize(Vector3.Cross(up, seed));
    }
}
