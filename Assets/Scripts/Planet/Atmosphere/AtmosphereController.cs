using UnityEngine;

/// <summary>
/// Drives the screen-space atmospheric scattering render pass.
/// Bakes the optical depth look-up texture via compute shader and sets
/// all atmosphere-related global shader properties each frame.
///
/// Uses wavelength-based scattering with /planetRadius normalization
/// for scale-independent rendering at any planet radius.
/// </summary>
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public ComputeShader OpticalDepthCompute;
    public CelestialManager CelestialManager;

    [Header("Scale")]
    [Range(0.01f, 1f)] public float AtmosphereScale = 0.15f;

    [Header("Scattering")]
    [Range(1, 30)] public int InScatteringPoints = 10;
    [Range(0.1f, 20f)] public float Intensity = 1f;
    [Range(0.1f, 100f)] public float ScatteringStrength = 8f;
    public Vector3 Wavelengths = new Vector3(700, 530, 460);

    [Header("Density")]
    [Range(0.1f, 30f)] public float DensityFalloff = 4f;

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9998f;
    [Range(0.0001f, 0.01f)] public float SunDiscBlend = 0.001f;

    [Header("Surface")]
    [Range(0.1f, 5f), Tooltip("How much atmosphere dims the planet surface. Lower = less white washout.")]
    public float SurfaceAttenuation = 1f;

    [Header("Night")]
    [Tooltip("Ambient color on the dark side (simulates moonlight/starlight)")]
    public Color NightAmbient = new Color(0.01f, 0.012f, 0.02f, 1f);

    [Header("Dithering")]
    public Texture2D BlueNoise;
    [Range(0f, 2f)] public float DitherStrength = 0.8f;
    [Range(1f, 8f)] public float DitherScale = 4f;

    [Header("Optical Depth Bake")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(8, 64)] public int BakeSteps = 10;

    float _planetRadius;
    float _atmosphereRadius;
    RenderTexture _bakedOpticalDepth;

    // Track LUT-affecting values to re-bake when they change
    float _lastBakedDensityFalloff;
    float _lastBakedAtmosphereScale;

    // Shader property IDs
    static readonly int _bakedOpticalDepthId = Shader.PropertyToID("_BakedOpticalDepth");
    static readonly int _blueNoiseId = Shader.PropertyToID("_BlueNoise");
    static readonly int _dirToSunId = Shader.PropertyToID("_DirToSun");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _planetRadiusId = Shader.PropertyToID("_PlanetRadius");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly int _numInScatteringPointsId = Shader.PropertyToID("_NumInScatteringPoints");
    static readonly int _densityFalloffId = Shader.PropertyToID("_DensityFalloff");
    static readonly int _scatteringCoefficientsId = Shader.PropertyToID("_ScatteringCoefficients");
    static readonly int _intensityId = Shader.PropertyToID("_Intensity");
    static readonly int _ditherStrengthId = Shader.PropertyToID("_DitherStrength");
    static readonly int _ditherScaleId = Shader.PropertyToID("_DitherScale");
    static readonly int _sunDiscSizeId = Shader.PropertyToID("_SunDiscSize");
    static readonly int _sunDiscBlendId = Shader.PropertyToID("_SunDiscBlend");
    static readonly int _surfaceAttenuationId = Shader.PropertyToID("_SurfaceAttenuation");
    static readonly int _nightAmbientId = Shader.PropertyToID("_NightAmbient");

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }

    void OnDestroy()
    {
        _bakedOpticalDepth?.Release();
    }

    void Update()
    {
        if (CelestialManager != null)
            Shader.SetGlobalVector(_dirToSunId, CelestialManager.SunDirection);

        // Push all properties every frame so inspector changes are reflected in real time
        if (_planetRadius > 0f)
        {
            _atmosphereRadius = _planetRadius * (1 + AtmosphereScale);

            if (DensityFalloff != _lastBakedDensityFalloff || AtmosphereScale != _lastBakedAtmosphereScale)
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

    Vector3 ComputeScatteringCoefficients()
    {
        // Rayleigh scattering is inversely proportional to wavelength^4
        float scatterX = Mathf.Pow(400f / Wavelengths.x, 4f);
        float scatterY = Mathf.Pow(400f / Wavelengths.y, 4f);
        float scatterZ = Mathf.Pow(400f / Wavelengths.z, 4f);
        return new Vector3(scatterX, scatterY, scatterZ) * ScatteringStrength;
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
            _bakedOpticalDepth = new RenderTexture(BakeTextureSize, BakeTextureSize, 0, RenderTextureFormat.RFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                name = "BakedOpticalDepth"
            };
            _bakedOpticalDepth.Create();
        }

        // Compute shader uses normalized radius (planetRadius = 1)
        float normalizedAtmosphereRadius = 1f + AtmosphereScale;

        int kernel = OpticalDepthCompute.FindKernel("Main");
        OpticalDepthCompute.SetTexture(kernel, "_Result", _bakedOpticalDepth);
        OpticalDepthCompute.SetInt("_TextureSize", BakeTextureSize);
        OpticalDepthCompute.SetInt("_NumOutScatteringSteps", BakeSteps);
        OpticalDepthCompute.SetFloat("_AtmosphereRadius", normalizedAtmosphereRadius);
        OpticalDepthCompute.SetFloat("_DensityFalloff", DensityFalloff);

        int groups = Mathf.CeilToInt(BakeTextureSize / 8f);
        OpticalDepthCompute.Dispatch(kernel, groups, groups, 1);

        _lastBakedDensityFalloff = DensityFalloff;
        _lastBakedAtmosphereScale = AtmosphereScale;

        // Debug: read back a sample to verify LUT has non-zero values
        var readback = new Texture2D(1, 1, TextureFormat.RFloat, false);
        RenderTexture.active = _bakedOpticalDepth;
        readback.ReadPixels(new Rect(BakeTextureSize / 2, BakeTextureSize / 2, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = null;
        float sample = readback.GetPixel(0, 0).r;
        UnityEngine.Debug.Log($"[Atmosphere] LUT baked. Center sample = {sample:F6} (should be > 0)");
        Object.Destroy(readback);

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
        Shader.SetGlobalFloat(_densityFalloffId, DensityFalloff);
        Shader.SetGlobalVector(_scatteringCoefficientsId, ComputeScatteringCoefficients());
        Shader.SetGlobalFloat(_intensityId, Intensity);
        Shader.SetGlobalFloat(_ditherStrengthId, DitherStrength);
        Shader.SetGlobalFloat(_ditherScaleId, DitherScale);
        Shader.SetGlobalFloat(_sunDiscSizeId, SunDiscSize);
        Shader.SetGlobalFloat(_sunDiscBlendId, SunDiscBlend);
        Shader.SetGlobalFloat(_surfaceAttenuationId, SurfaceAttenuation);
        Shader.SetGlobalVector(_nightAmbientId, new Vector3(NightAmbient.r, NightAmbient.g, NightAmbient.b));

        if (BlueNoise != null)
            Shader.SetGlobalTexture(_blueNoiseId, BlueNoise);
    }
}
