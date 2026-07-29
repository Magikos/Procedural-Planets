using UnityEngine;

public struct PlacementRules
{
    public float Weight;
    public float MaxSlopeCos;   // cos(maxSlope + fade)
    public float MinSlopeCos;   // cos(maxSlope)
    public bool HasMinAltitude; public float MinAltitude;
    public bool HasMaxAltitude; public float MaxAltitude;
    public float MinWaterClearance;
    public Vector2 ScaleRange;
    public bool RandomYaw;
}

// The single HLSL-portable placement decision: no managed sampler calls. Surface height, biome
// membership, and slope are inputs the caller sampled; keeping them out means the SP3 compute
// kernel can mirror this function against baked textures. This is the CPU<->GPU parity surface.
public static class ScatterPlacementMath
{
    // At zero fade MaxSlopeCos == MinSlopeCos and InverseLerp(a,a,x) returns 0, which would reject
    // flat ground too. Degenerate interval -> explicit hard cutoff.
    static float SlopeKeep(float maxSlopeCos, float minSlopeCos, float slopeCos)
    {
        if (minSlopeCos - maxSlopeCos <= 1e-5f)
            return slopeCos >= minSlopeCos ? 1f : 0f;
        return Mathf.InverseLerp(maxSlopeCos, minSlopeCos, slopeCos);
    }

    // Altitude + water-clearance gate, split out so the gather can reject a candidate BEFORE it pays
    // for the slope normal (2 extra ground samples). TryPlace is the sole owner and calls the same
    // predicate, so the fast-path pre-check can never drift from the final placement decision.
    public static bool PassesAltitudeWater(float altitudeMeters, bool hasOcean, in PlacementRules rules)
    {
        if (rules.HasMinAltitude && altitudeMeters < rules.MinAltitude) return false;
        if (rules.HasMaxAltitude && altitudeMeters > rules.MaxAltitude) return false;
        if (hasOcean && rules.MinWaterClearance > 0f && altitudeMeters < rules.MinWaterClearance) return false;
        return true;
    }

    // dir/radius are LOCAL (caller converts to world). slopeCos = dot(surfaceNormal, dir).
    // altitudeMeters = signed metres above sea. densityKeep = areaKeep * membership^blendPower,
    // folded in by the caller so this function stays free of biome types.
    public static bool TryPlace(uint slotSeed, Vector3 dir, float localRadius,
        float altitudeMeters, float slopeCos, float densityKeep, bool hasOcean, in PlacementRules rules,
        out Vector3 posLocal, out Quaternion rot, out float scale)
    {
        posLocal = default; rot = Quaternion.identity; scale = 0f;

        if (!PassesAltitudeWater(altitudeMeters, hasOcean, rules)) return false;

        float slopeKeep = SlopeKeep(rules.MaxSlopeCos, rules.MinSlopeCos, slopeCos);
        if (slopeKeep <= 0f) return false;

        float accept = rules.Weight * slopeKeep * Mathf.Clamp01(densityKeep);
        if (ScatterHash.To01(slotSeed) >= accept) return false;

        posLocal = dir * localRadius;
        scale = Mathf.Lerp(rules.ScaleRange.x, rules.ScaleRange.y, ScatterHash.To01(ScatterHash.Slot(slotSeed, 7)));
        Quaternion align = Quaternion.FromToRotation(Vector3.up, dir);
        float yaw = rules.RandomYaw ? ScatterHash.To01(ScatterHash.Slot(slotSeed, 9)) * 360f : 0f;
        rot = Quaternion.AngleAxis(yaw, dir) * align; // LOCAL rotation; caller applies planet rotation
        return true;
    }
}
