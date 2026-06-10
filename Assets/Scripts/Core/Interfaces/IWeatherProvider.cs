using UnityEngine;

public enum WeatherCellState
{
    Clear,
    Cloudy,
    Storm
}

public enum WindPreset
{
    Calm,
    Breeze,
    Windy,
    Gale,
    Storm,
}

public static class WeatherUnits
{
    public const float ReferenceStormWindMetersPerSecond = 25f;

    public static float WindStrength01(float metersPerSecond)
    {
        return Mathf.Clamp01(metersPerSecond / ReferenceStormWindMetersPerSecond);
    }

    public static float WindSpeedForPreset(WindPreset preset)
    {
        return preset switch
        {
            WindPreset.Calm => 1f,
            WindPreset.Breeze => 4f,
            WindPreset.Windy => 9f,
            WindPreset.Gale => 17f,
            WindPreset.Storm => 25f,
            _ => 4f,
        };
    }
}

public readonly struct WeatherSample
{
    public readonly float CloudCoverage;
    public readonly float StormIntensity;
    public readonly float Precipitation;
    public readonly float TemperatureCelsius;
    public readonly float MoistureSource;
    public readonly WeatherCellState State;
    public float Temperature => TemperatureCelsius;

    public WeatherSample(
        float cloudCoverage,
        float stormIntensity,
        float precipitation,
        float temperatureCelsius,
        float moistureSource,
        WeatherCellState state)
    {
        CloudCoverage = cloudCoverage;
        StormIntensity = stormIntensity;
        Precipitation = precipitation;
        TemperatureCelsius = temperatureCelsius;
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
    float WindSpeedMetersPerSecond { get; }
    float WindStrength01 { get; }

    bool TryFindStrongestPrecipitation(out Vector3 worldPosition, out WeatherSample sample);
    WeatherSample SampleWeather(Vector3 worldPosition);
    float GetCloudCoverage(Vector3 worldPosition);
    float GetPrecipitation(Vector3 worldPosition);
    float GetStormIntensity(Vector3 worldPosition);
    // Physical air temperature in degrees Celsius.
    float GetTemperature(Vector3 worldPosition);
}
