using UnityEngine;

public struct ScatterWaterRules
{
    public float MinDepthMeters;
    public float MaxDepthMeters;
    public float MaxSpeedMetersPerSecond;
    public bool RequireShelter;
}

public static class ScatterWaterPlacement
{
    // Shelter is supplied by reviewed habitat data, never inferred from zero velocity.
    public static bool PassesHabitat(bool hasWater, in WaterSample water,
        bool knownSheltered, in ScatterWaterRules rules)
    {
        if (!hasWater || water.BodyId == 0 || water.IsOcean) return false;
        if (!Finite(rules.MinDepthMeters) || rules.MinDepthMeters < 0f ||
            !Finite(rules.MaxDepthMeters) || rules.MaxDepthMeters < rules.MinDepthMeters ||
            !Finite(rules.MaxSpeedMetersPerSecond) || rules.MaxSpeedMetersPerSecond < 0f)
            return false;

        // BodyDepth is clamped by WaterQueryKernel. Dry points have zero depth even if a body mask overlaps.
        if (!Finite(water.BodyDepth) || water.BodyDepth <= 0f ||
            water.BodyDepth < rules.MinDepthMeters || water.BodyDepth > rules.MaxDepthMeters)
            return false;
        if (!Finite(water.Velocity.x) || !Finite(water.Velocity.y) || !Finite(water.Velocity.z))
            return false;
        float speed = water.Velocity.magnitude;
        if (!Finite(speed) || speed > rules.MaxSpeedMetersPerSecond) return false;
        return !rules.RequireShelter || knownSheltered;
    }

    // Radii describe the same canonical surface used by the water query, in planet-local units.
    // The visual offset affects the anchor only. Altitude always describes the bed in world metres.
    public static bool TryAnchor(bool onWater, float groundRadiusLocal, float waterRadiusLocal,
        float worldScale, out float placementRadiusLocal, out float bedAltitudeMeters)
    {
        placementRadiusLocal = 0f;
        bedAltitudeMeters = 0f;
        if (!Finite(groundRadiusLocal) || groundRadiusLocal <= 0f ||
            !Finite(waterRadiusLocal) || waterRadiusLocal <= 0f ||
            !Finite(worldScale) || worldScale <= 0f)
            return false;

        float altitude = (groundRadiusLocal - waterRadiusLocal) * worldScale;
        float radius = onWater
            ? waterRadiusLocal + ScatterPlacementMath.OnWaterSurfaceOffsetMeters / worldScale
            : groundRadiusLocal;
        if (!Finite(altitude) || !Finite(radius)) return false;
        if (onWater && altitude >= 0f) return false;
        placementRadiusLocal = radius;
        bedAltitudeMeters = altitude;
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
