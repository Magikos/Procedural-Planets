using System.Text;
using UnityEngine;

public static class BiomeDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("biome");
    public static readonly DebugCaptureSetId Biome = new DebugCaptureSetId(Module, "biome");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class BiomeDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider
{
    public DebugModuleId Id => BiomeDebugIds.Module;
    public DebugModuleId ModuleId => BiomeDebugIds.Module;

    static readonly int _biomeVoronoiSeedCountId = Shader.PropertyToID(ShaderGlobalIds.BiomeVoronoiSeedCount);
    static readonly int _biomeVoronoiCleanupChangesId = Shader.PropertyToID(ShaderGlobalIds.BiomeVoronoiCleanupChanges);
    static readonly int _biomeVoronoiDistinctBiomesId = Shader.PropertyToID(ShaderGlobalIds.BiomeVoronoiDistinctBiomes);
    static readonly int _biomeVoronoiBuildMsId = Shader.PropertyToID(ShaderGlobalIds.BiomeVoronoiBuildMs);
    static readonly int _biomeVoronoiAtlasResolutionId = Shader.PropertyToID(ShaderGlobalIds.BiomeVoronoiAtlasResolution);

    public void Register(DebugRegistry registry)
    {
        RegisterModes(registry);
        RegisterCaptureSets(registry);
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        OceanDebugModeWriter.Set(BiomeDebugIds.Module, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        OceanDebugModeWriter.Clear(BiomeDebugIds.Module);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        int atlasResolution = Shader.GetGlobalInt(_biomeVoronoiAtlasResolutionId);
        sb.AppendLine();
        sb.AppendLine("--- Biome Assignment ---");
        sb.AppendLine(
            $"Voronoi: seeds={Shader.GetGlobalInt(_biomeVoronoiSeedCountId)}, " +
            $"distinct={Shader.GetGlobalInt(_biomeVoronoiDistinctBiomesId)}, " +
            $"cleanupChanges={Shader.GetGlobalInt(_biomeVoronoiCleanupChangesId)}, " +
            $"atlas={atlasResolution}x{atlasResolution}x6, " +
            $"buildMs={Shader.GetGlobalInt(_biomeVoronoiBuildMsId)}");
    }

    static void RegisterModes(DebugRegistry registry)
    {
        RegisterMode(registry, DebugModeConstants.BiomePrimaryId, "BiomePrimaryId", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeTemperature, "BiomeTemperature", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMoisture, "BiomeMoisture", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeLatitude, "BiomeLatitude", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeElevationBand, "BiomeElevationBand", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeAltitudeCooling, "BiomeAltitudeCooling", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapPrimaryId, "BiomeMapPrimaryId", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapBlend, "BiomeMapBlend", "Biome");
        RegisterMode(registry, DebugModeConstants.BiomeMapFlatColor, "BiomeMapFlatColor", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSelectedAlbedo, "TerrainSelectedAlbedo", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSunLighting, "TerrainSunLighting", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceNormal, "TerrainSurfaceNormal", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceAo, "TerrainSurfaceAO", "Biome");
        RegisterMode(registry, DebugModeConstants.TerrainSurfaceRoughness, "TerrainSurfaceRoughness", "Biome");
        RegisterMode(registry, DebugModeConstants.GrassLodCoverage, "GrassLodCoverage", "Biome");
    }

    static void RegisterCaptureSets(DebugRegistry registry)
    {
        // Biome capture set bypasses atmosphere + clouds (SuppressesWeatherPasses +
        // Atmosphere.shader ShouldBypassAtmosphereForWaterDebug) so the planet surface is
        // never obscured by weather for these modes.
        registry.RegisterDefaultCaptureSet(BiomeDebugIds.Biome, "Biome",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.AtmosphereBypass),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            BiomeDebugIds.Mode(DebugModeConstants.BiomePrimaryId),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeTemperature),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMoisture),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeLatitude),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeElevationBand),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeAltitudeCooling),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapPrimaryId),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapBlend),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapFlatColor),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSelectedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSunLighting),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceAo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceRoughness),
            BiomeDebugIds.Mode(DebugModeConstants.GrassLodCoverage),
            WaterDebugIds.Mode(DebugModeConstants.TerrainFaceId));
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(BiomeDebugIds.Mode(localId), name, category);
    }
}
