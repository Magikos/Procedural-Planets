using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ConsoleController : MonoBehaviour, IConsoleService
{
    const float FadeDuration = 0.12f;
    const float RepeatInitialDelay = 0.40f;
    const float RepeatInterval = 0.05f;
    const float SpinnerUpdateInterval = 0.2f;
    const float SpinnerDotPeriod = 0.5f;
    const int PopupVisibleCount = 8;

    // Input-line syntax-highlight colors live on ConsoleTheme (Resources/ConsoleTheme.asset).
    // Accessed via _renderer.Theme.InputXyz at use sites.

    enum InputMode { Normal, Confirm }

    struct PendingAsync
    {
        public string Alias;
        public object Awaitable;
        public float StartTime;
        public long LineId;
        public bool IsCancellable;
    }

    sealed class ConfirmContext
    {
        public string Question;
        public Action OnYes;
        public Action OnNo;
        public bool ActiveIsYes;
    }

    /// <summary>Tracks initial-delay-then-repeat firing for held keys (Backspace, Delete, arrows).</summary>
    sealed class KeyRepeat
    {
        float _heldTime;
        float _nextRepeat;

        public int Update(bool pressed, bool held)
        {
            if (pressed)
            {
                _heldTime = 0f;
                _nextRepeat = RepeatInitialDelay;
                return 1;
            }
            if (!held)
            {
                _heldTime = 0f;
                _nextRepeat = 0f;
                return 0;
            }
            _heldTime += Time.unscaledDeltaTime;
            int count = 0;
            while (_heldTime >= _nextRepeat)
            {
                count++;
                _nextRepeat += RepeatInterval;
            }
            return count;
        }
    }

    IInputMapService _input;
    ConsoleRenderer _renderer;
    readonly ConsoleScrollback _scrollback = new();
    readonly ConsoleInputBuffer _inputBuffer = new();
    readonly IntellisenseEngine _intellisense = new();
    readonly ConsoleHistory _history = new();
    IReadOnlyList<Suggestion> _suggestions = System.Array.Empty<Suggestion>();
    readonly ConsoleInputLineFormatter _inputFormatter = new();
    int _activeSuggestionIdx;
    int _popupScrollOffset;
    bool _suggestionsFrozen;
    bool _suggestionsSuppressed;
    readonly KeyRepeat _bsRepeat = new();
    readonly KeyRepeat _delRepeat = new();
    readonly KeyRepeat _leftRepeat = new();
    readonly KeyRepeat _rightRepeat = new();
    readonly KeyRepeat _upRepeat = new();
    readonly KeyRepeat _downRepeat = new();
    int _scrollOffset;
    int _tailScrollbackVersion;  // scrollback.Version last seen while _scrollOffset == 0
    string _draftBeforeHistory;
    PendingAsync? _pending;
    CancellationTokenSource _pendingCts;
    float _spinnerLastUpdate;
    InputMode _mode = InputMode.Normal;
    ConfirmContext _confirm;
    float _currentAlpha;
    float _targetAlpha;
    bool _isOpen;
    bool _historyLoaded;
    bool _warnedNoInput;
    bool _textInputHooked;

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
        // Null out _pending before anything else so any in-flight ObservePending sees
        // 'abandoned == true' and skips writing to dead scrollback state.
        _pending = null;
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = null;
        if (_isOpen && _input != null)
        {
            _input.DisableConsole();
            _input.EnableGameplay();
        }
        UnhookTextInput();
        if (_historyLoaded) _history.Save();
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
                if (_mode == InputMode.Confirm) DismissConfirm(invokeNo: true);
                Close();
            }

            if (_isOpen)
            {
                if (_mode == InputMode.Confirm)
                    UpdateConfirmMode();
                else
                    UpdateNormalMode();
            }
        }

        if (_pending.HasValue)
            UpdatePendingLine();

        float step = Time.unscaledDeltaTime / FadeDuration;
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, step);
    }

    void UpdateConfirmMode()
    {
        if (_input.ConsoleEscape.WasPerformedThisFrame())
        {
            DismissConfirm(invokeNo: true);
            return;
        }
        if (_input.ConsoleSubmit.WasPerformedThisFrame())
        {
            if (_confirm == null) { _mode = InputMode.Normal; return; }
            if (_confirm.ActiveIsYes) _confirm.OnYes?.Invoke();
            else _confirm.OnNo?.Invoke();
            DismissConfirm(invokeNo: false);
            return;
        }
        if (_input.ConsoleTab.WasPerformedThisFrame()
            || _input.ConsoleCursorLeft.WasPerformedThisFrame()
            || _input.ConsoleCursorRight.WasPerformedThisFrame())
        {
            if (_confirm != null) _confirm.ActiveIsYes = !_confirm.ActiveIsYes;
        }
    }

    void UpdateNormalMode()
    {
        if (_input.ConsoleEscape.WasPerformedThisFrame())
        {
            if (PopupVisible)
                DismissSuggestions();
            else if (_inputBuffer.Length > 0)
            {
                _inputBuffer.Clear();
                ResetSuggestions();
                _history.ResetCursor();
                _draftBeforeHistory = null;
            }
            else
                Close();
        }

        if (_input.ConsoleSubmit.WasPerformedThisFrame())
            HandleSubmitKey();

        int bsTicks = _bsRepeat.Update(
            _input.ConsoleBackspace.WasPerformedThisFrame(),
            _input.ConsoleBackspace.IsPressed());
        for (int i = 0; i < bsTicks; i++) { _inputBuffer.Backspace(); OnInputMutated(); }

        int delTicks = _delRepeat.Update(
            _input.ConsoleDelete.WasPerformedThisFrame(),
            _input.ConsoleDelete.IsPressed());
        for (int i = 0; i < delTicks; i++) { _inputBuffer.Delete(); OnInputMutated(); }

        int leftTicks = _leftRepeat.Update(
            _input.ConsoleCursorLeft.WasPerformedThisFrame(),
            _input.ConsoleCursorLeft.IsPressed());
        for (int i = 0; i < leftTicks; i++) { _inputBuffer.MoveLeft(); _suggestionsSuppressed = true; }

        int rightTicks = _rightRepeat.Update(
            _input.ConsoleCursorRight.WasPerformedThisFrame(),
            _input.ConsoleCursorRight.IsPressed());
        for (int i = 0; i < rightTicks; i++) { _inputBuffer.MoveRight(); _suggestionsSuppressed = true; }

        if (_input.ConsoleCursorHome.WasPerformedThisFrame())
        {
            _inputBuffer.MoveHome();
            _suggestionsSuppressed = true;
        }
        if (_input.ConsoleCursorEnd.WasPerformedThisFrame())
        {
            _inputBuffer.MoveEnd();
            _suggestionsSuppressed = true;
        }

        if (_input.ConsoleTab.WasPerformedThisFrame())
        {
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (shift) HandleShiftTab(); else AcceptSuggestion();
        }

        // Ctrl+V — paste clipboard into input buffer at cursor. Strips control chars so
        // multi-line / tab content collapses to printable text only.
        if (Keyboard.current != null
            && Keyboard.current.vKey.wasPressedThisFrame
            && Keyboard.current.ctrlKey.isPressed)
        {
            PasteClipboard();
        }

        int downTicks = _downRepeat.Update(
            _input.ConsoleSuggestionNext.WasPerformedThisFrame(),
            _input.ConsoleSuggestionNext.IsPressed());
        for (int i = 0; i < downTicks; i++)
        {
            if (PopupVisible && _suggestions.Count > 0)
            {
                _suggestionsFrozen = true;
                _activeSuggestionIdx = (_activeSuggestionIdx + 1) % _suggestions.Count;
                UpdatePopupScroll();
            }
            else HistoryNext();
        }

        int upTicks = _upRepeat.Update(
            _input.ConsoleSuggestionPrev.WasPerformedThisFrame(),
            _input.ConsoleSuggestionPrev.IsPressed());
        for (int i = 0; i < upTicks; i++)
        {
            if (PopupVisible && _suggestions.Count > 0)
            {
                _suggestionsFrozen = true;
                _activeSuggestionIdx = (_activeSuggestionIdx - 1 + _suggestions.Count) % _suggestions.Count;
                UpdatePopupScroll();
            }
            else HistoryPrevious();
        }

        if (_input.ConsolePageUp.WasPerformedThisFrame())
            _scrollOffset = Mathf.Min(_scrollOffset + 5, Mathf.Max(0, _scrollback.Count - 1));
        if (_input.ConsolePageDown.WasPerformedThisFrame())
            _scrollOffset = Mathf.Max(0, _scrollOffset - 5);

        if (!_suggestionsFrozen && !_suggestionsSuppressed)
        {
            _suggestions = _intellisense.Update(_inputBuffer.Text);
            UpdatePopupScroll();
        }
    }

    void UpdatePopupScroll()
    {
        if (_suggestions == null || _suggestions.Count <= PopupVisibleCount)
        {
            _popupScrollOffset = 0;
            return;
        }
        const int halfWindow = PopupVisibleCount / 2;
        int total = _suggestions.Count;
        int active = Mathf.Clamp(_activeSuggestionIdx, 0, total - 1);
        if (active < halfWindow)
            _popupScrollOffset = 0;
        else if (active >= total - (PopupVisibleCount - halfWindow))
            _popupScrollOffset = total - PopupVisibleCount;
        else
            _popupScrollOffset = active - halfWindow;
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
        if (!_historyLoaded) { _history.Load(); _historyLoaded = true; }
        if (_input != null) { _input.DisableGameplay(); _input.EnableConsole(); }
        HookTextInput();
        EventBus<ConsoleOpenedEvent>.Raise(new ConsoleOpenedEvent());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _targetAlpha = 0f;
        if (_input != null) { _input.DisableConsole(); _input.EnableGameplay(); }
        UnhookTextInput();
        if (_historyLoaded) _history.Save();
        EventBus<ConsoleClosedEvent>.Raise(new ConsoleClosedEvent());
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    // Popup is visible whenever the renderer would draw it — which is whenever there are
    // suggestions AND ghost completion is NOT taking over the visual. Keeping this in lock-step
    // with ShouldShowGhost avoids the "Enter submits raw text instead of accepting the popup"
    // bug for single-substring-match cases like 'sun' → 'atmosphere.sun-intensity'.
    bool PopupVisible => _suggestions.Count > 0 && !ShouldShowGhost(_inputBuffer.Text);

    /// <summary>
    /// Enter behavior: if there's an active suggestion that differs from what's typed, accept it
    /// (popup OR ghost — same rule). If the active suggestion exactly matches typed text, submit
    /// (already complete, no work to do). If there are no suggestions, submit raw (which will
    /// produce "unknown command" if it's not a valid alias). Bryan's mental model:
    /// "if it's not in the list, it's not a valid command" — so submitting raw is only meaningful
    /// when intellisense couldn't find ANY match.
    /// </summary>
    void HandleSubmitKey()
    {
        if (_suggestions.Count == 0)
        {
            SubmitInputLine();
            return;
        }

        int idx = Mathf.Clamp(_activeSuggestionIdx, 0, _suggestions.Count - 1);
        string completion = _suggestions[idx].CompletionText;
        if (completion.Equals(_inputBuffer.Text, System.StringComparison.OrdinalIgnoreCase))
        {
            // Typed text already equals the suggestion — Enter submits, no accept dance needed.
            SubmitInputLine();
        }
        else
        {
            AcceptSuggestion();
        }
    }

    void HandleShiftTab()
    {
        if (_suggestions.Count == 0) return;
        _suggestionsFrozen = true;
        _activeSuggestionIdx = (_activeSuggestionIdx - 1 + _suggestions.Count) % _suggestions.Count;
    }

    void AcceptSuggestion()
    {
        if (_suggestions.Count == 0) return;
        // Auto-append a space so the next keystroke starts an argument naturally.
        // Trailing whitespace is harmless on submit (string.IsNullOrWhiteSpace check) and
        // the tokenizer ignores it, so this is safe even for zero-arg commands.
        _inputBuffer.Set(_suggestions[_activeSuggestionIdx].CompletionText + " ");
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
        _history.ResetCursor();
        _draftBeforeHistory = null;
    }

    void DismissSuggestions()
    {
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    void ResetSuggestions()
    {
        _suggestionsFrozen = false;
        _activeSuggestionIdx = 0;
        _suggestionsSuppressed = false;
    }

    void OnInputMutated()
    {
        ResetSuggestions();
        _history.ResetCursor();
        _draftBeforeHistory = null;
    }

    void HistoryPrevious()
    {
        if (_history.Count == 0) return;
        if (!_history.IsNavigating) _draftBeforeHistory = _inputBuffer.Text;
        string entry = _history.Previous();
        if (entry == null) return;
        _inputBuffer.Set(entry);
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    void HistoryNext()
    {
        if (!_history.IsNavigating) return;
        string entry = _history.Next();
        if (string.IsNullOrEmpty(entry))
        {
            _inputBuffer.Set(_draftBeforeHistory ?? "");
            _draftBeforeHistory = null;
        }
        else
        {
            _inputBuffer.Set(entry);
        }
        _suggestionsSuppressed = true;
        _suggestions = System.Array.Empty<Suggestion>();
    }

    void PasteClipboard()
    {
        string clipboard = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(clipboard)) return;

        var sb = new System.Text.StringBuilder(clipboard.Length);
        foreach (char c in clipboard)
        {
            if (c >= 0x20 && c != 0x7F) sb.Append(c);
        }
        if (sb.Length == 0) return;

        _inputBuffer.Insert(sb.ToString());
        OnInputMutated();
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
        if (_mode == InputMode.Confirm) return;
        if (c == '`' || c == '~') return;
        if (c < 0x20 || c == 0x7F) return;
        _inputBuffer.Insert(c);
        OnInputMutated();
    }

    void SubmitInputLine()
    {
        string line = _inputBuffer.Text;
        if (string.IsNullOrWhiteSpace(line))
        {
            _inputBuffer.Clear();
            ResetSuggestions();
            _suggestions = System.Array.Empty<Suggestion>();
            return;
        }

        bool isBypass = _pending.HasValue && IsBypassPendingCommand(line);

        // While async is pending, only `console.abandon` and `console.cancel` get through.
        // Everything else is rejected with a hint so the user knows what their options are.
        if (_pending.HasValue && !isBypass)
        {
            float elapsed = Time.unscaledTime - _pending.Value.StartTime;
            _scrollback.Append(
                $"'{_pending.Value.Alias}' running ({elapsed:F1}s) — wait, or run 'console.abandon' / 'console.cancel'",
                ConsoleMessageType.Warning);
            return;
        }

        _inputBuffer.Clear();
        ResetSuggestions();
        _suggestions = System.Array.Empty<Suggestion>();
        _scrollOffset = 0;

        _scrollback.Append($"> {line}", ConsoleMessageType.Input);
        _history.Add(line);
        _draftBeforeHistory = null;

        if (isBypass)
        {
            // Preserve _pendingCts — it belongs to the async being abandoned/cancelled.
            // Bypass commands are sync and don't take a CancellationToken.
            CommandExecutor.Execute(line, this, CancellationToken.None);
            return;
        }

        ExecuteWithOwnedCancellation(line);
    }

    void ExecuteWithOwnedCancellation(string commandLine)
    {
        var cts = new CancellationTokenSource();
        _pendingCts = cts;
        try
        {
            CommandExecutor.Execute(commandLine, this, cts.Token);
        }
        finally
        {
            if (!_pending.HasValue)
            {
                _pendingCts = null;
                cts.Dispose();
            }
            // else: cts was passed to ObservePending as a parameter; it owns disposal.
        }
    }

    static bool IsBypassPendingCommand(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var tokens = CommandParser.Tokenize(line);
        if (tokens.Count == 0) return false;
        return tokens[0].Equals("console.abandon", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Equals("console.cancel", StringComparison.OrdinalIgnoreCase);
    }

    public void RunCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;

        bool isBypass = _pending.HasValue && IsBypassPendingCommand(commandLine);
        if (_pending.HasValue && !isBypass)
        {
            float elapsed = Time.unscaledTime - _pending.Value.StartTime;
            _scrollback.Append(
                $"'{_pending.Value.Alias}' running ({elapsed:F1}s) - reject '{commandLine}'",
                ConsoleMessageType.Warning);
            return;
        }

        _scrollback.Append($"> {commandLine}", ConsoleMessageType.Input);
        if (isBypass)
        {
            CommandExecutor.Execute(commandLine, this, CancellationToken.None);
            return;
        }

        ExecuteWithOwnedCancellation(commandLine);
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

    // --- Async pending tracking --------------------------------------------

    public void BeginAsync(string alias, object awaitable, bool isCancellable)
    {
        if (awaitable == null) return;
        if (_pending.HasValue)
        {
            float elapsed = Time.unscaledTime - _pending.Value.StartTime;
            _scrollback.Append(
                $"'{_pending.Value.Alias}' running ({elapsed:F1}s) — reject '{alias}'",
                ConsoleMessageType.Warning);
            return;
        }

        long id = _scrollback.Append($"running {alias} ... (0.0s)", ConsoleMessageType.Log);
        _pending = new PendingAsync
        {
            Alias = alias,
            Awaitable = awaitable,
            StartTime = Time.unscaledTime,
            LineId = id,
            IsCancellable = isCancellable,
        };
        _spinnerLastUpdate = Time.unscaledTime;
        var cts = _pendingCts;  // captured by ObservePending
        _ = ObservePending(_pending.Value, cts);
    }

    public void AbandonPending()
    {
        if (!_pending.HasValue)
        {
            _scrollback.Append("nothing to abandon", ConsoleMessageType.Warning);
            return;
        }
        var p = _pending.Value;
        float elapsed = Time.unscaledTime - p.StartTime;
        _scrollback.Replace(p.LineId, $"{p.Alias} abandoned ({elapsed:F2}s)", ConsoleMessageType.Warning);
        _pending = null;
        // Detach our reference; the observer still holds the CTS via its closure and
        // will dispose it when the underlying awaitable eventually finishes.
        _pendingCts = null;
    }

    public void RequestCancelPending()
    {
        if (!_pending.HasValue)
        {
            _scrollback.Append("nothing to cancel", ConsoleMessageType.Warning);
            return;
        }
        var p = _pending.Value;
        if (!p.IsCancellable)
        {
            _scrollback.Append(
                $"'{p.Alias}' does not support cancellation (no CancellationToken parameter) — use 'console.abandon' instead",
                ConsoleMessageType.Warning);
            return;
        }
        ShowConfirm(
            $"Cancel '{p.Alias}'?",
            onYes: () =>
            {
                if (_pendingCts != null && !_pendingCts.IsCancellationRequested)
                    _pendingCts.Cancel();
                else
                    _scrollback.Append("cancellation already requested", ConsoleMessageType.Warning);
            });
    }

    /// <inheritdoc/>
    public void Confirm(string question, Action onYes, Action onNo = null)
        => ShowConfirm(question, onYes, onNo);

    /// <inheritdoc/>
    public ConsoleDiagnostics GetDiagnostics()
    {
        string pendingAlias = _pending?.Alias;
        float elapsed = _pending.HasValue ? Time.unscaledTime - _pending.Value.StartTime : 0f;
        bool cancellable = _pending?.IsCancellable ?? false;
        return new ConsoleDiagnostics(
            _isOpen,
            Anchor,
            _scrollback.Count,
            _scrollback.Capacity,
            _history.Count,
            ConsoleRegistry.Commands.Count,
            pendingAlias,
            elapsed,
            cancellable);
    }

    void ShowConfirm(string question, Action onYes, Action onNo = null)
    {
        _confirm = new ConfirmContext
        {
            Question = question,
            OnYes = onYes,
            OnNo = onNo,
            ActiveIsYes = false,   // safe default — Enter without toggle = No
        };
        _mode = InputMode.Confirm;
    }

    void DismissConfirm(bool invokeNo)
    {
        if (_confirm == null) { _mode = InputMode.Normal; return; }
        if (invokeNo) _confirm.OnNo?.Invoke();
        _confirm = null;
        _mode = InputMode.Normal;
    }

    void UpdatePendingLine()
    {
        if (!_pending.HasValue) return;
        if (Time.unscaledTime - _spinnerLastUpdate < SpinnerUpdateInterval) return;
        _spinnerLastUpdate = Time.unscaledTime;

        var p = _pending.Value;
        float elapsed = Time.unscaledTime - p.StartTime;
        string dots = CurrentDotPhase();
        _scrollback.Replace(p.LineId, $"running {p.Alias} {dots} ({elapsed:F1}s)", ConsoleMessageType.Log);
    }

    static string CurrentDotPhase()
    {
        int phase = Mathf.FloorToInt(Time.unscaledTime / (SpinnerDotPeriod / 4f)) % 4;
        return phase switch
        {
            0 => "   ",
            1 => ".  ",
            2 => ".. ",
            _ => "...",
        };
    }

    async Awaitable ObservePending(PendingAsync p, CancellationTokenSource cts)
    {
        object result = null;
        string error = null;
        bool cancelled = false;

        try
        {
            result = await AwaitGeneric(p.Awaitable);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex.Message ?? ex.GetType().Name;
        }

        bool abandoned = !_pending.HasValue || _pending.Value.LineId != p.LineId;

        // this != null: Unity null check guards against MonoBehaviour destruction.
        // abandoned: true if _pending was cleared (OnDestroy, user abandon, etc.).
        if (this != null && !abandoned)
        {
            float elapsed = Time.unscaledTime - p.StartTime;
            if (cancelled)
            {
                _scrollback.Replace(p.LineId, $"{p.Alias} cancelled ({elapsed:F2}s)", ConsoleMessageType.Warning);
            }
            else if (error != null)
            {
                _scrollback.Replace(p.LineId, $"{p.Alias}: {error} ({elapsed:F2}s)", ConsoleMessageType.Error);
            }
            else
            {
                _scrollback.Replace(p.LineId, $"{p.Alias} completed in {elapsed:F2}s", ConsoleMessageType.Output);
                if (result != null) _scrollback.AppendText(result.ToString(), ConsoleMessageType.Output);
            }
            _pending = null;
            _pendingCts = null;
        }

        cts?.Dispose();
    }

    static async Awaitable<object> AwaitGeneric(object awaitableObj)
    {
        if (awaitableObj is Awaitable nonGeneric)
        {
            await nonGeneric;
            return null;
        }

        // Registers a continuation via UnsafeOnCompleted so the Awaitable<T>'s internal
        // scheduler drives completion — no per-frame reflection polling.
        // Polling IsCompleted via reflection without a registered continuation may never
        // see it become true, because Unity's Awaitable<T> only updates that state after
        // a continuation is attached.
        var type = awaitableObj.GetType();
        var getAwaiter = type.GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance);
        if (getAwaiter == null) throw new InvalidOperationException($"{type.Name} has no GetAwaiter()");
        object awaiter = getAwaiter.Invoke(awaitableObj, null);
        var awaiterType = awaiter.GetType();
        var getResult = awaiterType.GetMethod("GetResult");
        var unsafeOnCompleted = awaiterType.GetMethod("UnsafeOnCompleted")
                             ?? awaiterType.GetMethod("OnCompleted");
        if (getResult == null || unsafeOnCompleted == null)
            throw new InvalidOperationException($"{awaiterType.Name} is not a valid awaiter");

        bool done = false;
        object result = null;
        Exception caught = null;
        Action continuation = () =>
        {
            try { result = getResult.Invoke(awaiter, null); }
            catch (TargetInvocationException tex) { caught = tex.InnerException ?? tex; }
            catch (Exception ex) { caught = ex; }
            done = true;
        };
        unsafeOnCompleted.Invoke(awaiter, new object[] { continuation });

        while (!done)
            await Awaitable.NextFrameAsync();

        if (caught != null) throw caught;
        return result;
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
        string typed = _inputBuffer.Text;
        int cursorPos = _inputBuffer.CursorPos;

        // Track whether new messages have arrived while the user is scrolled back.
        // _tailScrollbackVersion is refreshed every frame at offset 0, so the first
        // frame after scrolling uses the version that was current just before scrolling.
        bool hasNewMessages;
        if (_scrollOffset == 0)
        {
            _tailScrollbackVersion = _scrollback.Version;
            hasNewMessages = false;
        }
        else
        {
            hasNewMessages = _scrollback.Version > _tailScrollbackVersion;
        }

        bool ghostActive = ShouldShowGhost(typed);
        IReadOnlyList<Suggestion> popupSuggestions =
            (_mode == InputMode.Confirm || ghostActive)
                ? System.Array.Empty<Suggestion>()
                : _suggestions;

        string pendingDots = _pending.HasValue ? CurrentDotPhase() : null;
        List<TextSpan> inputSpans = _inputFormatter.Build(
            _renderer.Theme, typed, cursorPos, cursorOn,
            _suggestions, ghostActive, _pending.HasValue, pendingDots);

        var cmd = new CommandBuffer { name = "ConsoleOverlay" };
        try
        {
            ConsoleRenderer.ConfirmRenderData? confirmData = null;
            if (_mode == InputMode.Confirm && _confirm != null)
                confirmData = new ConsoleRenderer.ConfirmRenderData
                {
                    Question = _confirm.Question,
                    ActiveIsYes = _confirm.ActiveIsYes,
                };

            var state = new ConsoleRenderer.ConsoleRenderState
            {
                Alpha = _currentAlpha,
                Anchor = Anchor,
                InputSpans = inputSpans,
                Scrollback = _scrollback,
                ScrollOffset = _scrollOffset,
                Suggestions = popupSuggestions,
                ActiveSuggestion = _activeSuggestionIdx,
                PopupScrollOffset = _popupScrollOffset,
                PopupVisibleCount = PopupVisibleCount,
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

    /// <summary>
    /// Ghost completion fires only when there's a single suggestion that is a true PREFIX
    /// of what's typed. Substring-only matches (e.g. "enum" → "test.console.enum") flow
    /// to the popup instead so the user can see the system is suggesting something distant.
    /// </summary>
    bool ShouldShowGhost(string typed)
    {
        if (_suggestions.Count != 1) return false;
        if (_suggestionsFrozen || _suggestionsSuppressed) return false;
        string completion = _suggestions[0].CompletionText;
        return completion.Length > typed.Length
            && completion.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
    }
}
