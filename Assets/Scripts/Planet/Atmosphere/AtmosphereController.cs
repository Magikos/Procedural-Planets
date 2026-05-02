using UnityEngine;

/// <summary>
/// Drives the screen-space atmospheric scattering render pass.
/// All settings live in an AtmosphereSettings ScriptableObject asset.
/// </summary>
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public AtmosphereSettings Settings;
    public ComputeShader OpticalDepthCompute;
    public CelestialManager CelestialManager;

    float _planetRadius;
    float _atmosphereRadius;
    RenderTexture _bakedOpticalDepth;

    float _lastRayleighFalloff, _lastMieFalloff, _lastHeightAbsorption, _lastAtmosphereScale;
    int _lastBakeSteps;

    static readonly int _bakedOpticalDepthId = Shader.PropertyToID("_BakedOpticalDepth");
    static readonly int _blueNoiseId = Shader.PropertyToID("_BlueNoise");
    static readonly int _dirToSunId = Shader.PropertyToID("_DirToSun");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _planetRadiusId = Shader.PropertyToID("_PlanetRadius");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int _numInScatteringPointsId = Shader.PropertyToID("_NumInScatteringPoints");
    static readonly int _rayleighScatteringId = Shader.PropertyToID("_RayleighScattering");
    static readonly int _mieScatteringId = Shader.PropertyToID("_MieScattering");
    static readonly int _mieGId = Shader.PropertyToID("_MieG");
    static readonly int _absorptionBetaId = Shader.PropertyToID("_AbsorptionBeta");
    static readonly int _ambientBetaId = Shader.PropertyToID("_AmbientBeta");
    static readonly int _rayleighFalloffId = Shader.PropertyToID("_RayleighFalloff");
    static readonly int _mieFalloffId = Shader.PropertyToID("_MieFalloff");
    static readonly int _heightAbsorptionId = Shader.PropertyToID("_HeightAbsorption");
    static readonly int _intensityId = Shader.PropertyToID("_Intensity");
    static readonly int _ditherStrengthId = Shader.PropertyToID("_DitherStrength");
    static readonly int _ditherScaleId = Shader.PropertyToID("_DitherScale");
    static readonly int _sunDiscSizeId = Shader.PropertyToID("_SunDiscSize");
    static readonly int _sunDiscBlendId = Shader.PropertyToID("_SunDiscBlend");
    static readonly int _nightAmbientId = Shader.PropertyToID("_NightAmbient");

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }
    void OnDestroy() => _bakedOpticalDepth?.Release();

    void Update()
    {
        if (Settings == null) return;

        if (CelestialManager != null)
            Shader.SetGlobalVector(_dirToSunId, CelestialManager.SunDirection);

        if (_planetRadius > 0f)
        {
            _atmosphereRadius = _planetRadius * (1 + Settings.AtmosphereScale);

            if (LutNeedsRebake())
                BakeOpticalDepth();

            SetGlobalProperties();
        }
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _atmosphereRadius = _planetRadius * (1 + Settings.AtmosphereScale);
        BakeOpticalDepth();
        SetGlobalProperties();
    }

    bool LutNeedsRebake()
    {
        return Settings.RayleighFalloff != _lastRayleighFalloff
            || Settings.MieFalloff != _lastMieFalloff
            || Settings.HeightAbsorption != _lastHeightAbsorption
            || Settings.AtmosphereScale != _lastAtmosphereScale
            || Settings.BakeSteps != _lastBakeSteps;
    }

    void BakeOpticalDepth()
    {
        if (OpticalDepthCompute == null || Settings == null || _planetRadius <= 0f) return;

        if (_bakedOpticalDepth != null && _bakedOpticalDepth.width != Settings.BakeTextureSize)
        {
            _bakedOpticalDepth.Release();
            _bakedOpticalDepth = null;
        }

        if (_bakedOpticalDepth == null)
        {
            _bakedOpticalDepth = new RenderTexture(Settings.BakeTextureSize, Settings.BakeTextureSize, 0, RenderTextureFormat.ARGBHalf)
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
        OpticalDepthCompute.SetInt("_TextureSize", Settings.BakeTextureSize);
        OpticalDepthCompute.SetInt("_NumOutScatteringSteps", Settings.BakeSteps);
        OpticalDepthCompute.SetFloat("_PlanetRadius", _planetRadius);
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", _atmosphereRadius);
        OpticalDepthCompute.SetFloat("_RayleighFalloff", Settings.RayleighFalloff);
        OpticalDepthCompute.SetFloat("_MieFalloff", Settings.MieFalloff);
        OpticalDepthCompute.SetFloat("_HeightAbsorbtion", Settings.HeightAbsorption);

        int groups = Mathf.CeilToInt(Settings.BakeTextureSize / 8f);
        OpticalDepthCompute.Dispatch(kernel, groups, groups, 1);

        _lastRayleighFalloff = Settings.RayleighFalloff;
        _lastMieFalloff = Settings.MieFalloff;
        _lastHeightAbsorption = Settings.HeightAbsorption;
        _lastAtmosphereScale = Settings.AtmosphereScale;
        _lastBakeSteps = Settings.BakeSteps;

        Shader.SetGlobalTexture(_bakedOpticalDepthId, _bakedOpticalDepth);
    }

    void SetGlobalProperties()
    {
        if (_planetRadius <= 0f || Settings == null) return;

        Vector3 center = Vector3.zero;
        var planet = FindAnyObjectByType<Planet>();
        if (planet != null) center = planet.transform.position;

        Shader.SetGlobalFloat(_planetRadiusId, _planetRadius);
        Shader.SetGlobalFloat(_atmosphereRadiusId, _atmosphereRadius);
        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalInt(_numInScatteringPointsId, Settings.InScatteringPoints);

        Shader.SetGlobalVector(_rayleighScatteringId, Settings.RayleighScattering);
        Shader.SetGlobalVector(_mieScatteringId, Vector3.one * Settings.MieStrength);
        Shader.SetGlobalFloat(_mieGId, Settings.MieAnisotropy);
        Shader.SetGlobalVector(_absorptionBetaId, Settings.AbsorptionBeta);
        Shader.SetGlobalVector(_ambientBetaId, new Vector4(Settings.AmbientBeta.r, Settings.AmbientBeta.g, Settings.AmbientBeta.b, 0));

        Shader.SetGlobalFloat(_rayleighFalloffId, Settings.RayleighFalloff);
        Shader.SetGlobalFloat(_mieFalloffId, Settings.MieFalloff);
        Shader.SetGlobalFloat(_heightAbsorptionId, Settings.HeightAbsorption);
        Shader.SetGlobalFloat(_intensityId, Settings.Intensity);

        Shader.SetGlobalFloat(_ditherStrengthId, Settings.DitherStrength);
        Shader.SetGlobalFloat(_ditherScaleId, Settings.DitherScale);
        Shader.SetGlobalFloat(_sunDiscSizeId, Settings.SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, Settings.SunDiscBlend);
        Shader.SetGlobalVector(_nightAmbientId, new Vector3(Settings.NightAmbient.r, Settings.NightAmbient.g, Settings.NightAmbient.b));

        if (Settings.BlueNoise != null)
            Shader.SetGlobalTexture(_blueNoiseId, Settings.BlueNoise);
    }
}
