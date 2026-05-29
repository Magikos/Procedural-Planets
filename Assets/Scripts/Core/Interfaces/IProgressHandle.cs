/// <summary>
/// A per-reporter progress handle. Obtained from <see cref="IProgressReporter.ProgressHandle"/>.
/// The reporter calls <see cref="Report"/> at any point during Early or Late initialization;
/// <see cref="ProgressTracker"/> observes it and aggregates the overall scene progress.
/// </summary>
public interface IProgressHandle
{
    /// <summary>Reports normalized progress (0–1) with an optional status message.</summary>
    void Report(float progress, string message = "");

    /// <summary>Most recently reported progress value (0–1).</summary>
    float CurrentProgress { get; }

    /// <summary>Most recently reported status message.</summary>
    string CurrentMessage { get; }
}
