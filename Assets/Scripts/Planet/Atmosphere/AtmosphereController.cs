using UnityEngine;

public class AtmosphereController : MonoBehaviour
{
    [Header("References")]
    public AtmosphereSettings Settings;
    public CelestialManager CelestialManager;

    float _planetRadius;
    float _seaLevelRadius;

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
    static readonly int _debugModeId = Shader.PropertyToID("_DebugMode");
    static readonly int _starSeedId = Shader.PropertyToID("_StarSeed");
    static readonly int _starDensityId = Shader.PropertyToID("_StarDensity");
    static readonly int _starBrightnessId = Shader.PropertyToID("_StarBrightness");

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

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

        float atmosphereRadius = _planetRadius * Settings.AtmosphereScale;
        Debug.Log($"[AtmosphereController v3.5] maxRadius={_planetRadius:F1}, seaLevel={_seaLevelRadius:F1}, atmosphereRadius={atmosphereRadius:F1}");

        SetGlobalProperties();
    }

    void SetGlobalProperties()
    {
        float atmosphereRadius = _planetRadius * Settings.AtmosphereScale;
        float atmosphereThickness = atmosphereRadius - _seaLevelRadius;

        Vector3 center = Vector3.zero;
        var planet = FindAnyObjectByType<Planet>();
        if (planet != null) center = planet.transform.position;

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
        Shader.SetGlobalInt(_debugModeId, Settings.DebugMode);

        int seed = planet != null ? planet.Seed : 12345;
        Shader.SetGlobalFloat(_starSeedId, seed * 0.01f);
        Shader.SetGlobalFloat(_starDensityId, Settings.StarDensity);
        Shader.SetGlobalFloat(_starBrightnessId, Settings.StarBrightness);
    }
}
