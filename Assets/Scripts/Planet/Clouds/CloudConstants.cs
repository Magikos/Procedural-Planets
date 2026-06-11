using UnityEngine;

public static class CloudConstants
{
    // Weather grid seed
    public const float FrontScale = 4f;
    public const float FrontSharpness = 18f;
    public const float BiomeInfluence = 0.15f;
    public const float FrontAdvectionSpeedMultiplier = 1f;

    // Weather evolution rates (compute shader sim constants)
    public const float MoistureSourceStrength = 0.04f;
    public const float DryAirEvaporationRate = 0.03f;
    public const float StormGrowthRate = 0.075f;
    public const float StormDecayRate = 0.13f;
    public const float StormMoistureBias = 0.5f;
    public const float StormSourceThreshold = 0.76f;
    public const float StormSourceSoftness = 0.16f;

    // Precipitation dynamics
    public const float RainFormationThreshold = 0.58f;
    public const float RainFormationSoftness = 0.22f;
    public const float RainCloudThreshold = 0.78f;
    public const float PrecipitationBuildRate = 0.055f;
    public const float PrecipitationDecayRate = 0.14f;
    public const float RainOutRate = 0.09f;
    public const float HumidityRecoveryRate = 0.018f;
    public const float CondensationRainDrain = 0.32f;

    // Layer feathering
    public const float BottomFeather = 0.06f;
    public const float TopFeather = 0.45f;
    public const float TopDensityBias = 1.2f;

    // Shape
    public const float NoiseScale = 0.003f;
    public const float DetailNoiseScale = 0.008f;
    public const float DetailWeight = 0.3f;
    public const float DensityThreshold = 0.22f;
    public const float ShapeSharpness = 8f;
    public static readonly Vector4 ShapeNoiseWeights = new Vector4(1f, 0.5f, 0.25f, 0.125f);

    // Lighting
    public const float LightAbsorption = 1.2f;
    public const float DarknessThreshold = 0.1f;
    public const float ForwardScattering = 0.8f;
    public const float BackScattering = 0.3f;
    public const float BaseBrightness = 0.8f;
    public const float PhaseStrength = 0.15f;
    public const float AmbientStrength = 0.12f;
    public const float StormDarkening = 0.65f;
    public const float SilverLiningStrength = 0.9f;
    public const float SilverLiningPower = 10f;
    public const float SilverLiningEdgePower = 1.6f;
    public const float SilverLiningStormSuppression = 0.85f;

    // Shadows
    public const float ShadowStrength = 0.35f;
    public const float ShadowSoftness = 0.45f;
    public const float StormShadowBoost = 1.35f;
    public const float ShadowHorizonFade = 0.18f;
}
