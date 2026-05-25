public enum DebugCommandType
{
    TogglePrecipitation,
    CycleCaptureSet,
    TriggerCapture,
    ToggleSunFreeze,
    ToggleDebugOverlay,
    ToggleWaterDebugDetails,
    ToggleProfiling
}

public readonly struct DebugCommandRequestedEvent : IGameEvent
{
    public readonly DebugCommandType Command;

    public DebugCommandRequestedEvent(DebugCommandType command)
    {
        Command = command;
    }
}
