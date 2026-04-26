using UnityEngine;

public class ShapeGenerator : ITerrainProvider
{
    ShapeSettings _shapeSettings;
    INoiseFilter[] _noiseFilters;
    int _seed;

    public float ElevationMin => _elevationMinMax.Min;
    public float ElevationMax => _elevationMinMax.Max;

    MinMax _elevationMinMax = new MinMax();

    public void Configure(ShapeSettings settings)
    {
        _shapeSettings = settings;
    }

    public void Initialize(int seed)
    {
        _seed = seed;
        _elevationMinMax = new MinMax();
        _noiseFilters = new INoiseFilter[_shapeSettings.NoiseLayers.Length];
        for (int i = 0; i < _noiseFilters.Length; i++)
        {
            _noiseFilters[i] = NoiseFilterFactory.CreateNoiseFilter(
                _shapeSettings.NoiseLayers[i].NoiseSettings, _seed + i);
        }
    }

    public float EvaluateElevation(Vector3 pointOnUnitSphere)
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
        return _shapeSettings.PlanetRadius * (1 + unscaledElevation);
    }
}
