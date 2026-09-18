using UnityEngine;

/// <summary>Water-constrained movement for both solitary fish and members of a school.</summary>
public static class FishMovement
{
    public static bool IsHabitat(IWaterQueryService water, Vector3 point, ushort body, float clearance,
        FishSpecies species = null)
        => TryGetHabitat(water, point, body, clearance, out _, species);

    internal static bool TryGetHabitat(IWaterQueryService water, Vector3 point, ushort body, float clearance,
        out WaterSample sample, FishSpecies species = null)
        => TryGetHabitat(new ManagedWaterQuery(water), point, body, FishRules.From(species, clearance), out sample);

    internal static bool TryGetHabitat<T>(T water, Vector3 point, ushort body, FishRules rules, out WaterSample sample)
        where T : struct, IWaterQueryService
    {
        float clearance = rules.Clearance;
        sample = default;
        if (!CharacterMath.IsFinite(point) || float.IsNaN(clearance) ||
            float.IsInfinity(clearance) || clearance <= 0f || body == 0) return false;
        if (!water.TryGetWaterSurface(point, out sample) || !Fits(sample, body, rules)) return false;
        if (!CharacterMath.IsFinite(sample.Normal) || sample.Normal.sqrMagnitude < 0.99f) return false;
        Vector3 right = CharacterMath.ArbitraryTangent(sample.Normal);
        Vector3 forward = Vector3.Cross(sample.Normal, right);
        // Check the body's sides as well as its centre. Clearance encloses the model at every heading.
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = (i < 2 ? right : forward) * ((i & 1) == 0 ? clearance : -clearance);
            if (!water.TryGetWaterSurface(point + offset, out var edge) || !Fits(edge, body, rules)) return false;
        }
        return true;
    }

    static bool Fits(WaterSample sample, ushort body, FishRules rules) =>
        sample.BodyId == body && !float.IsInfinity(sample.BodyDepth) &&
        sample.SignedDepth >= rules.Clearance + rules.QueryMargin &&
        sample.BodyDepth - sample.SignedDepth >= rules.Clearance + rules.QueryMargin && rules.Accepts(sample);

    /// <summary>Stop before an invalid segment. Never clamp across a shoreline into a separate body.</summary>
    public static bool TryMove(IWaterQueryService water, Vector3 from, Vector3 desired, ushort body,
        float clearance, out Vector3 position, FishSpecies species = null)
    {
        position = from;
        if (!IsHabitat(water, from, body, clearance, species)) return false;
        return TryMoveFromHabitat(water, from, desired, body, clearance, out position, species);
    }

    // FishSchool already checked every starting clearance sample in this same synchronous substep.
    internal static bool TryMoveFromHabitat(IWaterQueryService water, Vector3 from, Vector3 desired, ushort body,
        float clearance, out Vector3 position, FishSpecies species)
        => TryMoveFromHabitat(new ManagedWaterQuery(water), from, desired, body, FishRules.From(species, clearance), out position);

    internal static bool TryMoveFromHabitat<T>(T water, Vector3 from, Vector3 desired, ushort body, FishRules rules, out Vector3 position)
        where T : struct, IWaterQueryService
    {
        float clearance = rules.Clearance;
        position = from;
        if (!CharacterMath.IsFinite(desired)) return false;
        float distance = Vector3.Distance(from, desired);
        // Bounded queries; a long teleport is not swimming. The caller must subdivide large time steps.
        if (distance > 8f) return false;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Min(0.25f, clearance)));
        if (steps > 128) return false;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 candidate = Vector3.Lerp(from, desired, (float)i / steps);
            if (!TryGetHabitat(water, candidate, body, rules, out _)) return false;
        }
        position = desired;
        return true;
    }
}

public readonly struct ManagedWaterQuery : IWaterQueryService
{
    readonly IWaterQueryService _water;
    public ManagedWaterQuery(IWaterQueryService water) => _water = water;
    public bool TryGetWaterSurface(Vector3 point, out WaterSample sample)
    { sample = default; return _water != null && _water.TryGetWaterSurface(point, out sample); }
    public bool IsUnderwater(Vector3 point) => TryGetWaterSurface(point, out var sample) && sample.IsSubmerged;
}
