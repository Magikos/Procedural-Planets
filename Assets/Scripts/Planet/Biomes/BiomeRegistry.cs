using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Biomes/Biome Registry")]
public class BiomeRegistry : ScriptableObject
{
    [Header("Grid Layout (rows = temperature cold→hot, cols = moisture dry→wet)")]
    [Range(1, 8)] public int TemperatureSteps = 4;
    [Range(1, 8)] public int MoistureSteps = 3;

    [Tooltip("Row-major: index = tempStep * MoistureSteps + moistStep")]
    public BiomeDefinition[] GridEntries;

    [Header("Elevation Overrides")]
    public BiomeDefinition OceanBiome;
    public BiomeDefinition BeachBiome;
    public BiomeDefinition MountainBiome;
    public BiomeDefinition SnowyMountainBiome;

    public int BiomeCount => (GridEntries != null ? GridEntries.Length : 0) + 4;

    public BiomeDefinition GetDefinitionByIndex(int index)
    {
        if (index == 0) return OceanBiome;
        if (index == 1) return BeachBiome;

        int gridCount = GridEntries != null ? GridEntries.Length : 0;
        int gridIdx = index - 2;
        if (gridIdx >= 0 && gridIdx < gridCount)
            return GridEntries[gridIdx];

        if (index == gridCount + 2) return MountainBiome;
        if (index == gridCount + 3) return SnowyMountainBiome;
        return null;
    }
}
