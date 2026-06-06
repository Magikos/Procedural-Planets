using UnityEngine;

public enum WeatherCellState
{
    Clear,
    Cloudy,
    Storm
}

public readonly struct WeatherSample
{
    public readonly float CloudCoverage;
    public readonly float StormIntensity;
    public readonly float Precipitation;
    public readonly float Temperature;
    public readonly float MoistureSource;
    public readonly WeatherCellState State;

    public WeatherSample(
        float cloudCoverage,
        float stormIntensity,
        float precipitation,
        float temperature,
        float moistureSource,
        WeatherCellState state)
    {
        CloudCoverage = cloudCoverage;
        StormIntensity = stormIntensity;
        Precipitation = precipitation;
        Temperature = temperature;
        MoistureSource = moistureSource;
        State = state;
    }
}

/// <summary>
/// Provides weather state for systems that react to wind, clouds, precipitation,
/// and temperature. Callers should treat values as planet-position queries so
/// implementations can evolve from static grids to dynamic simulation.
/// </summary>
public interface IWeatherProvider
{
    // Normalized world-space direction the wind moves toward.
    Vector3 WindDirection { get; }
    float WindSpeed { get; }

    bool TryFindStrongestPrecipitation(out Vector3 worldPosition, out WeatherSample sample);
    WeatherSample SampleWeather(Vector3 worldPosition);
    float GetCloudCoverage(Vector3 worldPosition);
    float GetPrecipitation(Vector3 worldPosition);
    float GetStormIntensity(Vector3 worldPosition);
    float GetTemperature(Vector3 worldPosition);
}
