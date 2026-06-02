public enum DebugCommandType
{
    TogglePrecipitation,
    CycleCaptureSet,
    TriggerCapture,
    ToggleSunFreeze,
    ToggleDebugOverlay,
    ToggleWaterDebugDetails,
    ToggleProfiling,
    DumpWeatherDiagnostics,
    // Scale reference markers (Phase C measurement aid).
    // Drop = place a set of 1m/3m/10m cubes + 30m pillar on the surface at the camera's
    // look target. Clear = destroy them. Teleport = warp the camera to look at the markers
    // from ~20m back, useful for sizing biome features against known scales.
    DropScaleMarkers,
    ClearScaleMarkers,
    TeleportToScaleMarkers,
}

public readonly struct DebugCommandRequestedEvent : IGameEvent
{
    public readonly DebugCommandType Command;

    public DebugCommandRequestedEvent(DebugCommandType command)
    {
        Command = command;
    }
}
