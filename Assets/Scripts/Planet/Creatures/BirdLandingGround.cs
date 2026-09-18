using UnityEngine;

/// <summary>Terrain-only bird support. Branches require authored sockets, not inferred tree bounds.</summary>
public sealed class BirdLandingGround
{
    readonly IPlanetSurfaceSampler _surface;
    readonly Vector3 _center;
    readonly float _seaRadius;
    readonly CharacterWaterFloor _water;

    public BirdLandingGround(IPlanetSurfaceSampler surface, Vector3 center, float seaRadius,
        CharacterWaterFloor water = default)
    {
        _surface = surface;
        _center = center;
        _seaRadius = seaRadius;
        _water = water;
    }

    public bool TryFind(Vector3 candidate, float bodyHeight, out Vector3 position)
    {
        position = default;
        if (_surface == null || !CharacterMath.IsFinite(candidate) ||
            float.IsNaN(bodyHeight) || float.IsInfinity(bodyHeight) || bodyHeight <= 0f) return false;
        Vector3 radial = candidate - _center;
        if (radial.sqrMagnitude < 1f) return false;
        Vector3 up = radial.normalized;
        if (!TryDryPoint(up, out Vector3 point)) return false;
        Vector3 right = CharacterMath.ArbitraryTangent(up);
        Vector3 forward = Vector3.Cross(up, right);
        float span = Mathf.Max(0.5f, bodyHeight);
        float rightRise = 0f, forwardRise = 0f;
        // Four footprint probes reject shorelines, cliffs, and slopes above thirty degrees.
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = (i < 2 ? right : forward) * ((i & 1) == 0 ? span : -span);
            if (!TryDryPoint((point + offset - _center).normalized, out Vector3 sample)) return false;
            float rise = Mathf.Abs(Vector3.Dot(sample - point, up));
            if (i < 2) rightRise = Mathf.Max(rightRise, rise);
            else forwardRise = Mathf.Max(forwardRise, rise);
        }
        if (rightRise * rightRise + forwardRise * forwardRise > span * span / 3f) return false;
        position = point;
        return true;
    }

    public bool TryFindNear(Vector3 candidate, float bodyHeight, float radius, out Vector3 position)
    {
        position = default;
        if (!float.IsFinite(radius) || radius < 0f) return false;
        if (TryFind(candidate, bodyHeight, out position)) return true;
        if (radius == 0f || !CharacterMath.IsFinite(candidate)) return false;
        Vector3 up = (candidate - _center).normalized;
        if (up.sqrMagnitude < .5f) return false;
        Vector3 tangent = CharacterMath.ArbitraryTangent(up);
        for (int i = 0; i < 8; i++)
            if (TryFind(candidate + Quaternion.AngleAxis(i * 45f, up) * tangent * radius, bodyHeight, out position)) return true;
        return false;
    }

    bool TryDryPoint(Vector3 up, out Vector3 point)
    {
        point = default;
        if (!_surface.TryGetSurfaceRadius(up, out float radius) ||
            float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) return false;
        point = _center + up * radius;
        return radius > Mathf.Max(_seaRadius, _water.RadiusAt(point, up)) + 0.1f;
    }
}
