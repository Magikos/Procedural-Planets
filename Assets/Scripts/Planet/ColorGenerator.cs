using UnityEngine;

public class ColorGenerator : IBiomeProvider, System.IDisposable
{
    BiomeDto _biome;
    IClimateProvider _climateProvider;
    BiomeRegistryDto _biomeRegistry;
    VoronoiBiomeField _voronoiBiomeField;
    Color[] _biomeColors;

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

    public BiomeRegistryDto Registry => _biome?.Registry;
    internal VoronoiBiomeField VoronoiBiomeField => _voronoiBiomeField;
    internal IClimateProvider ClimateProvider => _climateProvider;

    // Per-biome flat color, indexed by GetDefinitionByIndex slot id. Phase B step 5b bake
    // reads this to compute its pre-blended color texture. Mirrors what
    // BiomeSurfaceTextureArrays.BuildFlatColorLut uses for the GPU LUT — keeping both paths
    // in sync via a single source would be cleaner long-term.
    public Color[] BiomeColors => _biomeColors;

    public void Configure(BiomeDto biome)
    {
        _biome = biome;
        _biomeRegistry = null;
        _climateProvider = null;
        _voronoiBiomeField = null;
        _biomeColors = null;

        if (_biome != null && _biome.Registry != null)
        {
            _biomeRegistry = _biome.Registry;
            _climateProvider = new ClimateProvider(_biome);
            _surfaceArrays.Build(_biome.Registry);
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

        if (_biome != null && _biome.Registry != null && _climateProvider != null)
        {
            _voronoiBiomeField = VoronoiBiomeField.Build(
                _biome,
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
        primaryBiomeIndex = Mathf.Clamp(_biomeRegistry.GetSliceIdForBiomeType(result.PrimaryBiome), 0, _biomeColors.Length - 1);
        int secondaryBiomeIndex = Mathf.Clamp(_biomeRegistry.GetSliceIdForBiomeType(result.SecondaryBiome), 0, _biomeColors.Length - 1);

        Color primary = _biomeColors[primaryBiomeIndex];
        Color secondary = _biomeColors[secondaryBiomeIndex];
        return Color.Lerp(primary, secondary, Mathf.Clamp01(result.BlendWeight));
    }

    BiomeResult ResolveBiome(Vector3 pointOnUnitSphere, ClimateSample climate)
    {
        if (_voronoiBiomeField == null)
        {
            return _biomeRegistry.Resolve(
                climate.Temperature01,
                climate.Moisture01,
                climate.Elevation);
        }

        VoronoiBiomeSample sample = _voronoiBiomeField.Evaluate(pointOnUnitSphere);
        BiomeDefinitionDto primary = _biomeRegistry.GetDefinitionByIndex(sample.PrimaryId);
        BiomeDefinitionDto secondary = _biomeRegistry.GetDefinitionByIndex(sample.SecondaryId);
        BiomeType primaryType = primary != null ? primary.Type : BiomeType.Grassland;
        BiomeType secondaryType = secondary != null ? secondary.Type : primaryType;
        return _biomeRegistry.ResolveWithLandBiomes(
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

        int count = _biomeRegistry.BiomeCount;
        _biomeColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            var def = _biomeRegistry.GetDefinitionByIndex(i);
            if (def != null)
                _biomeColors[i] = def.ColorGradient.Evaluate(0.5f) * (1 - def.TintPercent) + def.TintColor * def.TintPercent;
            else
                _biomeColors[i] = Color.magenta;
        }
    }
}
