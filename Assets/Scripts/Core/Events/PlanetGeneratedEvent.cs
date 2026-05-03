using UnityEngine;

public struct PlanetGeneratedEvent : IGameEvent
{
    public Vector3 PlanetCenter;
    public float PlanetRadius;
    public float SeaLevelRadius;
    public float ElevationMin;
    public float ElevationMax;

    public PlanetGeneratedEvent(Vector3 center, float radius, float seaLevelRadius = 0f, float elevationMin = 0f, float elevationMax = 0f)
    {
        PlanetCenter = center;
        PlanetRadius = radius;
        SeaLevelRadius = seaLevelRadius;
        ElevationMin = elevationMin;
        ElevationMax = elevationMax;
    }
}

// FUTURE: Raised during planet generation to report progress.
// Will be used for loading screens / progress bars.
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