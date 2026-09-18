using System.Collections.Generic;
using System.Threading;
using UnityEngine;

// Owns the planet's water body: the "Water" GameObject + its cloned ocean material, and the
// async (pure-CPU) water-mesh build driven during generation. Split out of Planet (slice 6).
// The Ocean shader itself is untouched — this only relocates the C# that builds the mesh and
// configures the material. The orchestrator hands in the per-face samplers and climate provider.
public sealed class PlanetWaterSurface
{
    readonly Transform _planetTransform;
    readonly IWaterQueryService _waterQuery;
    ILogger Logger => LoggerProvider.Get();

    GameObject _waterObject;
    Mesh _waterMesh;
    Material _waterMaterial;
    public Material SurfaceMaterial => _waterMaterial;

    readonly Shader _oceanShader;
    readonly Shader _urpLitShader;
    readonly Shader _standardShader;

    static readonly int _shallowColorId = Shader.PropertyToID("_ShallowColor");
    static readonly int _deepColorId = Shader.PropertyToID("_DeepColor");
    static readonly int _foamColorId = Shader.PropertyToID("_FoamColor");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _waveAmplitudeId = Shader.PropertyToID(ShaderGlobalIds.WaveAmplitude);
    static readonly int _waveScaleId = Shader.PropertyToID(ShaderGlobalIds.WaveScale);
    static readonly int _waveSpeedId = Shader.PropertyToID(ShaderGlobalIds.WaveSpeed);
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _swellAmplitudeId = Shader.PropertyToID(ShaderGlobalIds.SwellAmplitude);
    static readonly int _swellWavelengthId = Shader.PropertyToID(ShaderGlobalIds.SwellWavelength);
    static readonly int _waterSurfaceOffsetId = Shader.PropertyToID(ShaderGlobalIds.WaterSurfaceOffset);
    static readonly int _waterEdgeFadeStartId = Shader.PropertyToID(ShaderGlobalIds.WaterEdgeFadeStart);
    static readonly int _waterEdgeFadeEndId = Shader.PropertyToID(ShaderGlobalIds.WaterEdgeFadeEnd);
    static readonly int _waterEdgeFadeEndOceanId = Shader.PropertyToID(ShaderGlobalIds.WaterEdgeFadeEndOcean);
    static readonly int _underwaterNightScaleId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterNightScale);
    static readonly int _underwaterShaftIntensityId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterShaftIntensity);
    static readonly int _underwaterFogColorId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterFogColor);
    static readonly int _underwaterVisibilityId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterVisibility);
    static readonly int _underwaterShaftWidthId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterShaftWidth);
    static readonly int _underwaterSurfaceDetailId = Shader.PropertyToID(ShaderGlobalIds.UnderwaterSurfaceDetail);
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _sunGlitterPowerId = Shader.PropertyToID("_SunGlitterPower");
    static readonly int _shoreFoamIntensityId = Shader.PropertyToID("_ShoreFoamIntensity");
    static readonly int _whitecapIntensityId = Shader.PropertyToID("_WhitecapIntensity");
    static readonly int _oceanFocusModeId = Shader.PropertyToID(ShaderGlobalIds.OceanFocusMode);
    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");
    static readonly int _freezingEnabledId = Shader.PropertyToID(ShaderGlobalIds.FreezingEnabled);
    static readonly int _lakeFreezeStartId = Shader.PropertyToID(ShaderGlobalIds.LakeFreezeStart);
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID(ShaderGlobalIds.LakeFreezeComplete);
    static readonly int _oceanFreezeStartId = Shader.PropertyToID(ShaderGlobalIds.OceanFreezeStart);
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID(ShaderGlobalIds.OceanFreezeComplete);
    static readonly int _iceTintId = Shader.PropertyToID("_IceTint");
    static readonly int _iceOpacityId = Shader.PropertyToID("_IceOpacity");
    static readonly int _iceRoughnessId = Shader.PropertyToID("_IceRoughness");
    static readonly int _iceNormalStrengthId = Shader.PropertyToID("_IceNormalStrength");
    static readonly int _iceBreakupScaleId = Shader.PropertyToID("_IceBreakupScale");
    static readonly int _frozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.FrozenWaterBodies);
    static readonly int _partiallyFrozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.PartiallyFrozenWaterBodies);
    static readonly int _liquidWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.LiquidWaterBodies);

    readonly WaterLevelTexture _levelTexture = new();

    // Wider companion field. Grass fades out approaching water and that fade cannot finish inside the tight
    // field - see WaterBodyMap.ShoreLevelGrid for what that produced.
    readonly WaterLevelTexture _shoreLevelTexture = new(
        ShaderGlobalIds.ShoreLevelTex, ShaderGlobalIds.ShoreLevelRes,
        ShaderGlobalIds.WaterLevelBaseRadius, "ShoreLevelField");



    public PlanetWaterSurface(Transform planetTransform, IWaterQueryService waterQuery = null)
    {
        _planetTransform = planetTransform;
        _waterQuery = waterQuery;
        _oceanShader = Shader.Find("Planet/Ocean");
        _urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        _standardShader = Shader.Find("Standard");
    }

    // The planet destroys all child GameObjects on regen (DestroyChildren), which kills the water
    // object; drop the stale reference so the next GenerateAsync rebuilds it.
    public void NotifyChildrenDestroyed()
    {
        if (_waterObject != null) WaterSurfaceRegistry.Unregister(_waterObject.GetComponent<MeshFilter>());
        ReleaseMesh();
        _waterObject = null;
    }

    public async Awaitable GenerateAsync(
        IReadOnlyList<IFaceMeshSampler> faceSamplers,
        IClimateProvider climateProvider,
        int perFaceResolution,
        IProgressHandle progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Shader.SetGlobalInt(_frozenWaterBodiesId, 0);
        Shader.SetGlobalInt(_partiallyFrozenWaterBodiesId, 0);
        Shader.SetGlobalInt(_liquidWaterBodiesId, 0);

        var planet = SettingsProvider.GetSettings<PlanetDto>();
        if (!planet.HasOceans)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }

        if (_waterObject == null)
        {
            _waterObject = new GameObject("Water");
            _waterObject.transform.SetParent(_planetTransform, false);
            var waterRenderer = _waterObject.AddComponent<MeshRenderer>();
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            _waterObject.AddComponent<MeshFilter>();
        }

        _waterObject.SetActive(true);
        _waterObject.transform.localScale = Vector3.one;
        _waterObject.transform.localPosition = Vector3.zero;

        var meshFilter = _waterObject.GetComponent<MeshFilter>();
        if (_waterMesh == null)
            _waterMesh = new Mesh { name = "WaterBodies" };
        meshFilter.sharedMesh = _waterMesh;
        WaterSurfaceRegistry.Register(meshFilter);

        var water = SettingsProvider.GetSettings<WaterDto>();
        float waterScale = GetWaterDistanceScale();
        // One expression, shared with WaterQueryService and published to the shaders, so the mesh, gameplay
        // and every shader agree on where the surface is.
        float surfaceOffset = WaterMeshBuilder.SurfaceOffsetFor(planet.PlanetRadius);
        Shader.SetGlobalFloat(_waterSurfaceOffsetId, surfaceOffset);

        var buildSettings = new WaterMeshBuilder.Settings
        {
            PlanetRadius = planet.PlanetRadius,
            OceanLevel = planet.OceanLevel,
            DeepDepth = water.DeepDepth * waterScale,
            ShoreRange = water.ShoreRange * waterScale,
            SurfaceOffset = surfaceOffset,
            OceanBodyVertexThreshold = Mathf.Max(48, perFaceResolution * perFaceResolution / 28),
            ClimateProvider = climateProvider,
            EnableFreezing = planet.EnableFrozenWater,
            LakeFreezeStartTemperature01 = water.LakeFreezeStartTemperature01,
            LakeFreezeCompleteTemperature01 = water.LakeFreezeCompleteTemperature01,
            OceanFreezeStartTemperature01 = water.OceanFreezeStartTemperature01,
            OceanFreezeCompleteTemperature01 = water.OceanFreezeCompleteTemperature01,
            Levels = WaterBodyMap.Current
        };

        // Water builder reads per-face vertex/elevation grids via IFaceMeshSampler. Both
        // resolution modes (Low/High) supply this view; chunked path wraps each root chunk.
        if (faceSamplers == null || faceSamplers.Count == 0)
        {
            if (_waterObject != null) _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }
        var terrainFaces = new IFaceMeshSampler[faceSamplers.Count];
        for (int i = 0; i < faceSamplers.Count; i++) terrainFaces[i] = faceSamplers[i];
        var waterMesh = meshFilter.sharedMesh;

        // Run the (pure-CPU) water build on a worker and poll its progress each frame on the main
        // thread so the loading bar advances through the heavy global-graph + per-face phases.
        progress?.Report(0f, "Building water bodies...");
        float buildProgress = 0f;
        var buildTask = BuildWaterMeshAsync(
            terrainFaces, buildSettings,
            p => System.Threading.Volatile.Write(ref buildProgress, p), ct);
        var buildAwaiter = buildTask.GetAwaiter();
        // The worker observes cancellation. Drain it before returning so no work is orphaned.
        while (!buildAwaiter.IsCompleted)
        {
            progress?.Report(0.6f * System.Threading.Volatile.Read(ref buildProgress), "Building water bodies...");
            await Awaitable.NextFrameAsync();
        }
        var waterMeshData = buildAwaiter.GetResult();
        ct.ThrowIfCancellationRequested();
        if (_waterObject == null) return;
        // Publish the captured level fields and mesh together after successful computation.
        _levelTexture.Publish(buildSettings.Levels?.LevelGrid, WaterBodyMap.Resolution, planet.PlanetRadius);
        _shoreLevelTexture.Publish(buildSettings.Levels?.ShoreLevelGrid, WaterBodyMap.Resolution, planet.PlanetRadius);
        progress?.Report(0.7f, "Uploading water mesh...");

        if (waterMeshData.Stats.Triangles == 0)
        {
            _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }

        WaterMeshBuilder.Apply(waterMesh, waterMeshData);
        Shader.SetGlobalInt(_frozenWaterBodiesId, waterMeshData.Stats.FrozenBodies);
        Shader.SetGlobalInt(_partiallyFrozenWaterBodiesId, waterMeshData.Stats.PartiallyFrozenBodies);
        Shader.SetGlobalInt(_liquidWaterBodiesId, waterMeshData.Stats.LiquidBodies);
        progress?.Report(0.9f, "Configuring water...");

        Logger.Log(LogLevel.Debug, "Water",
            $"Generated water mesh: {waterMeshData.Stats.MeshVertices} verts, {waterMeshData.Stats.Triangles} tris, " +
            $"wet terrain verts {waterMeshData.Stats.WetVertices}, ocean bodies {waterMeshData.Stats.OceanBodies}, " +
            $"small bodies {waterMeshData.Stats.SmallBodies}, frozen/partial/liquid " +
            $"{waterMeshData.Stats.FrozenBodies}/{waterMeshData.Stats.PartiallyFrozenBodies}/{waterMeshData.Stats.LiquidBodies}, " +
            $"water temp {waterMeshData.Stats.MinWaterTemperature01:F3}-{waterMeshData.Stats.MaxWaterTemperature01:F3} " +
            $"avg {waterMeshData.Stats.AverageWaterTemperature01:F3}, max depth {waterMeshData.Stats.MaxDepth:F1}");

        // The volume prepass packs shore01 and body01 into one channel, which only decodes unambiguously
        // while body01 stays near 0 or 1. Measured 0.02% on the reference world, so the pack is sound
        // today; a body class that lands mid-range (per-body levels, rivers) breaks it silently, and this
        // is the only place that would notice. See docs/design/2026-08-18-water-data-contract.md.
        int ambiguous = waterMeshData.Stats.AmbiguousBodyVertices;
        if (ambiguous > waterMeshData.Stats.MeshVertices / 200)
            Logger.Log(LogLevel.Warning, "Water",
                $"{ambiguous} of {waterMeshData.Stats.MeshVertices} water vertices carry an intermediate body " +
                $"factor ({100f * ambiguous / Mathf.Max(waterMeshData.Stats.MeshVertices, 1):F2}%). The volume " +
                "prepass packing decodes those as the wrong body type; the channel needs splitting.");

        var renderer = _waterObject.GetComponent<Renderer>();
        if (_waterMaterial == null ||
            _waterMaterial.name == "Default-Material" ||
            (_oceanShader != null && _waterMaterial.shader != _oceanShader))
        {
            if (_waterMaterial != null) Object.Destroy(_waterMaterial);
            _waterMaterial = CreateWaterMaterial();
        }
        renderer.sharedMaterial = _waterMaterial;
        UpdateWaterMaterial(_waterMaterial);
        _waterObject.layer = LayerMask.NameToLayer("Water");
        var presentation = _waterObject.GetComponent<WaterPresentationController>();
        if (presentation == null) presentation = _waterObject.AddComponent<WaterPresentationController>();
        presentation.Initialize(_waterQuery, _planetTransform);
        progress?.Report(1f, "Water ready.");
    }

    static async Awaitable<WaterMeshBuilder.MeshData> BuildWaterMeshAsync(
        IFaceMeshSampler[] terrainFaces,
        WaterMeshBuilder.Settings buildSettings,
        System.Action<float> onProgress,
        CancellationToken ct)
    {
        await Awaitable.BackgroundThreadAsync();
        try
        {
            return WaterMeshBuilder.Compute(terrainFaces, buildSettings, onProgress, ct);
        }
        finally
        {
            await Awaitable.MainThreadAsync();
        }
    }

    Material CreateWaterMaterial()
    {
        var shader = _oceanShader != null ? _oceanShader
                   : _urpLitShader != null ? _urpLitShader
                   : _standardShader;
        var mat = new Material(shader) { name = "Water" };
        return mat;
    }

    void UpdateWaterMaterial(Material mat)
    {
        var planet = SettingsProvider.GetSettings<PlanetDto>();
        var water = SettingsProvider.GetSettings<WaterDto>();
        var color = planet.WaterColor;
        float waterScale = GetWaterDistanceScale();
        if (mat.HasProperty(_shallowColorId))
        {
            Color shallow = Color.Lerp(color, water.ShallowBaseColor, water.ShallowColorBlend);
            shallow.a = Mathf.Clamp01(Mathf.Max(color.a * water.ShallowAlphaFactor, water.ShallowAlphaMin));
            Color deep = Color.Lerp(color, water.DeepBaseColor, water.DeepColorBlend);
            deep.a = Mathf.Clamp01(Mathf.Max(color.a, water.DeepAlphaMin));

            mat.SetColor(_shallowColorId, shallow);
            mat.SetColor(_deepColorId, deep);
            mat.SetColor(_foamColorId, water.FoamColor);
            mat.SetFloat(_shallowDepthId, water.ShallowDepth * waterScale);
            mat.SetFloat(_deepDepthId, water.DeepDepth * waterScale);
            mat.SetFloat(_shoreFoamDepthId, water.ShoreFoamDepth * waterScale);
            mat.SetFloat(_shoreFoamSoftnessId, water.ShoreRange * waterScale);
            Shader.SetGlobalFloat(_waveAmplitudeId, water.WaveAmplitude * waterScale);
            Shader.SetGlobalFloat(_waveScaleId, water.WaveScale * waterScale);
            Shader.SetGlobalFloat(_waveSpeedId, water.WaveSpeed);
            mat.SetFloat(_waveNormalStrengthId, water.WaveNormalStrength);
            Shader.SetGlobalFloat(_swellAmplitudeId, water.SwellAmplitude);
            Shader.SetGlobalFloat(_swellWavelengthId, water.SwellWavelength);
            Shader.SetGlobalFloat(_waterEdgeFadeStartId, water.EdgeFadeStartMeters / Mathf.Max(water.DeepDepth, 0.001f));
            Shader.SetGlobalFloat(_waterEdgeFadeEndId, water.EdgeFadeEndMeters / Mathf.Max(water.DeepDepth, 0.001f));
            Shader.SetGlobalFloat(_waterEdgeFadeEndOceanId, water.OceanEdgeFadeEndMeters / Mathf.Max(water.DeepDepth, 0.001f));
            Shader.SetGlobalFloat(_underwaterNightScaleId, water.UnderwaterNightScale);
            Shader.SetGlobalFloat(_underwaterShaftIntensityId, water.UnderwaterShaftIntensity);
            Shader.SetGlobalColor(_underwaterFogColorId, water.UnderwaterFogColor);
            Shader.SetGlobalFloat(_underwaterVisibilityId, water.UnderwaterVisibility);
            Shader.SetGlobalFloat(_underwaterShaftWidthId, water.UnderwaterShaftWidth);
            Shader.SetGlobalFloat(_underwaterSurfaceDetailId, water.UnderwaterSurfaceDetail);
            mat.SetFloat(_waterMotionStrengthId, water.MotionStrength);
            mat.SetFloat(_sunGlitterIntensityId, water.SunGlitterIntensity);
            mat.SetFloat(_sunGlitterPowerId, water.SunGlitterPower);
            mat.SetFloat(_shoreFoamIntensityId, water.ShoreFoamIntensity);
            mat.SetFloat(_whitecapIntensityId, water.WhitecapIntensity);
            mat.SetFloat(_oceanFocusModeId, 1f);
            Shader.SetGlobalFloat(_waterFocusModeId, 0f);
            mat.SetFloat(_alphaId, water.Alpha);
            Shader.SetGlobalFloat(_freezingEnabledId, planet.EnableFrozenWater ? 1f : 0f);
            Shader.SetGlobalFloat(_lakeFreezeStartId, water.LakeFreezeStartTemperature01);
            Shader.SetGlobalFloat(_lakeFreezeCompleteId, water.LakeFreezeCompleteTemperature01);
            Shader.SetGlobalFloat(_oceanFreezeStartId, water.OceanFreezeStartTemperature01);
            Shader.SetGlobalFloat(_oceanFreezeCompleteId, water.OceanFreezeCompleteTemperature01);
            mat.SetColor(_iceTintId, planet.IceTint);
            mat.SetFloat(_iceOpacityId, water.IceOpacity);
            mat.SetFloat(_iceRoughnessId, water.IceRoughness);
            mat.SetFloat(_iceNormalStrengthId, water.IceNormalStrength);
            mat.SetFloat(_iceBreakupScaleId, water.IceBreakupScale * waterScale);
            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            Logger.Log(LogLevel.Debug, "Water", "Applied integrated ocean mode: clouds, rain, and terrain cloud shadows enabled; focused water rendering retained.");
            return;
        }

        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetColor("_BaseColor", color);
        mat.renderQueue = 3000;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
    }

    static float GetWaterDistanceScale()
    {
        if (!SettingsProvider.IsRegistered<PlanetDto>() || !SettingsProvider.IsRegistered<WaterDto>())
            return 1f;

        return SettingsProvider.GetSettings<WaterDto>()
            .DistanceScale(SettingsProvider.GetSettings<PlanetDto>().PlanetRadius);
    }

    public void ApplySettings(WaterDto settings)
    {
        if (settings == null) throw new System.ArgumentNullException(nameof(settings));
        SettingsProvider.Update(settings);
        if (_waterMaterial != null) UpdateWaterMaterial(_waterMaterial);
    }

    public void Dispose()
    {
        if (_waterObject != null) WaterSurfaceRegistry.Unregister(_waterObject.GetComponent<MeshFilter>());
        ReleaseMesh();
        if (_waterMaterial != null)
        {
            Object.Destroy(_waterMaterial);
            _waterMaterial = null;
        }
        // _waterObject is a child of the planet transform and is destroyed with the planet.
        _waterObject = null;
        _levelTexture.Dispose();
        _shoreLevelTexture.Dispose();
    }

    void ReleaseMesh()
    {
        if (_waterMesh == null) return;
        if (Application.isPlaying) Object.Destroy(_waterMesh);
        else Object.DestroyImmediate(_waterMesh);
        _waterMesh = null;
    }
}
