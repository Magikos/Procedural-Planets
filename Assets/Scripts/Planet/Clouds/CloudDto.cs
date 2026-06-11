using UnityEngine;

public sealed record CloudDto(
    int WeatherResolution,
    float InitialCoverage,
    float StormThreshold,
    bool EnableWeatherEvolution,
    float EvolutionInterval,
    float BaseAltitude,
    float LayerThickness,
    float DensityMultiplier,
    Color CloudColor,
    Color StormColor,
    float AnimationSpeed,
    int ViewSteps,
    int LightSteps,
    float RayOffsetStrength,
    int MinViewSteps,
    float StepScaleNearAltitude,
    float StepScaleFarAltitude,
    int ShapeNoiseResolution,
    int DetailNoiseResolution,
    CloudSettings.DebugView DebugMode,
    float CondensationChangeDebugThreshold,
    float CondensationChangeDebugSaturation)
{
    public static void EnsureRegistered()
    {
        if (SettingsProvider.IsRegistered<CloudDto>()) return;
        var so = Resources.Load<CloudSettings>("Settings/CloudSettings");
        SettingsProvider.Register(From(so));
    }

    public static CloudDto From(CloudSettings src) => new(
        src.WeatherResolution,
        src.InitialCoverage,
        src.StormThreshold,
        src.EnableWeatherEvolution,
        src.EvolutionInterval,
        src.BaseAltitude,
        src.LayerThickness,
        src.DensityMultiplier,
        src.CloudColor,
        src.StormColor,
        src.AnimationSpeed,
        src.ViewSteps,
        src.LightSteps,
        src.RayOffsetStrength,
        src.MinViewSteps,
        src.StepScaleNearAltitude,
        src.StepScaleFarAltitude,
        src.ShapeNoiseResolution,
        src.DetailNoiseResolution,
        src.DebugMode,
        src.CondensationChangeDebugThreshold,
        src.CondensationChangeDebugSaturation);
}
