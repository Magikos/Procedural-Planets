using UnityEngine;

public sealed record AtmosphereDto(
    float AtmosphereScale,
    int ViewSteps,
    int SunSteps,
    Vector3 RayleighScattering,
    float RayleighScaleHeight,
    float MieScattering,
    float MieScaleHeight,
    float MieAnisotropy,
    float TerrainClarityDistance,
    float TerrainAtmosphereDistance,
    float SunIntensity,
    float SunDiscSize,
    float SunDiscBlend,
    float SunDiscIntensity,
    float SunAureoleStrength,
    float SunAureolePower,
    bool EnableLightShafts,
    int LightShaftSamples,
    float LightShaftStrength,
    float LightShaftDensity,
    float LightShaftDecay,
    float LightShaftWeight,
    float LightShaftExposure,
    float LightShaftThreshold,
    int BakeTextureSize,
    int BakeSteps,
    int DebugMode)
{
    public static AtmosphereDto From(AtmosphereSettings src) => new(
        src.AtmosphereScale,
        src.ViewSteps,
        src.SunSteps,
        src.RayleighScattering,
        src.RayleighScaleHeight,
        src.MieScattering,
        src.MieScaleHeight,
        src.MieAnisotropy,
        src.TerrainClarityDistance,
        src.TerrainAtmosphereDistance,
        src.SunIntensity,
        src.SunDiscSize,
        src.SunDiscBlend,
        src.SunDiscIntensity,
        src.SunAureoleStrength,
        src.SunAureolePower,
        src.EnableLightShafts,
        src.LightShaftSamples,
        src.LightShaftStrength,
        src.LightShaftDensity,
        src.LightShaftDecay,
        src.LightShaftWeight,
        src.LightShaftExposure,
        src.LightShaftThreshold,
        src.BakeTextureSize,
        src.BakeSteps,
        src.DebugMode);

    public bool TryValidate(out string error)
    {
        error = null;
        if (!InRange(AtmosphereScale, 1.01f, 1.5f)) error = "Atmosphere scale must be between 1.01 and 1.5.";
        else if (ViewSteps < 1 || ViewSteps > 32 || SunSteps < 1 || SunSteps > 16)
            error = "Atmosphere view steps must be 1-32 and sun steps must be 1-16.";
        else if (BakeTextureSize < 1 || BakeTextureSize > 512 || BakeSteps < 1 || BakeSteps > 128)
            error = "Atmosphere bake size must be 1-512 and bake steps must be 1-128.";
        else if (!Nonnegative(RayleighScattering.x) || !Nonnegative(RayleighScattering.y) || !Nonnegative(RayleighScattering.z))
            error = "Rayleigh scattering must be finite and nonnegative.";
        else if (!Positive(RayleighScaleHeight) || !Positive(MieScaleHeight))
            error = "Atmosphere scale heights must be finite and positive.";
        else if (!Nonnegative(MieScattering) || !Nonnegative(SunIntensity))
            error = "Atmosphere scattering and sun intensity must be finite and nonnegative.";
        else if (!InRange(MieAnisotropy, 0f, 0.99f)) error = "Mie anisotropy must be between 0 and 0.99.";
        else if (!Nonnegative(TerrainClarityDistance) || !Nonnegative(TerrainAtmosphereDistance))
            error = "Terrain atmosphere distances must be finite and nonnegative.";
        else if (!InRange(SunDiscSize, 0.99f, 0.9999f) || !InRange(SunDiscBlend, AtmosphereSettings.SunDiscBlendMin, AtmosphereSettings.SunDiscBlendMax))
            error = "Sun disc size or blend is outside its supported range.";
        else if (!Nonnegative(SunDiscIntensity) || !Nonnegative(SunAureoleStrength) || !Positive(SunAureolePower))
            error = "Sun disc and aureole settings must be finite, with nonnegative intensity and positive power.";
        else if (LightShaftSamples < 0 || LightShaftSamples > 32 || !Nonnegative(LightShaftStrength)
            || !Positive(LightShaftDensity) || !InRange(LightShaftDecay, 0f, 1f)
            || !Nonnegative(LightShaftWeight) || !Nonnegative(LightShaftExposure) || !Nonnegative(LightShaftThreshold))
            error = "Light shaft settings must be finite and valid, with 0-32 samples and decay between 0 and 1.";
        else if (DebugMode < 0 || DebugMode > 5) error = "Atmosphere debug mode must be between 0 and 5.";
        return error == null;
    }

    static bool Nonnegative(float value) => float.IsFinite(value) && value >= 0f;
    static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    static bool InRange(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
}
