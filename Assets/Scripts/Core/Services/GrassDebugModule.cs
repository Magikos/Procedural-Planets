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
    static readonly int TerrainAerialPerspectiveDistancesId =
        Shader.PropertyToID("_TerrainAerialPerspectiveDistances");

    public void Register(DebugRegistry registry)
    {
        registry.RegisterDefaultCaptureSet(GrassDebugIds.Surface, "Grass",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.AtmosphereBypass),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            WaterDebugIds.Mode(DebugModeConstants.BiomeMapPrimaryId),
            WaterDebugIds.Mode(DebugModeConstants.BiomeMapBlend),
            WaterDebugIds.Mode(DebugModeConstants.TerrainPrimaryAlbedo),
            WaterDebugIds.Mode(DebugModeConstants.TerrainMixedAlbedo),
            WaterDebugIds.Mode(DebugModeConstants.GrassLodCoverage),
            WaterDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
            WaterDebugIds.Mode(DebugModeConstants.TerrainFaceId));
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        AppendRuntimeStateMetadata(sb);
        sb.AppendLine("--- Grass ---");
        if (!ServiceLocator.TryGet(out IGrassDebugStatsProvider provider))
        {
            sb.AppendLine("Controller: missing");
        }
        else
        {
            GrassDebugStats stats = provider.GetGrassDebugStats();
            sb.AppendLine($"Controller: active={stats.ControllerActive}, shader={stats.ShaderAvailable}, smoke={stats.SmokeRenderer}");
            sb.AppendLine($"Chunks: visible={stats.VisibleChunks}, tracked={stats.TrackedChunks}, maxDepth={stats.MaxChunkDepth}, minBladeDepth={stats.MinChunkDepthForBlades}, coarseOffset={stats.MaxCoarseLodOffsetForBlades}");
            sb.AppendLine($"Quality: maxBladesPerLane={stats.MaxBladesPerLane}, visualBladesPerInstance={stats.VisualBladesPerInstance}, vertexCount={stats.BladeVertexCount}, densityMultiplier={stats.DensityMultiplier:F2}, maxDistance={stats.MaxRenderDistance:F1}, fadeStart={stats.DistanceFadeStart:F1}, distanceJitter={stats.CullDistanceJitter01:F2}");
            sb.AppendLine($"PlacementCull: frustumEnabled={stats.PlacementFrustumCullEnabled}");
            sb.AppendLine($"Draw: calls={stats.DrawCalls}, chunksWithInstances={stats.ChunksWithInstances}, instances={stats.BladeInstances}, visualBlades={(long)stats.BladeInstances * Mathf.Max(stats.VisualBladesPerInstance, 1)}, buffer={stats.BufferMegabytes:F3} MB");
            sb.AppendLine($"Dispatch: placement={stats.PlacementDispatches}, chunksWithStats={stats.ChunksWithStats}, chunkInstances={stats.ChunkInstanceMin}/{stats.ChunkInstanceAverage:F1}/{stats.ChunkInstanceMax} min/avg/max");
            sb.AppendLine($"CullLanes: candidates={stats.CandidateLanes}, visible={stats.VisibleLanes}, density={stats.DensityRejectedLanes}, shape={stats.ShapeRejectedLanes}, state={stats.StateRejectedLanes}, water={stats.WaterRejectedLanes}, slope={stats.SlopeRejectedLanes}, distance={stats.DistanceRejectedLanes}, distanceFade={stats.DistanceFadeRejectedLanes}, frustum={stats.FrustumRejectedLanes}");
            sb.AppendLine($"CullBlades: candidates={stats.CandidateBlades}, emitted={stats.EmittedBlades}, densityRoll={stats.DensityRejectedBlades}, innerFade={stats.InnerFadeRejectedBlades}, slopeRoll={stats.SlopeRejectedBlades}, overflow={stats.OverflowRejectedBlades}");
            sb.AppendLine($"Suppression: oldChunkSuppressed={stats.OldChunkSuppressedCount}/{stats.DrawCalls + stats.OldChunkSuppressedCount}");
            sb.AppendLine($"Interactors: registered={stats.RegisteredInteractors}, uploaded={stats.UploadedInteractors}, activeSources={stats.ActiveInteractorSources}, releaseGpu={stats.UploadedReleaseSamples}, releaseRetained={stats.RetainedReleaseSamples}");
            sb.AppendLine($"SurfaceAtlas: resolution={stats.SurfaceAtlasResolution}");
        }
        AppendNearFieldMetadata(sb);
        AppendMidFieldMetadata(sb);
        AppendAtmosphereMetadata(sb);
        AppendScaleReferenceMetadata(sb);
    }

    static void AppendRuntimeStateMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- GrassRuntime ---");
        if (!ServiceLocator.TryGet(out IGrassRuntimeControl control))
        {
            sb.AppendLine("Control: missing");
            return;
        }

        GrassRuntimeState state = control.GetGrassRuntimeState();
        sb.AppendLine($"Master: enabled={state.MasterEnabled}");
        sb.AppendLine($"Layers: near={state.NearFieldRequested}/{state.NearFieldActive} requested/active, mid={state.MidFieldRequested}/{state.MidFieldActive}, chunk={state.ChunkPathRequested}/{state.ChunkPathActive}, blanket={state.BlanketRequested}/{state.BlanketActive}");
    }

    static void AppendNearFieldMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- GrassNearField ---");
        if (!ServiceLocator.TryGet(out IGrassNearFieldStatsProvider provider))
        {
            sb.AppendLine("Controller: missing");
            return;
        }

        GrassNearFieldStats nf = provider.GetGrassNearFieldStats();
        sb.AppendLine($"Controller: active={nf.ControllerActive}, shader={nf.ShaderAvailable}");
        sb.AppendLine($"Quality: spacing={nf.Spacing:F2}, fullDensity={nf.FullDensityDistance:F1}, draw={nf.DrawDistance:F1}, fadeBand={nf.FadeBand:F1}, suppress={nf.SuppressionRadius:F1}");
        sb.AppendLine($"Page: cellSize={nf.PageCellSize}, face={nf.FaceIndex}, facesActive={nf.FacesActive}, multiFace={nf.MultiFaceDispatchEnabled}, originCellUV=({nf.PageOriginCellU},{nf.PageOriginCellV}), seamRisk={nf.SeamRisk}");
        sb.AppendLine($"Grid: {nf.GridWidth}x{nf.GridHeight}, reason={nf.LastDispatchReason}, dispatchedThisFrame={nf.DispatchedThisFrame}, dispatchesTotal={nf.DispatchesTotal}");
        sb.AppendLine($"Draw: emitted={nf.EmittedInstances}, visualBlades={(long)nf.EmittedInstances * 3}, capacity={nf.CapacityInstances}, buffer={nf.BufferMegabytes:F1} MB");
        sb.AppendLine($"Cull: candidates={nf.CandidateCells}, density={nf.DensityRejectedCells}, water={nf.WaterRejectedCells}, slope={nf.SlopeRejectedCells}, distance={nf.DistanceRejectedCells}, distanceFade={nf.DistanceFadeRejectedCells}, frustum={nf.FrustumRejectedCells}, faceArea={nf.FaceAreaRejectedCells}, rangeBudget={nf.RangeBudgetRejectedCells}, overflow={nf.OverflowDropped}");
    }

    static void AppendMidFieldMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- GrassMidField (deprecated experiment) ---");
        if (!ServiceLocator.TryGet(out IGrassMidFieldStatsProvider provider))
        {
            sb.AppendLine("Controller: missing");
            return;
        }

        GrassMidFieldStats mid = provider.GetGrassMidFieldStats();
        sb.AppendLine($"Controller: active={mid.ControllerActive}, shader={mid.ShaderAvailable}");
        sb.AppendLine($"Quality: spacing={mid.Spacing:F2}, fullDensity={mid.FullDensityDistance:F1}, draw={mid.DrawDistance:F1}, innerFade={mid.InnerFadeStart:F1}-{mid.InnerFadeEnd:F1}, outerFadeBand={mid.OuterFadeBand:F1}");
        sb.AppendLine($"Page: cellSize={mid.PageCellSize}, face={mid.FaceIndex}, facesActive={mid.FacesActive}, originCellUV=({mid.PageOriginCellU},{mid.PageOriginCellV}), seamRisk={mid.SeamRisk}");
        sb.AppendLine($"Grid: {mid.GridWidth}x{mid.GridHeight}, reason={mid.LastDispatchReason}, dispatchedThisFrame={mid.DispatchedThisFrame}, dispatchesTotal={mid.DispatchesTotal}");
        sb.AppendLine($"Draw: emitted={mid.EmittedInstances}, capacity={mid.CapacityInstances}, buffer={mid.BufferMegabytes:F1} MB");
        sb.AppendLine($"Cull: candidates={mid.CandidateCells}, density={mid.DensityRejectedCells}, water={mid.WaterRejectedCells}, slope={mid.SlopeRejectedCells}, distance={mid.DistanceRejectedCells}, distanceFade={mid.DistanceFadeRejectedCells}, faceArea={mid.FaceAreaRejectedCells}, overflow={mid.OverflowDropped}");
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        GUILayout.Space(6);
        GUILayout.Label("Grass Debug");
        if (ServiceLocator.TryGet(out IGrassRuntimeControl runtimeControl))
        {
            GrassRuntimeState runtime = runtimeControl.GetGrassRuntimeState();
            GUILayout.Label($"Layers: master={runtime.MasterEnabled}, near={runtime.NearFieldRequested}/{runtime.NearFieldActive}, mid={runtime.MidFieldRequested}/{runtime.MidFieldActive}, chunk={runtime.ChunkPathRequested}/{runtime.ChunkPathActive}, blanket={runtime.BlanketRequested}/{runtime.BlanketActive}");
        }

        if (ServiceLocator.TryGet(out IGrassDebugStatsProvider provider))
        {
            GrassDebugStats stats = provider.GetGrassDebugStats();
            GUILayout.Label($"Chunk: visible={stats.VisibleChunks}, tracked={stats.TrackedChunks}, calls={stats.DrawCalls}, buffer={stats.BufferMegabytes:F1} MB");
            GUILayout.Label($"Chunk roots: {stats.BladeInstances}, lanes={stats.VisibleLanes}/{stats.CandidateLanes}");
        }

        if (ServiceLocator.TryGet(out IGrassNearFieldStatsProvider nearProvider))
        {
            GrassNearFieldStats near = nearProvider.GetGrassNearFieldStats();
            GUILayout.Label($"Near: emitted={near.EmittedInstances}, grid={near.GridWidth}x{near.GridHeight}, buffer={near.BufferMegabytes:F1} MB");
        }

        if (ServiceLocator.TryGet(out IGrassMidFieldStatsProvider midProvider))
        {
            GrassMidFieldStats mid = midProvider.GetGrassMidFieldStats();
            GUILayout.Label($"Mid (deprecated): emitted={mid.EmittedInstances}, faces={mid.FacesActive}, grid={mid.GridWidth}x{mid.GridHeight}, buffer={mid.BufferMegabytes:F1} MB");
        }
    }

    static void AppendAtmosphereMetadata(StringBuilder sb)
    {
        sb.AppendLine("--- Atmosphere ---");
        sb.AppendLine($"Globals: oceanDebug={Shader.GetGlobalInt(OceanDebugModeId)}, radius={Shader.GetGlobalFloat(AtmosphereRadiusId):F2}, sea={Shader.GetGlobalFloat(SeaLevelRadiusId):F2}, densityOrigin={Shader.GetGlobalFloat(DensityOriginRadiusId):F2}");
        sb.AppendLine($"PassInputs: waterVolume={Shader.GetGlobalFloat(WaterVolumeEnabledId):F2}, viewSteps={Shader.GetGlobalInt(ViewStepsId)}, sunSteps={Shader.GetGlobalInt(SunStepsId)}");
        Vector4 terrainDistances = Shader.GetGlobalVector(TerrainAerialPerspectiveDistancesId);
        sb.AppendLine($"TerrainClarity: fullTo={terrainDistances.x:F1}m, atmosphereBy={terrainDistances.y:F1}m");
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

[CommandPrefix("grass")]
public static class GrassCommands
{
    [ConsoleCommand("status", "Show master and per-layer grass runtime state.")]
    public static string Status()
    {
        return TryGetControl(out IGrassRuntimeControl control)
            ? FormatState(control.GetGrassRuntimeState())
            : "grass runtime control is unavailable";
    }

    [ConsoleCommand("enabled", "Get or set the grass master switch.")]
    public static string Enabled(bool? value = null)
    {
        if (!TryGetControl(out IGrassRuntimeControl control))
            return "grass runtime control is unavailable";

        if (value.HasValue)
            control.SetGrassEnabled(value.Value);
        return FormatState(control.GetGrassRuntimeState());
    }

    [ConsoleCommand("layer", "Get or set Near, Chunk, or Blanket. Mid is a deprecated A/B experiment.")]
    public static string Layer(GrassRenderLayer layer, bool? enabled = null)
    {
        if (!TryGetControl(out IGrassRuntimeControl control))
            return "grass runtime control is unavailable";

        if (enabled.HasValue)
            control.SetGrassLayerEnabled(layer, enabled.Value);
        return FormatState(control.GetGrassRuntimeState());
    }

    static bool TryGetControl(out IGrassRuntimeControl control)
    {
        return ServiceLocator.TryGet(out control);
    }

    static string FormatState(GrassRuntimeState state)
    {
        return $"master={state.MasterEnabled}, "
            + $"near={state.NearFieldRequested} (active={state.NearFieldActive}), "
            + $"mid={state.MidFieldRequested} (active={state.MidFieldActive}), "
            + $"chunk={state.ChunkPathRequested} (active={state.ChunkPathActive}), "
            + $"blanket={state.BlanketRequested} (active={state.BlanketActive})";
    }
}
