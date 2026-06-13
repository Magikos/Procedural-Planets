using System.Collections.Generic;

/// <summary>
/// Scoped to a single initialization pass. Collects <see cref="ProgressHandle"/> instances
/// from all registered <see cref="IProgressReporter"/> components, aggregates their weighted
/// progress into a single 0–1 value, and fires <see cref="ProgressEvent"/> on the
/// <see cref="EventBus{T}"/> each time any handle reports.
///
/// Created by <see cref="LoadingManager"/> at the start of each <c>InitializeAsync</c> call
/// and discarded when the pass completes — not a persistent singleton.
/// </summary>
internal sealed class ProgressTracker
{
    private readonly List<(IProgressReporter Reporter, ProgressHandle Handle)> _entries = new();
    private int _totalSteps;
    private string _lastMessage = string.Empty;

    /// <summary>
    /// Registers a reporter. Resets its handle to zero so stale progress from a previous
    /// initialization pass doesn't bleed into this one.
    /// </summary>
    internal void Register(IProgressReporter reporter)
    {
        if (reporter.ProgressHandle is not ProgressHandle handle)
            return;

        handle.Reset();
        handle.OnReport += OnHandleReport;
        _entries.Add((reporter, handle));
        _totalSteps += reporter.StepCount;
    }

    private void OnHandleReport(ProgressHandle changed)
    {
        if (!string.IsNullOrEmpty(changed.CurrentMessage))
            _lastMessage = changed.CurrentMessage;

        float overall = _totalSteps > 0 ? ComputeOverall() : 0f;
        EventBus<ProgressEvent>.Raise(new ProgressEvent(overall, _lastMessage));
    }

    private float ComputeOverall()
    {
        float sum = 0f;
        foreach (var (reporter, handle) in _entries)
            sum += handle.CurrentProgress * reporter.StepCount;
        return sum / _totalSteps;
    }

    /// <summary>
    /// Fires a definitive 100% <see cref="ProgressEvent"/>, then unsubscribes all handle
    /// listeners. The tracker is done — it no longer cares what the handles report.
    /// </summary>
    internal void Complete()
    {
        EventBus<ProgressEvent>.Raise(new ProgressEvent(1f, _lastMessage));
        Dispose();
    }

    internal void Abort()
    {
        Dispose();
    }

    private void Dispose()
    {
        foreach (var (_, handle) in _entries)
            handle.OnReport -= OnHandleReport;

        _entries.Clear();
        _totalSteps = 0;
    }
}
