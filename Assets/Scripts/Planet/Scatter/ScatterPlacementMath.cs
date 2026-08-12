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
    public float ConformToSlope;   // 0 = up stays radial (trees); 1 = up lies on the surface normal (rocks)
    public float BaseRadius;       // ground-contact footprint radius; sinks a tilted wide base so it doesn't float
}

// The single HLSL-portable placement decision: no managed sampler calls. Surface height, biome
// membership, and slope are inputs the caller sampled; keeping them out means the SP3 compute
// kernel can mirror this function against baked textures. This is the CPU<->GPU parity surface.
public static class ScatterPlacementMath
{
    // OnWater scatter (lily pads) sits this many metres above the sea surface so it rides ON the water
    // instead of z-fighting the coplanar water mesh / reading as submerged under the water tint. Small
    // enough not to look like it floats on a calm lake.
    public const float OnWaterSurfaceOffsetMeters = 0.15f;


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
    public static bool TryPlace(uint slotSeed, Vector3 dir, Vector3 surfaceNormal, float localRadius,
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
        // Ground-hugging props (rocks, conform=1) stand on the local surface normal; tall props (trees,
        // conform=0) stay radial. conform=0 keeps up == dir exactly, so trees/golden placements don't drift.
        Vector3 up = rules.ConformToSlope > 0f
            ? Vector3.Normalize(Vector3.Lerp(dir, surfaceNormal, rules.ConformToSlope))
            : dir;
        // Partial conform tilts the base off the slope; a wide base then floats its downhill edge. Sink the
        // prop along the surface normal by baseRadius*scale*sin(tilt) so the lifted edge meets the ground.
        float sinTilt = Vector3.Cross(up, surfaceNormal).magnitude;
        posLocal -= surfaceNormal * (rules.BaseRadius * scale * sinTilt);
        Quaternion align = Quaternion.FromToRotation(Vector3.up, up);
        float yaw = rules.RandomYaw ? ScatterHash.To01(ScatterHash.Slot(slotSeed, 9)) * 360f : 0f;
        rot = Quaternion.AngleAxis(yaw, up) * align; // LOCAL rotation; caller applies planet rotation
        return true;
    }
}
