using System.Text;
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

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("climate: tempNoise=").Append(TemperatureNoiseStrength.ToString("F3"));
        sb.Append(", moistureBands=").Append(MoistureLatitudeInfluence.ToString("F2"));
        sb.Append(", moistureNoise=").Append(MoistureNoiseStrength.ToString("F2"));
        sb.Append(", altitudeLapse=").Append(AltitudeTemperatureDrop.ToString("F2"));
        sb.Append(", lut=").Append(ClimateLutResolution);
        sb.Append(", map=").Append(ClimateMapResolution);
        sb.Append(", celsius=")
            .Append(MinimumTemperatureCelsius.ToString("F1"))
            .Append("..")
            .Append(MaximumTemperatureCelsius.ToString("F1"));
        sb.AppendLine();
        sb.Append("temperatureCurve=").Append(ClimateCurves.Describe(TemperatureLatitudeCurve));
        sb.AppendLine();
        sb.Append("moistureCurve=").Append(ClimateCurves.Describe(MoistureLatitudeCurve));
        sb.AppendLine();
        sb.Append("seeds=").Append(VoronoiSeedCount);
        sb.Append(", jitter=").Append(VoronoiSeedJitter.ToString("F2"));
        sb.Append(", warp=").Append(VoronoiDomainWarpStrength.ToString("F3"));
        return sb.ToString();
    }
}
