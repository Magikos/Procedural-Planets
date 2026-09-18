using UnityEngine;

public sealed record PlanetDto(
    float PlanetRadius,
    PlanetResolution Resolution,
    int MaxChunkDepth,
    float ContinentSize,
    float OceanDepth,
    float MountainHeight,
    float MountainDensity,
    float TerrainRoughness,
    bool EnableSurfaceOverrides,
    bool HasOceans,
    float OceanLevel,
    Color WaterColor,
    bool EnableFrozenWater,
    Color IceTint,
    Material PlanetMaterial,
    DiagnosticTerrainLayoutDto DiagnosticTerrainLayout)
{
    public static PlanetDto From(PlanetSettings src)
    {
        if (src == null) return null;
        return new PlanetDto(
            src.PlanetRadius,
            src.Resolution,
            src.MaxChunkDepth,
            src.ContinentSize,
            src.OceanDepth,
            src.MountainHeight,
            src.MountainDensity,
            src.TerrainRoughness,
            src.EnableSurfaceOverrides,
            src.HasOceans,
            src.OceanLevel,
            src.WaterColor,
            src.EnableFrozenWater,
            src.IceTint,
            src.PlanetMaterial,
            null);
    }

    public ShapeSettings BuildShapeSettings() => new()
    {
        PlanetRadius = PlanetRadius,
        NoiseLayers = BuildNoiseLayers(),
        DiagnosticTerrainLayout = DiagnosticTerrainLayout
    };

    // These recipe layers use persistence 0.5: sum(0.5^i) = 2 * (1 - 0.5^layers).
    static float OctaveAmplitudeSum(int layers) => 2f * (1f - Mathf.Pow(0.5f, layers));

    ShapeSettings.NoiseLayer[] BuildNoiseLayers()
    {
        var continent = new ShapeSettings.NoiseLayer
        {
            Enabled = true,
            UseFirstLayerAsMask = false,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = Mathf.Lerp(0.04f, 0.09f, OceanDepth),
                Layers = 4,
                BaseRoughness = Mathf.Lerp(0.6f, 1.4f, 1f - ContinentSize),
                Roughness = 2.2f,
                Persistence = 0.5f,
                Center = Vector3.zero,
                MinValue = Mathf.Lerp(0.9f, 1.0f, 1f - OceanDepth)
            }
        };

        var mountains = new ShapeSettings.NoiseLayer
        {
            Enabled = MountainHeight > 0.05f,
            UseFirstLayerAsMask = true,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Rigid,
                // Budget uplift as a fraction of radius, independent of octave count.
                Strength = 0.06f * Mathf.Clamp01(MountainHeight) / OctaveAmplitudeSum(4),
                Layers = 4,
                BaseRoughness = Mathf.Lerp(6f, 12f, MountainDensity),
                Roughness = 3f,
                Persistence = 0.5f,
                Center = new Vector3(0, 0, 4.61f),
                MinValue = 0f
            }
        };

        int detailLayers = Mathf.Clamp((int)(TerrainRoughness * 4) + 1, 1, 5);
        var detail = new ShapeSettings.NoiseLayer
        {
            Enabled = TerrainRoughness > 0.05f,
            UseFirstLayerAsMask = true,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = 0.005f * Mathf.Clamp01(TerrainRoughness) / OctaveAmplitudeSum(detailLayers),
                Layers = detailLayers,
                BaseRoughness = Mathf.Lerp(3f, 8f, TerrainRoughness),
                Roughness = 2.5f,
                Persistence = 0.5f,
                Center = new Vector3(100, 0, 0),
                MinValue = 0f
            }
        };

        return new[] { continent, mountains, detail };
    }
}

