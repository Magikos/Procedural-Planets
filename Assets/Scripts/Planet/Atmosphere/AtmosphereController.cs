using UnityEngine;

/// <summary>
/// Drives the screen-space atmospheric scattering render pass.
/// Restored to match the original URP-Atmosphere implementation exactly.
/// Settings live in AtmosphereSettings ScriptableObject.
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
    static readonly int _sunParamsId = Shader.PropertyToID("_SunParams");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _planetRadiusId = Shader.PropertyToID("_PlanetRadius");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int _cutoffRadiusId = Shader.PropertyToID("_CutoffRadius");
    static readonly int _numInScatteringPointsId = Shader.PropertyToID("_NumInScatteringPoints");
    static readonly int _rayleighScatteringId = Shader.PropertyToID("_RayleighScattering");
    static readonly int _mieScatteringId = Shader.PropertyToID("_MieScattering");
    static readonly int _mieGId = Shader.PropertyToID("_MieG");
    static readonly int _absorbtionBetaId = Shader.PropertyToID("_AbsorbtionBeta");
    static readonly int _ambientBetaId = Shader.PropertyToID("_AmbientBeta");
    static readonly int _rayleighFalloffId = Shader.PropertyToID("_RayleighFalloff");
    static readonly int _mieFalloffId = Shader.PropertyToID("_MieFalloff");
    static readonly int _heightAbsorbtionId = Shader.PropertyToID("_HeightAbsorbtion");
    static readonly int _intensityId = Shader.PropertyToID("_Intensity");
    static readonly int _sunDiscSizeId = Shader.PropertyToID("_SunDiscSize");
    static readonly int _sunDiscBlendId = Shader.PropertyToID("_SunDiscBlend");

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }
    void OnDestroy() => _bakedOpticalDepth?.Release();

    void Update()
    {
        // Always update sun direction, even if Settings isn't assigned yet
        if (CelestialManager != null)
            Shader.SetGlobalVector(_sunParamsId, CelestialManager.SunDirection);

        if (Settings == null)
        {
            Debug.LogWarning("[AtmosphereController] Settings asset is not assigned!");
            return;
        }

        if (_planetRadius > 0f)
        {
            _atmosphereRadius = _planetRadius * Settings.AtmosphereScale;

            if (LutNeedsRebake())
                BakeOpticalDepth();

            SetGlobalProperties();
        }
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _atmosphereRadius = _planetRadius * Settings.AtmosphereScale;
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
        Shader.SetGlobalFloat(_cutoffRadiusId, _planetRadius - 5f);
        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalInt(_numInScatteringPointsId, Settings.InScatteringPoints);
        Shader.SetGlobalVector(_rayleighScatteringId, Settings.RayleighScattering);
        Shader.SetGlobalVector(_mieScatteringId, Settings.MieScattering);
        Shader.SetGlobalFloat(_mieGId, Settings.MieAnisotropy);
        Shader.SetGlobalVector(_absorbtionBetaId, Settings.AbsorptionBeta);
        Shader.SetGlobalVector(_ambientBetaId, Settings.AmbientBeta);
        Shader.SetGlobalFloat(_rayleighFalloffId, Settings.RayleighFalloff);
        Shader.SetGlobalFloat(_mieFalloffId, Settings.MieFalloff);
        Shader.SetGlobalFloat(_heightAbsorbtionId, Settings.HeightAbsorption);
        Shader.SetGlobalFloat(_intensityId, Settings.Intensity);
        Shader.SetGlobalFloat(_sunDiscSizeId, Settings.SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, Settings.SunDiscBlend);
    }
}
