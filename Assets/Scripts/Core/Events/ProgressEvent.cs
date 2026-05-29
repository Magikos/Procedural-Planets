/// <summary>
/// Raised by <see cref="ProgressTracker"/> to report aggregated initialization progress
/// across all <see cref="IProgressReporter"/> components in the current scene.
/// Subscribe via <see cref="EventBus{T}"/> to drive any progress UI.
/// </summary>
public struct ProgressEvent : IProgressEvent
{
    public float Progress { get; private set; }
    public string Message { get; private set; }

    public ProgressEvent(float progress, string message = "")
    {
        Progress = progress;
        Message = message;
    }
}
