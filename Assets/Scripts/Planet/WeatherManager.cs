using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages weather state including cloud placement and wind.
/// Clouds are explicit instances at world positions — the weather system decides
/// where clouds form, how big they are, and how dense they are.
///
/// CURRENT: Static test clouds + wind movement.
///
/// FUTURE PLANS:
/// - Weather simulation spawns/grows/dissipates clouds over time
/// - Storm cells: clusters of dense clouds that produce rain/lightning
/// - Regional weather: different areas have different cloud patterns
/// - Biome influence: deserts = few clouds, tropics = frequent storms
/// - Weather map texture for spatial queries from shaders
/// - Cloud types: cumulus, stratus, cumulonimbus with different shapes
/// </summary>
public class WeatherManager : MonoBehaviour, IWeatherProvider
{
    [Header("Wind")]
    public Vector3 WindDir = new Vector3(1, 0, 0.3f);
    [Range(0f, 5f)] public float Speed = 0.5f;

    [Header("Test Clouds")]
    [Tooltip("Spawn test clouds on start for development")]
    public bool SpawnTestClouds = true;
    [Range(1, 50)] public int TestCloudCount = 10;
    [Range(50f, 500f)] public float TestCloudRadius = 300f;
    [Range(20f, 200f)] public float TestCloudThickness = 60f;
    [Range(0.5f, 3f)] public float TestCloudDensity = 1f;

    [Header("Precipitation (future)")]
    [Range(0f, 1f)] public float Precipitation = 0f;

    [Header("Storm (future)")]
    [Range(0f, 1f)] public float StormIntensity = 0f;

    // GPU data layout — must match shader struct
    public struct CloudInstance
    {
        public Vector3 Position;
        public float HorizontalRadius;
        public float VerticalThickness;
        public float Density;
        public float Padding1, Padding2; // pad to 32 bytes
    }

    List<CloudInstance> _clouds = new();
    float _planetRadius;
    Vector3 _planetCenter;

    static readonly int _windDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int _windSpeedId = Shader.PropertyToID("_WindSpeed");

    public Vector3 WindDirection => WindDir.normalized;
    public float WindSpeed => Speed;
    public List<CloudInstance> Clouds => _clouds;

    public float GetCloudCoverage(Vector3 worldPosition) => _clouds.Count > 0 ? 0.5f : 0f;
    public float GetPrecipitation(Vector3 worldPosition) => Precipitation;
    public float GetStormIntensity(Vector3 worldPosition) => StormIntensity;
    public float GetTemperature(Vector3 worldPosition) => 0.5f;

    void Awake()
    {
        ServiceLocator.Register<IWeatherProvider>(this);
    }

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _planetCenter = evt.PlanetCenter;

        if (SpawnTestClouds)
            GenerateTestClouds();
    }

    void Update()
    {
        Shader.SetGlobalVector(_windDirectionId, WindDirection);
        Shader.SetGlobalFloat(_windSpeedId, WindSpeed);

        MoveCloudsByWind();
    }

    void MoveCloudsByWind()
    {
        if (_planetRadius <= 0f || _clouds.Count == 0) return;

        float dt = Time.deltaTime;
        Vector3 windDelta = WindDirection * WindSpeed * dt * 10f;

        for (int i = 0; i < _clouds.Count; i++)
        {
            var cloud = _clouds[i];

            // Move cloud along the planet surface (project wind onto tangent plane)
            Vector3 surfaceNormal = (cloud.Position - _planetCenter).normalized;
            Vector3 tangentWind = Vector3.ProjectOnPlane(windDelta, surfaceNormal);
            cloud.Position += tangentWind;

            // Re-project onto cloud altitude sphere to keep at correct height
            float cloudAltitude = Vector3.Distance(cloud.Position, _planetCenter);
            float targetAltitude = _planetRadius * 1.02f;
            cloud.Position = _planetCenter + (cloud.Position - _planetCenter).normalized * targetAltitude;

            _clouds[i] = cloud;
        }
    }

    void GenerateTestClouds()
    {
        _clouds.Clear();

        if (ServiceLocator.TryGet<ISeedProvider>(out var seedProvider))
        {
            var rand = new System.Random(seedProvider.GetSeedForSystem("Weather"));
            float cloudAltitude = _planetRadius * 1.02f;

            for (int i = 0; i < TestCloudCount; i++)
            {
                // Random direction on unit sphere
                float z = (float)(rand.NextDouble() * 2.0 - 1.0);
                float theta = (float)(rand.NextDouble() * 2.0 * Mathf.PI);
                float r = Mathf.Sqrt(1f - z * z);
                Vector3 dir = new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);

                Vector3 pos = _planetCenter + dir * cloudAltitude;
                float radius = Mathf.Lerp(TestCloudRadius * 0.7f, TestCloudRadius * 1.5f, (float)rand.NextDouble());
                float thickness = Mathf.Lerp(TestCloudThickness * 0.7f, TestCloudThickness * 1.3f, (float)rand.NextDouble());
                float density = Mathf.Lerp(TestCloudDensity * 0.7f, TestCloudDensity * 1.3f, (float)rand.NextDouble());

                _clouds.Add(new CloudInstance
                {
                    Position = pos,
                    HorizontalRadius = radius,
                    VerticalThickness = thickness,
                    Density = density
                });
            }

            Debug.Log($"[WeatherManager] Spawned {_clouds.Count} test clouds at altitude {cloudAltitude:F0}");
        }
    }
}
