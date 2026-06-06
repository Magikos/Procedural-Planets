using UnityEngine;

readonly struct MoistureClimateSample
{
    public readonly float FinalMoisture01;
    public readonly float LatitudeMoisture01;
    public readonly float NoiseContribution;
    public readonly float LegacyMoisture01;

    public MoistureClimateSample(
        float finalMoisture01,
        float latitudeMoisture01,
        float noiseContribution,
        float legacyMoisture01)
    {
        FinalMoisture01 = finalMoisture01;
        LatitudeMoisture01 = latitudeMoisture01;
        NoiseContribution = noiseContribution;
        LegacyMoisture01 = legacyMoisture01;
    }
}

public class MoistureProvider : IMoistureProvider
{
    readonly NoiseSettings _noiseSettings;
    readonly ClimateCurveLut _latitudeCurve;
    readonly float _latitudeInfluence;
    readonly float _noiseStrength;
    INoiseFilter _noiseFilter;
    float _maxValue;

    internal MoistureProvider(
        NoiseSettings noiseSettings,
        ClimateCurveLut latitudeCurve,
        float latitudeInfluence,
        float noiseStrength)
    {
        _noiseSettings = noiseSettings;
        _latitudeCurve = latitudeCurve;
        _latitudeInfluence = Mathf.Clamp01(latitudeInfluence);
        _noiseStrength = Mathf.Clamp01(noiseStrength);
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
        float latitude01 = CoordinateConverter.NormalizedLatitude(pointOnUnitSphere);
        return EvaluateClimate(pointOnUnitSphere, latitude01).FinalMoisture01;
    }

    internal MoistureClimateSample EvaluateClimate(
        Vector3 pointOnUnitSphere,
        float latitude01)
    {
        float raw = _noiseFilter.Evaluate(pointOnUnitSphere);
        float normalized = raw / _maxValue;

        // Preserve the previous noise-only field exactly when latitude influence is zero.
        float stretched = (normalized - 0.5f) * 2.5f + 0.5f;
        float legacyMoisture = Mathf.Clamp01(stretched);

        float latitudeMoisture = _latitudeCurve.Sample(latitude01);
        float noiseContribution = (normalized - 0.5f) * 2f * _noiseStrength;
        float bandMoisture = Mathf.Clamp01(latitudeMoisture + noiseContribution);
        float finalMoisture = Mathf.Lerp(
            legacyMoisture, bandMoisture, _latitudeInfluence);

        return new MoistureClimateSample(
            finalMoisture,
            latitudeMoisture,
            noiseContribution,
            legacyMoisture);
    }
}
