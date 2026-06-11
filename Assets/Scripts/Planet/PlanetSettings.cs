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

    public bool EnableSurfaceOverrides = true;

    [Header("Water")]
    public bool HasOceans = true;
    [Range(-0.05f, 0.05f)] public float OceanLevel = 0f;
    public Color WaterColor = new Color(0.07f, 0.35f, 0.63f, 0.7f);
    public bool EnableFrozenWater = true;
    public Color IceTint = new Color(0.62f, 0.82f, 0.88f, 1f);

    [Header("Biomes")]
    public BiomeSettings BiomeSettings;
    public Material PlanetMaterial;
}
