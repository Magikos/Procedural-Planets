using UnityEngine;

/// <summary>
/// Packs cloud instance data from WeatherManager into a GPU buffer
/// and sets all cloud shader globals. Generates 3D noise textures once.
/// </summary>
public class CloudController : MonoBehaviour
{
    [Header("References")]
    public CloudSettings Settings;
    public ComputeShader NoiseCompute;

    const int MaxClouds = 64;

    float _planetRadius;
    Vector3 _planetCenter;
    RenderTexture _shapeNoise;
    RenderTexture _detailNoise;
    ComputeBuffer _cloudBuffer;
    WeatherManager _weather;

    static readonly int _cloudPlanetCenterId = Shader.PropertyToID("_CloudPlanetCenter");
    static readonly int _cloudNoiseScaleId = Shader.PropertyToID("_CloudNoiseScale");
    static readonly int _cloudDetailNoiseScaleId = Shader.PropertyToID("_CloudDetailNoiseScale");
    static readonly int _cloudDetailWeightId = Shader.PropertyToID("_CloudDetailWeight");
    static readonly int _cloudShapeWeightsId = Shader.PropertyToID("_CloudShapeWeights");
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID("_CloudDensityMultiplier");
    static readonly int _cloudLightAbsorptionId = Shader.PropertyToID("_CloudLightAbsorption");
    static readonly int _cloudDarknessThresholdId = Shader.PropertyToID("_CloudDarknessThreshold");
    static readonly int _cloudPhaseParamsId = Shader.PropertyToID("_CloudPhaseParams");
    static readonly int _cloudAnimSpeedId = Shader.PropertyToID("_CloudAnimSpeed");
    static readonly int _cloudViewStepsId = Shader.PropertyToID("_CloudViewSteps");
    static readonly int _cloudLightStepsId = Shader.PropertyToID("_CloudLightSteps");
    static readonly int _cloudShapeNoiseId = Shader.PropertyToID("_CloudShapeNoise");
    static readonly int _cloudDetailNoiseId = Shader.PropertyToID("_CloudDetailNoise");
    static readonly int _cloudBufferId = Shader.PropertyToID("_CloudBuffer");
    static readonly int _cloudCountId = Shader.PropertyToID("_CloudCount");

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _planetCenter = evt.PlanetCenter;
        _weather = FindAnyObjectByType<WeatherManager>();
        GenerateNoiseTextures();
        EnsureBuffer();
        SetGlobalProperties();
    }

    void Update()
    {
        if (Settings == null || _planetRadius <= 0f) return;
        UploadCloudData();
        SetGlobalProperties();
    }

    void GenerateNoiseTextures()
    {
        if (NoiseCompute == null || Settings == null) return;

        var planet = FindAnyObjectByType<Planet>();
        int seed = planet != null ? planet.Seed : 12345;

        ReleaseTextures();
        _shapeNoise = CloudNoiseGenerator.GenerateShapeNoise(NoiseCompute, Settings.ShapeNoiseResolution, seed);
        _detailNoise = CloudNoiseGenerator.GenerateDetailNoise(NoiseCompute, Settings.DetailNoiseResolution, seed);
    }

    void EnsureBuffer()
    {
        if (_cloudBuffer == null || _cloudBuffer.count != MaxClouds)
        {
            _cloudBuffer?.Release();
            // 32 bytes per cloud: float3 pos (12) + float radius (4) + float density (4) + 3 padding (12)
            _cloudBuffer = new ComputeBuffer(MaxClouds, 32);
        }
    }

    void UploadCloudData()
    {
        if (_cloudBuffer == null || _weather == null) return;

        var clouds = _weather.Clouds;
        int count = Mathf.Min(clouds.Count, MaxClouds);

        if (count > 0)
        {
            var data = new WeatherManager.CloudInstance[MaxClouds];
            for (int i = 0; i < count; i++)
                data[i] = clouds[i];
            _cloudBuffer.SetData(data);
        }

        Shader.SetGlobalBuffer(_cloudBufferId, _cloudBuffer);
        Shader.SetGlobalInt(_cloudCountId, count);
    }

    void SetGlobalProperties()
    {
        Shader.SetGlobalVector(_cloudPlanetCenterId, _planetCenter);
        Shader.SetGlobalFloat(_cloudNoiseScaleId, Settings.NoiseScale);
        Shader.SetGlobalFloat(_cloudDetailNoiseScaleId, Settings.DetailNoiseScale);
        Shader.SetGlobalFloat(_cloudDetailWeightId, Settings.DetailWeight);
        Shader.SetGlobalVector(_cloudShapeWeightsId, Settings.ShapeNoiseWeights);
        Shader.SetGlobalFloat(_cloudDensityMultiplierId, Settings.DensityMultiplier);
        Shader.SetGlobalFloat(_cloudLightAbsorptionId, Settings.LightAbsorption);
        Shader.SetGlobalFloat(_cloudDarknessThresholdId, Settings.DarknessThreshold);
        Shader.SetGlobalVector(_cloudPhaseParamsId, new Vector4(
            Settings.ForwardScattering, Settings.BackScattering, Settings.BaseBrightness, 0));
        Shader.SetGlobalFloat(_cloudAnimSpeedId, Settings.AnimationSpeed);
        Shader.SetGlobalInt(_cloudViewStepsId, Settings.ViewSteps);
        Shader.SetGlobalInt(_cloudLightStepsId, Settings.LightSteps);

        if (_shapeNoise != null)
            Shader.SetGlobalTexture(_cloudShapeNoiseId, _shapeNoise);
        if (_detailNoise != null)
            Shader.SetGlobalTexture(_cloudDetailNoiseId, _detailNoise);
    }

    void ReleaseTextures()
    {
        if (_shapeNoise != null) { _shapeNoise.Release(); _shapeNoise = null; }
        if (_detailNoise != null) { _detailNoise.Release(); _detailNoise = null; }
    }

    void OnDestroy()
    {
        ReleaseTextures();
        _cloudBuffer?.Release();
    }
}
