using System.Threading;
using UnityEngine;

[DisallowMultipleComponent]
[CommandPrefix("debug")]
public class DebugCaptureController : MonoBehaviour, IDebugCaptureModeContext
{
    [Header("Debug Runtime")]
    public int CappedFrameRate = 60;
    public int ProfilingFrameRate = 1000;
    [Range(0f, 1f)]
    public float TimedCaptureLocalTime = 0.5f;

    [Header("Debug Info")]
    [System.NonSerialized]
    public bool ShowDebugOverlay;
    public bool ShowDetailedDebug;
    public bool IncludeMeshIntegrityInDebugCaptures;

    static readonly int _climateMapResolutionId = Shader.PropertyToID(ShaderGlobalIds.ClimateMapResolution);

    Light _cachedSunLight;
    ICameraRigContext _cachedCameraContext;
    ICelestialTimeController _cachedCelestialManager;
    IPrecipitationDebugControl _cachedPrecipitationController;
    IWeatherProvider _cachedWeatherProvider;
    DebugRegistry _debugRegistry;
    DebugModeId _currentDebugModeId;
    int _f10CaptureSetIndex = -1;
    bool _profilingFrameRateEnabled;
    DebugOverlayHud _hud;
    DebugCapturePipeline _pipeline;

    // IDebugCaptureModeContext
    bool IDebugCaptureModeContext.IsActive => gameObject.activeInHierarchy;
    DebugRegistry IDebugCaptureModeContext.Registry => _debugRegistry;
    DebugModeId IDebugCaptureModeContext.CurrentModeId => _currentDebugModeId;
    void IDebugCaptureModeContext.ApplyDebugMode(DebugModeId id) => ApplyDebugMode(id);
    void IDebugCaptureModeContext.CycleDebugMode() => CycleDebugMode();
    DebugCaptureSetDefinition IDebugCaptureModeContext.GetCurrentCaptureSet() => GetCurrentCaptureSet();
    DebugModeId[] IDebugCaptureModeContext.GetCaptureModes() => GetDebugCaptureModes();
    int IDebugCaptureModeContext.CappedFrameRate => CappedFrameRate;
    int IDebugCaptureModeContext.ProfilingFrameRate => ProfilingFrameRate;
    float IDebugCaptureModeContext.TimedCaptureLocalTime => TimedCaptureLocalTime;
    bool IDebugCaptureModeContext.ShowDetailedDebug => ShowDetailedDebug;
    bool IDebugCaptureModeContext.IncludeMeshIntegrityInDebugCaptures => IncludeMeshIntegrityInDebugCaptures;
    ICelestialTimeController IDebugCaptureModeContext.CelestialController => _cachedCelestialManager;
    Light IDebugCaptureModeContext.SunLight => _cachedSunLight;
    IPrecipitationDebugControl IDebugCaptureModeContext.PrecipitationController => _cachedPrecipitationController;
    IWeatherProvider IDebugCaptureModeContext.WeatherProvider => _cachedWeatherProvider;
    ICameraRigContext IDebugCaptureModeContext.CameraContext => _cachedCameraContext;
    int IDebugCaptureModeContext.ClimateMapResolutionId => _climateMapResolutionId;

    void Awake()
    {
        InitializeRegistry();
        if (_debugRegistry != null)
            ServiceLocator.Register<DebugRegistry>(_debugRegistry);
    }

    void OnEnable()
    {
        ApplyDebugMode(_currentDebugModeId);
        EventBus<DebugPrecipitationToggleRequestedEvent>.Listen(OnDebugPrecipitationToggleRequested);
        EventBus<DebugCaptureSetCycleRequestedEvent>.Listen(OnDebugCaptureSetCycleRequested);
        EventBus<DebugCaptureRequestedEvent>.Listen(OnDebugCaptureRequested);
        EventBus<DebugSunFreezeToggleRequestedEvent>.Listen(OnDebugSunFreezeToggleRequested);
        EventBus<DebugOverlayToggleRequestedEvent>.Listen(OnDebugOverlayToggleRequested);
        EventBus<DebugDetailedToggleRequestedEvent>.Listen(OnDebugDetailedToggleRequested);
        EventBus<DebugProfilingToggleRequestedEvent>.Listen(OnDebugProfilingToggleRequested);
        EventBus<WorldReadyEvent>.Listen(OnWorldReady);
    }

    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        InitializeRegistry();
        ServiceLocator.TryGet(out _cachedCameraContext);
        ServiceLocator.TryGet(out _cachedCelestialManager);
        ServiceLocator.TryGet(out _cachedPrecipitationController);
        ServiceLocator.TryGet(out _cachedWeatherProvider);
        _cachedSunLight = FindSunLight();
        _hud ??= new DebugOverlayHud();
        _pipeline ??= new DebugCapturePipeline(this);
    }

    void InitializeRegistry()
    {
        if (_debugRegistry != null)
            return;

        _debugRegistry = new DebugRegistry();
        _debugRegistry.RegisterModule(new WaterDebugModule());
        _debugRegistry.RegisterModule(new BiomeDebugModule());
        _debugRegistry.RegisterModule(new TerrainDebugModule());
        _debugRegistry.RegisterModule(new GrassDebugModule());
        _debugRegistry.RegisterModule(new AtmosphereDebugModule());
        _debugRegistry.RegisterModule(new ScaleReferenceDebugModule());
        _debugRegistry.RegisterModule(new CloudDebugModule());
        _debugRegistry.RegisterModule(new MemoryDebugModule());
        _debugRegistry.RegisterModule(new FrameTimingModule());
        _debugRegistry.RegisterModule(new ConsoleDebugModule());
        _debugRegistry.RegisterCoreCaptureSets();

        if (!_currentDebugModeId.IsValid)
            _currentDebugModeId = _debugRegistry.DefaultModeId;
        _f10CaptureSetIndex = _debugRegistry.ResolveCaptureSetIndex(_f10CaptureSetIndex);
    }

    void OnDisable()
    {
        EventBus<DebugPrecipitationToggleRequestedEvent>.Unlisten(OnDebugPrecipitationToggleRequested);
        EventBus<DebugCaptureSetCycleRequestedEvent>.Unlisten(OnDebugCaptureSetCycleRequested);
        EventBus<DebugCaptureRequestedEvent>.Unlisten(OnDebugCaptureRequested);
        EventBus<DebugSunFreezeToggleRequestedEvent>.Unlisten(OnDebugSunFreezeToggleRequested);
        EventBus<DebugOverlayToggleRequestedEvent>.Unlisten(OnDebugOverlayToggleRequested);
        EventBus<DebugDetailedToggleRequestedEvent>.Unlisten(OnDebugDetailedToggleRequested);
        EventBus<DebugProfilingToggleRequestedEvent>.Unlisten(OnDebugProfilingToggleRequested);
        EventBus<WorldReadyEvent>.Unlisten(OnWorldReady);
        _debugRegistry?.ClearModes();
    }

    void OnWorldReady(WorldReadyEvent _)
    {
        Initialize();
    }

    void OnDestroy()
    {
        _hud?.Dispose();
        if (_debugRegistry != null)
            ServiceLocator.Unregister<DebugRegistry>(_debugRegistry);
    }

    void OnDebugPrecipitationToggleRequested(DebugPrecipitationToggleRequestedEvent _) => TogglePrecipitationRendering();
    void OnDebugCaptureSetCycleRequested(DebugCaptureSetCycleRequestedEvent _) => CycleF10CaptureSet();
    void OnDebugCaptureRequested(DebugCaptureRequestedEvent _) => _pipeline?.TriggerCapture();
    void OnDebugSunFreezeToggleRequested(DebugSunFreezeToggleRequestedEvent _) => ToggleSunFreeze();
    void OnDebugOverlayToggleRequested(DebugOverlayToggleRequestedEvent _) => ShowDebugOverlay = !ShowDebugOverlay;
    void OnDebugDetailedToggleRequested(DebugDetailedToggleRequestedEvent _) => ShowDetailedDebug = !ShowDetailedDebug;
    void OnDebugProfilingToggleRequested(DebugProfilingToggleRequestedEvent _) => ToggleProfilingFrameRate();

    void TogglePrecipitationRendering()
    {
        IPrecipitationDebugControl controller = _cachedPrecipitationController;
        if (controller == null)
            return;

        bool next = !controller.PrecipitationRenderingEnabled;
        controller.PrecipitationRenderingEnabled = next;
        _hud?.NotifyPrecipitationToggle(next);
    }

    void CycleF10CaptureSet()
    {
        _f10CaptureSetIndex = _debugRegistry.GetNextCaptureSetIndex(_f10CaptureSetIndex);
    }

    void ToggleSunFreeze()
    {
        _cachedCelestialManager?.ToggleTimeFrozen();
    }

    void ToggleProfilingFrameRate()
    {
        _profilingFrameRateEnabled = !_profilingFrameRateEnabled;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = _profilingFrameRateEnabled
            ? Mathf.Max(ProfilingFrameRate, CappedFrameRate + 1)
            : Mathf.Max(CappedFrameRate, 1);
    }

    void CycleDebugMode()
    {
        ApplyDebugMode(_debugRegistry.GetNextModeId(_currentDebugModeId));
    }

    void ApplyDebugMode(DebugModeId modeId)
    {
        _currentDebugModeId = modeId;
        _debugRegistry.ApplyMode(modeId);
    }

    DebugModeId[] GetDebugCaptureModes()
    {
        return _debugRegistry.GetCaptureModeIds(GetCurrentCaptureSet(), _currentDebugModeId);
    }

    DebugCaptureSetDefinition GetCurrentCaptureSet()
    {
        _f10CaptureSetIndex = _debugRegistry.ResolveCaptureSetIndex(_f10CaptureSetIndex);
        return _debugRegistry.GetCaptureSet(_f10CaptureSetIndex);
    }

    DebugRuntimeState CreateRuntimeState()
    {
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
        string modeName = _debugRegistry.GetModeName(_currentDebugModeId);
        return new DebugRuntimeState(
            _debugRegistry,
            _cachedCameraContext,
            _cachedCelestialManager,
            _cachedPrecipitationController,
            _cachedWeatherProvider,
            _currentDebugModeId,
            modeName,
            captureSet.Id,
            captureSet.Name,
            ShowDetailedDebug,
            IncludeMeshIntegrityInDebugCaptures);
    }

    Light FindSunLight()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    void OnGUI()
    {
        if (_hud == null) return;

        if (!ShowDebugOverlay)
        {
            _hud.DrawHint();
            return;
        }

        _hud.Draw(
            _debugRegistry,
            _currentDebugModeId,
            GetCurrentCaptureSet(),
            _cachedCameraContext,
            _cachedCelestialManager,
            _cachedPrecipitationController,
            ShowDetailedDebug,
            CreateRuntimeState());
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("overlay", "Get or set the F6 debug HUD overlay state.", MonoTargetType.Single)]
    string OverlayCmd(bool? on = null)
    {
        if (on == null) return $"debug overlay: {ShowDebugOverlay}";
        ShowDebugOverlay = on.Value;
        return $"debug overlay: {ShowDebugOverlay}";
    }

    [ConsoleCommand("detailed-debug", "Get or set the detailed debug HUD section (F9 equivalent).", MonoTargetType.Single)]
    string DetailedDebugCmd(bool? on = null)
    {
        if (on == null) return $"detailed debug: {ShowDetailedDebug}";
        ShowDetailedDebug = on.Value;
        return $"detailed debug: {ShowDetailedDebug}";
    }

    [ConsoleCommand("profiling", "Toggle the high-FPS profiling target (F11 equivalent).", MonoTargetType.Single)]
    string ProfilingCmd()
    {
        ToggleProfilingFrameRate();
        return $"profiling mode: {(Application.targetFrameRate >= ProfilingFrameRate ? "ON" : "OFF")} (target={Application.targetFrameRate})";
    }

    [ConsoleCommand("precipitation", "Toggle precipitation rendering (P key equivalent).", MonoTargetType.Single)]
    string PrecipitationCmd()
    {
        TogglePrecipitationRendering();
        IPrecipitationDebugControl c = _cachedPrecipitationController;
        return c != null ? $"precipitation render: {(c.PrecipitationRenderingEnabled ? "ON" : "OFF")}" : "no precipitation controller";
    }

    [ConsoleCommand("cycle-capture-set", "Advance to the next F10 capture set.", MonoTargetType.Single)]
    string CycleCaptureSetCmd()
    {
        CycleF10CaptureSet();
        return $"capture set: {GetCurrentCaptureSet().Name}";
    }

    [ConsoleCommand("mode", "Get or set active debug visualization mode by name.", MonoTargetType.Single)]
    string ModeCmd([CompletionSource(typeof(DebugModeNamesProvider))] string name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            string current = _debugRegistry?.GetModeName(_currentDebugModeId) ?? "(none)";
            return $"debug mode: {current} ({_currentDebugModeId})";
        }
        if (_debugRegistry == null) return "debug registry not initialised";
        if (!_debugRegistry.TryFindModeByName(name, out var def))
            return $"unknown debug mode: '{name}'";
        ApplyDebugMode(def.Id);
        return $"debug mode: {def.Name} ({def.Id})";
    }

    [ConsoleCommand("capture-set", "Get or set active F10 capture set by name.", MonoTargetType.Single)]
    string CaptureSetCmd([CompletionSource(typeof(DebugCaptureSetNamesProvider))] string name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"capture set: {GetCurrentCaptureSet().Name}";
        if (_debugRegistry == null) return "debug registry not initialised";
        if (!_debugRegistry.TryFindCaptureSetByName(name, out var set, out int index))
            return $"unknown capture set: '{name}'";
        _f10CaptureSetIndex = index;
        return $"capture set: {set.Name}";
    }

    [ConsoleCommand("capture", "Trigger F10 capture using current set. Closes console during capture so it stays out of screenshots, then reopens.", MonoTargetType.Single)]
    async Awaitable CaptureCmd(CancellationToken ct)
    {
        bool reopenConsole = false;
        ServiceLocator.TryGet<IConsoleService>(out var console);

        try
        {
            if (console != null && console.IsOpen)
            {
                reopenConsole = true;
                console.Close();
                float endTime = Time.unscaledTime + 0.2f;
                while (Time.unscaledTime < endTime)
                    await Awaitable.NextFrameAsync(ct);
            }

            await CaptureCurrentSetAsync(ct);
        }
        finally
        {
            if (reopenConsole && console != null) console.Open();
        }
    }

    public async Awaitable CaptureCurrentSetAsync(CancellationToken ct)
    {
        if (_pipeline == null) return;
        await _pipeline.CaptureCurrentSetAsync(ct);
    }
}
