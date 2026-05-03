using UnityEngine;

/// <summary>
/// Provides weather state for all systems (clouds, trees, grass, waves, particles).
/// 
/// CURRENT: Static values, uniform across the planet.
/// 
/// FUTURE PLANS:
/// - Position-based weather via a baked weather map texture on the sphere
/// - Weather state machine: Clear → Cloudy → Overcast → Storm → Clear
/// - Smooth transitions (lerp coverage/wind over minutes)
/// - Regional weather cells that move across the planet surface
/// - Biome influence (deserts rarely rain, tropics have frequent storms)
/// - Season influence (winter = snow, summer = thunderstorms)
/// - Weather map texture shared between C# queries and cloud shader
/// - Lightning/thunder system driven by StormIntensity
/// - Wet surface effects driven by Precipitation
/// - Fog density driven by weather state
/// </summary>
public interface IWeatherProvider
{
    // Global wind (used by shaders directly via globals)
    Vector3 WindDirection { get; }
    float WindSpeed { get; }

    // Position-based queries (for local gameplay systems)
    // Current stub returns uniform values; future implementation samples a weather map
    float GetCloudCoverage(Vector3 worldPosition);
    float GetPrecipitation(Vector3 worldPosition);
    float GetStormIntensity(Vector3 worldPosition);
    float GetTemperature(Vector3 worldPosition);
}
