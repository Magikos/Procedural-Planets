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
        // SimpleNoiseFilter already outputs ~0 to ~1.87 (positive-biased)
        // Normalize to 0-1 by dividing by approximate max
        float raw = _noiseFilter.Evaluate(pointOnUnitSphere);
        return Mathf.Clamp01(raw);
    }
}
