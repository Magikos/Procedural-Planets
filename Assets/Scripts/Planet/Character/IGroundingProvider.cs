using UnityEngine;

/// <summary>Where the supporting surface is, and its normal, for a grounded pose.</summary>
public readonly struct GroundResult
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;

    public GroundResult(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
    }
}

/// <summary>
/// "Where is the supporting surface?" — the composable grounding capability. Swapping the implementation is
/// how a flat test plane, the analytic sphere, and future collider/marching-cubes ground slot in without
/// rewriting the driver.
/// </summary>
public interface IGroundingProvider
{
    /// <summary>
    /// Resolve a grounded pose for a body at <paramref name="worldPos"/> falling along <paramref name="downDir"/>,
    /// sitting <paramref name="footOffset"/> above the surface. Returns true with a valid grounded
    /// <paramref name="result"/> (its <see cref="GroundResult.Position"/> and <see cref="GroundResult.Normal"/>
    /// are meaningful); returns false when there is no support, in which case <paramref name="result"/> is
    /// default and the caller keeps its prior settled pose.
    /// </summary>
    bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result);
}
