using UnityEngine;

public class MoistureProvider : IMoistureProvider
{
    NoiseSettings _noiseSettings;
    INoiseFilter _noiseFilter;

    public MoistureProvider(NoiseSettings noiseSettings)
    {
        _noiseSettings = noiseSettings;
    }

    public void Initialize(int seed)
    {
        _noiseFilter = NoiseFilterFactory.CreateNoiseFilter(_noiseSettings, seed);
    }

    public float Evaluate(Vector3 pointOnUnitSphere)
    {
        // Raw noise centered around 0, shift to 0-1 range
        float raw = _noiseFilter.Evaluate(pointOnUnitSphere);
        return Mathf.Clamp01(raw * 0.5f + 0.5f);
    }
}
