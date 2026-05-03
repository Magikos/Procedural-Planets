using UnityEngine;

/// <summary>
/// Stub weather system. Provides static, uniform weather across the planet.
/// Registers as IWeatherProvider in ServiceLocator so any system can access it.
/// Sets shader globals each frame for wind/coverage so shaders can read them directly.
///
/// FUTURE PLANS:
/// - Bake a 2D weather map texture mapped onto the sphere (coverage, precipitation per region)
/// - Weather state machine with smooth transitions between states
/// - Regional weather cells that drift across the planet driven by wind
/// - Integration with biome system (desert = low precipitation, tropical = high)
/// - Integration with CelestialManager (seasons affect weather patterns)
/// - Dynamic wind that shifts direction over time
/// - Storm events: coverage ramps up → rain → lightning/thunder → clears
/// - Shader reads _WeatherMap texture for spatial variation
/// - C# GetCloudCoverage(pos) samples the same weather map for consistency
/// </summary>
public class WeatherManager : MonoBehaviour, IWeatherProvider
{
    [Header("Wind")]
    [Tooltip("Wind direction on the planet surface (normalized at runtime)")]
    public Vector3 WindDir = new Vector3(1, 0, 0.3f);
    [Range(0f, 5f), Tooltip("0 = calm, 1 = normal breeze, 2+ = storm winds")]
    public float Speed = 0.5f;

    [Header("Clouds")]
    [Range(0f, 1f), Tooltip("0 = clear sky, 1 = fully overcast")]
    public float CloudCoverage = 0.5f;

    [Header("Precipitation (future)")]
    [Range(0f, 1f), Tooltip("0 = dry, 1 = heavy rain/snow")]
    public float Precipitation = 0f;

    [Header("Storm (future)")]
    [Range(0f, 1f), Tooltip("0 = none, 1 = full storm (lightning, thunder)")]
    public float StormIntensity = 0f;

    static readonly int _windDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int _windSpeedId = Shader.PropertyToID("_WindSpeed");
    static readonly int _cloudCoverageId = Shader.PropertyToID("_CloudCoverage");

    public Vector3 WindDirection => WindDir.normalized;
    public float WindSpeed => Speed;

    public float GetCloudCoverage(Vector3 worldPosition) => CloudCoverage;
    public float GetPrecipitation(Vector3 worldPosition) => Precipitation;
    public float GetStormIntensity(Vector3 worldPosition) => StormIntensity;
    public float GetTemperature(Vector3 worldPosition) => 0.5f;

    void Awake()
    {
        ServiceLocator.Register<IWeatherProvider>(this);
    }

    void Update()
    {
        Shader.SetGlobalVector(_windDirectionId, WindDirection);
        Shader.SetGlobalFloat(_windSpeedId, WindSpeed);
        Shader.SetGlobalFloat(_cloudCoverageId, CloudCoverage);
    }
}
