using UnityEngine;

public class ColorGenerator : IBiomeProvider, System.IDisposable
{
    BiomeSettings _biomeSettings;
    ITemperatureProvider _temperatureProvider;
    IMoistureProvider _moistureProvider;
    IBiomeRegistry _biomeRegistry;
    Color[] _biomeColors;

    // Per-planet Texture2DArrays bound globally as _BiomeAlbedoArray / _BiomeNormalArray /
    // _BiomeArmArray. Rebuilt on every Configure(); the build call disposes its previous
    // arrays before allocating new ones, so calling Configure repeatedly does not leak.
    readonly BiomeSurfaceTextureArrays _surfaceArrays = new BiomeSurfaceTextureArrays();
    public BiomeSurfaceTextureArrays SurfaceArrays => _surfaceArrays;

    // Exposed for the Phase B chunk biome-map bake (ChunkedSurfaceProvider casts the
    // IBiomeProvider it gets to ColorGenerator and reads this to build a BiomeLookupData
    // snapshot). IBiomeProvider stays a pure evaluation interface — Core doesn't see the
    // BiomeRegistry concrete type.
    public BiomeRegistry Registry => _biomeSettings?.Registry;

    // Per-biome flat color, indexed by GetDefinitionByIndex slot id. Phase B step 5b bake
    // reads this to compute its pre-blended color texture. Mirrors what
    // BiomeSurfaceTextureArrays.BuildFlatColorLut uses for the GPU LUT — keeping both paths
    // in sync via a single source would be cleaner long-term.
    public Color[] BiomeColors => _biomeColors;

    public void Configure(BiomeSettings settings)
    {
        _biomeSettings = settings;

        if (_biomeSettings != null && _biomeSettings.Registry != null)
        {
            _biomeRegistry = _biomeSettings.Registry;
            _temperatureProvider = new TemperatureProvider(
                _biomeSettings.TemperatureNoise,
                _biomeSettings.TemperatureNoiseStrength);
            _moistureProvider = new MoistureProvider(_biomeSettings.MoistureNoise);
            _surfaceArrays.Build(_biomeSettings.Registry);
        }
        else
        {
            _surfaceArrays.Dispose();
        }

        BuildBiomeColorLookup();
    }

    public void Dispose()
    {
        _surfaceArrays.Dispose();
    }

    public void Initialize(int seed)
    {
        _temperatureProvider?.Initialize(seed);
        _moistureProvider?.Initialize(seed + 100);
    }

    public BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return new BiomeResult(BiomeType.Grassland, 0.5f, 0.5f);

        float temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        float moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        return _biomeRegistry.Resolve(temperature, moisture, elevation);
    }

    public Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation)
    {
        return EvaluateBiomeColor(pointOnUnitSphere, elevation, out _, out _, out _);
    }

    public Color GetBiomeColorAndData(Vector3 pointOnUnitSphere, float elevation, out Vector4 biomeData)
    {
        Color color = EvaluateBiomeColor(pointOnUnitSphere, elevation, out float temperature, out float moisture, out int primaryBiomeIndex);
        float latitude01 = Mathf.Abs(pointOnUnitSphere.y);
        float biomeCount = _biomeColors != null && _biomeColors.Length > 0 ? _biomeColors.Length : 1f;
        biomeData = new Vector4(temperature, moisture, primaryBiomeIndex / biomeCount, latitude01);
        return color;
    }

    // Shared color-resolution path. Returns the final blended color and writes back the raw
    // signals (temperature, moisture, primary biome index) used by the diagnostic surface.
    Color EvaluateBiomeColor(Vector3 pointOnUnitSphere, float elevation,
        out float temperature, out float moisture, out int primaryBiomeIndex)
    {
        temperature = 0f;
        moisture = 0f;
        primaryBiomeIndex = 0;

        if (_biomeRegistry == null || _temperatureProvider == null || _moistureProvider == null)
            return Color.magenta;

        temperature = _temperatureProvider.Evaluate(pointOnUnitSphere);
        moisture = _moistureProvider.Evaluate(pointOnUnitSphere);

        var result = _biomeSettings.Registry.Resolve(temperature, moisture, elevation);
        primaryBiomeIndex = Mathf.Clamp(_biomeSettings.Registry.GetSliceIdForBiomeType(result.PrimaryBiome), 0, _biomeColors.Length - 1);
        int secondaryBiomeIndex = Mathf.Clamp(_biomeSettings.Registry.GetSliceIdForBiomeType(result.SecondaryBiome), 0, _biomeColors.Length - 1);

        Color primary = _biomeColors[primaryBiomeIndex];
        Color secondary = _biomeColors[secondaryBiomeIndex];
        return Color.Lerp(primary, secondary, Mathf.Clamp01(result.BlendWeight));
    }

    void BuildBiomeColorLookup()
    {
        if (_biomeRegistry == null) return;

        var registry = _biomeSettings.Registry;
        int count = _biomeRegistry.BiomeCount;
        _biomeColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            var def = registry.GetDefinitionByIndex(i);
            if (def != null)
                _biomeColors[i] = def.ColorGradient.Evaluate(0.5f) * (1 - def.TintPercent) + def.TintColor * def.TintPercent;
            else
                _biomeColors[i] = Color.magenta;
        }
    }
}
