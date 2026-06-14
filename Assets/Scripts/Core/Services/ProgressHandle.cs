using UnityEngine;

/// <summary>
/// Concrete <see cref="IProgressHandle"/> owned by an <see cref="IProgressReporter"/>.
/// Reporters store this as a readonly field and call <see cref="Report"/> at any point
/// during initialization. <see cref="ProgressTracker"/> subscribes to <see cref="OnReport"/>
/// to aggregate progress; all other members are public for cross-assembly use.
/// </summary>
public sealed class ProgressHandle : IProgressHandle
{
    public float CurrentProgress { get; private set; }
    public string CurrentMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Fired synchronously on the calling thread each time <see cref="Report"/> is called.
    /// Internal — only <see cref="ProgressTracker"/> subscribes.
    /// </summary>
    internal event System.Action<ProgressHandle> OnReport;

    public void Report(float progress, string message = "")
    {
        CurrentProgress = Mathf.Clamp01(progress);
        CurrentMessage = message ?? string.Empty;
        OnReport?.Invoke(this);
    }

    /// <summary>Resets state to zero. Called by <see cref="ProgressTracker"/> before each initialization pass.</summary>
    internal void Reset()
    {
        CurrentProgress = 0f;
        CurrentMessage = string.Empty;
    }
}

public sealed class ProgressRangeHandle : IProgressHandle
{
    readonly IProgressHandle _inner;
    readonly float _start;
    readonly float _length;

    public float CurrentProgress { get; private set; }
    public string CurrentMessage { get; private set; } = string.Empty;

    public ProgressRangeHandle(IProgressHandle inner, float start, float length)
    {
        _inner = inner;
        _start = Mathf.Clamp01(start);
        _length = Mathf.Clamp(length, 0f, 1f - _start);
    }

    public void Report(float progress, string message = "")
    {
        CurrentProgress = _start + Mathf.Clamp01(progress) * _length;
        CurrentMessage = message ?? string.Empty;
        _inner?.Report(CurrentProgress, CurrentMessage);
    }
}
