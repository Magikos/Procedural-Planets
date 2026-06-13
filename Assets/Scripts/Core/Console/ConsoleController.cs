using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ConsoleController : MonoBehaviour, IConsoleService
{
    const float FadeDuration = 0.12f;

    // Input-line syntax-highlight colors live on ConsoleTheme (Resources/ConsoleTheme.asset).
    // Accessed via _renderer.Theme.InputXyz inside ConsoleInputLineFormatter.

    IInputMapService _input;
    ConsoleRenderer _renderer;
    readonly ConsoleScrollback _scrollback = new();
    readonly ConsoleInputLineFormatter _inputFormatter = new();
    ConsoleAsyncRunner _runner;
    ConsoleInputController _inputController;
    int _tailScrollbackVersion;  // scrollback.Version last seen while ScrollOffset == 0
    float _currentAlpha;
    float _targetAlpha;
    bool _isOpen;
    bool _warnedNoInput;

    public bool IsOpen => _isOpen;
    public ConsoleAnchor Anchor { get; set; } = ConsoleAnchor.Top;
    public ConsoleScrollback Scrollback => _scrollback;
    public int ScrollbackCapacity
    {
        get => _scrollback.Capacity;
        set => _scrollback.Capacity = value;
    }

    void Awake()
    {
        _renderer = new ConsoleRenderer();
        // The runner's confirm callback resolves _inputController lazily so the two can hold
        // mutual references (runner needs confirm; the input controller submits via the runner).
        _runner = new ConsoleAsyncRunner(_scrollback, this, (question, onYes) => _inputController.ShowConfirm(question, onYes));
        _inputController = new ConsoleInputController(_scrollback, _runner, Close);
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
        _inputController.UnhookTextInput();
    }

    void OnDestroy()
    {
        // Shut the runner down first so any in-flight ObservePending skips writing to dead state.
        _runner.Shutdown();
        if (_isOpen && _input != null)
        {
            _input.DisableConsole();
            _input.EnableGameplay();
        }
        _inputController.NotifyClosed();
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
            {
                _inputController.CancelConfirmIfActive();
                Close();
            }

            if (_isOpen)
                _inputController.Tick(_input);
        }

        _runner.Tick();

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
        if (_input != null) { _input.DisableGameplay(); _input.EnableConsole(); }
        _inputController.NotifyOpened();
        EventBus<ConsoleOpenedEvent>.Raise(new ConsoleOpenedEvent());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _targetAlpha = 0f;
        if (_input != null) { _input.DisableConsole(); _input.EnableGameplay(); }
        _inputController.NotifyClosed();
        EventBus<ConsoleClosedEvent>.Raise(new ConsoleClosedEvent());
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    public void RunCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;
        _runner.RunProgrammatic(commandLine);
    }

    public void Print(string text)
    {
        if (text == null) return;
        _scrollback.AppendText(text, ConsoleMessageType.Output);
    }
    public void PrintLine(string text) => Print(text);
    public void PrintWarning(string text)
    {
        if (text == null) return;
        _scrollback.AppendText(text, ConsoleMessageType.Warning);
    }
    public void PrintError(string text)
    {
        if (text == null) return;
        _scrollback.AppendText(text, ConsoleMessageType.Error);
    }
    public void Clear() => _scrollback.Clear();

    public void BeginAsync(string alias, object awaitable, bool isCancellable)
        => _runner.BeginAsync(alias, awaitable, isCancellable);

    public void AbandonPending() => _runner.AbandonPending();

    public void RequestCancelPending() => _runner.RequestCancelPending();

    /// <inheritdoc/>
    public void Confirm(string question, Action onYes, Action onNo = null)
        => _inputController.ShowConfirm(question, onYes, onNo);

    /// <inheritdoc/>
    public ConsoleDiagnostics GetDiagnostics()
    {
        return new ConsoleDiagnostics(
            _isOpen,
            Anchor,
            _scrollback.Count,
            _scrollback.Capacity,
            _inputController.HistoryCount,
            ConsoleRegistry.Commands.Count,
            _runner.PendingAlias,
            _runner.PendingElapsedSeconds,
            _runner.PendingIsCancellable);
    }

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
        _scrollback.AppendText(condition ?? "", msgType);
    }

    // --- Rendering ---------------------------------------------------------

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_currentAlpha <= 0.001f) return;
        if (_renderer == null) return;
        if (cam != Camera.main) return;

        bool cursorOn = (Mathf.FloorToInt(Time.unscaledTime * 2f) & 1) == 0;
        string typed = _inputController.Text;
        int cursorPos = _inputController.CursorPos;
        int scrollOffset = _inputController.ScrollOffset;

        // Track whether new messages have arrived while the user is scrolled back.
        // _tailScrollbackVersion is refreshed every frame at offset 0, so the first
        // frame after scrolling uses the version that was current just before scrolling.
        bool hasNewMessages;
        if (scrollOffset == 0)
        {
            _tailScrollbackVersion = _scrollback.Version;
            hasNewMessages = false;
        }
        else
        {
            hasNewMessages = _scrollback.Version > _tailScrollbackVersion;
        }

        bool ghostActive = _inputController.GhostActive;
        IReadOnlyList<Suggestion> popupSuggestions =
            (_inputController.IsConfirmMode || ghostActive)
                ? System.Array.Empty<Suggestion>()
                : _inputController.Suggestions;

        List<TextSpan> inputSpans = _inputFormatter.Build(
            _renderer.Theme, typed, cursorPos, cursorOn,
            _inputController.Suggestions, ghostActive, _runner.HasPending, _runner.PendingSpinnerDots);

        var cmd = new CommandBuffer { name = "ConsoleOverlay" };
        try
        {
            ConsoleRenderer.ConfirmRenderData? confirmData = null;
            if (_inputController.IsConfirmMode && _inputController.HasConfirm)
                confirmData = new ConsoleRenderer.ConfirmRenderData
                {
                    Question = _inputController.ConfirmQuestion,
                    ActiveIsYes = _inputController.ConfirmActiveIsYes,
                };

            var state = new ConsoleRenderer.ConsoleRenderState
            {
                Alpha = _currentAlpha,
                Anchor = Anchor,
                InputSpans = inputSpans,
                Scrollback = _scrollback,
                ScrollOffset = scrollOffset,
                Suggestions = popupSuggestions,
                ActiveSuggestion = _inputController.ActiveSuggestion,
                PopupScrollOffset = _inputController.PopupScrollOffset,
                PopupVisibleCount = ConsoleInputController.PopupVisibleCount,
                Confirm = confirmData,
                HasNewMessages = hasNewMessages,
            };
            _renderer.Render(cmd, state);
            ctx.ExecuteCommandBuffer(cmd);
            ctx.Submit();
        }
        finally
        {
            cmd.Release();
        }
    }
}
