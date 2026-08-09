using UnityEngine;

/// <summary>
/// "Which way is down, and how strong?" — the composable gravity capability a locomotion driver consumes.
/// Swapping the implementation is how a flat test plane, the radial sphere, flight, and anti-gravity are
/// expressed without rewriting the driver. Acceleration is in m/s²; callers that only need orientation take
/// <c>up = -acceleration.normalized</c>.
/// </summary>
public interface IGravityProvider
{
    /// <summary>
    /// Gravitational acceleration (m/s²) at <paramref name="worldPos"/>. On success the returned vector is
    /// guaranteed finite and NON-ZERO (callers derive <c>up</c> by normalization, so zero is not a valid
    /// success). Returns false + <see cref="Vector3.zero"/> where gravity is undefined (e.g. a radial field
    /// sampled exactly at its center, or a configured magnitude of zero).
    /// </summary>
    bool TryGetGravity(Vector3 worldPos, out Vector3 acceleration);
}
