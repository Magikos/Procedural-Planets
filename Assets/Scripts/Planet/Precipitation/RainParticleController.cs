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
[DefaultExecutionOrder(-50)]
public sealed class RainParticleController : MonoBehaviour, IRainParticleRenderer
{
    [Header("Particle Budget")]
    [Tooltip("Total raindrop instances in the persistent buffer.")]
    [Range(0, 100000)] public int ParticleCountSetting = 30000;

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

    // --- Render material property IDs (set on shared material) -----------------------
    static readonly int _rainStreakWidthId = Shader.PropertyToID("_RainStreakWidth");
    static readonly int _rainStreakLengthId = Shader.PropertyToID("_RainStreakLength");
    static readonly int _rainColorId = Shader.PropertyToID("_RainColor");
    static readonly int _rainVisibilityThresholdId = Shader.PropertyToID("_RainVisibilityThreshold");
    static readonly int _rainDensityScaleId = Shader.PropertyToID("_RainDensityScale");
    static readonly int _rainPlanetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _rainSeaRadiusId = Shader.PropertyToID("_SeaRadius");

    void Awake()
    {
        ServiceLocator.Register<IRainParticleRenderer>(this);
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
        ServiceLocator.Unregister<IRainParticleRenderer>(this);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;

        // Cloud bottom altitude comes from CloudSettings; PrecipitationController
        // exposes the precipitation top radius via the existing _PrecipitationRadii
        // global, so we read it from there to stay in lockstep without taking a
        // direct dependency on CloudSettings or PrecipitationController.
        Vector4 precipRadii = Shader.GetGlobalVector(Shader.PropertyToID(ShaderGlobalIds.PrecipitationRadii));
        _cloudBottomRadius = precipRadii.y > 0f ? precipRadii.y : _seaLevelRadius + 375f;
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
                Debug.LogError("RainParticleController: failed to load RainParticleUpdate.compute from Resources.");
                return;
            }
            _updateKernel = _updateCompute.FindKernel("RainUpdate");
        }

        if (_runtimeMaterial == null)
        {
            var shader = Shader.Find("Hidden/RainParticles");
            if (shader == null)
            {
                Debug.LogError("RainParticleController: shader 'Hidden/RainParticles' not found.");
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
            DestroyImmediate(_runtimeMaterial);
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

        UploadMaterialParams();
        DispatchUpdate();
    }

    void UploadMaterialParams()
    {
        if (_runtimeMaterial == null)
            return;
        _runtimeMaterial.SetBuffer(_rainParticlesId, _particleBuffer);
        _runtimeMaterial.SetFloat(_rainStreakWidthId, StreakWidth);
        _runtimeMaterial.SetFloat(_rainStreakLengthId, StreakLength);
        _runtimeMaterial.SetColor(_rainColorId, RainColor);
        _runtimeMaterial.SetFloat(_rainVisibilityThresholdId, VisibilityThreshold);
        _runtimeMaterial.SetFloat(_rainDensityScaleId, DensityScale);
        _runtimeMaterial.SetVector(_rainPlanetCenterId, _planetCenter);
        _runtimeMaterial.SetFloat(_rainSeaRadiusId, _seaLevelRadius);
    }

    void DispatchUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 windDirection = Shader.GetGlobalVector(Shader.PropertyToID(ShaderGlobalIds.WindDirection));
        if (windDirection.sqrMagnitude < 1e-6f)
            windDirection = Vector3.right;
        else
            windDirection.Normalize();
        float windSpeed = Shader.GetGlobalFloat(Shader.PropertyToID(ShaderGlobalIds.WindSpeedMps));

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

        int groups = (ParticleCount + 63) / 64;
        _updateCompute.Dispatch(_updateKernel, Mathf.Max(1, groups), 1, 1);
    }

    // --- Console commands -------------------------------------------------------------

    [CommandPrefix("rain-particles")]
    static class RainParticleCommands
    {
        static RainParticleController Get() => Object.FindAnyObjectByType<RainParticleController>();

        [ConsoleCommand("count", "Get or set the rain particle count.", MonoTargetType.Single)]
        public static string Count(int? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.ParticleCountSetting = Mathf.Clamp(value.Value, 0, 50000);
            return $"rain particles: {c.ParticleCountSetting}";
        }

        [ConsoleCommand("near-radius", "Camera-near radius in meters. Particles beyond this respawn near camera.", MonoTargetType.Single)]
        public static string NearRadius(float? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.CameraNearRadius = Mathf.Clamp(value.Value, 50f, 3000f);
            return $"rain near-radius: {c.CameraNearRadius:F0} m";
        }

        [ConsoleCommand("fall-speed", "Constant fall speed in m/s. Each drop gets a 0.8x..1.2x personal multiplier on top of this.", MonoTargetType.Single)]
        public static string FallSpeed(float? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.FallSpeedMps = Mathf.Clamp(value.Value, 5f, 5000f);
            return $"rain fall speed: {c.FallSpeedMps:F1} m/s";
        }

        [ConsoleCommand("streak-length", "Streak length in meters.", MonoTargetType.Single)]
        public static string StreakLen(float? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.StreakLength = Mathf.Clamp(value.Value, 0.05f, 5f);
            return $"rain streak length: {c.StreakLength:F3} m";
        }

        [ConsoleCommand("streak-width", "Streak width in meters.", MonoTargetType.Single)]
        public static string StreakWid(float? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.StreakWidth = Mathf.Clamp(value.Value, 0.002f, 0.5f);
            return $"rain streak width: {c.StreakWidth:F4} m";
        }

        [ConsoleCommand("density-scale", "Density gate multiplier on rain rate. Higher = denser rain at the same dynamics.b. Tune to taste between sprinkle and downpour.", MonoTargetType.Single)]
        public static string DensityScale(float? value = null)
        {
            var c = Get(); if (c == null) return "no RainParticleController";
            if (value.HasValue) c.DensityScale = Mathf.Clamp(value.Value, 0.5f, 6f);
            return $"rain density scale: {c.DensityScale:F2}";
        }
    }
}
