using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Biome Settings")]
public class BiomeSettings : ScriptableObject
{
    public BiomeRegistry Registry;

    [Header("Latitude Climate")]
    [Tooltip("Base normalized temperature by angular latitude: 0 = equator, 1 = pole.")]
    public AnimationCurve TemperatureLatitudeCurve = ClimateCurves.DefaultTemperature();

    [Tooltip("Base normalized moisture by angular latitude: 0 = equator, 1 = pole.")]
    public AnimationCurve MoistureLatitudeCurve = ClimateCurves.EarthlikeMoisture();

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
            TemperatureLatitudeCurve = ClimateCurves.DefaultTemperature();

        if (MoistureLatitudeCurve == null || MoistureLatitudeCurve.length < 2)
            MoistureLatitudeCurve = ClimateCurves.EarthlikeMoisture();

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

    void OnEnable()
    {
        EnsureClimateCurves();
    }

    void OnValidate()
    {
        EnsureClimateCurves();
    }
}
