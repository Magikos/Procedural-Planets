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

        // Noise perturbation centered around 0 for organic variation
        // SimpleNoiseFilter outputs ~0 to ~1.87, center it by subtracting ~0.9
        float noise = (_noiseFilter.Evaluate(pointOnUnitSphere) - 0.9f) * _noiseStrength;

        return Mathf.Clamp01(baseTemp + noise);
    }
}
