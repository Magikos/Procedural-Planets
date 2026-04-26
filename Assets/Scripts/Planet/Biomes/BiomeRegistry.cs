using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Biomes/Biome Registry")]
public class BiomeRegistry : ScriptableObject, IBiomeRegistry
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

    [Range(-0.1f, 0.1f)] public float OceanThreshold = 0f;
    [Range(0f, 0.1f)] public float BeachWidth = 0.003f;
    [Range(0f, 1f)] public float MountainThreshold = 0.08f;

    [Header("Blending")]
    [Range(0f, 0.1f)] public float BlendWidth = 0.005f;

    public int BiomeCount => GridEntries != null ? GridEntries.Length + 3 : 3;

    public BiomeResult Resolve(float temperature, float moisture, float elevation)
    {
        // Elevation overrides
        if (elevation < OceanThreshold)
            return new BiomeResult(BiomeType.Ocean, temperature, moisture);

        if (elevation < OceanThreshold + BeachWidth && elevation >= OceanThreshold)
            return new BiomeResult(BiomeType.Beach, temperature, moisture);

        if (elevation > MountainThreshold)
            return new BiomeResult(BiomeType.Mountain, temperature, moisture);

        // Grid lookup
        if (GridEntries == null || GridEntries.Length == 0)
            return new BiomeResult(BiomeType.Grassland, temperature, moisture);

        float tempCont = Mathf.Clamp01(temperature) * (TemperatureSteps - 1);
        float moistCont = Mathf.Clamp01(moisture) * (MoistureSteps - 1);

        int tempIdx = Mathf.FloorToInt(tempCont);
        int moistIdx = Mathf.FloorToInt(moistCont);
        float tempFrac = tempCont - tempIdx;
        float moistFrac = moistCont - moistIdx;

        tempIdx = Mathf.Clamp(tempIdx, 0, TemperatureSteps - 1);
        moistIdx = Mathf.Clamp(moistIdx, 0, MoistureSteps - 1);

        int primaryIdx = GetGridIndex(tempIdx, moistIdx);
        var primary = GetBiomeAt(primaryIdx);

        // Find neighbor for blending
        int neighborTempIdx = tempFrac > 0.5f ? Mathf.Min(tempIdx + 1, TemperatureSteps - 1) : Mathf.Max(tempIdx - 1, 0);
        int neighborMoistIdx = moistFrac > 0.5f ? Mathf.Min(moistIdx + 1, MoistureSteps - 1) : Mathf.Max(moistIdx - 1, 0);

        // Blend toward the axis with the larger fractional component
        float blendWeight = 0f;
        BiomeType secondary = primary;

        float tempDist = Mathf.Abs(tempFrac - 0.5f);
        float moistDist = Mathf.Abs(moistFrac - 0.5f);

        if (BlendWidth > 0f)
        {
            int secIdx;
            float edgeDist;

            if (tempDist < moistDist)
            {
                secIdx = GetGridIndex(tempIdx, neighborMoistIdx);
                edgeDist = moistFrac > 0.5f ? 1f - moistFrac : moistFrac;
            }
            else
            {
                secIdx = GetGridIndex(neighborTempIdx, moistIdx);
                edgeDist = tempFrac > 0.5f ? 1f - tempFrac : tempFrac;
            }

            secondary = GetBiomeAt(secIdx);
            blendWeight = 1f - Mathf.Clamp01(edgeDist / BlendWidth);
        }

        return new BiomeResult
        {
            PrimaryBiome = primary,
            SecondaryBiome = secondary,
            BlendWeight = blendWeight,
            Temperature = temperature,
            Moisture = moisture
        };
    }

    int GetGridIndex(int tempIdx, int moistIdx)
    {
        return Mathf.Clamp(tempIdx * MoistureSteps + moistIdx, 0, GridEntries.Length - 1);
    }

    BiomeType GetBiomeAt(int gridIndex)
    {
        if (gridIndex < 0 || gridIndex >= GridEntries.Length || GridEntries[gridIndex] == null)
            return BiomeType.Grassland;
        return GridEntries[gridIndex].Type;
    }

    public BiomeDefinition GetDefinition(BiomeType type)
    {
        if (OceanBiome != null && OceanBiome.Type == type) return OceanBiome;
        if (BeachBiome != null && BeachBiome.Type == type) return BeachBiome;
        if (MountainBiome != null && MountainBiome.Type == type) return MountainBiome;

        if (GridEntries != null)
        {
            foreach (var entry in GridEntries)
            {
                if (entry != null && entry.Type == type) return entry;
            }
        }
        return null;
    }

    public BiomeDefinition GetDefinitionByIndex(int index)
    {
        if (index == 0 && OceanBiome != null) return OceanBiome;
        if (index == 1 && BeachBiome != null) return BeachBiome;
        if (index == 2 && MountainBiome != null) return MountainBiome;

        int gridIdx = index - 3;
        if (GridEntries != null && gridIdx >= 0 && gridIdx < GridEntries.Length)
            return GridEntries[gridIdx];
        return null;
    }
}
