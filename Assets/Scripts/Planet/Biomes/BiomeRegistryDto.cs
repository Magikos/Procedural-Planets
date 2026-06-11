using Unity.Collections;
using UnityEngine;

public sealed record BiomeRegistryDto(
    int TemperatureSteps,
    int MoistureSteps,
    BiomeDefinitionDto[] GridEntries,
    BiomeDefinitionDto OceanBiome,
    BiomeDefinitionDto BeachBiome,
    BiomeDefinitionDto MountainBiome,
    BiomeDefinitionDto SnowyMountainBiome)
{
    public int BiomeCount => (GridEntries?.Length ?? 0) + 4;

    public static BiomeRegistryDto From(BiomeRegistry src)
    {
        if (src == null) return null;
        BiomeDefinitionDto[] grid = null;
        if (src.GridEntries != null)
        {
            grid = new BiomeDefinitionDto[src.GridEntries.Length];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = src.GridEntries[i] != null ? BiomeDefinitionDto.From(src.GridEntries[i]) : null;
        }
        return new BiomeRegistryDto(
            src.TemperatureSteps,
            src.MoistureSteps,
            grid,
            src.OceanBiome != null ? BiomeDefinitionDto.From(src.OceanBiome) : null,
            src.BeachBiome != null ? BiomeDefinitionDto.From(src.BeachBiome) : null,
            src.MountainBiome != null ? BiomeDefinitionDto.From(src.MountainBiome) : null,
            src.SnowyMountainBiome != null ? BiomeDefinitionDto.From(src.SnowyMountainBiome) : null);
    }

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

    public BiomeDefinitionDto GetDefinition(BiomeType type)
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

    public BiomeDefinitionDto GetDefinitionByIndex(int index)
    {
        if (index == 0) return OceanBiome;
        if (index == 1) return BeachBiome;

        int gridCount = GridEntries?.Length ?? 0;
        int gridIdx = index - 2;
        if (gridIdx >= 0 && gridIdx < gridCount)
            return GridEntries[gridIdx];

        if (index == gridCount + 2) return MountainBiome;
        if (index == gridCount + 3) return SnowyMountainBiome;
        return null;
    }

    public byte GetSliceIdForBiomeType(BiomeType type)
    {
        int gridCount = GridEntries?.Length ?? 0;
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

    public BiomeLookupData BuildLookupData(Allocator allocator)
    {
        int gridCount = GridEntries?.Length ?? 0;
        int cells = TemperatureSteps * MoistureSteps;
        var gridIds = new NativeArray<byte>(cells, allocator, NativeArrayOptions.UninitializedMemory);

        byte grasslandFallbackId = GetSliceIdForBiomeType(BiomeType.Grassland);

        for (int i = 0; i < cells; i++)
        {
            BiomeDefinitionDto def = i < gridCount ? GridEntries[i] : null;
            gridIds[i] = def != null ? (byte)(i + 2) : grasslandFallbackId;
        }

        return new BiomeLookupData
        {
            TemperatureSteps = TemperatureSteps,
            MoistureSteps = MoistureSteps,
            OceanBiomeId = 0,
            BeachBiomeId = 1,
            MountainBiomeId = (byte)(gridCount + 2),
            SnowyMountainBiomeId = (byte)(gridCount + 3),
            GridBiomeIds = gridIds,
            BiomeCount = BiomeCount,
        };
    }

    BiomeResult ResolveElevationOverrides(
        BiomeResult gridResult,
        float temperature,
        float moisture,
        float elevation)
    {
        float beachWidth = BiomeConstants.BeachWidth;
        float beachTop = BiomeConstants.OceanThreshold + beachWidth;
        float elevationBlend = BiomeConstants.ElevationBlendWidth;
        float beachInnerBlend = beachWidth > 0f
            ? Mathf.Min(elevationBlend, beachWidth * 0.5f)
            : 0f;

        if (elevation < BiomeConstants.OceanThreshold)
        {
            float blend = BoundaryBlendWeight(BiomeConstants.OceanThreshold - elevation, elevationBlend);
            return NewBlendedResult(BiomeType.Ocean, BiomeType.Beach, blend, temperature, moisture);
        }

        if (beachWidth > 0f && elevation < beachTop)
        {
            float distanceToOcean = elevation - BiomeConstants.OceanThreshold;
            float distanceToLand = beachTop - elevation;
            if (distanceToOcean <= distanceToLand)
            {
                float blend = BoundaryBlendWeight(distanceToOcean, beachInnerBlend);
                return NewBlendedResult(BiomeType.Beach, BiomeType.Ocean, blend, temperature, moisture);
            }

            float landBlend = BoundaryBlendWeight(distanceToLand, beachInnerBlend);
            return NewBlendedResult(BiomeType.Beach, gridResult.PrimaryBiome, landBlend, temperature, moisture);
        }

        if (elevation > BiomeConstants.MountainThreshold)
        {
            BiomeType mountainBiome = temperature < 0.4f ? BiomeType.Snow : BiomeType.Mountain;
            float blend = BoundaryBlendWeight(elevation - BiomeConstants.MountainThreshold, elevationBlend);
            return NewBlendedResult(mountainBiome, gridResult.PrimaryBiome, blend, temperature, moisture);
        }

        float shoreBlend = BoundaryBlendWeight(elevation - beachTop, elevationBlend);
        gridResult = ApplyBoundaryBlend(gridResult, BiomeType.Beach, shoreBlend);

        BiomeType nearMountainBiome = temperature < 0.4f ? BiomeType.Snow : BiomeType.Mountain;
        float mountainBlend = BoundaryBlendWeight(BiomeConstants.MountainThreshold - elevation, elevationBlend);
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

        int neighborTempIdx = tempFrac > 0.5f ? Mathf.Min(tempIdx + 1, TemperatureSteps - 1) : Mathf.Max(tempIdx - 1, 0);
        int neighborMoistIdx = moistFrac > 0.5f ? Mathf.Min(moistIdx + 1, MoistureSteps - 1) : Mathf.Max(moistIdx - 1, 0);

        float blendWeight = 0f;
        BiomeType secondary = primary;

        float tempDist = Mathf.Abs(tempFrac - 0.5f);
        float moistDist = Mathf.Abs(moistFrac - 0.5f);

        if (BiomeConstants.BlendWidth > 0f)
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
                blendWeight = 0.5f * (1f - Mathf.Clamp01(edgeDist / BiomeConstants.BlendWidth));
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
}
