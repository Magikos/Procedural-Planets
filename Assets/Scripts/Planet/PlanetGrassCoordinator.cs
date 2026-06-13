using UnityEngine;

// Owns the planet's grass LOD layers: the chunk-following GrassPlacementController, the
// camera-centered GrassNearFieldController, and the far terrain-blanket overlay baked into the
// terrain material. Tracks the master/per-layer enable flags and drives altitude-based activation
// of the near field. Planet forwards Configure/Tick/Dispose and the IGrassRuntimeControl surface.
sealed class PlanetGrassCoordinator
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

    const float GrassFarOverlayStrength = 1.0f;
    const float GrassFarOverlayStart = 35f;
    const float GrassFarOverlayEnd = 260f;
    const float GrassFarOverlayNoiseScale = 0.055f;
    const float GrassFarOverlayOrbitStrength = 0.42f;
    const float GrassFarOverlayAltitudeStart = 750f;
    const float GrassFarOverlayAltitudeEnd = 2600f;
    const float GrassFarOverlayFiberStrength = 0.65f;
    const float NearFieldGrassActivationAltitude = 350f;
    const float NearFieldGrassDeactivationAltitude = 500f;

    public PlanetGrassCoordinator(Transform planetTransform, IPlanetSurfaceSampler surfaceSampler, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceSampler = surfaceSampler;
        _logger = logger;
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
        if (_grassEnabled && _chunkGrassEnabled)
            CreateChunkGrassController(waterRadius);

        if (_grassEnabled && _nearFieldGrassEnabled
            && observerCamera != null
            && ShouldActivateNearFieldGrass(observerCamera.transform.position, false))
        {
            CreateNearFieldGrassController(waterRadius);
        }

        ApplyBlanketState(_terrainMaterial);
    }

    public void Tick(Camera camera)
    {
        UpdateControllerActivation(camera);
        _grassController?.Tick(camera);
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
            waterRadius, _seed, _logger);
    }

    void CreateNearFieldGrassController(float waterRadius)
    {
        _grassNearFieldController = new GrassNearFieldController(_planetTransform, _chunkedProvider,
            _surfaceArrays.GrassParamsBuffer, _surfaceArrays.SliceCount,
            waterRadius, SettingsProvider.GetSettings<PlanetDto>().PlanetRadius, _seed, _logger);
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
    }

    void ApplyBlanketState(Material mat)
    {
        if (mat == null)
            return;
        float strength = _grassEnabled && _grassBlanketEnabled ? GrassFarOverlayStrength : 0f;
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
    }
}
