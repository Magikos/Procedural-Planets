using UnityEngine;

[CommandPrefix("cloud")]
public class CloudController : MonoBehaviour, ICloudRuntime, IWorldServiceRegistrar,
    IWorldSettingsRegistrar
{
    static readonly System.Type[] RequiredSettings = { typeof(CloudDto) };

    [Header("References")]
    public ComputeShader NoiseCompute;
    public Texture2D BlueNoiseTexture;

    CloudDto _settings;
    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _shapeNoise;
    RenderTexture _detailNoise;
    IWeatherConfigurator _weather;

    bool _staticPropertiesDirty = true;
    float _aerialFade = CloudConstants.AerialFade;
    float _backlitStrength = CloudConstants.BacklitStrength;
    float _godRayStrength = CloudConstants.GodRayStreakStrength;
    float _godRayDawnBoost = CloudConstants.GodRayStreakDawnBoost;
    // Human "how far the glow reaches" (UV-ish units, aspect-corrected); converted to the
    // shader's exponential decay rate on upload. Bigger reach = decay rate gets smaller = glow
    // extends further from the sun before fading to ~5%.
    float _godRayReach = 3.0f / CloudConstants.GodRayStreakRadialFalloff;
    float _godRayThreshold = CloudConstants.GodRayStreakBrightThreshold;
    float _bottomFeather = CloudConstants.BottomFeather;
    float _rainShaftStrength = CloudConstants.RainShaftStrength;
    float _rainShaftLength = CloudConstants.RainShaftLength;
    Texture _lastWeatherTexture;
    Texture _lastDynamicsTexture;
    int _lastWeatherResolution = -1;
    int _lastUploadedViewSteps = int.MinValue;
    int _lastNoiseShapeResolution = -1;
    int _lastNoiseDetailResolution = -1;

    static readonly int _cloudPlanetCenterId = Shader.PropertyToID(ShaderGlobalIds.CloudPlanetCenter);
    static readonly int _cloudInnerRadiusId = Shader.PropertyToID(ShaderGlobalIds.CloudInnerRadius);
    static readonly int _cloudOuterRadiusId = Shader.PropertyToID(ShaderGlobalIds.CloudOuterRadius);
    static readonly int _cloudWeatherMapId = Shader.PropertyToID(ShaderGlobalIds.CloudWeatherMap);
    static readonly int _weatherDynamicsMapId = Shader.PropertyToID(ShaderGlobalIds.WeatherDynamicsMap);
    static readonly int _cloudWeatherResolutionId = Shader.PropertyToID(ShaderGlobalIds.CloudWeatherResolution);
    static readonly int _cloudNoiseScaleId = Shader.PropertyToID(ShaderGlobalIds.CloudNoiseScale);
    static readonly int _cloudDetailNoiseScaleId = Shader.PropertyToID(ShaderGlobalIds.CloudDetailNoiseScale);
    static readonly int _cloudDetailWeightId = Shader.PropertyToID(ShaderGlobalIds.CloudDetailWeight);
    static readonly int _cloudShapeWeightsId = Shader.PropertyToID(ShaderGlobalIds.CloudShapeWeights);
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID(ShaderGlobalIds.CloudDensityMultiplier);
    static readonly int _cloudDensityThresholdId = Shader.PropertyToID(ShaderGlobalIds.CloudDensityThreshold);
    static readonly int _cloudShapeSharpnessId = Shader.PropertyToID(ShaderGlobalIds.CloudShapeSharpness);
    static readonly int _cloudBottomFeatherId = Shader.PropertyToID(ShaderGlobalIds.CloudBottomFeather);
    static readonly int _cloudRainShaftParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudRainShaftParams);
    static readonly int _cloudTopFeatherId = Shader.PropertyToID(ShaderGlobalIds.CloudTopFeather);
    static readonly int _cloudTopDensityBiasId = Shader.PropertyToID(ShaderGlobalIds.CloudTopDensityBias);
    static readonly int _cloudLightAbsorptionId = Shader.PropertyToID(ShaderGlobalIds.CloudLightAbsorption);
    static readonly int _cloudDarknessThresholdId = Shader.PropertyToID(ShaderGlobalIds.CloudDarknessThreshold);
    static readonly int _cloudPhaseParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudPhaseParams);
    static readonly int _cloudColorId = Shader.PropertyToID(ShaderGlobalIds.CloudColor);
    static readonly int _cloudStormColorId = Shader.PropertyToID(ShaderGlobalIds.CloudStormColor);
    static readonly int _cloudAmbientStrengthId = Shader.PropertyToID(ShaderGlobalIds.CloudAmbientStrength);
    static readonly int _cloudStormDarkeningId = Shader.PropertyToID(ShaderGlobalIds.CloudStormDarkening);
    static readonly int _cloudPowderStrengthId = Shader.PropertyToID(ShaderGlobalIds.CloudPowderStrength);
    static readonly int _cloudMultiScatterParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudMultiScatterParams);
    static readonly int _cloudAmbientSkyId = Shader.PropertyToID(ShaderGlobalIds.CloudAmbientSky);
    static readonly int _cloudAmbientGroundId = Shader.PropertyToID(ShaderGlobalIds.CloudAmbientGround);
    static readonly int _cloudAerialDensityId = Shader.PropertyToID(ShaderGlobalIds.CloudAerialDensity);
    static readonly int _cloudBacklitParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudBacklitParams);
    static readonly int _godRayStreakParamsId = Shader.PropertyToID(ShaderGlobalIds.GodRayStreakParams);
    static readonly int _godRayStreakRadialFalloffId = Shader.PropertyToID(ShaderGlobalIds.GodRayStreakRadialFalloff);
    static readonly int _godRayStreakDawnBoostId = Shader.PropertyToID(ShaderGlobalIds.GodRayStreakDawnBoost);
    static readonly int _godRayStreakBrightThresholdId = Shader.PropertyToID(ShaderGlobalIds.GodRayStreakBrightThreshold);
    static readonly int _cloudSilverLiningParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudSilverLiningParams);
    static readonly int _cloudShadowParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudShadowParams);
    static readonly int _cloudViewStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudViewSteps);
    static readonly int _cloudLightStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudLightSteps);
    static readonly int _cloudRayOffsetStrengthId = Shader.PropertyToID(ShaderGlobalIds.CloudRayOffsetStrength);
    static readonly int _cloudDebugModeId = Shader.PropertyToID(ShaderGlobalIds.CloudDebugMode);
    static readonly int _cloudDebugParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudDebugParams);
    static readonly int _cloudShapeNoiseId = Shader.PropertyToID(ShaderGlobalIds.CloudShapeNoise);
    static readonly int _cloudDetailNoiseId = Shader.PropertyToID(ShaderGlobalIds.CloudDetailNoise);
    static readonly int _cloudBlueNoiseId = Shader.PropertyToID(ShaderGlobalIds.CloudBlueNoise);
    static readonly int _cloudBlueNoiseTexelSizeId = Shader.PropertyToID(ShaderGlobalIds.CloudBlueNoiseTexelSize);

    public System.Collections.Generic.IReadOnlyList<System.Type> RequiredSettingsTypes => RequiredSettings;

    public void RegisterWorldServices(IWorldContext context)
    {
        context.Register<ICloudRuntime>(this);
    }

    public void RegisterWorldSettings(ISettingsService settings)
    {
        CloudDto.EnsureRegistered(settings);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
    }

    void Start()
    {
        Initialize();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        if (!TryResolveSettings())
            return;

        _planetRadius = evt.PlanetRadius;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : evt.PlanetRadius;
        _planetCenter = evt.PlanetCenter;
        _staticPropertiesDirty = true;

        Initialize();
        GenerateNoiseTextures();
        EnsureStaticPropertiesUploaded();
        UpdatePerFrameProperties();
    }

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (evt.DtoType != typeof(CloudDto)) return;
        _settings = SettingsProvider.GetSettings<CloudDto>();
        _staticPropertiesDirty = true;
        if (_settings.ShapeNoiseResolution != _lastNoiseShapeResolution
            || _settings.DetailNoiseResolution != _lastNoiseDetailResolution)
        {
            GenerateNoiseTextures();
        }
    }

    void Update()
    {
        if (!TryResolveSettings())
            return;

        if (_planetRadius <= 0f)
        {
            if (_lastWeatherResolution != 0)
            {
                Shader.SetGlobalInt(_cloudWeatherResolutionId, 0);
                _lastWeatherResolution = 0;
            }
            return;
        }

        using (FrameTimingCounters.Measure(FrameTimingSection.Clouds))
        {
            EnsureStaticPropertiesUploaded();
            UpdatePerFrameProperties();
        }
    }

    void Initialize()
    {
        if (_weather == null)
            ServiceLocator.TryGet(out _weather);
    }

    void GenerateNoiseTextures()
    {
        if (!TryResolveSettings())
            return;

        if (NoiseCompute == null) return;

        int seed = ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("CloudNoise");

        ReleaseTextures();
        _shapeNoise = CloudNoiseGenerator.GenerateShapeNoise(NoiseCompute, _settings.ShapeNoiseResolution, seed);
        _detailNoise = CloudNoiseGenerator.GenerateDetailNoise(NoiseCompute, _settings.DetailNoiseResolution, seed);
        _lastNoiseShapeResolution = _settings.ShapeNoiseResolution;
        _lastNoiseDetailResolution = _settings.DetailNoiseResolution;
        _staticPropertiesDirty = true;
    }

    void EnsureStaticPropertiesUploaded()
    {
        if (!TryResolveSettings())
            return;

        if (!_staticPropertiesDirty) return;
        _staticPropertiesDirty = false;

        float innerRadius = _seaLevelRadius + _settings.BaseAltitude;
        float outerRadius = innerRadius + _settings.LayerThickness;

        Shader.SetGlobalVector(_cloudPlanetCenterId, _planetCenter);
        Shader.SetGlobalFloat(_cloudInnerRadiusId, innerRadius);
        Shader.SetGlobalFloat(_cloudOuterRadiusId, outerRadius);
        Shader.SetGlobalFloat(_cloudNoiseScaleId, CloudConstants.NoiseScale);
        Shader.SetGlobalFloat(_cloudDetailNoiseScaleId, CloudConstants.DetailNoiseScale);
        Shader.SetGlobalFloat(_cloudDetailWeightId, CloudConstants.DetailWeight);
        Shader.SetGlobalVector(_cloudShapeWeightsId, CloudConstants.ShapeNoiseWeights);
        Shader.SetGlobalFloat(_cloudDensityMultiplierId, _settings.DensityMultiplier);
        Shader.SetGlobalFloat(_cloudDensityThresholdId, CloudConstants.DensityThreshold);
        Shader.SetGlobalFloat(_cloudShapeSharpnessId, CloudConstants.ShapeSharpness);
        Shader.SetGlobalFloat(_cloudBottomFeatherId, _bottomFeather);
        Shader.SetGlobalFloat(_cloudTopFeatherId, CloudConstants.TopFeather);
        Shader.SetGlobalFloat(_cloudTopDensityBiasId, CloudConstants.TopDensityBias);
        Shader.SetGlobalFloat(_cloudLightAbsorptionId, CloudConstants.LightAbsorption);
        Shader.SetGlobalFloat(_cloudDarknessThresholdId, CloudConstants.DarknessThreshold);
        Shader.SetGlobalVector(_cloudPhaseParamsId, new Vector4(
            CloudConstants.ForwardScattering, CloudConstants.BackScattering, CloudConstants.BaseBrightness, CloudConstants.PhaseStrength));
        Shader.SetGlobalColor(_cloudColorId, _settings.CloudColor);
        Shader.SetGlobalColor(_cloudStormColorId, _settings.StormColor);
        Shader.SetGlobalFloat(_cloudAmbientStrengthId, CloudConstants.AmbientStrength);
        Shader.SetGlobalFloat(_cloudStormDarkeningId, CloudConstants.StormDarkening);
        Shader.SetGlobalFloat(_cloudPowderStrengthId, CloudConstants.PowderStrength);
        Shader.SetGlobalVector(_cloudMultiScatterParamsId, new Vector4(
            CloudConstants.MultiScatterAttenuation,
            CloudConstants.MultiScatterContribution,
            CloudConstants.MultiScatterPhaseScale,
            CloudConstants.MultiScatterStrength));
        Shader.SetGlobalColor(_cloudAmbientSkyId, CloudConstants.AmbientSky);
        Shader.SetGlobalColor(_cloudAmbientGroundId, CloudConstants.AmbientGround);
        float aerialFade = Mathf.Clamp(_aerialFade, 0f, 0.999f);
        float aerialDensity = aerialFade <= 0f
            ? 0f
            : -Mathf.Log(1f - aerialFade) / CloudConstants.AerialReferenceDistance;
        Shader.SetGlobalFloat(_cloudAerialDensityId, aerialDensity);
        Shader.SetGlobalVector(_cloudBacklitParamsId, new Vector4(
            _backlitStrength, CloudConstants.BacklitPower, 0f, 0f));
        Shader.SetGlobalVector(_godRayStreakParamsId, new Vector4(
            _godRayStrength,
            CloudConstants.GodRayStreakSampleCount,
            CloudConstants.GodRayStreakDecay,
            CloudConstants.GodRayStreakMarchLength));
        float godRayReach = Mathf.Max(_godRayReach, 0.05f);
        Shader.SetGlobalFloat(_godRayStreakRadialFalloffId, 3.0f / godRayReach);
        Shader.SetGlobalFloat(_godRayStreakDawnBoostId, _godRayDawnBoost);
        Shader.SetGlobalFloat(_godRayStreakBrightThresholdId, _godRayThreshold);
        Shader.SetGlobalVector(_cloudSilverLiningParamsId, new Vector4(
            CloudConstants.SilverLiningStrength,
            CloudConstants.SilverLiningPower,
            CloudConstants.SilverLiningEdgePower,
            CloudConstants.SilverLiningStormSuppression));
        Shader.SetGlobalVector(_cloudRainShaftParamsId, new Vector4(
            _rainShaftStrength, _rainShaftLength, 0f, 0f));
        Shader.SetGlobalVector(_cloudShadowParamsId, new Vector4(
            CloudConstants.ShadowStrength,
            CloudConstants.ShadowSoftness,
            CloudConstants.StormShadowBoost,
            CloudConstants.ShadowHorizonFade));
        Shader.SetGlobalInt(_cloudLightStepsId, _settings.LightSteps);
        Shader.SetGlobalFloat(_cloudRayOffsetStrengthId, _settings.RayOffsetStrength);
        Shader.SetGlobalInt(_cloudDebugModeId, (int)CloudDebugState.Mode);
        Shader.SetGlobalVector(_cloudDebugParamsId, new Vector4(
            CloudDebugState.CondensationChangeThreshold,
            Mathf.Max(CloudDebugState.CondensationChangeSaturation, CloudDebugState.CondensationChangeThreshold + 0.0001f),
            SphericalWeatherGrid.DeltaVisualizationScale,
            0f));

        if (_shapeNoise != null)
            Shader.SetGlobalTexture(_cloudShapeNoiseId, _shapeNoise);
        if (_detailNoise != null)
            Shader.SetGlobalTexture(_cloudDetailNoiseId, _detailNoise);
        if (BlueNoiseTexture != null)
        {
            Shader.SetGlobalTexture(_cloudBlueNoiseId, BlueNoiseTexture);
            Shader.SetGlobalVector(_cloudBlueNoiseTexelSizeId, new Vector4(
                1f / BlueNoiseTexture.width,
                1f / BlueNoiseTexture.height,
                BlueNoiseTexture.width,
                BlueNoiseTexture.height));
        }

        _lastWeatherTexture = null;
        _lastDynamicsTexture = null;
        _lastWeatherResolution = -1;
        _lastUploadedViewSteps = int.MinValue;
    }

    void UpdatePerFrameProperties()
    {
        int viewSteps = _settings.ViewSteps;
        Camera mainCam = Camera.main;
        if (mainCam != null && _seaLevelRadius > 0f)
        {
            float altitude = Vector3.Distance(mainCam.transform.position, _planetCenter) - _seaLevelRadius;
            float t = Mathf.InverseLerp(_settings.StepScaleNearAltitude,
                Mathf.Max(_settings.StepScaleFarAltitude, _settings.StepScaleNearAltitude + 1f), altitude);
            viewSteps = Mathf.RoundToInt(Mathf.Lerp(_settings.ViewSteps, _settings.MinViewSteps, t));
        }
        viewSteps = Mathf.Max(_settings.MinViewSteps,
            Mathf.RoundToInt(viewSteps * QualityController.CloudStepMultiplier));
        if (viewSteps != _lastUploadedViewSteps)
        {
            Shader.SetGlobalInt(_cloudViewStepsId, viewSteps);
            _lastUploadedViewSteps = viewSteps;
        }

        if (_weather != null && _weather.WeatherTexture != null)
        {
            var weatherTex = _weather.WeatherTexture;
            var dynamicsTex = _weather.WeatherDynamicsTexture;
            int weatherRes = _weather.WeatherResolution;

            if (_lastWeatherTexture != weatherTex)
            {
                Shader.SetGlobalTexture(_cloudWeatherMapId, weatherTex);
                _lastWeatherTexture = weatherTex;
            }
            if (dynamicsTex != null && _lastDynamicsTexture != dynamicsTex)
            {
                Shader.SetGlobalTexture(_weatherDynamicsMapId, dynamicsTex);
                _lastDynamicsTexture = dynamicsTex;
            }
            if (_lastWeatherResolution != weatherRes)
            {
                Shader.SetGlobalInt(_cloudWeatherResolutionId, weatherRes);
                _lastWeatherResolution = weatherRes;
            }
        }
        else if (_lastWeatherResolution != 0)
        {
            Shader.SetGlobalInt(_cloudWeatherResolutionId, 0);
            _lastWeatherResolution = 0;
        }
    }

    void ReleaseTextures()
    {
        if (_shapeNoise != null)
        {
            _shapeNoise.Release();
            _shapeNoise = null;
        }

        if (_detailNoise != null)
        {
            _detailNoise.Release();
            _detailNoise = null;
        }
    }

    bool TryResolveSettings()
    {
        return _settings != null || SettingsProvider.TryGetFrozen(out _settings);
    }

    void OnDestroy()
    {
        ReleaseTextures();
    }

    [ConsoleCommand("density", "Get or set cloud density, 0-1 (0=none, 1=thickest). Converts to the internal density multiplier.", MonoTargetType.Single)]
    string DensityCmd(float? value = null)
    {
        if (value == null) return $"cloud density: {_settings.DensityMultiplier / CloudSettings.DensityMax:F2} (0-1)";
        float human = Mathf.Clamp01(value.Value);
        SettingsProvider.Update(_settings with { DensityMultiplier = human * CloudSettings.DensityMax });
        return $"cloud density: {human:F2} (0-1)";
    }

    [ConsoleCommand("altitude", "Get or set cloud base altitude in meters (range 20-1000).", MonoTargetType.Single)]
    string AltitudeCmd(float? value = null)
    {
        if (value == null) return $"cloud base altitude: {_settings.BaseAltitude:F0}m";
        float clamped = Mathf.Clamp(value.Value, 20f, 1000f);
        SettingsProvider.Update(_settings with { BaseAltitude = clamped });
        return $"cloud base altitude: {clamped:F0}m";
    }

    [ConsoleCommand("thickness", "Get or set cloud layer thickness in meters (range 50-1000).", MonoTargetType.Single)]
    string ThicknessCmd(float? value = null)
    {
        if (value == null) return $"cloud layer thickness: {_settings.LayerThickness:F0}m";
        float clamped = Mathf.Clamp(value.Value, 50f, 1000f);
        SettingsProvider.Update(_settings with { LayerThickness = clamped });
        return $"cloud layer thickness: {clamped:F0}m";
    }

    [ConsoleCommand("debug-mode", "Get or set cloud debug visualization mode.", MonoTargetType.Single)]
    string DebugModeCmd(CloudDebugState.View? mode = null)
    {
        if (mode == null) return $"cloud debug mode: {CloudDebugState.Mode}";
        CloudDebugState.Mode = mode.Value;
        _staticPropertiesDirty = true;
        return $"cloud debug mode: {CloudDebugState.Mode}";
    }

    [ConsoleCommand("debug-threshold", "Get or set condensation-change debug threshold, 0-1. Converts to the internal condensation delta.", MonoTargetType.Single)]
    string DebugThresholdCmd(float? value = null)
    {
        if (value == null) return $"cloud debug threshold: {CloudDebugState.CondensationChangeThreshold / CloudDebugState.CondensationChangeThresholdMax:F2} (0-1)";
        float human = Mathf.Clamp01(value.Value);
        CloudDebugState.CondensationChangeThreshold = human * CloudDebugState.CondensationChangeThresholdMax;
        _staticPropertiesDirty = true;
        return $"cloud debug threshold: {human:F2} (0-1)";
    }

    [ConsoleCommand("debug-saturation", "Get or set condensation-change debug saturation, 0-1. Converts to the internal condensation delta.", MonoTargetType.Single)]
    string DebugSaturationCmd(float? value = null)
    {
        if (value == null) return $"cloud debug saturation: {CloudDebugState.CondensationChangeSaturation / CloudDebugState.CondensationChangeSaturationMax:F2} (0-1)";
        float human = Mathf.Clamp01(value.Value);
        CloudDebugState.CondensationChangeSaturation = human * CloudDebugState.CondensationChangeSaturationMax;
        _staticPropertiesDirty = true;
        return $"cloud debug saturation: {human:F2} (0-1)";
    }

    [ConsoleCommand("aerial-fade", "Get or set how much distant clouds haze into the sky, 0-1 (0=off, 0.5=half, 1=full at the reference distance).", MonoTargetType.Single)]
    string AerialFadeCmd(float? value = null)
    {
        if (value == null) return $"cloud aerial fade: {_aerialFade:P0} (0-1)";
        _aerialFade = Mathf.Clamp01(value.Value);
        _staticPropertiesDirty = true;
        return $"cloud aerial fade: {_aerialFade:P0} (0-1)";
    }

    [ConsoleCommand("backlit-glow", "Get or set backlit inner-glow strength (sun behind cloud), 0-2. 0=off, ~0.6 default.", MonoTargetType.Single)]
    string BacklitGlowCmd(float? value = null)
    {
        if (value == null) return $"cloud backlit glow: {_backlitStrength:F2} (0-2)";
        _backlitStrength = Mathf.Clamp(value.Value, 0f, 2f);
        _staticPropertiesDirty = true;
        return $"cloud backlit glow: {_backlitStrength:F2} (0-2)";
    }

    [ConsoleCommand("godray-strength", "Get or set god-ray streak strength (crepuscular rays through cloud gaps), 0-2. 0=off, 1=default.", MonoTargetType.Single)]
    string GodRayStrengthCmd(float? value = null)
    {
        if (value == null) return $"cloud god-ray streak strength: {_godRayStrength:F2} (0-2)";
        _godRayStrength = Mathf.Clamp(value.Value, 0f, 2f);
        _staticPropertiesDirty = true;
        return $"cloud god-ray streak strength: {_godRayStrength:F2} (0-2)";
    }

    [ConsoleCommand("godray-dawn-boost", "Get or set extra god-ray strength multiplier near the horizon (dawn/dusk), 0-5. 1=no boost, 2=default (double near horizon).", MonoTargetType.Single)]
    string GodRayDawnBoostCmd(float? value = null)
    {
        if (value == null) return $"cloud god-ray dawn boost: {_godRayDawnBoost:F2} (0-5)";
        _godRayDawnBoost = Mathf.Clamp(value.Value, 0f, 5f);
        _staticPropertiesDirty = true;
        return $"cloud god-ray dawn boost: {_godRayDawnBoost:F2} (0-5)";
    }

    [ConsoleCommand("godray-reach", "Get or set how far the god-ray glow reaches from the sun before fading to ~5%, 0.1-3. Bigger = larger glow. ~1.5 default.", MonoTargetType.Single)]
    string GodRayReachCmd(float? value = null)
    {
        if (value == null) return $"cloud god-ray reach: {_godRayReach:F2}";
        _godRayReach = Mathf.Clamp(value.Value, 0.1f, 3f);
        _staticPropertiesDirty = true;
        return $"cloud god-ray reach: {_godRayReach:F2}";
    }

    [ConsoleCommand("godray-threshold", "Get or set scene brightness above which a pixel becomes a god-ray source, 0-1. Lower = more of the bright sky beams (softer/broader); higher = only the very brightest sun/cloud-rim pixels beam (crisper). Default 0.55.", MonoTargetType.Single)]
    string GodRayThresholdCmd(float? value = null)
    {
        if (value == null) return $"cloud god-ray threshold: {_godRayThreshold:F2} (0-1)";
        _godRayThreshold = Mathf.Clamp01(value.Value);
        _staticPropertiesDirty = true;
        return $"cloud god-ray threshold: {_godRayThreshold:F2} (0-1)";
    }

    [ConsoleCommand("bottom-feather", "Get or set cloud base softness, fraction of layer thickness (range 0.01-0.5). Higher = more gradual transition flying in/out of the cloud base. Default 0.06.", MonoTargetType.Single)]
    string BottomFeatherCmd(float? value = null)
    {
        if (value == null) return $"cloud bottom feather: {_bottomFeather:F3}";
        _bottomFeather = Mathf.Clamp(value.Value, 0.01f, 0.5f);
        _staticPropertiesDirty = true;
        return $"cloud bottom feather: {_bottomFeather:F3}";
    }

    [ConsoleCommand("rain-shaft", "Get or set the virga / rain-shaft veil hung under raining cloud cells, 0-2. 0=off, ~0.5-1 typical. Makes storm cells read as storms (curtain of rain to the horizon).", MonoTargetType.Single)]
    string RainShaftCmd(float? value = null)
    {
        if (value == null) return $"cloud rain shaft: {_rainShaftStrength:F2} (0-2)";
        _rainShaftStrength = Mathf.Clamp(value.Value, 0f, 2f);
        _staticPropertiesDirty = true;
        return $"cloud rain shaft: {_rainShaftStrength:F2} (0-2)";
    }

    [ConsoleCommand("rain-shaft-length", "Get or set how far (metres) the rain shaft reaches below the cloud base before fading, 50-1000. Default 300. Larger = rain reaches the ground.", MonoTargetType.Single)]
    string RainShaftLengthCmd(float? value = null)
    {
        if (value == null) return $"cloud rain shaft length: {_rainShaftLength:F0} m";
        _rainShaftLength = Mathf.Clamp(value.Value, 50f, 1000f);
        _staticPropertiesDirty = true;
        return $"cloud rain shaft length: {_rainShaftLength:F0} m";
    }
}
