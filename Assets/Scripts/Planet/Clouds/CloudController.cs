using UnityEngine;

/// <summary>
/// Sets cloud shader globals from CloudSettings and planet data.
/// Cloud rendering is handled by CloudRenderFeature (fullscreen ray march).
/// </summary>
public class CloudController : MonoBehaviour
{
    [Header("References")]
    public CloudSettings Settings;

    float _planetRadius;
    Vector3 _planetCenter;

    static readonly int _cloudInnerRadiusId = Shader.PropertyToID("_CloudInnerRadius");
    static readonly int _cloudOuterRadiusId = Shader.PropertyToID("_CloudOuterRadius");
    static readonly int _cloudPlanetCenterId = Shader.PropertyToID("_CloudPlanetCenter");
    static readonly int _cloudNoiseScaleId = Shader.PropertyToID("_CloudNoiseScale");
    static readonly int _cloudDetailNoiseScaleId = Shader.PropertyToID("_CloudDetailNoiseScale");
    static readonly int _cloudDetailWeightId = Shader.PropertyToID("_CloudDetailWeight");
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID("_CloudDensityMultiplier");
    static readonly int _cloudDensityOffsetId = Shader.PropertyToID("_CloudDensityOffset");
    static readonly int _cloudLightAbsorptionId = Shader.PropertyToID("_CloudLightAbsorption");
    static readonly int _cloudDarknessThresholdId = Shader.PropertyToID("_CloudDarknessThreshold");
    static readonly int _cloudPhaseParamsId = Shader.PropertyToID("_CloudPhaseParams");
    static readonly int _cloudAnimSpeedId = Shader.PropertyToID("_CloudAnimSpeed");
    static readonly int _cloudViewStepsId = Shader.PropertyToID("_CloudViewSteps");
    static readonly int _cloudLightStepsId = Shader.PropertyToID("_CloudLightSteps");

    void OnEnable() => EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
    void OnDisable() => EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        _planetCenter = evt.PlanetCenter;
        SetGlobalProperties();
    }

    void Update()
    {
        if (Settings == null || _planetRadius <= 0f) return;
        SetGlobalProperties();
    }

    void SetGlobalProperties()
    {
        float innerRadius = _planetRadius * Settings.CloudAltitudeScale;
        float outerRadius = innerRadius + _planetRadius * Settings.CloudThickness;

        Shader.SetGlobalVector(_cloudPlanetCenterId, _planetCenter);
        Shader.SetGlobalFloat(_cloudInnerRadiusId, innerRadius);
        Shader.SetGlobalFloat(_cloudOuterRadiusId, outerRadius);
        Shader.SetGlobalFloat(_cloudNoiseScaleId, Settings.NoiseScale);
        Shader.SetGlobalFloat(_cloudDetailNoiseScaleId, Settings.DetailNoiseScale);
        Shader.SetGlobalFloat(_cloudDetailWeightId, Settings.DetailWeight);
        Shader.SetGlobalFloat(_cloudDensityMultiplierId, Settings.DensityMultiplier);
        Shader.SetGlobalFloat(_cloudDensityOffsetId, Settings.DensityOffset);
        Shader.SetGlobalFloat(_cloudLightAbsorptionId, Settings.LightAbsorption);
        Shader.SetGlobalFloat(_cloudDarknessThresholdId, Settings.DarknessThreshold);
        Shader.SetGlobalVector(_cloudPhaseParamsId, new Vector4(
            Settings.ForwardScattering, Settings.BackScattering, Settings.BaseBrightness, 0));
        Shader.SetGlobalFloat(_cloudAnimSpeedId, Settings.AnimationSpeed);
        Shader.SetGlobalInt(_cloudViewStepsId, Settings.ViewSteps);
        Shader.SetGlobalInt(_cloudLightStepsId, Settings.LightSteps);
    }
}
