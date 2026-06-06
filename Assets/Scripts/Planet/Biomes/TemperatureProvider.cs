using UnityEngine;

public class TemperatureProvider : ITemperatureProvider
{
    NoiseSettings _noiseSettings;
    INoiseFilter _noiseFilter;
    float _noiseStrength;
    float _maxValue;

    public TemperatureProvider(NoiseSettings noiseSettings, float noiseStrength = 0.15f)
    {
        _noiseSettings = noiseSettings;
        _noiseStrength = noiseStrength;
    }

    public void Initialize(int seed)
    {
        _noiseFilter = NoiseFilterFactory.CreateNoiseFilter(_noiseSettings, seed);

        float amp = 1f;
        _maxValue = 0f;
        for (int i = 0; i < _noiseSettings.Layers; i++)
        {
            _maxValue += amp;
            amp *= _noiseSettings.Persistence;
        }
        _maxValue *= _noiseSettings.Strength;
        if (_maxValue < 0.001f) _maxValue = 1f;
    }

    public float Evaluate(Vector3 pointOnUnitSphere)
    {
        // Base temperature from latitude: 1 at equator, 0 at poles
        float absLatitude = CoordinateConverter.NormalizedLatitude(pointOnUnitSphere);
        float baseTemp = 1f - absLatitude;

        // Normalize noise to 0-1, then center around 0 (-0.5 to +0.5)
        float normalized = _noiseFilter.Evaluate(pointOnUnitSphere) / _maxValue;
        float noise = (normalized - 0.5f) * _noiseStrength;

        return Mathf.Clamp01(baseTemp + noise);
    }
}

public sealed class ClimateProvider : IClimateProvider
{
    readonly ITemperatureProvider _temperatureProvider;
    readonly IMoistureProvider _moistureProvider;

    public ClimateProvider(
        NoiseSettings temperatureNoise,
        float temperatureNoiseStrength,
        NoiseSettings moistureNoise)
    {
        _temperatureProvider = new TemperatureProvider(
            temperatureNoise,
            temperatureNoiseStrength);
        _moistureProvider = new MoistureProvider(moistureNoise);
    }

    public void Initialize(int seed)
    {
        _temperatureProvider.Initialize(seed);
        _moistureProvider.Initialize(seed + 100);
    }

    public ClimateSample Evaluate(Vector3 pointOnUnitSphere, float elevation)
    {
        return new ClimateSample(
            _temperatureProvider.Evaluate(pointOnUnitSphere),
            _moistureProvider.Evaluate(pointOnUnitSphere),
            elevation);
    }
}
