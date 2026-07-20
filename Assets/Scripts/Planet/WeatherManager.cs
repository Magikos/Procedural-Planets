using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering;

/// <summary>
/// Owns planet-scale weather state. The CPU seeds the initial cube-sphere weather
/// grid, then runtime evolution is dispatched to a GPU ping-pong texture.
/// </summary>
[CommandPrefix("weather")]
public class WeatherManager : MonoBehaviour, IWeatherProvider, IWeatherConfigurator, ILateInitialize,
    IProgressReporter, IWorldServiceRegistrar, IWorldSettingsRegistrar, IWorldTeardown
{
    static readonly Type[] RequiredSettings = { typeof(CloudDto) };
    CloudDto _settings;

    [ConsoleCommand("diagnostics", "Write weather diagnostics file (F9 equivalent).")]
    static void DiagnosticsCmd()
        => EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.DumpWeatherDiagnostics));

    [ConsoleCommand("export-grid", "Force-read and write full weather grid summary JSON plus raw cell CSV.", MonoTargetType.Single)]
    async Awaitable<string> ExportGridCmd(CancellationToken ct = default)
    {
        if (_grid == null)
            return "[WeatherDiagnostics] No weather grid generated.";

        bool readbackOk = await ReadbackAllGridFacesAsync(ct);
        string result = _diagnostics.ExportGrid("console", readbackOk);
        return readbackOk ? result : result + " (readback incomplete; check log)";
    }

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

    [ConsoleCommand("freeze", "Get or set whether weather evolution is paused. True holds the current condensation/storm state steady (no drift); false resumes normal evolution. Independent of time.freeze (sun).", MonoTargetType.Single)]
    string FreezeCmd(bool? value = null)
    {
        if (value == null) return $"weather evolution frozen: {!_settings.EnableWeatherEvolution}";
        SettingsProvider.Update(_settings with { EnableWeatherEvolution = !value.Value });
        return $"weather evolution frozen: {value.Value}";
    }

    [ConsoleCommand("force", "Force the ENTIRE weather grid to one uniform condensation/storm value (each 0-1), for deterministic test scenes - removes noise-pattern coverage variance. Also freezes evolution (weather.freeze true) so the forced state holds steady; weather.freeze false resumes normal drift. Storm defaults to 0.", MonoTargetType.Single)]
    string ForceCmd(float condensation, float? storm = null)
    {
        if (_grid == null) return "weather grid not ready";

        float c = Mathf.Clamp01(condensation);
        float s = Mathf.Clamp01(storm ?? 0f);
        var weatherValue = new Vector4(c, s, c, 0.5f);
        var dynamicsValue = new Vector4(c, 0f, 0f, c);
        if (!_grid.ForceUniform(WeatherCompute, weatherValue, dynamicsValue))
            return "weather force failed: WeatherCompute is not assigned";

        SettingsProvider.Update(_settings with { EnableWeatherEvolution = false });
        return $"weather forced: condensation={c:F2} storm={s:F2} (evolution frozen)";
    }

    [ConsoleCommand("coverage", "Get or set overall cloud coverage, 0-1 (the InitialCoverage seed). Lower = clearer skies with scattered clouds and only occasional storms; higher = overcast with storms everywhere. Changing it reseeds the whole grid. Default 0.48.", MonoTargetType.Single)]
    string CoverageCmd(float? value = null)
    {
        if (value == null) return $"cloud coverage: {_settings.InitialCoverage:F2}";
        float clamped = Mathf.Clamp01(value.Value);
        SettingsProvider.Update(_settings with { InitialCoverage = clamped });
        return $"cloud coverage: {clamped:F2} (grid reseeding)";
    }

    [ConsoleCommand("regenerate", "Reseed the weather grid from noise, restoring the natural varied moisture-source map, and resume evolution. Use this to undo weather.force / weather.test-pattern, which overwrite the source (b) channel with uniform/patterned values - after which evolution can only relax toward that flattened source, so the planet stays uniform until regenerated.", MonoTargetType.Single)]
    async Awaitable<string> RegenerateCmd(CancellationToken ct = default)
    {
        await GenerateWeatherGridAsync(ct);
        SettingsProvider.Update(_settings with { EnableWeatherEvolution = true });
        return "weather grid regenerated (natural source map restored, evolution resumed)";
    }

    [ConsoleCommand("storm-threshold", "Get or set the condensation level above which storms (cumulonimbus) form, 0-1. Lower = storms form in more, less-saturated cells, so storms actually develop during normal evolution instead of almost never. Takes effect on the next evolution step. Default 0.86.", MonoTargetType.Single)]
    string StormThresholdCmd(float? value = null)
    {
        if (value == null) return $"storm threshold: {_settings.StormThreshold:F2}";
        float clamped = Mathf.Clamp01(value.Value);
        SettingsProvider.Update(_settings with { StormThreshold = clamped });
        return $"storm threshold: {clamped:F2}";
    }

    [ConsoleCommand("test-pattern", "Write a deterministic 3-band cloud-type test bed to every cube face (stratus | cumulus | cumulonimbus along the u axis) and freeze evolution. Pan the horizon on any face to see all three cloud shapes side by side - isolates the vertical profile from the sim.", MonoTargetType.Single)]
    string TestPatternCmd()
    {
        if (_grid == null) return "weather grid not ready";
        if (!_grid.WriteTestPattern(WeatherCompute))
            return "weather test-pattern failed: WeatherCompute is not assigned";

        SettingsProvider.Update(_settings with { EnableWeatherEvolution = false });
        return "weather test pattern written: stratus | cumulus | cumulonimbus bands (evolution frozen)";
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
    WeatherEvolutionScheduler _evolutionScheduler;
    WeatherQueryCache _queryCache;
    Vector3 _planetCenter;
    float _seaLevelRadius;
    bool _windDirty = true;
    Vector3 _lastUploadedWindDirection;
    float _lastUploadedWindSpeedMetersPerSecond;
    float _lastUploadedWindStrength;
    IClimateSampler _climateSampler;

    static readonly int _windDirectionId = Shader.PropertyToID(ShaderGlobalIds.WindDirection);
    static readonly int _windSpeedMetersPerSecondId = Shader.PropertyToID(ShaderGlobalIds.WindSpeedMps);
    static readonly int _windStrengthId = Shader.PropertyToID(ShaderGlobalIds.WindStrength01);
    static readonly int _cloudWeatherRotationId = Shader.PropertyToID(ShaderGlobalIds.CloudWeatherRotation);

    CancellationTokenSource _generateCts;
    bool _lateInitialized;
    bool _worldTornDown;

    // Resolved through ServiceLocator (PrecipitationController self-registers in Awake/OnEnable).
    // Returns null if no precipitation system is wired up.
    internal IPrecipitationDebugControl PrecipitationDebugControl =>
        ServiceLocator.TryGet(out IPrecipitationDebugControl control) ? control : null;

    internal SphericalWeatherGrid Grid => _grid;
    internal int QueryCacheFaceCount => _queryCache.FaceCount;
    internal int QueryCacheDynamicsFaceCount => _queryCache.DynamicsFaceCount;
    internal int QueryCacheLastFace => _queryCache.LastFace;
    internal bool QueryCacheError => _queryCache.Error;

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
        if (_seaLevelRadius <= 0f || !TryResolveSettings()) return;   // no planet in this scene

        _progressHandle.Report(0f, "Generating clouds...");
        await GenerateWeatherGridAsync(cancellationToken);
        _progressHandle.Report(1f, "Clouds ready");
        _lateInitialized = true;
    }

    ILogger Logger => LoggerProvider.Get();

    void Awake()
    {
        MigrateWindUnits();
        _diagnostics = new WeatherDiagnostics(this);
        _evolutionScheduler = new WeatherEvolutionScheduler(Logger);
        _queryCache = new WeatherQueryCache();
        Shader.SetGlobalMatrix(_cloudWeatherRotationId, Matrix4x4.identity);
        _evolutionScheduler.Reset();
    }

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<IWeatherProvider>(this);
        context.Register<IWeatherConfigurator>(this);
    }

    public IReadOnlyList<Type> RequiredSettingsTypes => RequiredSettings;

    public void RegisterWorldSettings(ISettingsService settings)
    {
        CloudDto.EnsureRegistered(settings);
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
        _settings = null;
        if (!TryResolveSettings())
            return;

        if (_seaLevelRadius > 0f && prev != null && (_settings.WeatherResolution != prev.WeatherResolution
            || _settings.InitialCoverage != prev.InitialCoverage))
        {
            _ = GenerateWeatherGridAsync();
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
        if (!TryResolveSettings())
            return;

        _evolutionScheduler.Tick(_grid, _settings, WeatherCompute, WindDirection,
            WindSpeedMetersPerSecond, _seaLevelRadius, _diagnostics);
        _queryCache.Tick(_grid, EnableWeatherQueryCache, WeatherQueryCacheInterval,
            ShowWeatherDiagnostics, _diagnostics);
        _diagnostics.Tick();

        Vector3 windDir = WindDirection;
        float windSpeed = WindSpeedMetersPerSecond;
        float windStrength = WindStrength01;
        if (_windDirty || windDir != _lastUploadedWindDirection)
        {
            Shader.SetGlobalVector(_windDirectionId, windDir);
            _lastUploadedWindDirection = windDir;
        }
        if (_windDirty || windSpeed != _lastUploadedWindSpeedMetersPerSecond)
        {
            Shader.SetGlobalFloat(_windSpeedMetersPerSecondId, windSpeed);
            _lastUploadedWindSpeedMetersPerSecond = windSpeed;
        }
        if (_windDirty || windStrength != _lastUploadedWindStrength)
        {
            Shader.SetGlobalFloat(_windStrengthId, windStrength);
            _lastUploadedWindStrength = windStrength;
        }
        _windDirty = false;
    }

    public WeatherSample SampleWeather(Vector3 worldPosition)
    {
        if (_grid == null)
        {
            float fallbackCoverage = TryResolveSettings() ? _settings.InitialCoverage : 0f;
            return new WeatherSample(fallbackCoverage, 0f, 0f, GetTemperature(worldPosition), 0f,
                fallbackCoverage >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear);
        }

        _grid.GetWeatherCell(worldPosition, _planetCenter, Quaternion.identity,
            out float cloudCoverage, out float stormIntensity, out float moistureSource, out float precipitation);

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

        var stats = _grid.CalculateStats(CloudyThreshold, PrecipitationStormThreshold, PrecipitationStormThreshold);
        if (stats.CellCount == 0)
            return false;

        Vector3 worldDirection = stats.StrongestStormDirection.normalized;
        worldPosition = _planetCenter + worldDirection * (_seaLevelRadius + 25f);

        float precipitation = CalculatePrecipitation(stats.StrongestStorm);
        WeatherCellState state = stats.StrongestStorm >= PrecipitationStormThreshold
            ? WeatherCellState.Storm
            : stats.StrongestStormCondensation >= CloudyThreshold ? WeatherCellState.Cloudy : WeatherCellState.Clear;

        sample = new WeatherSample(
            stats.StrongestStormCondensation,
            stats.StrongestStorm,
            precipitation,
            GetTemperature(worldPosition),
            stats.StrongestStormMoistureSource,
            state);
        return true;
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        // During startup, LateInitialize handles generation. At runtime (after init), re-generate
        // whenever the planet changes.
        if (_lateInitialized)
            _ = GenerateWeatherGridAsync();
    }

    async Awaitable GenerateWeatherGridAsync(CancellationToken externalToken = default)
    {
        if (!TryResolveSettings())
            return;

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

            var newGrid = await SphericalWeatherGrid.GenerateComputeAsync(WeatherCompute, _settings, seed, linked.Token);

            if (this == null)
            {
                newGrid.Dispose();
                return;
            }
            _progressHandle.Report(0.85f, "Uploading weather...");
            _grid?.Dispose();
            _grid = newGrid;
            _evolutionScheduler.Reset();
            _queryCache.Reset();
            _diagnostics.Reset();
            Logger.Log(LogLevel.Debug, "Weather", $"Generated {WeatherResolution}x{WeatherResolution}x6 condensation grid.");
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex) { Logger.LogException("Weather", ex); }
    }

    void OnWeatherDiagnosticsRequested(DebugWeatherDiagnosticsRequestedEvent evt)
        => _diagnostics.OnDiagnosticsRequested();

    void OnGUI() => _diagnostics.DrawOverlay();

    async Awaitable<bool> ReadbackAllGridFacesAsync(CancellationToken ct)
    {
        if (_grid?.Texture == null || _grid.DynamicsTexture == null)
            return false;

        bool ok = true;
        for (int face = 0; face < 6; face++)
        {
            ok &= await ReadbackGridFaceAsync(_grid.Texture, face, _grid.ApplyWeatherFaceReadback, ct);
            ok &= await ReadbackGridFaceAsync(_grid.DynamicsTexture, face, _grid.ApplyDynamicsFaceReadback, ct);
        }
        return ok;
    }

    async Awaitable<bool> ReadbackGridFaceAsync(
        Texture texture,
        int face,
        Action<int, NativeArray<Color>> apply,
        CancellationToken ct)
    {
        var request = AsyncGPUReadback.Request(texture, 0,
            0, _grid.Resolution,
            0, _grid.Resolution,
            face, 1,
            TextureFormat.RGBAFloat);

        while (!request.done)
        {
            ct.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync(ct);
        }

        if (request.hasError)
            return false;

        apply(face, request.GetData<Color>());
        return true;
    }

    internal float CalculatePrecipitation(float stormIntensity)
    {
        float end = Mathf.Min(1f, PrecipitationStormThreshold + PrecipitationStormSoftness);
        float t = Mathf.InverseLerp(PrecipitationStormThreshold, end, stormIntensity);
        return Precipitation * Mathf.SmoothStep(0f, 1f, t);
    }

    bool TryResolveSettings()
    {
        return _settings != null || SettingsProvider.TryGetFrozen(out _settings);
    }
}
