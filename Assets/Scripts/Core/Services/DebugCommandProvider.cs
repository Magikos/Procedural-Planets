public class DebugCommandProvider : IDebugCommandProvider, System.IDisposable
{
    public DebugCommandProvider()
    {
        EventBus<DebugCommandRequestedEvent>.Listen(OnDebugCommandRequested);
    }

    public void Dispose()
    {
        EventBus<DebugCommandRequestedEvent>.Unlisten(OnDebugCommandRequested);
    }

    public void HandleCommand(DebugCommandType command)
    {
        switch (command)
        {
            case DebugCommandType.TogglePrecipitation:
                EventBus<DebugPrecipitationToggleRequestedEvent>.Raise(new DebugPrecipitationToggleRequestedEvent());
                break;
            case DebugCommandType.CycleCaptureSet:
                EventBus<DebugCaptureSetCycleRequestedEvent>.Raise(new DebugCaptureSetCycleRequestedEvent());
                break;
            case DebugCommandType.TriggerCapture:
                EventBus<DebugCaptureRequestedEvent>.Raise(new DebugCaptureRequestedEvent());
                break;
            case DebugCommandType.ToggleSunFreeze:
                EventBus<DebugSunFreezeToggleRequestedEvent>.Raise(new DebugSunFreezeToggleRequestedEvent());
                break;
            case DebugCommandType.ToggleDebugOverlay:
                EventBus<DebugOverlayToggleRequestedEvent>.Raise(new DebugOverlayToggleRequestedEvent());
                break;
            case DebugCommandType.ToggleWaterDebugDetails:
                EventBus<DebugWaterDebugDetailsToggleRequestedEvent>.Raise(new DebugWaterDebugDetailsToggleRequestedEvent());
                break;
            case DebugCommandType.ToggleProfiling:
                EventBus<DebugProfilingToggleRequestedEvent>.Raise(new DebugProfilingToggleRequestedEvent());
                break;
            case DebugCommandType.DumpWeatherDiagnostics:
                EventBus<DebugWeatherDiagnosticsRequestedEvent>.Raise(new DebugWeatherDiagnosticsRequestedEvent());
                break;
        }
    }

    void OnDebugCommandRequested(DebugCommandRequestedEvent evt)
    {
        HandleCommand(evt.Command);
    }
}
