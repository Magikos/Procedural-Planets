using UnityEngine;

[CommandPrefix("atmosphere")]
public class AtmosphereController : MonoBehaviour, IAtmosphereRuntime, IWorldServiceRegistrar,
    IWorldSettingsRegistrar
{
    static readonly System.Type[] RequiredSettings = { typeof(AtmosphereDto) };

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

    static readonly int _sunParamsId = Shader.PropertyToID(ShaderGlobalIds.SunParams);
    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _seaLevelRadiusId = Shader.PropertyToID(ShaderGlobalIds.SeaLevelRadius);
    static readonly int _densityOriginRadiusId = Shader.PropertyToID(ShaderGlobalIds.DensityOriginRadius);
    static readonly int _atmosphereRadiusId = Shader.PropertyToID(ShaderGlobalIds.AtmosphereRadius);
    static readonly int _viewStepsId = Shader.PropertyToID(ShaderGlobalIds.ViewSteps);
    static readonly int _sunStepsId = Shader.PropertyToID(ShaderGlobalIds.SunSteps);
    static readonly int _rayleighScatteringId = Shader.PropertyToID(ShaderGlobalIds.RayleighScattering);
    static readonly int _rayleighScaleHeightId = Shader.PropertyToID(ShaderGlobalIds.RayleighScaleHeight);
    static readonly int _mieScatteringId = Shader.PropertyToID(ShaderGlobalIds.MieScatteringCoeff);
    static readonly int _mieScaleHeightId = Shader.PropertyToID(ShaderGlobalIds.MieScaleHeight);
    static readonly int _mieAnisotropyId = Shader.PropertyToID(ShaderGlobalIds.MieAnisotropy);
    static readonly int _terrainAerialPerspectiveDistancesId =
        Shader.PropertyToID(ShaderGlobalIds.TerrainAerialPerspectiveDistances);
    static readonly int _sunIntensityId = Shader.PropertyToID(ShaderGlobalIds.SunIntensity);
    static readonly int _sunDiscSizeId = Shader.PropertyToID(ShaderGlobalIds.SunDiscSize);
    static readonly int _sunDiscBlendId = Shader.PropertyToID(ShaderGlobalIds.SunDiscBlend);
    static readonly int _sunDiscIntensityId = Shader.PropertyToID(ShaderGlobalIds.SunDiscIntensity);
    static readonly int _sunAureoleParamsId = Shader.PropertyToID(ShaderGlobalIds.SunAureoleParams);
    static readonly int _lightShaftParamsId = Shader.PropertyToID(ShaderGlobalIds.LightShaftParams);
    static readonly int _lightShaftParams2Id = Shader.PropertyToID(ShaderGlobalIds.LightShaftParams2);
    static readonly int _lightShaftSamplesId = Shader.PropertyToID(ShaderGlobalIds.LightShaftSamples);
    static readonly int _debugModeId = Shader.PropertyToID(ShaderGlobalIds.AtmosphereDebugMode);
    static readonly int _bakedOpticalDepthId = Shader.PropertyToID(ShaderGlobalIds.BakedOpticalDepth);

    public System.Collections.Generic.IReadOnlyList<System.Type> RequiredSettingsTypes => RequiredSettings;

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<IAtmosphereRuntime>(this);
    }

    public void RegisterWorldSettings(ISettingsService settings)
    {
        EnsureSettingsRegistered(settings);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
    }

    static void EnsureSettingsRegistered(ISettingsService settings)
    {
        if (settings.IsRegistered<AtmosphereDto>()) return;
        var so = Resources.Load<AtmosphereSettings>("Settings/AtmosphereSettings");
        if (so == null)
            throw new System.InvalidOperationException(
                "AtmosphereDto requires Resources/Settings/AtmosphereSettings.asset.");
        settings.Register(AtmosphereDto.From(so));
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
        if (!TryResolveSettings()) return;

        EnsureStaticPropertiesUploaded();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        if (!TryResolveSettings())
            return;

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
        if (!TryResolveSettings())
            return;

        BakeOpticalDepth();
        EnsureStaticPropertiesUploaded();
    }

    void EnsureStaticPropertiesUploaded()
    {
        if (!TryResolveSettings())
            return;

        if (!_staticPropertiesDirty) return;
        _staticPropertiesDirty = false;

        float atmosphereRadius = _planetRadius * _settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;

        if (LutNeedsRebake()) BakeOpticalDepth();

        Vector3 center = _planetCenter;

        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalFloat(_seaLevelRadiusId, _seaLevelRadius);
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
        Shader.SetGlobalFloat(_sunDiscIntensityId, _settings.SunDiscIntensity);
        Shader.SetGlobalVector(_sunAureoleParamsId, new Vector4(
            _settings.SunAureoleStrength, _settings.SunAureolePower, 0f, 0f));
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
        if (!TryResolveSettings())
            return;

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

    bool TryResolveSettings()
    {
        return _settings != null || SettingsProvider.TryGetFrozen(out _settings);
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

    // Human 1.0 maps to this Mie coefficient; the 0.002 default therefore reads as 0.5.
    const float MieHumanScaleMax = 0.004f;

    [ConsoleCommand("mie", "Get or set sun-glow / haze strength, 0-1 (0=clear, 0.5=default, 1=heavy). Converts to the Mie scattering coefficient internally.", MonoTargetType.Single)]
    string MieCmd(float? value = null)
    {
        if (value == null) return $"mie (glow/haze): {_settings.MieScattering / MieHumanScaleMax:F2} (0-1)";
        float human = Mathf.Clamp01(value.Value);
        SettingsProvider.Update(_settings with { MieScattering = human * MieHumanScaleMax });
        return $"mie (glow/haze): {human:F2} (0-1)";
    }

    [ConsoleCommand("glow-tightness","Get or set sun glow tightness (Mie anisotropy, 0-0.99). Higher = smaller, more defined glow core; lower = broad washed-out halo. Default 0.76. Pair with 'mie' (glow strength).", MonoTargetType.Single)]
    string GlowTightnessCmd(float? value = null)
    {
        if (value == null) return $"glow tightness: {_settings.MieAnisotropy:F3}";
        float clamped = Mathf.Clamp(value.Value, 0f, 0.99f);
        SettingsProvider.Update(_settings with { MieAnisotropy = clamped });
        return $"glow tightness: {clamped:F3}";
    }

    [ConsoleCommand("scale", "Get or set atmosphere thickness scale (range 1.01-1.5).", MonoTargetType.Single)]
    string ScaleCmd(float? value = null)
    {
        if (value == null) return $"atmosphere scale: {_settings.AtmosphereScale:F3}";
        float clamped = Mathf.Clamp(value.Value, 1.01f, 1.5f);
        SettingsProvider.Update(_settings with { AtmosphereScale = clamped });
        return $"atmosphere scale: {clamped:F3}";
    }

    [ConsoleCommand("shaft-strength", "Get or set god-ray (light shaft) strength. 0=off, ~1 default, up to 8. Enables shafts when >0.", MonoTargetType.Single)]
    string ShaftStrengthCmd(float? value = null)
    {
        if (value == null) return $"light shaft strength: {_settings.LightShaftStrength:F2} (enabled: {_settings.EnableLightShafts})";
        float clamped = Mathf.Clamp(value.Value, 0f, 8f);
        SettingsProvider.Update(_settings with { LightShaftStrength = clamped, EnableLightShafts = clamped > 0f });
        return $"light shaft strength: {clamped:F2} (enabled: {clamped > 0f})";
    }

    [ConsoleCommand("sun-disc-blend", "Get or set sun disc edge softness, 0-1 (0=hard circle, 1=soft glow). Higher = thin cloud in front reads as a diffuse glow instead of a hard circle. Converts to the internal edge-blend width.", MonoTargetType.Single)]
    string SunDiscBlendCmd(float? value = null)
    {
        if (value == null) return $"sun disc blend: {Mathf.InverseLerp(AtmosphereSettings.SunDiscBlendMin, AtmosphereSettings.SunDiscBlendMax, _settings.SunDiscBlend):F2} (0-1)";
        float human = Mathf.Clamp01(value.Value);
        SettingsProvider.Update(_settings with { SunDiscBlend = Mathf.Lerp(AtmosphereSettings.SunDiscBlendMin, AtmosphereSettings.SunDiscBlendMax, human) });
        return $"sun disc blend: {human:F2} (0-1)";
    }

    [ConsoleCommand("sun-disc-intensity", "Get or set the hard sun disc brightness, 0-2. 0 = no disc (sun is only the atmosphere glow + god rays; vanishes behind cloud instead of bleeding through as a hard circle). 1 = full. ~0.3-0.5 = dim soft disc that centres the clear-sky sun without the bleed-through. Default 1.", MonoTargetType.Single)]
    string SunDiscIntensityCmd(float? value = null)
    {
        if (value == null) return $"sun disc intensity: {_settings.SunDiscIntensity:F2}";
        float clamped = Mathf.Clamp(value.Value, 0f, 2f);
        SettingsProvider.Update(_settings with { SunDiscIntensity = clamped });
        return $"sun disc intensity: {clamped:F2}";
    }

    [ConsoleCommand("sun-aureole", "Get or set the low-sun bloom around the sun disc (0-4). Fills the soft 'fireball' halo at dawn/dusk where the Mie glow collapses and leaves a bare circle; auto-fades by midday. 0 = off. Default 0.", MonoTargetType.Single)]
    string SunAureoleCmd(float? value = null)
    {
        if (value == null) return $"sun aureole: {_settings.SunAureoleStrength:F2}";
        float clamped = Mathf.Clamp(value.Value, 0f, 4f);
        SettingsProvider.Update(_settings with { SunAureoleStrength = clamped });
        return $"sun aureole: {clamped:F2}";
    }

    [ConsoleCommand("sun-aureole-size", "Get or set the sun aureole bloom radius (1-200). Lower = larger, softer bloom; higher = tighter. Default 40 (~15 deg).", MonoTargetType.Single)]
    string SunAureoleSizeCmd(float? value = null)
    {
        if (value == null) return $"sun aureole size (power): {_settings.SunAureolePower:F0}";
        float clamped = Mathf.Clamp(value.Value, 1f, 200f);
        SettingsProvider.Update(_settings with { SunAureolePower = clamped });
        return $"sun aureole size (power): {clamped:F0}";
    }
}
