using UnityEngine;

public struct WeatherLightningEvent : IGameEvent
{
    public Vector3 WorldPosition;
    public Vector3 Direction;
    public float Intensity;
    public float StormIntensity;
    public float Precipitation;
    public float Duration;
    public bool IsGroundStrike;

    public WeatherLightningEvent(
        Vector3 worldPosition,
        Vector3 direction,
        float intensity,
        float stormIntensity,
        float precipitation,
        float duration,
        bool isGroundStrike)
    {
        WorldPosition = worldPosition;
        Direction = direction;
        Intensity = intensity;
        StormIntensity = stormIntensity;
        Precipitation = precipitation;
        Duration = duration;
        IsGroundStrike = isGroundStrike;
    }
}
