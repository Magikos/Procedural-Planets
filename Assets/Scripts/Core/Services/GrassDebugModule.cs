using System.Text;
using UnityEngine;

public static class GrassDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("grass");
    public static readonly DebugCaptureSetId Surface = new DebugCaptureSetId(Module, "surface");
}

public sealed class GrassDebugModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => GrassDebugIds.Module;
    static readonly int OceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int AtmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int SeaLevelRadiusId = Shader.PropertyToID("_SeaLevelRadius");
    static readonly int DensityOriginRadiusId = Shader.PropertyToID("_DensityOriginRadius");
    static readonly int ViewStepsId = Shader.PropertyToID("_ViewSteps");
    static readonly int SunStepsId = Shader.PropertyToID("_SunSteps");
    static readonly int WaterVolumeEnabledId = Shader.PropertyToID("_WaterVolumeEnabled");

    public void Register(DebugRegistry registry)
    {
        registry.RegisterDefaultCaptureSet(GrassDebugIds.Surface, "Grass",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.AtmosphereBypass),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            WaterDebugIds.Mode(DebugModeConstants.BiomeMapPrimaryId),
            WaterDebugIds.Mode(DebugModeConstants.BiomeMapBlend),
            WaterDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
            WaterDebugIds.Mode(DebugModeConstants.TerrainFaceId));
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        sb.AppendLine("--- Grass ---");
        if (!ServiceLocator.TryGet(out IGrassDebugStatsProvider provider))
        {
            sb.AppendLine("Controller: missing");
            return;
        }

        GrassDebugStats stats = provider.GetGrassDebugStats();
        sb.AppendLine($"Controller: active={stats.ControllerActive}, shader={stats.ShaderAvailable}, smoke={stats.SmokeRenderer}");
        sb.AppendLine($"Chunks: visible={stats.VisibleChunks}, tracked={stats.TrackedChunks}, maxDepth={stats.MaxChunkDepth}, minBladeDepth={stats.MinChunkDepthForBlades}, coarseOffset={stats.MaxCoarseLodOffsetForBlades}");
        sb.AppendLine($"Quality: maxBladesPerLane={stats.MaxBladesPerLane}, densityMultiplier={stats.DensityMultiplier:F2}, maxDistance={stats.MaxRenderDistance:F1}, fadeStart={stats.DistanceFadeStart:F1}, distanceJitter={stats.CullDistanceJitter01:F2}");
        sb.AppendLine($"Draw: calls={stats.DrawCalls}, chunksWithInstances={stats.ChunksWithInstances}, instances={stats.BladeInstances}, buffer={stats.BufferMegabytes:F3} MB");
        sb.AppendLine($"Dispatch: placement={stats.PlacementDispatches}, chunksWithStats={stats.ChunksWithStats}, chunkInstances={stats.ChunkInstanceMin}/{stats.ChunkInstanceAverage:F1}/{stats.ChunkInstanceMax} min/avg/max");
        sb.AppendLine($"CullLanes: candidates={stats.CandidateLanes}, visible={stats.VisibleLanes}, density={stats.DensityRejectedLanes}, shape={stats.ShapeRejectedLanes}, state={stats.StateRejectedLanes}, water={stats.WaterRejectedLanes}, slope={stats.SlopeRejectedLanes}, distance={stats.DistanceRejectedLanes}, distanceFade={stats.DistanceFadeRejectedLanes}, frustum={stats.FrustumRejectedLanes}");
        sb.AppendLine($"CullBlades: candidates={stats.CandidateBlades}, emitted={stats.EmittedBlades}, densityRoll={stats.DensityRejectedBlades}, slopeRoll={stats.SlopeRejectedBlades}, overflow={stats.OverflowRejectedBlades}");
        sb.AppendLine($"SurfaceAtlas: resolution={stats.SurfaceAtlasResolution}");
        AppendAtmosphereMetadata(sb);
        AppendScaleReferenceMetadata(sb);
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;
        if (!ServiceLocator.TryGet(out IGrassDebugStatsProvider provider))
            return;

        GrassDebugStats stats = provider.GetGrassDebugStats();
        GUILayout.Space(6);
        GUILayout.Label("Grass Debug");
        GUILayout.Label($"Chunks: visible={stats.VisibleChunks}, tracked={stats.TrackedChunks}");
        GUILayout.Label($"Draw: calls={stats.DrawCalls}, blades={stats.BladeInstances}, activeChunks={stats.ChunksWithInstances}");
        GUILayout.Label($"Lanes: visible={stats.VisibleLanes}/{stats.CandidateLanes}");
        GUILayout.Label($"Atlas: {stats.SurfaceAtlasResolution}");
    }

    static void AppendAtmosphereMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- Atmosphere ---");
        sb.AppendLine($"Globals: oceanDebug={Shader.GetGlobalInt(OceanDebugModeId)}, radius={Shader.GetGlobalFloat(AtmosphereRadiusId):F2}, sea={Shader.GetGlobalFloat(SeaLevelRadiusId):F2}, densityOrigin={Shader.GetGlobalFloat(DensityOriginRadiusId):F2}");
        sb.AppendLine($"PassInputs: waterVolume={Shader.GetGlobalFloat(WaterVolumeEnabledId):F2}, viewSteps={Shader.GetGlobalInt(ViewStepsId)}, sunSteps={Shader.GetGlobalInt(SunStepsId)}");
    }

    static void AppendScaleReferenceMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- ScaleRef ---");
        if (!ServiceLocator.TryGet(out IScaleReferenceDebugStatsProvider provider))
        {
            sb.AppendLine("Markers: provider=missing");
            return;
        }

        ScaleReferenceDebugStats stats = provider.GetScaleReferenceDebugStats();
        sb.AppendLine($"Markers: hasDrop={stats.HasDrop}, lastSuccess={stats.LastDropSucceeded}, status={stats.LastTargetStatus ?? "none"}, count={stats.MarkerCount}");
        sb.AppendLine($"MarkerProjection: meshHits={stats.MarkerProjectionHits}, fallbacks={stats.MarkerProjectionFallbacks}");
        if (!stats.HasDrop && !stats.LastDropSucceeded)
            return;

        sb.AppendLine($"Target: anchor={stats.LastAnchor:F1}, up={stats.LastWorldUp:F3}, forward={stats.LastTangentForward:F3}");
        sb.AppendLine($"Ray: distance={stats.LastRayDistance:F2}m, cameraToAnchor={stats.LastCameraToAnchorDistance:F2}m, cameraRadius={stats.LastCameraDistance:F2}m");
        sb.AppendLine($"Surface: radius={stats.LastSurfaceRadius:F2}m, sea={stats.LastSeaLevelRadius:F2}m, altitude={stats.LastAltitudeAboveSurface:F2}m");
    }
}
