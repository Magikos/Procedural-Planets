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
