using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Publishes render settings for distant precipitation shafts. The renderer samples
/// the shared cloud weather map, so rain stays tied to storm cells.
/// </summary>
[CommandPrefix("precipitation")]
public class PrecipitationController : MonoBehaviour, IPrecipitationDebugControl
{
    public enum DebugView
    {
        Off = 0,
        RainMask = 1,
        RainDots = 2,
        StormDots = 3
    }

    public enum WeatherParticleProof
    {
        Off = 0,
        Dust = 1,
        Rain = 2,
        Snow = 3,
        All = 4,
    }

    [Header("References")]
    public CloudSettings CloudSettings;

    ICloudController _cloudController;

    [Header("Rendering")]
    public bool RenderPrecipitation = true;
    [Range(0f, 2f)] public float Intensity = 1.15f;
    [Range(0f, 1f)] public float StormThreshold = 0.55f;
    [Range(0.01f, 1f)] public float StormSoftness = 0.2f;
    [Range(0f, 1f)] public float MaxOpacity = 0.38f;
    [Range(4, 48)] public int ViewSteps = 32;
    [Range(1000f, 50000f)] public float MaxDistance = 22000f;

    [Header("LOD")]
    [Tooltip("Minimum view steps used when the camera is very far from the planet.")]
    [Range(2, 32)] public int MinViewSteps = 6;
    [Tooltip("Camera altitude above sea level (meters) below which full ViewSteps are used.")]
    [Range(0f, 500000f)] public float StepScaleNearAltitude = 3000f;
    [Tooltip("Camera altitude above sea level (meters) above which MinViewSteps are used.")]
    [Range(0f, 1000000f)] public float StepScaleFarAltitude = 25000f;

    [Header("Layer")]
    [Range(0f, 300f)] public float BottomAltitude = 25f;
    [FormerlySerializedAs("CloudBaseInset")]
    [Range(0f, 300f)] public float CloudBaseOverlap = 45f;
    [Range(0.01f, 0.5f)] public float BottomFeather = 0.08f;
    [Range(0.01f, 0.5f)] public float TopFeather = 0.18f;

    [Header("Distant Shafts")]
    [Range(50f, 2000f)] public float CurtainScale = 520f;
    [Range(0f, 2f)] public float WindSlant = 0.35f;
    [Range(0f, 30f)] public float FallSpeed = 16f;

    [Header("Local Weather Particles")]
    public bool RenderLocalParticles = true;
    [Range(10f, 300f)] public float LocalParticleRadius = 120f;
    [Range(5f, 100f)] public float LocalParticleVerticalRange = 18f;
    [Range(100f, 5000f)] public float LocalMaxCameraAltitude = 1600f;
    public WeatherParticleProof ParticleProof = WeatherParticleProof.Off;

    [Header("Dust Profile")]
    [Range(0, 8000)] public int DustParticleCount = 1600;
    [Range(0f, 1f)] public float DustBaseDensity = 0.18f;
    [Range(0f, 2f)] public float DustStormDensity = 0.65f;
    [Range(0f, 1f)] public float DustOpacity = 0.45f;
    [Range(0.005f, 0.2f)] public float DustSize = 0.025f;
    [Range(0.02f, 4f)] public float DustStreakLength = 0.65f;
    [Range(0f, 2f)] public float DustTurbulence = 0.28f;
    public Color DustColor = new Color(0.72f, 0.64f, 0.48f, 1f);

    [Header("Rain Profile")]
    [FormerlySerializedAs("LocalParticleCount")]
    [Range(0, 20000)] public int RainParticleCount = 12000;
    [Tooltip("Horizontal distribution radius of rain particles around the camera. Beyond ~800m, particles fall below the planet horizon and get depth-test culled at low camera altitudes.")]
    [Range(200f, 5000f)] public float RainParticleRadius = 600f;
    [Range(0.05f, 8f)] public float RainStreakLength = 4.0f;
    [FormerlySerializedAs("LocalParticleOpacity")]
    [Range(0f, 1f)] public float RainParticleOpacity = 0.65f;
    [Tooltip("Visual fall speed in m/s. Real rain terminal velocity is ~9 m/s but reads as slow motion on screen; 50-70 m/s looks like 'fast rain' to the eye.")]
    [Range(1f, 120f)] public float RainFallSpeed = 60f;
    [FormerlySerializedAs("LocalRainThreshold")]
    [Range(0f, 1f)] public float RainThreshold = 0.05f;
    [Range(0.003f, 0.2f)] public float RainWidth = 0.06f;

    [Header("Distant Rain Profile")]
    [Tooltip("Planet-scale rain layer visible from far away. Renders longer, wider streaks across a much wider radius so storms are visible at a distance.")]
    [Range(0, 30000)] public int DistantRainParticleCount = 10000;
    [Range(800f, 8000f)] public float DistantRainParticleRadius = 3000f;
    [Tooltip("Streak length for distant rain. Much longer than close rain so the streak reads at long viewing distance.")]
    [Range(2f, 120f)] public float DistantRainStreakLength = 60f;
    [Range(0f, 1f)] public float DistantRainOpacity = 0.55f;
    [Tooltip("Visibility threshold for distant rain. Lower than close-rain threshold so light storms still show streaks at a distance.")]
    [Range(0f, 1f)] public float DistantRainThreshold = 0.03f;
    [Tooltip("Streak width for distant rain in meters. Close rain at 0.06m is sub-pixel at 1km+; distant rain needs to be wide enough that each streak resolves to at least 1 pixel at long viewing distance.")]
    [Range(0.05f, 1.5f)] public float DistantRainWidth = 0.4f;

    [Header("Snow Profile")]
    [Range(0, 8000)] public int SnowParticleCount = 1800;
    [Range(0f, 1f)] public float SnowParticleOpacity = 0.72f;
    [Range(0.1f, 8f)] public float SnowFallSpeed = 1.6f;
    [Range(0f, 1f)] public float SnowThreshold = 0.12f;
    [Range(0.02f, 0.6f)] public float SnowSize = 0.12f;
    [Range(-10f, 10f)] public float SnowTemperatureCelsius = 1f;
    [Range(0.1f, 10f)] public float SnowTemperatureBlendCelsius = 2f;
    public Color SnowColor = new Color(0.88f, 0.92f, 0.96f, 1f);

    const int CurrentLocalWeatherParticleSettingsVersion = 1;
    [SerializeField, HideInInspector]
    int _localWeatherParticleSettingsVersion;

    [Header("Lighting")]
    public Color RainColor = new Color(0.36f, 0.44f, 0.50f, 1f);
    public Color StormRainColor = new Color(0.24f, 0.28f, 0.32f, 1f);

    [Header("Debug")]
    public DebugView DebugMode = DebugView.Off;
    [Range(0.02f, 0.45f)] public float DebugDotMinRadius = 0.08f;
    [Range(0.05f, 0.5f)] public float DebugDotMaxRadius = 0.42f;
    [Range(0f, 1f)] public float DebugDotOpacity = 0.95f;

    Vector3 _planetCenter;
    float _seaLevelRadius;

    static readonly int _precipitationEnabledId = Shader.PropertyToID("_PrecipitationEnabled");
    static readonly int _precipitationPlanetCenterId = Shader.PropertyToID("_PrecipitationPlanetCenter");
    static readonly int _precipitationRadiiId = Shader.PropertyToID("_PrecipitationRadii");
    static readonly int _precipitationParamsId = Shader.PropertyToID("_PrecipitationParams");
    static readonly int _precipitationFadeParamsId = Shader.PropertyToID("_PrecipitationFadeParams");
    static readonly int _precipitationVisualParamsId = Shader.PropertyToID("_PrecipitationVisualParams");
    static readonly int _precipitationColorId = Shader.PropertyToID("_PrecipitationColor");
    static readonly int _precipitationStormColorId = Shader.PropertyToID("_PrecipitationStormColor");
    static readonly int _precipitationViewStepsId = Shader.PropertyToID("_PrecipitationViewSteps");
    static readonly int _precipitationDebugModeId = Shader.PropertyToID("_PrecipitationDebugMode");
    static readonly int _precipitationDebugDotParamsId = Shader.PropertyToID("_PrecipitationDebugDotParams");
    static readonly int _weatherParticlesEnabledId = Shader.PropertyToID("_WeatherParticlesEnabled");
    static readonly int _weatherParticleCommonId = Shader.PropertyToID("_WeatherParticleCommon");
    static readonly int _weatherParticleCountsId = Shader.PropertyToID("_WeatherParticleCounts");
    static readonly int _weatherParticleDustParamsId = Shader.PropertyToID("_WeatherParticleDustParams");
    static readonly int _weatherParticleRainParamsId = Shader.PropertyToID("_WeatherParticleRainParams");
    static readonly int _weatherParticleRainExtendedId = Shader.PropertyToID("_WeatherParticleRainExtended");
    static readonly int _weatherParticleDistantRainParamsId = Shader.PropertyToID("_WeatherParticleDistantRainParams");
    static readonly int _weatherParticleDistantRainExtendedId = Shader.PropertyToID("_WeatherParticleDistantRainExtended");
    static readonly int _weatherParticleSnowParamsId = Shader.PropertyToID("_WeatherParticleSnowParams");
    static readonly int _weatherParticlePhaseParamsId = Shader.PropertyToID("_WeatherParticlePhaseParams");
    static readonly int _weatherParticleDustColorId = Shader.PropertyToID("_WeatherParticleDustColor");
    static readonly int _weatherParticleSnowColorId = Shader.PropertyToID("_WeatherParticleSnowColor");
    static readonly int _weatherParticleProofId = Shader.PropertyToID("_WeatherParticleProof");

    public bool IsRenderingEnabled =>
        _seaLevelRadius > 0f && (IsDistantPrecipitationEnabled || IsLocalParticleSystemEnabled);

    bool IsDistantPrecipitationEnabled =>
        RenderPrecipitation && Intensity > 0f || DebugMode != DebugView.Off;

    bool IsLocalParticleSystemEnabled =>
        RenderLocalParticles && (DustParticleCount > 0 ||
                                 RainParticleCount > 0 ||
                                 SnowParticleCount > 0 ||
                                 DistantRainParticleCount > 0);

    public bool PrecipitationRenderingEnabled
    {
        get => RenderPrecipitation;
        set => RenderPrecipitation = value;
    }

    public bool LocalPrecipitationParticlesEnabled =>
        RenderLocalParticles && RainParticleCount > 0;

    // Explicit forward so the interface property is satisfied by the serialized field.
    float IPrecipitationDebugControl.StormThreshold => StormThreshold;
    float IPrecipitationDebugControl.LocalParticleRadius => LocalParticleRadius;
    int IPrecipitationDebugControl.DustParticleCount => DustParticleCount;
    int IPrecipitationDebugControl.RainParticleCount => RainParticleCount;
    int IPrecipitationDebugControl.SnowParticleCount => SnowParticleCount;
    int IPrecipitationDebugControl.WeatherParticleProofMode => (int)ParticleProof;
    string IPrecipitationDebugControl.WeatherParticleSettingsSummary =>
        $"rain={RainStreakLength:F2}m/{RainWidth:F3}m/{RainFallSpeed:F1}mps, " +
        $"dust={DustSize:F3}m/{DustStreakLength:F2}m";

    public bool ShouldRenderLocalParticles(Camera camera)
    {
        if (!IsRenderingEnabled || !IsLocalParticleSystemEnabled ||
            DebugMode != DebugView.Off || camera == null)
            return false;

        float cameraAltitude = Vector3.Distance(camera.transform.position, _planetCenter) - _seaLevelRadius;
        return cameraAltitude >= 0f && cameraAltitude <= LocalMaxCameraAltitude;
    }

    void Awake()
    {
        MigrateLocalWeatherParticleSettings();
        ServiceLocator.Register<IPrecipitationDebugControl>(this);
    }

    void OnValidate()
    {
        MigrateLocalWeatherParticleSettings();
        LocalParticleRadius = Mathf.Clamp(LocalParticleRadius, 10f, 300f);
        LocalParticleVerticalRange = Mathf.Clamp(LocalParticleVerticalRange, 5f, 100f);
        DustSize = Mathf.Clamp(DustSize, 0.005f, 0.2f);
        DustStreakLength = Mathf.Clamp(DustStreakLength, 0.02f, 4f);
        RainStreakLength = Mathf.Clamp(RainStreakLength, 0.05f, 2f);
        RainFallSpeed = Mathf.Clamp(RainFallSpeed, 1f, 25f);
        RainWidth = Mathf.Clamp(RainWidth, 0.003f, 0.05f);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        UploadGlobals();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Shader.SetGlobalInt(_precipitationEnabledId, 0);
        Shader.SetGlobalInt(_weatherParticlesEnabledId, 0);
    }

    void Start()
    {
        Initialize();
        UploadGlobals();
    }

    void OnDestroy()
    {
        ServiceLocator.Unregister<IPrecipitationDebugControl>(this);
    }

    void Update()
    {
        UploadGlobals();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;
        UploadGlobals();
    }

    void Initialize()
    {
        if (CloudSettings != null)
            return;

        _cloudController = ServiceLocator.Get<ICloudController>();
        CloudSettings = _cloudController.Settings;

        if (CloudSettings == null)
            throw new System.InvalidOperationException(
                "CloudController settings are missing. Assign CloudSettings or initialize CloudController before precipitation initialization.");
    }

    void UploadGlobals()
    {
        Shader.SetGlobalInt(_precipitationEnabledId, IsDistantPrecipitationEnabled ? 1 : 0);
        Shader.SetGlobalInt(_weatherParticlesEnabledId, IsLocalParticleSystemEnabled ? 1 : 0);
        if (!IsRenderingEnabled)
            return;

        float cloudBaseAltitude = CloudSettings != null ? CloudSettings.BaseAltitude : 330f;
        float bottomRadius = _seaLevelRadius + BottomAltitude;
        float topRadius = _seaLevelRadius + Mathf.Max(BottomAltitude + 1f, cloudBaseAltitude + CloudBaseOverlap);

        Shader.SetGlobalVector(_precipitationPlanetCenterId, _planetCenter);
        Shader.SetGlobalVector(_precipitationRadiiId, new Vector4(bottomRadius, topRadius, MaxDistance, _seaLevelRadius));
        Shader.SetGlobalVector(_precipitationParamsId, new Vector4(
            Intensity,
            StormThreshold,
            StormSoftness,
            MaxOpacity));
        Shader.SetGlobalVector(_precipitationFadeParamsId, new Vector4(
            BottomFeather,
            TopFeather,
            0f,
            0f));
        Shader.SetGlobalVector(_precipitationVisualParamsId, new Vector4(
            CurtainScale,
            0f,
            WindSlant,
            FallSpeed));
        Shader.SetGlobalColor(_precipitationColorId, RainColor);
        Shader.SetGlobalColor(_precipitationStormColorId, StormRainColor);

        int viewSteps = ViewSteps;
        Camera mainCam = Camera.main;
        if (mainCam != null && _seaLevelRadius > 0f)
        {
            float altitude = Vector3.Distance(mainCam.transform.position, _planetCenter) - _seaLevelRadius;
            float t = Mathf.InverseLerp(StepScaleNearAltitude,
                Mathf.Max(StepScaleFarAltitude, StepScaleNearAltitude + 1f), altitude);
            viewSteps = Mathf.RoundToInt(Mathf.Lerp(ViewSteps, MinViewSteps, t));
        }
        viewSteps = Mathf.Max(MinViewSteps,
            Mathf.RoundToInt(viewSteps * QualityController.CloudStepMultiplier));
        Shader.SetGlobalInt(_precipitationViewStepsId, viewSteps);
        Shader.SetGlobalInt(_precipitationDebugModeId, (int)DebugMode);
        Shader.SetGlobalVector(_precipitationDebugDotParamsId, new Vector4(
            DebugDotMinRadius,
            Mathf.Max(DebugDotMaxRadius, DebugDotMinRadius + 0.01f),
            DebugDotOpacity,
            0f));

        Shader.SetGlobalVector(_weatherParticleCommonId, new Vector4(
            LocalParticleRadius,
            LocalMaxCameraAltitude,
            LocalParticleVerticalRange,
            DustTurbulence));
        Shader.SetGlobalVector(_weatherParticleCountsId, new Vector4(
            Mathf.Max(1, DustParticleCount),
            Mathf.Max(1, RainParticleCount),
            Mathf.Max(1, SnowParticleCount),
            Mathf.Max(1, DistantRainParticleCount)));
        Shader.SetGlobalVector(_weatherParticleDustParamsId, new Vector4(
            DustBaseDensity,
            DustStormDensity,
            DustOpacity,
            DustSize));
        Shader.SetGlobalVector(_weatherParticleRainParamsId, new Vector4(
            RainThreshold,
            RainParticleOpacity,
            RainStreakLength,
            RainFallSpeed));
        Shader.SetGlobalVector(_weatherParticleRainExtendedId, new Vector4(
            RainParticleRadius,
            0f, 0f, 0f));
        Shader.SetGlobalVector(_weatherParticleDistantRainParamsId, new Vector4(
            DistantRainParticleRadius,
            DistantRainStreakLength,
            DistantRainOpacity,
            DistantRainThreshold));
        Shader.SetGlobalVector(_weatherParticleDistantRainExtendedId, new Vector4(
            DistantRainWidth,
            0f, 0f, 0f));
        Shader.SetGlobalVector(_weatherParticleSnowParamsId, new Vector4(
            SnowThreshold,
            SnowParticleOpacity,
            SnowSize,
            SnowFallSpeed));
        Shader.SetGlobalVector(_weatherParticlePhaseParamsId, new Vector4(
            SnowTemperatureCelsius,
            SnowTemperatureBlendCelsius,
            RainWidth,
            DustStreakLength));
        Shader.SetGlobalColor(_weatherParticleDustColorId, DustColor);
        Shader.SetGlobalColor(_weatherParticleSnowColorId, SnowColor);
        Shader.SetGlobalInt(_weatherParticleProofId, (int)ParticleProof);
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("intensity", "Get or set precipitation intensity multiplier (range 0-2).", MonoTargetType.Single)]
    string IntensityCmd(float? value = null)
    {
        if (value == null) return $"precipitation intensity: {Intensity:F2}";
        Intensity = Mathf.Clamp(value.Value, 0f, 2f);
        return $"precipitation intensity: {Intensity:F2}";
    }

    [ConsoleCommand("debug-mode", "Get or set precipitation debug visualization mode.", MonoTargetType.Single)]
    string DebugModeCmd(DebugView? mode = null)
    {
        if (mode == null) return $"precipitation debug mode: {DebugMode}";
        DebugMode = mode.Value;
        return $"precipitation debug mode: {DebugMode}";
    }

    void MigrateLocalWeatherParticleSettings()
    {
        if (_localWeatherParticleSettingsVersion >= CurrentLocalWeatherParticleSettingsVersion)
            return;

        // The previous local rain used 95 m lines moving at 520 arbitrary units/s.
        // Those serialized numbers are not compatible with the physical meter-scale renderer.
        LocalParticleRadius = 120f;
        LocalParticleVerticalRange = 18f;
        DustOpacity = 0.45f;
        DustSize = 0.025f;
        DustStreakLength = 0.65f;
        DustTurbulence = 0.28f;
        RainStreakLength = 4.0f;
        RainParticleOpacity = 0.65f;
        RainFallSpeed = 60f;
        RainWidth = 0.06f;
        _localWeatherParticleSettingsVersion = CurrentLocalWeatherParticleSettingsVersion;
    }

    [ConsoleCommand("particles-enabled", "Enable or disable local dust, rain, and snow particles.", MonoTargetType.Single)]
    string ParticlesEnabledCmd(bool? enabled = null)
    {
        if (enabled.HasValue)
            RenderLocalParticles = enabled.Value;
        return $"weather particles: {(RenderLocalParticles ? "on" : "off")}";
    }

    [ConsoleCommand("particle-proof", "Force a local weather-particle profile for visual validation.", MonoTargetType.Single)]
    string ParticleProofCmd(WeatherParticleProof? proof = null)
    {
        if (proof.HasValue)
            ParticleProof = proof.Value;
        return $"weather particle proof: {ParticleProof}";
    }

    [ConsoleCommand("particle-radius", "Get or set local weather-particle radius in meters (dust + snow).", MonoTargetType.Single)]
    string ParticleRadiusCmd(float? radius = null)
    {
        if (radius.HasValue)
            LocalParticleRadius = Mathf.Clamp(radius.Value, 10f, 300f);
        return $"weather particle radius: {LocalParticleRadius:F1} m";
    }

    [ConsoleCommand("rain-radius", "Get or set close rain horizontal radius in meters.", MonoTargetType.Single)]
    string RainRadiusCmd(float? radius = null)
    {
        if (radius.HasValue)
            RainParticleRadius = Mathf.Clamp(radius.Value, 200f, 5000f);
        return $"rain particle radius: {RainParticleRadius:F1} m";
    }

    [ConsoleCommand("distant-rain-radius", "Get or set distant-rain horizontal radius in meters. Distant rain renders longer streaks at planet scale so storms are visible from far away.", MonoTargetType.Single)]
    string DistantRainRadiusCmd(float? radius = null)
    {
        if (radius.HasValue)
            DistantRainParticleRadius = Mathf.Clamp(radius.Value, 800f, 8000f);
        return $"distant rain radius: {DistantRainParticleRadius:F1} m";
    }

    [ConsoleCommand("distant-rain-count", "Get or set the distant-rain particle count.", MonoTargetType.Single)]
    string DistantRainCountCmd(int? count = null)
    {
        if (count.HasValue)
            DistantRainParticleCount = Mathf.Clamp(count.Value, 0, 20000);
        return $"distant rain particles: {DistantRainParticleCount}";
    }

    [ConsoleCommand("distant-rain-length", "Get or set distant-rain streak length in meters.", MonoTargetType.Single)]
    string DistantRainLengthCmd(float? length = null)
    {
        if (length.HasValue)
            DistantRainStreakLength = Mathf.Clamp(length.Value, 2f, 80f);
        return $"distant rain streak length: {DistantRainStreakLength:F1} m";
    }

    [ConsoleCommand("dust-count", "Get or set the maximum procedural dust instance count.", MonoTargetType.Single)]
    string DustCountCmd(int? count = null)
    {
        if (count.HasValue)
            DustParticleCount = Mathf.Clamp(count.Value, 0, 8000);
        return $"dust instances: {DustParticleCount}";
    }

    [ConsoleCommand("rain-count", "Get or set the maximum procedural rain instance count.", MonoTargetType.Single)]
    string RainCountCmd(int? count = null)
    {
        if (count.HasValue)
            RainParticleCount = Mathf.Clamp(count.Value, 0, 8000);
        return $"rain instances: {RainParticleCount}";
    }

    [ConsoleCommand("snow-count", "Get or set the maximum procedural snow instance count.", MonoTargetType.Single)]
    string SnowCountCmd(int? count = null)
    {
        if (count.HasValue)
            SnowParticleCount = Mathf.Clamp(count.Value, 0, 8000);
        return $"snow instances: {SnowParticleCount}";
    }

    [ConsoleCommand("rain-length", "Get or set local rain streak length in meters.", MonoTargetType.Single)]
    string RainLengthCmd(float? length = null)
    {
        if (length.HasValue)
            RainStreakLength = Mathf.Clamp(length.Value, 0.05f, 2f);
        return $"rain streak length: {RainStreakLength:F2} m";
    }

    [ConsoleCommand("rain-speed", "Get or set local rain fall speed in meters per second.", MonoTargetType.Single)]
    string RainSpeedCmd(float? speed = null)
    {
        if (speed.HasValue)
            RainFallSpeed = Mathf.Clamp(speed.Value, 1f, 25f);
        return $"rain fall speed: {RainFallSpeed:F1} m/s";
    }

    [ConsoleCommand("rain-width", "Get or set local rain streak half-width in meters.", MonoTargetType.Single)]
    string RainWidthCmd(float? width = null)
    {
        if (width.HasValue)
            RainWidth = Mathf.Clamp(width.Value, 0.003f, 0.05f);
        return $"rain streak half-width: {RainWidth:F3} m";
    }

    [ConsoleCommand("dust-size", "Get or set low-wind dust mote half-width in meters.", MonoTargetType.Single)]
    string DustSizeCmd(float? size = null)
    {
        if (size.HasValue)
            DustSize = Mathf.Clamp(size.Value, 0.005f, 0.2f);
        return $"dust mote half-width: {DustSize:F3} m";
    }

    [ConsoleCommand("dust-length", "Get or set high-wind dust streak length in meters.", MonoTargetType.Single)]
    string DustLengthCmd(float? length = null)
    {
        if (length.HasValue)
            DustStreakLength = Mathf.Clamp(length.Value, 0.02f, 4f);
        return $"dust streak length: {DustStreakLength:F2} m";
    }
}
