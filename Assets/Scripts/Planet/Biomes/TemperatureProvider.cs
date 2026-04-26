using UnityEngine;

public class TemperatureProvider : ITemperatureProvider
{
    NoiseSettings _noiseSettings;
    INoiseFilter _noiseFilter;
    float _noiseStrength;

    public TemperatureProvider(NoiseSettings noiseSettings, float noiseStrength = 0.15f)
    {
        _noiseSettings = noiseSettings;
        _noiseStrength = noiseStrength;
    }

    public void Initialize(int seed)
    {
        _noiseFilter = NoiseFilterFactory.CreateNoiseFilter(_noiseSettings, seed);
    }

    public float Evaluate(Vector3 pointOnUnitSphere)
    {
        // Base temperature from latitude: 1 at equator, 0 at poles
        float absLatitude = CoordinateConverter.NormalizedLatitude(pointOnUnitSphere);
        float baseTemp = 1f - absLatitude;

        // Noise perturbation for organic variation
        float noise = _noiseFilter.Evaluate(pointOnUnitSphere) * _noiseStrength;

        return Mathf.Clamp01(baseTemp + noise);
    }
}
