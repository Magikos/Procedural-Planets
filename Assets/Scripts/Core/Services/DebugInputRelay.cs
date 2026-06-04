using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class DebugInputRelay : MonoBehaviour
{
    IInputMapService _input;

    void Update()
    {
        if (_input == null && !ServiceLocator.TryGet(out _input))
            return;

        if (_input.ToggleDebugOverlay.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleDebugOverlay));

        if (_input.CycleCaptureSet.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.CycleCaptureSet));

        if (_input.ToggleSunFreeze.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleSunFreeze));

        if (_input.DumpWeather.WasPerformedThisFrame())
        {
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleWaterDebugDetails));
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.DumpWeatherDiagnostics));
        }

        if (_input.TogglePrecipitation.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TogglePrecipitation));

        if (_input.TriggerCapture.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TriggerCapture));

        if (_input.ToggleProfiling.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleProfiling));

        // Scale reference markers — measurement aid for Phase C grass density tuning.
        // M = drop at camera look-target. Shift+M = clear. T = teleport to markers.
        if (_input.DropScaleMarker.WasPerformedThisFrame())
        {
            bool shift = Keyboard.current?.shiftKey.isPressed ?? false;
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(
                shift ? DebugCommandType.ClearScaleMarkers : DebugCommandType.DropScaleMarkers));
        }

        if (_input.TeleportToMarkers.WasPerformedThisFrame())
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TeleportToScaleMarkers));
    }
}
