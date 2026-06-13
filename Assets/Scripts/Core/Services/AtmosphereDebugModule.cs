using System.Text;
using UnityEngine;

public static class AtmosphereDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("atmosphere");
}

// Owns the atmosphere globals block in the F10 capture sidecar. Metadata-only — the atmosphere
// debug *modes* live on the water module's mode list; this contributes the shader-global readout.
public sealed class AtmosphereDebugModule : IDebugModule, IDebugCaptureMetadataProvider
{
    public DebugModuleId Id => AtmosphereDebugIds.Module;

    static readonly int OceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int AtmosphereRadiusId = Shader.PropertyToID(ShaderGlobalIds.AtmosphereRadius);
    static readonly int SeaLevelRadiusId = Shader.PropertyToID(ShaderGlobalIds.SeaLevelRadius);
    static readonly int DensityOriginRadiusId = Shader.PropertyToID(ShaderGlobalIds.DensityOriginRadius);
    static readonly int ViewStepsId = Shader.PropertyToID(ShaderGlobalIds.ViewSteps);
    static readonly int SunStepsId = Shader.PropertyToID(ShaderGlobalIds.SunSteps);
    static readonly int WaterVolumeEnabledId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeEnabled);
    static readonly int TerrainAerialPerspectiveDistancesId =
        Shader.PropertyToID("_TerrainAerialPerspectiveDistances");

    public void Register(DebugRegistry registry)
    {
        registry.RegisterMetadataProvider(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        sb.AppendLine("--- Atmosphere ---");
        sb.AppendLine($"Globals: oceanDebug={Shader.GetGlobalInt(OceanDebugModeId)}, radius={Shader.GetGlobalFloat(AtmosphereRadiusId):F2}, sea={Shader.GetGlobalFloat(SeaLevelRadiusId):F2}, densityOrigin={Shader.GetGlobalFloat(DensityOriginRadiusId):F2}");
        sb.AppendLine($"PassInputs: waterVolume={Shader.GetGlobalFloat(WaterVolumeEnabledId):F2}, viewSteps={Shader.GetGlobalInt(ViewStepsId)}, sunSteps={Shader.GetGlobalInt(SunStepsId)}");
        Vector4 terrainDistances = Shader.GetGlobalVector(TerrainAerialPerspectiveDistancesId);
        sb.AppendLine($"TerrainClarity: fullTo={terrainDistances.x:F1}m, atmosphereBy={terrainDistances.y:F1}m");
    }
}
