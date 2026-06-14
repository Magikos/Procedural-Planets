using UnityEngine;

readonly struct TemperatureClimateSample
{
    public readonly float FinalTemperature01;
    public readonly float Latitude01;
    public readonly float LatitudeTemperature01;
    public readonly float NoiseContribution;
    public readonly float AltitudeDrop;

    public TemperatureClimateSample(
        float finalTemperature01,
        float latitude01,
        float latitudeTemperature01,
        float noiseContribution,
        float altitudeDrop)
    {
        FinalTemperature01 = finalTemperature01;
        Latitude01 = latitude01;
        LatitudeTemperature01 = latitudeTemperature01;
        NoiseContribution = noiseContribution;
        AltitudeDrop = altitudeDrop;
    }
}

public class TemperatureProvider : ITemperatureProvider
{
    readonly NoiseSettings _noiseSettings;
    readonly ClimateCurveLut _latitudeCurve;
    readonly float _noiseStrength;
    readonly float _altitudeTemperatureDrop;
    readonly float _waterLevel;
    INoiseFilter _noiseFilter;
    float _maxValue;

    internal TemperatureProvider(
        NoiseSettings noiseSettings,
        ClimateCurveLut latitudeCurve,
        float noiseStrength,
        float altitudeTemperatureDrop,
        float waterLevel)
    {
        _noiseSettings = noiseSettings;
        _latitudeCurve = latitudeCurve;
        _noiseStrength = noiseStrength;
        _altitudeTemperatureDrop = altitudeTemperatureDrop;
        _waterLevel = waterLevel;
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
        return EvaluateClimate(pointOnUnitSphere, _waterLevel).FinalTemperature01;
    }

    internal TemperatureClimateSample EvaluateClimate(
        Vector3 pointOnUnitSphere,
        float elevation)
    {
        float latitude01 = CoordinateConverter.NormalizedLatitude(pointOnUnitSphere);
        float latitudeTemperature01 = _latitudeCurve.Sample(latitude01);
        float normalized = _noiseFilter.Evaluate(pointOnUnitSphere) / _maxValue;
        float noiseContribution = (normalized - 0.5f) * _noiseStrength;
        float landHeight = Mathf.Max(0f, elevation - _waterLevel);
        float altitudeDrop = landHeight * _altitudeTemperatureDrop;
        float finalTemperature = Mathf.Clamp01(
            latitudeTemperature01 + noiseContribution - altitudeDrop);

        return new TemperatureClimateSample(
            finalTemperature,
            latitude01,
            latitudeTemperature01,
            noiseContribution,
            altitudeDrop);
    }
}
