using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[DisallowMultipleComponent]
public class DebugInputRelay : MonoBehaviour
{
    Mouse _mouse;
    Keyboard _keyboard;

    void Update()
    {
        RefreshInputDevices();

        if (WasKeyPressed(_keyboard?.f6Key, KeyCode.F6))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleDebugOverlay));

        if (WasKeyPressed(_keyboard?.f7Key, KeyCode.F7))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.CycleCaptureSet));

        if (WasKeyPressed(_keyboard?.f8Key, KeyCode.F8))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleSunFreeze));

        if (WasKeyPressed(_keyboard?.f9Key, KeyCode.F9))
        {
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleWaterDebugDetails));
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.DumpWeatherDiagnostics));
        }

        if (WasKeyPressed(_keyboard?.pKey, KeyCode.P))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TogglePrecipitation));

        if (WasKeyPressed(_keyboard?.f10Key, KeyCode.F10))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TriggerCapture));

        if (WasKeyPressed(_keyboard?.f11Key, KeyCode.F11))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleProfiling));

        // Scale reference markers — measurement aid for Phase C grass density tuning.
        // M = drop at camera look-target. Shift+M = clear. T = teleport to markers.
        if (WasKeyPressed(_keyboard?.mKey, KeyCode.M))
        {
            bool shift = (_keyboard?.shiftKey.isPressed ?? false) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(
                shift ? DebugCommandType.ClearScaleMarkers : DebugCommandType.DropScaleMarkers));
        }
        if (WasKeyPressed(_keyboard?.tKey, KeyCode.T))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TeleportToScaleMarkers));
    }

    void RefreshInputDevices()
    {
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;
    }

    static bool WasKeyPressed(KeyControl keyControl, KeyCode fallback)
    {
        return keyControl != null ? keyControl.wasPressedThisFrame : Input.GetKeyDown(fallback);
    }
}
