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

    public ClimateSample(float temperature01, float moisture01, float elevation)
    {
        Temperature01 = temperature01;
        Moisture01 = moisture01;
        Elevation = elevation;
    }
}
