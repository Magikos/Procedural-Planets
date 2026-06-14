using UnityEngine;

// Owns the planet's grass LOD layers: the chunk-following GrassPlacementController, the
// camera-centered GrassNearFieldController, and the far terrain-blanket overlay baked into the
// terrain material. Tracks the master/per-layer enable flags and drives altitude-based activation
// of the near field. Planet forwards Configure/Tick/Dispose and the IGrassRuntimeControl surface.
[CommandPrefix("grass")]
sealed class PlanetGrassCoordinator : IGrassNearFieldStatsProvider
{
    readonly Transform _planetTransform;
    readonly IPlanetSurfaceSampler _surfaceSampler;
    readonly ILogger _logger;

    GrassPlacementController _grassController;
    GrassNearFieldController _grassNearFieldController;
    bool _grassEnabled = true;
    bool _nearFieldGrassEnabled = true;
    bool _chunkGrassEnabled = true;
    bool _grassBlanketEnabled = true;

    ChunkedSurfaceProvider _chunkedProvider;
    BiomeSurfaceTextureArrays _surfaceArrays;
    int _seed;
    Material _terrainMaterial;

    static readonly int _grassFarOverlayStrengthId = Shader.PropertyToID("_GrassFarOverlayStrength");
    static readonly int _grassFarOverlayStartId = Shader.PropertyToID("_GrassFarOverlayStart");
    static readonly int _grassFarOverlayEndId = Shader.PropertyToID("_GrassFarOverlayEnd");
    static readonly int _grassFarOverlayNoiseScaleId = Shader.PropertyToID("_GrassFarOverlayNoiseScale");
    static readonly int _grassFarOverlayOrbitStrengthId = Shader.PropertyToID("_GrassFarOverlayOrbitStrength");
    static readonly int _grassFarOverlayAltitudeStartId = Shader.PropertyToID("_GrassFarOverlayAltitudeStart");
    static readonly int _grassFarOverlayAltitudeEndId = Shader.PropertyToID("_GrassFarOverlayAltitudeEnd");
    static readonly int _grassFarOverlayFiberStrengthId = Shader.PropertyToID("_GrassFarOverlayFiberStrength");
    static readonly int _grassSurfaceBrightnessId = Shader.PropertyToID("_GrassSurfaceBrightness");

    // Live-tunable via grass.* console commands (see bottom of file). surface-brightness was
    // dialed to 0.4 to bring the painted ground grass down to the dark 3D-tuft shade, which is
    // what removes the visible grass line; strength stays at full coverage.
    float _farOverlayStrength = 1.0f;
    float _grassSurfaceBrightness = 0.4f;

    const float GrassFarOverlayStart = 35f;
    const float GrassFarOverlayEnd = 260f;
    const float GrassFarOverlayNoiseScale = 0.055f;
    const float GrassFarOverlayFiberStrength = 0.65f;
    const float GrassFarOverlayOrbitStrength = 0.42f;
    const float GrassFarOverlayAltitudeStart = 750f;
    const float GrassFarOverlayAltitudeEnd = 2600f;
    const float NearFieldGrassActivationAltitude = 350f;
    const float NearFieldGrassDeactivationAltitude = 500f;

    public PlanetGrassCoordinator(Transform planetTransform, IPlanetSurfaceSampler surfaceSampler, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceSampler = surfaceSampler;
        _logger = logger;
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Configure(ChunkedSurfaceProvider provider, BiomeSurfaceTextureArrays surfaceArrays,
        int seed, Camera observerCamera, Material terrainMaterial)
    {
        DisposeControllers();
        _chunkedProvider = provider;
        _surfaceArrays = surfaceArrays;
        _seed = seed;
        _terrainMaterial = terrainMaterial;

        if (provider == null)
            return;

        var planet = SettingsProvider.GetSettings<PlanetDto>();
        float waterRadius = ComputeWaterRadius(planet);
        if (_grassEnabled && _nearFieldGrassEnabled
            && observerCamera != null
            && ShouldActivateNearFieldGrass(observerCamera.transform.position, false))
        {
            CreateNearFieldGrassController(waterRadius);
        }

        if (_grassEnabled && _chunkGrassEnabled)
            CreateChunkGrassController(waterRadius);

        ApplyBlanketState(_terrainMaterial);
    }

    public void Tick(Camera camera)
    {
        UpdateControllerActivation(camera);
        using (FrameTimingCounters.Measure(FrameTimingSection.ChunkGrass))
            _grassController?.Tick(camera);
        using (FrameTimingCounters.Measure(FrameTimingSection.NearGrass))
            _grassNearFieldController?.Tick(camera);
    }

    void UpdateControllerActivation(Camera camera)
    {
        if (_chunkedProvider == null || camera == null)
            return;

        var planet = SettingsProvider.GetSettings<PlanetDto>();
        float waterRadius = ComputeWaterRadius(planet);

        if (_grassEnabled && _chunkGrassEnabled)
        {
            if (_grassController == null)
                CreateChunkGrassController(waterRadius);
        }
        else if (_grassController != null)
        {
            _grassController.Dispose();
            _grassController = null;
        }

        bool nearFieldShouldBeActive = _grassEnabled
            && _nearFieldGrassEnabled
            && ShouldActivateNearFieldGrass(camera.transform.position, _grassNearFieldController != null);
        if (nearFieldShouldBeActive)
        {
            if (_grassNearFieldController == null)
                CreateNearFieldGrassController(waterRadius);
        }
        else if (_grassNearFieldController != null)
        {
            _grassNearFieldController.Dispose();
            _grassNearFieldController = null;
        }
    }

    static float ComputeWaterRadius(PlanetDto planet)
    {
        return planet.HasOceans
            ? planet.PlanetRadius * (1f + planet.OceanLevel)
            : -1f;
    }

    bool ShouldActivateNearFieldGrass(Vector3 cameraPosition, bool currentlyActive)
    {
        return ShouldActivateGrassLayer(cameraPosition, currentlyActive,
            NearFieldGrassActivationAltitude, NearFieldGrassDeactivationAltitude);
    }

    bool ShouldActivateGrassLayer(Vector3 cameraPosition, bool currentlyActive,
        float activationAltitude, float deactivationAltitude)
    {
        Vector3 fromCenter = cameraPosition - _planetTransform.position;
        if (fromCenter.sqrMagnitude < 0.0001f)
            return true;

        if (!_surfaceSampler.TryGetSurfaceRadius(fromCenter.normalized, out float surfaceRadius))
            return currentlyActive;

        float altitude = Mathf.Max(0f, fromCenter.magnitude - surfaceRadius);
        float threshold = currentlyActive
            ? deactivationAltitude
            : activationAltitude;
        return altitude <= threshold;
    }

    void CreateChunkGrassController(float waterRadius)
    {
        _grassController = new GrassPlacementController(_planetTransform, _chunkedProvider,
            _surfaceArrays.GrassParamsBuffer, _surfaceArrays.SliceCount,
            waterRadius, _seed, this, _logger);
    }

    void CreateNearFieldGrassController(float waterRadius)
    {
        _grassNearFieldController = new GrassNearFieldController(_planetTransform, _chunkedProvider,
            _surfaceArrays.GrassParamsBuffer, _surfaceArrays.SliceCount,
            waterRadius, SettingsProvider.GetSettings<PlanetDto>().PlanetRadius, _seed, _logger);
    }

    public GrassNearFieldStats GetGrassNearFieldStats()
    {
        return _grassNearFieldController != null
            ? _grassNearFieldController.GetGrassNearFieldStats()
            : default;
    }

    public void ApplyTerrainOverlay(Material mat)
    {
        _terrainMaterial = mat;
        ApplyBlanketState(mat);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayStartId, GrassFarOverlayStart);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayEndId, GrassFarOverlayEnd);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayNoiseScaleId, GrassFarOverlayNoiseScale);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayOrbitStrengthId, GrassFarOverlayOrbitStrength);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeStartId, GrassFarOverlayAltitudeStart);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeEndId, GrassFarOverlayAltitudeEnd);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayFiberStrengthId, GrassFarOverlayFiberStrength);
        SetMaterialFloatIfPresent(mat, _grassSurfaceBrightnessId, _grassSurfaceBrightness);
    }

    void ApplyBlanketState(Material mat)
    {
        if (mat == null)
            return;
        float strength = _grassEnabled && _grassBlanketEnabled ? _farOverlayStrength : 0f;
        SetMaterialFloatIfPresent(mat, _grassFarOverlayStrengthId, strength);
    }

    static void SetMaterialFloatIfPresent(Material mat, int propertyId, float value)
    {
        if (!mat.HasProperty(propertyId)) return;
        mat.SetFloat(propertyId, value);
    }

    public GrassRuntimeState GetGrassRuntimeState()
    {
        return new GrassRuntimeState
        {
            MasterEnabled = _grassEnabled,
            NearFieldRequested = _nearFieldGrassEnabled,
            NearFieldActive = _grassNearFieldController != null,
            ChunkPathRequested = _chunkGrassEnabled,
            ChunkPathActive = _grassController != null,
            BlanketRequested = _grassBlanketEnabled,
            BlanketActive = _grassEnabled && _grassBlanketEnabled,
        };
    }

    public void SetGrassEnabled(bool enabled)
    {
        _grassEnabled = enabled;
        ApplyBlanketState(_terrainMaterial);
        if (!enabled)
        {
            _grassNearFieldController?.Dispose();
            _grassNearFieldController = null;
            _grassController?.Dispose();
            _grassController = null;
        }
    }

    public void SetGrassLayerEnabled(GrassRenderLayer layer, bool enabled)
    {
        switch (layer)
        {
            case GrassRenderLayer.Near:
                _nearFieldGrassEnabled = enabled;
                if (!enabled)
                {
                    _grassNearFieldController?.Dispose();
                    _grassNearFieldController = null;
                }
                break;
            case GrassRenderLayer.Chunk:
                _chunkGrassEnabled = enabled;
                if (!enabled)
                {
                    _grassController?.Dispose();
                    _grassController = null;
                }
                break;
            case GrassRenderLayer.Blanket:
                _grassBlanketEnabled = enabled;
                ApplyBlanketState(_terrainMaterial);
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(layer), layer, null);
        }
    }

    public void DisposeControllers()
    {
        _grassNearFieldController?.Dispose();
        _grassNearFieldController = null;
        _grassController?.Dispose();
        _grassController = null;
    }

    public void Dispose()
    {
        DisposeControllers();
        ConsoleRegistry.UnregisterInstance(typeof(PlanetGrassCoordinator));
    }

    // --- Live grass-line tuning (console) ---------------------------------
    // The far overlay paints grass onto the terrain surface where real blades are sparse or
    // culled; surface-brightness darkens/brightens that painted grass to match the 3D tufts.
    // Tune live, then bake the winning values into the field defaults above.

    void ReapplyOverlay()
    {
        if (_terrainMaterial != null)
            ApplyTerrainOverlay(_terrainMaterial);
    }

    [ConsoleCommand("overlay-strength", "Grass far-overlay coverage strength (0-1). How much terrain reads as grass.", MonoTargetType.Registry)]
    string OverlayStrengthCmd(float? value = null)
    {
        if (value.HasValue) { _farOverlayStrength = Mathf.Clamp01(value.Value); ReapplyOverlay(); }
        return $"grass overlay-strength: {_farOverlayStrength:F3}";
    }

    [ConsoleCommand("surface-brightness", "Painted grass-surface brightness (0.3-1.5). Lower to darken ground to match 3D tufts.", MonoTargetType.Registry)]
    string SurfaceBrightnessCmd(float? value = null)
    {
        if (value.HasValue) { _grassSurfaceBrightness = Mathf.Clamp(value.Value, 0.3f, 1.5f); ReapplyOverlay(); }
        return $"grass surface-brightness: {_grassSurfaceBrightness:F3}";
    }

    [ConsoleCommand("overlay-status", "Print the live grass-line overlay tuning values.", MonoTargetType.Registry)]
    string OverlayStatusCmd()
    {
        return $"strength={_farOverlayStrength:F3} surface-brightness={_grassSurfaceBrightness:F3}";
    }
}
