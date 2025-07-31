using UnityEngine;

public class SimpleNoiseFilter : INoiseFilter
{
    protected Noise _noise = new Noise();
    protected NoiseSettings _noiseSettings;

    public SimpleNoiseFilter(NoiseSettings noiseSettings)
    {
        _noiseSettings = noiseSettings;
    }

    public virtual float Evaluate(Vector3 point)
    {
        float noiseValue = 0;
        float frequency = _noiseSettings.BaseRoughness;
        float amplitude = 1;

        for (int i = 0; i < _noiseSettings.Layers; i++)
        {
            float v = _noise.Evaluate(point * frequency + _noiseSettings.Center);
            noiseValue += (v + 1) * 0.5f * amplitude;
            frequency *= _noiseSettings.Roughness;
            amplitude *= _noiseSettings.Persistence;
        }

        noiseValue = noiseValue - _noiseSettings.MinValue;
        return noiseValue * _noiseSettings.Strength;
    }
}
