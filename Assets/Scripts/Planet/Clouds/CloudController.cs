using UnityEngine;

/// <summary>
/// Uploads planet-scale cloud data and render settings to the cloud shader.
/// Weather state lives in WeatherManager; this component owns render-only noise textures.
/// </summary>
public class CloudController : MonoBehaviour
{
    [Header("References")]
    public CloudSettings Settings;
    public ComputeShader NoiseCompute;

    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    RenderTexture _shapeNoise;
    RenderTexture _detailNoise;
    WeatherManager _weather;

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
        ServiceLocator.Register<CloudController>(this);
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

        Initialize();
        GenerateNoiseTextures();
        SetGlobalProperties();
    }

    void Update()
    {
        if (Settings == null || _planetRadius <= 0f)
        {
            Shader.SetGlobalInt(_cloudWeatherResolutionId, 0);
            return;
        }

        SetGlobalProperties();
    }

    void Initialize()
    {
        _weather = ServiceLocator.Get<WeatherManager>();
        if (_weather != null && _weather.Settings != Settings)
            _weather.Configure(Settings);
    }

    void GenerateNoiseTextures()
    {
        if (NoiseCompute == null || Settings == null) return;

        int seed = 12345;
        ISeedProvider seedProvider = ServiceLocator.Get<ISeedProvider>();
        seed = seedProvider.GetSeedForSystem("CloudNoise");

        ReleaseTextures();
        _shapeNoise = CloudNoiseGenerator.GenerateShapeNoise(NoiseCompute, Settings.ShapeNoiseResolution, seed);
        _detailNoise = CloudNoiseGenerator.GenerateDetailNoise(NoiseCompute, Settings.DetailNoiseResolution, seed);
    }

    void SetGlobalProperties()
    {
        if (Settings == null) return;

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
        Shader.SetGlobalInt(_cloudViewStepsId, viewSteps);
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

        if (_weather != null && _weather.WeatherTexture != null)
        {
            Shader.SetGlobalTexture(_cloudWeatherMapId, _weather.WeatherTexture);
            if (_weather.WeatherDynamicsTexture != null)
                Shader.SetGlobalTexture(_weatherDynamicsMapId, _weather.WeatherDynamicsTexture);
            Shader.SetGlobalInt(_cloudWeatherResolutionId, _weather.WeatherResolution);
        }
        else
        {
            Shader.SetGlobalInt(_cloudWeatherResolutionId, 0);
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
        ServiceLocator.Unregister<CloudController>(this);
        ReleaseTextures();
    }
}
