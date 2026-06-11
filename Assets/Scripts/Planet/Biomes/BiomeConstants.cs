using UnityEngine;

public static class BiomeConstants
{
    // Elevation thresholds (planet-radius-relative)
    public const float OceanThreshold = 0f;
    public const float BeachWidth = 0.001f;
    public const float MountainThreshold = 0.1f;

    // Transition bands
    public const float BlendWidth = 0.003f;
    public const float ElevationBlendWidth = 0.001f;

    // Voronoi biome assignment
    public const float VoronoiTemperatureWeight = 4.26f;
    public const float VoronoiDomainWarpScale = 2.5f;
    public const int VoronoiDomainWarpOctaves = 4;
    public const float VoronoiDomainWarpPersistence = 0.418f;
    public const float VoronoiDomainWarpLacunarity = 2.73f;
    public const int VoronoiCleanupIterations = 5;

    public static readonly NoiseSettings TemperatureNoise = new NoiseSettings
    {
        Filter = NoiseSettings.FilterType.Simple,
        Strength = 1f,
        Layers = 3,
        BaseRoughness = 0.8f,
        Roughness = 2f,
        Persistence = 0.5f,
        Center = new Vector3(100f, 0f, 0f),
        MinValue = 0f
    };

    public static readonly NoiseSettings MoistureNoise = new NoiseSettings
    {
        Filter = NoiseSettings.FilterType.Simple,
        Strength = 1f,
        Layers = 4,
        BaseRoughness = 1.2f,
        Roughness = 2.5f,
        Persistence = 0.5f,
        Center = new Vector3(0f, 0f, 200f),
        MinValue = 0f
    };
}
