/// <summary>Marker interface for events that carry normalized progress (0–1) and a status message.</summary>
public interface IProgressEvent : IGameEvent
{
    float Progress { get; }
    string Message { get; }
}