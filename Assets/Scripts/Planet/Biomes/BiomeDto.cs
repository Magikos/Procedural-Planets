using UnityEngine;

public sealed record BiomeDto(
    BiomeRegistryDto Registry,
    AnimationCurve TemperatureLatitudeCurve,
    AnimationCurve MoistureLatitudeCurve,
    float MoistureLatitudeInfluence,
    float MoistureNoiseStrength,
    float AltitudeTemperatureDrop,
    int ClimateLutResolution,
    float MinimumTemperatureCelsius,
    float MaximumTemperatureCelsius,
    int ClimateMapResolution,
    float TemperatureNoiseStrength,
    int VoronoiSeedCount,
    float VoronoiSeedJitter,
    float VoronoiDomainWarpStrength)
{
    public static BiomeDto From(BiomeSettings src)
    {
        if (src == null) return null;
        src.EnsureClimateCurves();
        return new BiomeDto(
            BiomeRegistryDto.From(src.Registry),
            src.TemperatureLatitudeCurve,
            src.MoistureLatitudeCurve,
            src.MoistureLatitudeInfluence,
            src.MoistureNoiseStrength,
            src.AltitudeTemperatureDrop,
            src.ClimateLutResolution,
            src.MinimumTemperatureCelsius,
            src.MaximumTemperatureCelsius,
            src.ClimateMapResolution,
            src.TemperatureNoiseStrength,
            src.VoronoiSeedCount,
            src.VoronoiSeedJitter,
            src.VoronoiDomainWarpStrength);
    }
}
