using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owns planet-scale weather state. The CPU seeds the initial cube-sphere weather
/// grid, then runtime evolution is dispatched to a GPU ping-pong texture.
/// </summary>
public class WeatherManager : MonoBehaviour, IWeatherProvider, IWeatherConfigurator, ILateInitialize, IProgressReporter
{
    [Header("References")]
    public CloudSettings Settings;
    public ComputeShader WeatherCompute;

    CloudSettings IWeatherConfigurator.Settings => Settings;

    [Header("Wind")]
    public Vector3 WindDir = new Vector3(1f, 0f, 0.3f);
    [Range(0f, 5f)] public float Speed = 0.5f;

    [Header("Precipitation")]
    [Range(0f, 1f)] public float Precipitation = 1f;
    [Range(0f, 1f)] public float CloudyThreshold = 0.18f;
    [Range(0f, 1f)] public float PrecipitationStormThreshold = 0.5f;
    [Range(0.01f, 1f)] public float PrecipitationStormSoftness = 0.22f;

    [Header("Query Cache")]
    public bool EnableWeatherQueryCache = true;
    [Range(0.05f, 5f)] public float WeatherQueryCacheInterval = 0.5f;

    [Header("Diagnostics")]
    public bool ShowWeatherDiagnostics = false;
    [Range(0.25f, 10f)] public float WeatherDiagnosticsInterval = 2f;
    public bool EnableWeatherDiagnosticHotkey = true;
    public bool WriteWeatherDiagnosticsFile = true;

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
    float _weatherAverageRainRate;
    float _weatherAverageMoistureSource;
    float _weatherAverageCondensationChange;
    float _weatherMaxCondensationChange;
    float _weatherCloudyFraction;
    float _weatherStormFraction;
    float _weatherRainingFraction;
    float _weatherCondensingFraction;
    float _weatherDryingFraction;
    int _weatherDiagnosticsSamples;
    float _nextWeatherAggregateStatsTime;
    int _weatherDiagnosticsNextFace;
    int _weatherDiagnosticsLastFace = -1;
    bool _weatherQueryCachePending;
    bool _weatherQueryCacheError;
    float _nextWeatherQueryCacheTime;
    int _weatherQueryCacheNextFace;
    int _weatherQueryCacheLastFace = -1;
    int _weatherQueryCacheFaceMask;
    static readonly int _windDirectionId = Shader.PropertyToID("_WindDirection");
    static readonly int _windSpeedId = Shader.PropertyToID("_WindSpeed");
    static readonly int _cloudWeatherRotationId = Shader.PropertyToID("_CloudWeatherRotation");

    CancellationTokenSource _generateCts;
    bool _lateInitialized;
    PrecipitationController _precipitationController;

    // Resolved once and reused; the Unity null check re-acquires it if the previous one was destroyed.
    PrecipitationController PrecipitationController =>
        _precipitationController != null
            ? _precipitationController
            : _precipitationController = FindAnyObjectByType<PrecipitationController>();

    public Vector3 WindDirection => WindDir.sqrMagnitude > 0.0001f ? WindDir.normalized : Vector3.right;
    public float WindSpeed => Speed;
    public Texture WeatherTexture => _grid != null ? _grid.Texture : null;
    public Texture WeatherDynamicsTexture => _grid != null ? _grid.DynamicsTexture : null;
    public int WeatherResolution => _grid != null ? _grid.Resolution : 0;
    public bool HasWeatherGrid => _grid != null;

    // IProgressReporter
    readonly ProgressHandle _progressHandle = new ProgressHandle();
    public string ReporterName => "WeatherManager";
    public IProgressHandle ProgressHandle => _progressHandle;

    // ILateInitialize — runs after Planet (priority 0).  OnPlanetGenerated has already fired
    // (synchronously during Planet.LateInitialize), so _seaLevelRadius is populated.
    // We generate the grid here directly rather than fire-and-forget so the overlay stays up
    // until clouds are ready.
    public int LatePriority => -10;
    public async Awaitable LateInitialize(CancellationToken cancellationToken)
    {
        if (_seaLevelRadius <= 0f) return;   // no planet in this scene

        _progressHandle.Report(0f, "Generating clouds...");
        await GenerateWeatherGridAsync(cancellationToken);
        _progressHandle.Report(1f, "Clouds ready");
        _lateInitialized = true;
    }

    ILogger Logger => LoggerProvider.Get();

    void Awake()
    {
        ServiceLocator.Register<IWeatherProvider>(this);
        ServiceLocator.Register<IWeatherConfigurator>(this);
        Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.identity);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<DebugWeatherDiagnosticsRequestedEvent>.Listen(OnWeatherDiagnosticsRequested);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<DebugWeatherDiagnosticsRequestedEvent>.Unlisten(OnWeatherDiagnosticsRequested);
    }

    void OnDestroy()
    {
        ServiceLocator.Unregister<IWeatherProvider>(this);
        ServiceLocator.Unregister<IWeatherConfigurator>(this);
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _generateCts = null;
        _grid?.Dispose();
        _grid = null;
    }

    void Update()
    {
        UpdateWeatherAdvection();
        UpdateWeatherEvolution();
        UpdateWeatherQueryCache();
        UpdateWeatherDiagnostics();
        UpdateWeatherAggregateDiagnostics();
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

    public bool TryFindStrongestPrecipitation(out Vector3 worldPosition, out WeatherSample sample)
    {
        worldPosition = Vector3.zero;
        sample = default;

        if (_grid == null || _seaLevelRadius <= 0f)
            return false;

        if (!_grid.TryFindStrongestStorm(out Vector3 weatherDirection,
            out float cloudCoverage,
            out float stormIntensity,
            out float moistureSource))
            return false;

        Vector3 worldDirection = (_weatherVisualRotation * weatherDirection).normalized;
        worldPosition = _planetCenter + worldDirection * (_seaLevelRadius + 25f);

        float precipitation = CalculatePrecipitation(stormIntensity);
        WeatherCellState state = stormIntensity >= PrecipitationStormThreshold
            ? WeatherCellState.Storm
            : cloudCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear;

        sample = new WeatherSample(cloudCoverage, stormIntensity, precipitation, 0.5f, moistureSource, state);
        return true;
    }

    public string DumpWeatherDiagnostics(string reason)
    {
        if (_grid == null)
        {
            const string noGrid = "[WeatherDiagnostics] No weather grid generated.";
            Logger.Log(LogLevel.Info, "Weather", noGrid);
            return noGrid;
        }

        var precipitationController = PrecipitationController;
        float rainThreshold = precipitationController != null
            ? precipitationController.StormThreshold
            : PrecipitationStormThreshold;
        var stats = _grid.CalculateStats(CloudyThreshold, PrecipitationStormThreshold, rainThreshold);
        TryFindStrongestPrecipitation(out Vector3 strongestPosition, out WeatherSample strongestSample);

        bool precipitationRenderEnabled = precipitationController != null
            && precipitationController.RenderPrecipitation
            && precipitationController.IsRenderingEnabled;
        string report = BuildWeatherDiagnosticsJson(reason, stats, strongestPosition, strongestSample, precipitationController);
        string path = string.Empty;
        if (WriteWeatherDiagnosticsFile)
        {
            string fileName = $"weather-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(path, report);
        }

        float invCellCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;
        int queryCacheFaces = GetQueryCacheFaceCount();
        string summary = $"[WeatherDiagnostics] cells={stats.CellCount}, " +
            $"avgCloud={stats.AverageCondensation:F3}, cloudy={stats.CloudyCellCount * invCellCount:P1}, " +
            $"avgPotential={stats.AverageMoistureSource:F3}, maxCloud={stats.MaxCondensation:F3}, " +
            $"avgStorm={stats.AverageStorm:F3}, storm={stats.StormCellCount * invCellCount:P1}, maxStorm={stats.MaxStorm:F3}, " +
            $"avgRain={stats.AverageRainRate:F3}, raining={stats.RainingCellCount * invCellCount:P1}, maxRain={stats.MaxRainRate:F3}, " +
            $"rainCandidates={stats.RainCandidateCellCount}, rainRender={(precipitationRenderEnabled ? "on" : "off")}, " +
            $"queryCacheFaces={queryCacheFaces}/6";
        if (queryCacheFaces < 6)
            summary += ", cache=warming";
        if (!string.IsNullOrEmpty(path))
            summary += $", file={path}";

        Logger.Log(LogLevel.Info, "Weather", summary);
        return report;
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        // During startup, LateInitialize handles generation.  At runtime (after init), re-generate
        // whenever the planet changes.
        if (_lateInitialized && Settings != null)
            _ = GenerateWeatherGridAsync();
    }

    async Awaitable GenerateWeatherGridAsync(CancellationToken externalToken = default)
    {
        if (Settings == null) return;

        // Cancel any in-flight generation before starting a new one.
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _generateCts = new CancellationTokenSource();

        using var linked = externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalToken, _generateCts.Token, destroyCancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(_generateCts.Token, destroyCancellationToken);

        try
        {
            int seed = ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("Weather");
            var settings = Settings;

            _progressHandle.Report(0.15f, "Seeding weather grid...");

            // Compute cell data on background thread, upload textures on main thread.
            var newGrid = await SphericalWeatherGrid.GenerateAsync(settings, seed, linked.Token);

            if (this == null) return;
            _progressHandle.Report(0.85f, "Uploading weather...");
            _grid?.Dispose();
            _grid = newGrid;
            _weatherVisualRotation = Quaternion.identity;
            _evolutionAccumulator = 0f;
            ResetWeatherDiagnostics();
            UploadWeatherAdvection();
            Logger.Log(LogLevel.Debug, "Weather", $"Generated {WeatherResolution}x{WeatherResolution}x6 condensation grid.");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex) { Logger.LogException("Weather", ex); }
    }

    void GenerateWeatherGrid()
    {
        if (Settings == null) return;

        int seed = ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("Weather");

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

        int maxSteps = 3;
        int steps = 0;
        while (_evolutionAccumulator >= interval && steps < maxSteps)
        {
            if (_grid.Advance(WeatherCompute, Settings, interval, _weatherVisualRotation))
            {
                _evolutionDispatchCount++;
                _lastEvolutionDelta = interval;
                _lastEvolutionTime = Time.time;
            }

            _evolutionAccumulator -= interval;
            steps++;
        }

        float maxCarry = interval * maxSteps;
        if (_evolutionAccumulator > maxCarry)
            _evolutionAccumulator = maxCarry;
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

    void UpdateWeatherAggregateDiagnostics()
    {
        if (!ShowWeatherDiagnostics || _grid == null || Time.unscaledTime < _nextWeatherAggregateStatsTime)
            return;

        var precipitationController = PrecipitationController;
        float rainThreshold = precipitationController != null
            ? precipitationController.StormThreshold
            : PrecipitationStormThreshold;
        var stats = _grid.CalculateStats(CloudyThreshold, PrecipitationStormThreshold, rainThreshold);
        float invCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;

        _weatherAverageCondensation = stats.AverageCondensation;
        _weatherAverageStorm = stats.AverageStorm;
        _weatherAverageMoistureSource = stats.AverageMoistureSource;
        _weatherAverageRainRate = stats.AverageRainRate;
        _weatherCloudyFraction = stats.CloudyCellCount * invCount;
        _weatherStormFraction = stats.StormCellCount * invCount;
        _weatherRainingFraction = stats.RainingCellCount * invCount;
        _weatherDiagnosticsSamples = stats.CellCount;
        _nextWeatherAggregateStatsTime = Time.unscaledTime + Mathf.Max(WeatherDiagnosticsInterval, 0.5f);
    }

    void OnWeatherDiagnosticsRequested(DebugWeatherDiagnosticsRequestedEvent evt)
    {
        if (!EnableWeatherDiagnosticHotkey)
            return;

        DumpWeatherDiagnostics("F9");
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
        if (_grid != null && _grid.DynamicsTexture != null)
        {
            AsyncGPUReadback.Request(_grid.DynamicsTexture, 0,
                0, WeatherResolution,
                0, WeatherResolution,
                face, 1,
                TextureFormat.RGBAFloat,
                request => OnWeatherDynamicsQueryCacheReadback(request, face));
        }
        _weatherQueryCacheFaceMask |= 1 << face;

        if (ShowWeatherDiagnostics)
        {
            _weatherDiagnosticsLastFace = face;
            UpdateWeatherDiagnosticsStats(data);
        }
    }

    void OnWeatherDynamicsQueryCacheReadback(AsyncGPUReadbackRequest request, int face)
    {
        if (request.hasError)
            return;

        _grid?.ApplyDynamicsFaceReadback(face, request.GetData<Color>());
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
        _weatherAverageRainRate = 0f;
        _weatherAverageMoistureSource = 0f;
        _weatherAverageCondensationChange = 0f;
        _weatherMaxCondensationChange = 0f;
        _weatherCloudyFraction = 0f;
        _weatherStormFraction = 0f;
        _weatherRainingFraction = 0f;
        _weatherCondensingFraction = 0f;
        _weatherDryingFraction = 0f;
        _weatherDiagnosticsSamples = 0;
        _nextWeatherAggregateStatsTime = 0f;
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
        GUILayout.Label($"Cloudy/storm/raining: {_weatherCloudyFraction:P1} / {_weatherStormFraction:P1} / {_weatherRainingFraction:P1}");
        GUILayout.Label($"Rain rate avg: {_weatherAverageRainRate:F3}");
        GUILayout.Label($"Moisture source avg: {_weatherAverageMoistureSource:F3}");
        GUILayout.Label($"Delta avg/max: {_weatherAverageCondensationChange:+0.0000;-0.0000;0.0000} / {_weatherMaxCondensationChange:F4}");
        GUILayout.Label($"Condensing: {_weatherCondensingFraction * 100f:F1}%, drying: {_weatherDryingFraction * 100f:F1}%");
        GUILayout.Label("F9=Dump weather diagnostics");
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

    string BuildWeatherDiagnosticsJson(
        string reason,
        WeatherGridStats stats,
        Vector3 strongestPosition,
        WeatherSample strongestSample,
        PrecipitationController precipitationController)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(2048);
        sb.AppendLine("{");
        AppendJsonString(sb, "reason", reason, 1, true);
        AppendJsonString(sb, "utc", DateTime.UtcNow.ToString("O", culture), 1, true);
        AppendJsonNumber(sb, "weatherResolution", WeatherResolution, 1, true);
        int queryCacheFaces = GetQueryCacheFaceCount();
        AppendJsonNumber(sb, "queryCacheFaces", queryCacheFaces, 1, true);
        AppendJsonBool(sb, "queryCacheComplete", queryCacheFaces >= 6, 1, true);
        AppendJsonNumber(sb, "evolutionDispatches", _evolutionDispatchCount, 1, true);
        AppendJsonNumber(sb, "lastEvolutionDelta", _lastEvolutionDelta, 1, true);
        AppendJsonBool(sb, "validationEvolutionRates", Settings != null && Settings.UseValidationEvolutionRates, 1, true);
        AppendJsonBool(sb, "precipitationRenderEnabled",
            precipitationController != null && precipitationController.RenderPrecipitation && precipitationController.IsRenderingEnabled,
            1, true);
        AppendJsonNumber(sb, "precipitationStormThreshold", precipitationController != null ? precipitationController.StormThreshold : PrecipitationStormThreshold, 1, true);

        Indent(sb, 1).AppendLine("\"gridStats\": {");
        float invCellCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;
        AppendJsonNumber(sb, "cellCount", stats.CellCount, 2, true);
        AppendJsonNumber(sb, "cloudyCellCount", stats.CloudyCellCount, 2, true);
        AppendJsonNumber(sb, "stormCellCount", stats.StormCellCount, 2, true);
        AppendJsonNumber(sb, "rainCandidateCellCount", stats.RainCandidateCellCount, 2, true);
        AppendJsonNumber(sb, "rainingCellCount", stats.RainingCellCount, 2, true);
        AppendJsonNumber(sb, "cloudyFraction", stats.CloudyCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "stormFraction", stats.StormCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "rainCandidateFraction", stats.RainCandidateCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "rainingFraction", stats.RainingCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "averageCondensation", stats.AverageCondensation, 2, true);
        AppendJsonNumber(sb, "averageStorm", stats.AverageStorm, 2, true);
        AppendJsonNumber(sb, "averageMoistureSource", stats.AverageMoistureSource, 2, true);
        AppendJsonNumber(sb, "averageRainRate", stats.AverageRainRate, 2, true);
        AppendJsonNumber(sb, "maxCondensation", stats.MaxCondensation, 2, true);
        AppendJsonNumber(sb, "maxStorm", stats.MaxStorm, 2, true);
        AppendJsonNumber(sb, "maxMoistureSource", stats.MaxMoistureSource, 2, true);
        AppendJsonNumber(sb, "maxRainRate", stats.MaxRainRate, 2, false);
        Indent(sb, 1).AppendLine("},");

        Indent(sb, 1).AppendLine("\"strongestStorm\": {");
        AppendJsonVector(sb, "weatherDirection", stats.StrongestStormDirection, 2, true);
        AppendJsonVector(sb, "worldPosition", strongestPosition, 2, true);
        AppendJsonNumber(sb, "condensation", stats.StrongestStormCondensation, 2, true);
        AppendJsonNumber(sb, "storm", stats.StrongestStorm, 2, true);
        AppendJsonNumber(sb, "moistureSource", stats.StrongestStormMoistureSource, 2, true);
        AppendJsonNumber(sb, "samplePrecipitation", strongestSample.Precipitation, 2, false);
        Indent(sb, 1).AppendLine("}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static StringBuilder Indent(StringBuilder sb, int count)
    {
        for (int i = 0; i < count; i++)
            sb.Append("  ");
        return sb;
    }

    static void AppendJsonString(StringBuilder sb, string name, string value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": \"")
            .Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""))
            .Append('"').AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonBool(StringBuilder sb, string name, bool value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value ? "true" : "false").AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonNumber(StringBuilder sb, string name, float value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonNumber(StringBuilder sb, string name, int value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonVector(StringBuilder sb, string name, Vector3 value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": { ")
            .Append("\"x\": ").Append(value.x.ToString("0.####", CultureInfo.InvariantCulture)).Append(", ")
            .Append("\"y\": ").Append(value.y.ToString("0.####", CultureInfo.InvariantCulture)).Append(", ")
            .Append("\"z\": ").Append(value.z.ToString("0.####", CultureInfo.InvariantCulture)).Append(" }")
            .AppendLine(comma ? "," : string.Empty);
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
