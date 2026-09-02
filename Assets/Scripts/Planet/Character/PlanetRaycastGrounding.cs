using UnityEngine;

/// <summary>
/// Grounding against the VISIBLE terrain mesh via a downward surface raycast. This matters because the planet's
/// analytic surface radius (<see cref="IPlanetSurfaceSampler.TryGetSurfaceRadius"/>) can sit BELOW the rendered
/// chunk mesh — grounding on the analytic radius sinks the character under the terrain you actually see.
/// <see cref="IPlanetSurfaceRaycaster.TryRaycastSurface"/> hits the visible surface, so the character stands on
/// exactly what's drawn. Falls back to an analytic grounding when a ray misses (a hole/edge). Normal stays
/// radial (upright); a per-position water floor keeps the character on the surface of the body he is in.
/// </summary>
public sealed class PlanetRaycastGrounding : IGroundingProvider
{
    const float ProbeUp = 400f; // start the down-ray this far above the body to clear any terrain relief

    readonly IPlanetSurfaceRaycaster _raycaster;
    readonly IGroundingProvider _fallback;
    readonly Vector3 _center;
    readonly CharacterWaterFloor _water;

    public PlanetRaycastGrounding(
        IPlanetSurfaceRaycaster raycaster, Vector3 center, CharacterWaterFloor water, IGroundingProvider fallback)
    {
        _raycaster = raycaster;
        _center = center;
        _water = water;
        _fallback = fallback;
    }

    public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
    {
        result = default;
        if (!CharacterMath.IsFinite(worldPos))
            return false;

        Vector3 fromCenter = worldPos - _center;
        if (fromCenter.sqrMagnitude < 1e-8f)
            return false;
        Vector3 up = fromCenter.normalized;

        if (_raycaster != null)
        {
            Vector3 origin = worldPos + up * ProbeUp;
            if (_raycaster.TryRaycastSurface(new Ray(origin, -up), ProbeUp * 2f, out PlanetSurfaceRaycastHit hit))
            {
                float radius = Mathf.Max(Vector3.Dot(hit.Point - _center, up), _water.RadiusAt(worldPos, up));
                result = new GroundResult(_center + up * (radius + footOffset), up);
                return true;
            }
        }

        // Ray missed the visible surface (edge/hole) — fall back to the analytic surface.
        return _fallback != null && _fallback.TryGround(worldPos, downDir, footOffset, out result);
    }
}
