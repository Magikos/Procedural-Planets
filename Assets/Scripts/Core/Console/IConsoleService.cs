public interface IConsoleService
{
    bool IsOpen { get; }
    ConsoleAnchor Anchor { get; set; }

    void Open();
    void Close();
    void Toggle();

    void RunCommand(string commandLine);
    void Print(string text);
    void PrintLine(string text);
    void PrintWarning(string text);
    void PrintError(string text);
    void Clear();

    /// <summary>
    /// Begin tracking an in-flight async command (Awaitable or Awaitable&lt;T&gt;).
    /// The console displays an in-place spinner line with elapsed time and replaces it
    /// with a completion / error message when the awaitable resolves.
    /// <paramref name="isCancellable"/> is true when the underlying method accepts a
    /// <see cref="System.Threading.CancellationToken"/> parameter; <c>console.cancel</c>
    /// pre-checks this and refuses with a hint pointing at <c>console.abandon</c> otherwise.
    /// </summary>
    void BeginAsync(string alias, object awaitable, bool isCancellable);

    /// <summary>
    /// Stop tracking the pending async command. The Awaitable runs to completion in the
    /// background; the console no longer updates the spinner or reports the result.
    /// No-op if nothing is pending.
    /// </summary>
    void AbandonPending();

    /// <summary>
    /// Open a confirmation modal to cancel the pending async command. On confirm,
    /// the controller's <see cref="System.Threading.CancellationTokenSource"/> is signalled.
    /// Commands that accept a trailing <c>CancellationToken</c> parameter cooperatively bail.
    /// No-op if nothing is pending.
    /// </summary>
    void RequestCancelPending();

    /// <summary>
    /// Open a Yes/No confirmation modal. Default active option is "No" (safety).
    /// </summary>
    void Confirm(string question, System.Action onYes, System.Action onNo = null);

    /// <summary>Scrollback capacity in lines. Setting to a smaller value trims oldest entries.</summary>
    int ScrollbackCapacity { get; set; }

    /// <summary>Current scrollback lines, oldest first. For <c>console.dump</c> / external inspection.</summary>
    System.Collections.Generic.IReadOnlyList<ConsoleMessage> ScrollbackLines { get; }

    /// <summary>Snapshot of console state for F10 diagnostics / external inspection.</summary>
    ConsoleDiagnostics GetDiagnostics();
}

public readonly struct ConsoleDiagnostics
{
    public readonly bool IsOpen;
    public readonly ConsoleAnchor Anchor;
    public readonly int ScrollbackCount;
    public readonly int ScrollbackCapacity;
    public readonly int HistoryCount;
    public readonly int CommandsRegistered;
    public readonly string PendingAlias;
    public readonly float PendingElapsedSeconds;
    public readonly bool IsPendingCancellable;

    public ConsoleDiagnostics(
        bool isOpen,
        ConsoleAnchor anchor,
        int scrollbackCount,
        int scrollbackCapacity,
        int historyCount,
        int commandsRegistered,
        string pendingAlias,
        float pendingElapsedSeconds,
        bool isPendingCancellable)
    {
        IsOpen = isOpen;
        Anchor = anchor;
        ScrollbackCount = scrollbackCount;
        ScrollbackCapacity = scrollbackCapacity;
        HistoryCount = historyCount;
        CommandsRegistered = commandsRegistered;
        PendingAlias = pendingAlias;
        PendingElapsedSeconds = pendingElapsedSeconds;
        IsPendingCancellable = isPendingCancellable;
    }
}
