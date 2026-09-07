using UnityEngine;

/// <summary>
/// World-anchored rain particle system. Persistent particle state lives in a
/// <see cref="ComputeBuffer"/>; a compute shader advances positions each frame
/// (gravity toward the planet center, plus wind force); the render feature
/// draws each particle as a stretched billboard via DrawProcedural reading the
/// same buffer.
/// <para>
/// Design intent (see also conversation 2026-06-09):
/// <list type="bullet">
///   <item>Particles have real <c>position</c> and <c>velocity</c>. No
///         <c>frac()</c>-based teleportation; no instanceID-derived state.</item>
///   <item>Gravity points toward planet center each frame, so motion is
///         spherical-planet correct.</item>
///   <item>On landing (altitude &lt; sea radius), a new drop spawns at the
///         cloud top above a random direction near the camera — the
///         "new drop in the cloud" case.</item>
///   <item>On drifting beyond camera-near radius (camera moved), the particle
///         respawns at a RANDOM altitude in the fall column — the "freshly-
///         visible region looks like rain has been falling" case.</item>
///   <item>The render shader samples weather <c>dynamics.b</c> per particle to
///         gate visibility, so drops only show where there is actual rain.</item>
/// </list>
/// </para>
/// </summary>
[CommandPrefix("rain-particles")]
public sealed class RainParticleController : MonoBehaviour, IRainParticleRenderer, IWorldServiceRegistrar
{
    const int MaxParticleCount = 100000;

    [Header("Particle Budget")]
    [Tooltip("Total raindrop instances in the persistent buffer.")]
    [Range(0, MaxParticleCount)] public int ParticleCountSetting = 30000;

    [Header("Spawn / Respawn")]
    [Tooltip("Horizontal radius around the camera within which particles are kept active. Smaller radius packs the same particle count denser around the camera — better for human-scale rain where you mostly see drops close in.")]
    [Range(50f, 3000f)] public float CameraNearRadius = 300f;

    [Tooltip("0 = spawn anywhere around the camera (full sphere). 1 = tight cone hugging camera-forward. Higher values concentrate particles where the camera is looking.")]
    [Range(0f, 1f)] public float ForwardConeBias = 0.35f;

    [Header("Physics")]
    [Tooltip("Constant downward fall speed in m/s. No gravity ramp — drops fall at this rate from the moment they spawn. Real rain terminal velocity is ~9 m/s but reads as slow motion on screen. Each drop gets a stable 0.8x..1.2x personal multiplier so the field doesn't synchronize into bands.")]
    [Range(5f, 5000f)] public float FallSpeedMps = 200f;

    [Tooltip("Fraction of wind speed added to each particle's horizontal velocity. 0 = drops fall straight down. 0.2 = subtle wind drift.")]
    [Range(0f, 1f)] public float WindCoupling = 0.2f;

    [Header("LOD Fade")]
    [Tooltip("Altitude band (meters) below LocalMaxCameraAltitude over which rain fades to zero. Keeps the particle cutoff invisible.")]
    [Range(50f, 1000f)] public float AltitudeFadeBand = 200f;

    [Header("Visuals")]
    [Tooltip("Width of each rendered streak in meters. Human-scale rain is ~1cm; bigger values read as 'fire hoses' next to a 1.8m capsule.")]
    [Range(0.002f, 0.5f)] public float StreakWidth = 0.012f;

    [Tooltip("Length of each rendered streak in meters. Human-scale visible rain streaks are ~30-60cm; larger values dwarf the player reference.")]
    [Range(0.05f, 5f)] public float StreakLength = 0.4f;

    [Tooltip("Dynamics.b sample (rain rate) threshold below which a drop renders invisible. Lower = drops show even in light rain regions.")]
    [Range(0f, 1f)] public float VisibilityThreshold = 0.05f;

    [Tooltip("Multiplier on rain rate (dynamics.b) for the per-drop density gate. Each drop has a stable rank 0..1; it renders when its rank is below rate*scale. Heavy rain shows lots of drops (downpour), light rain shows few (sprinkle). 1.0 = full density at dynamics.b == 1; 3.0 = full density at dynamics.b == 0.33.")]
    [Range(0.5f, 6f)] public float DensityScale = 2.5f;

    [Tooltip("Drop color. Brighter (closer to white) cuts through atmospheric scattering tint better than a gray-blue.")]
    public Color RainColor = new Color(0.92f, 0.95f, 1.0f, 0.95f);

    public bool IsReadyToDraw => _ready && _particleBuffer != null && _runtimeMaterial != null;
    public int ParticleCount => _ready ? Mathf.Min(ParticleCountSetting, _allocatedCount) : 0;
    public Material Material => _runtimeMaterial;

    const int RaindropStride = sizeof(float) * 8;  // float3 pos, float3 vel, float life, float pad

    ComputeShader _updateCompute;
    Material _runtimeMaterial;
    ComputeBuffer _particleBuffer;
    int _updateKernel;
    int _allocatedCount;
    bool _ready;

    Vector3 _planetCenter;
    float _seaLevelRadius;
    float _cloudBottomRadius;
    float _cloudTopRadius;
    IWeatherProvider _weatherProvider;
    IPrecipitationDebugControl _precipControl;
    float _altitudeFadeAlpha = 1f;

    // --- Compute shader property IDs -------------------------------------------------
    static readonly int _rainParticlesId = Shader.PropertyToID("_RainParticles");
    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _seaRadiusId = Shader.PropertyToID("_SeaRadius");
    static readonly int _cloudBottomRadiusId = Shader.PropertyToID("_CloudBottomRadius");
    static readonly int _cloudTopRadiusId = Shader.PropertyToID("_CloudTopRadius");
    static readonly int _cameraPositionId = Shader.PropertyToID("_CameraPosition");
    static readonly int _cameraForwardId = Shader.PropertyToID("_CameraForward");
    static readonly int _cameraNearRadiusId = Shader.PropertyToID("_CameraNearRadius");
    static readonly int _forwardConeBiasId = Shader.PropertyToID("_ForwardConeBias");
    static readonly int _windDirectionId = Shader.PropertyToID(ShaderGlobalIds.WindDirection);
    static readonly int _windSpeedMpsId = Shader.PropertyToID(ShaderGlobalIds.WindSpeedMps);
    static readonly int _windCouplingId = Shader.PropertyToID("_WindCoupling");
    static readonly int _fallSpeedId = Shader.PropertyToID("_FallSpeedMps");
    static readonly int _deltaTimeId = Shader.PropertyToID("_DeltaTime");
    static readonly int _frameSeedId = Shader.PropertyToID("_FrameSeed");
    static readonly int _activeCountId = Shader.PropertyToID("_ActiveCount");

    // --- Render material property IDs (set on shared material) -----------------------
    static readonly int _rainStreakWidthId = Shader.PropertyToID("_RainStreakWidth");
    static readonly int _rainStreakLengthId = Shader.PropertyToID("_RainStreakLength");
    static readonly int _rainColorId = Shader.PropertyToID("_RainColor");
    static readonly int _rainVisibilityThresholdId = Shader.PropertyToID("_RainVisibilityThreshold");
    static readonly int _rainDensityScaleId = Shader.PropertyToID("_RainDensityScale");
    static readonly int _rainPlanetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _rainSeaRadiusId = Shader.PropertyToID("_SeaRadius");

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<IRainParticleRenderer>(this);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EnsureResources();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void OnDestroy()
    {
        ReleaseResources();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        // Same formula PrecipitationController uses for _PrecipitationRadii.y, computed
        // from the DTOs directly: reading the shader global back raced its own
        // PlanetGeneratedEvent handler (listener order is not guaranteed).
        float cloudBase = 330f;
        float bottomAltitude = 25f;
        float cloudBaseOverlap = 45f;
        if (SettingsProvider.IsRegistered<CloudDto>())
            cloudBase = SettingsProvider.GetSettings<CloudDto>().BaseAltitude;
        if (SettingsProvider.IsRegistered<PrecipitationDto>())
        {
            var precip = SettingsProvider.GetSettings<PrecipitationDto>();
            bottomAltitude = precip.BottomAltitude;
            cloudBaseOverlap = precip.CloudBaseOverlap;
        }
        _cloudBottomRadius = _seaLevelRadius
            + Mathf.Max(bottomAltitude + 1f, cloudBase + cloudBaseOverlap);
        _cloudTopRadius = _cloudBottomRadius + 60f;
        _ready = true;
    }

    void EnsureResources()
    {
        if (_updateCompute == null)
        {
            _updateCompute = Resources.Load<ComputeShader>("RainParticleUpdate");
            if (_updateCompute == null)
            {
                LoggerProvider.Get().Log(LogLevel.Error, "RainParticleController", "Failed to load RainParticleUpdate.compute from Resources.");
                return;
            }
            _updateKernel = _updateCompute.FindKernel("RainUpdate");
        }

        if (_runtimeMaterial == null)
        {
            var shader = Shader.Find("Hidden/RainParticles");
            if (shader == null)
            {
                LoggerProvider.Get().Log(LogLevel.Error, "RainParticleController", "Shader 'Hidden/RainParticles' not found.");
                return;
            }
            _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        EnsureBuffer();
    }

    void EnsureBuffer()
    {
        int desired = Mathf.Max(64, ParticleCountSetting);
        if (_particleBuffer != null && _allocatedCount == desired)
            return;

        ReleaseBuffer();
        _particleBuffer = new ComputeBuffer(desired, RaindropStride, ComputeBufferType.Default);
        // Zero the buffer so each entry has Life01 == 0 — the compute shader
        // treats that as "needs initial spawn" and seeds with a random altitude.
        var zero = new byte[desired * RaindropStride];
        _particleBuffer.SetData(zero);
        _allocatedCount = desired;
    }

    void ReleaseBuffer()
    {
        _particleBuffer?.Release();
        _particleBuffer = null;
        _allocatedCount = 0;
    }

    void ReleaseResources()
    {
        ReleaseBuffer();
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
        }
    }

    void Update()
    {
        if (!_ready)
            return;

        EnsureResources();
        if (_particleBuffer == null || _updateCompute == null)
            return;

        if (_allocatedCount != Mathf.Max(64, ParticleCountSetting))
            EnsureBuffer();

        UpdateAltitudeFade();
        UploadMaterialParams();
        DispatchUpdate();
    }

    void UpdateAltitudeFade()
    {
        if (_precipControl == null)
            ServiceLocator.TryGet(out _precipControl);

        Camera cam = Camera.main;
        if (_precipControl == null || cam == null || _seaLevelRadius <= 0f)
        {
            _altitudeFadeAlpha = 1f;
            return;
        }

        float altitude = Vector3.Distance(cam.transform.position, _planetCenter) - _seaLevelRadius;
        float maxAlt = _precipControl.LocalMaxCameraAltitude;
        _altitudeFadeAlpha = 1f - Mathf.Clamp01(Mathf.InverseLerp(maxAlt - AltitudeFadeBand, maxAlt, altitude));
    }

    float _uploadedStreakWidth = float.NaN;
    float _uploadedStreakLength = float.NaN;
    float _uploadedVisibilityThreshold = float.NaN;
    float _uploadedDensityScale = float.NaN;
    float _uploadedSeaRadius = float.NaN;
    Color _uploadedFadeColor = new Color(float.NaN, 0f, 0f, 0f);
    Vector3 _uploadedPlanetCenter = new Vector3(float.NaN, 0f, 0f);

    void UploadMaterialParams()
    {
        if (_runtimeMaterial == null)
            return;
        _runtimeMaterial.SetBuffer(_rainParticlesId, _particleBuffer);
        if (StreakWidth != _uploadedStreakWidth)
            _runtimeMaterial.SetFloat(_rainStreakWidthId, _uploadedStreakWidth = StreakWidth);
        if (StreakLength != _uploadedStreakLength)
            _runtimeMaterial.SetFloat(_rainStreakLengthId, _uploadedStreakLength = StreakLength);
        Color fadeColor = RainColor;
        fadeColor.a *= _altitudeFadeAlpha;
        if (fadeColor != _uploadedFadeColor)
            _runtimeMaterial.SetColor(_rainColorId, _uploadedFadeColor = fadeColor);
        if (VisibilityThreshold != _uploadedVisibilityThreshold)
            _runtimeMaterial.SetFloat(_rainVisibilityThresholdId, _uploadedVisibilityThreshold = VisibilityThreshold);
        if (DensityScale != _uploadedDensityScale)
            _runtimeMaterial.SetFloat(_rainDensityScaleId, _uploadedDensityScale = DensityScale);
        if (_planetCenter != _uploadedPlanetCenter)
            _runtimeMaterial.SetVector(_rainPlanetCenterId, _uploadedPlanetCenter = _planetCenter);
        if (_seaLevelRadius != _uploadedSeaRadius)
            _runtimeMaterial.SetFloat(_rainSeaRadiusId, _uploadedSeaRadius = _seaLevelRadius);
    }

    void DispatchUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        if (_weatherProvider == null)
            ServiceLocator.TryGet(out _weatherProvider);
        Vector3 windDirection = _weatherProvider != null ? _weatherProvider.WindDirection : Vector3.right;
        float windSpeed = _weatherProvider != null ? _weatherProvider.WindSpeedMetersPerSecond : 0f;

        _updateCompute.SetBuffer(_updateKernel, _rainParticlesId, _particleBuffer);
        _updateCompute.SetVector(_planetCenterId, _planetCenter);
        _updateCompute.SetFloat(_seaRadiusId, _seaLevelRadius);
        _updateCompute.SetFloat(_cloudBottomRadiusId, _cloudBottomRadius);
        _updateCompute.SetFloat(_cloudTopRadiusId, _cloudTopRadius);
        _updateCompute.SetVector(_cameraPositionId, cam.transform.position);
        _updateCompute.SetVector(_cameraForwardId, cam.transform.forward);
        _updateCompute.SetFloat(_cameraNearRadiusId, CameraNearRadius);
        _updateCompute.SetFloat(_forwardConeBiasId, ForwardConeBias);
        _updateCompute.SetVector(_windDirectionId, windDirection);
        _updateCompute.SetFloat(_windSpeedMpsId, windSpeed);
        _updateCompute.SetFloat(_windCouplingId, WindCoupling);
        _updateCompute.SetFloat(_fallSpeedId, FallSpeedMps);
        _updateCompute.SetFloat(_deltaTimeId, Time.deltaTime);
        _updateCompute.SetInt(_frameSeedId, (int)(Time.frameCount * 2654435761u));

        _updateCompute.SetInt(_activeCountId, ParticleCount);
        if (ParticleCount <= 0) return;
        int groups = (ParticleCount + 63) / 64;
        _updateCompute.Dispatch(_updateKernel, groups, 1, 1);
    }

    // --- Console commands -------------------------------------------------------------

    [ConsoleCommand("count", "Get or set the rain particle count.", MonoTargetType.Single)]
    string CountCmd(int? value = null)
    {
        if (value.HasValue) ParticleCountSetting = Mathf.Clamp(value.Value, 0, MaxParticleCount);
        return $"rain particles: {ParticleCountSetting}";
    }

    [ConsoleCommand("near-radius", "Camera-near radius in meters. Particles beyond this respawn near camera.", MonoTargetType.Single)]
    string NearRadiusCmd(float? value = null)
    {
        if (value.HasValue) CameraNearRadius = Mathf.Clamp(value.Value, 50f, 3000f);
        return $"rain near-radius: {CameraNearRadius:F0} m";
    }

    [ConsoleCommand("fall-speed", "Constant fall speed in m/s. Each drop gets a 0.8x..1.2x personal multiplier on top of this.", MonoTargetType.Single)]
    string FallSpeedCmd(float? value = null)
    {
        if (value.HasValue) FallSpeedMps = Mathf.Clamp(value.Value, 5f, 5000f);
        return $"rain fall speed: {FallSpeedMps:F1} m/s";
    }

    [ConsoleCommand("streak-length", "Streak length in meters.", MonoTargetType.Single)]
    string StreakLenCmd(float? value = null)
    {
        if (value.HasValue) StreakLength = Mathf.Clamp(value.Value, 0.05f, 5f);
        return $"rain streak length: {StreakLength:F3} m";
    }

    [ConsoleCommand("streak-width", "Streak width in meters.", MonoTargetType.Single)]
    string StreakWidCmd(float? value = null)
    {
        if (value.HasValue) StreakWidth = Mathf.Clamp(value.Value, 0.002f, 0.5f);
        return $"rain streak width: {StreakWidth:F4} m";
    }

    [ConsoleCommand("density-scale", "Density gate multiplier on rain rate. Higher = denser rain at the same dynamics.b. Tune to taste between sprinkle and downpour.", MonoTargetType.Single)]
    string DensityScaleCmd(float? value = null)
    {
        if (value.HasValue) DensityScale = Mathf.Clamp(value.Value, 0.5f, 6f);
        return $"rain density scale: {DensityScale:F2}";
    }
}
