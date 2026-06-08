using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputMapService : IInputMapService, IDisposable
{
    readonly InputActionAsset _asset;

    public InputActionMap GameplayMap { get; }
    public InputActionMap ConsoleMap { get; }

    public bool GameplayEnabled => GameplayMap.enabled;
    public bool ConsoleEnabled => ConsoleMap.enabled;

    public InputAction Move { get; }
    public InputAction VerticalMove { get; }
    public InputAction Roll { get; }
    public InputAction Look { get; }
    public InputAction Scroll { get; }
    public InputAction LookHold { get; }
    public InputAction Sprint { get; }
    public InputAction ToggleOrbit { get; }
    public InputAction FaceSun { get; }
    public InputAction FrameStorm { get; }
    public InputAction ToggleDebugOverlay { get; }
    public InputAction CycleCaptureSet { get; }
    public InputAction ToggleSunFreeze { get; }
    public InputAction DumpWeather { get; }
    public InputAction TogglePrecipitation { get; }
    public InputAction TriggerCapture { get; }
    public InputAction ToggleProfiling { get; }
    public InputAction DropScaleMarker { get; }
    public InputAction TeleportToMarkers { get; }
    public InputAction GrassInteractorDistance { get; }

    public InputAction OpenConsole { get; }
    public InputAction CloseConsole { get; }
    public InputAction ConsoleSubmit { get; }
    public InputAction ConsoleBackspace { get; }
    public InputAction ConsoleTab { get; }
    public InputAction ConsoleSuggestionNext { get; }
    public InputAction ConsoleSuggestionPrev { get; }
    public InputAction ConsoleEscape { get; }
    public InputAction ConsolePageUp { get; }
    public InputAction ConsolePageDown { get; }

    public InputAction ConsoleCursorLeft { get; }
    public InputAction ConsoleCursorRight { get; }
    public InputAction ConsoleCursorHome { get; }
    public InputAction ConsoleCursorEnd { get; }
    public InputAction ConsoleDelete { get; }

    public InputMapService()
    {
        _asset = ScriptableObject.CreateInstance<InputActionAsset>();
        _asset.name = "PlanetGameInput";

        GameplayMap = _asset.AddActionMap("Gameplay");
        ConsoleMap = _asset.AddActionMap("Console");

        Move = GameplayMap.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
        Move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/s")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/a")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        VerticalMove = GameplayMap.AddAction("VerticalMove", InputActionType.Value, expectedControlLayout: "Axis");
        VerticalMove.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/e")
            .With("Negative", "<Keyboard>/q");

        Roll = GameplayMap.AddAction("Roll", InputActionType.Value, expectedControlLayout: "Axis");
        Roll.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/z")
            .With("Negative", "<Keyboard>/c");

        Look = GameplayMap.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
        Look.AddBinding("<Mouse>/delta");

        Scroll = GameplayMap.AddAction("Scroll", InputActionType.Value, expectedControlLayout: "Vector2");
        Scroll.AddBinding("<Mouse>/scroll");

        LookHold = GameplayMap.AddAction("LookHold", InputActionType.Button);
        LookHold.AddBinding("<Mouse>/rightButton");

        Sprint = GameplayMap.AddAction("Sprint", InputActionType.Button);
        Sprint.AddBinding("<Keyboard>/leftShift");
        Sprint.AddBinding("<Keyboard>/rightShift");

        ToggleOrbit = AddButton("ToggleOrbit", "<Keyboard>/space");
        FaceSun = AddButton("FaceSun", "<Keyboard>/backspace");
        FrameStorm = AddButton("FrameStorm", "<Keyboard>/r");
        ToggleDebugOverlay = AddButton("ToggleDebugOverlay", "<Keyboard>/f6");
        CycleCaptureSet = AddButton("CycleCaptureSet", "<Keyboard>/f7");
        ToggleSunFreeze = AddButton("ToggleSunFreeze", "<Keyboard>/f8");
        DumpWeather = AddButton("DumpWeather", "<Keyboard>/f9");
        TogglePrecipitation = AddButton("TogglePrecipitation", "<Keyboard>/p");
        TriggerCapture = AddButton("TriggerCapture", "<Keyboard>/f10");
        ToggleProfiling = AddButton("ToggleProfiling", "<Keyboard>/f11");
        DropScaleMarker = AddButton("DropScaleMarker", "<Keyboard>/m");
        TeleportToMarkers = AddButton("TeleportToMarkers", "<Keyboard>/t");
        GrassInteractorDistance = GameplayMap.AddAction(
            "GrassInteractorDistance", InputActionType.Value, expectedControlLayout: "Axis");
        GrassInteractorDistance.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/rightBracket")
            .With("Negative", "<Keyboard>/leftBracket");

        OpenConsole = AddButton("OpenConsole", "<Keyboard>/backquote");

        CloseConsole = ConsoleMap.AddAction("CloseConsole", InputActionType.Button);
        CloseConsole.AddBinding("<Keyboard>/backquote");

        ConsoleSubmit = ConsoleMap.AddAction("ConsoleSubmit", InputActionType.Button);
        ConsoleSubmit.AddBinding("<Keyboard>/enter");
        ConsoleSubmit.AddBinding("<Keyboard>/numpadEnter");

        ConsoleBackspace = ConsoleMap.AddAction("ConsoleBackspace", InputActionType.Button);
        ConsoleBackspace.AddBinding("<Keyboard>/backspace");

        ConsoleTab = ConsoleMap.AddAction("ConsoleTab", InputActionType.Button);
        ConsoleTab.AddBinding("<Keyboard>/tab");

        ConsoleSuggestionNext = ConsoleMap.AddAction("ConsoleSuggestionNext", InputActionType.Button);
        ConsoleSuggestionNext.AddBinding("<Keyboard>/downArrow");

        ConsoleSuggestionPrev = ConsoleMap.AddAction("ConsoleSuggestionPrev", InputActionType.Button);
        ConsoleSuggestionPrev.AddBinding("<Keyboard>/upArrow");

        ConsoleEscape = ConsoleMap.AddAction("ConsoleEscape", InputActionType.Button);
        ConsoleEscape.AddBinding("<Keyboard>/escape");

        ConsolePageUp = ConsoleMap.AddAction("ConsolePageUp", InputActionType.Button);
        ConsolePageUp.AddBinding("<Keyboard>/pageUp");

        ConsolePageDown = ConsoleMap.AddAction("ConsolePageDown", InputActionType.Button);
        ConsolePageDown.AddBinding("<Keyboard>/pageDown");

        ConsoleCursorLeft = ConsoleMap.AddAction("ConsoleCursorLeft", InputActionType.Button);
        ConsoleCursorLeft.AddBinding("<Keyboard>/leftArrow");

        ConsoleCursorRight = ConsoleMap.AddAction("ConsoleCursorRight", InputActionType.Button);
        ConsoleCursorRight.AddBinding("<Keyboard>/rightArrow");

        ConsoleCursorHome = ConsoleMap.AddAction("ConsoleCursorHome", InputActionType.Button);
        ConsoleCursorHome.AddBinding("<Keyboard>/home");

        ConsoleCursorEnd = ConsoleMap.AddAction("ConsoleCursorEnd", InputActionType.Button);
        ConsoleCursorEnd.AddBinding("<Keyboard>/end");

        ConsoleDelete = ConsoleMap.AddAction("ConsoleDelete", InputActionType.Button);
        ConsoleDelete.AddBinding("<Keyboard>/delete");

        GameplayMap.Enable();
    }

    InputAction AddButton(string name, string binding)
    {
        InputAction action = GameplayMap.AddAction(name, InputActionType.Button);
        action.AddBinding(binding);
        return action;
    }

    public void EnableGameplay() => GameplayMap.Enable();
    public void DisableGameplay() => GameplayMap.Disable();
    public void EnableConsole() => ConsoleMap.Enable();
    public void DisableConsole() => ConsoleMap.Disable();

    public void Dispose()
    {
        if (_asset == null)
            return;

        _asset.Disable();
        UnityEngine.Object.Destroy(_asset);
    }
}
