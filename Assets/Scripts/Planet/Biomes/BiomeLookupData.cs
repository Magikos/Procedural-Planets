using Unity.Burst;
using Unity.Collections;

// Blittable mirror of BiomeRegistryDto suitable for Burst jobs. Built once per planet gen via
// BiomeRegistryDto.BuildLookupData(); disposed by the caller when the bake pass finishes.
//
// Biome ids are GetDefinitionByIndex slot ids, NOT BiomeType enum values. Layout:
//   0                          = Ocean
//   1                          = Beach
//   2..(2+gridCount-1)         = grid entries (row-major, tempIdx * MoistureSteps + moistIdx)
//   2+gridCount                = Mountain
//   2+gridCount+1              = SnowyMountain
//
// The Texture2DArray slice index matches this scheme — keeps the bake → shader path 1:1.
public struct BiomeLookupData
{
    public int TemperatureSteps;
    public int MoistureSteps;

    public byte OceanBiomeId;
    public byte BeachBiomeId;
    public byte MountainBiomeId;
    public byte SnowyMountainBiomeId;
    public byte LakeBiomeId;
    public byte LakeShoreBiomeId;

    // Length = TemperatureSteps * MoistureSteps. Each entry is the slice id of the biome at
    // that grid cell. Unused cells (no GridEntries[i] in registry) fall back to the grid base
    // id (2) so sampling is always in-range.
    public NativeArray<byte> GridBiomeIds;

    public int BiomeCount;

    public void Dispose()
    {
        if (GridBiomeIds.IsCreated) GridBiomeIds.Dispose();
    }
}

// Burst-compatible resolver. Reproduces BiomeRegistryDto.Resolve() exactly, but returns slice
// ids (for Texture2DArray sampling) instead of BiomeType enum values, plus a secondary slice
// id used by Phase B step 5b's primary+secondary fast path (now superseded by the top-K
// placement.
[BurstCompile]
public static class BiomeLookupEvaluator
{
    public static void Resolve(in BiomeLookupData lookup,
        float temperature, float moisture, float elevation,
        out byte primaryId, out byte secondaryId, out float blendWeight)
    {
        ResolveGrid(lookup, temperature, moisture, out byte gridPrimaryId, out byte gridSecondaryId, out float gridBlendWeight);
        ResolveFromLandBiomes(
            lookup,
            temperature,
            elevation,
            gridPrimaryId,
            gridSecondaryId,
            gridBlendWeight,
            0,
            BiomeConstants.OceanThreshold,
            out primaryId,
            out secondaryId,
            out blendWeight);
    }

    public static void ResolveFromLandBiomes(
        in BiomeLookupData lookup,
        float temperature,
        float elevation,
        byte landPrimaryId,
        byte landSecondaryId,
        float landBlendWeight,
        byte lakeState,
        float waterLevel,
        out byte primaryId,
        out byte secondaryId,
        out float blendWeight)
    {
        float beachWidth = BiomeConstants.BeachWidth;
        float beachTop = BiomeConstants.OceanThreshold + beachWidth;
        float elevationBlend = BiomeConstants.ElevationBlendWidth;
        float beachInnerBlend = beachWidth > 0f
            ? Min(elevationBlend, beachWidth * 0.5f)
            : 0f;

        // Lake override (WaterBodyMap): a small below-water body -> Lake (blends to LakeShore at the edge like
        // Ocean blends to Beach); its dry shore ring -> LakeShore (blends into the surrounding land). Gated
        // on the elevation side so a mask-resolution mismatch can't put lake water on dry land or vice versa.
        //
        // The gate is against THIS body's surface, not the global ocean level. A lake perched 40 m up has a
        // bed at +20 m, which against the global level reads as ordinary highland - so the override never
        // fired, the lake floor came out Grassland, and grass grew on it under the water.
        // The mask is a 38 m grid but elevation is sampled at mesh resolution, so the true waterline sits
        // INSIDE a mask cell. Deciding by elevation within any lake-adjacent cell puts the boundary on the
        // waterline itself rather than on the grid: dry ground inside a Water cell becomes shore, which is
        // the metre-scale band reeds need, and submerged ground inside a Shore cell becomes lake.
        if (lakeState != 0 && elevation < waterLevel)
        {
            SetBlendedResult(lookup.LakeBiomeId, lookup.LakeShoreBiomeId,
                BoundaryBlendWeight(waterLevel - elevation, elevationBlend),
                out primaryId, out secondaryId, out blendWeight);
            return;
        }
        if (lakeState != 0)
        {
            // Ramp on height above THIS lake's surface. It was a constant 0.35, so the whole shore ring was
            // one flat 35 % mix that jumped to pure land at the ring's outer edge - and that edge is the 38 m
            // mask grid, which is why the shore read as a hard stair-stepped band rather than a beach.
            SetBlendedResult(lookup.LakeShoreBiomeId, landPrimaryId,
                LakeShoreHandoff(elevation, waterLevel),
                out primaryId, out secondaryId, out blendWeight);
            return;
        }

        if (elevation < BiomeConstants.OceanThreshold)
        {
            SetBlendedResult(lookup.OceanBiomeId, lookup.BeachBiomeId,
                BoundaryBlendWeight(BiomeConstants.OceanThreshold - elevation, elevationBlend),
                out primaryId, out secondaryId, out blendWeight);
            return;
        }

        if (beachWidth > 0f && elevation < beachTop)
        {
            float distanceToOcean = elevation - BiomeConstants.OceanThreshold;
            float distanceToLand = beachTop - elevation;
            if (distanceToOcean <= distanceToLand)
            {
                SetBlendedResult(lookup.BeachBiomeId, lookup.OceanBiomeId,
                    BoundaryBlendWeight(distanceToOcean, beachInnerBlend),
                    out primaryId, out secondaryId, out blendWeight);
            }
            else
            {
                SetBlendedResult(lookup.BeachBiomeId, landPrimaryId,
                    BoundaryBlendWeight(distanceToLand, beachInnerBlend),
                    out primaryId, out secondaryId, out blendWeight);
            }
            return;
        }

        if (elevation > BiomeConstants.MountainThreshold)
        {
            byte mountainId = temperature < 0.4f ? lookup.SnowyMountainBiomeId : lookup.MountainBiomeId;
            SetBlendedResult(mountainId, landPrimaryId,
                BoundaryBlendWeight(elevation - BiomeConstants.MountainThreshold, elevationBlend),
                out primaryId, out secondaryId, out blendWeight);
            return;
        }

        primaryId = landPrimaryId;
        secondaryId = landSecondaryId;
        blendWeight = landBlendWeight;

        float shoreBlend = BoundaryBlendWeight(elevation - beachTop, elevationBlend);
        ApplyBoundaryBlend(primaryId, lookup.BeachBiomeId, shoreBlend, ref secondaryId, ref blendWeight);

        byte nearMountainId = temperature < 0.4f ? lookup.SnowyMountainBiomeId : lookup.MountainBiomeId;
        float mountainBlend = BoundaryBlendWeight(BiomeConstants.MountainThreshold - elevation, elevationBlend);
        ApplyBoundaryBlend(primaryId, nearMountainId, mountainBlend, ref secondaryId, ref blendWeight);
    }

    static void SetBlendedResult(byte primary, byte secondary, float blend,
        out byte primaryId, out byte secondaryId, out float blendWeight)
    {
        primaryId = primary;
        if (secondary != primary && blend > 0f)
        {
            secondaryId = secondary;
            blendWeight = Clamp01(blend);
            return;
        }

        secondaryId = primary;
        blendWeight = 0f;
    }

    static void ResolveGrid(in BiomeLookupData lookup,
        float temperature, float moisture,
        out byte primaryId, out byte secondaryId, out float blendWeight)
    {
        float tempClamp = temperature < 0f ? 0f : (temperature > 1f ? 1f : temperature);
        float moistClamp = moisture < 0f ? 0f : (moisture > 1f ? 1f : moisture);

        GridCoordinate(tempClamp, lookup.TemperatureSteps, out int tempIdx, out float tempFrac);
        GridCoordinate(moistClamp, lookup.MoistureSteps, out int moistIdx, out float moistFrac);

        primaryId = lookup.GridBiomeIds[tempIdx * lookup.MoistureSteps + moistIdx];

        int neighborTempIdx = tempFrac > 0.5f
            ? (tempIdx + 1 < lookup.TemperatureSteps ? tempIdx + 1 : lookup.TemperatureSteps - 1)
            : (tempIdx > 0 ? tempIdx - 1 : 0);
        int neighborMoistIdx = moistFrac > 0.5f
            ? (moistIdx + 1 < lookup.MoistureSteps ? moistIdx + 1 : lookup.MoistureSteps - 1)
            : (moistIdx > 0 ? moistIdx - 1 : 0);

        secondaryId = primaryId;
        blendWeight = 0f;

        if (BiomeConstants.BlendWidth > 0f)
        {
            float tempDist = tempFrac - 0.5f;
            if (tempDist < 0f) tempDist = -tempDist;
            float moistDist = moistFrac - 0.5f;
            if (moistDist < 0f) moistDist = -moistDist;

            int secIdx;
            float edgeDist;
            if (tempDist < moistDist)
            {
                secIdx = tempIdx * lookup.MoistureSteps + neighborMoistIdx;
                edgeDist = moistFrac > 0.5f ? 1f - moistFrac : moistFrac;
            }
            else
            {
                secIdx = neighborTempIdx * lookup.MoistureSteps + moistIdx;
                edgeDist = tempFrac > 0.5f ? 1f - tempFrac : tempFrac;
            }

            secondaryId = lookup.GridBiomeIds[secIdx];

            if (secondaryId != primaryId)
            {
                // Match BiomeRegistryDto.Resolve(): the edge itself is a symmetric 50/50
                // blend, so adjacent grid cells meet continuously.
                float t = edgeDist / BiomeConstants.BlendWidth;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;
                blendWeight = 0.5f * (1f - t);
            }
        }
    }

    static void ApplyBoundaryBlend(byte primaryId, byte boundaryBiomeId, float boundaryBlendWeight, ref byte secondaryId, ref float blendWeight)
    {
        if (boundaryBiomeId == primaryId || boundaryBlendWeight <= blendWeight)
            return;

        secondaryId = boundaryBiomeId;
        blendWeight = Clamp01(boundaryBlendWeight);
    }

    static float BoundaryBlendWeight(float distanceFromBoundary, float width)
    {
        if (width <= 0f) return 0f;
        return 0.5f * (1f - Clamp01(distanceFromBoundary / width));
    }

    // Reaches a full 1, unlike BoundaryBlendWeight - see BiomeConstants.LakeShoreBlendHeight for why the
    // shore ring has to carry the whole ramp instead of meeting the land halfway. Smoothstep rather than a
    // straight line so neither end of the band shows a crease where the gradient changes.
    static float LakeShoreHandoff(float elevation, float waterLevel)
    {
        if (BiomeConstants.LakeShoreBlendHeight <= 0f) return 1f;
        float t = Clamp01((elevation - waterLevel) / BiomeConstants.LakeShoreBlendHeight);
        return t * t * (3f - 2f * t);
    }

    static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        return value > 1f ? 1f : value;
    }

    static float Min(float a, float b)
    {
        return a < b ? a : b;
    }

    static void GridCoordinate(float value, int steps, out int index, out float frac)
    {
        if (steps < 1) steps = 1;
        float scaled = value * steps;
        index = (int)scaled;
        if (index >= steps)
        {
            index = steps - 1;
            frac = 1f;
        }
        else if (index < 0)
        {
            index = 0;
            frac = 0f;
        }
        else
        {
            frac = scaled - index;
        }
    }
}
