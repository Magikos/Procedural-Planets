using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Cloud Settings")]
public class CloudSettings : ScriptableObject
{
    public enum DebugView
    {
        Off = 0,
        Weather = 1,
        Storm = 2,
        Density = 3,
        OpticalDepth = 4,
        SilverLining = 5,
        MoistureSource = 6,
        CondensationChange = 7
    }

    [Header("Weather Grid")]
    [Range(32, 512)] public int WeatherResolution = 256;
    [Range(0f, 1f)] public float InitialCoverage = 0.48f;
    [Range(0.5f, 12f)] public float FrontScale = 4f;
    [Range(1f, 40f)] public float FrontSharpness = 18f;
    [Range(0f, 1f)] public float StormThreshold = 0.78f;
    [Range(0f, 1f)] public float BiomeInfluence = 0.15f;

    [Header("Weather Motion")]
    [Range(0f, 5f)] public float FrontAdvectionDegreesPerSecond = 0.4f;

    [Header("Weather Evolution")]
    public bool EnableWeatherEvolution = true;
    [Range(0.05f, 5f)] public float EvolutionInterval = 0.1f;
    [Range(0f, 0.2f)] public float MoistureSourceStrength = 0.04f;
    [Range(0f, 0.2f)] public float DryAirEvaporationRate = 0.03f;
    [Range(0f, 0.5f)] public float StormGrowthRate = 0.12f;
    [Range(0f, 0.5f)] public float StormDecayRate = 0.08f;
    [Range(0f, 1f)] public float StormMoistureBias = 0.35f;

    [Header("Precipitation Dynamics")]
    [Range(0f, 1f)] public float RainFormationThreshold = 0.25f;
    [Range(0.01f, 1f)] public float RainFormationSoftness = 0.35f;
    [Range(0f, 0.5f)] public float PrecipitationBuildRate = 0.1f;
    [Range(0f, 0.5f)] public float PrecipitationDecayRate = 0.06f;
    [Range(0f, 0.5f)] public float RainOutRate = 0.05f;
    [Range(0f, 0.5f)] public float HumidityRecoveryRate = 0.025f;
    [Range(0f, 1f)] public float CondensationRainDrain = 0.22f;

    [Header("Weather Validation")]
    public bool UseValidationEvolutionRates = false;
    [Range(0.05f, 1f)] public float ValidationEvolutionInterval = 0.1f;
    [Range(1f, 20f)] public float ValidationMoistureMultiplier = 8f;
    [Range(1f, 20f)] public float ValidationEvaporationMultiplier = 8f;
    [Range(1f, 20f)] public float ValidationStormMultiplier = 6f;

    [Header("Layer")]
    [Range(20f, 1000f)] public float BaseAltitude = 330f;
    [Range(50f, 1000f)] public float LayerThickness = 300f;
    [Range(0.01f, 0.4f)] public float BottomFeather = 0.06f;
    [Range(0.05f, 0.8f)] public float TopFeather = 0.45f;
    [Range(0.1f, 4f)] public float TopDensityBias = 1.2f;

    [Header("Shape")]
    [Range(0.0001f, 0.01f)] public float NoiseScale = 0.003f;
    [Range(0.0001f, 0.02f)] public float DetailNoiseScale = 0.008f;
    [Range(0f, 1f)] public float DetailWeight = 0.3f;
    public Vector4 ShapeNoiseWeights = new Vector4(1f, 0.5f, 0.25f, 0.125f);
    [Range(0f, 0.08f)] public float DensityMultiplier = 0.018f;
    [Range(0f, 1f)] public float DensityThreshold = 0.22f;
    [Range(1f, 24f)] public float ShapeSharpness = 8f;

    [Header("Lighting")]
    public Color CloudColor = new Color(1f, 0.98f, 0.92f, 1f);
    public Color StormColor = new Color(0.35f, 0.37f, 0.42f, 1f);
    [Range(0f, 3f)] public float LightAbsorption = 1.2f;
    [Range(0f, 1f)] public float DarknessThreshold = 0.1f;
    [Range(0f, 1f)] public float ForwardScattering = 0.8f;
    [Range(0f, 1f)] public float BackScattering = 0.3f;
    [Range(0f, 1f)] public float BaseBrightness = 0.8f;
    [Range(0f, 1f)] public float PhaseStrength = 0.15f;
    [Range(0f, 1f)] public float AmbientStrength = 0.12f;
    [Range(0f, 1f)] public float StormDarkening = 0.65f;
    [Range(0f, 3f)] public float SilverLiningStrength = 0.9f;
    [Range(1f, 32f)] public float SilverLiningPower = 10f;
    [Range(0.25f, 4f)] public float SilverLiningEdgePower = 1.6f;
    [Range(0f, 1f)] public float SilverLiningStormSuppression = 0.85f;

    [Header("Shadows")]
    [Range(0f, 1f)] public float ShadowStrength = 0.35f;
    [Range(0.01f, 1f)] public float ShadowSoftness = 0.45f;
    [Range(0.5f, 3f)] public float StormShadowBoost = 1.35f;
    [Range(0f, 0.4f)] public float ShadowHorizonFade = 0.18f;

    [Header("Animation")]
    [Range(0f, 2f)] public float AnimationSpeed = 0.35f;

    [Header("Ray March")]
    [Range(8, 96)] public int ViewSteps = 48;
    [Range(2, 16)] public int LightSteps = 6;
    [Range(0f, 2f)] public float RayOffsetStrength = 0.65f;

    [Header("Noise Textures")]
    [Range(32, 256)] public int ShapeNoiseResolution = 128;
    [Range(16, 64)] public int DetailNoiseResolution = 32;

    [Header("Debug")]
    public DebugView DebugMode = DebugView.Off;
    [Range(0f, 0.01f)] public float CondensationChangeDebugThreshold = 0.0002f;
    [Range(0.0005f, 0.02f)] public float CondensationChangeDebugSaturation = 0.004f;

    public float ActiveEvolutionInterval =>
        UseValidationEvolutionRates ? ValidationEvolutionInterval : EvolutionInterval;

    public float ActiveMoistureSourceStrength =>
        MoistureSourceStrength * (UseValidationEvolutionRates ? ValidationMoistureMultiplier : 1f);

    public float ActiveDryAirEvaporationRate =>
        DryAirEvaporationRate * (UseValidationEvolutionRates ? ValidationEvaporationMultiplier : 1f);

    public float ActiveStormGrowthRate =>
        StormGrowthRate * (UseValidationEvolutionRates ? ValidationStormMultiplier : 1f);

    public float ActiveStormDecayRate =>
        StormDecayRate * (UseValidationEvolutionRates ? ValidationStormMultiplier : 1f);
}
