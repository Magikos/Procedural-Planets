using UnityEngine;

public sealed class ClimateProvider : IClimateProvider
{
    readonly TemperatureProvider _temperatureProvider;
    readonly MoistureProvider _moistureProvider;
    readonly float _minimumTemperatureCelsius;
    readonly float _maximumTemperatureCelsius;

    public ClimateProvider(BiomeDto biome)
    {
        int resolution = biome.ClimateLutResolution;
        ClimateCurveLut temperatureCurve = ClimateCurveLut.Bake(
            biome.TemperatureLatitudeCurve, resolution);
        ClimateCurveLut moistureCurve = ClimateCurveLut.Bake(
            biome.MoistureLatitudeCurve, resolution);
        float waterLevel = BiomeConstants.OceanThreshold;

        _temperatureProvider = new TemperatureProvider(
            BiomeConstants.TemperatureNoise,
            temperatureCurve,
            biome.TemperatureNoiseStrength,
            biome.AltitudeTemperatureDrop,
            waterLevel);
        _moistureProvider = new MoistureProvider(
            BiomeConstants.MoistureNoise,
            moistureCurve,
            biome.MoistureLatitudeInfluence,
            biome.MoistureNoiseStrength);
        _minimumTemperatureCelsius = biome.MinimumTemperatureCelsius;
        _maximumTemperatureCelsius = biome.MaximumTemperatureCelsius;
    }

    public void Initialize(int seed)
    {
        _temperatureProvider.Initialize(seed);
        _moistureProvider.Initialize(seed + 100);
    }

    public ClimateSample Evaluate(Vector3 pointOnUnitSphere, float elevation)
    {
        TemperatureClimateSample temperature = _temperatureProvider.EvaluateClimate(
            pointOnUnitSphere, elevation);
        MoistureClimateSample moisture = _moistureProvider.EvaluateClimate(
            pointOnUnitSphere, temperature.Latitude01);

        return new ClimateSample(
            temperature.FinalTemperature01,
            TemperatureUnits.NormalizedToCelsius(
                temperature.FinalTemperature01,
                _minimumTemperatureCelsius,
                _maximumTemperatureCelsius),
            moisture.FinalMoisture01,
            elevation,
            temperature.Latitude01,
            temperature.LatitudeTemperature01,
            temperature.NoiseContribution,
            temperature.AltitudeDrop,
            moisture.LatitudeMoisture01,
            moisture.NoiseContribution,
            moisture.LegacyMoisture01);
    }
}
