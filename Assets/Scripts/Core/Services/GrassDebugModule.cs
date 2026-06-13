using System.Text;
using UnityEngine;

public static class GrassDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("grass");
    public static readonly DebugCaptureSetId Surface = new DebugCaptureSetId(Module, "surface");
}

public enum GrassGeometryMode
{
    Physical = 0,
    Hybrid = 1,
    Cluster = 2,
}

public static class GrassRenderDiagnostics
{
    public const float DefaultClusterStartDistance = 12f;
    public const float DefaultClusterEndDistance = 24f;

    static readonly int GeometryModeId = Shader.PropertyToID(ShaderGlobalIds.GrassGeometryMode);
    static readonly int ClusterStartDistanceId = Shader.PropertyToID(ShaderGlobalIds.GrassClusterStartDistance);
    static readonly int ClusterEndDistanceId = Shader.PropertyToID(ShaderGlobalIds.GrassClusterEndDistance);

    public static GrassGeometryMode GeometryMode { get; private set; } = GrassGeometryMode.Hybrid;
    public static float ClusterStartDistance { get; private set; } = DefaultClusterStartDistance;
    public static float ClusterEndDistance { get; private set; } = DefaultClusterEndDistance;

    public static void Reset()
    {
        GeometryMode = GrassGeometryMode.Hybrid;
        ClusterStartDistance = DefaultClusterStartDistance;
        ClusterEndDistance = DefaultClusterEndDistance;
        Apply();
    }

    public static void SetGeometryMode(GrassGeometryMode mode)
    {
        GeometryMode = mode;
        Apply();
    }

    public static bool TrySetClusterRange(float startDistance, float endDistance, out string error)
    {
        if (startDistance < 2f)
        {
            error = "cluster start must be at least 2m";
            return false;
        }
        if (endDistance <= startDistance)
        {
            error = "cluster end must be greater than cluster start";
            return false;
        }

        ClusterStartDistance = startDistance;
        ClusterEndDistance = endDistance;
        Apply();
        error = null;
        return true;
    }

    public static string FormatState()
    {
        return $"mode={GeometryMode}, clusterRange={ClusterStartDistance:F1}-{ClusterEndDistance:F1}m";
    }

    public static void ApplyCurrent()
    {
        Apply();
    }

    static void Apply()
    {
        Shader.SetGlobalInt(GeometryModeId, (int)GeometryMode);
        Shader.SetGlobalFloat(ClusterStartDistanceId, ClusterStartDistance);
        Shader.SetGlobalFloat(ClusterEndDistanceId, ClusterEndDistance);
    }
}

public sealed class GrassDebugModule : IDebugModule, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => GrassDebugIds.Module;

    public void Register(DebugRegistry registry)
    {
        GrassRenderDiagnostics.Reset();
        registry.RegisterDefaultCaptureSet(GrassDebugIds.Surface, "Grass",
            WaterDebugIds.Mode(DebugModeConstants.Off),
            WaterDebugIds.Mode(DebugModeConstants.AtmosphereBypass),
            WaterDebugIds.Mode(DebugModeConstants.WaterOff),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapPrimaryId),
            BiomeDebugIds.Mode(DebugModeConstants.BiomeMapBlend),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainPrimaryAlbedo),
            TerrainDebugIds.Mode(DebugModeConstants.TerrainMixedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSelectedAlbedo),
            BiomeDebugIds.Mode(DebugModeConstants.GrassLodCoverage),
            BiomeDebugIds.Mode(DebugModeConstants.TerrainSurfaceNormal),
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
            sb.AppendLine($"Controller: active={stats.ControllerActive}, shader={stats.ShaderAvailable}");
            sb.AppendLine($"Chunks: visible={stats.VisibleChunks}, residency={stats.ResidencyChunks}, buffered={stats.BufferedResidencyChunks}, tracked={stats.TrackedChunks}, maxDepth={stats.MaxChunkDepth}, minBladeDepth={stats.MinChunkDepthForBlades}, coarseOffset={stats.MaxCoarseLodOffsetForBlades}");
            sb.AppendLine($"ChunkDepths: fine={stats.FineTrackedChunks}/{stats.FineChunksWithInstances}, coarse={stats.CoarseTrackedChunks}/{stats.CoarseChunksWithInstances} tracked/populated");
            sb.AppendLine($"Quality: maxBladesPerLane={stats.MaxBladesPerLane}, visualBladesPerInstance={stats.VisualBladesPerInstance}, vertexCount={stats.BladeVertexCount}, densityMultiplier={stats.DensityMultiplier:F2}, maxDistance={stats.MaxRenderDistance:F1}, fadeStart={stats.DistanceFadeStart:F1}, distanceJitter={stats.CullDistanceJitter01:F2}, residencyPadding={stats.ResidencyFrustumPaddingDegrees:F1}deg");
            sb.AppendLine($"PlacementCull: frustumEnabled={stats.PlacementFrustumCullEnabled}");
            sb.AppendLine($"Draw: calls={stats.DrawCalls}, chunksWithInstances={stats.ChunksWithInstances}, instances={stats.BladeInstances}, visualBlades={(long)stats.BladeInstances * Mathf.Max(stats.VisualBladesPerInstance, 1)}, buffer={stats.BufferMegabytes:F3} MB");
            sb.AppendLine($"ResidencyGpu: chunksWithInstances={stats.ResidentChunksWithInstances}, instances={stats.ResidentBladeInstances}");
            sb.AppendLine($"Dispatch: placement={stats.PlacementDispatches}, chunksWithStats={stats.ChunksWithStats}, chunkInstances={stats.ChunkInstanceMin}/{stats.ChunkInstanceAverage:F1}/{stats.ChunkInstanceMax} min/avg/max");
            sb.AppendLine($"CullLanes: candidates={stats.CandidateLanes}, visible={stats.VisibleLanes}, density={stats.DensityRejectedLanes}, shape={stats.ShapeRejectedLanes}, state={stats.StateRejectedLanes}, water={stats.WaterRejectedLanes}, slope={stats.SlopeRejectedLanes}, distance={stats.DistanceRejectedLanes}, distanceFade={stats.DistanceFadeRejectedLanes}, frustum={stats.FrustumRejectedLanes}");
            sb.AppendLine($"CullBlades: candidates={stats.CandidateBlades}, emitted={stats.EmittedBlades}, densityRoll={stats.DensityRejectedBlades}, innerFade={stats.InnerFadeRejectedBlades}, slopeRoll={stats.SlopeRejectedBlades}, overflow={stats.OverflowRejectedBlades}");
            sb.AppendLine($"Suppression: oldChunkSuppressed={stats.OldChunkSuppressedCount}/{stats.DrawCalls + stats.OldChunkSuppressedCount}");
            sb.AppendLine($"Interactors: registered={stats.RegisteredInteractors}, uploaded={stats.UploadedInteractors}, activeSources={stats.ActiveInteractorSources}, releaseGpu={stats.UploadedReleaseSamples}, releaseRetained={stats.RetainedReleaseSamples}");
            sb.AppendLine($"SurfaceAtlas: resolution={stats.SurfaceAtlasResolution}");
        }
        AppendNearFieldMetadata(sb);
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
        sb.AppendLine($"Layers: near={state.NearFieldRequested}/{state.NearFieldActive} requested/active, chunk={state.ChunkPathRequested}/{state.ChunkPathActive}, blanket={state.BlanketRequested}/{state.BlanketActive}");
        sb.AppendLine($"Representation: {GrassRenderDiagnostics.FormatState()}");
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
        sb.AppendLine(
            $"Draw: emitted={nf.EmittedInstances}, " +
            $"visualBlades={(long)nf.EmittedInstances * Mathf.Max(nf.VisualBladesPerInstance, 1)}, " +
            $"bladesPerInstance={nf.VisualBladesPerInstance}, vertexCount={nf.BladeVertexCount}, " +
            $"capacity={nf.CapacityInstances}, buffer={nf.BufferMegabytes:F1} MB");
        sb.AppendLine($"Cull: candidates={nf.CandidateCells}, density={nf.DensityRejectedCells}, water={nf.WaterRejectedCells}, slope={nf.SlopeRejectedCells}, distance={nf.DistanceRejectedCells}, distanceFade={nf.DistanceFadeRejectedCells}, frustum={nf.FrustumRejectedCells}, faceArea={nf.FaceAreaRejectedCells}, rangeBudget={nf.RangeBudgetRejectedCells}, overflow={nf.OverflowDropped}");
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
            GUILayout.Label($"Layers: master={runtime.MasterEnabled}, near={runtime.NearFieldRequested}/{runtime.NearFieldActive}, chunk={runtime.ChunkPathRequested}/{runtime.ChunkPathActive}, blanket={runtime.BlanketRequested}/{runtime.BlanketActive}");
            GUILayout.Label($"Representation: {GrassRenderDiagnostics.FormatState()}");
        }

        if (ServiceLocator.TryGet(out IGrassDebugStatsProvider provider))
        {
            GrassDebugStats stats = provider.GetGrassDebugStats();
            GUILayout.Label($"Chunk: visible={stats.VisibleChunks}, resident={stats.ResidencyChunks}, buffered={stats.BufferedResidencyChunks}, tracked={stats.TrackedChunks}, calls={stats.DrawCalls}, buffer={stats.BufferMegabytes:F1} MB");
            GUILayout.Label($"Chunk roots: {stats.BladeInstances}, lanes={stats.VisibleLanes}/{stats.CandidateLanes}");
        }

        if (ServiceLocator.TryGet(out IGrassNearFieldStatsProvider nearProvider))
        {
            GrassNearFieldStats near = nearProvider.GetGrassNearFieldStats();
            GUILayout.Label($"Near: emitted={near.EmittedInstances}, grid={near.GridWidth}x{near.GridHeight}, buffer={near.BufferMegabytes:F1} MB");
        }
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

    [ConsoleCommand("render-mode", "Get or set Physical, Hybrid, or Cluster geometry without rebuilding.")]
    public static string RenderMode(GrassGeometryMode? mode = null)
    {
        if (mode.HasValue)
            GrassRenderDiagnostics.SetGeometryMode(mode.Value);
        return GrassRenderDiagnostics.FormatState();
    }

    [ConsoleCommand("render-cluster-range", "Get or set the hybrid cluster handoff range in meters without rebuilding.")]
    public static string RenderClusterRange(float? start = null, float? end = null)
    {
        if (!start.HasValue && !end.HasValue)
            return GrassRenderDiagnostics.FormatState();
        if (!start.HasValue || !end.HasValue)
            return "both start and end are required";
        return GrassRenderDiagnostics.TrySetClusterRange(start.Value, end.Value, out string error)
            ? GrassRenderDiagnostics.FormatState()
            : error;
    }

    [ConsoleCommand("render-reset", "Restore the default Hybrid grass representation.")]
    public static string RenderReset()
    {
        GrassRenderDiagnostics.Reset();
        return GrassRenderDiagnostics.FormatState();
    }

    static bool TryGetControl(out IGrassRuntimeControl control)
    {
        return ServiceLocator.TryGet(out control);
    }

    static string FormatState(GrassRuntimeState state)
    {
        return $"master={state.MasterEnabled}, "
            + $"near={state.NearFieldRequested} (active={state.NearFieldActive}), "
            + $"chunk={state.ChunkPathRequested} (active={state.ChunkPathActive}), "
            + $"blanket={state.BlanketRequested} (active={state.BlanketActive})";
    }
}
