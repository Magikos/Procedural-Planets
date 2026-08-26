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
    public const float RainFormationThreshold = 0.88f;
    public const float RainFormationSoftness = 0.10f;
    public const float RainCloudThreshold = 0.90f;
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
    public const float PowderStrength = 0.65f;
    public const float MultiScatterAttenuation = 0.5f;
    public const float MultiScatterContribution = 0.5f;
    public const float MultiScatterPhaseScale = 0.5f;
    public const float MultiScatterStrength = 0.35f;
    public static readonly Color AmbientSky = new Color(0.62f, 0.76f, 0.98f, 1f);
    public static readonly Color AmbientGround = new Color(0.50f, 0.45f, 0.38f, 1f);
    // Aerial perspective (Phase 3): "distant clouds sit in the sky". Authored as a 0-1 fade
    // fraction (human), converted to the shader's Beer-Lambert per-metre coefficient at
    // AerialReferenceDistance. 0 = off, 1 = fully hazed at the reference distance.
    public const float AerialFade = 0.7f;
    public const float AerialReferenceDistance = 2500f;
    // Backlit inner glow: forward-scattered sunlight bleeding through a cloud lit from behind.
    // Strength is the human 0-2 knob (cloud.backlit); Power tightens the forward lobe.
    public const float BacklitStrength = 0.6f;
    public const float BacklitPower = 6f;
    // God-ray streaks (post-cloud pass): crepuscular rays via radial blur of the composited
    // scene's bright pixels (sun disc + bright cloud rims) - dark cloud bodies read as the gaps.
    // Strength is the human 0-2 knob (cloud.godray-strength). Higher decay + longer march = the
    // beams reach further across the sky (the dramatic reference look).
    public const float GodRayStreakStrength = 1.0f;
    public const float GodRayStreakSampleCount = 48f;
    public const float GodRayStreakDecay = 0.975f;
    public const float GodRayStreakMarchLength = 0.85f;
    // Scene luminance above which a sample becomes a ray source (cloud.godray-threshold). Lower =
    // more of the bright sky feeds the rays (softer, broader); higher = only the very brightest
    // sun/rim pixels beam (crisper, more selective).
    public const float GodRayStreakBrightThreshold = 0.65f;
    // Soft radial fade by angular (screen-UV) distance from the sun: exp(-dist * rate). Localizes
    // the effect near the sun while still fanning out believably (not a hard cutoff).
    public const float GodRayStreakRadialFalloff = 3.0f;
    // Extra multiplier on strength specifically near the horizon (dawn/dusk), on top of the
    // localSunVisibility gate. 1 = no boost. Human live-tunable via cloud.godray-dawn-boost.
    public const float GodRayStreakDawnBoost = 1.3f;
    // Rain shaft / virga: faint grey veil hung below gloomy cloud cells. Strength is the human
    // 0-2 knob (cloud.rain-shaft), 0 = off. Length is how far (metres) below the cloud base the
    // veil reaches before fading out (roughly the base altitude, so heavy rain reaches the ground).
    public const float RainShaftStrength = 0f;
    public const float RainShaftLength = 300f;
    public const float SilverLiningStrength = 0.9f;
    public const float SilverLiningPower = 10f;
    public const float SilverLiningEdgePower = 1.6f;
    // Fraction of the silver lining removed at full storm gloom.
    public const float SilverLiningStormSuppression = 0.45f;

    // Shadows
    public const float ShadowStrength = 0.35f;
    public const float ShadowSoftness = 0.45f;
    public const float StormShadowBoost = 1.35f;
    public const float ShadowHorizonFade = 0.18f;
}
