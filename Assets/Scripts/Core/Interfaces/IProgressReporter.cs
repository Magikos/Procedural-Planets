/// <summary>
/// Opt-in interface for components that report meaningful initialization progress.
/// <see cref="LoadingManager"/> scans the scene for this interface before running
/// <see cref="IEarlyInitialize"/> and <see cref="ILateInitialize"/> phases, registering each
/// reporter's handle with a <see cref="ProgressTracker"/> for weighted-average aggregation.
///
/// The same <see cref="ProgressHandle"/> instance is valid across both Early and Late phases,
/// so a component that spans both can report continuously from 0 to 1 without coordination.
///
/// Components that have nothing meaningful to report simply don't implement this interface —
/// <see cref="IEarlyInitialize"/> and <see cref="ILateInitialize"/> are unaffected.
/// </summary>
public interface IProgressReporter
{
    /// <summary>Human-readable name used in diagnostics and progress messages.</summary>
    string ReporterName { get; }

    /// <summary>
    /// Number of steps this reporter contributes to the overall bar. Default is 1.
    /// Set this to the number of meaningful milestones you report so the bar is
    /// divided proportionally without requiring knowledge of other reporters.
    /// </summary>
    int StepCount => 1;

    /// <summary>
    /// The reporter's stable progress handle. Must always return the same instance — the
    /// <see cref="ProgressTracker"/> holds a reference to it across the entire initialization pass.
    /// </summary>
    IProgressHandle ProgressHandle { get; }
}
