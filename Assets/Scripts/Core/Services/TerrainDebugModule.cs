using System.Text;
using UnityEngine;

public static class TerrainDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("terrain");
    public static readonly DebugCaptureSetId TerrainGeography = new DebugCaptureSetId(Module, "terrain-geography");
    public static readonly DebugCaptureSetId TerrainTextures = new DebugCaptureSetId(Module, "terrain-textures");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class TerrainDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider
{
    public DebugModuleId Id => TerrainDebugIds.Module;
    public DebugModuleId ModuleId => TerrainDebugIds.Module;

    static readonly int _terrainOverrideEnabledId = Shader.PropertyToID("_TerrainOverrideEnabled");
    static readonly int _coastSliceId = Shader.PropertyToID("_CoastSlice");
    static readonly int _coastBelowSeaDepthId = Shader.PropertyToID("_CoastBelowSeaDepth");
    static readonly int _coastStartHeightId = Shader.PropertyToID("_CoastStartHeight");
    static readonly int _coastEndHeightId = Shader.PropertyToID("_CoastEndHeight");
    static readonly int _coastTilingId = Shader.PropertyToID("_CoastTiling");
    static readonly int _slopeSliceId = Shader.PropertyToID("_SlopeSlice");
    static readonly int _slopeStartDegreesId = Shader.PropertyToID("_SlopeStartDegrees");
    static readonly int _slopeFullDegreesId = Shader.PropertyToID("_SlopeFullDegrees");
    static readonly int _slopeTilingId = Shader.PropertyToID("_SlopeTiling");
    static readonly int _snowSliceId = Shader.PropertyToID("_SnowSlice");
    static readonly int _snowFullTemperatureId = Shader.PropertyToID("_SnowFullTemperature");
    static readonly int _snowFadeEndTemperatureId = Shader.PropertyToID("_SnowFadeEndTemperature");
    static readonly int _snowTilingId = Shader.PropertyToID("_SnowTiling");

    public void Register(DebugRegistry registry)
    {
        RegisterModes(registry);
        RegisterCaptureSets(registry);
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        OceanDebugModeWriter.Set(TerrainDebugIds.Module, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        OceanDebugModeWriter.Clear(TerrainDebugIds.Module);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("--- Terrain Geography ---");
        sb.AppendLine($"Enabled: {Shader.GetGlobalFloat(_terrainOverrideEnabledId) > 0.5f}");
        sb.AppendLine(
            $"Coast: slice={Shader.GetGlobalInt(_coastSliceId)}, " +
            $"belowSea={Shader.GetGlobalFloat(_coastBelowSeaDepthId):F2}m, " +
            $"height={Shader.GetGlobalFloat(_coastStartHeightId):F2}-{Shader.GetGlobalFloat(_coastEndHeightId):F2}m, " +
            $"tiling={Shader.GetGlobalFloat(_coastTilingId):F3}");
        sb.AppendLine(
            $"Slope: slice={Shader.GetGlobalInt(_slopeSliceId)}, " +
            $"degrees={Shader.GetGlobalFloat(_slopeStartDegreesId):F1}-{Shader.GetGlobalFloat(_slopeFullDegreesId):F1}, " +
            $"tiling={Shader.GetGlobalFloat(_slopeTilingId):F3}");
        sb.AppendLine(
            $"Snow: slice={Shader.GetGlobalInt(_snowSliceId)}, " +
            $"temperature={Shader.GetGlobalFloat(_snowFullTemperatureId):F3}-{Shader.GetGlobalFloat(_snowFadeEndTemperatureId):F3}, " +
            $"tiling={Shader.GetGlobalFloat(_snowTilingId):F3}");
    }

    static void RegisterModes(DebugRegistry registry)
    {
        RegisterMode(registry, DebugModeConstants.TerrainCoastMask, "TerrainCoastMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainSlopeMask, "TerrainSlopeMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainSnowMask, "TerrainSnowMask", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainOverrideComposite, "TerrainOverrideComposite", "Terrain Geography");
        RegisterMode(registry, DebugModeConstants.TerrainPrimaryAlbedo, "TerrainPrimaryAlbedo", "Terrain Textures");
        RegisterMode(registry, DebugModeConstants.TerrainMixedAlbedo, "TerrainMixedAlbedo", "Terrain Textures");
    }

    static void RegisterCaptureSets(DebugRegistry registry)
    {
        registry.RegisterCaptureSet(TerrainDebugIds.TerrainGeography, "Terrain Geography",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeTemperature),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeElevationBand),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainCoastMask),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainSlopeMask),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainSnowMask),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainOverrideComposite),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSelectedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceRoughness));
        registry.RegisterCaptureSet(TerrainDebugIds.TerrainTextures, "Terrain Textures",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.AtmosphereBypass),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapBlend),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainPrimaryAlbedo),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainMixedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSelectedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceAo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceRoughness),
            WaterDebugIds.Mode(DebugModeConstants.TerrainFaceId));
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(TerrainDebugIds.Mode(localId), name, category);
    }
}
