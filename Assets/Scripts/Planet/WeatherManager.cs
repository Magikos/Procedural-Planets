using UnityEngine;

/// <summary>
/// Owns planet-scale weather state. Current implementation generates a static
/// cube-sphere condensation grid; later phases will advect and evolve the same data.
/// </summary>
public class WeatherManager : MonoBehaviour, IWeatherProvider
{
    [Header("References")]
    public CloudSettings Settings;

    [Header("Wind")]
    public Vector3 WindDir = new Vector3(1f, 0f, 0.3f);
    [Range(0f, 5f)] public float Speed = 0.5f;

    [Header("Precipitation (future)")]
    [Range(0f, 1f)] public float Precipitation = 0f;

    SphericalWeatherGrid _grid;
    Vector3 _planetCenter;
    float _seaLevelRadius;
    Quaternion _weatherVisualRotation = Quaternion.identity;
    ILogger _logger;

    static readonly int _windDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int _windSpeedId = Shader.PropertyToID("_WindSpeed");
    static readonly int _cloudWeatherRotationId = Shader.PropertyToID("_CloudWeatherRotation");

    public Vector3 WindDirection => WindDir.sqrMagnitude > 0.0001f ? WindDir.normalized : Vector3.right;
    public float WindSpeed => Speed;
    public Texture2DArray WeatherTexture => _grid != null ? _grid.Texture : null;
    public int WeatherResolution => _grid != null ? _grid.Resolution : 0;
    public bool HasWeatherGrid => _grid != null;

    ILogger Logger
    {
        get
        {
            if (_logger == null && !ServiceLocator.TryGet(out _logger))
                _logger = new UnityLogger();
            return _logger;
        }
    }

    void Awake()
    {
        ServiceLocator.Register<IWeatherProvider>(this);
        Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.identity);
    }

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);

    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void OnDestroy()
    {
        _grid?.Dispose();
        _grid = null;
    }

    void Update()
    {
        UpdateWeatherAdvection();
        Shader.SetGlobalVector(_windDirectionId, WindDirection);
        Shader.SetGlobalFloat(_windSpeedId, WindSpeed);
    }

    public void Configure(CloudSettings settings)
    {
        Settings = settings;
        if (_seaLevelRadius > 0f)
            GenerateWeatherGrid();
    }

    public void RegenerateWeatherGrid()
    {
        if (_seaLevelRadius > 0f)
            GenerateWeatherGrid();
    }

    public float GetCloudCoverage(Vector3 worldPosition)
    {
        if (_grid == null)
            return Settings != null ? Settings.InitialCoverage : 0f;

        return _grid.GetCondensation(worldPosition, _planetCenter, SampleWeatherRotation);
    }

    public float GetPrecipitation(Vector3 worldPosition)
    {
        return Precipitation * GetStormIntensity(worldPosition);
    }

    public float GetStormIntensity(Vector3 worldPosition)
    {
        return _grid != null ? _grid.GetStorm(worldPosition, _planetCenter, SampleWeatherRotation) : 0f;
    }

    public float GetTemperature(Vector3 worldPosition) => 0.5f;

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        if (Settings != null)
            GenerateWeatherGrid();
    }

    void GenerateWeatherGrid()
    {
        if (Settings == null) return;

        int seed = 12345;
        if (ServiceLocator.TryGet<ISeedProvider>(out var seedProvider))
            seed = seedProvider.GetSeedForSystem("Weather");

        _grid?.Dispose();
        _grid = SphericalWeatherGrid.Generate(Settings, seed);
        _weatherVisualRotation = Quaternion.identity;
        UploadWeatherAdvection();
        Logger.Log(LogLevel.Debug, "Weather", $"Generated {WeatherResolution}x{WeatherResolution}x6 condensation grid.");
    }

    Quaternion SampleWeatherRotation => Quaternion.Inverse(_weatherVisualRotation);

    void UpdateWeatherAdvection()
    {
        if (_grid == null || Settings == null)
        {
            Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.identity);
            return;
        }

        float degrees = Settings.FrontAdvectionDegreesPerSecond * WindSpeed * Time.deltaTime;
        if (degrees > 0f)
        {
            Vector3 axis = GetAdvectionAxis(WindDirection);
            _weatherVisualRotation = Quaternion.AngleAxis(degrees, axis) * _weatherVisualRotation;
            _weatherVisualRotation = Normalize(_weatherVisualRotation);
        }

        UploadWeatherAdvection();
    }

    void UploadWeatherAdvection()
    {
        Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.Rotate(SampleWeatherRotation));
    }

    static Vector3 GetAdvectionAxis(Vector3 windDirection)
    {
        Vector3 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector3.right;
        Vector3 referenceNormal = Mathf.Abs(Vector3.Dot(wind, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
        Vector3 axis = Vector3.Cross(referenceNormal, wind);
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
    }

    static Quaternion Normalize(Quaternion rotation)
    {
        float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y
            + rotation.z * rotation.z + rotation.w * rotation.w);
        if (magnitude <= 0.000001f)
            return Quaternion.identity;

        float invMagnitude = 1f / magnitude;
        return new Quaternion(rotation.x * invMagnitude, rotation.y * invMagnitude,
            rotation.z * invMagnitude, rotation.w * invMagnitude);
    }
}
