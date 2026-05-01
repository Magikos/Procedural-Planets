using UnityEngine;

public struct PlanetGeneratedEvent : IGameEvent
{
    public Vector3 PlanetCenter;
    public float PlanetRadius;

    public PlanetGeneratedEvent(Vector3 center, float radius)
    {
        PlanetCenter = center;
        PlanetRadius = radius;
    }
}

public struct PlanetGenerationProgressEvent : IProgressEvent
{
    public float Progress { get; private set; }
    public string Message { get; private set; }

    public PlanetGenerationProgressEvent(float progress, string message = "")
    {
        Progress = progress;
        Message = message;
    }
}