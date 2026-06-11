using UnityEngine;

[CommandPrefix("atmosphere")]
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public CelestialManager CelestialManager;
    public ComputeShader OpticalDepthCompute;

    AtmosphereDto _settings;
    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _bakedOpticalDepth;
    float _lastBakedScaleR, _lastBakedScaleM, _lastBakedAtmoScale;
    int _lastBakedSize, _lastBakedSteps;
    IPlanet _planet;
    bool _staticPropertiesDirty = true;

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
    static readonly int _terrainAerialPerspectiveDistancesId =
        Shader.PropertyToID("_TerrainAerialPerspectiveDistances");
    static readonly int _sunIntensityId = Shader.PropertyToID("_SunIntensity");
    static readonly int _sunDiscSizeId = Shader.PropertyToID("_SunDiscSize");
    static readonly int _sunDiscBlendId = Shader.PropertyToID("_SunDiscBlend");
    static readonly int _lightShaftParamsId = Shader.PropertyToID("_LightShaftParams");
    static readonly int _lightShaftParams2Id = Shader.PropertyToID("_LightShaftParams2");
    static readonly int _lightShaftSamplesId = Shader.PropertyToID("_LightShaftSamples");
    static readonly int _debugModeId = Shader.PropertyToID("_DebugMode");
    static readonly int _bakedOpticalDepthId = Shader.PropertyToID("_BakedOpticalDepth");

    void OnEnable()
    {
        _settings = SettingsProvider.GetSettings<AtmosphereDto>();
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
    }

    void Start()
    {
        InitializeDependencies();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }

    void OnDestroy() => _bakedOpticalDepth?.Release();

    void Update()
    {
        if (CelestialManager != null)
            Shader.SetGlobalVector(_sunParamsId, CelestialManager.SunDirection);

        if (_planetRadius <= 0f) return;

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

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (evt.DtoType != typeof(AtmosphereDto)) return;
        _settings = SettingsProvider.GetSettings<AtmosphereDto>();
        _staticPropertiesDirty = true;
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

    void EnsureStaticPropertiesUploaded()
    {
        if (!_staticPropertiesDirty) return;
        _staticPropertiesDirty = false;

        float atmosphereRadius = _planetRadius * _settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;

        if (LutNeedsRebake()) BakeOpticalDepth();

        Vector3 center = _planetCenter;

        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalFloat(_planetRadiusId, _seaLevelRadius);
        Shader.SetGlobalFloat(_densityOriginRadiusId, _seaLevelRadius);
        Shader.SetGlobalFloat(_atmosphereRadiusId, atmosphereRadius);

        Shader.SetGlobalInt(_viewStepsId, _settings.ViewSteps);
        Shader.SetGlobalInt(_sunStepsId, _settings.SunSteps);

        Shader.SetGlobalVector(_rayleighScatteringId, _settings.RayleighScattering);
        Shader.SetGlobalFloat(_rayleighScaleHeightId, _settings.RayleighScaleHeight * atmosphereThickness);
        Shader.SetGlobalFloat(_mieScatteringId, _settings.MieScattering);
        Shader.SetGlobalFloat(_mieScaleHeightId, _settings.MieScaleHeight * atmosphereThickness);
        Shader.SetGlobalFloat(_mieAnisotropyId, _settings.MieAnisotropy);
        float terrainClarityDistance = Mathf.Max(0f, _settings.TerrainClarityDistance);
        float terrainAtmosphereDistance = Mathf.Max(
            terrainClarityDistance + 1f, _settings.TerrainAtmosphereDistance);
        Shader.SetGlobalVector(_terrainAerialPerspectiveDistancesId,
            new Vector4(terrainClarityDistance, terrainAtmosphereDistance, 0f, 0f));

        Shader.SetGlobalFloat(_sunIntensityId, _settings.SunIntensity);
        Shader.SetGlobalFloat(_sunDiscSizeId, _settings.SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, _settings.SunDiscBlend);
        Shader.SetGlobalVector(_lightShaftParamsId, new Vector4(
            _settings.EnableLightShafts ? _settings.LightShaftStrength : 0f,
            _settings.LightShaftDensity,
            _settings.LightShaftDecay,
            _settings.LightShaftWeight));
        Shader.SetGlobalVector(_lightShaftParams2Id, new Vector4(
            _settings.LightShaftExposure,
            _settings.LightShaftThreshold,
            0.25f,
            1.35f));
        Shader.SetGlobalInt(_lightShaftSamplesId, _settings.EnableLightShafts ? _settings.LightShaftSamples : 0);
        Shader.SetGlobalInt(_debugModeId, _settings.DebugMode);
    }

    bool LutNeedsRebake()
    {
        return _settings.RayleighScaleHeight != _lastBakedScaleR
            || _settings.MieScaleHeight != _lastBakedScaleM
            || _settings.AtmosphereScale != _lastBakedAtmoScale
            || _settings.BakeTextureSize != _lastBakedSize
            || _settings.BakeSteps != _lastBakedSteps;
    }

    void BakeOpticalDepth()
    {
        if (OpticalDepthCompute == null || _seaLevelRadius <= 0f) return;

        float atmosphereRadius = _planetRadius * _settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;
        int size = _settings.BakeTextureSize;

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
        OpticalDepthCompute.SetInt("_NumSteps", _settings.BakeSteps);
        OpticalDepthCompute.SetFloat("_SeaLevelRadius", _seaLevelRadius);
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", atmosphereRadius);
        OpticalDepthCompute.SetFloat("_RayleighScaleHeight", _settings.RayleighScaleHeight * atmosphereThickness);
        OpticalDepthCompute.SetFloat("_MieScaleHeight", _settings.MieScaleHeight * atmosphereThickness);

        int groups = Mathf.CeilToInt(size / 8f);
        OpticalDepthCompute.Dispatch(kernel, groups, groups, 1);

        Shader.SetGlobalTexture(_bakedOpticalDepthId, _bakedOpticalDepth);

        _lastBakedScaleR = _settings.RayleighScaleHeight;
        _lastBakedScaleM = _settings.MieScaleHeight;
        _lastBakedAtmoScale = _settings.AtmosphereScale;
        _lastBakedSize = _settings.BakeTextureSize;
        _lastBakedSteps = _settings.BakeSteps;
    }

    [ConsoleCommand("sun-intensity", "Get or set scattering sun intensity (range 1-100).", MonoTargetType.Single)]
    string SunIntensityCmd(float? value = null)
    {
        if (value == null) return $"sun intensity: {_settings.SunIntensity:F2}";
        float clamped = Mathf.Clamp(value.Value, 1f, 100f);
        SettingsProvider.Update(_settings with { SunIntensity = clamped });
        return $"sun intensity: {clamped:F2}";
    }

    [ConsoleCommand("rayleigh", "Get or set Rayleigh scattering vector (sky color).", MonoTargetType.Single)]
    string RayleighCmd(Vector3? value = null)
    {
        if (value == null)
        {
            Vector3 r = _settings.RayleighScattering;
            return $"rayleigh scattering: ({r.x:E3}, {r.y:E3}, {r.z:E3})";
        }
        SettingsProvider.Update(_settings with { RayleighScattering = value.Value });
        return $"rayleigh scattering: ({value.Value.x:E3}, {value.Value.y:E3}, {value.Value.z:E3})";
    }

    [ConsoleCommand("mie", "Get or set Mie scattering coefficient (haze; range 0-0.1).", MonoTargetType.Single)]
    string MieCmd(float? value = null)
    {
        if (value == null) return $"mie scattering: {_settings.MieScattering:E3}";
        float clamped = Mathf.Clamp(value.Value, 0f, 0.1f);
        SettingsProvider.Update(_settings with { MieScattering = clamped });
        return $"mie scattering: {clamped:E3}";
    }

    [ConsoleCommand("scale", "Get or set atmosphere thickness scale (range 1.01-1.5).", MonoTargetType.Single)]
    string ScaleCmd(float? value = null)
    {
        if (value == null) return $"atmosphere scale: {_settings.AtmosphereScale:F3}";
        float clamped = Mathf.Clamp(value.Value, 1.01f, 1.5f);
        SettingsProvider.Update(_settings with { AtmosphereScale = clamped });
        return $"atmosphere scale: {clamped:F3}";
    }
}
