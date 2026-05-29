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
