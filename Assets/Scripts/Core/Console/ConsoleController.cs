using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ConsoleController : MonoBehaviour, IConsoleService
{
    const float FadeDuration = 0.12f;
    const float BackspaceInitialDelay = 0.40f;  // seconds before key-repeat starts
    const float BackspaceRepeatInterval = 0.05f;  // seconds between repeats

    // Syntax-highlight colours for the input line.
    static readonly Color PromptColor = new Color(0.38f, 0.38f, 0.40f, 1f);
    static readonly Color CmdColor = new Color(0.35f, 0.80f, 0.88f, 1f);
    static readonly Color ValColor = new Color(0.85f, 0.78f, 0.62f, 1f);
    static readonly Color StrColor = new Color(0.58f, 0.85f, 0.48f, 1f);
    static readonly Color GhostColor = new Color(0.30f, 0.30f, 0.32f, 0.85f);
    static readonly Color HintReqdColor = new Color(0.58f, 0.43f, 0.24f, 0.72f);
    static readonly Color HintOptColor = new Color(0.35f, 0.42f, 0.52f, 0.65f);

    IInputMapService _input;
    ConsoleRenderer _renderer;
    readonly ConsoleScrollback _scrollback = new();
    readonly ConsoleInputBuffer _inputBuffer = new();
    readonly IntellisenseEngine _intellisense = new();
    IReadOnlyList<Suggestion> _suggestions = System.Array.Empty<Suggestion>();
    readonly List<TextSpan> _inputSpans = new();
    int _activeSuggestionIdx;
    bool _suggestionsFrozen;
    bool _suggestionsSuppressed;
    float _backspaceHeldTime;
    float _backspaceNextRepeat;
    int _scrollOffset;           // lines from bottom (0 = live tail)
    float _currentAlpha;
    float _targetAlpha;
    bool _isOpen;
    bool _warnedNoInput;
    bool _textInputHooked;

    public bool IsOpen => _isOpen;
    public ConsoleAnchor Anchor { get; set; } = ConsoleAnchor.Top;
    public ConsoleScrollback Scrollback => _scrollback;

    void Awake()
    {
        _renderer = new ConsoleRenderer();
    }

    void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        Application.logMessageReceived += OnLogMessageReceived;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        Application.logMessageReceived -= OnLogMessageReceived;
        UnhookTextInput();
    }

    void OnDestroy()
    {
        _renderer?.Dispose();
        _renderer = null;
        ServiceLocator.Unregister<IConsoleService>(this);
    }

    void Update()
    {
        EnsureInput();

        if (_input != null)
        {
            if (_input.OpenConsole.WasPerformedThisFrame() && !_isOpen)
                Open();
            if (_input.CloseConsole.WasPerformedThisFrame() && _isOpen)
                Close();

            if (_isOpen)
            {
                if (_input.ConsoleEscape.WasPerformedThisFrame())
                {
                    if (PopupVisible)
                        DismissSuggestions();
                    else if (_inputBuffer.Length > 0)
                    {
                        _inputBuffer.Clear();
                        ResetSuggestions();
                    }
                    else
                        Close();
                }
                if (_input.ConsoleSubmit.WasPerformedThisFrame())
                {
                    if (PopupVisible)
                        AcceptSuggestion();
                    else
                        SubmitInputLine();
                }
                bool bsPressed = _input.ConsoleBackspace.WasPerformedThisFrame();
                bool bsHeld = _input.ConsoleBackspace.IsPressed();
                if (bsPressed)
                {
                    _inputBuffer.Backspace();
                    ResetSuggestions();
                    _backspaceHeldTime = 0f;
                    _backspaceNextRepeat = BackspaceInitialDelay;
                }
                else if (bsHeld)
                {
                    _backspaceHeldTime += Time.unscaledDeltaTime;
                    while (_backspaceHeldTime >= _backspaceNextRepeat)
                    {
                        _inputBuffer.Backspace();
                        ResetSuggestions();
                        _backspaceNextRepeat += BackspaceRepeatInterval;
                    }
                }
                else
                {
                    _backspaceHeldTime = 0f;
                    _backspaceNextRepeat = 0f;
                }
                if (_input.ConsoleTab.WasPerformedThisFrame())
                {
                    bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                    if (shift) HandleShiftTab(); else HandleTab();
                }
                if (_input.ConsoleSuggestionNext.WasPerformedThisFrame() && _suggestions.Count > 0)
                {
                    _suggestionsFrozen = true;
                    _activeSuggestionIdx = (_activeSuggestionIdx + 1) % _suggestions.Count;
                }
                if (_input.ConsoleSuggestionPrev.WasPerformedThisFrame() && _suggestions.Count > 0)
                {
                    _suggestionsFrozen = true;
                    _activeSuggestionIdx = (_activeSuggestionIdx - 1 + _suggestions.Count) % _suggestions.Count;
                }

                if (_input.ConsolePageUp.WasPerformedThisFrame())
                    _scrollOffset = Mathf.Min(_scrollOffset + 5, Mathf.Max(0, _scrollback.Count - 1));
                if (_input.ConsolePageDown.WasPerformedThisFrame())
                    _scrollOffset = Mathf.Max(0, _scrollOffset - 5);

                if (!_suggestionsFrozen && !_suggestionsSuppressed)
                    _suggestions = _intellisense.Update(_inputBuffer.Text);
            }
        }

        float step = Time.unscaledDeltaTime / FadeDuration;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, step);
    }

    void EnsureInput()
    {
        if (_input != null) return;
        if (ServiceLocator.TryGet(out _input)) return;

        if (!_warnedNoInput)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "ConsoleController",
                "IInputMapService not registered yet; console open/close will be unresponsive until it appears.");
            _warnedNoInput = true;
        }
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        _targetAlpha = 1f;
        if (_input != null)
        {
            _input.DisableGameplay();
            _input.EnableConsole();
        }
        HookTextInput();
        EventBus<ConsoleOpenedEvent>.Raise(new ConsoleOpenedEvent());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _targetAlpha = 0f;
        if (_input != null)
        {
            _input.DisableConsole();
            _input.EnableGameplay();
        }
        UnhookTextInput();
        EventBus<ConsoleClosedEvent>.Raise(new ConsoleClosedEvent());
    }

    public void Toggle()
    {
        if (_isOpen) Close(); else Open();
    }

    // Popup is visible when there are multiple options, or exactly one frozen via arrow-key nav.
    bool PopupVisible => _suggestions.Count > 1 || (_suggestions.Count == 1 && _suggestionsFrozen);

    void HandleTab()
    {
        if (_suggestions.Count == 0) return;
        _inputBuffer.Clear();
        _inputBuffer.Append(_suggestions[_activeSuggestionIdx].CompletionText);
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    void HandleShiftTab()
    {
        if (_suggestions.Count == 0) return;
        _suggestionsFrozen = true;
        _activeSuggestionIdx = (_activeSuggestionIdx - 1 + _suggestions.Count) % _suggestions.Count;
    }

    // Accepts the active suggestion into the input buffer (same as Tab) — does not submit.
    void AcceptSuggestion()
    {
        if (_suggestions.Count == 0) return;
        _inputBuffer.Clear();
        _inputBuffer.Append(_suggestions[_activeSuggestionIdx].CompletionText);
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    void DismissSuggestions()
    {
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    // Called when user modifies input text — re-enables suggestion engine.
    void ResetSuggestions()
    {
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = false;
    }

    void HookTextInput()
    {
        if (_textInputHooked) return;
        if (Keyboard.current == null) return;
        Keyboard.current.onTextInput += OnTextInput;
        _textInputHooked = true;
    }

    void UnhookTextInput()
    {
        if (!_textInputHooked) return;
        if (Keyboard.current != null)
            Keyboard.current.onTextInput -= OnTextInput;
        _textInputHooked = false;
    }

    void OnTextInput(char c)
    {
        if (!_isOpen) return;
        // Filter the toggle keys (backtick / tilde) so the open-keystroke doesn't leak into the line.
        if (c == '`' || c == '~') return;
        // Filter control characters; Enter / Backspace are wired via Input Actions instead.
        if (c < 0x20 || c == 0x7F) return;
        _inputBuffer.Append(c);
        ResetSuggestions();
    }

    void SubmitInputLine()
    {
        string line = _inputBuffer.Text;
        _inputBuffer.Clear();
        ResetSuggestions();
        _suggestions = System.Array.Empty<Suggestion>();
        _scrollOffset = 0;   // jump to tail on submit
        if (string.IsNullOrWhiteSpace(line)) return;

        _scrollback.Append($"> {line}", ConsoleMessageType.Input);
        CommandExecutor.Execute(line, this);
    }

    public void RunCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;
        _scrollback.Append($"> {commandLine}", ConsoleMessageType.Input);
        CommandExecutor.Execute(commandLine, this);
    }

    public void Print(string text)
    {
        if (text == null) return;
        _scrollback.Append(text, ConsoleMessageType.Output);
    }

    public void PrintLine(string text) => Print(text);

    public void PrintWarning(string text)
    {
        if (text == null) return;
        _scrollback.Append(text, ConsoleMessageType.Warning);
    }

    public void PrintError(string text)
    {
        if (text == null) return;
        _scrollback.Append(text, ConsoleMessageType.Error);
    }

    public void Clear() => _scrollback.Clear();

    void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        var msgType = type switch
        {
            LogType.Warning => ConsoleMessageType.Warning,
            LogType.Error => ConsoleMessageType.Error,
            LogType.Exception => ConsoleMessageType.Exception,
            LogType.Assert => ConsoleMessageType.Error,
            _ => ConsoleMessageType.Log,
        };
        _scrollback.Append(condition ?? "", msgType);
    }


    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_currentAlpha <= 0.001f) return;
        if (_renderer == null) return;
        if (cam != Camera.main) return;

        bool cursorOn = (Mathf.FloorToInt(Time.unscaledTime * 2f) & 1) == 0;
        string typed = _inputBuffer.Text;

        // Single live suggestion suppresses popup and becomes ghost completion.
        IReadOnlyList<Suggestion> popupSuggestions =
            (_suggestions.Count == 1 && !_suggestionsFrozen && !_suggestionsSuppressed)
            ? System.Array.Empty<Suggestion>()
            : _suggestions;

        BuildInputSpans(typed, cursorOn);

        var cmd = new CommandBuffer { name = "ConsoleOverlay" };
        try
        {
            _renderer.Render(cmd, _currentAlpha, Anchor, _inputSpans,
                _scrollback, _scrollOffset, popupSuggestions, _activeSuggestionIdx);
            ctx.ExecuteCommandBuffer(cmd);
            ctx.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }

    void BuildInputSpans(string typed, bool cursorOn)
    {
        _inputSpans.Clear();
        _inputSpans.Add(new TextSpan(PromptColor, "> "));

        if (typed.Length > 0)
        {
            int spaceIdx = typed.IndexOf(' ');
            if (spaceIdx < 0)
            {
                _inputSpans.Add(new TextSpan(CmdColor, typed));
            }
            else
            {
                _inputSpans.Add(new TextSpan(CmdColor, typed.Substring(0, spaceIdx)));
                AppendSyntaxSpans(_inputSpans, typed.Substring(spaceIdx));
            }
        }

        _inputSpans.Add(new TextSpan(cursorOn ? Color.white : Color.clear, "|"));

        // Ghost completion: single live suggestion whose text starts with what's typed.
        bool hintAdded = false;
        if (_suggestions.Count == 1 && !_suggestionsFrozen && !_suggestionsSuppressed)
        {
            string completion = _suggestions[0].CompletionText;
            if (completion.Length > typed.Length &&
                completion.StartsWith(typed, System.StringComparison.OrdinalIgnoreCase))
            {
                _inputSpans.Add(new TextSpan(GhostColor, completion.Substring(typed.Length)));
                hintAdded = true;
            }
        }

        // Param hint: only when the input is exactly a command name with no space typed yet.
        if (!hintAdded)
        {
            if (typed.Length > 0 && typed.IndexOf(' ') < 0 &&
                ConsoleRegistry.TryGet(typed, out var hintCmd) && hintCmd.Parameters.Length > 0)
            {
                foreach (ParameterData p in hintCmd.Parameters)
                {
                    if (p.HasDefault)
                        _inputSpans.Add(new TextSpan(HintOptColor, $" [{p.Name}]"));
                    else
                        _inputSpans.Add(new TextSpan(HintReqdColor, $" <{p.Name}>"));
                }
            }
        }
    }

    static void AppendSyntaxSpans(List<TextSpan> spans, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                int end = text.IndexOf('"', i + 1);
                int len = (end < 0 ? text.Length : end + 1) - i;
                spans.Add(new TextSpan(StrColor, text.Substring(i, len)));
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && text[i] != '"') i++;
                if (i > start)
                    spans.Add(new TextSpan(ValColor, text.Substring(start, i - start)));
            }
        }
    }
}
