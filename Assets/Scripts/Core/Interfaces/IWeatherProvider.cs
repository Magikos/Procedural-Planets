using UnityEngine;

/// <summary>
/// Provides weather state for systems that react to wind, clouds, precipitation,
/// and temperature. Callers should treat values as planet-position queries so
/// implementations can evolve from static grids to dynamic simulation.
/// </summary>
public interface IWeatherProvider
{
    Vector3 WindDirection { get; }
    float WindSpeed { get; }

    float GetCloudCoverage(Vector3 worldPosition);
    float GetPrecipitation(Vector3 worldPosition);
    float GetStormIntensity(Vector3 worldPosition);
    float GetTemperature(Vector3 worldPosition);
}
