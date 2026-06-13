using UnityEngine;

[CommandPrefix("cloud")]
public class CloudController : MonoBehaviour
{
    [Header("References")]
    public ComputeShader NoiseCompute;

    CloudDto _settings;
    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _shapeNoise;
    RenderTexture _detailNoise;
    IWeatherConfigurator _weather;

    bool _staticPropertiesDirty = true;
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
    static readonly int _cloudTopFeatherId = Shader.PropertyToID(ShaderGlobalIds.CloudTopFeather);
    static readonly int _cloudTopDensityBiasId = Shader.PropertyToID(ShaderGlobalIds.CloudTopDensityBias);
    static readonly int _cloudLightAbsorptionId = Shader.PropertyToID(ShaderGlobalIds.CloudLightAbsorption);
    static readonly int _cloudDarknessThresholdId = Shader.PropertyToID(ShaderGlobalIds.CloudDarknessThreshold);
    static readonly int _cloudPhaseParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudPhaseParams);
    static readonly int _cloudColorId = Shader.PropertyToID(ShaderGlobalIds.CloudColor);
    static readonly int _cloudStormColorId = Shader.PropertyToID(ShaderGlobalIds.CloudStormColor);
    static readonly int _cloudAmbientStrengthId = Shader.PropertyToID(ShaderGlobalIds.CloudAmbientStrength);
    static readonly int _cloudStormDarkeningId = Shader.PropertyToID(ShaderGlobalIds.CloudStormDarkening);
    static readonly int _cloudSilverLiningParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudSilverLiningParams);
    static readonly int _cloudShadowParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudShadowParams);
    static readonly int _cloudViewStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudViewSteps);
    static readonly int _cloudLightStepsId = Shader.PropertyToID(ShaderGlobalIds.CloudLightSteps);
    static readonly int _cloudRayOffsetStrengthId = Shader.PropertyToID(ShaderGlobalIds.CloudRayOffsetStrength);
    static readonly int _cloudDebugModeId = Shader.PropertyToID(ShaderGlobalIds.CloudDebugMode);
    static readonly int _cloudDebugParamsId = Shader.PropertyToID(ShaderGlobalIds.CloudDebugParams);
    static readonly int _cloudShapeNoiseId = Shader.PropertyToID(ShaderGlobalIds.CloudShapeNoise);
    static readonly int _cloudDetailNoiseId = Shader.PropertyToID(ShaderGlobalIds.CloudDetailNoise);

    void OnEnable()
    {
        CloudDto.EnsureRegistered();
        _settings = SettingsProvider.GetSettings<CloudDto>();
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
        if (_planetRadius <= 0f)
        {
            if (_lastWeatherResolution != 0)
            {
                Shader.SetGlobalInt(_cloudWeatherResolutionId, 0);
                _lastWeatherResolution = 0;
            }
            return;
        }

        EnsureStaticPropertiesUploaded();
        UpdatePerFrameProperties();
    }

    void Initialize()
    {
        if (_weather == null)
            _weather = ServiceLocator.Get<IWeatherConfigurator>();
    }

    void GenerateNoiseTextures()
    {
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
        Shader.SetGlobalFloat(_cloudBottomFeatherId, CloudConstants.BottomFeather);
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
        Shader.SetGlobalVector(_cloudSilverLiningParamsId, new Vector4(
            CloudConstants.SilverLiningStrength,
            CloudConstants.SilverLiningPower,
            CloudConstants.SilverLiningEdgePower,
            CloudConstants.SilverLiningStormSuppression));
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

    void OnDestroy()
    {
        ReleaseTextures();
    }

    [ConsoleCommand("density", "Get or set cloud density multiplier (range 0-0.08).", MonoTargetType.Single)]
    string DensityCmd(float? value = null)
    {
        if (value == null) return $"cloud density: {_settings.DensityMultiplier:F4}";
        float clamped = Mathf.Clamp(value.Value, 0f, 0.08f);
        SettingsProvider.Update(_settings with { DensityMultiplier = clamped });
        return $"cloud density: {clamped:F4}";
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

    [ConsoleCommand("debug-threshold", "Get or set condensation-change debug threshold (range 0-0.01).", MonoTargetType.Single)]
    string DebugThresholdCmd(float? value = null)
    {
        if (value == null) return $"cloud debug threshold: {CloudDebugState.CondensationChangeThreshold:F4}";
        CloudDebugState.CondensationChangeThreshold = Mathf.Clamp(value.Value, 0f, 0.01f);
        _staticPropertiesDirty = true;
        return $"cloud debug threshold: {CloudDebugState.CondensationChangeThreshold:F4}";
    }

    [ConsoleCommand("debug-saturation", "Get or set condensation-change debug saturation (range 0.0005-0.02).", MonoTargetType.Single)]
    string DebugSaturationCmd(float? value = null)
    {
        if (value == null) return $"cloud debug saturation: {CloudDebugState.CondensationChangeSaturation:F4}";
        CloudDebugState.CondensationChangeSaturation = Mathf.Clamp(value.Value, 0.0005f, 0.02f);
        _staticPropertiesDirty = true;
        return $"cloud debug saturation: {CloudDebugState.CondensationChangeSaturation:F4}";
    }
}
