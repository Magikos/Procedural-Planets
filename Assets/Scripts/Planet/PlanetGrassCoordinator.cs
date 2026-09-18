using UnityEngine;

// Owns the planet's grass LOD layers: the chunk-following GrassPlacementController, the
// camera-centered GrassNearFieldController, and the far terrain-blanket overlay baked into the
// terrain material. Tracks the master/per-layer enable flags and drives altitude-based activation
// of the near field. Planet forwards Configure/Tick/Dispose and the IGrassRuntimeControl surface.
[CommandPrefix("grass", Group = "Vegetation and wildlife", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
sealed class PlanetGrassCoordinator : IGrassNearFieldStatsProvider
{
    readonly Transform _planetTransform;
    readonly IPlanetSurfaceSampler _surfaceSampler;
    readonly ILogger _logger;

    GrassPlacementController _grassController;
    GrassNearFieldController _grassNearFieldController;
    bool _grassEnabled = true;
    bool _nearFieldGrassEnabled = true;
    bool _chunkGrassEnabled = false;
    // The far grass-surface overlay ("blanket") paints grass tint + procedural blade-fiber onto the
    // terrain beyond the near-blade range, so grassland reads as a grass carpet at distance instead of
    // bare ground. Its coverage gate is a soft proportional ramp (PlanetVertexColor.shader) so the
    // grassy->arid transition is a gradient, not the hard biome-edge stripe that parked it before.
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
    static readonly int _grassSurfaceSaturationId = Shader.PropertyToID("_GrassSurfaceSaturation");
    static readonly int _bladeSaturationId = Shader.PropertyToID(ShaderGlobalIds.GrassBladeSaturation);
    static readonly int _bladeBrightnessId = Shader.PropertyToID(ShaderGlobalIds.GrassBladeBrightness);
    static readonly int _grassWaterRadiusId = Shader.PropertyToID("_GrassWaterRadius");
    static readonly int _biomeGrassParamCountId = Shader.PropertyToID(ShaderGlobalIds.BiomeGrassParamCount);

    // Live-tunable via grass.* console commands (see bottom of file). The default aims the
    // painted surface at the aggregate blade canopy; close blade gaps still expose terrain.
    float _farOverlayStrength = 1.0f;
    float _grassSurfaceBrightness = 0.6f;
    float _grassSurfaceSaturation = 0.72f; // green-over-tan biome-edge line lever; lower trims the vivid pop
    // 3D blade grade. Authored per-biome tints read neon on the lush biomes; these pull the whole set earthier.
    float _bladeSaturation = 0.78f;
    float _bladeBrightness = 0.88f;

    // The painted grass is a BASE LAYER at full strength everywhere, not a distance ramp. It used to fade in
    // over 24..120 m, which put a coverage gradient at a fixed radius around the camera — so the handoff to the
    // blades was a ring you dragged around with you as you walked, and no amount of colour matching can hide a
    // moving edge. With coverage constant, nothing about the ground changes with camera distance: the blades
    // simply add geometry on top of paint that already reads as grass, and their fade has nothing to reveal.
    // Which biomes get painted is a separate question, still handled by the biome/greenness gate in the shader.
    const float GrassFarOverlayStart = 0f;
    const float GrassFarOverlayEnd = 1f;
    const float GrassFarOverlayNoiseScale = 0.055f;
    const float GrassFarOverlayFiberStrength = 0.65f;
    const float GrassFarOverlayOrbitStrength = 0.42f;

    // Resolved lazily on first use: the coordinator is constructed in Planet.Awake,
    // before GameBootstrap.EarlyInitialize registers IGrassQualitySettings.
    IGrassQualitySettings _quality;
    IGrassQualitySettings Quality => _quality ??= ServiceLocator.Get<IGrassQualitySettings>();
    PlanetDto _planetDto;

    public PlanetGrassCoordinator(Transform planetTransform, IPlanetSurfaceSampler surfaceSampler, ILogger logger)
    {
        _planetTransform = planetTransform;
        _surfaceSampler = surfaceSampler;
        _logger = logger;
        EventBus<SettingsChangedEvent>.Listen(OnSettingsChanged);
        ConsoleRegistry.RegisterInstance(this);
    }

    void OnSettingsChanged(SettingsChangedEvent evt)
    {
        if (evt.DtoType != typeof(PlanetDto)) return;
        _planetDto = SettingsProvider.GetSettings<PlanetDto>();
    }

    public void Configure(ChunkedSurfaceProvider provider, BiomeSurfaceTextureArrays surfaceArrays,
        int seed, Camera observerCamera, Material terrainMaterial)
    {
        DisposeControllers();
        _chunkedProvider = provider;
        _surfaceArrays = surfaceArrays;
        _seed = seed;
        _terrainMaterial = terrainMaterial;
        // Before any placement dispatch: the compute reads these when it writes blade.Color, and a blade keeps
        // the colour it was born with, so setting them afterwards would only affect blades placed later.
        PushBladeGrade();

        if (provider == null)
            return;

        _planetDto = SettingsProvider.GetSettings<PlanetDto>();
        float waterRadius = ComputeWaterRadius(_planetDto);
        if (_grassEnabled && _nearFieldGrassEnabled
            && observerCamera != null
            && ShouldActivateNearFieldGrass(observerCamera.transform.position, false))
        {
            CreateNearFieldGrassController(waterRadius);
        }

        if (_grassEnabled && _chunkGrassEnabled)
            CreateChunkGrassController(waterRadius);

        if (_terrainMaterial != null)
            ApplyTerrainOverlay(_terrainMaterial);
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

        float waterRadius = ComputeWaterRadius(_planetDto);

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

        // Blades fade to zero alpha across the altitude band below the activation gate,
        // so the create/dispose above never pops a visible layer in or out.
        if (_grassNearFieldController != null
            && TryGetCameraAltitude(camera.transform.position, out float altitude))
        {
            float fade = 1f - Mathf.InverseLerp(
                Quality.NearFieldFadeAltitudeStart,
                Mathf.Max(Quality.NearFieldActivationAltitude, Quality.NearFieldFadeAltitudeStart + 1f),
                altitude);
            _grassNearFieldController.SetAltitudeFade(Mathf.SmoothStep(0f, 1f, fade));
        }
    }

    static float ComputeWaterRadius(PlanetDto planet)
    {
        return planet != null && planet.HasOceans
            ? planet.PlanetRadius * (1f + planet.OceanLevel)
            : -1f;
    }

    bool ShouldActivateNearFieldGrass(Vector3 cameraPosition, bool currentlyActive)
    {
        if (!TryGetCameraAltitude(cameraPosition, out float altitude))
            return currentlyActive;

        float threshold = currentlyActive
            ? Quality.NearFieldDeactivationAltitude
            : Quality.NearFieldActivationAltitude;
        return altitude <= threshold;
    }

    bool TryGetCameraAltitude(Vector3 cameraPosition, out float altitude)
    {
        altitude = 0f;
        Vector3 fromCenter = cameraPosition - _planetTransform.position;
        if (fromCenter.sqrMagnitude < 0.0001f)
            return true;

        if (!_surfaceSampler.TryGetSurfaceRadius(fromCenter.normalized, out float surfaceRadius))
            return false;

        altitude = Mathf.Max(0f, fromCenter.magnitude - surfaceRadius);
        return true;
    }

    void CreateChunkGrassController(float waterRadius)
    {
        _grassController = new GrassPlacementController(_planetTransform, _chunkedProvider,
            _surfaceArrays.GrassParamsBuffer, _surfaceArrays.SliceCount,
            waterRadius, _seed, this, _logger);
    }

    void CreateNearFieldGrassController(float waterRadius)
    {
        if (_planetDto == null)
            return;

        _grassNearFieldController = new GrassNearFieldController(_planetTransform, _chunkedProvider,
            _surfaceArrays.GrassParamsBuffer, _surfaceArrays.SliceCount,
            waterRadius, _planetDto.PlanetRadius, _seed, _logger);
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
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeStartId, Quality.FarOverlayAltitudeStart);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayAltitudeEndId, Quality.FarOverlayAltitudeEnd);
        SetMaterialFloatIfPresent(mat, _grassFarOverlayFiberStrengthId, GrassFarOverlayFiberStrength);
        SetMaterialFloatIfPresent(mat, _grassSurfaceBrightnessId, _grassSurfaceBrightness);
        SetMaterialFloatIfPresent(mat, _grassSurfaceSaturationId, _grassSurfaceSaturation);
        SetMaterialFloatIfPresent(mat, _grassWaterRadiusId, ComputeWaterRadius(_planetDto));
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

    public void InvalidateSurfaceMasks()
    {
        _grassNearFieldController?.RequestRedispatch();
        _grassController?.RequestRedispatch();
    }

    // Dispose the GPU grass controllers so the next Tick recreates them with a fresh Material,
    // compute reference, and GraphicsBuffers. The controllers cache those once in their ctor with no
    // reload recovery, so an in-editor shader/compute reimport staled them and the blades stop drawing;
    // this rebuilds without a full scene restart. Editor-iteration aid, harmless at runtime.
    public void Rebuild()
    {
        DisposeControllers();
        PushBladeGrade();
        if (_terrainMaterial != null)
            ApplyTerrainOverlay(_terrainMaterial);
    }

    void PushBladeGrade()
    {
        Shader.SetGlobalFloat(_bladeSaturationId, _bladeSaturation);
        Shader.SetGlobalFloat(_bladeBrightnessId, _bladeBrightness);
    }

    public void Dispose()
    {
        DisposeControllers();
        EventBus<SettingsChangedEvent>.Unlisten(OnSettingsChanged);
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

    [ConsoleCommand("surface-saturation", "Painted grass-surface saturation (0-1). Lower to trim the vivid green biome-edge line over tan ground.", MonoTargetType.Registry)]
    string SurfaceSaturationCmd(float? value = null)
    {
        if (value.HasValue) { _grassSurfaceSaturation = Mathf.Clamp01(value.Value); ReapplyOverlay(); }
        return $"grass surface-saturation: {_grassSurfaceSaturation:F3}";
    }

    // The 3D blades, NOT the painted surface. Their colour is the per-biome authored Tint, which reads neon on
    // the lush biomes; this grades the whole set toward earthier green without re-authoring 14 biomes. Blades
    // are written into a buffer at placement time, so both setters rebuild.
    [ConsoleCommand("blade-saturation", "3D grass blade saturation (0-1). Lower for earthier, less neon green. Default 0.78.", MonoTargetType.Registry)]
    string BladeSaturationCmd(float? value = null)
    {
        if (value.HasValue)
        {
            _bladeSaturation = Mathf.Clamp01(value.Value);
            Shader.SetGlobalFloat(_bladeSaturationId, _bladeSaturation);
            Rebuild();
        }
        return $"grass blade-saturation: {_bladeSaturation:F3}";
    }

    [ConsoleCommand("blade-brightness", "3D grass blade brightness (0.3-1.5). Lower to stop blades glowing against the ground. Default 0.88.", MonoTargetType.Registry)]
    string BladeBrightnessCmd(float? value = null)
    {
        if (value.HasValue)
        {
            _bladeBrightness = Mathf.Clamp(value.Value, 0.3f, 1.5f);
            Shader.SetGlobalFloat(_bladeBrightnessId, _bladeBrightness);
            Rebuild();
        }
        return $"grass blade-brightness: {_bladeBrightness:F3}";
    }

    [ConsoleCommand("overlay-status", "Print the live grass-line overlay tuning values.", MonoTargetType.Registry)]
    string OverlayStatusCmd()
    {
        if (_terrainMaterial == null)
            return $"strength={_farOverlayStrength:F3} surface-brightness={_grassSurfaceBrightness:F3} surface-saturation={_grassSurfaceSaturation:F3} material=<none>";

        bool hasStrength = _terrainMaterial.HasProperty(_grassFarOverlayStrengthId);
        bool hasBrightness = _terrainMaterial.HasProperty(_grassSurfaceBrightnessId);
        string materialStrength = hasStrength
            ? _terrainMaterial.GetFloat(_grassFarOverlayStrengthId).ToString("F3")
            : "missing";
        string materialBrightness = hasBrightness
            ? _terrainMaterial.GetFloat(_grassSurfaceBrightnessId).ToString("F3")
            : "missing";
        bool textureMode = _terrainMaterial.IsKeywordEnabled("_BIOME_COLOR_MODE_TEXTURE");
        int grassParamCount = Shader.GetGlobalInt(_biomeGrassParamCountId);
        string waterRadius = _terrainMaterial.HasProperty(_grassWaterRadiusId)
            ? _terrainMaterial.GetFloat(_grassWaterRadiusId).ToString("F2")
            : "missing";
        bool hasSaturation = _terrainMaterial.HasProperty(_grassSurfaceSaturationId);
        string materialSaturation = hasSaturation
            ? _terrainMaterial.GetFloat(_grassSurfaceSaturationId).ToString("F3")
            : "missing";
        return $"strength={_farOverlayStrength:F3} surface-brightness={_grassSurfaceBrightness:F3} surface-saturation={_grassSurfaceSaturation:F3} materialStrength={materialStrength} materialBrightness={materialBrightness} materialSaturation={materialSaturation} textureMode={textureMode} grassParamCount={grassParamCount} waterRadius={waterRadius}";
    }
}
