using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Planet Settings")]
public class PlanetSettings : ScriptableObject
{
    [Header("General")]
    [Range(1, 5000)] public float PlanetRadius = 50f;

    [Header("Terrain")]
    [Range(0.1f, 1f), Tooltip("Size of continents. Low = small islands, High = large landmasses")]
    public float ContinentSize = 0.5f;

    [Range(0f, 1f), Tooltip("How deep the ocean basins are")]
    public float OceanDepth = 0.3f;

    [Range(0f, 1f), Tooltip("Height of mountain peaks")]
    public float MountainHeight = 0.5f;

    [Range(0f, 1f), Tooltip("How many mountains appear")]
    public float MountainDensity = 0.3f;

    [Range(0f, 1f), Tooltip("Surface roughness / detail level")]
    public float TerrainRoughness = 0.5f;

    [Header("Water")]
    public bool HasOceans = true;
    [Range(-0.05f, 0.05f)] public float OceanLevel = 0f;
    public Color WaterColor = new Color(0.07f, 0.35f, 0.63f, 0.7f);

    [Header("Biomes")]
    public BiomeSettings BiomeSettings;
    public Material PlanetMaterial;

    public ShapeSettings BuildShapeSettings()
    {
        var shape = CreateInstance<ShapeSettings>();
        shape.PlanetRadius = PlanetRadius;
        shape.NoiseLayers = BuildNoiseLayers();
        return shape;
    }

    public ColorSettings BuildColorSettings()
    {
        var color = CreateInstance<ColorSettings>();
        color.PlanetMaterial = PlanetMaterial;
        color.BiomeSettings = BiomeSettings;
        color.OceanColorGradient = new Gradient();
        return color;
    }

    ShapeSettings.NoiseLayer[] BuildNoiseLayers()
    {
        // Layer 0: Continental shelf — large-scale land vs ocean
        var continent = new ShapeSettings.NoiseLayer
        {
            Enabled = true,
            UseFirstLayerAsMask = false,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = Mathf.Lerp(0.03f, 0.12f, OceanDepth),
                Layers = 4,
                BaseRoughness = Mathf.Lerp(0.5f, 1.5f, 1f - ContinentSize),
                Roughness = 2.2f,
                Persistence = 0.5f,
                Center = Vector3.zero,
                MinValue = Mathf.Lerp(0.85f, 1.0f, 1f - OceanDepth)
            }
        };

        // Layer 1: Mountains — rigid noise masked by continents
        var mountains = new ShapeSettings.NoiseLayer
        {
            Enabled = MountainHeight > 0.05f,
            UseFirstLayerAsMask = true,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Rigid,
                Strength = Mathf.Lerp(0.2f, 1.5f, MountainHeight),
                Layers = 4,
                BaseRoughness = Mathf.Lerp(1.5f, 4f, MountainDensity),
                Roughness = 3f,
                Persistence = 0.5f,
                Center = new Vector3(0, 0, 4.61f),
                MinValue = 0f
            }
        };

        // Layer 2: Surface detail — small bumps and roughness
        var detail = new ShapeSettings.NoiseLayer
        {
            Enabled = TerrainRoughness > 0.05f,
            UseFirstLayerAsMask = true,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = Mathf.Lerp(0.01f, 0.08f, TerrainRoughness),
                Layers = Mathf.Clamp((int)(TerrainRoughness * 6) + 1, 1, 6),
                BaseRoughness = Mathf.Lerp(2f, 6f, TerrainRoughness),
                Roughness = 2.5f,
                Persistence = 0.5f,
                Center = new Vector3(100, 0, 0),
                MinValue = 0f
            }
        };

        return new[] { continent, mountains, detail };
    }
}
