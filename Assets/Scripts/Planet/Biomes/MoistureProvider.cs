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
        // Normalize to 0-1, then stretch to use full range
        // The noise averages around maxValue/2, so remap:
        // 0 → 0, maxValue/2 → 0.5, maxValue → 1
        float normalized = raw / _maxValue;
        // Stretch contrast to push values toward 0 and 1
        float stretched = (normalized - 0.5f) * 2.5f + 0.5f;
        return Mathf.Clamp01(stretched);
    }
}
