using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

// Owns command execution and the single in-flight async command: an owned
// CancellationTokenSource, the animated "running ..." scrollback line, and
// abandon/cancel handling. The console permits only one pending async at a time;
// everything here is built around that invariant.
public sealed class ConsoleAsyncRunner
{
    const float SpinnerUpdateInterval = 0.2f;
    const float SpinnerDotPeriod = 0.5f;

    struct PendingAsync
    {
        public string Alias;
        public object Awaitable;
        public float StartTime;
        public long LineId;
        public bool IsCancellable;
    }

    readonly ConsoleScrollback _scrollback;
    readonly IConsoleService _console;
    readonly Action<string, Action> _requestConfirm;

    PendingAsync? _pending;
    CancellationTokenSource _pendingCts;
    float _spinnerLastUpdate;
    bool _shutdown;

    public ConsoleAsyncRunner(
        ConsoleScrollback scrollback,
        IConsoleService console,
        Action<string, Action> requestConfirm)
    {
        _scrollback = scrollback;
        _console = console;
        _requestConfirm = requestConfirm;
    }

    public bool HasPending => _pending.HasValue;
    public string PendingAlias => _pending?.Alias;
    public float PendingElapsedSeconds => _pending.HasValue ? Time.unscaledTime - _pending.Value.StartTime : 0f;
    public bool PendingIsCancellable => _pending?.IsCancellable ?? false;
    public string PendingSpinnerDots => _pending.HasValue ? CurrentDotPhase() : null;

    /// <summary>
    /// Interactive (Enter-key) submission. Returns false when rejected because another async
    /// is still pending, so the caller can preserve the typed input line.
    /// </summary>
    public bool TryRunInteractive(string line)
    {
        if (RejectIfBusy(line, interactive: true)) return false;
        EchoAndExecute(line);
        return true;
    }

    /// <summary>Programmatic submission (IConsoleService.RunCommand).</summary>
    public void RunProgrammatic(string line)
    {
        if (RejectIfBusy(line, interactive: false)) return;
        EchoAndExecute(line);
    }

    public void Tick()
    {
        if (_pending.HasValue) UpdatePendingLine();
    }

    public void Shutdown()
    {
        // Null out _pending before anything else so any in-flight ObservePending sees
        // 'abandoned == true' (or _shutdown) and skips writing to dead scrollback state.
        _shutdown = true;
        _pending = null;
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = null;
    }

    bool RejectIfBusy(string line, bool interactive)
    {
        bool isBypass = _pending.HasValue && IsBypassPendingCommand(line);
        if (!_pending.HasValue || isBypass) return false;

        var p = _pending.Value;
        float elapsed = Time.unscaledTime - p.StartTime;
        string message = interactive
            ? $"'{p.Alias}' running ({elapsed:F1}s) — wait, or run 'console.abandon' / 'console.cancel'"
            : $"'{p.Alias}' running ({elapsed:F1}s) - reject '{line}'";
        _scrollback.Append(message, ConsoleMessageType.Warning);
        return true;
    }

    void EchoAndExecute(string line)
    {
        bool isBypass = _pending.HasValue && IsBypassPendingCommand(line);
        _scrollback.Append($"> {line}", ConsoleMessageType.Input);

        if (isBypass)
        {
            // Preserve _pendingCts — it belongs to the async being abandoned/cancelled.
            // Bypass commands are sync and don't take a CancellationToken.
            CommandExecutor.Execute(line, _console, CancellationToken.None);
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
            CommandExecutor.Execute(commandLine, _console, cts.Token);
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
        _requestConfirm(
            $"Cancel '{p.Alias}'?",
            () =>
            {
                if (_pendingCts != null && !_pendingCts.IsCancellationRequested)
                    _pendingCts.Cancel();
                else
                    _scrollback.Append("cancellation already requested", ConsoleMessageType.Warning);
            });
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

        // _shutdown: guards against writing to scrollback after the console is destroyed.
        // abandoned: true if _pending was cleared (shutdown, user abandon, etc.).
        if (!_shutdown && !abandoned)
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
}
