public enum BiomeType
{
    Ocean,
    Beach,
    Desert,
    Savanna,
    Tropical,
    Scrub,
    Grassland,
    Forest,
    Steppe,
    Taiga,
    Swamp,
    Tundra,
    Snow,
    IceBog,
    Mountain,
    Cave,
    Underwater
}

public struct BiomeResult
{
    public BiomeType PrimaryBiome;
    public BiomeType SecondaryBiome;
    public float BlendWeight;
    public float Temperature;
    public float Moisture;

    public BiomeResult(BiomeType primary, float temperature, float moisture)
    {
        PrimaryBiome = primary;
        SecondaryBiome = primary;
        BlendWeight = 0f;
        Temperature = temperature;
        Moisture = moisture;
    }
}

public readonly struct ClimateSample
{
    public readonly float Temperature01;
    public readonly float Moisture01;
    public readonly float Elevation;
    public readonly float Latitude01;
    public readonly float TemperatureLatitude01;
    public readonly float TemperatureNoiseContribution;
    public readonly float AltitudeTemperatureDrop;
    public readonly float MoistureLatitude01;
    public readonly float MoistureNoiseContribution;
    public readonly float LegacyMoisture01;

    public ClimateSample(float temperature01, float moisture01, float elevation)
        : this(
            temperature01,
            moisture01,
            elevation,
            0f,
            temperature01,
            0f,
            0f,
            moisture01,
            0f,
            moisture01)
    {
    }

    public ClimateSample(
        float temperature01,
        float moisture01,
        float elevation,
        float latitude01,
        float temperatureLatitude01,
        float temperatureNoiseContribution,
        float altitudeTemperatureDrop,
        float moistureLatitude01,
        float moistureNoiseContribution,
        float legacyMoisture01)
    {
        Temperature01 = temperature01;
        Moisture01 = moisture01;
        Elevation = elevation;
        Latitude01 = latitude01;
        TemperatureLatitude01 = temperatureLatitude01;
        TemperatureNoiseContribution = temperatureNoiseContribution;
        AltitudeTemperatureDrop = altitudeTemperatureDrop;
        MoistureLatitude01 = moistureLatitude01;
        MoistureNoiseContribution = moistureNoiseContribution;
        LegacyMoisture01 = legacyMoisture01;
    }
}
