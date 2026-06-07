using UnityEngine;

public class ColorGenerator : IBiomeProvider, System.IDisposable
{
    BiomeSettings _biomeSettings;
    IClimateProvider _climateProvider;
    IBiomeRegistry _biomeRegistry;
    VoronoiBiomeField _voronoiBiomeField;
    Color[] _biomeColors;

    static readonly int _biomeAssignmentModeId = Shader.PropertyToID("_BiomeAssignmentMode");
    static readonly int _biomeVoronoiSeedCountId = Shader.PropertyToID("_BiomeVoronoiSeedCount");
    static readonly int _biomeVoronoiCleanupChangesId = Shader.PropertyToID("_BiomeVoronoiCleanupChanges");
    static readonly int _biomeVoronoiDistinctBiomesId = Shader.PropertyToID("_BiomeVoronoiDistinctBiomes");
    static readonly int _biomeVoronoiBuildMsId = Shader.PropertyToID("_BiomeVoronoiBuildMs");
    static readonly int _biomeVoronoiAtlasResolutionId = Shader.PropertyToID("_BiomeVoronoiAtlasResolution");

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
    internal VoronoiBiomeField VoronoiBiomeField => _voronoiBiomeField;
    internal IClimateProvider ClimateProvider => _climateProvider;

    // Per-biome flat color, indexed by GetDefinitionByIndex slot id. Phase B step 5b bake
    // reads this to compute its pre-blended color texture. Mirrors what
    // BiomeSurfaceTextureArrays.BuildFlatColorLut uses for the GPU LUT — keeping both paths
    // in sync via a single source would be cleaner long-term.
    public Color[] BiomeColors => _biomeColors;

    public void Configure(BiomeSettings settings)
    {
        _biomeSettings = settings;
        _biomeRegistry = null;
        _climateProvider = null;
        _voronoiBiomeField = null;
        _biomeColors = null;

        if (_biomeSettings != null && _biomeSettings.Registry != null)
        {
            _biomeRegistry = _biomeSettings.Registry;
            _climateProvider = new ClimateProvider(_biomeSettings);
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

    public void Initialize(int seed, int biomeAssignmentSeed)
    {
        _climateProvider?.Initialize(seed);
        _voronoiBiomeField = null;

        if (_biomeSettings != null &&
            _biomeSettings.AssignmentMode == BiomeAssignmentMode.Voronoi &&
            _biomeSettings.Registry != null &&
            _climateProvider != null)
        {
            _voronoiBiomeField = VoronoiBiomeField.Build(
                _biomeSettings,
                _biomeSettings.Registry,
                _climateProvider,
                biomeAssignmentSeed);
            LoggerProvider.Log(
                LogLevel.Debug,
                "Biome",
                $"Built global Voronoi field: seeds={_voronoiBiomeField.SeedCount}, " +
                $"biomes={_voronoiBiomeField.DistinctBiomeCount}, " +
                $"cleanupChanges={_voronoiBiomeField.CleanupChanges}, " +
                $"atlas={_voronoiBiomeField.LookupAtlasResolution}x{_voronoiBiomeField.LookupAtlasResolution}x6, " +
                $"build={_voronoiBiomeField.BuildMilliseconds}ms");
        }

        Shader.SetGlobalInt(_biomeAssignmentModeId, _voronoiBiomeField != null ? 1 : 0);
        Shader.SetGlobalInt(_biomeVoronoiSeedCountId, _voronoiBiomeField?.SeedCount ?? 0);
        Shader.SetGlobalInt(_biomeVoronoiCleanupChangesId, _voronoiBiomeField?.CleanupChanges ?? 0);
        Shader.SetGlobalInt(_biomeVoronoiDistinctBiomesId, _voronoiBiomeField?.DistinctBiomeCount ?? 0);
        Shader.SetGlobalInt(_biomeVoronoiBuildMsId, (int)(_voronoiBiomeField?.BuildMilliseconds ?? 0));
        Shader.SetGlobalInt(_biomeVoronoiAtlasResolutionId, _voronoiBiomeField?.LookupAtlasResolution ?? 0);
    }

    public BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation)
    {
        if (_biomeRegistry == null || _climateProvider == null)
            return new BiomeResult(BiomeType.Grassland, 0.5f, 0.5f);

        ClimateSample climate = _climateProvider.Evaluate(pointOnUnitSphere, elevation);

        return ResolveBiome(pointOnUnitSphere, climate);
    }

    public Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation)
    {
        return EvaluateBiomeColor(pointOnUnitSphere, elevation, out _, out _, out _, out _);
    }

    public Color GetBiomeColorAndData(Vector3 pointOnUnitSphere, float elevation, out Vector4 biomeData)
    {
        Color color = EvaluateBiomeColor(
            pointOnUnitSphere,
            elevation,
            out float temperature,
            out float moisture,
            out int primaryBiomeIndex,
            out float altitudeTemperatureDrop);
        float biomeCount = _biomeColors != null && _biomeColors.Length > 0 ? _biomeColors.Length : 1f;
        biomeData = new Vector4(
            temperature,
            moisture,
            primaryBiomeIndex / biomeCount,
            Mathf.Clamp01(altitudeTemperatureDrop));
        return color;
    }

    // Shared color-resolution path. Returns the final blended color and writes back the raw
    // signals (temperature, moisture, primary biome index) used by the diagnostic surface.
    Color EvaluateBiomeColor(
        Vector3 pointOnUnitSphere,
        float elevation,
        out float temperature,
        out float moisture,
        out int primaryBiomeIndex,
        out float altitudeTemperatureDrop)
    {
        temperature = 0f;
        moisture = 0f;
        primaryBiomeIndex = 0;
        altitudeTemperatureDrop = 0f;

        if (_biomeRegistry == null || _climateProvider == null)
            return Color.magenta;

        ClimateSample climate = _climateProvider.Evaluate(pointOnUnitSphere, elevation);
        temperature = climate.Temperature01;
        moisture = climate.Moisture01;
        altitudeTemperatureDrop = climate.AltitudeTemperatureDrop;

        BiomeResult result = ResolveBiome(pointOnUnitSphere, climate);
        primaryBiomeIndex = Mathf.Clamp(_biomeSettings.Registry.GetSliceIdForBiomeType(result.PrimaryBiome), 0, _biomeColors.Length - 1);
        int secondaryBiomeIndex = Mathf.Clamp(_biomeSettings.Registry.GetSliceIdForBiomeType(result.SecondaryBiome), 0, _biomeColors.Length - 1);

        Color primary = _biomeColors[primaryBiomeIndex];
        Color secondary = _biomeColors[secondaryBiomeIndex];
        return Color.Lerp(primary, secondary, Mathf.Clamp01(result.BlendWeight));
    }

    BiomeResult ResolveBiome(Vector3 pointOnUnitSphere, ClimateSample climate)
    {
        if (_voronoiBiomeField == null)
        {
            return _biomeSettings.Registry.Resolve(
                climate.Temperature01,
                climate.Moisture01,
                climate.Elevation);
        }

        VoronoiBiomeSample sample = _voronoiBiomeField.Evaluate(pointOnUnitSphere);
        BiomeDefinition primary = _biomeSettings.Registry.GetDefinitionByIndex(sample.PrimaryId);
        BiomeDefinition secondary = _biomeSettings.Registry.GetDefinitionByIndex(sample.SecondaryId);
        BiomeType primaryType = primary != null ? primary.Type : BiomeType.Grassland;
        BiomeType secondaryType = secondary != null ? secondary.Type : primaryType;
        return _biomeSettings.Registry.ResolveWithLandBiomes(
            primaryType,
            secondaryType,
            sample.SecondaryWeight,
            climate.Temperature01,
            climate.Moisture01,
            climate.Elevation);
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
