using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Owns the console's input-editing state and per-frame key handling: the text buffer,
// intellisense suggestions + popup navigation, command history, held-key repeat, the
// scrollback view offset, and confirm-prompt mode. The owning ConsoleController drives
// it via Tick() while the console is open and reads the render-facing accessors from its
// OnEndCameraRendering pass. Submission is delegated to ConsoleAsyncRunner; closing the
// console (Escape on an empty line) is delegated back via the requestClose callback.
public sealed class ConsoleInputController
{
    const float RepeatInitialDelay = 0.40f;
    const float RepeatInterval = 0.05f;
    public const int PopupVisibleCount = 8;

    enum InputMode { Normal, Confirm }

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

    readonly ConsoleScrollback _scrollback;
    readonly ConsoleAsyncRunner _runner;
    readonly Action _requestClose;

    readonly ConsoleInputBuffer _inputBuffer = new();
    readonly IntellisenseEngine _intellisense = new();
    readonly ConsoleHistory _history = new();
    IReadOnlyList<Suggestion> _suggestions = System.Array.Empty<Suggestion>();
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
    string _draftBeforeHistory;
    InputMode _mode = InputMode.Normal;
    ConfirmContext _confirm;
    bool _historyLoaded;
    bool _textInputHooked;
    bool _active;

    public ConsoleInputController(ConsoleScrollback scrollback, ConsoleAsyncRunner runner, Action requestClose)
    {
        _scrollback = scrollback;
        _runner = runner;
        _requestClose = requestClose;
    }

    // --- Render-facing state ----------------------------------------------

    public string Text => _inputBuffer.Text;
    public int CursorPos => _inputBuffer.CursorPos;
    public IReadOnlyList<Suggestion> Suggestions => _suggestions;
    public int ActiveSuggestion => _activeSuggestionIdx;
    public int PopupScrollOffset => _popupScrollOffset;
    public int ScrollOffset => _scrollOffset;
    public bool GhostActive => ShouldShowGhost(_inputBuffer.Text);
    public bool IsConfirmMode => _mode == InputMode.Confirm;
    public bool HasConfirm => _confirm != null;
    public string ConfirmQuestion => _confirm?.Question;
    public bool ConfirmActiveIsYes => _confirm?.ActiveIsYes ?? false;
    public int HistoryCount => _history.Count;

    // --- Lifecycle --------------------------------------------------------

    public void NotifyOpened()
    {
        if (!_historyLoaded) { _history.Load(); _historyLoaded = true; }
        HookTextInput();
        _active = true;
    }

    public void NotifyClosed()
    {
        UnhookTextInput();
        if (_historyLoaded) _history.Save();
        _active = false;
    }

    public void UnhookTextInput()
    {
        if (!_textInputHooked) return;
        if (Keyboard.current != null)
            Keyboard.current.onTextInput -= OnTextInput;
        _textInputHooked = false;
    }

    public void CancelConfirmIfActive()
    {
        if (_mode == InputMode.Confirm) DismissConfirm(invokeNo: true);
    }

    public void ShowConfirm(string question, Action onYes, Action onNo = null)
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

    // --- Per-frame key handling -------------------------------------------

    public void Tick(IInputMapService input)
    {
        if (_mode == InputMode.Confirm)
            UpdateConfirmMode(input);
        else
            UpdateNormalMode(input);
    }

    void UpdateConfirmMode(IInputMapService input)
    {
        if (input.ConsoleEscape.WasPerformedThisFrame())
        {
            DismissConfirm(invokeNo: true);
            return;
        }
        if (input.ConsoleSubmit.WasPerformedThisFrame())
        {
            if (_confirm == null) { _mode = InputMode.Normal; return; }
            if (_confirm.ActiveIsYes) _confirm.OnYes?.Invoke();
            else _confirm.OnNo?.Invoke();
            DismissConfirm(invokeNo: false);
            return;
        }
        if (input.ConsoleTab.WasPerformedThisFrame()
            || input.ConsoleCursorLeft.WasPerformedThisFrame()
            || input.ConsoleCursorRight.WasPerformedThisFrame())
        {
            if (_confirm != null) _confirm.ActiveIsYes = !_confirm.ActiveIsYes;
        }
    }

    void UpdateNormalMode(IInputMapService input)
    {
        if (input.ConsoleEscape.WasPerformedThisFrame())
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
                _requestClose();
        }

        if (input.ConsoleSubmit.WasPerformedThisFrame())
            HandleSubmitKey();

        int bsTicks = _bsRepeat.Update(
            input.ConsoleBackspace.WasPerformedThisFrame(),
            input.ConsoleBackspace.IsPressed());
        for (int i = 0; i < bsTicks; i++) { _inputBuffer.Backspace(); OnInputMutated(); }

        int delTicks = _delRepeat.Update(
            input.ConsoleDelete.WasPerformedThisFrame(),
            input.ConsoleDelete.IsPressed());
        for (int i = 0; i < delTicks; i++) { _inputBuffer.Delete(); OnInputMutated(); }

        int leftTicks = _leftRepeat.Update(
            input.ConsoleCursorLeft.WasPerformedThisFrame(),
            input.ConsoleCursorLeft.IsPressed());
        for (int i = 0; i < leftTicks; i++) { _inputBuffer.MoveLeft(); _suggestionsSuppressed = true; }

        int rightTicks = _rightRepeat.Update(
            input.ConsoleCursorRight.WasPerformedThisFrame(),
            input.ConsoleCursorRight.IsPressed());
        for (int i = 0; i < rightTicks; i++) { _inputBuffer.MoveRight(); _suggestionsSuppressed = true; }

        if (input.ConsoleCursorHome.WasPerformedThisFrame())
        {
            _inputBuffer.MoveHome();
            _suggestionsSuppressed = true;
        }
        if (input.ConsoleCursorEnd.WasPerformedThisFrame())
        {
            _inputBuffer.MoveEnd();
            _suggestionsSuppressed = true;
        }

        if (input.ConsoleTab.WasPerformedThisFrame())
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
            input.ConsoleSuggestionNext.WasPerformedThisFrame(),
            input.ConsoleSuggestionNext.IsPressed());
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
            input.ConsoleSuggestionPrev.WasPerformedThisFrame(),
            input.ConsoleSuggestionPrev.IsPressed());
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

        if (input.ConsolePageUp.WasPerformedThisFrame())
            _scrollOffset = Mathf.Min(_scrollOffset + 5, Mathf.Max(0, _scrollback.Count - 1));
        if (input.ConsolePageDown.WasPerformedThisFrame())
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

    void OnTextInput(char c)
    {
        if (!_active) return;
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

        // Rejected while another async is pending — preserve the typed line so the user
        // can abandon/cancel and resubmit.
        if (!_runner.TryRunInteractive(line))
            return;

        _inputBuffer.Clear();
        ResetSuggestions();
        _suggestions = System.Array.Empty<Suggestion>();
        _scrollOffset = 0;
        _history.Add(line);
        _draftBeforeHistory = null;
    }

    void DismissConfirm(bool invokeNo)
    {
        if (_confirm == null) { _mode = InputMode.Normal; return; }
        if (invokeNo) _confirm.OnNo?.Invoke();
        _confirm = null;
        _mode = InputMode.Normal;
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
