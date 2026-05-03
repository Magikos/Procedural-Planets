// FUTURE: Progress reporting for long-running generation tasks.
// Will be used for loading screens / progress bars during planet generation.
public interface IProgressEvent : IGameEvent
{
    float Progress { get; }
    string Message { get; }
}