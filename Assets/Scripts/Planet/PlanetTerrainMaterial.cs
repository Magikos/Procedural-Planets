using UnityEngine;

// Owns the runtime clone of the authoring PlanetMaterial and its shader configuration. All runtime
// shader-property/keyword writes and the surface renderers target this clone so the SO-referenced
// asset is never mutated (CLAUDE.md: materials are cloned on first use). Configures the vertex-color
// shader, the grass-far overlay (via the grass coordinator), and the coast/slope/snow surface
// overrides on each generation.
sealed class PlanetTerrainMaterial
{
    readonly ILogger _logger;

    Material _runtime;

    static Shader _vcShader;

    static readonly int _terrainOverrideEnabledId = Shader.PropertyToID("_TerrainOverrideEnabled");
    static readonly int _coastSliceId = Shader.PropertyToID("_CoastSlice");
    static readonly int _coastBelowSeaDepthId = Shader.PropertyToID("_CoastBelowSeaDepth");
    static readonly int _coastStartHeightId = Shader.PropertyToID("_CoastStartHeight");
    static readonly int _coastEndHeightId = Shader.PropertyToID("_CoastEndHeight");
    static readonly int _coastTilingId = Shader.PropertyToID("_CoastTiling");
    static readonly int _slopeSliceId = Shader.PropertyToID("_SlopeSlice");
    static readonly int _slopeStartDegreesId = Shader.PropertyToID("_SlopeStartDegrees");
    static readonly int _slopeFullDegreesId = Shader.PropertyToID("_SlopeFullDegrees");
    static readonly int _slopeTilingId = Shader.PropertyToID("_SlopeTiling");
    static readonly int _snowSliceId = Shader.PropertyToID("_SnowSlice");
    static readonly int _snowFullTemperatureId = Shader.PropertyToID("_SnowFullTemperature");
    static readonly int _snowFadeEndTemperatureId = Shader.PropertyToID("_SnowFadeEndTemperature");
    static readonly int _snowTilingId = Shader.PropertyToID("_SnowTiling");

    public Material Material => _runtime;

    public PlanetTerrainMaterial(ILogger logger)
    {
        _logger = logger;
        if (_vcShader == null) _vcShader = Shader.Find("Planet/VertexColor");
    }

    // Clones the authoring PlanetMaterial into the runtime material, replacing any prior clone.
    // Everything downstream (renderers, property writes) targets the clone so the asset stays clean.
    public void EnsureRuntime(Material source)
    {
        if (_runtime != null)
        {
            Object.Destroy(_runtime);
            _runtime = null;
        }
        if (source != null)
            _runtime = new Material(source) { name = $"{source.name} (runtime)" };
    }

    public void Configure(PlanetGrassCoordinator grass)
    {
        var mat = _runtime;
        if (mat == null)
        {
            _logger.Log(LogLevel.Warning, "Planet", "PlanetMaterial is not assigned in PlanetSettings.");
            return;
        }
        if (mat.shader.name != "Planet/VertexColor")
        {
            if (_vcShader != null) mat.shader = _vcShader;
        }
        mat.SetFloat("_Smoothness", 0f);
        grass.ApplyTerrainOverlay(mat);
        ConfigureSurfaceOverrides(mat);
    }

    void ConfigureSurfaceOverrides(Material mat)
    {
        BiomeRegistryDto registry = SettingsProvider.IsRegistered<BiomeDto>()
            ? SettingsProvider.GetSettings<BiomeDto>()?.Registry
            : null;
        bool surfaceOverridesEnabled = SettingsProvider.IsRegistered<PlanetDto>()
            && SettingsProvider.GetSettings<PlanetDto>().EnableSurfaceOverrides;

        SetMaterialAndGlobalFloat(mat, _terrainOverrideEnabledId,
            surfaceOverridesEnabled && registry != null ? 1f : 0f);
        if (!surfaceOverridesEnabled || registry == null)
            return;

        SetMaterialAndGlobalInt(mat, _coastSliceId, registry.GetSliceIdForBiomeType(BiomeType.Beach));
        SetMaterialAndGlobalFloat(mat, _coastBelowSeaDepthId, PlanetConstants.CoastBelowSeaDepth);
        SetMaterialAndGlobalFloat(mat, _coastStartHeightId, PlanetConstants.CoastStartHeight);
        SetMaterialAndGlobalFloat(mat, _coastEndHeightId, PlanetConstants.CoastEndHeight);
        SetMaterialAndGlobalFloat(mat, _coastTilingId, PlanetConstants.CoastTiling);

        SetMaterialAndGlobalInt(mat, _slopeSliceId, registry.GetSliceIdForBiomeType(BiomeType.Mountain));
        SetMaterialAndGlobalFloat(mat, _slopeStartDegreesId, PlanetConstants.SlopeStartDegrees);
        SetMaterialAndGlobalFloat(mat, _slopeFullDegreesId, PlanetConstants.SlopeFullDegrees);
        SetMaterialAndGlobalFloat(mat, _slopeTilingId, PlanetConstants.SlopeTiling);

        SetMaterialAndGlobalInt(mat, _snowSliceId, registry.GetSliceIdForBiomeType(BiomeType.Snow));
        SetMaterialAndGlobalFloat(mat, _snowFullTemperatureId, PlanetConstants.SnowFullTemperature01);
        SetMaterialAndGlobalFloat(mat, _snowFadeEndTemperatureId, PlanetConstants.SnowFadeEndTemperature01);
        SetMaterialAndGlobalFloat(mat, _snowTilingId, PlanetConstants.SnowTiling);
    }

    static void SetMaterialAndGlobalFloat(Material mat, int propertyId, float value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetFloat(propertyId, value);
        Shader.SetGlobalFloat(propertyId, value);
    }

    static void SetMaterialAndGlobalInt(Material mat, int propertyId, int value)
    {
        if (mat.HasProperty(propertyId))
            mat.SetInt(propertyId, value);
        Shader.SetGlobalInt(propertyId, value);
    }

    public void Dispose()
    {
        if (_runtime != null)
        {
            Object.Destroy(_runtime);
            _runtime = null;
        }
    }
}
