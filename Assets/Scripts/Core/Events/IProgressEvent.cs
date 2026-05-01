public interface IProgressEvent : IGameEvent
{
    float Progress { get; }
    string Message { get; }
}