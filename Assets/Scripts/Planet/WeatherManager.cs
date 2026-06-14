using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// Owns planet-scale weather state. The CPU seeds the initial cube-sphere weather
/// grid, then runtime evolution is dispatched to a GPU ping-pong texture.
/// </summary>
[CommandPrefix("weather")]
public class WeatherManager : MonoBehaviour, IWeatherProvider, IWeatherConfigurator, ILateInitialize,
    IProgressReporter, IWorldServiceRegistrar, IWorldTeardown
{
    CloudDto _settings;

    [ConsoleCommand("diagnostics", "Write weather diagnostics file (F9 equivalent).")]
    static void DiagnosticsCmd()
        => EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.DumpWeatherDiagnostics));

    [ConsoleCommand("frame-storm", "Reposition the camera to frame the strongest active storm.", MonoTargetType.Single)]
    string FrameStormCmd()
    {
        if (!TryFindStrongestPrecipitation(out Vector3 pos, out _))
            return "no active storm found";
        if (!ServiceLocator.TryGet<IFreeCameraService>(out var cam))
            return "no free camera active";
        cam.FrameWorldTarget(pos);
        return "framing strongest storm";
    }

    [ConsoleCommand("wind-speed", "Get or set global wind speed in meters per second.", MonoTargetType.Single)]
    string WindSpeedCmd(float? value = null)
    {
        if (value == null) return $"wind speed: {WindSpeedMetersPerSecond:F2} m/s";
        WindSpeedMetersPerSecond = Mathf.Max(0f, value.Value);
        return $"wind speed: {WindSpeedMetersPerSecond:F2} m/s";
    }

    [ConsoleCommand("wind-preset", "Apply a physical wind-speed preset.", MonoTargetType.Single)]
    string WindPresetCmd(WindPreset preset)
    {
        WindSpeedMetersPerSecond = WeatherUnits.WindSpeedForPreset(preset);
        return $"wind preset {preset}: {WindSpeedMetersPerSecond:F2} m/s";
    }

    [ConsoleCommand("wind-direction", "Get or set the global wind direction vector.", MonoTargetType.Single)]
    string WindDirectionCmd(Vector3? value = null)
    {
        if (value == null) return $"wind direction: ({WindDir.x:F2}, {WindDir.y:F2}, {WindDir.z:F2})";
        WindDir = value.Value;
        return $"wind direction: ({WindDir.x:F2}, {WindDir.y:F2}, {WindDir.z:F2})";
    }

    [Header("References")]
    public ComputeShader WeatherCompute;

    [Header("Wind")]
    [Tooltip("World-space direction the wind and weather features move toward.")]
    public Vector3 WindDir = new Vector3(1f, 0f, 0.3f);
    [FormerlySerializedAs("Speed")]
    [Range(0f, 40f), Tooltip("Physical wind speed in meters per second.")]
    public float WindSpeedMetersPerSecond = 2.5f;
    const int CurrentWindUnitsVersion = 1;
    [SerializeField, HideInInspector] int _windUnitsVersion;

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
    WeatherDiagnostics _diagnostics;
    Vector3 _planetCenter;
    float _seaLevelRadius;
    float _cloudWindAngle;
    float _evolutionAccumulator;
    bool _missingWeatherComputeLogged;
    bool _weatherQueryCachePending;
    bool _weatherQueryCacheError;
    float _nextWeatherQueryCacheTime;
    int _weatherQueryCacheNextFace;
    int _weatherQueryCacheLastFace = -1;
    int _weatherQueryCacheFaceMask;
    static readonly int _windDirectionId = Shader.PropertyToID(ShaderGlobalIds.WindDirection);
    static readonly int _windSpeedMetersPerSecondId = Shader.PropertyToID(ShaderGlobalIds.WindSpeedMps);
    static readonly int _windStrengthId = Shader.PropertyToID(ShaderGlobalIds.WindStrength01);
    static readonly int _cloudWeatherRotationId = Shader.PropertyToID(ShaderGlobalIds.CloudWeatherRotation);
    static readonly int _cloudWindAngleId = Shader.PropertyToID(ShaderGlobalIds.CloudWindAngle);

    CancellationTokenSource _generateCts;
    bool _lateInitialized;
    bool _worldTornDown;
    // Last wind values pushed to shader globals; sentinel values force the first upload.
    Vector3 _lastUploadedWindDirection = new Vector3(float.NaN, float.NaN, float.NaN);
    float _lastUploadedWindSpeedMetersPerSecond = float.NaN;
    float _lastUploadedWindStrength = float.NaN;
    IClimateSampler _climateSampler;

    // Resolved through ServiceLocator (PrecipitationController self-registers in Awake/OnEnable).
    // Returns null if no precipitation system is wired up.
    internal IPrecipitationDebugControl PrecipitationDebugControl =>
        ServiceLocator.Get<IPrecipitationDebugControl>();

    internal SphericalWeatherGrid Grid => _grid;
    internal int QueryCacheFaceCount => GetQueryCacheFaceCount();
    internal int QueryCacheLastFace => _weatherQueryCacheLastFace;
    internal bool QueryCacheError => _weatherQueryCacheError;

    public Vector3 WindDirection => WindDir.sqrMagnitude > 0.0001f ? WindDir.normalized : Vector3.right;
    float IWeatherProvider.WindSpeedMetersPerSecond => WindSpeedMetersPerSecond;
    public float WindStrength01 => WeatherUnits.WindStrength01(WindSpeedMetersPerSecond);
    public Texture WeatherTexture => _grid != null ? _grid.Texture : null;
    public Texture WeatherDynamicsTexture => _grid != null ? _grid.DynamicsTexture : null;
    public int WeatherResolution => _grid != null ? _grid.Resolution : 0;
    public bool HasWeatherGrid => _grid != null;

    // IProgressReporter
    readonly ProgressHandle _progressHandle = new ProgressHandle();
    public string ReporterName => "WeatherManager";
    public IProgressHandle ProgressHandle => _progressHandle;

    // OnPlanetGenerated fires synchronously during Planet.LateInitialize, so by the time
    // our LateInitialize runs _seaLevelRadius is populated. Generate the grid here directly
    // rather than fire-and-forget so the loading overlay stays up until clouds are ready.
    static readonly Type[] _lateDeps = { typeof(IPlanet) };
    public IReadOnlyList<Type> LateDependencies => _lateDeps;
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
        MigrateWindUnits();
        CloudDto.EnsureRegistered();
        _settings = SettingsProvider.GetSettings<CloudDto>();
        _diagnostics = new WeatherDiagnostics(this);
        Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.identity);
        Shader.SetGlobalFloat(_cloudWindAngleId, 0f);
    }

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<IWeatherProvider>(this);
        context.Register<IWeatherConfigurator>(this);
    }

    void OnValidate()
    {
        MigrateWindUnits();
        WindSpeedMetersPerSecond = Mathf.Clamp(WindSpeedMetersPerSecond, 0f, 40f);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<DebugWeatherDiagnosticsRequestedEvent>.Listen(OnWeatherDiagnosticsRequested);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<DebugWeatherDiagnosticsRequestedEvent>.Unlisten(OnWeatherDiagnosticsRequested);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
    }

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (evt.DtoType != typeof(CloudDto)) return;
        var prev = _settings;
        _settings = SettingsProvider.GetSettings<CloudDto>();
        if (_seaLevelRadius > 0f && (_settings.WeatherResolution != prev.WeatherResolution
            || _settings.InitialCoverage != prev.InitialCoverage))
        {
            GenerateWeatherGrid();
        }
    }

    void OnDestroy()
    {
        TeardownWorld();
    }

    public void TeardownWorld()
    {
        if (_worldTornDown)
            return;

        _worldTornDown = true;
        _generateCts?.Cancel();
        _generateCts?.Dispose();
        _generateCts = null;
        _grid?.Dispose();
        _grid = null;
    }

    void Update()
    {
        UpdateWeatherEvolution();
        UpdateWeatherQueryCache();
        _diagnostics.Tick();

        Vector3 windDir = WindDirection;
        float windSpeed = WindSpeedMetersPerSecond;
        float windStrength = WindStrength01;
        if (windDir != _lastUploadedWindDirection)
        {
            Shader.SetGlobalVector(_windDirectionId, windDir);
            _lastUploadedWindDirection = windDir;
        }
        if (windSpeed != _lastUploadedWindSpeedMetersPerSecond)
        {
            Shader.SetGlobalFloat(_windSpeedMetersPerSecondId, windSpeed);
            _lastUploadedWindSpeedMetersPerSecond = windSpeed;
        }
        if (windStrength != _lastUploadedWindStrength)
        {
            Shader.SetGlobalFloat(_windStrengthId, windStrength);
            _lastUploadedWindStrength = windStrength;
        }
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
            float fallbackCoverage = _settings.InitialCoverage;
            return new WeatherSample(fallbackCoverage, 0f, 0f, GetTemperature(worldPosition), 0f,
                fallbackCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear);
        }

        _grid.GetWeatherCell(worldPosition, _planetCenter, Quaternion.identity,
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

    public float GetTemperature(Vector3 worldPosition)
    {
        if (_climateSampler == null)
            ServiceLocator.TryGet(out _climateSampler);

        return _climateSampler != null &&
               _climateSampler.TrySampleClimate(worldPosition, out ClimateSample climate)
            ? climate.TemperatureCelsius
            : 0f;
    }

    void MigrateWindUnits()
    {
        if (_windUnitsVersion >= CurrentWindUnitsVersion)
            return;

        // Legacy scenes stored an abstract 0-5 value. One legacy unit represented
        // approximately 5 m/s in every visual consumer.
        WindSpeedMetersPerSecond = Mathf.Clamp(
            WindSpeedMetersPerSecond * 5f,
            0f,
            40f);
        _windUnitsVersion = CurrentWindUnitsVersion;
    }

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

        Vector3 worldDirection = weatherDirection.normalized;
        worldPosition = _planetCenter + worldDirection * (_seaLevelRadius + 25f);

        float precipitation = CalculatePrecipitation(stormIntensity);
        WeatherCellState state = stormIntensity >= PrecipitationStormThreshold
            ? WeatherCellState.Storm
            : cloudCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear;

        sample = new WeatherSample(
            cloudCoverage,
            stormIntensity,
            precipitation,
            GetTemperature(worldPosition),
            moistureSource,
            state);
        return true;
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        // During startup, LateInitialize handles generation.  At runtime (after init), re-generate
        // whenever the planet changes.
        if (_lateInitialized)
            _ = GenerateWeatherGridAsync();
    }

    async Awaitable GenerateWeatherGridAsync(CancellationToken externalToken = default)
    {
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

            _progressHandle.Report(0.15f, "Seeding weather grid...");

            // Compute cell data on background thread, upload textures on main thread.
            var newGrid = await SphericalWeatherGrid.GenerateAsync(_settings, seed, linked.Token);

            if (this == null) return;
            _progressHandle.Report(0.85f, "Uploading weather...");
            _grid?.Dispose();
            _grid = newGrid;
            _evolutionAccumulator = 0f;
            ResetAdvection();
            ResetQueryCache();
            _diagnostics.Reset();
            Logger.Log(LogLevel.Debug, "Weather", $"Generated {WeatherResolution}x{WeatherResolution}x6 condensation grid.");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex) { Logger.LogException("Weather", ex); }
    }

    void GenerateWeatherGrid()
    {
        int seed = ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("Weather");

        _grid?.Dispose();
        _grid = SphericalWeatherGrid.Generate(_settings, seed);
        _evolutionAccumulator = 0f;
        ResetAdvection();
        ResetQueryCache();
        _diagnostics.Reset();
        Logger.Log(LogLevel.Debug, "Weather", $"Generated {WeatherResolution}x{WeatherResolution}x6 condensation grid.");
    }

    // How far (radians) weather drifts along the sphere in one evolution step. The compute and
    // the cloud-shape noise both advect along the local windTangent by rotating about
    // cross(direction, windDir) by the accumulated angle, so detail and fronts ride together.
    float StepAdvectionAngle(float stepSeconds)
    {
        float angularSpeed = WindSpeedMetersPerSecond / Mathf.Max(_seaLevelRadius, 1f);
        return angularSpeed * CloudConstants.FrontAdvectionSpeedMultiplier * stepSeconds;
    }

    void ResetAdvection()
    {
        _cloudWindAngle = 0f;
        Shader.SetGlobalFloat(_cloudWindAngleId, 0f);
    }

    void UpdateWeatherEvolution()
    {
        if (_grid == null || !_settings.EnableWeatherEvolution)
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
        float interval = Mathf.Max(_settings.EvolutionInterval, 0.05f);
        if (_evolutionAccumulator < interval)
            return;

        float stepAngle = StepAdvectionAngle(interval);
        Vector3 windDir = WindDirection;
        int maxSteps = 3;
        int steps = 0;
        while (_evolutionAccumulator >= interval && steps < maxSteps)
        {
            if (_grid.Advance(WeatherCompute, _settings, interval, windDir, stepAngle))
            {
                _cloudWindAngle += stepAngle;
                _diagnostics.RecordEvolutionDispatch(interval);
            }

            _evolutionAccumulator -= interval;
            steps++;
        }

        Shader.SetGlobalFloat(_cloudWindAngleId, _cloudWindAngle);

        float maxCarry = interval * maxSteps;
        if (_evolutionAccumulator > maxCarry)
            _evolutionAccumulator = maxCarry;
    }

    void OnWeatherDiagnosticsRequested(DebugWeatherDiagnosticsRequestedEvent evt)
        => _diagnostics.OnDiagnosticsRequested();

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
            _diagnostics.OnQueryCacheFaceData(face, data);
    }

    void OnWeatherDynamicsQueryCacheReadback(AsyncGPUReadbackRequest request, int face)
    {
        if (request.hasError)
            return;

        _grid?.ApplyDynamicsFaceReadback(face, request.GetData<Color>());
    }

    void ResetQueryCache()
    {
        _weatherQueryCachePending = false;
        _weatherQueryCacheError = false;
        _nextWeatherQueryCacheTime = 0f;
        _weatherQueryCacheNextFace = 0;
        _weatherQueryCacheLastFace = -1;
        _weatherQueryCacheFaceMask = 0;
    }

    void OnGUI() => _diagnostics.DrawOverlay();

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

    internal float CalculatePrecipitation(float stormIntensity)
    {
        return Precipitation * Mathf.SmoothStep(
            PrecipitationStormThreshold,
            Mathf.Min(1f, PrecipitationStormThreshold + PrecipitationStormSoftness),
            stormIntensity);
    }
}
