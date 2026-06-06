using System;
using System.Text;
using System.Threading;
using UnityEngine;

public enum ClimateLatitudePreset
{
    Legacy,
    Earthlike,
    StrongBands,
}

public enum BiomeAssignmentMode
{
    DirectClimateGrid,
    Voronoi,
}

[CreateAssetMenu(menuName = "Planet/Settings/Biome Settings")]
public class BiomeSettings : ScriptableObject
{
    public BiomeRegistry Registry;

    [Header("Latitude Climate")]
    [Tooltip("Base normalized temperature by angular latitude: 0 = equator, 1 = pole.")]
    public AnimationCurve TemperatureLatitudeCurve = CreateLinearCurve(
        new Vector2(0f, 1f),
        new Vector2(1f, 0f));

    [Tooltip("Base normalized moisture by angular latitude: 0 = equator, 1 = pole.")]
    public AnimationCurve MoistureLatitudeCurve = CreateLinearCurve(
        new Vector2(0f, 0.90f),
        new Vector2(0.18f, 0.30f),
        new Vector2(0.42f, 0.68f),
        new Vector2(0.70f, 0.42f),
        new Vector2(1f, 0.18f));

    [Range(0f, 1f), Tooltip("0 preserves the legacy noise-only moisture field; 1 uses latitude bands plus centered noise.")]
    public float MoistureLatitudeInfluence;

    [Range(0f, 1f), Tooltip("Maximum centered noise contribution used by the latitude-band moisture model.")]
    public float MoistureNoiseStrength = 0.35f;

    [Range(0f, 10f), Tooltip("Normalized temperature removed per unit of land elevation above the biome registry ocean threshold.")]
    public float AltitudeTemperatureDrop;

    [Range(16, 512), Tooltip("Samples baked from each climate curve before worker-thread terrain generation.")]
    public int ClimateLutResolution = 256;

    [Header("Temperature Noise")]
    public NoiseSettings TemperatureNoise;
    [Range(0f, 0.5f)] public float TemperatureNoiseStrength = 0.15f;

    [Header("Moisture Noise")]
    public NoiseSettings MoistureNoise;

    [Header("Voronoi Assignment")]
    [Tooltip("Voronoi is the validated default. DirectClimateGrid preserves the legacy resolver for regression comparisons.")]
    public BiomeAssignmentMode AssignmentMode = BiomeAssignmentMode.Voronoi;

    [Range(128, 8192)]
    public int VoronoiSeedCount = 2048;

    [Range(0f, 1f), Tooltip("Tangent-space displacement relative to the average Fibonacci seed spacing.")]
    public float VoronoiSeedJitter = 0.55f;

    [Range(0.1f, 10f), Tooltip("Climate-space temperature distance multiplier used when assigning seed biomes.")]
    public float VoronoiTemperatureWeight = 4.26f;

    [Range(0f, 0.25f), Tooltip("Unit-sphere displacement applied before nearest-seed lookup.")]
    public float VoronoiDomainWarpStrength = 0.08f;

    [Range(0.1f, 16f), Tooltip("Domain-warp noise frequency on the unit sphere.")]
    public float VoronoiDomainWarpScale = 2.5f;

    [Range(1, 6)]
    public int VoronoiDomainWarpOctaves = 4;

    [Range(0.1f, 0.9f)]
    public float VoronoiDomainWarpPersistence = 0.418f;

    [Range(1.1f, 4f)]
    public float VoronoiDomainWarpLacunarity = 2.73f;

    [Range(0, 8)]
    public int VoronoiCleanupIterations = 5;

    public void EnsureClimateCurves()
    {
        if (TemperatureLatitudeCurve == null || TemperatureLatitudeCurve.length < 2)
        {
            TemperatureLatitudeCurve = CreateLinearCurve(
                new Vector2(0f, 1f),
                new Vector2(1f, 0f));
        }

        if (MoistureLatitudeCurve == null || MoistureLatitudeCurve.length < 2)
            MoistureLatitudeCurve = CreateEarthlikeMoistureCurve();

        ClimateLutResolution = Mathf.Clamp(ClimateLutResolution, 16, 512);
        MoistureLatitudeInfluence = Mathf.Clamp01(MoistureLatitudeInfluence);
        MoistureNoiseStrength = Mathf.Clamp01(MoistureNoiseStrength);
        AltitudeTemperatureDrop = Mathf.Clamp(AltitudeTemperatureDrop, 0f, 10f);
        VoronoiSeedCount = Mathf.Clamp(VoronoiSeedCount, 128, 8192);
        VoronoiSeedJitter = Mathf.Clamp01(VoronoiSeedJitter);
        VoronoiTemperatureWeight = Mathf.Clamp(VoronoiTemperatureWeight, 0.1f, 10f);
        VoronoiDomainWarpStrength = Mathf.Clamp(VoronoiDomainWarpStrength, 0f, 0.25f);
        VoronoiDomainWarpScale = Mathf.Clamp(VoronoiDomainWarpScale, 0.1f, 16f);
        VoronoiDomainWarpOctaves = Mathf.Clamp(VoronoiDomainWarpOctaves, 1, 6);
        VoronoiDomainWarpPersistence = Mathf.Clamp(VoronoiDomainWarpPersistence, 0.1f, 0.9f);
        VoronoiDomainWarpLacunarity = Mathf.Clamp(VoronoiDomainWarpLacunarity, 1.1f, 4f);
        VoronoiCleanupIterations = Mathf.Clamp(VoronoiCleanupIterations, 0, 8);
    }

    public void ApplyPreset(ClimateLatitudePreset preset)
    {
        switch (preset)
        {
            case ClimateLatitudePreset.Legacy:
                TemperatureLatitudeCurve = CreateLinearCurve(
                    new Vector2(0f, 1f),
                    new Vector2(1f, 0f));
                MoistureLatitudeCurve = CreateEarthlikeMoistureCurve();
                MoistureLatitudeInfluence = 0f;
                MoistureNoiseStrength = 0.35f;
                AltitudeTemperatureDrop = 0f;
                break;

            case ClimateLatitudePreset.Earthlike:
                TemperatureLatitudeCurve = CreateLinearCurve(
                    new Vector2(0f, 1f),
                    new Vector2(0.18f, 0.93f),
                    new Vector2(0.42f, 0.72f),
                    new Vector2(0.68f, 0.38f),
                    new Vector2(1f, 0f));
                MoistureLatitudeCurve = CreateEarthlikeMoistureCurve();
                MoistureLatitudeInfluence = 0.70f;
                MoistureNoiseStrength = 0.32f;
                AltitudeTemperatureDrop = 2.5f;
                break;

            case ClimateLatitudePreset.StrongBands:
                TemperatureLatitudeCurve = CreateLinearCurve(
                    new Vector2(0f, 1f),
                    new Vector2(0.16f, 0.96f),
                    new Vector2(0.40f, 0.72f),
                    new Vector2(0.66f, 0.34f),
                    new Vector2(1f, 0f));
                MoistureLatitudeCurve = CreateEarthlikeMoistureCurve();
                MoistureLatitudeInfluence = 1f;
                MoistureNoiseStrength = 0.22f;
                AltitudeTemperatureDrop = 4f;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
        }

        EnsureClimateCurves();
    }

    public void SetTemperatureLatitudePoint(float latitude01, float value01)
    {
        TemperatureLatitudeCurve = WithLinearPoint(
            TemperatureLatitudeCurve, latitude01, value01);
    }

    public void SetMoistureLatitudePoint(float latitude01, float value01)
    {
        MoistureLatitudeCurve = WithLinearPoint(
            MoistureLatitudeCurve, latitude01, value01);
    }

    public string Describe()
    {
        EnsureClimateCurves();
        var sb = new StringBuilder();
        sb.Append("climate: tempNoise=").Append(TemperatureNoiseStrength.ToString("F3"));
        sb.Append(", moistureBands=").Append(MoistureLatitudeInfluence.ToString("F2"));
        sb.Append(", moistureNoise=").Append(MoistureNoiseStrength.ToString("F2"));
        sb.Append(", altitudeLapse=").Append(AltitudeTemperatureDrop.ToString("F2"));
        sb.Append(", lut=").Append(ClimateLutResolution);
        sb.AppendLine();
        sb.Append("temperatureCurve=").Append(DescribeCurve(TemperatureLatitudeCurve));
        sb.AppendLine();
        sb.Append("moistureCurve=").Append(DescribeCurve(MoistureLatitudeCurve));
        sb.AppendLine();
        sb.Append("assignment=").Append(AssignmentMode);
        sb.Append(", seeds=").Append(VoronoiSeedCount);
        sb.Append(", jitter=").Append(VoronoiSeedJitter.ToString("F2"));
        sb.Append(", tempWeight=").Append(VoronoiTemperatureWeight.ToString("F2"));
        sb.Append(", warp=").Append(VoronoiDomainWarpStrength.ToString("F3"));
        sb.Append('@').Append(VoronoiDomainWarpScale.ToString("F2"));
        sb.Append(", octaves=").Append(VoronoiDomainWarpOctaves);
        sb.Append(", cleanup=").Append(VoronoiCleanupIterations);
        return sb.ToString();
    }

    void OnEnable()
    {
        EnsureClimateCurves();
    }

    void OnValidate()
    {
        EnsureClimateCurves();
    }

    static AnimationCurve CreateEarthlikeMoistureCurve()
    {
        return CreateLinearCurve(
            new Vector2(0f, 0.90f),
            new Vector2(0.18f, 0.30f),
            new Vector2(0.42f, 0.68f),
            new Vector2(0.70f, 0.42f),
            new Vector2(1f, 0.18f));
    }

    static AnimationCurve WithLinearPoint(AnimationCurve source, float latitude01, float value01)
    {
        latitude01 = Mathf.Clamp01(latitude01);
        value01 = Mathf.Clamp01(value01);

        var points = new System.Collections.Generic.List<Vector2>();
        if (source != null)
        {
            Keyframe[] keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - latitude01) > 0.0001f)
                    points.Add(new Vector2(keys[i].time, keys[i].value));
            }
        }

        points.Add(new Vector2(latitude01, value01));
        points.Sort((a, b) => a.x.CompareTo(b.x));
        return CreateLinearCurve(points.ToArray());
    }

    static AnimationCurve CreateLinearCurve(params Vector2[] points)
    {
        if (points == null || points.Length == 0)
            points = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) };

        var keys = new Keyframe[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float inTangent = i > 0 ? Slope(points[i - 1], points[i]) : 0f;
            float outTangent = i + 1 < points.Length ? Slope(points[i], points[i + 1]) : 0f;
            keys[i] = new Keyframe(points[i].x, points[i].y, inTangent, outTangent);
        }

        return new AnimationCurve(keys);
    }

    static float Slope(Vector2 a, Vector2 b)
    {
        float dt = b.x - a.x;
        return Mathf.Abs(dt) > 0.000001f ? (b.y - a.y) / dt : 0f;
    }

    static string DescribeCurve(AnimationCurve curve)
    {
        if (curve == null)
            return "<missing>";

        Keyframe[] keys = curve.keys;
        var sb = new StringBuilder();
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(')
                .Append(keys[i].time.ToString("F2"))
                .Append(':')
                .Append(keys[i].value.ToString("F2"))
                .Append(')');
        }
        return sb.ToString();
    }
}

[CommandPrefix("climate")]
public static class ClimateCommands
{
    [ConsoleCommand("status", "Show active latitude curves and climate contribution settings.")]
    public static string Status()
    {
        return GetSettings(out _).Describe();
    }

    [ConsoleCommand("preset", "Apply a climate latitude preset. Run 'climate.apply' to regenerate.")]
    public static string Preset(ClimateLatitudePreset preset)
    {
        BiomeSettings settings = GetEditableSettings(out _);
        settings.ApplyPreset(preset);
        return $"climate preset {preset} applied. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("altitude-lapse", "Get or set normalized altitude temperature drop. Run 'climate.apply' after setting.")]
    public static string AltitudeLapse(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.AltitudeTemperatureDrop = Mathf.Clamp(value.Value, 0f, 10f);
        return $"altitude temperature drop: {settings.AltitudeTemperatureDrop:F3}";
    }

    [ConsoleCommand("moisture-bands", "Get or set latitude-band influence from 0 to 1. Run 'climate.apply' after setting.")]
    public static string MoistureBands(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.MoistureLatitudeInfluence = Mathf.Clamp01(value.Value);
        return $"moisture latitude influence: {settings.MoistureLatitudeInfluence:F3}";
    }

    [ConsoleCommand("moisture-noise", "Get or set latitude-band moisture noise strength from 0 to 1. Run 'climate.apply' after setting.")]
    public static string MoistureNoise(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.MoistureNoiseStrength = Mathf.Clamp01(value.Value);
        return $"moisture band noise strength: {settings.MoistureNoiseStrength:F3}";
    }

    [ConsoleCommand("temperature-noise", "Get or set centered temperature noise strength from 0 to 0.5. Run 'climate.apply' after setting.")]
    public static string TemperatureNoise(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.TemperatureNoiseStrength = Mathf.Clamp(value.Value, 0f, 0.5f);
        return $"temperature noise strength: {settings.TemperatureNoiseStrength:F3}";
    }

    [ConsoleCommand("lut-resolution", "Get or set climate curve LUT resolution from 16 to 512. Run 'climate.apply' after setting.")]
    public static string LutResolution(int? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.ClimateLutResolution = Mathf.Clamp(value.Value, 16, 512);
        return $"climate LUT resolution: {settings.ClimateLutResolution}";
    }

    [ConsoleCommand("assignment", "Get or set biome assignment mode. Run 'climate.apply' after setting.")]
    public static string Assignment(BiomeAssignmentMode? mode = null)
    {
        BiomeSettings settings = mode.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (mode.HasValue)
            settings.AssignmentMode = mode.Value;
        return $"biome assignment: {settings.AssignmentMode}";
    }

    [ConsoleCommand("voronoi-seeds", "Get or set global Voronoi seed count from 128 to 8192. Run 'climate.apply' after setting.")]
    public static string VoronoiSeeds(int? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.VoronoiSeedCount = Mathf.Clamp(value.Value, 128, 8192);
        return $"Voronoi seeds: {settings.VoronoiSeedCount}";
    }

    [ConsoleCommand("voronoi-warp", "Get or set unit-sphere domain-warp strength from 0 to 0.25. Run 'climate.apply' after setting.")]
    public static string VoronoiWarp(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.VoronoiDomainWarpStrength = Mathf.Clamp(value.Value, 0f, 0.25f);
        return $"Voronoi warp strength: {settings.VoronoiDomainWarpStrength:F3}";
    }

    [ConsoleCommand("voronoi-jitter", "Get or set Fibonacci seed jitter from 0 to 1. Run 'climate.apply' after setting.")]
    public static string VoronoiJitter(float? value = null)
    {
        BiomeSettings settings = value.HasValue ? GetEditableSettings(out _) : GetSettings(out _);
        if (value.HasValue)
            settings.VoronoiSeedJitter = Mathf.Clamp01(value.Value);
        return $"Voronoi seed jitter: {settings.VoronoiSeedJitter:F3}";
    }

    [ConsoleCommand("temperature-point", "Set a normalized temperature curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string TemperaturePoint(float latitude01, float value01)
    {
        BiomeSettings settings = GetEditableSettings(out _);
        settings.SetTemperatureLatitudePoint(latitude01, value01);
        return $"temperature curve point set at {Mathf.Clamp01(latitude01):F2}. Run 'climate.apply' to regenerate.";
    }

    [ConsoleCommand("moisture-point", "Set a normalized moisture curve point: latitude value. Run 'climate.apply' after setting.")]
    public static string MoisturePoint(float latitude01, float value01)
    {
        BiomeSettings settings = GetEditableSettings(out _);
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

    static BiomeSettings GetEditableSettings(out Planet planet)
    {
        BiomeSettings settings = GetSettings(out planet);
        if (planet.IsGenerating)
            throw new InvalidOperationException("climate settings cannot change while planet generation is in progress");
        return settings;
    }

    static BiomeSettings GetSettings(out Planet planet)
    {
        planet = GetPlanet();
        BiomeSettings settings = planet.PlanetSettingsAsset?.BiomeSettings;
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
