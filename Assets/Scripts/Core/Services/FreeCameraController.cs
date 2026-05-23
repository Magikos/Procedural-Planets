using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public enum OceanDebugCaptureSet
{
    CurrentModeOnly = 0,
    WaterArtifact = 1,
    AtmosphereWater = 3,
    WaterInterface = 4,
    Precipitation = 5,
    WaterVolumeDeepDive = 6,
    FullLoop = 2
}

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 10f;
    public float FastMultiplier = 3f;
    public float ScrollSpeed = 50f;
    public float OrbitSpeedMultiplier = 0.5f;
    public float SurfaceSpeedMultiplier = 0.02f;

    [Header("Look")]
    public float LookSensitivity = 2f;

    [Header("Auto Position")]
    public float ViewDistanceMultiplier = 2.5f;
    public float SurfaceHeight = 2f;
    [Range(1f, 30f)] public float SurfaceSunriseOffsetDegrees = 8f;
    public bool AutoPositionOnGenerate = true;

    [Header("Debug Runtime")]
    public int CappedFrameRate = 60;
    public int ProfilingFrameRate = 1000;

    [Header("Debug Info")]
    public bool ShowDebugOverlay = true;
    public bool ShowWaterDebugDetails;
    public Transform TargetCenter;

    [Header("Debug Capture")]
    public bool SaveF10DebugScreenshots = true;
    [Range(160, 1920)] public int DebugScreenshotMaxWidth = 960;
    [Range(1, 20)] public int DebugScreenshotMaxRuns = 6;
    public OceanDebugCaptureSet F10CaptureSet = OceanDebugCaptureSet.WaterArtifact;
    [Range(0.02f, 1f)] public float DebugCaptureModeDelaySeconds = 0.12f;
    public bool RestoreOceanDebugOffAfterCaptureSet = true;
    public string DebugScreenshotFolder = "local-only/debug-screenshots";

    float _lastPlanetRadius;
    float _lastSeaLevelRadius;
    float _lastElevationMin;
    float _lastElevationMax;
    Vector3 _lastPlanetCenter;

    Mouse _mouse;
    Keyboard _keyboard;
    bool _looking;
    bool _skipNextDelta;
    bool _surfaceView;
    Light _cachedSunLight;
    ICelestialTimeController _cachedCelestialManager;
    IPlanetSurfaceSampler _cachedPlanet;
    IPrecipitationDebugControl _cachedPrecipitationController;
    MonoBehaviour _cachedPrecipitationBehaviour;
    Renderer _cachedWaterRenderer;
    Mesh _cachedWaterMesh;
    WaterDebugStats _waterDebugStats;
    float _nextWaterDebugRefreshTime;
    Vector3 _sunOrbitAxis = Vector3.forward;
    Vector3 _lastSunDirectionToSun;
    int _oceanDebugMode;
    bool _profilingFrameRateEnabled;
    bool _debugScreenshotCaptureRunning;
    bool _precipitationToggleFlashActive;
    float _precipitationToggleFlashUntil;
    string _precipitationToggleFlashMessage;

    static readonly int _oceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _oceanFocusModeId = Shader.PropertyToID("_OceanFocusMode");
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveScaleId = Shader.PropertyToID("_WaveScale");
    static readonly int _waveSpeedId = Shader.PropertyToID("_WaveSpeed");
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly string[] OceanDebugModeNames =
    {
        "Off",
        "Depth",
        "Shore",
        "Body",
        "Lighting",
        "Glint",
        "Normals",
        "Foam",
        "MotionMask",
        "WaveHeight",
        "WaveSlope",
        "WaterData",
        "Absorption",
        "VolumeData",
        "VolumeMask",
        "VolumePath",
        "VolumeLight",
        "VolumeRefraction",
        "FoamParts",
        "SurfaceAlpha",
        "VolumeBoundary",
        "VolumeOptical",
        "SurfaceContact",
        "SurfaceBlend",
        "VolumeOnly",
        "SurfaceOnly",
        "WaterOff",
        "VolumeContact",
        "VolumeDilation",
        "VolumeNoRefraction",
        "VolumeOcclusion",
        "TerrainSourcePink",
        "FoamPink",
        "VolumeSphere",
        "TerrainFaceId",
        "SeaRay",
        "SeaVsMesh",
        "SeaPath",
        "SeaMatte",
        "SeaSourceMatte",
        "AtmosphereBypass",
        "VolumeAfterAtmosphere",
        "AtmosphereWaterCut",
        "VolumeContribution",
        "AtmosphereContribution",
        "PrecipitationContribution"
    };

    static readonly int[] WaterArtifactDebugModes =
    {
        0,
        24,
        25,
        26,
        30,
        31,
        32,
        40,
        41,
        42,
        43,
        44,
        45
    };

    static readonly int[] AtmosphereWaterDebugModes =
    {
        0,
        24,
        26,
        40,
        41,
        42,
        44
    };

    static readonly int[] WaterInterfaceDebugModes =
    {
        0,
        11,
        14,
        15,
        20,
        27,
        28,
        33,
        34,
        35,
        36,
        37
    };

    static readonly int[] PrecipitationDebugModes =
    {
        0,
        40,
        42,
        44,
        45
    };

    static readonly int[] WaterVolumeDeepDiveDebugModes =
    {
        0,
        2,
        7,
        11,
        12,
        14,
        15,
        18,
        19,
        20,
        21,
        22,
        23,
        24,
        25,
        26,
        27,
        28,
        29,
        30,
        31,
        32,
        33,
        34,
        35,
        36,
        37,
        38,
        39
    };

    struct WaterDebugStats
    {
        public bool Valid;
        public int Vertices;
        public int Triangles;
        public float DepthMin, DepthMax, DepthAvg;
        public float ShoreMin, ShoreMax, ShoreAvg;
        public float BodyMin, BodyMax, BodyAvg;
        public float MotionMaskAvg, MotionMaskMax, MotionMaskSample;
        public float NormalMaskAvg, NormalMaskMax, NormalMaskSample;
        public float SampleDepth, SampleShore, SampleBody;
        public float MotionEligiblePercent;
        public float NormalEligiblePercent;
    }

    void OnEnable()
    {
        RefreshInputDevices();
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        StopLooking();
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void Start()
    {
        RefreshInputDevices();
        ConfigureCamera();
        ShowWaterDebugDetails = false;
        ApplyOceanDebugMode();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            RefreshInputDevices();
        else
            StopLooking();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _lastPlanetCenter = evt.PlanetCenter;
        _lastPlanetRadius = evt.PlanetRadius;
        _lastSeaLevelRadius = evt.SeaLevelRadius;
        _lastElevationMin = evt.ElevationMin;
        _lastElevationMax = evt.ElevationMax;
        _cachedPlanet = FindPlanetSurfaceSampler();
        UpdateSunOrbitAxis();

        if (AutoPositionOnGenerate)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
    }

    void Update()
    {
        RefreshInputDevices();
        UpdateSunOrbitAxis();

        HandleLook();
        HandleMovement();
        HandleShortcuts();
    }

    void ConfigureCamera()
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.farClipPlane = 100000f;
    }

    void RefreshInputDevices()
    {
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;
    }

    void HandleShortcuts()
    {
        if (WasKeyPressed(_keyboard?.spaceKey, KeyCode.Space) && _lastPlanetRadius > 0f)
            ToggleOrbitSurfaceView();

        if (WasKeyPressed(_keyboard?.backspaceKey, KeyCode.Backspace))
            FaceSun();

        if (WasKeyPressed(_keyboard?.rKey, KeyCode.R))
            FrameStrongestStorm();

        if (WasKeyPressed(_keyboard?.f8Key, KeyCode.F8))
            ToggleSunFreeze();

        if (WasKeyPressed(_keyboard?.f9Key, KeyCode.F9))
            ShowWaterDebugDetails = !ShowWaterDebugDetails;

        if (WasKeyPressed(_keyboard?.f11Key, KeyCode.F11))
            ToggleProfilingFrameRate();

        if (WasKeyPressed(_keyboard?.f7Key, KeyCode.F7))
            CycleF10CaptureSet();

        if (WasKeyPressed(_keyboard?.pKey, KeyCode.P))
            TogglePrecipitationRendering();

        if (WasKeyPressed(_keyboard?.f10Key, KeyCode.F10))
            TriggerOceanDebugCapture();
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
        OceanDebugCaptureSet[] sets =
        {
            OceanDebugCaptureSet.WaterArtifact,
            OceanDebugCaptureSet.AtmosphereWater,
            OceanDebugCaptureSet.WaterInterface,
            OceanDebugCaptureSet.Precipitation,
            OceanDebugCaptureSet.WaterVolumeDeepDive,
            OceanDebugCaptureSet.CurrentModeOnly,
            OceanDebugCaptureSet.FullLoop
        };

        int index = System.Array.IndexOf(sets, F10CaptureSet);
        F10CaptureSet = sets[(index + 1 + sets.Length) % sets.Length];
    }

    void ToggleSunFreeze()
    {
        var celestial = GetCelestialManager();
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

    void CycleOceanDebugMode()
    {
        _oceanDebugMode = (_oceanDebugMode + 1) % OceanDebugModeNames.Length;
        ApplyOceanDebugMode();
        QueueDebugScreenshot();
    }

    void TriggerOceanDebugCapture()
    {
        if (F10CaptureSet == OceanDebugCaptureSet.CurrentModeOnly)
        {
            CycleOceanDebugMode();
            return;
        }

        if (!SaveF10DebugScreenshots)
        {
            CycleOceanDebugMode();
            return;
        }

        QueueDebugCaptureSet(GetDebugCaptureModes());
    }

    void ApplyOceanDebugMode()
    {
        Shader.SetGlobalInt(_oceanDebugModeId, _oceanDebugMode);
    }

    int[] GetDebugCaptureModes()
    {
        if (F10CaptureSet == OceanDebugCaptureSet.CurrentModeOnly)
            return new[] { _oceanDebugMode };

        if (F10CaptureSet == OceanDebugCaptureSet.FullLoop)
        {
            int[] modes = new int[OceanDebugModeNames.Length];
            for (int i = 0; i < modes.Length; i++)
                modes[i] = i;
            return modes;
        }

        switch (F10CaptureSet)
        {
            case OceanDebugCaptureSet.AtmosphereWater:
                return AtmosphereWaterDebugModes;
            case OceanDebugCaptureSet.WaterInterface:
                return WaterInterfaceDebugModes;
            case OceanDebugCaptureSet.Precipitation:
                return PrecipitationDebugModes;
            case OceanDebugCaptureSet.WaterVolumeDeepDive:
                return WaterVolumeDeepDiveDebugModes;
            default:
                return WaterArtifactDebugModes;
        }
    }

    void QueueDebugCaptureSet(int[] modes)
    {
        if (!SaveF10DebugScreenshots || _debugScreenshotCaptureRunning || !gameObject.activeInHierarchy || modes == null || modes.Length == 0)
            return;

        StartCoroutine(CaptureDebugModeSetCoroutine(modes));
    }

    void QueueDebugScreenshot()
    {
        if (!SaveF10DebugScreenshots || _debugScreenshotCaptureRunning || !gameObject.activeInHierarchy)
            return;

        string modeName = OceanDebugModeNames[Mathf.Clamp(_oceanDebugMode, 0, OceanDebugModeNames.Length - 1)];
        StartCoroutine(CaptureDebugScreenshotCoroutine(_oceanDebugMode, modeName));
    }

    System.Collections.IEnumerator CaptureDebugModeSetCoroutine(int[] modes)
    {
        _debugScreenshotCaptureRunning = true;
        int restoreMode = RestoreOceanDebugOffAfterCaptureSet ? 0 : _oceanDebugMode;

        try
        {
            for (int i = 0; i < modes.Length; i++)
            {
                int modeIndex = Mathf.Clamp(modes[i], 0, OceanDebugModeNames.Length - 1);
                string modeName = OceanDebugModeNames[modeIndex];
                _oceanDebugMode = modeIndex;
                ApplyOceanDebugMode();

                yield return null;
                yield return new WaitForEndOfFrame();

                try
                {
                    SaveDebugScreenshot(modeIndex, modeName);
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }

                if (DebugCaptureModeDelaySeconds > 0f && i < modes.Length - 1)
                    yield return new WaitForSecondsRealtime(DebugCaptureModeDelaySeconds);
            }
        }
        finally
        {
            _oceanDebugMode = Mathf.Clamp(restoreMode, 0, OceanDebugModeNames.Length - 1);
            ApplyOceanDebugMode();
            _debugScreenshotCaptureRunning = false;
        }
    }

    System.Collections.IEnumerator CaptureDebugScreenshotCoroutine(int modeIndex, string modeName)
    {
        _debugScreenshotCaptureRunning = true;

        yield return null;
        yield return new WaitForEndOfFrame();

        try
        {
            SaveDebugScreenshot(modeIndex, modeName);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            _debugScreenshotCaptureRunning = false;
        }
    }

    void SaveDebugScreenshot(int modeIndex, string modeName)
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
            string baseName = $"F10-{modeIndex:00}-{safeModeName}-{timestamp}";
            string imagePath = System.IO.Path.Combine(directory, baseName + ".png");
            string metadataPath = System.IO.Path.Combine(directory, baseName + ".txt");

            System.IO.File.WriteAllBytes(imagePath, resized.EncodeToPNG());
            System.IO.File.WriteAllText(metadataPath, BuildDebugCaptureMetadata(modeIndex, modeName, source.width, source.height, resized.width, resized.height, imagePath));
            PruneDebugScreenshots(directory);

            Debug.Log($"[FreeCameraController] Saved F10 debug screenshot: {imagePath}");
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
        int modesPerRun = F10CaptureSet == OceanDebugCaptureSet.CurrentModeOnly
            ? 1
            : GetDebugCaptureModes().Length;
        int keepFiles = Mathf.Max(1, DebugScreenshotMaxRuns) * Mathf.Max(1, modesPerRun) * 2;
        if (keepFiles <= 0 || string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            return;

        var captures = new System.Collections.Generic.List<System.IO.FileInfo>();
        var dir = new System.IO.DirectoryInfo(directory);
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
                Debug.LogWarning($"[FreeCameraController] Could not prune F10 debug capture '{captures[i].FullName}': {ex.Message}");
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

    string BuildDebugCaptureMetadata(int modeIndex, string modeName, int sourceWidth, int sourceHeight, int savedWidth, int savedHeight, string imagePath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== F10 DEBUG CAPTURE ===");
        sb.AppendLine($"Image: {imagePath}");
        sb.AppendLine($"Source: {sourceWidth}x{sourceHeight}");
        sb.AppendLine($"Saved: {savedWidth}x{savedHeight}");
        sb.AppendLine($"Mode: {modeIndex}:{modeName}");
        sb.AppendLine($"CaptureSet: {F10CaptureSet}");
        sb.AppendLine($"Time: {System.DateTime.Now:O}");
        sb.AppendLine();

        sb.AppendLine("--- Camera ---");
        sb.AppendLine($"Position: {transform.position.x:F2}, {transform.position.y:F2}, {transform.position.z:F2}");
        sb.AppendLine($"Forward: {transform.forward.x:F4}, {transform.forward.y:F4}, {transform.forward.z:F4}");
        sb.AppendLine($"Surface view: {_surfaceView}");
        if (TargetCenter != null)
        {
            Vector3 dirToSurface = (transform.position - TargetCenter.position).normalized;
            var (lat, lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            sb.AppendLine($"LatLonDeg: {lat * Mathf.Rad2Deg:F2}, {lon * Mathf.Rad2Deg:F2}");
            sb.AppendLine($"DistanceToCenter: {Vector3.Distance(transform.position, TargetCenter.position):F2}");
        }
        sb.AppendLine($"PlanetRadius: {_lastPlanetRadius:F2}");
        sb.AppendLine($"SeaLevelRadius: {_lastSeaLevelRadius:F2}");
        sb.AppendLine($"ElevationMinMax: {_lastElevationMin:F4}, {_lastElevationMax:F4}");
        sb.AppendLine();

        sb.AppendLine("--- Runtime ---");
        sb.AppendLine($"FPS: {(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F1}");
        sb.AppendLine($"FrameTarget: {Application.targetFrameRate}");
        sb.AppendLine($"VSync: {QualitySettings.vSyncCount}");
        var celestial = GetCelestialManager();
        if (celestial != null)
            sb.AppendLine($"SunFrozen: {celestial.IsTimeFrozen}");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && _lastPlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (transform.position - _lastPlanetCenter).normalized);
            sb.AppendLine($"SunElevationDeg: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F2}");
        }
        IPrecipitationDebugControl precipitation = GetPrecipitationController();
        if (precipitation != null)
        {
            sb.AppendLine($"PrecipitationEnabled: {precipitation.PrecipitationRenderingEnabled}");
            sb.AppendLine($"PrecipLocalParticlesEnabled: {precipitation.LocalPrecipitationParticlesEnabled}");
            sb.AppendLine($"PrecipLocalParticlesForCamera: {precipitation.ShouldRenderLocalParticles(GetComponent<Camera>())}");
        }
        sb.AppendLine();

        AppendWaterDebugMetadata(sb);
        return sb.ToString();
    }

    void AppendWaterDebugMetadata(System.Text.StringBuilder sb)
    {
        Renderer waterRenderer = GetWaterRenderer();
        sb.AppendLine("--- Water ---");

        if (waterRenderer == null)
        {
            sb.AppendLine("Renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        sb.AppendLine($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        sb.AppendLine($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F2}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F2}, debug={_oceanDebugMode}:{OceanDebugModeNames[Mathf.Clamp(_oceanDebugMode, 0, OceanDebugModeNames.Length - 1)]}");
        sb.AppendLine($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F2}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        sb.AppendLine($"DepthFoam: shallow={GetMaterialFloat(mat, _shallowDepthId):F2}, deep={GetMaterialFloat(mat, _deepDepthId):F2}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F2}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F2}");

        if (ServiceLocator.TryGet<IWeatherProvider>(out var weatherProvider))
        {
            Vector3 samplePosition = transform.position;
            Vector3 fromCenter = transform.position - _lastPlanetCenter;
            if (_lastSeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
                samplePosition = _lastPlanetCenter + fromCenter.normalized * _lastSeaLevelRadius;

            WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
            float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
            float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
            float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
            sb.AppendLine($"Weather: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
        }

        RefreshWaterDebugStats(waterRenderer);
        if (!_waterDebugStats.Valid)
        {
            sb.AppendLine("MeshData: missing vertex colors");
            return;
        }

        var s = _waterDebugStats;
        sb.AppendLine($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        MeshFilter volumeLipFilter = GetWaterVolumeLipFilter(waterRenderer);
        Mesh volumeLipMesh = volumeLipFilter != null ? volumeLipFilter.sharedMesh : null;
        if (volumeLipMesh != null)
        {
            int volumeLipTriangles = volumeLipMesh.subMeshCount > 0
                ? (int)(volumeLipMesh.GetIndexCount(0) / 3)
                : 0;
            sb.AppendLine($"VolumeLipMesh: active={volumeLipFilter.gameObject.activeInHierarchy}, verts={volumeLipMesh.vertexCount}, tris={volumeLipTriangles}");
        }
        else
        {
            sb.AppendLine("VolumeLipMesh: missing");
        }

        sb.AppendLine($"DataRanges: depth={s.DepthMin:F3}-{s.DepthMax:F3} avg={s.DepthAvg:F3}, shore={s.ShoreMin:F3}-{s.ShoreMax:F3} avg={s.ShoreAvg:F3}, body={s.BodyMin:F3}-{s.BodyMax:F3} avg={s.BodyAvg:F3}");
        sb.AppendLine($"CameraSample: depth={s.SampleDepth:F3}, shore={s.SampleShore:F3}, body={s.SampleBody:F3}, motionMask={s.MotionMaskSample:F3}, normalMask={s.NormalMaskSample:F3}");
        sb.AppendLine($"MotionMask: avg={s.MotionMaskAvg:F3}, max={s.MotionMaskMax:F3}, eligible={s.MotionEligiblePercent:F1}%");
        sb.AppendLine($"NormalMask: avg={s.NormalMaskAvg:F3}, max={s.NormalMaskMax:F3}, eligible={s.NormalEligiblePercent:F1}%");
    }

    Renderer GetWaterRenderer()
    {
        if (_cachedWaterRenderer != null && _cachedWaterRenderer.enabled && _cachedWaterRenderer.gameObject.activeInHierarchy)
            return _cachedWaterRenderer;

        var waterObject = GameObject.Find("Water");
        if (waterObject != null && waterObject.TryGetComponent(out Renderer waterRenderer))
        {
            _cachedWaterRenderer = waterRenderer;
            return _cachedWaterRenderer;
        }

        var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
        for (int i = 0; i < renderers.Length; i++)
        {
            var mat = renderers[i].sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name == "Planet/Ocean")
            {
                _cachedWaterRenderer = renderers[i];
                return _cachedWaterRenderer;
            }
        }

        return null;
    }

    MeshFilter GetWaterVolumeLipFilter(Renderer waterRenderer)
    {
        if (waterRenderer == null)
            return null;

        Transform lip = waterRenderer.transform.Find("WaterVolumeLip");
        return lip != null ? lip.GetComponent<MeshFilter>() : null;
    }

    void RefreshWaterDebugStats(Renderer waterRenderer)
    {
        _waterDebugStats = default;

        if (waterRenderer == null || !waterRenderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
            return;

        Mesh mesh = filter.sharedMesh;
        Color[] colors = mesh.colors;
        if (colors == null || colors.Length == 0)
            return;

        Vector3[] vertices = mesh.vertices;
        int count = Mathf.Min(colors.Length, vertices != null ? vertices.Length : colors.Length);
        if (count <= 0)
            return;

        WaterDebugStats stats = new WaterDebugStats
        {
            Valid = true,
            Vertices = mesh.vertexCount,
            Triangles = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0,
            DepthMin = 1f,
            ShoreMin = 1f,
            BodyMin = 1f
        };

        Vector3 localCamera = waterRenderer.transform.InverseTransformPoint(transform.position);
        Vector3 localCameraDir = localCamera.sqrMagnitude > 0.0001f ? localCamera.normalized : Vector3.up;
        int sampleIndex = 0;
        float bestAlignment = -2f;
        int motionEligible = 0;
        int normalEligible = 0;

        for (int i = 0; i < count; i++)
        {
            Color c = colors[i];
            float depth = Mathf.Clamp01(c.r);
            float shore = Mathf.Clamp01(c.g);
            float body = Mathf.Clamp01(c.b);
            float motionMask = FocusMotionMask(depth, shore, body);
            float normalMask = FocusNormalMask(depth, shore, body);

            stats.DepthMin = Mathf.Min(stats.DepthMin, depth);
            stats.DepthMax = Mathf.Max(stats.DepthMax, depth);
            stats.DepthAvg += depth;
            stats.ShoreMin = Mathf.Min(stats.ShoreMin, shore);
            stats.ShoreMax = Mathf.Max(stats.ShoreMax, shore);
            stats.ShoreAvg += shore;
            stats.BodyMin = Mathf.Min(stats.BodyMin, body);
            stats.BodyMax = Mathf.Max(stats.BodyMax, body);
            stats.BodyAvg += body;
            stats.MotionMaskAvg += motionMask;
            stats.MotionMaskMax = Mathf.Max(stats.MotionMaskMax, motionMask);
            stats.NormalMaskAvg += normalMask;
            stats.NormalMaskMax = Mathf.Max(stats.NormalMaskMax, normalMask);

            if (motionMask > 0.05f) motionEligible++;
            if (normalMask > 0.05f) normalEligible++;

            if (vertices != null && i < vertices.Length && vertices[i].sqrMagnitude > 0.0001f)
            {
                float alignment = Vector3.Dot(vertices[i].normalized, localCameraDir);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    sampleIndex = i;
                }
            }
        }

        float invCount = 1f / count;
        stats.DepthAvg *= invCount;
        stats.ShoreAvg *= invCount;
        stats.BodyAvg *= invCount;
        stats.MotionMaskAvg *= invCount;
        stats.NormalMaskAvg *= invCount;
        stats.MotionEligiblePercent = motionEligible * 100f * invCount;
        stats.NormalEligiblePercent = normalEligible * 100f * invCount;

        Color sample = colors[Mathf.Clamp(sampleIndex, 0, colors.Length - 1)];
        stats.SampleDepth = Mathf.Clamp01(sample.r);
        stats.SampleShore = Mathf.Clamp01(sample.g);
        stats.SampleBody = Mathf.Clamp01(sample.b);
        stats.MotionMaskSample = FocusMotionMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);
        stats.NormalMaskSample = FocusNormalMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);

        _cachedWaterMesh = mesh;
        _waterDebugStats = stats;
    }

    static float FocusMotionMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.02f, 0.18f, depth);
        float shoreRelease = SmoothStep(0.02f, 0.18f, shore) * 0.58f;
        return body
            * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float FocusNormalMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.012f, 0.10f, depth);
        float shoreRelease = SmoothStep(0.012f, 0.10f, shore) * 0.72f;
        return body
            * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float SmoothStep(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    static float GetMaterialFloat(Material mat, int id)
    {
        return mat != null && mat.HasProperty(id) ? mat.GetFloat(id) : float.NaN;
    }

    void ToggleOrbitSurfaceView()
    {
        UpdateSunOrbitAxis();

        float distance = Vector3.Distance(transform.position, _lastPlanetCenter);
        bool nearSurface = _surfaceView || distance < _lastPlanetRadius * 1.25f;

        if (nearSurface)
            RepositionCamera(_lastPlanetCenter, _lastPlanetRadius);
        else
            PositionOnSurface(_lastPlanetCenter, _lastPlanetRadius);
    }

    Vector3 GetSunDirectionToSun()
    {
        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();

        if (_cachedSunLight == null)
            return Vector3.up;

        return -_cachedSunLight.transform.forward.normalized;
    }

    void UpdateSunOrbitAxis()
    {
        Vector3 toSun = GetSunDirectionToSun();
        if (toSun.sqrMagnitude < 0.0001f)
            return;

        if (_lastSunDirectionToSun.sqrMagnitude > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(_lastSunDirectionToSun.normalized, toSun.normalized);
            if (axis.sqrMagnitude > 0.00000001f)
                _sunOrbitAxis = axis.normalized;
        }

        _lastSunDirectionToSun = toSun.normalized;
    }

    Vector3 GetStableViewUp(Vector3 forward)
    {
        Vector3 up = Vector3.ProjectOnPlane(_sunOrbitAxis, forward);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.right, forward);

        return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
    }

    void HandleLook()
    {
        bool rightMousePressed = IsRightMousePressed();
        if (rightMousePressed && !_looking)
            StartLooking();
        else if (!rightMousePressed && _looking)
            StopLooking();

        if (!_looking) return;

        if (_skipNextDelta)
        {
            _skipNextDelta = false;
            ReadMouseDelta();
            return;
        }

        Vector2 delta = ReadMouseDelta();
        if (delta.sqrMagnitude < 0.001f) return;

        float yawAmount = delta.x * LookSensitivity * 0.1f;
        float pitchAmount = -delta.y * LookSensitivity * 0.1f;
        transform.Rotate(Vector3.up, yawAmount, Space.Self);
        transform.Rotate(Vector3.right, pitchAmount, Space.Self);
    }

    void StartLooking()
    {
        _looking = true;
        _skipNextDelta = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void StopLooking()
    {
        _looking = false;
        _skipNextDelta = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void HandleMovement()
    {
        float speed = MoveSpeed;
        if (IsFastPressed())
            speed *= FastMultiplier;

        Vector3 move = Vector3.zero;
        if (IsKeyPressed(_keyboard?.wKey, KeyCode.W)) move += transform.forward;
        if (IsKeyPressed(_keyboard?.sKey, KeyCode.S)) move -= transform.forward;
        if (IsKeyPressed(_keyboard?.aKey, KeyCode.A)) move -= transform.right;
        if (IsKeyPressed(_keyboard?.dKey, KeyCode.D)) move += transform.right;
        if (IsKeyPressed(_keyboard?.eKey, KeyCode.E)) move += transform.up;
        if (IsKeyPressed(_keyboard?.qKey, KeyCode.Q)) move -= transform.up;

        if (move.sqrMagnitude > 0.0001f)
            transform.position += move.normalized * speed * Time.deltaTime;

        float rollSpeed = 60f;
        if (IsKeyPressed(_keyboard?.zKey, KeyCode.Z))
            transform.Rotate(Vector3.forward, rollSpeed * Time.deltaTime, Space.Self);
        if (IsKeyPressed(_keyboard?.cKey, KeyCode.C))
            transform.Rotate(Vector3.forward, -rollSpeed * Time.deltaTime, Space.Self);

        float scroll = ReadScroll();
        if (Mathf.Abs(scroll) > 0.001f)
            transform.position += transform.forward * scroll * ScrollSpeed * Time.deltaTime;
    }

    void FaceSun()
    {
        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight == null)
            return;

        Vector3 toSun = GetSunDirectionToSun();
        transform.rotation = Quaternion.LookRotation(toSun, GetStableViewUp(toSun));
    }

    void FrameStrongestStorm()
    {
        if (_lastPlanetRadius <= 0f)
            return;

        if (!ServiceLocator.TryGet<IWeatherProvider>(out var weather))
            return;

        if (!weather.TryFindStrongestPrecipitation(out Vector3 stormPosition, out _))
            return;

        Vector3 stormNormal = (stormPosition - _lastPlanetCenter).normalized;
        if (stormNormal.sqrMagnitude < 0.0001f)
            stormNormal = Vector3.up;

        if (_surfaceView)
        {
            Vector3 tangent = Vector3.Cross(stormNormal, GetSunDirectionToSun());
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(stormNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(stormNormal, Vector3.right);

            tangent.Normalize();
            Vector3 viewNormal = Quaternion.AngleAxis(10f, tangent) * stormNormal;
            float surfaceRadius = Mathf.Max(_lastPlanetRadius, _lastSeaLevelRadius);
            var planet = GetPlanet();
            if (planet != null && planet.TryGetSurfaceRadius(viewNormal.normalized, out float sampledRadius))
                surfaceRadius = Mathf.Max(sampledRadius, _lastSeaLevelRadius);

            transform.position = _lastPlanetCenter + viewNormal.normalized * (surfaceRadius + GetSurfaceClearance(_lastPlanetRadius));
            Vector3 lookDir = (stormPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, viewNormal.normalized);
            MoveSpeed = Mathf.Max(0.25f, _lastPlanetRadius * SurfaceSpeedMultiplier);
            ScrollSpeed = Mathf.Max(1f, _lastPlanetRadius * 0.1f);
        }
        else
        {
            float distance = Mathf.Max(_lastPlanetRadius * 1.85f, _lastPlanetRadius + 1000f);
            transform.position = _lastPlanetCenter + stormNormal * distance;
            Vector3 lookDir = (stormPosition - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(lookDir, GetStableViewUp(lookDir));
            MoveSpeed = Mathf.Max(1f, _lastPlanetRadius * OrbitSpeedMultiplier);
            ScrollSpeed = Mathf.Max(5f, _lastPlanetRadius * 2f);
        }
    }

    Light FindSunLight()
    {
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    ICelestialTimeController GetCelestialManager()
    {
        if (_cachedCelestialManager == null)
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICelestialTimeController controller)
                {
                    _cachedCelestialManager = controller;
                    break;
                }
            }
        }

        return _cachedCelestialManager;
    }

    IPlanetSurfaceSampler GetPlanet()
    {
        if (_cachedPlanet == null)
            _cachedPlanet = FindPlanetSurfaceSampler();
        return _cachedPlanet;
    }

    IPrecipitationDebugControl GetPrecipitationController()
    {
        if (_cachedPrecipitationController != null
            && _cachedPrecipitationBehaviour != null
            && _cachedPrecipitationBehaviour.isActiveAndEnabled)
            return _cachedPrecipitationController;

        _cachedPrecipitationController = null;
        _cachedPrecipitationBehaviour = null;

        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (behaviour is IPrecipitationDebugControl controller)
            {
                _cachedPrecipitationController = controller;
                _cachedPrecipitationBehaviour = behaviour;
                break;
            }
        }

        return _cachedPrecipitationController;
    }

    IPlanetSurfaceSampler FindPlanetSurfaceSampler()
    {
        if (TargetCenter != null)
        {
            var targetBehaviours = TargetCenter.GetComponents<MonoBehaviour>();
            for (int i = 0; i < targetBehaviours.Length; i++)
            {
                if (targetBehaviours[i] is IPlanetSurfaceSampler sampler)
                    return sampler;
            }
        }

        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlanetSurfaceSampler sampler)
                return sampler;
        }

        return null;
    }

    void RepositionCamera(Vector3 center, float radius)
    {
        float distance = radius * ViewDistanceMultiplier;
        Vector3 toSun = GetSunDirectionToSun();

        transform.position = center + toSun.normalized * distance;
        Vector3 forward = (center - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(forward, GetStableViewUp(forward));

        MoveSpeed = Mathf.Max(1f, radius * OrbitSpeedMultiplier);
        ScrollSpeed = Mathf.Max(5f, radius * 2f);
        _surfaceView = false;
    }

    void PositionOnSurface(Vector3 center, float radius)
    {
        Vector3 toSun = GetSunDirectionToSun();
        Vector3 sunMotion = Vector3.Cross(_sunOrbitAxis, toSun);
        if (sunMotion.sqrMagnitude < 0.0001f)
            sunMotion = Vector3.Cross(Vector3.up, toSun);
        if (sunMotion.sqrMagnitude < 0.0001f)
            sunMotion = Vector3.Cross(Vector3.right, toSun);

        sunMotion.Normalize();
        float offsetRadians = SurfaceSunriseOffsetDegrees * Mathf.Deg2Rad;
        Vector3 surfaceNormal = (sunMotion * Mathf.Cos(offsetRadians) - toSun * Mathf.Sin(offsetRadians)).normalized;

        float groundRadius = Mathf.Max(radius, _lastSeaLevelRadius);
        var planet = GetPlanet();
        if (planet != null && planet.TryGetSurfaceRadius(surfaceNormal, out float sampledRadius))
            groundRadius = Mathf.Max(sampledRadius, _lastSeaLevelRadius);

        transform.position = center + surfaceNormal * (groundRadius + GetSurfaceClearance(radius));

        Vector3 lookDir = Vector3.ProjectOnPlane(toSun, surfaceNormal);
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = Vector3.ProjectOnPlane(sunMotion, surfaceNormal);

        transform.rotation = Quaternion.LookRotation(lookDir.normalized, surfaceNormal);

        MoveSpeed = Mathf.Max(0.25f, radius * SurfaceSpeedMultiplier);
        ScrollSpeed = Mathf.Max(1f, radius * 0.1f);
        _surfaceView = true;
    }

    float GetSurfaceClearance(float radius)
    {
        return Mathf.Max(SurfaceHeight, Mathf.Max(4f, radius * 0.0012f));
    }

    bool IsFastPressed()
    {
        return IsKeyPressed(_keyboard?.leftShiftKey, KeyCode.LeftShift)
            || IsKeyPressed(_keyboard?.rightShiftKey, KeyCode.RightShift);
    }

    bool IsRightMousePressed()
    {
        if (_mouse != null && _mouse.rightButton.isPressed)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    Vector2 ReadMouseDelta()
    {
        if (_mouse != null)
            return _mouse.delta.ReadValue();

#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 20f;
#else
        return Vector2.zero;
#endif
    }

    float ReadScroll()
    {
        if (_mouse != null)
            return _mouse.scroll.ReadValue().y;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y * 120f;
#else
        return 0f;
#endif
    }

    static bool IsKeyPressed(KeyControl key, KeyCode legacyKey)
    {
        if (key != null && key.isPressed)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(legacyKey);
#else
        return false;
#endif
    }

    static bool WasKeyPressed(ButtonControl key, KeyCode legacyKey)
    {
        if (key != null && key.wasPressedThisFrame)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(legacyKey);
#else
        return false;
#endif
    }

    void OnGUI()
    {
        if (!ShowDebugOverlay) return;

        GUILayout.BeginArea(new Rect(10, 10, 520, ShowWaterDebugDetails ? 520 : 260));
        GUILayout.Label("Debug Camera");
        GUILayout.Label($"Position: {transform.position.x:F1}, {transform.position.y:F1}, {transform.position.z:F1}");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");

        if (TargetCenter != null)
        {
            Vector3 dirToSurface = (transform.position - TargetCenter.position).normalized;
            var (lat, lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}\u00b0 Lon: {lon * Mathf.Rad2Deg:F1}\u00b0");

            float distToCenter = Vector3.Distance(transform.position, TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Label("RMB=Look, WASD=Move, Shift=Fast, QE=Up/Down, ZC=Roll");
        GUILayout.Label("Space=Toggle Orbit/Surface, Backspace=Face Sun, R=Frame Storm");
        GUILayout.Label("F7=Cycle F10 Set, F8=Freeze Sun, F9=Water Stats, F11=Toggle FPS Cap, P=Toggle Precip");
        GUILayout.Label($"F10={F10CaptureSet} capture ({GetDebugCaptureModes().Length} modes, current {OceanDebugModeNames[Mathf.Clamp(_oceanDebugMode, 0, OceanDebugModeNames.Length - 1)]})");
        IPrecipitationDebugControl precipitation = GetPrecipitationController();
        if (precipitation != null)
        {
            GUILayout.Label($"Precipitation render: {(precipitation.PrecipitationRenderingEnabled ? "ON" : "OFF")}");
            GUILayout.Label($"Precip local particles: {(precipitation.ShouldRenderLocalParticles(GetComponent<Camera>()) ? "ON" : "OFF")}");
        }

        if (_precipitationToggleFlashActive)
        {
            if (Time.unscaledTime <= _precipitationToggleFlashUntil)
                GUILayout.Label(_precipitationToggleFlashMessage);
            else
                _precipitationToggleFlashActive = false;
        }

        GUILayout.Label($"Frame target: {Application.targetFrameRate}, vSync: {QualitySettings.vSyncCount}");

        var celestial = GetCelestialManager();
        if (celestial != null)
            GUILayout.Label($"Sun frozen: {(celestial.IsTimeFrozen ? "yes" : "no")}");

        if (_cachedSunLight == null)
            _cachedSunLight = FindSunLight();
        if (_cachedSunLight != null && _lastPlanetRadius > 0f)
        {
            Vector3 sd = -_cachedSunLight.transform.forward;
            float sunElevation = Vector3.Dot(sd, (transform.position - _lastPlanetCenter).normalized);
            GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}\u00b0");
        }

        if (ShowWaterDebugDetails)
            DrawWaterDebugOverlay();

        GUILayout.EndArea();
    }

    void DrawWaterDebugOverlay()
    {
        Renderer waterRenderer = GetWaterRenderer();
        GUILayout.Space(6);
        GUILayout.Label("Water Debug");

        if (waterRenderer == null)
        {
            GUILayout.Label("Water renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        GUILayout.Label($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        GUILayout.Label($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F1}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F1}, debug={_oceanDebugMode}:{OceanDebugModeNames[Mathf.Clamp(_oceanDebugMode, 0, OceanDebugModeNames.Length - 1)]}");
        GUILayout.Label($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F1}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        GUILayout.Label($"Depth/Foam: shallow={GetMaterialFloat(mat, _shallowDepthId):F1}, deep={GetMaterialFloat(mat, _deepDepthId):F1}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F1}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F1}");
        if (ServiceLocator.TryGet<IWeatherProvider>(out var weatherProvider))
        {
            Vector3 samplePosition = transform.position;
            Vector3 fromCenter = transform.position - _lastPlanetCenter;
            if (_lastSeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
                samplePosition = _lastPlanetCenter + fromCenter.normalized * _lastSeaLevelRadius;

            WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
            float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
            float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
            float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
            GUILayout.Label($"Weather/waves: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
        }

        if (Time.unscaledTime >= _nextWaterDebugRefreshTime || waterRenderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != _cachedWaterMesh)
        {
            RefreshWaterDebugStats(waterRenderer);
            _nextWaterDebugRefreshTime = Time.unscaledTime + 0.75f;
        }

        if (!_waterDebugStats.Valid)
        {
            GUILayout.Label("Mesh water data: missing vertex colors");
            return;
        }

        var s = _waterDebugStats;
        GUILayout.Label($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        GUILayout.Label($"Data ranges: depth {s.DepthMin:F2}-{s.DepthMax:F2} avg {s.DepthAvg:F2}, shore {s.ShoreMin:F2}-{s.ShoreMax:F2} avg {s.ShoreAvg:F2}, body {s.BodyMin:F2}-{s.BodyMax:F2} avg {s.BodyAvg:F2}");
        GUILayout.Label($"Camera sample: depth={s.SampleDepth:F2}, shore={s.SampleShore:F2}, body={s.SampleBody:F2}, motionMask={s.MotionMaskSample:F2}, normalMask={s.NormalMaskSample:F2}");
        GUILayout.Label($"Motion mask: avg={s.MotionMaskAvg:F2}, max={s.MotionMaskMax:F2}, eligible>{0.05f:F2}={s.MotionEligiblePercent:F1}%");
        GUILayout.Label($"Normal mask: avg={s.NormalMaskAvg:F2}, max={s.NormalMaskMax:F2}, eligible>{0.05f:F2}={s.NormalEligiblePercent:F1}%");
        GUILayout.Label("F10 sets: WaterArtifact is concise; use F7 for AtmosphereWater, WaterInterface, Precipitation, DeepDive, CurrentModeOnly, or FullLoop.");
    }
}
