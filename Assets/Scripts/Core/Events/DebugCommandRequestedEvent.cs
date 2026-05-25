public enum DebugCommandType
{
    TogglePrecipitation,
    CycleCaptureSet,
    TriggerCapture,
    ToggleSunFreeze,
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
