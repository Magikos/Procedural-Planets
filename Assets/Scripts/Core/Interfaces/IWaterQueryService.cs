using UnityEngine;

/// <summary>What the water is doing at a world position.</summary>
/// <remarks>
/// Deliberately primitives plus a stable id, with no reference to any planet type. Gameplay code lives in
/// its own assembly and may not reference the planet layer, so anything that returns a body record or a
/// catalog reference cannot be consumed by the systems this exists for.
/// </remarks>
public readonly struct WaterSample
{
    /// <summary>World point on the water surface directly above or below the query.</summary>
    public readonly Vector3 SurfacePoint;

    /// <summary>Outward surface normal. Radial for a still body; swell tilts it once that lands.</summary>
    public readonly Vector3 Normal;

    /// <summary>
    /// Metres the query point sits BELOW the surface. Negative above it, so a caller reads submersion,
    /// clearance and the crossing between them from one number without a second call.
    /// </summary>
    public readonly float SignedDepth;

    /// <summary>Metres from the surface down to the bed beneath the query point.</summary>
    public readonly float BodyDepth;

    /// <summary>Stable per-world id of the body, 0 when none. Distinguishes two lakes at the same height.</summary>
    public readonly ushort BodyId;

    /// <summary>True for the ocean, false for a lake. The distinction gameplay actually branches on.</summary>
    public readonly bool IsOcean;

    /// <summary>World-space downstream current in metres per second. Zero in still water.</summary>
    public readonly Vector3 Velocity;

    public WaterSample(Vector3 surfacePoint, Vector3 normal, float signedDepth, float bodyDepth,
        ushort bodyId, bool isOcean, Vector3 velocity = default)
    {
        SurfacePoint = surfacePoint;
        Normal = normal;
        SignedDepth = signedDepth;
        BodyDepth = bodyDepth;
        BodyId = bodyId;
        IsOcean = isOcean;
        Velocity = velocity;
    }

    public bool IsSubmerged => SignedDepth > 0f;
}

/// <summary>
/// Where the water is, for anything that has to float in it, swim through it, or decide it is standing in
/// it. Registered per world; resolve once at init and hold the reference, never per frame.
/// </summary>
/// <remarks>
/// <para>
/// Water is never replicated - every client derives it from the world seed - so every answer here must be
/// identical across machines given identical inputs. That is why time is a PARAMETER rather than something
/// the service reads. Reading a clock internally would make the result depend on per-process wall time,
/// and a boat would float at a different height on each client with nothing on the wire to correct it.
/// A caller on a fixed simulation tick passes the world tick; a caller that only needs the still surface
/// passes nothing.
/// </para>
/// </remarks>
public interface IWaterQueryService
{
    /// <summary>
    /// Still water surface at a world position, ignoring waves. Exact and cheap: it reads the solved
    /// per-body level rather than reconstructing anything, so it is safe to call at simulation rate.
    /// </summary>
    bool TryGetWaterSurface(Vector3 worldPosition, out WaterSample sample);

    /// <summary>Convenience for the common test. False when the position is not inside any body.</summary>
    bool IsUnderwater(Vector3 worldPosition);
}
