using UnityEngine;

/// <summary>Water-constrained movement for both solitary fish and members of a school.</summary>
public static class FishMovement
{
    public static bool IsHabitat(IWaterQueryService water, Vector3 point, ushort body, float clearance,
        FishSpecies species = null)
    {
        if (water == null || !CharacterMath.IsFinite(point) || float.IsNaN(clearance) ||
            float.IsInfinity(clearance) || clearance <= 0f || body == 0) return false;
        if (!water.TryGetWaterSurface(point, out WaterSample sample) || !Fits(sample, body, clearance, species)) return false;
        if (!CharacterMath.IsFinite(sample.Normal) || sample.Normal.sqrMagnitude < 0.99f) return false;
        Vector3 right = CharacterMath.ArbitraryTangent(sample.Normal);
        Vector3 forward = Vector3.Cross(sample.Normal, right);
        // Check the body's sides as well as its centre. Clearance encloses the model at every heading.
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = (i < 2 ? right : forward) * ((i & 1) == 0 ? clearance : -clearance);
            if (!water.TryGetWaterSurface(point + offset, out var edge) || !Fits(edge, body, clearance, species)) return false;
        }
        return true;
    }

    static bool Fits(WaterSample sample, ushort body, float clearance, FishSpecies species) =>
        sample.BodyId == body && !float.IsInfinity(sample.BodyDepth) &&
        sample.SignedDepth >= clearance && sample.BodyDepth - sample.SignedDepth >= clearance &&
        (species == null || species.Accepts(sample));

    /// <summary>Stop before an invalid segment. Never clamp across a shoreline into a separate body.</summary>
    public static bool TryMove(IWaterQueryService water, Vector3 from, Vector3 desired, ushort body,
        float clearance, out Vector3 position, FishSpecies species = null)
    {
        position = from;
        if (!IsHabitat(water, from, body, clearance, species) || !CharacterMath.IsFinite(desired)) return false;
        float distance = Vector3.Distance(from, desired);
        // Bounded queries; a long teleport is not swimming. The caller must subdivide large time steps.
        if (distance > 8f) return false;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Min(0.25f, clearance)));
        if (steps > 128) return false;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 candidate = Vector3.Lerp(from, desired, (float)i / steps);
            if (!IsHabitat(water, candidate, body, clearance, species)) return false;
        }
        position = desired;
        return true;
    }
}
