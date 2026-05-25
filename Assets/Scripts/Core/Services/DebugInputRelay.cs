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
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleWaterDebugDetails));

        if (WasKeyPressed(_keyboard?.pKey, KeyCode.P))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TogglePrecipitation));

        if (WasKeyPressed(_keyboard?.f10Key, KeyCode.F10))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TriggerCapture));

        if (WasKeyPressed(_keyboard?.f11Key, KeyCode.F11))
            EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ToggleProfiling));
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
