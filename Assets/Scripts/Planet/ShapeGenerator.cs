using Unity.Collections;
using UnityEngine;

public class ShapeGenerator : ITerrainProvider
{
    ShapeSettings _shapeSettings;
    INoiseFilter[] _noiseFilters;
    int _seed;

    public ShapeSettings Settings => _shapeSettings;
    public int LayerCount => _shapeSettings?.NoiseLayers?.Length ?? 0;

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

    // Builds a blittable per-layer noise data array for Burst jobs. The caller owns the
    // returned NativeArray and must Dispose() it. Layers are produced in the same order as
    // ShapeSettings.NoiseLayers so that downstream job code can index them identically.
    public NativeArray<NoiseFilterData> BuildNoiseFilterData(Allocator allocator)
    {
        if (_shapeSettings == null)
            throw new System.InvalidOperationException("ShapeGenerator.Configure() must be called before BuildNoiseFilterData().");

        var layers = _shapeSettings.NoiseLayers;
        int count = layers?.Length ?? 0;
        var data = new NativeArray<NoiseFilterData>(count, allocator, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < count; i++)
        {
            var layer = layers[i];
            data[i] = NoiseFilterData.Create(layer.NoiseSettings, _seed + i, layer.Enabled, layer.UseFirstLayerAsMask);
        }
        return data;
    }

    // Pushes a per-vertex elevation value into the working min/max range. Used after a Burst
    // mesh-job completes so the rendered elevation envelope still matches what biome colouring
    // expects, even though EvaluateElevation isn't called per vertex anymore.
    public void RecordElevationSample(float elevation)
    {
        _workingMinMax.AddValue(elevation);
    }
}
