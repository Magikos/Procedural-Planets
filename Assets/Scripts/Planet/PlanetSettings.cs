using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Planet Settings")]
public class PlanetSettings : ScriptableObject
{
    [Header("General")]
    [Range(1, 5000)] public float PlanetRadius = 50f;

    [Tooltip("Surface generator. Low = one mesh per cube face. High = CPU chunked quadtree. Defaults to Low for safety.")]
    public PlanetResolution Resolution = PlanetResolution.Low;

    [Range(0, 7), Tooltip("Chunked-mode only: max LOD depth pre-cached at load time. " +
             "Higher = finer detail but quadratically more memory (depth 4 approx. 300 MB, depth 5 approx. 1.2 GB). " +
             "Default 4 is a good balance for explorable planets; use 2-3 for distant decorative bodies.")]
    public int MaxChunkDepth = 4;

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
                Strength = Mathf.Lerp(0.1f, 0.6f, MountainHeight),
                Layers = 4,
                BaseRoughness = Mathf.Lerp(1.8f, 3.5f, MountainDensity),
                Roughness = 3f,
                Persistence = 0.5f,
                Center = new Vector3(0, 0, 4.61f),
                MinValue = 0f
            }
        };

        var detail = new ShapeSettings.NoiseLayer
        {
            Enabled = TerrainRoughness > 0.05f,
            UseFirstLayerAsMask = true,
            NoiseSettings = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = Mathf.Lerp(0.005f, 0.03f, TerrainRoughness),
                Layers = Mathf.Clamp((int)(TerrainRoughness * 4) + 1, 1, 5),
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
