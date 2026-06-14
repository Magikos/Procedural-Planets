using UnityEngine;

sealed class ClimateCurveLut
{
    readonly float[] _samples;

    ClimateCurveLut(float[] samples)
    {
        _samples = samples;
    }

    public static ClimateCurveLut Bake(AnimationCurve curve, int resolution)
    {
        resolution = Mathf.Clamp(resolution, 16, 512);
        var samples = new float[resolution];
        float denominator = resolution - 1f;
        for (int i = 0; i < resolution; i++)
            samples[i] = Mathf.Clamp01(curve.Evaluate(i / denominator));
        return new ClimateCurveLut(samples);
    }

    public float Sample(float input01)
    {
        float position = Mathf.Clamp01(input01) * (_samples.Length - 1);
        int lower = Mathf.FloorToInt(position);
        int upper = Mathf.Min(lower + 1, _samples.Length - 1);
        return Mathf.LerpUnclamped(_samples[lower], _samples[upper], position - lower);
    }
}
