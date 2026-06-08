using System.Threading;
using UnityEngine;

[DisallowMultipleComponent]
[CommandPrefix("debug")]
public class DebugCaptureController : MonoBehaviour
{
    static readonly Color DebugOverlayBackgroundColor = new(0f, 0f, 0f, 0.55f);

    [Header("Debug Runtime")]
    public int CappedFrameRate = 60;
    public int ProfilingFrameRate = 1000;

    [Header("Debug Info")]
    [System.NonSerialized]
    public bool ShowDebugOverlay;
    public bool ShowWaterDebugDetails;
    public bool IncludeMeshIntegrityInDebugCaptures;

    [Header("Debug Capture")]
    static readonly bool SaveF10DebugScreenshots = true;
    const int DebugScreenshotMaxWidth = 960;
    const int DebugScreenshotMaxRuns = 6;
    const float DebugCaptureModeDelaySeconds = 0.12f;
    const bool RestoreDebugOffAfterCaptureSet = true;
    const string DebugScreenshotFolder = "local-only/debug-screenshots";

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
    GUIStyle _debugOverlayPanelStyle;
    Texture2D _debugOverlayPanelTexture;

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
        _debugRegistry.RegisterModule(new GrassDebugModule());
        _debugRegistry.RegisterModule(new CloudDebugModule());
        _debugRegistry.RegisterModule(new MemoryDebugModule());
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
        EventBus<DebugWaterDebugDetailsToggleRequestedEvent>.Unlisten(OnDebugWaterDebugDetailsToggleRequested);
        EventBus<DebugProfilingToggleRequestedEvent>.Unlisten(OnDebugProfilingToggleRequested);
        _debugRegistry?.ClearModes();
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

        _ = CaptureDebugSequenceAsync(modes, captureScreenshots, CancellationToken.None);
    }

    async Awaitable CaptureDebugSequenceAsync(
        DebugModeId[] modes,
        bool captureScreenshots,
        CancellationToken ct)
    {
        _debugScreenshotCaptureRunning = true;
        RecordLastDebugCaptureCamera();
        DebugModeId restoreMode = RestoreDebugOffAfterCaptureSet ? _debugRegistry.DefaultModeId : _currentDebugModeId;
        LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 start. Modes={modes.Length}, CaptureScreenshots={captureScreenshots}");

        try
        {
            for (int i = 0; i < modes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                DebugModeDefinition mode = _debugRegistry.GetMode(modes[i]);
                string modeName = mode.Name;
                ApplyDebugMode(mode.Id);
                LoggerProvider.Log(LogLevel.Debug, "DebugCapture", $"F10 step {i + 1}/{modes.Length}: mode {mode.Id}:{modeName}");

                await WaitForDebugModeRenderAsync(ct);

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
            _debugScreenshotCaptureRunning = false;
            LoggerProvider.Log(LogLevel.Debug, "DebugCapture", "F10 end.");
        }
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
        RecordLastDebugCaptureCamera();

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
            sb.AppendLine($"Up: {cameraContext.CameraTransform.up.x:F4}, {cameraContext.CameraTransform.up.y:F4}, {cameraContext.CameraTransform.up.z:F4}");
            sb.AppendLine($"Right: {cameraContext.CameraTransform.right.x:F4}, {cameraContext.CameraTransform.right.y:F4}, {cameraContext.CameraTransform.right.z:F4}");
            Camera captureCamera = cameraContext.CameraComponent;
            if (captureCamera != null)
            {
                sb.AppendLine($"Projection: orthographic={captureCamera.orthographic}, fov={captureCamera.fieldOfView:F2}, aspect={captureCamera.aspect:F4}, near={captureCamera.nearClipPlane:F3}, far={captureCamera.farClipPlane:F1}");
                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(captureCamera);
                string[] planeNames = { "Left", "Right", "Bottom", "Top", "Near", "Far" };
                for (int i = 0; i < frustumPlanes.Length; i++)
                {
                    Plane plane = frustumPlanes[i];
                    Vector3 normal = plane.normal;
                    string planeName = i < planeNames.Length ? planeNames[i] : i.ToString();
                    sb.AppendLine($"FrustumPlane.{planeName}: normal=({normal.x:F5},{normal.y:F5},{normal.z:F5}), distance={plane.distance:F3}");
                }
            }
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
        int qualityLevel = QualitySettings.GetQualityLevel();
        string[] qualityNames = QualitySettings.names;
        string qualityName = qualityLevel >= 0 && qualityLevel < qualityNames.Length
            ? qualityNames[qualityLevel]
            : "Unknown";
        sb.AppendLine($"QualityLevel: {qualityLevel} ({qualityName})");
        sb.AppendLine($"CloudQuality: tier={QualityController.AppliedQualityTier}, low={QualityController.IsCloudLowQualityEnabled}, stepMultiplier={QualityController.CloudStepMultiplier:F2}");
        ICelestialTimeController celestial = _cachedCelestialManager;
        if (celestial != null)
            sb.AppendLine($"SunFrozen: {celestial.IsTimeFrozen}");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && cameraContext != null && cameraContext.PlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            sb.AppendLine($"SunDirection: {sd.x:F4}, {sd.y:F4}, {sd.z:F4}");
            sb.AppendLine($"SunLight: intensity={_cachedSunLight.intensity:F3}, color=({_cachedSunLight.color.r:F3},{_cachedSunLight.color.g:F3},{_cachedSunLight.color.b:F3})");
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

    static void RecordLastDebugCaptureCamera()
    {
        if (ServiceLocator.TryGet<ICameraTeleportRegistry>(out var teleports))
            teleports.RecordLastDebugCapture();
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
        GUILayout.Label("F6=Debug UI, F7=Cycle F10 Set, F8=Freeze Sun, F9=Water Stats, F11=FPS Cap, P=Precip");
        GUILayout.Label("M=Drop scale markers @ look, Shift+M=Clear, T=Teleport to markers");
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

    void DrawDebugOverlayHint()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 132f, 30f), GetDebugOverlayPanelStyle());
        GUILayout.Label("F6: debug data");
        GUILayout.EndArea();
    }

    Rect GetDebugOverlayRect()
    {
        float width = Mathf.Min(820f, Mathf.Max(320f, Screen.width - 20f));
        float targetHeight = ShowWaterDebugDetails ? Screen.height - 20f : 360f;
        float height = Mathf.Min(targetHeight, Mathf.Max(160f, Screen.height - 20f));
        return new Rect(10f, 10f, width, height);
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("overlay", "Get or set the F6 debug HUD overlay state.", MonoTargetType.Single)]
    string OverlayCmd(bool? on = null)
    {
        if (on == null) return $"debug overlay: {ShowDebugOverlay}";
        ShowDebugOverlay = on.Value;
        return $"debug overlay: {ShowDebugOverlay}";
    }

    [ConsoleCommand("water-details", "Get or set the expanded water debug HUD section.", MonoTargetType.Single)]
    string WaterDetailsCmd(bool? on = null)
    {
        if (on == null) return $"water details: {ShowWaterDebugDetails}";
        ShowWaterDebugDetails = on.Value;
        return $"water details: {ShowWaterDebugDetails}";
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
        IPrecipitationDebugControl c = GetPrecipitationController();
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

        await CaptureDebugSequenceAsync(GetDebugCaptureModes(), captureScreenshots: true, ct);
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

public static class CloudDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("cloud");
    public static readonly DebugCaptureSetId Diagnostics = new DebugCaptureSetId(Module, "diagnostics");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class CloudDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    const int Off = 0;
    const int Weather = 1;
    const int Storm = 2;
    const int Density = 3;
    const int OpticalDepth = 4;
    const int SilverLining = 5;
    const int MoistureSource = 6;
    const int CondensationChange = 7;

    static readonly int _cloudDebugModeId = Shader.PropertyToID("_CloudDebugMode");
    static readonly int _cloudWeatherResolutionId = Shader.PropertyToID("_CloudWeatherResolution");
    static readonly int _cloudInnerRadiusId = Shader.PropertyToID("_CloudInnerRadius");
    static readonly int _cloudOuterRadiusId = Shader.PropertyToID("_CloudOuterRadius");
    static readonly int _cloudViewStepsId = Shader.PropertyToID("_CloudViewSteps");
    static readonly int _cloudRayOffsetStrengthId = Shader.PropertyToID("_CloudRayOffsetStrength");
    static readonly int _cloudDensityThresholdId = Shader.PropertyToID("_CloudDensityThreshold");
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID("_CloudDensityMultiplier");

    public DebugModuleId Id => CloudDebugIds.Module;
    public DebugModuleId ModuleId => CloudDebugIds.Module;

    public void Register(DebugRegistry registry)
    {
        RegisterMode(registry, Off, "Off", "Clouds");
        RegisterMode(registry, Weather, "CloudWeather", "Clouds");
        RegisterMode(registry, Storm, "CloudStorm", "Clouds");
        RegisterMode(registry, Density, "CloudDensity", "Clouds");
        RegisterMode(registry, OpticalDepth, "CloudOpticalDepth", "Clouds");
        RegisterMode(registry, SilverLining, "CloudSilverLining", "Clouds");
        RegisterMode(registry, MoistureSource, "CloudMoistureSource", "Clouds");
        RegisterMode(registry, CondensationChange, "CloudCondensationChange", "Clouds");

        registry.RegisterCaptureSet(CloudDebugIds.Diagnostics, "Cloud Diagnostics",
            Modes(Off, Weather, Density, OpticalDepth, Storm, MoistureSource, CondensationChange));
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        Shader.SetGlobalInt(_cloudDebugModeId, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        Shader.SetGlobalInt(_cloudDebugModeId, Off);
    }

    public void AppendMetadata(DebugCaptureContext context, System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- Clouds ---");
        sb.AppendLine($"DebugMode: {Shader.GetGlobalInt(_cloudDebugModeId)} ({context.ModeId}:{context.ModeName})");
        sb.AppendLine($"WeatherResolution: {Shader.GetGlobalInt(_cloudWeatherResolutionId)}");
        sb.AppendLine($"LayerRadii: inner={Shader.GetGlobalFloat(_cloudInnerRadiusId):F2}, outer={Shader.GetGlobalFloat(_cloudOuterRadiusId):F2}");
        sb.AppendLine($"Raymarch: viewSteps={Shader.GetGlobalInt(_cloudViewStepsId)}, jitter={Shader.GetGlobalFloat(_cloudRayOffsetStrengthId):F2}");
        sb.AppendLine($"Density: threshold={Shader.GetGlobalFloat(_cloudDensityThresholdId):F3}, multiplier={Shader.GetGlobalFloat(_cloudDensityMultiplierId):F4}");

        IWeatherProvider weatherProvider = context.Runtime.WeatherProvider;
        ICameraRigContext cameraContext = context.Runtime.CameraContext;
        if (weatherProvider == null || cameraContext == null)
            return;

        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        sb.AppendLine($"CameraWeather: coverage={weather.CloudCoverage:F3}, storm={weather.StormIntensity:F3}, moisture={weather.MoistureSource:F3}, rain={weather.Precipitation:F3}, state={weather.State}");
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        GUILayout.Space(6);
        GUILayout.Label("Cloud Debug");
        GUILayout.Label($"Mode: {Shader.GetGlobalInt(_cloudDebugModeId)} ({state.CurrentModeId}:{state.CurrentModeName})");
        GUILayout.Label($"Weather: {Shader.GetGlobalInt(_cloudWeatherResolutionId)} px, view steps {Shader.GetGlobalInt(_cloudViewStepsId)}");
        GUILayout.Label($"Layer: {Shader.GetGlobalFloat(_cloudInnerRadiusId):F0}-{Shader.GetGlobalFloat(_cloudOuterRadiusId):F0}");

        IWeatherProvider weatherProvider = state.WeatherProvider;
        ICameraRigContext cameraContext = state.CameraContext;
        if (weatherProvider == null || cameraContext == null)
            return;

        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        GUILayout.Label($"Sample: cloud={weather.CloudCoverage:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(CloudDebugIds.Mode(localId), name, category);
    }

    static DebugModeId[] Modes(params int[] localIds)
    {
        DebugModeId[] modes = new DebugModeId[localIds.Length];
        for (int i = 0; i < localIds.Length; i++)
            modes[i] = CloudDebugIds.Mode(localIds[i]);
        return modes;
    }
}
