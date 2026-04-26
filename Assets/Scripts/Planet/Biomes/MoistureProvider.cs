using UnityEngine;

public class MoistureProvider : IMoistureProvider
{
    NoiseSettings _noiseSettings;
    INoiseFilter _noiseFilter;
    float _maxValue;

    public MoistureProvider(NoiseSettings noiseSettings)
    {
        _noiseSettings = noiseSettings;
    }

    public void Initialize(int seed)
    {
        _noiseFilter = NoiseFilterFactory.CreateNoiseFilter(_noiseSettings, seed);

        // Compute theoretical max output of SimpleNoiseFilter
        // Each layer outputs 0-1 (after (v+1)*0.5), scaled by amplitude
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
        float raw = _noiseFilter.Evaluate(pointOnUnitSphere);
        return Mathf.Clamp01(raw / _maxValue);
    }
}
