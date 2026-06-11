using System;
using System.Text;
using UnityEngine;

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

    [Range(0f, 1f), Tooltip("Maximum centered noise contribution used by the latitude-band moisture model. Has no effect while MoistureLatitudeInfluence is 0 (the legacy noise-only path bypasses this).")]
    public float MoistureNoiseStrength = 0.35f;

    [Range(0f, 10f), Tooltip("Normalized temperature removed per unit of land elevation above the biome registry ocean threshold.")]
    public float AltitudeTemperatureDrop;

    [Range(16, 512), Tooltip("Samples baked from each climate curve before worker-thread terrain generation.")]
    public int ClimateLutResolution = 256;

    [Header("Physical Temperature")]
    [Range(-100f, 50f), Tooltip("Celsius value represented by normalized temperature 0.")]
    public float MinimumTemperatureCelsius = TemperatureUnits.DefaultMinimumCelsius;

    [Range(-50f, 100f), Tooltip("Celsius value represented by normalized temperature 1.")]
    public float MaximumTemperatureCelsius = TemperatureUnits.DefaultMaximumCelsius;

    [Range(32, 512), Tooltip("Resolution per face of the GPU temperature/moisture map.")]
    public int ClimateMapResolution = 256;

    [Header("Temperature Noise")]
    [Range(0f, 0.5f)] public float TemperatureNoiseStrength = 0.15f;

    [Header("Voronoi Assignment")]
    [Range(128, 8192)]
    public int VoronoiSeedCount = 2048;

    [Range(0f, 1f), Tooltip("Tangent-space displacement relative to the average Fibonacci seed spacing.")]
    public float VoronoiSeedJitter = 0.55f;

    [Range(0f, 0.25f), Tooltip("Unit-sphere displacement applied before nearest-seed lookup.")]
    public float VoronoiDomainWarpStrength = 0.08f;

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
        MinimumTemperatureCelsius = Mathf.Clamp(MinimumTemperatureCelsius, -100f, 50f);
        MaximumTemperatureCelsius = Mathf.Clamp(
            MaximumTemperatureCelsius,
            MinimumTemperatureCelsius + 1f,
            100f);
        ClimateMapResolution = Mathf.Clamp(ClimateMapResolution, 32, 512);
        MoistureLatitudeInfluence = Mathf.Clamp01(MoistureLatitudeInfluence);
        MoistureNoiseStrength = Mathf.Clamp01(MoistureNoiseStrength);
        AltitudeTemperatureDrop = Mathf.Clamp(AltitudeTemperatureDrop, 0f, 10f);
        VoronoiSeedCount = Mathf.Clamp(VoronoiSeedCount, 128, 8192);
        VoronoiSeedJitter = Mathf.Clamp01(VoronoiSeedJitter);
        VoronoiDomainWarpStrength = Mathf.Clamp(VoronoiDomainWarpStrength, 0f, 0.25f);
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
        sb.Append(", map=").Append(ClimateMapResolution);
        sb.Append(", celsius=")
            .Append(MinimumTemperatureCelsius.ToString("F1"))
            .Append("..")
            .Append(MaximumTemperatureCelsius.ToString("F1"));
        sb.AppendLine();
        sb.Append("temperatureCurve=").Append(DescribeCurve(TemperatureLatitudeCurve));
        sb.AppendLine();
        sb.Append("moistureCurve=").Append(DescribeCurve(MoistureLatitudeCurve));
        sb.AppendLine();
        sb.Append("seeds=").Append(VoronoiSeedCount);
        sb.Append(", jitter=").Append(VoronoiSeedJitter.ToString("F2"));
        sb.Append(", warp=").Append(VoronoiDomainWarpStrength.ToString("F3"));
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
