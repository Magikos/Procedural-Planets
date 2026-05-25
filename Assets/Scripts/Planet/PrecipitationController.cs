using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Publishes render settings for distant precipitation shafts. The renderer samples
/// the shared cloud weather map, so rain stays tied to storm cells.
/// </summary>
public class PrecipitationController : MonoBehaviour, IPrecipitationDebugControl
{
    public enum DebugView
    {
        Off = 0,
        RainMask = 1,
        RainDots = 2,
        StormDots = 3
    }

    [Header("References")]
    public CloudSettings CloudSettings;

    CloudController _cloudController;

    [Header("Rendering")]
    public bool RenderPrecipitation = true;
    [Range(0f, 2f)] public float Intensity = 1.15f;
    [Range(0f, 1f)] public float StormThreshold = 0.55f;
    [Range(0.01f, 1f)] public float StormSoftness = 0.2f;
    [Range(0f, 1f)] public float MaxOpacity = 0.38f;
    [Range(4, 48)] public int ViewSteps = 32;
    [Range(1000f, 50000f)] public float MaxDistance = 22000f;

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

    [Header("Local Particles")]
    public bool RenderLocalParticles = true;
    [Range(0, 8000)] public int LocalParticleCount = 2600;
    [Range(100f, 2500f)] public float LocalParticleRadius = 900f;
    [Range(10f, 300f)] public float LocalStreakLength = 95f;
    [Range(0f, 1f)] public float LocalParticleOpacity = 0.45f;
    [Range(50f, 1400f)] public float LocalFallSpeed = 520f;
    [Range(0f, 500f)] public float LocalWindDrift = 125f;
    [Range(0f, 1f)] public float LocalRainThreshold = 0.16f;
    [Range(100f, 5000f)] public float LocalMaxCameraAltitude = 1600f;

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
    static readonly int _precipitationLocalParamsId = Shader.PropertyToID("_PrecipitationLocalParams");
    static readonly int _precipitationLocalMotionId = Shader.PropertyToID("_PrecipitationLocalMotion");

    public bool IsRenderingEnabled =>
        _seaLevelRadius > 0f && (RenderPrecipitation && Intensity > 0f || DebugMode != DebugView.Off);

    public bool PrecipitationRenderingEnabled
    {
        get => RenderPrecipitation;
        set => RenderPrecipitation = value;
    }

    public bool LocalPrecipitationParticlesEnabled => RenderLocalParticles;

    public bool ShouldRenderLocalParticles(Camera camera)
    {
        if (!IsRenderingEnabled || !RenderPrecipitation || !RenderLocalParticles ||
            DebugMode != DebugView.Off || LocalParticleCount <= 0 || camera == null)
            return false;

        float cameraAltitude = Vector3.Distance(camera.transform.position, _planetCenter) - _seaLevelRadius;
        return cameraAltitude >= 0f && cameraAltitude <= LocalMaxCameraAltitude;
    }

    void Awake()
    {
        ServiceLocator.Register<IPrecipitationDebugControl>(this);
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

        _cloudController = ServiceLocator.Get<CloudController>();
        CloudSettings = _cloudController.Settings;

        if (CloudSettings == null)
            throw new System.InvalidOperationException(
                "CloudController settings are missing. Assign CloudSettings or initialize CloudController before precipitation initialization.");
    }

    void UploadGlobals()
    {
        Shader.SetGlobalInt(_precipitationEnabledId, IsRenderingEnabled ? 1 : 0);
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
        Shader.SetGlobalInt(_precipitationViewStepsId, ViewSteps);
        Shader.SetGlobalInt(_precipitationDebugModeId, (int)DebugMode);
        Shader.SetGlobalVector(_precipitationDebugDotParamsId, new Vector4(
            DebugDotMinRadius,
            Mathf.Max(DebugDotMaxRadius, DebugDotMinRadius + 0.01f),
            DebugDotOpacity,
            0f));
        Shader.SetGlobalVector(_precipitationLocalParamsId, new Vector4(
            LocalParticleRadius,
            LocalStreakLength,
            LocalParticleOpacity,
            LocalWindDrift));
        Shader.SetGlobalVector(_precipitationLocalMotionId, new Vector4(
            LocalFallSpeed,
            LocalRainThreshold,
            LocalMaxCameraAltitude,
            Mathf.Max(1, LocalParticleCount)));
    }
}
