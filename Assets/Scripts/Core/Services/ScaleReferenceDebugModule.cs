using System.Text;

public static class ScaleReferenceDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("scaleref");
}

// Owns the scale-reference marker block in the F10 capture sidecar. Reads the scale-reference
// stats provider when present.
public sealed class ScaleReferenceDebugModule : IDebugModule, IDebugCaptureMetadataProvider
{
    public DebugModuleId Id => ScaleReferenceDebugIds.Module;

    public void Register(DebugRegistry registry)
    {
        registry.RegisterMetadataProvider(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
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
