using UnityEngine;

public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public AtmosphereSettings Settings;
    public CelestialManager CelestialManager;
    public ComputeShader OpticalDepthCompute;

    float _planetRadius;
    float _seaLevelRadius;
    Vector3 _planetCenter;
    int _planetSeed = 12345;
    RenderTexture _bakedOpticalDepth;
    float _lastBakedScaleR, _lastBakedScaleM, _lastBakedAtmoScale;
    int _lastBakedSize, _lastBakedSteps;

    static readonly int _sunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _planetRadiusId = Shader.PropertyToID("_PlanetRadius");
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

        SetGlobalProperties();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _seaLevelRadius = evt.SeaLevelRadius > 0f ? evt.SeaLevelRadius : _planetRadius * 0.95f;

        var planet = FindAnyObjectByType<Planet>();
        _planetCenter = planet != null ? planet.transform.position : Vector3.zero;
        _planetSeed = planet != null ? planet.Seed : 12345;

        BakeOpticalDepth();
        SetGlobalProperties();
    }

    void SetGlobalProperties()
    {
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
        OpticalDepthCompute.SetFloat("_PlanetRadius", _seaLevelRadius);
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
}
