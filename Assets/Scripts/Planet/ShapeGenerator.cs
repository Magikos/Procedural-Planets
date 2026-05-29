using UnityEngine;

public class ShapeGenerator : ITerrainProvider
{
    ShapeSettings _shapeSettings;
    INoiseFilter[] _noiseFilters;
    int _seed;

    // Accumulated during (possibly multi-threaded) evaluation; only published to the public
    // ElevationMin/Max via CommitElevationRange() once a full generation pass has completed,
    // so consumers never observe a partial range.
    readonly MinMax _workingMinMax = new MinMax();
    float _committedMin;
    float _committedMax;

    public float ElevationMin => _committedMin;
    public float ElevationMax => _committedMax;

    public void Configure(ShapeSettings settings)
    {
        _shapeSettings = settings ?? throw new System.ArgumentNullException(nameof(settings));
    }

    public void Initialize(int seed)
    {
        if (_shapeSettings == null)
            throw new System.InvalidOperationException("ShapeGenerator.Configure() must be called before Initialize().");
        _seed = seed;
        _workingMinMax.Reset();
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

        _workingMinMax.AddValue(elevation);
        return elevation;
    }

    // Publishes the accumulated elevation range. Call on the main thread after all evaluation
    // (e.g. the parallel mesh pass) has completed.
    public void CommitElevationRange()
    {
        _committedMin = _workingMinMax.Min;
        _committedMax = _workingMinMax.Max;
    }

    public float GetScaledElevation(float unscaledElevation)
    {
        return _shapeSettings.PlanetRadius * (1 + unscaledElevation);
    }
}
