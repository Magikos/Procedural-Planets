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
    ILogger Logger => LoggerProvider.Get();

    GameObject _waterObject;
    Material _waterMaterial;

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
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveScaleId = Shader.PropertyToID("_WaveScale");
    static readonly int _waveSpeedId = Shader.PropertyToID("_WaveSpeed");
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _sunGlitterPowerId = Shader.PropertyToID("_SunGlitterPower");
    static readonly int _shoreFoamIntensityId = Shader.PropertyToID("_ShoreFoamIntensity");
    static readonly int _whitecapIntensityId = Shader.PropertyToID("_WhitecapIntensity");
    static readonly int _wakeFoamIntensityId = Shader.PropertyToID("_WakeFoamIntensity");
    static readonly int _wakeNormalStrengthId = Shader.PropertyToID("_WakeNormalStrength");
    static readonly int _oceanFocusModeId = Shader.PropertyToID(ShaderGlobalIds.OceanFocusMode);
    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");
    static readonly int _freezingEnabledId = Shader.PropertyToID("_FreezingEnabled");
    static readonly int _lakeFreezeStartId = Shader.PropertyToID("_LakeFreezeStart");
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID("_LakeFreezeComplete");
    static readonly int _oceanFreezeStartId = Shader.PropertyToID("_OceanFreezeStart");
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID("_OceanFreezeComplete");
    static readonly int _iceTintId = Shader.PropertyToID("_IceTint");
    static readonly int _iceOpacityId = Shader.PropertyToID("_IceOpacity");
    static readonly int _iceRoughnessId = Shader.PropertyToID("_IceRoughness");
    static readonly int _iceNormalStrengthId = Shader.PropertyToID("_IceNormalStrength");
    static readonly int _iceBreakupScaleId = Shader.PropertyToID("_IceBreakupScale");
    static readonly int _frozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.FrozenWaterBodies);
    static readonly int _partiallyFrozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.PartiallyFrozenWaterBodies);
    static readonly int _liquidWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.LiquidWaterBodies);

    static readonly Color _waterShallowBaseColor = new Color(0.20f, 0.76f, 0.82f, 1f);
    static readonly Color _waterDeepBaseColor = new Color(0.00f, 0.018f, 0.065f, 1f);
    static readonly Color _waterFoamColor = new Color(0.88f, 0.98f, 0.94f, 0.9f);

    const float WaterReferenceRadius = 5000f;
    const float WaterShallowDepth = 28f;
    const float WaterDeepDepth = 360f;
    const float WaterShoreFoamDepth = 32f;
    const float WaterShoreRange = 125f;
    const float WaterWaveAmplitude = 3.4f;
    const float WaterWaveScale = 480f;
    const float WaterWaveSpeed = 0.58f;
    const float WaterWaveNormalStrength = 4.5f;
    const float WaterMotionStrength = 0.24f;
    const float WaterSunGlitterIntensity = 1.45f;
    const float WaterSunGlitterPower = 1400f;
    const float WaterShoreFoamIntensity = 1.0f;
    const float WaterWhitecapIntensity = 1.08f;
    const float WaterWakeFoamIntensity = 1.0f;
    const float WaterWakeNormalStrength = 1.0f;
    const float WaterAlpha = 0.36f;
    const float WaterShallowColorBlend = 0.68f;
    const float WaterDeepColorBlend = 0.88f;
    const float WaterShallowAlphaFactor = 0.14f;
    const float WaterShallowAlphaMin = 0.10f;
    const float WaterDeepAlphaMin = 0.96f;

    public PlanetWaterSurface(Transform planetTransform)
    {
        _planetTransform = planetTransform;
        _oceanShader = Shader.Find("Planet/Ocean");
        _urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        _standardShader = Shader.Find("Standard");
    }

    // The planet destroys all child GameObjects on regen (DestroyChildren), which kills the water
    // object; drop the stale reference so the next GenerateAsync rebuilds it.
    public void NotifyChildrenDestroyed()
    {
        _waterObject = null;
    }

    public async Awaitable GenerateAsync(
        IReadOnlyList<IFaceMeshSampler> faceSamplers,
        IClimateProvider climateProvider,
        int perFaceResolution,
        IProgressHandle progress,
        CancellationToken ct)
    {
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
            _waterObject.transform.parent = _planetTransform;
            _waterObject.transform.localPosition = Vector3.zero;
            var waterRenderer = _waterObject.AddComponent<MeshRenderer>();
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            _waterObject.AddComponent<MeshFilter>();
        }

        _waterObject.SetActive(true);
        _waterObject.transform.localScale = Vector3.one;
        _waterObject.transform.localPosition = Vector3.zero;

        var meshFilter = _waterObject.GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh { name = "WaterBodies" };

        float waterScale = GetWaterDistanceScale();
        var buildSettings = new WaterMeshBuilder.Settings
        {
            PlanetRadius = planet.PlanetRadius,
            OceanLevel = planet.OceanLevel,
            DeepDepth = WaterDeepDepth * waterScale,
            ShoreRange = WaterShoreRange * waterScale,
            SurfaceOffset = Mathf.Max(planet.PlanetRadius * 0.00003f, 0.02f),
            OceanBodyVertexThreshold = Mathf.Max(48, perFaceResolution * perFaceResolution / 28),
            ClimateProvider = climateProvider,
            EnableFreezing = planet.EnableFrozenWater,
            LakeFreezeStartTemperature01 = PlanetConstants.LakeFreezeStartTemperature01,
            LakeFreezeCompleteTemperature01 = PlanetConstants.LakeFreezeCompleteTemperature01,
            OceanFreezeStartTemperature01 = PlanetConstants.OceanFreezeStartTemperature01,
            OceanFreezeCompleteTemperature01 = PlanetConstants.OceanFreezeCompleteTemperature01
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
            p => System.Threading.Volatile.Write(ref buildProgress, p));
        var buildAwaiter = buildTask.GetAwaiter();
        while (!buildAwaiter.IsCompleted)
        {
            progress?.Report(0.6f * System.Threading.Volatile.Read(ref buildProgress), "Building water bodies...");
            await Awaitable.NextFrameAsync();
        }
        var waterMeshData = buildAwaiter.GetResult();
        ct.ThrowIfCancellationRequested();
        if (_waterObject == null) return;
        progress?.Report(0.7f, "Uploading water mesh...");

        if (waterMeshData.Stats.Triangles == 0)
        {
            _waterObject.SetActive(false);
            progress?.Report(1f, "Water skipped.");
            return;
        }

        WaterMeshBuilder.Apply(waterMesh, null, waterMeshData);
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
        progress?.Report(1f, "Water ready.");
    }

    static async Awaitable<WaterMeshBuilder.MeshData> BuildWaterMeshAsync(
        IFaceMeshSampler[] terrainFaces,
        WaterMeshBuilder.Settings buildSettings,
        System.Action<float> onProgress)
    {
        await Awaitable.BackgroundThreadAsync();
        var result = WaterMeshBuilder.Compute(terrainFaces, buildSettings, onProgress);
        await Awaitable.MainThreadAsync();
        return result;
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
        var color = planet.WaterColor;
        float waterScale = GetWaterDistanceScale();
        if (mat.HasProperty(_shallowColorId))
        {
            Color shallow = Color.Lerp(color, _waterShallowBaseColor, WaterShallowColorBlend);
            shallow.a = Mathf.Clamp01(Mathf.Max(color.a * WaterShallowAlphaFactor, WaterShallowAlphaMin));
            Color deep = Color.Lerp(color, _waterDeepBaseColor, WaterDeepColorBlend);
            deep.a = Mathf.Clamp01(Mathf.Max(color.a, WaterDeepAlphaMin));

            mat.SetColor(_shallowColorId, shallow);
            mat.SetColor(_deepColorId, deep);
            mat.SetColor(_foamColorId, _waterFoamColor);
            mat.SetFloat(_shallowDepthId, WaterShallowDepth * waterScale);
            mat.SetFloat(_deepDepthId, WaterDeepDepth * waterScale);
            mat.SetFloat(_shoreFoamDepthId, WaterShoreFoamDepth * waterScale);
            mat.SetFloat(_shoreFoamSoftnessId, WaterShoreRange * waterScale);
            mat.SetFloat(_waveAmplitudeId, WaterWaveAmplitude * waterScale);
            mat.SetFloat(_waveScaleId, WaterWaveScale * waterScale);
            mat.SetFloat(_waveSpeedId, WaterWaveSpeed);
            mat.SetFloat(_waveNormalStrengthId, WaterWaveNormalStrength);
            mat.SetFloat(_waterMotionStrengthId, WaterMotionStrength);
            mat.SetFloat(_sunGlitterIntensityId, WaterSunGlitterIntensity);
            mat.SetFloat(_sunGlitterPowerId, WaterSunGlitterPower);
            mat.SetFloat(_shoreFoamIntensityId, WaterShoreFoamIntensity);
            mat.SetFloat(_whitecapIntensityId, WaterWhitecapIntensity);
            mat.SetFloat(_wakeFoamIntensityId, WaterWakeFoamIntensity);
            mat.SetFloat(_wakeNormalStrengthId, WaterWakeNormalStrength);
            mat.SetFloat(_oceanFocusModeId, 1f);
            Shader.SetGlobalFloat(_waterFocusModeId, 0f);
            mat.SetFloat(_alphaId, WaterAlpha);
            mat.SetFloat(_freezingEnabledId, planet.EnableFrozenWater ? 1f : 0f);
            mat.SetFloat(_lakeFreezeStartId, PlanetConstants.LakeFreezeStartTemperature01);
            mat.SetFloat(_lakeFreezeCompleteId, PlanetConstants.LakeFreezeCompleteTemperature01);
            mat.SetFloat(_oceanFreezeStartId, PlanetConstants.OceanFreezeStartTemperature01);
            mat.SetFloat(_oceanFreezeCompleteId, PlanetConstants.OceanFreezeCompleteTemperature01);
            mat.SetColor(_iceTintId, planet.IceTint);
            mat.SetFloat(_iceOpacityId, PlanetConstants.IceOpacity);
            mat.SetFloat(_iceRoughnessId, PlanetConstants.IceRoughness);
            mat.SetFloat(_iceNormalStrengthId, PlanetConstants.IceNormalStrength);
            mat.SetFloat(_iceBreakupScaleId, PlanetConstants.IceBreakupScale * waterScale);
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

    float GetWaterDistanceScale()
    {
        if (!SettingsProvider.IsRegistered<PlanetDto>())
            return 1f;

        return Mathf.Max(SettingsProvider.GetSettings<PlanetDto>().PlanetRadius / WaterReferenceRadius, 0.0001f);
    }

    public void Dispose()
    {
        if (_waterMaterial != null)
        {
            Object.Destroy(_waterMaterial);
            _waterMaterial = null;
        }
        // _waterObject is a child of the planet transform and is destroyed with the planet.
        _waterObject = null;
    }
}
