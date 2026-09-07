using UnityEngine;

/// <summary>Foot probes use visible mesh normals, with analytic support while chunks are unavailable.</summary>
public sealed class CreaturePoseGrounding : IGroundingProvider
{
    readonly IPlanetSurfaceRaycaster _surface;
    readonly IGroundingProvider _fallback;

    public CreaturePoseGrounding(IPlanetSurfaceRaycaster surface, IGroundingProvider fallback)
    {
        _surface = surface;
        _fallback = fallback;
    }

    public bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)
    {
        result = default;
        if (!CharacterMath.IsFinite(worldPos) || downDir.sqrMagnitude < 1e-8f) return false;
        Vector3 down = downDir.normalized;
        if (_surface != null && _surface.TryRaycastSurface(new Ray(worldPos - down, down), 2f, out PlanetSurfaceRaycastHit hit))
        {
            result = new GroundResult(hit.Point - down * footOffset, hit.Normal);
            return true;
        }
        return _fallback != null && _fallback.TryGround(worldPos, down, footOffset, out result);
    }
}
