using System.Threading;
using UnityEngine;

[DisallowMultipleComponent]
[CommandPrefix("debug")]
public class DebugCaptureController : MonoBehaviour
{
    static readonly Color DebugOverlayBackgroundColor = new(0f, 0f, 0f, 0.55f);
    static readonly int DebugSuppressWeatherPassesId = Shader.PropertyToID(ShaderGlobalIds.DebugSuppressWeatherPasses);
    static readonly int CloudViewStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudViewSteps);
    static readonly int CloudLightStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudLightSteps);

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

    [Header("Debug Capture")]
    static readonly bool SaveF10DebugScreenshots = true;
    const int DebugScreenshotMaxWidth = 960;
    const int DebugScreenshotMaxRuns = 6;
    const float DebugCaptureModeDelaySeconds = 0.12f;
    const float TimedCaptureSampleTimeoutSeconds = 10f;
    const bool RestoreDebugOffAfterCaptureSet = true;

    Light _cachedSunLight;
    ICameraRigContext _cachedCameraContext;
    ICelestialTimeController _cachedCelestialManager;
    IPrecipitationDebugControl _cachedPrecipitationController;
    IWeatherProvider _cachedWeatherProvider;
    static readonly int _climateMapResolutionId = Shader.PropertyToID(ShaderGlobalIds.ClimateMapResolution);
    DebugRegistry _debugRegistry;
    DebugModeId _currentDebugModeId;
    int _f10CaptureSetIndex = -1;
    bool _profilingFrameRateEnabled;
    bool _debugScreenshotCaptureRunning;
    bool _precipitationToggleFlashActive;
    float _precipitationToggleFlashUntil;
    string _precipitationToggleFlashMessage;
    GUIStyle _debugOverlayPanelStyle;
    Texture2D _debugOverlayPanelTexture;
    Vector2 _debugOverlayScroll;

    void Awake()
    {
        InitializeRegistry();
        // Register the registry with the service locator so console completion providers
        // (DebugModeNamesProvider, DebugCaptureSetNamesProvider) can enumerate modes/sets.
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
        if (_debugOverlayPanelTexture != null)
        {
            Destroy(_debugOverlayPanelTexture);
            _debugOverlayPanelTexture = null;
        }
        if (_debugRegistry != null)
            ServiceLocator.Unregister<DebugRegistry>(_debugRegistry);
    }

    void OnDebugPrecipitationToggleRequested(DebugPrecipitationToggleRequestedEvent _)
    {
        TogglePrecipitationRendering();
    }

    void OnDebugCaptureSetCycleRequested(DebugCaptureSetCycleRequestedEvent _)
    {
        CycleF10CaptureSet();
    }

    void OnDebugCaptureRequested(DebugCaptureRequestedEvent _)
    {
        TriggerDebugCapture();
    }

    void OnDebugSunFreezeToggleRequested(DebugSunFreezeToggleRequestedEvent _)
    {
        ToggleSunFreeze();
    }

    void OnDebugOverlayToggleRequested(DebugOverlayToggleRequestedEvent _)
    {
        ShowDebugOverlay = !ShowDebugOverlay;
    }

    void OnDebugDetailedToggleRequested(DebugDetailedToggleRequestedEvent _)
    {
        ShowDetailedDebug = !ShowDetailedDebug;
    }

    void OnDebugProfilingToggleRequested(DebugProfilingToggleRequestedEvent _)
    {
        ToggleProfilingFrameRate();
    }

    void TogglePrecipitationRendering()
    {
        IPrecipitationDebugControl controller = _cachedPrecipitationController;
        if (controller == null)
            return;

        bool next = !controller.PrecipitationRenderingEnabled;
        controller.PrecipitationRenderingEnabled = next;
        _precipitationToggleFlashActive = true;
        _precipitationToggleFlashUntil = Time.unscaledTime + 1.2f;
        _precipitationToggleFlashMessage = $"Precipitation: {(next ? "ON" : "OFF")}";
    }

    void CycleF10CaptureSet()
    {
        _f10CaptureSetIndex = _debugRegistry.GetNextCaptureSetIndex(_f10CaptureSetIndex);
    }

    void ToggleSunFreeze()
    {
        ICelestialTimeController celestial = _cachedCelestialManager;
        if (celestial != null)
            celestial.ToggleTimeFrozen();
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

    void TriggerDebugCapture()
    {
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
        if (captureSet.Behavior == DebugCaptureSetBehavior.CurrentModeOnly)
        {
            CycleDebugMode();
            QueueDebugScreenshot();
            return;
        }

        if (!SaveF10DebugScreenshots)
        {
            CycleDebugMode();
            return;
        }

        QueueDebugCapture(captureSet, GetDebugCaptureModes(), captureScreenshots: true);
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

    void QueueDebugCapture(
        DebugCaptureSetDefinition captureSet,
        DebugModeId[] modes,
        bool captureScreenshots)
    {
        if (_debugScreenshotCaptureRunning || !gameObject.activeInHierarchy || modes == null || modes.Length == 0)
            return;

        _ = CaptureDebugSequenceAsync(captureSet, modes, captureScreenshots, CancellationToken.None);
    }

    async Awaitable CaptureDebugSequenceAsync(
        DebugCaptureSetDefinition captureSet,
        DebugModeId[] modes,
        bool captureScreenshots,
        CancellationToken ct)
    {
        _debugScreenshotCaptureRunning = true;
        DebugScreenshotFiles.RecordLastCaptureCamera();
        DebugModeId restoreMode = RestoreDebugOffAfterCaptureSet ? _debugRegistry.DefaultModeId : _currentDebugModeId;
        bool timedCapture = captureSet.TimingSamplesPerMode > 0;
        int restoreFrameRate = Application.targetFrameRate;
        int restoreVSync = QualitySettings.vSyncCount;
        float restoreDebugSuppressWeatherPasses = Shader.GetGlobalFloat(DebugSuppressWeatherPassesId);
        int restoreCloudViewSteps = Shader.GetGlobalInt(CloudViewStepsId);
        int restoreCloudLightSteps = Shader.GetGlobalInt(CloudLightStepsId);
        bool suppressWeatherPasses = captureSet.Id == DebugCoreIds.PerformanceWaterVolumeStages;
        bool cloudStepCapture = captureSet.Id == DebugCoreIds.PerformanceCloudSteps;
        ICelestialTimeController celestial = _cachedCelestialManager;
        float restoreTimeOfDay = celestial != null ? celestial.TimeOfDay : 0f;
        bool restoreTimeFrozen = celestial != null && celestial.IsTimeFrozen;
        LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 start. Modes={modes.Length}, CaptureScreenshots={captureScreenshots}");

        try
        {
            if (timedCapture)
            {
                if (celestial == null)
                    throw new System.InvalidOperationException("Timed debug captures require an ICelestialTimeController.");

                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Mathf.Max(ProfilingFrameRate, CappedFrameRate + 1);
                Shader.SetGlobalFloat(DebugSuppressWeatherPassesId, suppressWeatherPasses ? 1f : 0f);
                celestial.SetTimeFrozen(true);
                if (!celestial.TrySetLocalTimeOfDay(TimedCaptureLocalTime))
                    throw new System.InvalidOperationException("Timed debug capture could not set local celestial time.");

                await WaitForDebugModeRenderAsync(ct);
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                DebugModeDefinition mode = _debugRegistry.GetMode(modes[i]);
                string modeName = mode.Name;
                ApplyDebugMode(mode.Id);
                if (cloudStepCapture)
                    ApplyCloudStepBenchmark(mode.Id.LocalId, restoreCloudViewSteps, restoreCloudLightSteps);
                LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 step {i + 1}/{modes.Length}: mode {mode.Id}:{modeName}");

                await WaitForDebugModeRenderAsync(ct);
                if (captureSet.TimingSamplesPerMode > 0)
                {
                    FrameTimingCounters.Reset();
                    await WaitForFrameTimingSamplesAsync(
                        captureSet.TimingSamplesPerMode,
                        ct);
                }

                if (captureScreenshots)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        SaveDebugScreenshot(mode.Id, modeName);
                    }
                    catch (System.Exception ex)
                    {
                        LoggerProvider.LogException("DebugCapture", ex);
                    }
                }
            }
        }
        finally
        {
            ApplyDebugMode(restoreMode);
            if (timedCapture)
            {
                if (celestial != null)
                {
                    celestial.SetTimeOfDay(restoreTimeOfDay);
                    celestial.SetTimeFrozen(restoreTimeFrozen);
                }

                Application.targetFrameRate = restoreFrameRate;
                QualitySettings.vSyncCount = restoreVSync;
                Shader.SetGlobalFloat(DebugSuppressWeatherPassesId, restoreDebugSuppressWeatherPasses);
                Shader.SetGlobalInt(CloudViewStepsId, restoreCloudViewSteps);
                Shader.SetGlobalInt(CloudLightStepsId, restoreCloudLightSteps);
            }

            _debugScreenshotCaptureRunning = false;
            LoggerProvider.Log(LogLevel.Debug, "DebugCapture", "F10 end.");
        }
    }

    static void ApplyCloudStepBenchmark(int mode, int baselineViewSteps, int baselineLightSteps)
    {
        switch (mode)
        {
            case DebugModeConstants.PerformanceCloud72x8:
                Shader.SetGlobalInt(CloudViewStepsId, 72);
                Shader.SetGlobalInt(CloudLightStepsId, 8);
                break;
            case DebugModeConstants.PerformanceCloud48x8:
                Shader.SetGlobalInt(CloudViewStepsId, 48);
                Shader.SetGlobalInt(CloudLightStepsId, 8);
                break;
            case DebugModeConstants.PerformanceCloud72x4:
                Shader.SetGlobalInt(CloudViewStepsId, 72);
                Shader.SetGlobalInt(CloudLightStepsId, 4);
                break;
            case DebugModeConstants.PerformanceCloud48x4:
                Shader.SetGlobalInt(CloudViewStepsId, 48);
                Shader.SetGlobalInt(CloudLightStepsId, 4);
                break;
            default:
                Shader.SetGlobalInt(CloudViewStepsId, Mathf.Max(1, baselineViewSteps));
                Shader.SetGlobalInt(CloudLightStepsId, Mathf.Max(1, baselineLightSteps));
                break;
        }
    }

    static async Awaitable WaitForFrameTimingSamplesAsync(
        int requiredSamples,
        CancellationToken ct)
    {
        float timeoutAt = Time.realtimeSinceStartup + TimedCaptureSampleTimeoutSeconds;
        while (FrameTimingCounters.CompletedSampleCount < requiredSamples)
        {
            ct.ThrowIfCancellationRequested();
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                LoggerProvider.Log(
                    LogLevel.Warning,
                    "DebugCapture",
                    $"Timed capture reached {FrameTimingCounters.CompletedSampleCount}/{requiredSamples} samples before timeout.");
                break;
            }

            await Awaitable.NextFrameAsync(ct);
        }

        await Awaitable.EndOfFrameAsync();
    }

    void QueueDebugScreenshot()
    {
        if (!SaveF10DebugScreenshots || _debugScreenshotCaptureRunning || !gameObject.activeInHierarchy)
            return;

        string modeName = _debugRegistry.GetModeName(_currentDebugModeId);
        _ = CaptureDebugScreenshotAsync(_currentDebugModeId, modeName, CancellationToken.None);
    }

    async Awaitable CaptureDebugScreenshotAsync(
        DebugModeId modeId,
        string modeName,
        CancellationToken ct)
    {
        _debugScreenshotCaptureRunning = true;
        DebugScreenshotFiles.RecordLastCaptureCamera();

        try
        {
            await WaitForDebugModeRenderAsync(ct);
            ct.ThrowIfCancellationRequested();
            SaveDebugScreenshot(modeId, modeName);
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            LoggerProvider.LogException("DebugCapture", ex);
        }
        finally
        {
            _debugScreenshotCaptureRunning = false;
        }
    }

    async Awaitable WaitForDebugModeRenderAsync(CancellationToken ct)
    {
        await Awaitable.NextFrameAsync(ct);

        if (DebugCaptureModeDelaySeconds > 0f)
            await WaitUnscaledAsync(DebugCaptureModeDelaySeconds, ct);

        await Awaitable.NextFrameAsync(ct);
        await Awaitable.EndOfFrameAsync();
        ct.ThrowIfCancellationRequested();
    }

    static async Awaitable WaitUnscaledAsync(float seconds, CancellationToken ct)
    {
        float endTime = Time.unscaledTime + Mathf.Max(0f, seconds);
        while (Time.unscaledTime < endTime)
            await Awaitable.NextFrameAsync(ct);
    }

    void SaveDebugScreenshot(DebugModeId modeId, string modeName)
    {
        Texture2D source = null;
        Texture2D resized = null;

        try
        {
            source = ScreenCapture.CaptureScreenshotAsTexture();
            resized = DebugScreenshotFiles.Downsample(source, DebugScreenshotMaxWidth);

            string directory = DebugScreenshotFiles.GetDirectory();
            System.IO.Directory.CreateDirectory(directory);

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string safeModeName = DebugScreenshotFiles.SanitizeFilePart(modeName);
            string safeModeId = DebugScreenshotFiles.SanitizeFilePart(modeId.ToString());
            string baseName = $"F10-{safeModeId}-{safeModeName}-{timestamp}";
            string imagePath = System.IO.Path.Combine(directory, baseName + ".png");
            string metadataPath = System.IO.Path.Combine(directory, baseName + ".txt");

            System.IO.File.WriteAllBytes(imagePath, resized.EncodeToPNG());
            System.IO.File.WriteAllText(metadataPath, BuildDebugCaptureMetadata(
                modeId,
                modeName,
                source.width,
                source.height,
                resized.width,
                resized.height,
                imagePath));

            int modesPerRun = GetDebugCaptureModes().Length;
            int keepFiles = Mathf.Max(1, DebugScreenshotMaxRuns) * Mathf.Max(1, modesPerRun) * 2;
            DebugScreenshotFiles.Prune(directory, keepFiles);

            LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"Saved F10 debug screenshot: {imagePath}");
        }
        finally
        {
            if (source != null)
                Destroy(source);
            if (resized != null && resized != source)
                Destroy(resized);
        }
    }

    string BuildDebugCaptureMetadata(DebugModeId modeId, string modeName, int sourceWidth, int sourceHeight, int savedWidth, int savedHeight, string imagePath)
    {
        var inputs = new DebugCaptureMetadataInputs(
            _debugRegistry,
            GetCurrentCaptureSet(),
            _cachedCameraContext,
            _cachedCelestialManager,
            _cachedSunLight,
            _cachedPrecipitationController,
            _cachedWeatherProvider,
            _climateMapResolutionId,
            ShowDetailedDebug,
            IncludeMeshIntegrityInDebugCaptures);
        return DebugCaptureMetadataBuilder.Build(
            inputs, modeId, modeName, sourceWidth, sourceHeight, savedWidth, savedHeight, imagePath);
    }

    DebugRuntimeState CreateRuntimeState(DebugModeId modeId, string modeName)
    {
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
        return new DebugRuntimeState(
            _debugRegistry,
            _cachedCameraContext,
            _cachedCelestialManager,
            _cachedPrecipitationController,
            _cachedWeatherProvider,
            modeId,
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
        if (!ShowDebugOverlay)
        {
            DrawDebugOverlayHint();
            return;
        }

        ICameraRigContext cameraContext = _cachedCameraContext;
        if (cameraContext == null)
            return;

        GUILayout.BeginArea(GetDebugOverlayRect(), GetDebugOverlayPanelStyle());
        _debugOverlayScroll = GUILayout.BeginScrollView(_debugOverlayScroll);

        // F6 tier: high-value glanceable info, always visible.
        GUILayout.Label("Camera");
        GUILayout.Label($"Position: {cameraContext.CameraTransform.position.x:F1}, {cameraContext.CameraTransform.position.y:F1}, {cameraContext.CameraTransform.position.z:F1}");
        if (cameraContext.TargetCenter != null)
        {
            Vector3 dirToSurface = (cameraContext.CameraTransform.position - cameraContext.TargetCenter.position).normalized;
            (float lat, float lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}\u00b0 Lon: {lon * Mathf.Rad2Deg:F1}\u00b0");
            float distToCenter = Vector3.Distance(cameraContext.CameraTransform.position, cameraContext.TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Space(6);
        GUILayout.Label("Performance");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");
        GUILayout.Label($"Frame: CPU={FormatFrameMs(FrameTimingCounters.LastCpuFrameMs)} GPU={FormatFrameMs(FrameTimingCounters.LastGpuFrameMs)}");

        if (_precipitationToggleFlashActive)
        {
            if (Time.unscaledTime <= _precipitationToggleFlashUntil)
                GUILayout.Label(_precipitationToggleFlashMessage);
            else
                _precipitationToggleFlashActive = false;
        }

        if (!ShowDetailedDebug)
            GUILayout.Label("F9 = detailed debug");

        // F9 tier: controls reference + toggle/system state, shown only when detailed debug is on.
        if (ShowDetailedDebug)
        {
            GUILayout.Space(6);
            GUILayout.Label("Controls");
            GUILayout.Label("RMB=Look, WASD=Move, Shift=Fast, QE=Up/Down, ZC=Roll");
            GUILayout.Label("Space=Toggle Orbit/Surface, Backspace=Face Sun, R=Frame Storm");
            GUILayout.Label("F6=Debug UI, F7=Cycle F10 Set, F8=Freeze Sun, F9=Detailed, F11=FPS Cap, P=Precip");
            GUILayout.Label("M=Drop scale markers @ look, Shift+M=Clear, T=Teleport to markers");

            GUILayout.Space(6);
            GUILayout.Label("State");
            DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
            GUILayout.Label($"F10={captureSet.Name} capture ({GetDebugCaptureModes().Length} modes, current {_debugRegistry.GetModeName(_currentDebugModeId)})");
            IPrecipitationDebugControl precipitation = _cachedPrecipitationController;
            if (precipitation != null)
            {
                GUILayout.Label($"Precipitation render: {(precipitation.PrecipitationRenderingEnabled ? "ON" : "OFF")}");
                GUILayout.Label($"Precip local particles: {(precipitation.ShouldRenderLocalParticles(cameraContext.CameraComponent) ? "ON" : "OFF")}");
            }

            GUILayout.Label($"Frame target: {Application.targetFrameRate}, vSync: {QualitySettings.vSyncCount}");

            ICelestialTimeController celestial = _cachedCelestialManager;
            if (celestial != null)
                GUILayout.Label($"Sun frozen: {(celestial.IsTimeFrozen ? "yes" : "no")}");

            if (celestial != null && cameraContext.PlanetRadius > 0f)
            {
                float sunElevation = Vector3.Dot(celestial.SunDirection, (cameraContext.CameraTransform.position - cameraContext.PlanetCenter).normalized);
                GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}\u00b0");
            }
        }

        DebugRuntimeState runtimeState = CreateRuntimeState(_currentDebugModeId, _debugRegistry.GetModeName(_currentDebugModeId));
        for (int i = 0; i < _debugRegistry.OverlayContributors.Count; i++)
            _debugRegistry.OverlayContributors[i].DrawOverlay(runtimeState);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawDebugOverlayHint()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 132f, 30f), GetDebugOverlayPanelStyle());
        GUILayout.Label("F6: debug data");
        GUILayout.EndArea();
    }

    Rect GetDebugOverlayRect()
    {
        float width = Mathf.Min(820f, Mathf.Max(320f, Screen.width - 20f));
        float available = Screen.height - 20f;
        float height = ShowDetailedDebug ? available : Mathf.Min(230f, available);
        return new Rect(10f, 10f, width, height);
    }

    static string FormatFrameMs(double ms) => ms < 0.0 ? "?" : $"{ms:F1}ms";

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
    async Awaitable CaptureCmd(System.Threading.CancellationToken ct)
    {
        bool reopenConsole = false;
        ServiceLocator.TryGet<IConsoleService>(out var console);

        try
        {
            if (console != null && console.IsOpen)
            {
                reopenConsole = true;
                console.Close();
                // Let the console fade-out finish before grabbing the screenshot so we don't
                // catch it mid-alpha. FadeDuration is 0.12s; round up to be safe.
                await WaitUnscaledAsync(0.2f, ct);
            }

            await CaptureCurrentSetAsync(ct);
        }
        finally
        {
            if (reopenConsole && console != null) console.Open();
        }
    }

    /// <summary>
    /// Public async entry point for triggering the current F10 capture set. Equivalent to the
    /// F10 keypath but awaitable so callers (the console <c>debug.capture</c> command) can wrap
    /// it with close/reopen logic.
    /// </summary>
    public async Awaitable CaptureCurrentSetAsync(System.Threading.CancellationToken ct)
    {
        if (_debugRegistry == null) return;
        if (_debugScreenshotCaptureRunning)
            throw new System.InvalidOperationException("A debug capture is already running.");

        ct.ThrowIfCancellationRequested();
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();

        if (captureSet.Behavior == DebugCaptureSetBehavior.CurrentModeOnly)
        {
            CycleDebugMode();
            await CaptureDebugScreenshotAsync(
                _currentDebugModeId,
                _debugRegistry.GetModeName(_currentDebugModeId),
                ct);
            return;
        }

        if (!SaveF10DebugScreenshots)
        {
            CycleDebugMode();
            return;
        }

        await CaptureDebugSequenceAsync(
            captureSet,
            GetDebugCaptureModes(),
            captureScreenshots: true,
            ct);
    }

    GUIStyle GetDebugOverlayPanelStyle()
    {
        if (_debugOverlayPanelStyle != null)
            return _debugOverlayPanelStyle;

        _debugOverlayPanelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _debugOverlayPanelTexture.SetPixel(0, 0, DebugOverlayBackgroundColor);
        _debugOverlayPanelTexture.Apply();

        _debugOverlayPanelStyle = new GUIStyle(GUI.skin.box)
        {
            normal =
            {
                background = _debugOverlayPanelTexture
            },
            padding = new RectOffset(10, 10, 8, 8),
            border = new RectOffset(4, 4, 4, 4)
        };

        return _debugOverlayPanelStyle;
    }
}

