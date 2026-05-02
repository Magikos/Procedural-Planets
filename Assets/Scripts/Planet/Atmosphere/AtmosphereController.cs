using UnityEngine;

/// <summary>
/// Drives the screen-space atmospheric scattering render pass.
/// Uses URP-Atmosphere approach: world-scale 3-channel LUT, incremental optical depth,
/// phase functions, wavelength-based coefficients.
/// </summary>
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public ComputeShader OpticalDepthCompute;
    public CelestialManager CelestialManager;
    public Texture2D BlueNoise;

    [Header("Scale")]
    [Range(0.01f, 1f)] public float AtmosphereScale = 0.15f;

    [Header("Scattering")]
    [Range(1, 30)] public int InScatteringPoints = 10;
    [Range(0.1f, 100f)] public float Intensity = 20f;

    [Header("Rayleigh (Sky Color)")]
    [Tooltip("Rayleigh scattering coefficients — controls sky color")]
    public Vector3 RayleighScattering = new Vector3(5.8e-3f, 13.5e-3f, 33.1e-3f);
    [Range(1f, 30f)] public float RayleighFalloff = 8f;

    [Header("Mie (Sun Glow / Haze)")]
    [Range(0f, 0.01f)] public float MieStrength = 0.001f;
    [Range(1f, 30f)] public float MieFalloff = 1.2f;
    [Range(0f, 0.999f)] public float MieAnisotropy = 0.76f;

    [Header("Absorption (Ozone)")]
    public Vector3 AbsorptionBeta = new Vector3(2.04e-5f, 4.97e-5f, 1.95e-6f);
    [Range(0f, 1f)] public float HeightAbsorption = 0.25f;

    [Header("Ambient")]
    public Color AmbientBeta = Color.black;

    [Header("Night")]
    public Color NightAmbient = new Color(0.01f, 0.012f, 0.02f, 1f);

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9998f;
    [Range(0.0001f, 0.01f)] public float SunDiscBlend = 0.001f;

    [Header("Dithering")]
    [Range(0f, 2f)] public float DitherStrength = 0.8f;
    [Range(1f, 8f)] public float DitherScale = 4f;

    [Header("Optical Depth Bake")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(8, 64)] public int BakeSteps = 40;

    float _planetRadius;
    float _atmosphereRadius;
    RenderTexture _bakedOpticalDepth;

    // Track LUT-affecting values to re-bake when they change
    float _lastRayleighFalloff, _lastMieFalloff, _lastHeightAbsorption, _lastAtmosphereScale;
    int _lastBakeSteps;

    // Shader property IDs
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
        if (CelestialManager != null)
            Shader.SetGlobalVector(_dirToSunId, CelestialManager.SunDirection);

        if (_planetRadius > 0f)
        {
            _atmosphereRadius = _planetRadius * (1 + AtmosphereScale);

            if (LutNeedsRebake())
                BakeOpticalDepth();

            SetGlobalProperties();
        }
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _atmosphereRadius = _planetRadius * (1 + AtmosphereScale);
        BakeOpticalDepth();
        SetGlobalProperties();
    }

    bool LutNeedsRebake()
    {
        return RayleighFalloff != _lastRayleighFalloff
            || MieFalloff != _lastMieFalloff
            || HeightAbsorption != _lastHeightAbsorption
            || AtmosphereScale != _lastAtmosphereScale
            || BakeSteps != _lastBakeSteps;
    }

    Vector3 ComputeRayleighCoefficients()
    {
        return RayleighScattering;
    }

    void BakeOpticalDepth()
    {
        if (OpticalDepthCompute == null || _planetRadius <= 0f) return;

        if (_bakedOpticalDepth != null && _bakedOpticalDepth.width != BakeTextureSize)
        {
            _bakedOpticalDepth.Release();
            _bakedOpticalDepth = null;
        }

        if (_bakedOpticalDepth == null)
        {
            // ARGBHalf: 4-channel half-float, matches RWTexture2D<float4> and stores 3-channel density
            _bakedOpticalDepth = new RenderTexture(BakeTextureSize, BakeTextureSize, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "BakedOpticalDepth"
            };
            _bakedOpticalDepth.Create();
        }

        // Bake at WORLD SCALE — pass actual planet and atmosphere radii
        int kernel = OpticalDepthCompute.FindKernel("Main");
        OpticalDepthCompute.SetTexture(kernel, "_Result", _bakedOpticalDepth);
        OpticalDepthCompute.SetInt("_TextureSize", BakeTextureSize);
        OpticalDepthCompute.SetInt("_NumOutScatteringSteps", BakeSteps);
        OpticalDepthCompute.SetFloat("_PlanetRadius", _planetRadius);
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", _atmosphereRadius);
        OpticalDepthCompute.SetFloat("_RayleighFalloff", RayleighFalloff);
        OpticalDepthCompute.SetFloat("_MieFalloff", MieFalloff);
        OpticalDepthCompute.SetFloat("_HeightAbsorbtion", HeightAbsorption);

        int groups = Mathf.CeilToInt(BakeTextureSize / 8f);
        OpticalDepthCompute.Dispatch(kernel, groups, groups, 1);

        _lastRayleighFalloff = RayleighFalloff;
        _lastMieFalloff = MieFalloff;
        _lastHeightAbsorption = HeightAbsorption;
        _lastAtmosphereScale = AtmosphereScale;
        _lastBakeSteps = BakeSteps;

        Shader.SetGlobalTexture(_bakedOpticalDepthId, _bakedOpticalDepth);
    }

    void SetGlobalProperties()
    {
        if (_planetRadius <= 0f) return;

        Vector3 center = Vector3.zero;
        var planet = FindAnyObjectByType<Planet>();
        if (planet != null) center = planet.transform.position;

        Shader.SetGlobalFloat(_planetRadiusId, _planetRadius);
        Shader.SetGlobalFloat(_atmosphereRadiusId, _atmosphereRadius);
        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalInt(_numInScatteringPointsId, InScatteringPoints);

        Shader.SetGlobalVector(_rayleighScatteringId, ComputeRayleighCoefficients());
        Shader.SetGlobalVector(_mieScatteringId, Vector3.one * MieStrength);
        Shader.SetGlobalFloat(_mieGId, MieAnisotropy);
        Shader.SetGlobalVector(_absorptionBetaId, AbsorptionBeta);
        Shader.SetGlobalVector(_ambientBetaId, new Vector4(AmbientBeta.r, AmbientBeta.g, AmbientBeta.b, 0));

        Shader.SetGlobalFloat(_rayleighFalloffId, RayleighFalloff);
        Shader.SetGlobalFloat(_mieFalloffId, MieFalloff);
        Shader.SetGlobalFloat(_heightAbsorptionId, HeightAbsorption);
        Shader.SetGlobalFloat(_intensityId, Intensity);

        Shader.SetGlobalFloat(_ditherStrengthId, DitherStrength);
        Shader.SetGlobalFloat(_ditherScaleId, DitherScale);
        Shader.SetGlobalFloat(_sunDiscSizeId, SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, SunDiscBlend);
        Shader.SetGlobalVector(_nightAmbientId, new Vector3(NightAmbient.r, NightAmbient.g, NightAmbient.b));

        if (BlueNoise != null)
            Shader.SetGlobalTexture(_blueNoiseId, BlueNoise);
    }
}
