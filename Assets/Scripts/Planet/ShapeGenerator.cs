using UnityEngine;

public class ShapeGenerator : ITerrainProvider
{
    ShapeSettings _shapeSettings;
    INoiseFilter[] _noiseFilters;
    int _seed;

    public MinMax ElevationRange { get; private set; } = new MinMax();

    public void Initialize(ShapeSettings settings, int seed)
    {
        _shapeSettings = settings;
        _seed = seed;
        ElevationRange = new MinMax();
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

        ElevationRange.AddValue(elevation);
        return elevation;
    }

    public float GetScaledElevation(float unscaledElevation)
    {
        float elevation = Mathf.Max(0, unscaledElevation);
        elevation = _shapeSettings.PlanetRadius * (1 + elevation);
        return elevation;
    }
}
