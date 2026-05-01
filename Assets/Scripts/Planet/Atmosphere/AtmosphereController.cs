using UnityEngine;

/// <summary>
/// Drives the screen-space atmospheric scattering render pass.
/// Bakes the optical depth look-up texture via compute shader and sets
/// all atmosphere-related global shader properties each frame.
///
/// Requires AtmosphereRenderFeature to be added to the URP Renderer Asset.
/// Add this component to any GameObject in the scene (e.g., the planet).
/// </summary>
public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The OpticalDepth.compute shader asset")]
    public ComputeShader OpticalDepthCompute;
    [Tooltip("CelestialManager in the scene (provides sun direction)")]
    public CelestialManager CelestialManager;

    [Header("Scale")]
    [Range(1.01f, 1.3f), Tooltip("Atmosphere outer radius as a multiple of planet radius")]
    public float AtmosphereScale = 1.08f;

    [Header("Scattering")]
    [Range(1, 30), Tooltip("Number of in-scattering sample steps (quality vs. performance)")]
    public int InScatteringPoints = 10;
    [Range(1f, 80f), Tooltip("Overall light intensity multiplier")]
    public float Intensity = 20f;

    [Header("Rayleigh (Sky Color)")]
    [Tooltip("Rayleigh scattering coefficients — controls sky colour (dimensionless, wavelength-proportional; tune visually)")]
    public Vector3 RayleighScattering = new Vector3(1.0e-3f, 2.5e-3f, 4.0e-3f);
    [Range(1f, 20f), Tooltip("Scale height for Rayleigh scattering — higher = thicker lower atmosphere")]
    public float RayleighFalloff = 7.5f;

    [Header("Mie (Haze / Sun Glow)")]
    [Tooltip("Mie scattering coefficients — controls haze and sun glow size (dimensionless; tune visually)")]
    public Vector3 MieScattering = new Vector3(5.0e-4f, 5.0e-4f, 5.0e-4f);
    [Range(0.5f, 20f), Tooltip("Scale height for Mie scattering")]
    public float MieFalloff = 1.2f;
    [Range(0f, 0.99f), Tooltip("Mie anisotropy — higher = tighter glow around sun")]
    public float MieAnisotropy = 0.7f;

    [Header("Absorption (Ozone)")]
    [Tooltip("Absorption coefficients — controls ozone-like colour tinting")]
    public Vector3 AbsorptionBeta = new Vector3(2.04e-5f, 4.97e-5f, 1.95e-6f);
    [Range(0f, 1f), Tooltip("Normalised height at which absorption peaks")]
    public float HeightAbsorption = 0.3f;

    [Header("Ambient")]
    [Tooltip("Ambient atmosphere colour — slight glow on dark side (set to zero to disable)")]
    public Vector3 AmbientBeta = Vector3.zero;

    [Header("Optical Depth Bake")]
    [Range(64, 512), Tooltip("Resolution of the baked optical-depth texture (baked once per planet generation)")]
    public int BakeTextureSize = 256;
    [Range(8, 64), Tooltip("Number of samples used when baking the optical-depth texture")]
    public int BakeSteps = 40;

    // ─── cached state ──────────────────────────────────────────────────────────
    float _planetRadius;
    float _atmosphereRadius;
    RenderTexture _bakedOpticalDepth;

    // ─── shader property IDs ───────────────────────────────────────────────────
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
    static readonly int _absorbtionBetaId = Shader.PropertyToID("_AbsorbtionBeta"); // note: shader typo preserved
    static readonly int _ambientBetaId = Shader.PropertyToID("_AmbientBeta");
    static readonly int _rayleighFalloffId = Shader.PropertyToID("_RayleighFalloff");
    static readonly int _mieFalloffId = Shader.PropertyToID("_MieFalloff");
    static readonly int _heightAbsorbtionId = Shader.PropertyToID("_HeightAbsorbtion"); // note: shader typo preserved
    static readonly int _intensityId = Shader.PropertyToID("_Intensity");

    // ─── Unity lifecycle ───────────────────────────────────────────────────────

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        // Clear the global so the shader doesn't sample a stale/released texture
        // (visible in the Editor scene view after play stops).
        Shader.SetGlobalTexture(_bakedOpticalDepthId, null);
    }

    void OnDestroy()
    {
        _bakedOpticalDepth?.Release();
    }

    void Update()
    {
        if (CelestialManager != null)
            Shader.SetGlobalVector(_sunParamsId, CelestialManager.SunDirection);
    }

    // ─── event handler ─────────────────────────────────────────────────────────

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _atmosphereRadius = _planetRadius * AtmosphereScale;

        BakeOpticalDepth();
        SetGlobalProperties();
    }

    // ─── private helpers ───────────────────────────────────────────────────────

    void BakeOpticalDepth()
    {
        if (OpticalDepthCompute == null || _planetRadius <= 0f) return;

        // Re-create texture only when size changes
        if (_bakedOpticalDepth != null && _bakedOpticalDepth.width != BakeTextureSize)
        {
            _bakedOpticalDepth.Release();
            _bakedOpticalDepth = null;
        }

        if (_bakedOpticalDepth == null)
        {
            _bakedOpticalDepth = new RenderTexture(BakeTextureSize, BakeTextureSize, 0, RenderTextureFormat.ARGBHalf)
            {
                enableRandomWrite = true,
                name = "BakedOpticalDepth"
            };
            _bakedOpticalDepth.Create();
        }

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
        // Cutoff sphere limits rays from going underground. Must be <= planet radius.
        Shader.SetGlobalFloat(_cutoffRadiusId, _planetRadius);
        Shader.SetGlobalVector(_planetCenterId, center);
        Shader.SetGlobalInt(_numInScatteringPointsId, InScatteringPoints);
        Shader.SetGlobalVector(_rayleighScatteringId, new Vector4(RayleighScattering.x, RayleighScattering.y, RayleighScattering.z, 0f));
        Shader.SetGlobalVector(_mieScatteringId, new Vector4(MieScattering.x, MieScattering.y, MieScattering.z, 0f));
        Shader.SetGlobalFloat(_mieGId, MieAnisotropy);
        Shader.SetGlobalVector(_absorbtionBetaId, new Vector4(AbsorptionBeta.x, AbsorptionBeta.y, AbsorptionBeta.z, 0f));
        Shader.SetGlobalVector(_ambientBetaId, new Vector4(AmbientBeta.x, AmbientBeta.y, AmbientBeta.z, 0f));
        Shader.SetGlobalFloat(_rayleighFalloffId, RayleighFalloff);
        Shader.SetGlobalFloat(_mieFalloffId, MieFalloff);
        Shader.SetGlobalFloat(_heightAbsorbtionId, HeightAbsorption);
        Shader.SetGlobalFloat(_intensityId, Intensity);
    }
}
