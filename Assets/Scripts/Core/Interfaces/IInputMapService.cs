using UnityEngine.InputSystem;

public interface IInputMapService
{
    InputActionMap GameplayMap { get; }
    InputActionMap ConsoleMap { get; }

    bool GameplayEnabled { get; }
    bool ConsoleEnabled { get; }

    void EnableGameplay();
    void DisableGameplay();
    void EnableConsole();
    void DisableConsole();

    InputAction Move { get; }
    InputAction VerticalMove { get; }
    InputAction Roll { get; }
    InputAction Look { get; }
    InputAction Scroll { get; }
    InputAction LookHold { get; }
    InputAction Sprint { get; }
    InputAction ToggleOrbit { get; }
    InputAction FaceSun { get; }
    InputAction FrameStorm { get; }
    InputAction ToggleDebugOverlay { get; }
    InputAction CycleCaptureSet { get; }
    InputAction ToggleSunFreeze { get; }
    InputAction DumpWeather { get; }
    InputAction TogglePrecipitation { get; }
    InputAction TriggerCapture { get; }
    InputAction ToggleProfiling { get; }
    InputAction DropScaleMarker { get; }
    InputAction TeleportToMarkers { get; }

    InputAction OpenConsole { get; }
    InputAction CloseConsole { get; }
    InputAction ConsoleSubmit { get; }
    InputAction ConsoleBackspace { get; }
    InputAction ConsoleTab { get; }
    InputAction ConsoleSuggestionNext { get; }
    InputAction ConsoleSuggestionPrev { get; }
    InputAction ConsoleEscape { get; }
    InputAction ConsolePageUp { get; }
    InputAction ConsolePageDown { get; }

    InputAction ConsoleCursorLeft { get; }
    InputAction ConsoleCursorRight { get; }
    InputAction ConsoleCursorHome { get; }
    InputAction ConsoleCursorEnd { get; }
    InputAction ConsoleDelete { get; }
}
