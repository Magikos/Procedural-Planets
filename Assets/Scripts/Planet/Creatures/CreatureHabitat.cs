using System;
using UnityEngine;

/// <summary>Population capacity and terrain preferences. Existing residents keep their identities and homes.</summary>
[Serializable]
public sealed class CreatureHabitat
{
    [Serializable] public struct BiomeWeight { public BiomeType Biome; [Min(0f)] public float Weight; }
    public BiomeWeight[] BiomeWeights = Array.Empty<BiomeWeight>();
    [Min(0f)] public float DensityPerSquareKm = 20f;
    [Range(0f, 89f)] public float MaximumSlope = 45f;
    [Min(0f)] public float FreshWaterMultiplier = 1.5f;
    [Min(0f)] public float DryLandMultiplier = 1f;
    [Tooltip("Prefer suitable homes near mapped lake shores when several candidates are available.")]
    public bool PreferFreshWater = true;

    public CreatureHabitatDto Snapshot()
    {
        if (!float.IsFinite(DensityPerSquareKm) || DensityPerSquareKm < 0 ||
            !float.IsFinite(MaximumSlope) || MaximumSlope < 0 || MaximumSlope >= 90 ||
            !float.IsFinite(FreshWaterMultiplier) || FreshWaterMultiplier < 0 ||
            !float.IsFinite(DryLandMultiplier) || DryLandMultiplier < 0)
            throw new ArgumentException("Creature habitat values must be finite and within their stated ranges.");
        var weights = new System.Collections.Generic.Dictionary<BiomeType, float>();
        foreach (var item in BiomeWeights ?? Array.Empty<BiomeWeight>())
        {
            if (!float.IsFinite(item.Weight) || item.Weight < 0 || !weights.TryAdd(item.Biome, item.Weight))
                throw new ArgumentException("Creature habitat biome weights must be finite, nonnegative, and unique.");
        }
        return new(DensityPerSquareKm, MaximumSlope, FreshWaterMultiplier, DryLandMultiplier, PreferFreshWater)
        { BiomeWeights = new System.Collections.ObjectModel.ReadOnlyDictionary<BiomeType, float>(weights) };
    }
}

public sealed record CreatureHabitatDto(float DensityPerSquareKm, float MaximumSlope,
    float FreshWaterMultiplier, float DryLandMultiplier, bool PreferFreshWater)
{
    public System.Collections.Generic.IReadOnlyDictionary<BiomeType, float> BiomeWeights { get; init; }
    public double Occupancy(double areaSquareKm, int slots, bool freshWater, BiomeType biome = BiomeType.Grassland)
    {
        if (!double.IsFinite(areaSquareKm) || areaSquareKm < 0d) throw new ArgumentOutOfRangeException(nameof(areaSquareKm));
        float multiplier = freshWater ? FreshWaterMultiplier : DryLandMultiplier;
        float weight = BiomeWeights != null && BiomeWeights.TryGetValue(biome, out float value) ? value : 1f;
        if (!float.IsFinite(DensityPerSquareKm) || DensityPerSquareKm < 0f || !float.IsFinite(multiplier) || multiplier < 0f ||
            !float.IsFinite(weight) || weight < 0f)
            throw new InvalidOperationException("Habitat density and multipliers must be finite and nonnegative.");
        if (slots <= 0 || areaSquareKm == 0d || DensityPerSquareKm == 0f || multiplier == 0f || weight == 0f) return 0d;
        return Math.Clamp(DensityPerSquareKm * areaSquareKm * multiplier * weight / slots, 0d, 1d);
    }
}

public static class CreatureHabitatSampling
{
    /// <summary>Exact solid angle of the cube-face cell, scaled to square kilometres.</summary>
    public static double AreaSquareKm(EntityId slot, double radiusMeters)
    {
        if (!double.IsFinite(radiusMeters) || radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        if (!CreatureKey.IsCreature(slot)) throw new ArgumentException("A creature territory key is required.", nameof(slot));
        CreatureKey.Unpack(slot, out _, out int level, out int x, out int y, out _, out _);
        double width = 2d / (1 << level);
        double x0 = x * width - 1, y0 = y * width - 1, x1 = x0 + width, y1 = y0 + width;
        static double Corner(double u, double v) => Math.Atan2(u * v, Math.Sqrt(u * u + v * v + 1d));
        return Math.Abs(Corner(x1, y1) - Corner(x0, y1) - Corner(x1, y0) + Corner(x0, y0)) * radiusMeters * radiusMeters / 1e6d;
    }

    public static bool NearFreshWater(WaterBodyMap map, Vector3 localDirection) => map != null &&
        map.TrySampleBody(localDirection, out var body) && body.Kind == WaterBodyKind.Lake;

    public static float Slope(IPlanetSurfaceSampler sampler, Vector3 up, float radius)
    {
        if (sampler == null || !CharacterMath.IsFinite(up) || up.sqrMagnitude < 1e-8f || !float.IsFinite(radius) || radius <= 0f)
            return 90f;
        up.Normalize();
        Vector3 a = CharacterMath.ArbitraryTangent(up), b = Vector3.Cross(up, a);
        if (!sampler.TryGetSurfaceRadius((up * radius + a).normalized, out float a1) ||
            !sampler.TryGetSurfaceRadius((up * radius - a).normalized, out float a0) ||
            !sampler.TryGetSurfaceRadius((up * radius + b).normalized, out float b1) ||
            !sampler.TryGetSurfaceRadius((up * radius - b).normalized, out float b0) ||
            !float.IsFinite(a1) || !float.IsFinite(a0) || !float.IsFinite(b1) || !float.IsFinite(b0)) return 90f;
        return Mathf.Atan(Mathf.Sqrt((a1 - a0) * (a1 - a0) + (b1 - b0) * (b1 - b0)) * .5f) * Mathf.Rad2Deg;
    }
}
