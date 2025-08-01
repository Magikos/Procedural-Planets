using UnityEngine;

public class ShapeGenerator
{
    ShapeSettings _shapeSettings;
    INoiseFilter[] _noiseFilters;
    public MinMax _elevationMinMax = new MinMax();
    int _seed;

    public void UpdateSettings(ShapeSettings shapeSettings, int seed)
    {
        _shapeSettings = shapeSettings;
        _seed = seed;
        _noiseFilters = new INoiseFilter[_shapeSettings.NoiseLayers.Length];
        for (int i = 0; i < _noiseFilters.Length; i++)
        {
            _noiseFilters[i] = NoiseFilterFactory.CreateNoiseFilter(_shapeSettings.NoiseLayers[i].NoiseSettings, _seed + i);
        }
    }

    public float CalculateUnscaledElevation(Vector3 pointOnUnitSphere)
    {
        float elevation = 0;
        float firstLayerValue = 0;
        if (_noiseFilters.Length > 0)
        {
            firstLayerValue = _noiseFilters[0].Evaluate(pointOnUnitSphere);
            if (_shapeSettings.NoiseLayers[0].Enabled) { elevation = firstLayerValue; }
        }

        for (int i = 1; i < _noiseFilters.Length; i++)
        {
            if (!_shapeSettings.NoiseLayers[i].Enabled) continue;

            float mask = _shapeSettings.NoiseLayers[i].UseFirstLayerAsMask ? firstLayerValue : 1;
            elevation += _noiseFilters[i].Evaluate(pointOnUnitSphere) * mask;
        }

        _elevationMinMax.AddValue(elevation);
        return elevation;
    }

    public float GetScaledElevation(float unscaledElevation)
    {
        float elevation = Mathf.Max(0, unscaledElevation);
        elevation = _shapeSettings.PlanetRadius * (1 + elevation);
        return elevation;
    }

}