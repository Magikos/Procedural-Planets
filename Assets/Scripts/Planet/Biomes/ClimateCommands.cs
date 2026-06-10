using System;
using System.Threading;
using UnityEngine;

public enum ClimateLatitudePreset
{
    Legacy,
    Earthlike,
    StrongBands,
}

public static class TemperatureUnitPreferences
{
    const string PlayerPrefsKey = "TemperatureUnit.v1";

    public static TemperatureUnit PreferredUnit
    {
        get
        {
            int stored = PlayerPrefs.GetInt(PlayerPrefsKey, (int)TemperatureUnit.Celsius);
            return Enum.IsDefined(typeof(TemperatureUnit), stored)
                ? (TemperatureUnit)stored
                : TemperatureUnit.Celsius;
        }
    }

    public static void SetPreferredUnit(TemperatureUnit unit)
    {
        PlayerPrefs.SetInt(PlayerPrefsKey, (int)unit);
        PlayerPrefs.Save();
    }

    public static string Format(float celsius, string format = "F1")
    {
        TemperatureUnit unit = PreferredUnit;
        float value = TemperatureUnits.FromCelsius(celsius, unit);
        return $"{value.ToString(format)} {(unit == TemperatureUnit.Celsius ? "C" : "F")}";
    }
}

[CommandPrefix("climate")]
public static class ClimateCommands
{
    [ConsoleCommand("status", "Show active latitude curves and climate contribution settings.")]
    public static string Status()
    {
        return GetSettings().Describe();
    }

    [ConsoleCommand("preset", "Apply a climate latitude preset. Run 'climate.apply' to regenerate.")]
    public static string Preset(ClimateLatitudePreset preset)
    {
        BiomeSettings settings = GetEditableSettings();
        settings.ApplyPreset(preset);
        return $"climate preset {preset} applied. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("altitude-lapse", "Get or set normalized altitude temperature drop. Run 'climate.apply' after setting.")]
    public static string AltitudeLapse(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.AltitudeTemperatureDrop = Mathf.Clamp(value.Value, 0f, 10f);
        return $"altitude temperature drop: {settings.AltitudeTemperatureDrop:F3}";
    }

    [ConsoleCommand("moisture-bands", "Get or set latitude-band influence from 0 to 1. Run 'climate.apply' after setting.")]
    public static string MoistureBands(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.MoistureLatitudeInfluence = Mathf.Clamp01(value.Value);
        return $"moisture latitude influence: {settings.MoistureLatitudeInfluence:F3}";
    }

    [ConsoleCommand("moisture-noise", "Get or set latitude-band moisture noise strength from 0 to 1. Has no effect while moisture-bands is 0. Run 'climate.apply' after setting.")]
    public static string MoistureNoise(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.MoistureNoiseStrength = Mathf.Clamp01(value.Value);
        return $"moisture band noise strength: {settings.MoistureNoiseStrength:F3}";
    }

    [ConsoleCommand("temperature-noise", "Get or set centered temperature noise strength from 0 to 0.5. Run 'climate.apply' after setting.")]
    public static string TemperatureNoise(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.TemperatureNoiseStrength = Mathf.Clamp(value.Value, 0f, 0.5f);
        return $"temperature noise strength: {settings.TemperatureNoiseStrength:F3}";
    }

    [ConsoleCommand("lut-resolution", "Get or set climate curve LUT resolution from 16 to 512. Run 'climate.apply' after setting.")]
    public static string LutResolution(int? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.ClimateLutResolution = Mathf.Clamp(value.Value, 16, 512);
        return $"climate LUT resolution: {settings.ClimateLutResolution}";
    }

    [ConsoleCommand("map-resolution", "Get or set GPU climate map resolution per cube face from 32 to 512. Run 'climate.apply' after setting.")]
    public static string MapResolution(int? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.ClimateMapResolution = Mathf.Clamp(value.Value, 32, 512);
        return $"climate map resolution: {settings.ClimateMapResolution} per face";
    }

    [ConsoleCommand("temperature-range", "Get or set the normalized climate range in Celsius: minimum maximum. Run 'climate.apply' after setting.")]
    public static string TemperatureRange(float? minimumCelsius = null, float? maximumCelsius = null)
    {
        BiomeSettings settings = minimumCelsius.HasValue || maximumCelsius.HasValue
            ? GetEditableSettings()
            : GetSettings();
        if (minimumCelsius.HasValue)
            settings.MinimumTemperatureCelsius = Mathf.Clamp(minimumCelsius.Value, -100f, 50f);
        if (maximumCelsius.HasValue)
        {
            settings.MaximumTemperatureCelsius = Mathf.Clamp(
                maximumCelsius.Value,
                settings.MinimumTemperatureCelsius + 1f,
                100f);
        }
        settings.EnsureClimateCurves();
        return $"temperature range: {settings.MinimumTemperatureCelsius:F1} C to " +
               $"{settings.MaximumTemperatureCelsius:F1} C";
    }

    [ConsoleCommand("temperature-unit", "Get or set player temperature display units.")]
    public static string TemperatureUnit(TemperatureUnit? unit = null)
    {
        if (unit.HasValue)
            TemperatureUnitPreferences.SetPreferredUnit(unit.Value);
        return $"temperature display unit: {TemperatureUnitPreferences.PreferredUnit}";
    }

    [ConsoleCommand("voronoi-seeds", "Get or set global Voronoi seed count from 128 to 8192. Run 'climate.apply' after setting.")]
    public static string VoronoiSeeds(int? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.VoronoiSeedCount = Mathf.Clamp(value.Value, 128, 8192);
        return $"Voronoi seeds: {settings.VoronoiSeedCount}";
    }

    [ConsoleCommand("voronoi-warp", "Get or set unit-sphere domain-warp strength from 0 to 0.25. Run 'climate.apply' after setting.")]
    public static string VoronoiWarp(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.VoronoiDomainWarpStrength = Mathf.Clamp(value.Value, 0f, 0.25f);
        return $"Voronoi warp strength: {settings.VoronoiDomainWarpStrength:F3}";
    }

    [ConsoleCommand("voronoi-jitter", "Get or set Fibonacci seed jitter from 0 to 1. Run 'climate.apply' after setting.")]
    public static string VoronoiJitter(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings() : GetSettings();
        if (value.HasValue)
            settings.VoronoiSeedJitter = Mathf.Clamp01(value.Value);
        return $"Voronoi seed jitter: {settings.VoronoiSeedJitter:F3}";
    }

    [ConsoleCommand("temperature-point", "Set a normalized temperature curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string TemperaturePoint(float latitude01, float value01)
    {
        BiomeSettings settings = GetEditableSettings();
        settings.SetTemperatureLatitudePoint(latitude01, value01);
        return $"temperature curve point set at {Mathf.Clamp01(latitude01):F2}. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("moisture-point", "Set a normalized moisture curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string MoisturePoint(float latitude01, float value01)
    {
        BiomeSettings settings = GetEditableSettings();
        settings.SetMoistureLatitudePoint(latitude01, value01);
        return $"moisture curve point set at {Mathf.Clamp01(latitude01):F2}. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("apply", "Regenerate the planet with the current climate settings (async, cancellable).")]
    public static async Awaitable Apply(CancellationToken ct = default)
    {
        Planet planet = GetPlanet();
        if (planet.IsGenerating)
            throw new InvalidOperationException("planet generation already in progress");
        await planet.GeneratePlanetAsync(ct);
    }

    static BiomeSettings GetEditableSettings()
    {
        BiomeSettings settings = GetSettings();
        if (GetPlanet().IsGenerating)
            throw new InvalidOperationException("climate settings cannot change while planet generation is in progress");
        return settings;
    }

    static BiomeSettings GetSettings()
    {
        BiomeSettings settings = GetPlanet().PlanetSettingsAsset?.BiomeSettings;
        if (settings == null)
            throw new InvalidOperationException("active planet has no BiomeSettings assigned");
        settings.EnsureClimateCurves();
        return settings;
    }

    static Planet GetPlanet()
    {
        Planet planet = UnityEngine.Object.FindAnyObjectByType<Planet>(FindObjectsInactive.Exclude);
        if (planet == null)
            throw new InvalidOperationException("no active Planet was found");
        return planet;
    }
}
