using UnityEngine;

[DisallowMultipleComponent]
public class DebugCaptureController : MonoBehaviour
{
    [Header("Debug Runtime")]
    public int CappedFrameRate = 60;
    public int ProfilingFrameRate = 1000;

    [Header("Debug Info")]
    public bool ShowDebugOverlay = true;
    public bool ShowWaterDebugDetails;
    public bool IncludeMeshIntegrityInDebugCaptures;

    [Header("Debug Capture")]
    static readonly bool SaveF10DebugScreenshots = true;
    static readonly int DebugScreenshotMaxWidth = 960;
    static readonly int DebugScreenshotMaxRuns = 6;
    static readonly float DebugCaptureModeDelaySeconds = 0.12f;
    static readonly bool RestoreDebugOffAfterCaptureSet = true;
    static readonly string DebugScreenshotFolder = "local-only/debug-screenshots";

    Light _cachedSunLight;
    ICameraRigContext _cachedCameraContext;
    ICelestialTimeController _cachedCelestialManager;
    IPrecipitationDebugControl _cachedPrecipitationController;
    IWeatherProvider _cachedWeatherProvider;
    DebugRegistry _debugRegistry;
    DebugModeId _currentDebugModeId;
    int _f10CaptureSetIndex = -1;
    bool _profilingFrameRateEnabled;
    bool _debugScreenshotCaptureRunning;
    bool _precipitationToggleFlashActive;
    float _precipitationToggleFlashUntil;
    string _precipitationToggleFlashMessage;

    void Awake()
    {
        InitializeRegistry();
    }

    void OnEnable()
    {
        ApplyDebugMode(_currentDebugModeId);
        EventBus<DebugPrecipitationToggleRequestedEvent>.Listen(OnDebugPrecipitationToggleRequested);
        EventBus<DebugCaptureSetCycleRequestedEvent>.Listen(OnDebugCaptureSetCycleRequested);
        EventBus<DebugCaptureRequestedEvent>.Listen(OnDebugCaptureRequested);
        EventBus<DebugSunFreezeToggleRequestedEvent>.Listen(OnDebugSunFreezeToggleRequested);
        EventBus<DebugWaterDebugDetailsToggleRequestedEvent>.Listen(OnDebugWaterDebugDetailsToggleRequested);
        EventBus<DebugProfilingToggleRequestedEvent>.Listen(OnDebugProfilingToggleRequested);
    }

    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        InitializeRegistry();
        _cachedCameraContext = ServiceLocator.Get<ICameraRigContext>();
        _cachedCelestialManager = ServiceLocator.Get<ICelestialTimeController>();
        _cachedPrecipitationController = ServiceLocator.Get<IPrecipitationDebugControl>();
        _cachedWeatherProvider = ServiceLocator.Get<IWeatherProvider>();
    }

    void InitializeRegistry()
    {
        if (_debugRegistry != null)
            return;

        _debugRegistry = new DebugRegistry();
        _debugRegistry.RegisterModule(new WaterDebugModule());
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
        EventBus<DebugWaterDebugDetailsToggleRequestedEvent>.Unlisten(OnDebugWaterDebugDetailsToggleRequested);
        EventBus<DebugProfilingToggleRequestedEvent>.Unlisten(OnDebugProfilingToggleRequested);
        _debugRegistry?.ClearModes();
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

    void OnDebugWaterDebugDetailsToggleRequested(DebugWaterDebugDetailsToggleRequestedEvent _)
    {
        ShowWaterDebugDetails = !ShowWaterDebugDetails;
    }

    void OnDebugProfilingToggleRequested(DebugProfilingToggleRequestedEvent _)
    {
        ToggleProfilingFrameRate();
    }

    void TogglePrecipitationRendering()
    {
        IPrecipitationDebugControl controller = GetPrecipitationController();
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
        ICelestialTimeController celestial = GetCelestialManager();
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

        QueueDebugCapture(GetDebugCaptureModes(), captureScreenshots: true);
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

    void QueueDebugCapture(DebugModeId[] modes, bool captureScreenshots)
    {
        if (_debugScreenshotCaptureRunning || !gameObject.activeInHierarchy || modes == null || modes.Length == 0)
            return;

        _ = CaptureDebugSequenceAsync(modes, captureScreenshots);
    }

    async Awaitable CaptureDebugSequenceAsync(DebugModeId[] modes, bool captureScreenshots)
    {
        _debugScreenshotCaptureRunning = true;
        DebugModeId restoreMode = RestoreDebugOffAfterCaptureSet ? _debugRegistry.DefaultModeId : _currentDebugModeId;
        LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 start. Modes={modes.Length}, CaptureScreenshots={captureScreenshots}");

        try
        {
            for (int i = 0; i < modes.Length; i++)
            {
                DebugModeDefinition mode = _debugRegistry.GetMode(modes[i]);
                string modeName = mode.Name;
                ApplyDebugMode(mode.Id);
                LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 step {i + 1}/{modes.Length}: mode {mode.Id}:{modeName}");

                await WaitForDebugModeRenderAsync();

                if (captureScreenshots)
                {
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
            _debugScreenshotCaptureRunning = false;
            LoggerProvider.Log(LogLevel.Debug, "DebugCapture", "F10 end.");
        }
    }

    void QueueDebugScreenshot()
    {
        if (!SaveF10DebugScreenshots || _debugScreenshotCaptureRunning || !gameObject.activeInHierarchy)
            return;

        string modeName = _debugRegistry.GetModeName(_currentDebugModeId);
        _ = CaptureDebugScreenshotAsync(_currentDebugModeId, modeName);
    }

    async Awaitable CaptureDebugScreenshotAsync(DebugModeId modeId, string modeName)
    {
        _debugScreenshotCaptureRunning = true;

        try
        {
            await WaitForDebugModeRenderAsync();
            SaveDebugScreenshot(modeId, modeName);
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

    async Awaitable WaitForDebugModeRenderAsync()
    {
        await Awaitable.NextFrameAsync();

        if (DebugCaptureModeDelaySeconds > 0f)
            await Awaitable.WaitForSecondsAsync(DebugCaptureModeDelaySeconds);

        await Awaitable.NextFrameAsync();
        await Awaitable.EndOfFrameAsync();
    }

    void SaveDebugScreenshot(DebugModeId modeId, string modeName)
    {
        Texture2D source = null;
        Texture2D resized = null;

        try
        {
            source = ScreenCapture.CaptureScreenshotAsTexture();
            resized = DownsampleTexture(source, DebugScreenshotMaxWidth);

            string directory = GetDebugScreenshotDirectory();
            System.IO.Directory.CreateDirectory(directory);

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string safeModeName = SanitizeFilePart(modeName);
            string safeModeId = SanitizeFilePart(modeId.ToString());
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
            PruneDebugScreenshots(directory);

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

    Texture2D DownsampleTexture(Texture2D source, int maxWidth)
    {
        int targetWidth = Mathf.Clamp(maxWidth, 160, 1920);
        if (source.width <= targetWidth)
            return source;

        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * (targetWidth / (float)source.width)));
        RenderTexture previous = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);

        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            scaled.Apply(false, false);
            return scaled;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    string GetDebugScreenshotDirectory()
    {
        string folder = string.IsNullOrWhiteSpace(DebugScreenshotFolder)
            ? "local-only/debug-screenshots"
            : DebugScreenshotFolder;

        return System.IO.Path.IsPathRooted(folder)
            ? folder
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", folder));
    }

    void PruneDebugScreenshots(string directory)
    {
        int modesPerRun = GetDebugCaptureModes().Length;
        int keepFiles = Mathf.Max(1, DebugScreenshotMaxRuns) * Mathf.Max(1, modesPerRun) * 2;
        if (keepFiles <= 0 || string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            return;

        System.Collections.Generic.List<System.IO.FileInfo> captures = new System.Collections.Generic.List<System.IO.FileInfo>();
        System.IO.DirectoryInfo dir = new System.IO.DirectoryInfo(directory);
        System.IO.FileInfo[] files = dir.GetFiles("F10-*.*", System.IO.SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string extension = files[i].Extension;
            if (string.Equals(extension, ".png", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".txt", System.StringComparison.OrdinalIgnoreCase))
            {
                captures.Add(files[i]);
            }
        }

        if (captures.Count <= keepFiles)
            return;

        captures.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int i = keepFiles; i < captures.Count; i++)
        {
            try
            {
                captures[i].Delete();
            }
            catch (System.Exception ex)
            {
                LoggerProvider.Log(LogLevel.Warning, "DebugCapture", $"Could not prune F10 debug capture '{captures[i].FullName}': {ex.Message}");
            }
        }
    }

    static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "capture";

        char[] chars = value.ToCharArray();
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                chars[i] = '_';
        }

        return new string(chars);
    }

    string BuildDebugCaptureMetadata(DebugModeId modeId, string modeName, int sourceWidth, int sourceHeight, int savedWidth, int savedHeight, string imagePath)
    {
        ICameraRigContext cameraContext = _cachedCameraContext;
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== F10 DEBUG CAPTURE ===");
        sb.AppendLine($"Image: {imagePath}");
        sb.AppendLine($"Source: {sourceWidth}x{sourceHeight}");
        sb.AppendLine($"Saved: {savedWidth}x{savedHeight}");
        sb.AppendLine($"Mode: {modeId}:{modeName}");
        sb.AppendLine($"CaptureSet: {captureSet.Name} ({captureSet.Id})");
        sb.AppendLine($"Time: {System.DateTime.Now:O}");
        sb.AppendLine();

        sb.AppendLine("--- Camera ---");
        if (cameraContext != null)
        {
            sb.AppendLine($"Position: {cameraContext.CameraTransform.position.x:F2}, {cameraContext.CameraTransform.position.y:F2}, {cameraContext.CameraTransform.position.z:F2}");
            sb.AppendLine($"Forward: {cameraContext.CameraTransform.forward.x:F4}, {cameraContext.CameraTransform.forward.y:F4}, {cameraContext.CameraTransform.forward.z:F4}");
            sb.AppendLine($"Surface view: {cameraContext.SurfaceView}");
            if (cameraContext.TargetCenter != null)
            {
                Vector3 dirToSurface = (cameraContext.CameraTransform.position - cameraContext.TargetCenter.position).normalized;
                (float lat, float lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
                sb.AppendLine($"LatLonDeg: {lat * Mathf.Rad2Deg:F2}, {lon * Mathf.Rad2Deg:F2}");
                sb.AppendLine($"DistanceToCenter: {Vector3.Distance(cameraContext.CameraTransform.position, cameraContext.TargetCenter.position):F2}");
            }
            sb.AppendLine($"PlanetRadius: {cameraContext.PlanetRadius:F2}");
            sb.AppendLine($"SeaLevelRadius: {cameraContext.SeaLevelRadius:F2}");
            sb.AppendLine($"ElevationMinMax: {cameraContext.ElevationMin:F4}, {cameraContext.ElevationMax:F4}");
        }
        sb.AppendLine();

        sb.AppendLine("--- Runtime ---");
        sb.AppendLine($"FPS: {(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F1}");
        sb.AppendLine($"FrameTarget: {Application.targetFrameRate}");
        sb.AppendLine($"VSync: {QualitySettings.vSyncCount}");
        ICelestialTimeController celestial = _cachedCelestialManager;
        if (celestial != null)
            sb.AppendLine($"SunFrozen: {celestial.IsTimeFrozen}");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && cameraContext != null && cameraContext.PlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (cameraContext.CameraTransform.position - cameraContext.PlanetCenter).normalized);
            sb.AppendLine($"SunElevationDeg: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F2}");
        }
        IPrecipitationDebugControl precipitation = _cachedPrecipitationController;
        if (precipitation != null && cameraContext != null)
        {
            sb.AppendLine($"PrecipitationEnabled: {precipitation.PrecipitationRenderingEnabled}");
            sb.AppendLine($"PrecipLocalParticlesEnabled: {precipitation.LocalPrecipitationParticlesEnabled}");
            sb.AppendLine($"PrecipLocalParticlesForCamera: {precipitation.ShouldRenderLocalParticles(cameraContext.CameraComponent)}");
        }
        sb.AppendLine();

        DebugRuntimeState runtimeState = CreateRuntimeState(modeId, modeName);
        var captureContext = new DebugCaptureContext(
            runtimeState,
            modeId,
            modeName,
            sourceWidth,
            sourceHeight,
            savedWidth,
            savedHeight,
            imagePath);

        for (int i = 0; i < _debugRegistry.Diagnostics.Count; i++)
        {
            IDebugDiagnosticProvider diagnostic = _debugRegistry.Diagnostics[i];
            if (!diagnostic.IsEnabled)
                continue;

            diagnostic.Refresh(runtimeState);
            diagnostic.AppendCachedResult(captureContext, sb);
        }

        for (int i = 0; i < _debugRegistry.MetadataProviders.Count; i++)
            _debugRegistry.MetadataProviders[i].AppendMetadata(captureContext, sb);

        return sb.ToString();
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
            ShowWaterDebugDetails,
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

    ICelestialTimeController GetCelestialManager()
    {
        return _cachedCelestialManager;
    }

    IPrecipitationDebugControl GetPrecipitationController()
    {
        return _cachedPrecipitationController;
    }

    ICameraRigContext GetCameraContext()
    {
        return _cachedCameraContext;
    }

    void OnGUI()
    {
        if (!ShowDebugOverlay)
            return;

        ICameraRigContext cameraContext = _cachedCameraContext;
        if (cameraContext == null)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 520, ShowWaterDebugDetails ? 520 : 260));
        GUILayout.Label("Debug Camera");
        GUILayout.Label($"Position: {cameraContext.CameraTransform.position.x:F1}, {cameraContext.CameraTransform.position.y:F1}, {cameraContext.CameraTransform.position.z:F1}");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");

        if (cameraContext.TargetCenter != null)
        {
            Vector3 dirToSurface = (cameraContext.CameraTransform.position - cameraContext.TargetCenter.position).normalized;
            (float lat, float lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}\u00b0 Lon: {lon * Mathf.Rad2Deg:F1}\u00b0");
            float distToCenter = Vector3.Distance(cameraContext.CameraTransform.position, cameraContext.TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Label("RMB=Look, WASD=Move, Shift=Fast, QE=Up/Down, ZC=Roll");
        GUILayout.Label("Space=Toggle Orbit/Surface, Backspace=Face Sun, R=Frame Storm");
        GUILayout.Label("F7=Cycle F10 Set, F8=Freeze Sun, F9=Water Stats, F11=Toggle FPS Cap, P=Toggle Precip");
        DebugCaptureSetDefinition captureSet = GetCurrentCaptureSet();
        GUILayout.Label($"F10={captureSet.Name} capture ({GetDebugCaptureModes().Length} modes, current {_debugRegistry.GetModeName(_currentDebugModeId)})");
        IPrecipitationDebugControl precipitation = _cachedPrecipitationController;
        if (precipitation != null)
        {
            GUILayout.Label($"Precipitation render: {(precipitation.PrecipitationRenderingEnabled ? "ON" : "OFF")}");
            GUILayout.Label($"Precip local particles: {(precipitation.ShouldRenderLocalParticles(cameraContext.CameraComponent) ? "ON" : "OFF")}");
        }

        if (_precipitationToggleFlashActive)
        {
            if (Time.unscaledTime <= _precipitationToggleFlashUntil)
                GUILayout.Label(_precipitationToggleFlashMessage);
            else
                _precipitationToggleFlashActive = false;
        }

        GUILayout.Label($"Frame target: {Application.targetFrameRate}, vSync: {QualitySettings.vSyncCount}");

        ICelestialTimeController celestial = _cachedCelestialManager;
        if (celestial != null)
            GUILayout.Label($"Sun frozen: {(celestial.IsTimeFrozen ? "yes" : "no")}");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && cameraContext.PlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (cameraContext.CameraTransform.position - cameraContext.PlanetCenter).normalized);
            GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}\u00b0");
        }

        DebugRuntimeState runtimeState = CreateRuntimeState(_currentDebugModeId, _debugRegistry.GetModeName(_currentDebugModeId));
        for (int i = 0; i < _debugRegistry.OverlayContributors.Count; i++)
            _debugRegistry.OverlayContributors[i].DrawOverlay(runtimeState);

        GUILayout.EndArea();
    }
}
