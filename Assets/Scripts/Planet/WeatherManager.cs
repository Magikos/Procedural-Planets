using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owns planet-scale weather state. The CPU seeds the initial cube-sphere weather
/// grid, then runtime evolution is dispatched to a GPU ping-pong texture.
/// </summary>
public class WeatherManager : MonoBehaviour, IWeatherProvider
{
    [Header("References")]
    public CloudSettings Settings;
    public ComputeShader WeatherCompute;

    [Header("Wind")]
    public Vector3 WindDir = new Vector3(1f, 0f, 0.3f);
    [Range(0f, 5f)] public float Speed = 0.5f;

    [Header("Precipitation")]
    [Range(0f, 1f)] public float Precipitation = 1f;
    [Range(0f, 1f)] public float CloudyThreshold = 0.18f;
    [Range(0f, 1f)] public float PrecipitationStormThreshold = 0.25f;
    [Range(0.01f, 1f)] public float PrecipitationStormSoftness = 0.35f;

    [Header("Query Cache")]
    public bool EnableWeatherQueryCache = true;
    [Range(0.05f, 5f)] public float WeatherQueryCacheInterval = 0.5f;

    [Header("Diagnostics")]
    public bool ShowWeatherDiagnostics = false;
    [Range(0.25f, 10f)] public float WeatherDiagnosticsInterval = 2f;

    SphericalWeatherGrid _grid;
    Vector3 _planetCenter;
    float _seaLevelRadius;
    Quaternion _weatherVisualRotation = Quaternion.identity;
    float _evolutionAccumulator;
    bool _missingWeatherComputeLogged;
    int _evolutionDispatchCount;
    float _lastEvolutionDelta;
    float _lastEvolutionTime;
    bool _weatherDiagnosticsPending;
    bool _weatherDiagnosticsError;
    float _nextWeatherDiagnosticsTime;
    float _weatherAverageCondensation;
    float _weatherAverageStorm;
    float _weatherAveragePrecipitation;
    float _weatherAverageMoistureSource;
    float _weatherAverageCondensationChange;
    float _weatherMaxCondensationChange;
    float _weatherCondensingFraction;
    float _weatherDryingFraction;
    int _weatherDiagnosticsSamples;
    int _weatherDiagnosticsNextFace;
    int _weatherDiagnosticsLastFace = -1;
    bool _weatherQueryCachePending;
    bool _weatherQueryCacheError;
    float _nextWeatherQueryCacheTime;
    int _weatherQueryCacheNextFace;
    int _weatherQueryCacheLastFace = -1;
    int _weatherQueryCacheFaceMask;
    ILogger _logger;

    static readonly int _windDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int _windSpeedId = Shader.PropertyToID("_WindSpeed");
    static readonly int _cloudWeatherRotationId = Shader.PropertyToID("_CloudWeatherRotation");

    public Vector3 WindDirection => WindDir.sqrMagnitude > 0.0001f ? WindDir.normalized : Vector3.right;
    public float WindSpeed => Speed;
    public Texture WeatherTexture => _grid != null ? _grid.Texture : null;
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
        UpdateWeatherEvolution();
        UpdateWeatherQueryCache();
        UpdateWeatherDiagnostics();
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

    public WeatherSample SampleWeather(Vector3 worldPosition)
    {
        if (_grid == null)
        {
            float fallbackCoverage = Settings != null ? Settings.InitialCoverage : 0f;
            return new WeatherSample(fallbackCoverage, 0f, 0f, 0.5f, 0f,
                fallbackCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear);
        }

        _grid.GetWeatherCell(worldPosition, _planetCenter, SampleWeatherRotation,
            out float cloudCoverage, out float stormIntensity, out float moistureSource);

        float precipitation = CalculatePrecipitation(stormIntensity);

        WeatherCellState state = stormIntensity >= PrecipitationStormThreshold
            ? WeatherCellState.Storm
            : cloudCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear;

        return new WeatherSample(cloudCoverage, stormIntensity, precipitation, GetTemperature(worldPosition), moistureSource, state);
    }

    public float GetCloudCoverage(Vector3 worldPosition)
    {
        return SampleWeather(worldPosition).CloudCoverage;
    }

    public float GetPrecipitation(Vector3 worldPosition)
    {
        return SampleWeather(worldPosition).Precipitation;
    }

    public float GetStormIntensity(Vector3 worldPosition)
    {
        return SampleWeather(worldPosition).StormIntensity;
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
        _evolutionAccumulator = 0f;
        ResetWeatherDiagnostics();
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

    void UpdateWeatherEvolution()
    {
        if (_grid == null || Settings == null || !Settings.EnableWeatherEvolution)
        {
            _evolutionAccumulator = 0f;
            return;
        }

        if (WeatherCompute == null)
        {
            if (!_missingWeatherComputeLogged)
            {
                Logger.Log(LogLevel.Warning, "Weather", "WeatherCompute is not assigned; dynamic weather evolution is disabled.");
                _missingWeatherComputeLogged = true;
            }
            _evolutionAccumulator = 0f;
            return;
        }

        _evolutionAccumulator += Time.deltaTime;
        float interval = Mathf.Max(Settings.ActiveEvolutionInterval, 0.05f);
        if (_evolutionAccumulator < interval)
            return;

        float simulationDelta = Mathf.Min(_evolutionAccumulator, interval * 4f);
        _evolutionAccumulator = 0f;
        if (_grid.Advance(WeatherCompute, Settings, simulationDelta, _weatherVisualRotation))
        {
            _evolutionDispatchCount++;
            _lastEvolutionDelta = simulationDelta;
            _lastEvolutionTime = Time.time;
        }
    }

    void UpdateWeatherDiagnostics()
    {
        if (EnableWeatherQueryCache)
            return;

        if (!ShowWeatherDiagnostics || _grid == null || _grid.Texture == null)
            return;

        if (_weatherDiagnosticsPending || Time.unscaledTime < _nextWeatherDiagnosticsTime)
            return;

        int face = _weatherDiagnosticsNextFace;
        _weatherDiagnosticsNextFace = (_weatherDiagnosticsNextFace + 1) % 6;
        _weatherDiagnosticsLastFace = face;
        _weatherDiagnosticsPending = true;
        _weatherDiagnosticsError = false;
        _nextWeatherDiagnosticsTime = Time.unscaledTime + Mathf.Max(WeatherDiagnosticsInterval, 0.25f);
        AsyncGPUReadback.Request(_grid.Texture, 0,
            0, WeatherResolution,
            0, WeatherResolution,
            face, 1,
            TextureFormat.RGBAFloat,
            OnWeatherDiagnosticsReadback);
    }

    void UpdateWeatherQueryCache()
    {
        if (!EnableWeatherQueryCache || _grid == null || _grid.Texture == null)
            return;

        if (_weatherQueryCachePending || Time.unscaledTime < _nextWeatherQueryCacheTime)
            return;

        int face = _weatherQueryCacheNextFace;
        _weatherQueryCacheNextFace = (_weatherQueryCacheNextFace + 1) % 6;
        _weatherQueryCacheLastFace = face;
        _weatherQueryCachePending = true;
        _weatherQueryCacheError = false;
        _nextWeatherQueryCacheTime = Time.unscaledTime + Mathf.Max(WeatherQueryCacheInterval, 0.05f);
        AsyncGPUReadback.Request(_grid.Texture, 0,
            0, WeatherResolution,
            0, WeatherResolution,
            face, 1,
            TextureFormat.RGBAFloat,
            request => OnWeatherQueryCacheReadback(request, face));
    }

    void OnWeatherQueryCacheReadback(AsyncGPUReadbackRequest request, int face)
    {
        _weatherQueryCachePending = false;

        if (request.hasError)
        {
            _weatherQueryCacheError = true;
            return;
        }

        var data = request.GetData<Color>();
        _grid?.ApplyWeatherFaceReadback(face, data);
        _weatherQueryCacheFaceMask |= 1 << face;

        if (ShowWeatherDiagnostics)
        {
            _weatherDiagnosticsLastFace = face;
            UpdateWeatherDiagnosticsStats(data);
        }
    }

    void OnWeatherDiagnosticsReadback(AsyncGPUReadbackRequest request)
    {
        _weatherDiagnosticsPending = false;

        if (request.hasError)
        {
            _weatherDiagnosticsError = true;
            return;
        }

        UpdateWeatherDiagnosticsStats(request.GetData<Color>());
    }

    void UpdateWeatherDiagnosticsStats(Unity.Collections.NativeArray<Color> data)
    {
        int count = data.Length;
        if (count <= 0)
            return;

        double condensationSum = 0;
        double stormSum = 0;
        double precipitationSum = 0;
        double sourceSum = 0;
        double changeSum = 0;
        float maxChange = 0f;
        int condensing = 0;
        int drying = 0;

        for (int i = 0; i < count; i++)
        {
            Color pixel = data[i];
            float change = (pixel.a - 0.5f) / SphericalWeatherGrid.DeltaVisualizationScale;

            condensationSum += pixel.r;
            stormSum += pixel.g;
            precipitationSum += CalculatePrecipitation(pixel.g);
            sourceSum += pixel.b;
            changeSum += change;
            maxChange = Mathf.Max(maxChange, Mathf.Abs(change));

            if (change > 0.0001f)
                condensing++;
            else if (change < -0.0001f)
                drying++;
        }

        float invCount = 1f / count;
        _weatherAverageCondensation = (float)condensationSum * invCount;
        _weatherAverageStorm = (float)stormSum * invCount;
        _weatherAveragePrecipitation = (float)precipitationSum * invCount;
        _weatherAverageMoistureSource = (float)sourceSum * invCount;
        _weatherAverageCondensationChange = (float)changeSum * invCount;
        _weatherMaxCondensationChange = maxChange;
        _weatherCondensingFraction = condensing * invCount;
        _weatherDryingFraction = drying * invCount;
        _weatherDiagnosticsSamples = count;
    }

    void ResetWeatherDiagnostics()
    {
        _evolutionDispatchCount = 0;
        _lastEvolutionDelta = 0f;
        _lastEvolutionTime = 0f;
        _weatherDiagnosticsPending = false;
        _weatherDiagnosticsError = false;
        _nextWeatherDiagnosticsTime = 0f;
        _weatherAverageCondensation = 0f;
        _weatherAverageStorm = 0f;
        _weatherAveragePrecipitation = 0f;
        _weatherAverageMoistureSource = 0f;
        _weatherAverageCondensationChange = 0f;
        _weatherMaxCondensationChange = 0f;
        _weatherCondensingFraction = 0f;
        _weatherDryingFraction = 0f;
        _weatherDiagnosticsSamples = 0;
        _weatherDiagnosticsNextFace = 0;
        _weatherDiagnosticsLastFace = -1;
        _weatherQueryCachePending = false;
        _weatherQueryCacheError = false;
        _nextWeatherQueryCacheTime = 0f;
        _weatherQueryCacheNextFace = 0;
        _weatherQueryCacheLastFace = -1;
        _weatherQueryCacheFaceMask = 0;
    }

    void OnGUI()
    {
        if (!ShowWeatherDiagnostics)
            return;

        GUILayout.BeginArea(new Rect(10, 225, 430, 265), GUI.skin.box);
        GUILayout.Label("Weather Diagnostics");

        if (_grid == null)
        {
            GUILayout.Label("Grid: not generated");
            GUILayout.EndArea();
            return;
        }

        bool validationRates = Settings != null && Settings.UseValidationEvolutionRates;
        string evolutionMode = validationRates ? "validation" : "normal";
        string readbackState = _weatherDiagnosticsError ? "readback error" :
            _weatherDiagnosticsPending ? "readback pending" : $"{_weatherDiagnosticsSamples} cells";
        string lastUpdateAge = _evolutionDispatchCount > 0
            ? $"{Mathf.Max(0f, Time.time - _lastEvolutionTime):F2}s"
            : "none";

        GUILayout.Label($"Grid: {WeatherResolution} x {WeatherResolution} x 6 ({readbackState})");
        GUILayout.Label($"Query cache: {GetQueryCacheFaceCount()}/6 faces, last face {(_weatherQueryCacheLastFace >= 0 ? _weatherQueryCacheLastFace.ToString() : "none")}");
        if (_weatherQueryCacheError)
            GUILayout.Label("Query cache readback error");
        GUILayout.Label($"Diagnostics face: {(_weatherDiagnosticsLastFace >= 0 ? _weatherDiagnosticsLastFace.ToString() : "none")}");
        GUILayout.Label($"Evolution: {evolutionMode}, dispatches {_evolutionDispatchCount}, last dt {_lastEvolutionDelta:F2}s");
        GUILayout.Label($"Last update age: {lastUpdateAge}");
        GUILayout.Label($"Condensation avg: {_weatherAverageCondensation:F3}, storm avg: {_weatherAverageStorm:F3}");
        GUILayout.Label($"Precipitation avg: {_weatherAveragePrecipitation:F3}");
        GUILayout.Label($"Moisture source avg: {_weatherAverageMoistureSource:F3}");
        GUILayout.Label($"Delta avg/max: {_weatherAverageCondensationChange:+0.0000;-0.0000;0.0000} / {_weatherMaxCondensationChange:F4}");
        GUILayout.Label($"Condensing: {_weatherCondensingFraction * 100f:F1}%, drying: {_weatherDryingFraction * 100f:F1}%");
        if (Settings != null && Settings.DebugMode == CloudSettings.DebugView.CondensationChange)
        {
            GUILayout.Label("Delta view: cyan condensing, red drying, dim below threshold");
            GUILayout.Label($"Threshold/saturation: {Settings.CondensationChangeDebugThreshold:F4} / {Settings.CondensationChangeDebugSaturation:F4}");
        }
        GUILayout.EndArea();
    }

    int GetQueryCacheFaceCount()
    {
        int count = 0;
        int mask = _weatherQueryCacheFaceMask;
        while (mask != 0)
        {
            count += mask & 1;
            mask >>= 1;
        }

        return count;
    }

    float CalculatePrecipitation(float stormIntensity)
    {
        return Precipitation * Mathf.SmoothStep(
            PrecipitationStormThreshold,
            Mathf.Min(1f, PrecipitationStormThreshold + PrecipitationStormSoftness),
            stormIntensity);
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
