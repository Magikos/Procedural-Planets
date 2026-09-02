using UnityEngine;

/// <summary>
/// Grounding on the analytic planet surface. This is the ONLY character piece that touches
/// <see cref="IPlanetSurfaceSampler"/> / the planet center — the driver and motor stay planet-agnostic.
/// The MVP normal is radial up: the sampler returns no normal, and a finite-difference gradient would be
/// slope-awareness, which is out of scope.
/// </summary>
public sealed class PlanetSurfaceGrounding : IGroundingProvider
{
    readonly IPlanetSurfaceSampler _sampler;
    readonly Vector3 _center;
    readonly CharacterWaterFloor _water;

    /// <param name="water">The character never grounds below the water surface AT HIS POSITION — in a body he
    /// walks on that body's surface instead of its bed (placeholder until swimming exists). Default disables.</param>
    public PlanetSurfaceGrounding(IPlanetSurfaceSampler sampler, Vector3 center, CharacterWaterFloor water = default)
    {
        _sampler = sampler;
        _center = center;
        _water = water;
    }

    public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
    {
        result = default;
        if (_sampler == null || !CharacterMath.IsFinite(worldPos))
            return false;

        Vector3 fromCenter = worldPos - _center;
        if (fromCenter.sqrMagnitude < 1e-8f)
            return false;

        Vector3 radial = fromCenter.normalized; // the sampler wants a world unit direction from center
        if (!_sampler.TryGetSurfaceRadius(radial, out float surfaceRadius))
            return false;

        float groundedRadius = Mathf.Max(surfaceRadius, _water.RadiusAt(worldPos, radial));
        result = new GroundResult(_center + radial * (groundedRadius + footOffset), radial);
        return true;
    }
}
