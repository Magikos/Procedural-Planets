using UnityEngine;

public class AtmosphereController : MonoBehaviour, IAtmosphereRuntime, IWorldServiceRegistrar,
    IWorldSettingsRegistrar
{
    static readonly System.Type[] RequiredSettings = { typeof(AtmosphereDto) };

    [Header("References")]
    public ComputeShader OpticalDepthCompute;

    AtmosphereDto _settings;
    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _bakedOpticalDepth;
    float _lastBakedScaleR, _lastBakedScaleM, _lastBakedAtmoScale;
    int _lastBakedSize, _lastBakedSteps;
    IPlanet _planet;
    AtmosphereCommands _commands;
    bool _staticPropertiesDirty = true;

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
    public AtmosphereDto SettingsSnapshot => _settings;
    public bool IsReady => isActiveAndEnabled && _planetRadius > 0f && _settings != null
        && _bakedOpticalDepth != null && _bakedOpticalDepth.IsCreated();

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
        _commands = new AtmosphereCommands(this);
        _staticPropertiesDirty = true;
        if (_planetRadius > 0f)
        {
            if (SettingsProvider.TryGetFrozen<AtmosphereDto>(out var settings)) _settings = settings;
            EnsureStaticPropertiesUploaded();
        }
    }

    static void EnsureSettingsRegistered(ISettingsService settings)
    {
        if (settings.IsRegistered<AtmosphereDto>()) return;
        var so = Resources.Load<AtmosphereSettings>("Settings/AtmosphereSettings");
        if (so == null)
            throw new System.InvalidOperationException(
                "AtmosphereDto requires Resources/Settings/AtmosphereSettings.asset.");
        var snapshot = AtmosphereDto.From(so);
        if (!snapshot.TryValidate(out string error)) throw new System.InvalidOperationException(error);
        settings.Register(snapshot);
    }

    void Start()
    {
        InitializeDependencies();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
        _commands?.Dispose();
        _commands = null;
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }

    void OnDestroy() => ReleaseOpticalDepth();

    void ReleaseOpticalDepth()
    {
        if (_bakedOpticalDepth == null) return;
        _bakedOpticalDepth.Release();
        UnityEngine.Rendering.CoreUtils.Destroy(_bakedOpticalDepth);
        _bakedOpticalDepth = null;
    }

    void Update()
    {
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
        var next = SettingsProvider.GetSettings<AtmosphereDto>();
        if (!next.TryValidate(out string error)) throw new System.InvalidOperationException(error);
        _settings = next;
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
        if (!_settings.TryValidate(out string error)) throw new System.InvalidOperationException(error);
        _staticPropertiesDirty = false;

        float atmosphereRadius = _planetRadius * _settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;

        if (LutNeedsRebake()) BakeOpticalDepth();
        Shader.SetGlobalTexture(_bakedOpticalDepthId, _bakedOpticalDepth);

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
        return _bakedOpticalDepth == null || !_bakedOpticalDepth.IsCreated()
            || _settings.RayleighScaleHeight != _lastBakedScaleR
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
        if (!_settings.TryValidate(out string error)) throw new System.InvalidOperationException(error);

        float atmosphereRadius = _planetRadius * _settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;
        int size = _settings.BakeTextureSize;

        if (_bakedOpticalDepth != null && _bakedOpticalDepth.width != size)
            ReleaseOpticalDepth();

        if (_bakedOpticalDepth == null)
        {
            _bakedOpticalDepth = new RenderTexture(size, size, 0, RenderTextureFormat.RGHalf)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "BakedOpticalDepth"
            };
        }
        if (!_bakedOpticalDepth.IsCreated()) _bakedOpticalDepth.Create();

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

    public bool TryApplySettings(AtmosphereDto next, out string error)
    {
        error = "Atmosphere settings are not initialized.";
        if (_settings == null) return false;
        error = "Atmosphere settings cannot be null.";
        if (next == null || !next.TryValidate(out error)) return false;
        SettingsProvider.Update(next);
        _settings = next;
        _staticPropertiesDirty = true;
        return true;
    }

}
