using UnityEngine;

/// <summary>
/// Uploads planet-scale cloud data and render settings to the cloud shader.
/// Weather state lives in WeatherManager; this component owns render-only noise textures.
/// </summary>
[CommandPrefix("cloud")]
public class CloudController : MonoBehaviour, ICloudController
{
    [Header("References")]
    public CloudSettings Settings;
    public ComputeShader NoiseCompute;

    CloudSettings ICloudController.Settings => Settings;

    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _shapeNoise;
    RenderTexture _detailNoise;
    IWeatherConfigurator _weather;

    // Per-frame upload elides the ~30 static SetGlobal* calls unless something invalidated them.
    // Settings is treated as immutable at runtime (inspector edits are not supported); the dirty
    // flag is raised by OnPlanetGenerated when planet position / radius change, and by a Settings
    // reference swap (e.g. asset reload).
    bool _staticPropertiesDirty = true;
    CloudSettings _lastStaticSettings;
    Texture _lastWeatherTexture;
    Texture _lastDynamicsTexture;
    int _lastWeatherResolution = -1;
    int _lastUploadedViewSteps = int.MinValue;

    static readonly int _cloudPlanetCenterId = Shader.PropertyToID("_CloudPlanetCenter");
    static readonly int _cloudInnerRadiusId = Shader.PropertyToID("_CloudInnerRadius");
    static readonly int _cloudOuterRadiusId = Shader.PropertyToID("_CloudOuterRadius");
    static readonly int _cloudWeatherMapId = Shader.PropertyToID("_CloudWeatherMap");
    static readonly int _weatherDynamicsMapId = Shader.PropertyToID("_WeatherDynamicsMap");
    static readonly int _cloudWeatherResolutionId = Shader.PropertyToID("_CloudWeatherResolution");
    static readonly int _cloudNoiseScaleId = Shader.PropertyToID("_CloudNoiseScale");
    static readonly int _cloudDetailNoiseScaleId = Shader.PropertyToID("_CloudDetailNoiseScale");
    static readonly int _cloudDetailWeightId = Shader.PropertyToID("_CloudDetailWeight");
    static readonly int _cloudShapeWeightsId = Shader.PropertyToID("_CloudShapeWeights");
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID("_CloudDensityMultiplier");
    static readonly int _cloudDensityThresholdId = Shader.PropertyToID("_CloudDensityThreshold");
    static readonly int _cloudShapeSharpnessId = Shader.PropertyToID("_CloudShapeSharpness");
    static readonly int _cloudBottomFeatherId = Shader.PropertyToID("_CloudBottomFeather");
    static readonly int _cloudTopFeatherId = Shader.PropertyToID("_CloudTopFeather");
    static readonly int _cloudTopDensityBiasId = Shader.PropertyToID("_CloudTopDensityBias");
    static readonly int _cloudLightAbsorptionId = Shader.PropertyToID("_CloudLightAbsorption");
    static readonly int _cloudDarknessThresholdId = Shader.PropertyToID("_CloudDarknessThreshold");
    static readonly int _cloudPhaseParamsId = Shader.PropertyToID("_CloudPhaseParams");
    static readonly int _cloudColorId = Shader.PropertyToID("_CloudColor");
    static readonly int _cloudStormColorId = Shader.PropertyToID("_CloudStormColor");
    static readonly int _cloudAmbientStrengthId = Shader.PropertyToID("_CloudAmbientStrength");
    static readonly int _cloudStormDarkeningId = Shader.PropertyToID("_CloudStormDarkening");
    static readonly int _cloudSilverLiningParamsId = Shader.PropertyToID("_CloudSilverLiningParams");
    static readonly int _cloudShadowParamsId = Shader.PropertyToID("_CloudShadowParams");
    static readonly int _cloudAnimSpeedId = Shader.PropertyToID("_CloudAnimSpeed");
    static readonly int _cloudViewStepsId = Shader.PropertyToID("_CloudViewSteps");
    static readonly int _cloudLightStepsId = Shader.PropertyToID("_CloudLightSteps");
    static readonly int _cloudRayOffsetStrengthId = Shader.PropertyToID("_CloudRayOffsetStrength");
    static readonly int _cloudDebugModeId = Shader.PropertyToID("_CloudDebugMode");
    static readonly int _cloudDebugParamsId = Shader.PropertyToID("_CloudDebugParams");
    static readonly int _cloudShapeNoiseId = Shader.PropertyToID("_CloudShapeNoise");
    static readonly int _cloudDetailNoiseId = Shader.PropertyToID("_CloudDetailNoise");

    void Awake()
    {
        ServiceLocator.Register<ICloudController>(this);
    }

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

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

    void Update()
    {
        if (Settings == null || _planetRadius <= 0f)
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
        _weather = ServiceLocator.Get<IWeatherConfigurator>();
        if (_weather != null && _weather.Settings != Settings)
            _weather.Configure(Settings);
    }

    void GenerateNoiseTextures()
    {
        if (NoiseCompute == null || Settings == null) return;

        int seed = ServiceLocator.Get<ISeedProvider>().GetSeedForSystem("CloudNoise");

        ReleaseTextures();
        _shapeNoise = CloudNoiseGenerator.GenerateShapeNoise(NoiseCompute, Settings.ShapeNoiseResolution, seed);
        _detailNoise = CloudNoiseGenerator.GenerateDetailNoise(NoiseCompute, Settings.DetailNoiseResolution, seed);
        // Force the noise-texture globals to re-bind on the next static upload.
        _staticPropertiesDirty = true;
    }

    // Uploads all settings that are constant for a given (Settings, planet) pair. Skipped after
    // the first upload unless the planet is regenerated or Settings reference changes.
    void EnsureStaticPropertiesUploaded()
    {
        if (!_staticPropertiesDirty && _lastStaticSettings == Settings) return;
        _staticPropertiesDirty = false;
        _lastStaticSettings = Settings;

        float innerRadius = _seaLevelRadius + Settings.BaseAltitude;
        float outerRadius = innerRadius + Settings.LayerThickness;

        Shader.SetGlobalVector(_cloudPlanetCenterId, _planetCenter);
        Shader.SetGlobalFloat(_cloudInnerRadiusId, innerRadius);
        Shader.SetGlobalFloat(_cloudOuterRadiusId, outerRadius);
        Shader.SetGlobalFloat(_cloudNoiseScaleId, Settings.NoiseScale);
        Shader.SetGlobalFloat(_cloudDetailNoiseScaleId, Settings.DetailNoiseScale);
        Shader.SetGlobalFloat(_cloudDetailWeightId, Settings.DetailWeight);
        Shader.SetGlobalVector(_cloudShapeWeightsId, Settings.ShapeNoiseWeights);
        Shader.SetGlobalFloat(_cloudDensityMultiplierId, Settings.DensityMultiplier);
        Shader.SetGlobalFloat(_cloudDensityThresholdId, Settings.DensityThreshold);
        Shader.SetGlobalFloat(_cloudShapeSharpnessId, Settings.ShapeSharpness);
        Shader.SetGlobalFloat(_cloudBottomFeatherId, Settings.BottomFeather);
        Shader.SetGlobalFloat(_cloudTopFeatherId, Settings.TopFeather);
        Shader.SetGlobalFloat(_cloudTopDensityBiasId, Settings.TopDensityBias);
        Shader.SetGlobalFloat(_cloudLightAbsorptionId, Settings.LightAbsorption);
        Shader.SetGlobalFloat(_cloudDarknessThresholdId, Settings.DarknessThreshold);
        Shader.SetGlobalVector(_cloudPhaseParamsId, new Vector4(
            Settings.ForwardScattering, Settings.BackScattering, Settings.BaseBrightness, Settings.PhaseStrength));
        Shader.SetGlobalColor(_cloudColorId, Settings.CloudColor);
        Shader.SetGlobalColor(_cloudStormColorId, Settings.StormColor);
        Shader.SetGlobalFloat(_cloudAmbientStrengthId, Settings.AmbientStrength);
        Shader.SetGlobalFloat(_cloudStormDarkeningId, Settings.StormDarkening);
        Shader.SetGlobalVector(_cloudSilverLiningParamsId, new Vector4(
            Settings.SilverLiningStrength,
            Settings.SilverLiningPower,
            Settings.SilverLiningEdgePower,
            Settings.SilverLiningStormSuppression));
        Shader.SetGlobalVector(_cloudShadowParamsId, new Vector4(
            Settings.ShadowStrength,
            Settings.ShadowSoftness,
            Settings.StormShadowBoost,
            Settings.ShadowHorizonFade));
        Shader.SetGlobalFloat(_cloudAnimSpeedId, Settings.AnimationSpeed);
        Shader.SetGlobalInt(_cloudLightStepsId, Settings.LightSteps);
        Shader.SetGlobalFloat(_cloudRayOffsetStrengthId, Settings.RayOffsetStrength);
        Shader.SetGlobalInt(_cloudDebugModeId, (int)Settings.DebugMode);
        Shader.SetGlobalVector(_cloudDebugParamsId, new Vector4(
            Settings.CondensationChangeDebugThreshold,
            Mathf.Max(Settings.CondensationChangeDebugSaturation, Settings.CondensationChangeDebugThreshold + 0.0001f),
            SphericalWeatherGrid.DeltaVisualizationScale,
            0f));

        if (_shapeNoise != null)
            Shader.SetGlobalTexture(_cloudShapeNoiseId, _shapeNoise);
        if (_detailNoise != null)
            Shader.SetGlobalTexture(_cloudDetailNoiseId, _detailNoise);

        // Reset the per-frame texture cache so weather bindings re-publish against the fresh
        // state. Cheap; ensures consistency if a planet regen changed which weather grid is live.
        _lastWeatherTexture = null;
        _lastDynamicsTexture = null;
        _lastWeatherResolution = -1;
        _lastUploadedViewSteps = int.MinValue;
    }

    // Per-frame work: re-evaluate camera-distance-dependent ViewSteps, and re-bind weather
    // textures when WeatherManager has swapped them. Every other shader property is static.
    void UpdatePerFrameProperties()
    {
        int viewSteps = Settings.ViewSteps;
        Camera mainCam = Camera.main;
        if (mainCam != null && _seaLevelRadius > 0f)
        {
            float altitude = Vector3.Distance(mainCam.transform.position, _planetCenter) - _seaLevelRadius;
            float t = Mathf.InverseLerp(Settings.StepScaleNearAltitude,
                Mathf.Max(Settings.StepScaleFarAltitude, Settings.StepScaleNearAltitude + 1f), altitude);
            viewSteps = Mathf.RoundToInt(Mathf.Lerp(Settings.ViewSteps, Settings.MinViewSteps, t));
        }
        viewSteps = Mathf.Max(Settings.MinViewSteps,
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
        ServiceLocator.Unregister<ICloudController>(this);
        ReleaseTextures();
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("density", "Get or set cloud density multiplier (range 0-0.08).", MonoTargetType.Single)]
    string DensityCmd(float? value = null)
    {
        if (Settings == null) return "no CloudSettings bound";
        if (value == null) return $"cloud density: {Settings.DensityMultiplier:F4}";
        Settings.DensityMultiplier = Mathf.Clamp(value.Value, 0f, 0.08f);
        _staticPropertiesDirty = true;
        return $"cloud density: {Settings.DensityMultiplier:F4}";
    }

    [ConsoleCommand("altitude", "Get or set cloud base altitude in meters (range 20-1000).", MonoTargetType.Single)]
    string AltitudeCmd(float? value = null)
    {
        if (Settings == null) return "no CloudSettings bound";
        if (value == null) return $"cloud base altitude: {Settings.BaseAltitude:F0}m";
        Settings.BaseAltitude = Mathf.Clamp(value.Value, 20f, 1000f);
        _staticPropertiesDirty = true;
        return $"cloud base altitude: {Settings.BaseAltitude:F0}m";
    }

    [ConsoleCommand("thickness", "Get or set cloud layer thickness in meters (range 50-1000).", MonoTargetType.Single)]
    string ThicknessCmd(float? value = null)
    {
        if (Settings == null) return "no CloudSettings bound";
        if (value == null) return $"cloud layer thickness: {Settings.LayerThickness:F0}m";
        Settings.LayerThickness = Mathf.Clamp(value.Value, 50f, 1000f);
        _staticPropertiesDirty = true;
        return $"cloud layer thickness: {Settings.LayerThickness:F0}m";
    }
}
