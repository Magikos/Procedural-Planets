using UnityEngine;

public class RigidNoiseFilter : SimpleNoiseFilter
{
    public RigidNoiseFilter(NoiseSettings noiseSettings, int seed = 0) : base(noiseSettings, seed) { }

    public override float Evaluate(Vector3 point)
    {
        float noiseValue = 0;
        float frequency = _noiseSettings.BaseRoughness;
        float amplitude = 1;
        float weight = 1;

        for (int i = 0; i < _noiseSettings.Layers; i++)
        {
            float v = 1 - Mathf.Abs(_noise.Evaluate(point * frequency + _noiseSettings.Center));
            v *= v;
            v *= weight;
            weight = v;

            noiseValue += v * amplitude;
            frequency *= _noiseSettings.Roughness;
            amplitude *= _noiseSettings.Persistence;
        }

        noiseValue = noiseValue - _noiseSettings.MinValue;
        return noiseValue * _noiseSettings.Strength;
    }
}