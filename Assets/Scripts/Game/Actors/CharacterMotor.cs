using UnityEngine;

/// <summary>
/// Pure, planet-agnostic, actor-agnostic locomotion math. Given the local <c>up</c> (gravity-negated) it
/// walks along that tangent plane and returns the displaced position BEFORE grounding — the caller supplies
/// <c>up</c> (from a gravity provider) and grounds the result. A constant <c>up</c> is just a "flat world";
/// the motor never learns about planets or gravity sources.
/// </summary>
public static class CharacterMotor
{
    /// <summary>
    /// Walk along the tangent plane defined by <paramref name="up"/>, at <paramref name="speed"/>, for
    /// <paramref name="dt"/>, using a camera-derived forward for the movement basis. <paramref name="next"/>
    /// is the displaced world position before grounding. Returns false (and leaves <paramref name="next"/> =
    /// <paramref name="currentPos"/>) when <paramref name="up"/> is zero-length or non-finite, so no NaN can
    /// escape into a shared caller — a reusable API must not rely on a planet-specific precondition.
    /// </summary>
    public static bool TryStep(
        Vector3 up, Vector3 currentPos, Vector2 moveInput,
        Vector3 cameraForward, float speed, float dt, out Vector3 next)
    {
        next = currentPos;

        if (!CharacterMath.IsFinite(up) || up.sqrMagnitude < 1e-12f)
            return false;
        if (!CharacterMath.IsFinite(currentPos) || !float.IsFinite(speed) || !float.IsFinite(dt))
            return false;

        up = up.normalized;

        // Movement basis: camera forward projected onto the tangent plane; if the camera looks along up
        // (degenerate), fall back to an arbitrary tangent rather than emit a zero/NaN direction.
        if (!CharacterMath.TryProjectOntoTangent(cameraForward, up, out Vector3 forward))
            forward = CharacterMath.ArbitraryTangent(up);

        Vector3 right = Vector3.Cross(up, forward);

        Vector3 move = right * moveInput.x + forward * moveInput.y;
        float mag = move.magnitude;
        if (mag > 1f)
            move /= mag;

        next = currentPos + move * (speed * dt);
        return true;
    }
}
