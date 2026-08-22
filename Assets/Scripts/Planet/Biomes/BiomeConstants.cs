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

    // Height above a lake's own surface over which LakeShore hands off to the surrounding land. 12.5 m at
    // R=5000.
    //
    // This one cannot use BoundaryBlendWeight like every other boundary here. That helper tops out at 0.5
    // because it expects BOTH sides to blend toward each other and meet at a 50/50 mix. The land outside a
    // lake's shore ring has lakeState == 0, so it cannot know a lake is near and cannot blend back - the ring
    // has to carry the whole ramp from pure shore to pure land on its own, which means reaching 1.
    //
    // Keep it comfortably shorter than the ring is tall or the ramp runs out of ring before it finishes and
    // the hard edge comes back, just further out.
    public const float LakeShoreBlendHeight = 0.0025f;

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
