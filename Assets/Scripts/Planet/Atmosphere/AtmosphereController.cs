using UnityEngine;

[CommandPrefix("atmosphere")]
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public AtmosphereSettings Settings;
    public CelestialManager CelestialManager;
    public ComputeShader OpticalDepthCompute;

    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _bakedOpticalDepth;
    float _lastBakedScaleR, _lastBakedScaleM, _lastBakedAtmoScale;
    int _lastBakedSize, _lastBakedSteps;
    IPlanet _planet;
    // Per-frame Update() previously re-pushed ~20 SetGlobal* calls every tick. Only _SunParams
    // genuinely changes per frame; the rest are bound to (Settings, planet) and only need a
    // re-upload when the planet regenerates or the Settings reference swaps.
    bool _staticPropertiesDirty = true;
    AtmosphereSettings _lastStaticSettings;

    static readonly int _sunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _planetRadiusId = Shader.PropertyToID("_SeaLevelRadius");
    static readonly int _densityOriginRadiusId = Shader.PropertyToID("_DensityOriginRadius");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int _viewStepsId = Shader.PropertyToID("_ViewSteps");
    static readonly int _sunStepsId = Shader.PropertyToID("_SunSteps");
    static readonly int _rayleighScatteringId = Shader.PropertyToID("_RayleighScattering");
    static readonly int _rayleighScaleHeightId = Shader.PropertyToID("_RayleighScaleHeight");
    static readonly int _mieScatteringId = Shader.PropertyToID("_MieScatteringCoeff");
    static readonly int _mieScaleHeightId = Shader.PropertyToID("_MieScaleHeight");
    static readonly int _mieAnisotropyId = Shader.PropertyToID("_MieAnisotropy");
    static readonly int _sunIntensityId = Shader.PropertyToID("_SunIntensity");
    static readonly int _sunDiscSizeId = Shader.PropertyToID("_SunDiscSize");
    static readonly int _sunDiscBlendId = Shader.PropertyToID("_SunDiscBlend");
    static readonly int _lightShaftParamsId = Shader.PropertyToID("_LightShaftParams");
    static readonly int _lightShaftParams2Id = Shader.PropertyToID("_LightShaftParams2");
    static readonly int _lightShaftSamplesId = Shader.PropertyToID("_LightShaftSamples");
    static readonly int _debugModeId = Shader.PropertyToID("_DebugMode");
    static readonly int _bakedOpticalDepthId = Shader.PropertyToID("_BakedOpticalDepth");
    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);

    void Start()
    {
        InitializeDependencies();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }

    void OnDestroy() => _bakedOpticalDepth?.Release();

    void Update()
    {
        if (CelestialManager != null)
            Shader.SetGlobalVector(_sunParamsId, CelestialManager.SunDirection);

        if (Settings == null || _planetRadius <= 0f) return;

        EnsureStaticPropertiesUploaded();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : _planetRadius * 0.95f;

        InitializeDependencies();
        _planetCenter = _planet.Transform.position;
        _staticPropertiesDirty = true;

        Initialize();
    }

    void InitializeDependencies()
    {
        if (_planet == null)
            _planet = ServiceLocator.Get<IPlanet>();
    }

    void Initialize()
    {
        BakeOpticalDepth();
        EnsureStaticPropertiesUploaded();
    }

    // Uploads (planet, Settings)-bound shader globals. Skipped after the first upload unless the
    // planet regenerates or the Settings reference swaps; _SunParams (which actually changes per
    // frame) is handled directly in Update().
    void EnsureStaticPropertiesUploaded()
    {
        if (!_staticPropertiesDirty && _lastStaticSettings == Settings) return;
        _staticPropertiesDirty = false;
        _lastStaticSettings = Settings;

        float atmosphereRadius = _planetRadius * Settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;

        if (LutNeedsRebake()) BakeOpticalDepth();

        Vector3 center = _planetCenter;

        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalFloat(_planetRadiusId, _seaLevelRadius);
        Shader.SetGlobalFloat(_densityOriginRadiusId, _seaLevelRadius);
        Shader.SetGlobalFloat(_atmosphereRadiusId, atmosphereRadius);

        Shader.SetGlobalInt(_viewStepsId, Settings.ViewSteps);
        Shader.SetGlobalInt(_sunStepsId, Settings.SunSteps);

        Shader.SetGlobalVector(_rayleighScatteringId, Settings.RayleighScattering);
        Shader.SetGlobalFloat(_rayleighScaleHeightId, Settings.RayleighScaleHeight * atmosphereThickness);
        Shader.SetGlobalFloat(_mieScatteringId, Settings.MieScattering);
        Shader.SetGlobalFloat(_mieScaleHeightId, Settings.MieScaleHeight * atmosphereThickness);
        Shader.SetGlobalFloat(_mieAnisotropyId, Settings.MieAnisotropy);

        Shader.SetGlobalFloat(_sunIntensityId, Settings.SunIntensity);
        Shader.SetGlobalFloat(_sunDiscSizeId, Settings.SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, Settings.SunDiscBlend);
        Shader.SetGlobalVector(_lightShaftParamsId, new Vector4(
            Settings.EnableLightShafts ? Settings.LightShaftStrength : 0f,
            Settings.LightShaftDensity,
            Settings.LightShaftDecay,
            Settings.LightShaftWeight));
        Shader.SetGlobalVector(_lightShaftParams2Id, new Vector4(
            Settings.LightShaftExposure,
            Settings.LightShaftThreshold,
            0.25f,
            1.35f));
        Shader.SetGlobalInt(_lightShaftSamplesId, Settings.EnableLightShafts ? Settings.LightShaftSamples : 0);
        Shader.SetGlobalInt(_debugModeId, Settings.DebugMode);
    }

    bool LutNeedsRebake()
    {
        return Settings.RayleighScaleHeight != _lastBakedScaleR
            || Settings.MieScaleHeight != _lastBakedScaleM
            || Settings.AtmosphereScale != _lastBakedAtmoScale
            || Settings.BakeTextureSize != _lastBakedSize
            || Settings.BakeSteps != _lastBakedSteps;
    }

    void BakeOpticalDepth()
    {
        if (OpticalDepthCompute == null || Settings == null || _seaLevelRadius <= 0f) return;

        float atmosphereRadius = _planetRadius * Settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;
        int size = Settings.BakeTextureSize;

        if (_bakedOpticalDepth != null && _bakedOpticalDepth.width != size)
        {
            _bakedOpticalDepth.Release();
            _bakedOpticalDepth = null;
        }

        if (_bakedOpticalDepth == null)
        {
            _bakedOpticalDepth = new RenderTexture(size, size, 0, RenderTextureFormat.RGHalf)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "BakedOpticalDepth"
            };
            _bakedOpticalDepth.Create();
        }

        int kernel = OpticalDepthCompute.FindKernel("Main");
        OpticalDepthCompute.SetTexture(kernel, "_Result", _bakedOpticalDepth);
        OpticalDepthCompute.SetInt("_TextureSize", size);
        OpticalDepthCompute.SetInt("_NumSteps", Settings.BakeSteps);
        OpticalDepthCompute.SetFloat("_SeaLevelRadius", _seaLevelRadius);
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDepthCompute.SetFloat("_RayleighScaleHeight", Settings.RayleighScaleHeight * atmosphereThickness);
        OpticalDepthCompute.SetFloat("_MieScaleHeight", Settings.MieScaleHeight * atmosphereThickness);

        int groups = Mathf.CeilToInt(size / 8f);
        OpticalDepthCompute.Dispatch(kernel, groups, groups, 1);

        Shader.SetGlobalTexture(_bakedOpticalDepthId, _bakedOpticalDepth);

        _lastBakedScaleR = Settings.RayleighScaleHeight;
        _lastBakedScaleM = Settings.MieScaleHeight;
        _lastBakedAtmoScale = Settings.AtmosphereScale;
        _lastBakedSize = Settings.BakeTextureSize;
        _lastBakedSteps = Settings.BakeSteps;
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("sun-intensity", "Get or set scattering sun intensity (range 1-100).", MonoTargetType.Single)]
    string SunIntensityCmd(float? value = null)
    {
        if (Settings == null) return "no AtmosphereSettings bound";
        if (value == null) return $"sun intensity: {Settings.SunIntensity:F2}";
        Settings.SunIntensity = Mathf.Clamp(value.Value, 1f, 100f);
        _staticPropertiesDirty = true;
        return $"sun intensity: {Settings.SunIntensity:F2}";
    }

    [ConsoleCommand("rayleigh", "Get or set Rayleigh scattering vector (sky color).", MonoTargetType.Single)]
    string RayleighCmd(Vector3? value = null)
    {
        if (Settings == null) return "no AtmosphereSettings bound";
        if (value == null)
        {
            Vector3 r = Settings.RayleighScattering;
            return $"rayleigh scattering: ({r.x:E3}, {r.y:E3}, {r.z:E3})";
        }
        Settings.RayleighScattering = value.Value;
        _staticPropertiesDirty = true;
        return $"rayleigh scattering: ({value.Value.x:E3}, {value.Value.y:E3}, {value.Value.z:E3})";
    }

    [ConsoleCommand("mie", "Get or set Mie scattering coefficient (haze; range 0-0.1).", MonoTargetType.Single)]
    string MieCmd(float? value = null)
    {
        if (Settings == null) return "no AtmosphereSettings bound";
        if (value == null) return $"mie scattering: {Settings.MieScattering:E3}";
        Settings.MieScattering = Mathf.Clamp(value.Value, 0f, 0.1f);
        _staticPropertiesDirty = true;
        return $"mie scattering: {Settings.MieScattering:E3}";
    }

    [ConsoleCommand("scale", "Get or set atmosphere thickness scale (range 1.01-1.5).", MonoTargetType.Single)]
    string ScaleCmd(float? value = null)
    {
        if (Settings == null) return "no AtmosphereSettings bound";
        if (value == null) return $"atmosphere scale: {Settings.AtmosphereScale:F3}";
        Settings.AtmosphereScale = Mathf.Clamp(value.Value, 1.01f, 1.5f);
        _staticPropertiesDirty = true;
        return $"atmosphere scale: {Settings.AtmosphereScale:F3}";
    }
}
