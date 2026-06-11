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
        EnsureNotGenerating();
        BiomeDto updated = ApplyPreset(GetSettings(), preset);
        SettingsProvider.Update(updated);
        return $"climate preset {preset} applied. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("altitude-lapse", "Get or set normalized altitude temperature drop. Run 'climate.apply' after setting.")]
    public static string AltitudeLapse(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { AltitudeTemperatureDrop = Mathf.Clamp(value.Value, 0f, 10f) };
            SettingsProvider.Update(biome);
        }
        return $"altitude temperature drop: {biome.AltitudeTemperatureDrop:F3}";
    }

    [ConsoleCommand("moisture-bands", "Get or set latitude-band influence from 0 to 1. Run 'climate.apply' after setting.")]
    public static string MoistureBands(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { MoistureLatitudeInfluence = Mathf.Clamp01(value.Value) };
            SettingsProvider.Update(biome);
        }
        return $"moisture latitude influence: {biome.MoistureLatitudeInfluence:F3}";
    }

    [ConsoleCommand("moisture-noise", "Get or set latitude-band moisture noise strength from 0 to 1. Has no effect while moisture-bands is 0. Run 'climate.apply' after setting.")]
    public static string MoistureNoise(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { MoistureNoiseStrength = Mathf.Clamp01(value.Value) };
            SettingsProvider.Update(biome);
        }
        return $"moisture band noise strength: {biome.MoistureNoiseStrength:F3}";
    }

    [ConsoleCommand("temperature-noise", "Get or set centered temperature noise strength from 0 to 0.5. Run 'climate.apply' after setting.")]
    public static string TemperatureNoise(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { TemperatureNoiseStrength = Mathf.Clamp(value.Value, 0f, 0.5f) };
            SettingsProvider.Update(biome);
        }
        return $"temperature noise strength: {biome.TemperatureNoiseStrength:F3}";
    }

    [ConsoleCommand("lut-resolution", "Get or set climate curve LUT resolution from 16 to 512. Run 'climate.apply' after setting.")]
    public static string LutResolution(int? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { ClimateLutResolution = Mathf.Clamp(value.Value, 16, 512) };
            SettingsProvider.Update(biome);
        }
        return $"climate LUT resolution: {biome.ClimateLutResolution}";
    }

    [ConsoleCommand("map-resolution", "Get or set GPU climate map resolution per cube face from 32 to 512. Run 'climate.apply' after setting.")]
    public static string MapResolution(int? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { ClimateMapResolution = Mathf.Clamp(value.Value, 32, 512) };
            SettingsProvider.Update(biome);
        }
        return $"climate map resolution: {biome.ClimateMapResolution} per face";
    }

    [ConsoleCommand("temperature-range", "Get or set the normalized climate range in Celsius: minimum maximum. Run 'climate.apply' after setting.")]
    public static string TemperatureRange(float? minimumCelsius = null, float? maximumCelsius = null)
    {
        BiomeDto biome = GetSettings();
        if (minimumCelsius.HasValue || maximumCelsius.HasValue)
        {
            EnsureNotGenerating();
            float minC = minimumCelsius.HasValue
                ? Mathf.Clamp(minimumCelsius.Value, -100f, 50f)
                : biome.MinimumTemperatureCelsius;
            float maxC = maximumCelsius.HasValue
                ? Mathf.Clamp(maximumCelsius.Value, minC + 1f, 100f)
                : biome.MaximumTemperatureCelsius;
            maxC = Mathf.Max(maxC, minC + 1f);
            biome = biome with
            {
                MinimumTemperatureCelsius = minC,
                MaximumTemperatureCelsius = maxC,
            };
            SettingsProvider.Update(biome);
        }
        return $"temperature range: {biome.MinimumTemperatureCelsius:F1} C to " +
               $"{biome.MaximumTemperatureCelsius:F1} C";
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
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { VoronoiSeedCount = Mathf.Clamp(value.Value, 128, 8192) };
            SettingsProvider.Update(biome);
        }
        return $"Voronoi seeds: {biome.VoronoiSeedCount}";
    }

    [ConsoleCommand("voronoi-warp", "Get or set unit-sphere domain-warp strength from 0 to 0.25. Run 'climate.apply' after setting.")]
    public static string VoronoiWarp(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { VoronoiDomainWarpStrength = Mathf.Clamp(value.Value, 0f, 0.25f) };
            SettingsProvider.Update(biome);
        }
        return $"Voronoi warp strength: {biome.VoronoiDomainWarpStrength:F3}";
    }

    [ConsoleCommand("voronoi-jitter", "Get or set Fibonacci seed jitter from 0 to 1. Run 'climate.apply' after setting.")]
    public static string VoronoiJitter(float? value = null)
    {
        BiomeDto biome = GetSettings();
        if (value.HasValue)
        {
            EnsureNotGenerating();
            biome = biome with { VoronoiSeedJitter = Mathf.Clamp01(value.Value) };
            SettingsProvider.Update(biome);
        }
        return $"Voronoi seed jitter: {biome.VoronoiSeedJitter:F3}";
    }

    [ConsoleCommand("temperature-point", "Set a normalized temperature curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string TemperaturePoint(float latitude01, float value01)
    {
        EnsureNotGenerating();
        BiomeDto biome = GetSettings();
        AnimationCurve next = ClimateCurves.WithLinearPoint(biome.TemperatureLatitudeCurve, latitude01, value01);
        biome = biome with { TemperatureLatitudeCurve = next };
        SettingsProvider.Update(biome);
        return $"temperature curve point set at {Mathf.Clamp01(latitude01):F2}. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("moisture-point", "Set a normalized moisture curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string MoisturePoint(float latitude01, float value01)
    {
        EnsureNotGenerating();
        BiomeDto biome = GetSettings();
        AnimationCurve next = ClimateCurves.WithLinearPoint(biome.MoistureLatitudeCurve, latitude01, value01);
        biome = biome with { MoistureLatitudeCurve = next };
        SettingsProvider.Update(biome);
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

    static BiomeDto ApplyPreset(BiomeDto biome, ClimateLatitudePreset preset)
    {
        return preset switch
        {
            ClimateLatitudePreset.Legacy => biome with
            {
                TemperatureLatitudeCurve = ClimateCurves.DefaultTemperature(),
                MoistureLatitudeCurve = ClimateCurves.EarthlikeMoisture(),
                MoistureLatitudeInfluence = 0f,
                MoistureNoiseStrength = 0.35f,
                AltitudeTemperatureDrop = 0f,
            },
            ClimateLatitudePreset.Earthlike => biome with
            {
                TemperatureLatitudeCurve = ClimateCurves.CreateLinear(
                    new Vector2(0f, 1f),
                    new Vector2(0.18f, 0.93f),
                    new Vector2(0.42f, 0.72f),
                    new Vector2(0.68f, 0.38f),
                    new Vector2(1f, 0f)),
                MoistureLatitudeCurve = ClimateCurves.EarthlikeMoisture(),
                MoistureLatitudeInfluence = 0.70f,
                MoistureNoiseStrength = 0.32f,
                AltitudeTemperatureDrop = 2.5f,
            },
            ClimateLatitudePreset.StrongBands => biome with
            {
                TemperatureLatitudeCurve = ClimateCurves.CreateLinear(
                    new Vector2(0f, 1f),
                    new Vector2(0.16f, 0.96f),
                    new Vector2(0.40f, 0.72f),
                    new Vector2(0.66f, 0.34f),
                    new Vector2(1f, 0f)),
                MoistureLatitudeCurve = ClimateCurves.EarthlikeMoisture(),
                MoistureLatitudeInfluence = 1f,
                MoistureNoiseStrength = 0.22f,
                AltitudeTemperatureDrop = 4f,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    static BiomeDto GetSettings()
    {
        if (!SettingsProvider.IsRegistered<BiomeDto>())
            throw new InvalidOperationException("active planet has no BiomeSettings assigned");
        return SettingsProvider.GetSettings<BiomeDto>();
    }

    static void EnsureNotGenerating()
    {
        if (GetPlanet().IsGenerating)
            throw new InvalidOperationException("climate settings cannot change while planet generation is in progress");
    }

    static Planet GetPlanet()
    {
        Planet planet = UnityEngine.Object.FindAnyObjectByType<Planet>(FindObjectsInactive.Exclude);
        if (planet == null)
            throw new InvalidOperationException("no active Planet was found");
        return planet;
    }
}
