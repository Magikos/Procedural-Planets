using Unity.Collections;
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
    public BiomeDefinition SnowyMountainBiome;

    [Range(-0.1f, 0.1f)] public float OceanThreshold = 0f;
    [Range(0f, 0.1f)] public float BeachWidth = 0.003f;
    [Range(0f, 1f)] public float MountainThreshold = 0.08f;

    [Header("Blending")]
    [Tooltip("Biome transition band as a fraction of one temperature/moisture grid cell. 0.5 blends to the cell midpoint.")]
    [Range(0f, 0.5f)] public float BlendWidth = 0.18f;
    [Tooltip("Elevation-space transition band around ocean/beach and mountain thresholds.")]
    [Range(0f, 0.02f)] public float ElevationBlendWidth = 0.001f;

    public int BiomeCount => (GridEntries != null ? GridEntries.Length : 0) + 4;

    public BiomeResult Resolve(float temperature, float moisture, float elevation)
    {
        BiomeResult gridResult = ResolveGrid(temperature, moisture);
        return ResolveElevationOverrides(gridResult, temperature, moisture, elevation);
    }

    public BiomeResult ResolveWithLandBiomes(
        BiomeType primary,
        BiomeType secondary,
        float blendWeight,
        float temperature,
        float moisture,
        float elevation)
    {
        var landResult = new BiomeResult(primary, temperature, moisture)
        {
            SecondaryBiome = secondary,
            BlendWeight = secondary != primary ? Mathf.Clamp01(blendWeight) : 0f,
        };
        return ResolveElevationOverrides(landResult, temperature, moisture, elevation);
    }

    BiomeResult ResolveElevationOverrides(
        BiomeResult gridResult,
        float temperature,
        float moisture,
        float elevation)
    {
        float beachWidth = Mathf.Max(BeachWidth, 0f);
        float beachTop = OceanThreshold + beachWidth;
        float elevationBlendWidth = Mathf.Max(ElevationBlendWidth, 0f);
        float beachInnerBlendWidth = beachWidth > 0f
            ? Mathf.Min(elevationBlendWidth, beachWidth * 0.5f)
            : 0f;

        if (elevation < OceanThreshold)
        {
            float blend = BoundaryBlendWeight(OceanThreshold - elevation, elevationBlendWidth);
            return NewBlendedResult(BiomeType.Ocean, BiomeType.Beach, blend, temperature, moisture);
        }

        if (beachWidth > 0f && elevation < beachTop)
        {
            float distanceToOcean = elevation - OceanThreshold;
            float distanceToLand = beachTop - elevation;
            if (distanceToOcean <= distanceToLand)
            {
                float blend = BoundaryBlendWeight(distanceToOcean, beachInnerBlendWidth);
                return NewBlendedResult(BiomeType.Beach, BiomeType.Ocean, blend, temperature, moisture);
            }

            float landBlend = BoundaryBlendWeight(distanceToLand, beachInnerBlendWidth);
            return NewBlendedResult(BiomeType.Beach, gridResult.PrimaryBiome, landBlend, temperature, moisture);
        }

        if (elevation > MountainThreshold)
        {
            BiomeType mountainBiome = temperature < 0.4f ? BiomeType.Snow : BiomeType.Mountain;
            float blend = BoundaryBlendWeight(elevation - MountainThreshold, elevationBlendWidth);
            return NewBlendedResult(mountainBiome, gridResult.PrimaryBiome, blend, temperature, moisture);
        }

        float shoreBlend = BoundaryBlendWeight(elevation - beachTop, elevationBlendWidth);
        gridResult = ApplyBoundaryBlend(gridResult, BiomeType.Beach, shoreBlend);

        BiomeType nearMountainBiome = temperature < 0.4f ? BiomeType.Snow : BiomeType.Mountain;
        float mountainBlend = BoundaryBlendWeight(MountainThreshold - elevation, elevationBlendWidth);
        gridResult = ApplyBoundaryBlend(gridResult, nearMountainBiome, mountainBlend);

        return gridResult;
    }

    BiomeResult ResolveGrid(float temperature, float moisture)
    {
        if (GridEntries == null || GridEntries.Length == 0)
            return new BiomeResult(BiomeType.Grassland, temperature, moisture);

        GridCoordinate(temperature, TemperatureSteps, out int tempIdx, out float tempFrac);
        GridCoordinate(moisture, MoistureSteps, out int moistIdx, out float moistFrac);

        int primaryIdx = GetGridIndex(tempIdx, moistIdx);
        var primary = GetBiomeAt(primaryIdx);

        // Find neighbor for blending
        int neighborTempIdx = tempFrac > 0.5f ? Mathf.Min(tempIdx + 1, TemperatureSteps - 1) : Mathf.Max(tempIdx - 1, 0);
        int neighborMoistIdx = moistFrac > 0.5f ? Mathf.Min(moistIdx + 1, MoistureSteps - 1) : Mathf.Max(moistIdx - 1, 0);

        // Blend toward the closest cell edge so transitions follow the grid boundary.
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
            if (secondary != primary)
            {
                // Both sides of a cell boundary must meet at the same color. A 50/50 max
                // blend makes the current cell and its neighbor produce identical output
                // at the boundary instead of swapping fully to the opposite biome.
                blendWeight = 0.5f * (1f - Mathf.Clamp01(edgeDist / BlendWidth));
            }
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

    static BiomeResult NewBlendedResult(BiomeType primary, BiomeType secondary, float blendWeight, float temperature, float moisture)
    {
        var result = new BiomeResult(primary, temperature, moisture);
        if (secondary != primary && blendWeight > 0f)
        {
            result.SecondaryBiome = secondary;
            result.BlendWeight = Mathf.Clamp01(blendWeight);
        }
        return result;
    }

    static BiomeResult ApplyBoundaryBlend(BiomeResult result, BiomeType secondary, float blendWeight)
    {
        if (secondary == result.PrimaryBiome || blendWeight <= result.BlendWeight)
            return result;

        result.SecondaryBiome = secondary;
        result.BlendWeight = Mathf.Clamp01(blendWeight);
        return result;
    }

    static float BoundaryBlendWeight(float distanceFromBoundary, float width)
    {
        if (width <= 0f) return 0f;
        return 0.5f * (1f - Mathf.Clamp01(distanceFromBoundary / width));
    }

    int GetGridIndex(int tempIdx, int moistIdx)
    {
        return Mathf.Clamp(tempIdx * MoistureSteps + moistIdx, 0, GridEntries.Length - 1);
    }

    static void GridCoordinate(float value, int steps, out int index, out float frac)
    {
        steps = Mathf.Max(steps, 1);
        float scaled = Mathf.Clamp01(value) * steps;
        index = Mathf.FloorToInt(scaled);
        if (index >= steps)
        {
            index = steps - 1;
            frac = 1f;
        }
        else
        {
            frac = scaled - index;
        }
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
        if (SnowyMountainBiome != null && SnowyMountainBiome.Type == type) return SnowyMountainBiome;

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
        // Layout: Ocean(0), Beach(1), Grid(2..N+1), Mountain(N+2), SnowyMountain(N+3)
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

    // Slice id (== GetDefinitionByIndex slot) for a BiomeType. Searches grid first, then
    // elevation overrides. Returns 0 (Ocean slot) if not found — caller is responsible for
    // ensuring the registry actually contains the requested type.
    public byte GetSliceIdForBiomeType(BiomeType type)
    {
        int gridCount = GridEntries != null ? GridEntries.Length : 0;
        if (GridEntries != null)
        {
            for (int i = 0; i < gridCount; i++)
            {
                if (GridEntries[i] != null && GridEntries[i].Type == type)
                    return (byte)(i + 2);
            }
        }
        if (OceanBiome != null && OceanBiome.Type == type) return 0;
        if (BeachBiome != null && BeachBiome.Type == type) return 1;
        if (MountainBiome != null && MountainBiome.Type == type) return (byte)(gridCount + 2);
        if (SnowyMountainBiome != null && SnowyMountainBiome.Type == type) return (byte)(gridCount + 3);
        return 0;
    }

    // Burst-ready snapshot used by chunk biome map bake jobs. Caller disposes via
    // BiomeLookupData.Dispose(). See BiomeLookupEvaluator for the matching resolver.
    public BiomeLookupData BuildLookupData(Allocator allocator)
    {
        int gridCount = GridEntries != null ? GridEntries.Length : 0;
        int cells = TemperatureSteps * MoistureSteps;
        var gridIds = new NativeArray<byte>(cells, allocator, NativeArrayOptions.UninitializedMemory);

        // Mirrors GetBiomeAt: a missing/out-of-range/null grid entry resolves to Grassland.
        // Pre-resolve Grassland's slice id once so the cell loop stays a pure assignment.
        byte grasslandFallbackId = GetSliceIdForBiomeType(BiomeType.Grassland);

        for (int i = 0; i < cells; i++)
        {
            BiomeDefinition def = i < gridCount ? GridEntries[i] : null;
            gridIds[i] = def != null ? (byte)(i + 2) : grasslandFallbackId;
        }

        return new BiomeLookupData
        {
            TemperatureSteps = TemperatureSteps,
            MoistureSteps = MoistureSteps,
            OceanThreshold = OceanThreshold,
            BeachWidth = BeachWidth,
            MountainThreshold = MountainThreshold,
            BlendWidth = BlendWidth,
            ElevationBlendWidth = ElevationBlendWidth,
            OceanBiomeId = 0,
            BeachBiomeId = 1,
            MountainBiomeId = (byte)(gridCount + 2),
            SnowyMountainBiomeId = (byte)(gridCount + 3),
            GridBiomeIds = gridIds,
            BiomeCount = BiomeCount,
        };
    }
}
