using System.Collections.Generic;
using System.Threading;
using UnityEngine;

// Owns the planet's water body: the "Water" GameObject + its cloned ocean material, and the
// async (pure-CPU) water-mesh build driven during generation. Split out of Planet (slice 6).
// The Ocean shader itself is untouched — this only relocates the C# that builds the mesh and
// configures the material. The orchestrator hands in the per-face samplers and climate provider.
[CommandPrefix("water")]
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
    static readonly int _waveSpeedId = Shader.PropertyToID(ShaderGlobalIds.WaveSpeed);
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _swellAmplitudeId = Shader.PropertyToID(ShaderGlobalIds.SwellAmplitude);
    static readonly int _swellWavelengthId = Shader.PropertyToID(ShaderGlobalIds.SwellWavelength);
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



    public PlanetWaterSurface(Transform planetTransform)
    {
        _planetTransform = planetTransform;
        _oceanShader = Shader.Find("Planet/Ocean");
        _urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        _standardShader = Shader.Find("Standard");
        ConsoleRegistry.RegisterInstance(this);
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

        var water = SettingsProvider.GetSettings<WaterDto>();
        float waterScale = GetWaterDistanceScale();
        var buildSettings = new WaterMeshBuilder.Settings
        {
            PlanetRadius = planet.PlanetRadius,
            OceanLevel = planet.OceanLevel,
            DeepDepth = water.DeepDepth * waterScale,
            ShoreRange = water.ShoreRange * waterScale,
            SurfaceOffset = Mathf.Max(planet.PlanetRadius * 0.00003f, 0.02f),
            OceanBodyVertexThreshold = Mathf.Max(48, perFaceResolution * perFaceResolution / 28),
            ClimateProvider = climateProvider,
            EnableFreezing = planet.EnableFrozenWater,
            LakeFreezeStartTemperature01 = water.LakeFreezeStartTemperature01,
            LakeFreezeCompleteTemperature01 = water.LakeFreezeCompleteTemperature01,
            OceanFreezeStartTemperature01 = water.OceanFreezeStartTemperature01,
            OceanFreezeCompleteTemperature01 = water.OceanFreezeCompleteTemperature01
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
            mat.SetFloat(_waveAmplitudeId, water.WaveAmplitude * waterScale);
            mat.SetFloat(_waveScaleId, water.WaveScale * waterScale);
            Shader.SetGlobalFloat(_waveSpeedId, water.WaveSpeed);
            mat.SetFloat(_waveNormalStrengthId, water.WaveNormalStrength);
            Shader.SetGlobalFloat(_swellAmplitudeId, water.SwellAmplitude);
            Shader.SetGlobalFloat(_swellWavelengthId, water.SwellWavelength);
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

    // --- Console (registry-target: PlanetWaterSurface is a plain class) --------------------------

    // Fields the mesh build consumes. Changing one only reaches the screen after a regenerate, so the
    // setter says so rather than half-applying: DeepDepth in particular is ALSO a live material float
    // and is the divisor that normalized the baked vertex depths, so a live-only change would rescale
    // every depth read against a mesh built for the old value.
    static readonly System.Collections.Generic.HashSet<string> _bakedFields = new(System.StringComparer.OrdinalIgnoreCase)
    {
        nameof(WaterDto.DeepDepth),
        nameof(WaterDto.ShoreRange),
        nameof(WaterDto.ReferenceRadius),
        nameof(WaterDto.LakeFreezeStartTemperature01),
        nameof(WaterDto.LakeFreezeCompleteTemperature01),
        nameof(WaterDto.OceanFreezeStartTemperature01),
        nameof(WaterDto.OceanFreezeCompleteTemperature01),
    };

    [ConsoleCommand("list", "List water settings with current values; [baked] needs planet.generate to take effect.", MonoTargetType.Registry)]
    string ListCmd(string filter = null)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return "water: no WaterDto registered";
        var dto = SettingsProvider.GetSettings<WaterDto>();
        var sb = new System.Text.StringBuilder();
        foreach (var p in typeof(WaterDto).GetConstructors()[0].GetParameters())
        {
            if (!string.IsNullOrEmpty(filter) && p.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            object v = typeof(WaterDto).GetProperty(p.Name).GetValue(dto);
            sb.AppendLine($"{p.Name} = {v}{(_bakedFields.Contains(p.Name) ? "   [baked]" : "")}");
        }
        return sb.Length == 0 ? "water: no fields matched" : sb.ToString().TrimEnd();
    }

    [ConsoleCommand("set", "water.set <field> <value> - update a float setting on the runtime DTO.", MonoTargetType.Registry)]
    string SetCmd(string field, float value)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return "water: no WaterDto registered";
        WaterDto next = WithFloat(SettingsProvider.GetSettings<WaterDto>(), field, value, out string canonical, out string error);
        if (next == null) return "water.set: " + error;

        SettingsProvider.Update(next);
        ReapplyMaterial();
        return _bakedFields.Contains(canonical)
            ? $"water.set {canonical} = {value}  [baked - run planet.generate to rebuild the mesh]"
            : $"water.set {canonical} = {value}";
    }

    [ConsoleCommand("setcolor", "water.setcolor <field> <r> <g> <b> [a] - update a Color setting (0-1 components).", MonoTargetType.Registry)]
    string SetColorCmd(string field, float r, float g, float b, float a = 1f)
    {
        if (!SettingsProvider.IsRegistered<WaterDto>()) return "water: no WaterDto registered";
        var value = new Color(r, g, b, a);
        WaterDto next = WithValue(SettingsProvider.GetSettings<WaterDto>(), field, value, typeof(Color), out string canonical, out string error);
        if (next == null) return "water.setcolor: " + error;

        SettingsProvider.Update(next);
        ReapplyMaterial();
        return $"water.setcolor {canonical} = {value}";
    }

    [ConsoleCommand("bodies", "List the water bodies found this generation (id, kind, size, level).", MonoTargetType.Registry)]
    string BodiesCmd()
    {
        WaterBodyCatalog catalog = WaterBodyMap.Current?.Bodies;
        if (catalog == null) return "water.bodies: no catalog (generate a planet first)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"catalog v{catalog.Version}: {catalog.CountOf(WaterBodyKind.Lake)} lake(s), " +
                      $"{catalog.CountOf(WaterBodyKind.Ocean)} ocean(s), seam asymmetry {WaterBodyMap.Current.SeamAsymmetryCount}");
        foreach (WaterBody b in catalog.Bodies)
            sb.AppendLine($"  #{b.Id} {b.Kind} cells={b.CellCount} minElev={b.MinElevation:F4} " +
                          $"level={b.SurfaceElevation:F4} dir=({b.CenterDirection.x:F2},{b.CenterDirection.y:F2},{b.CenterDirection.z:F2})");
        return sb.ToString().TrimEnd();
    }

    [ConsoleCommand("reset", "Restore the runtime DTO from the authored WaterSettings asset.", MonoTargetType.Registry)]
    string ResetCmd()
    {
        var source = Resources.FindObjectsOfTypeAll<WaterSettings>();
        if (source == null || source.Length == 0) return "water.reset: no WaterSettings asset loaded";
        WaterDto restored = WaterDto.From(source[0]);
        if (restored == null) return "water.reset: failed to read WaterSettings";
        SettingsProvider.Update(restored);
        ReapplyMaterial();
        return "water.reset: restored from " + source[0].name + " (baked fields need planet.generate)";
    }

    static WaterDto WithFloat(WaterDto src, string field, float value, out string canonical, out string error) =>
        WithValue(src, field, value, typeof(float), out canonical, out error);

    // Records are immutable and positional, so build the replacement through the primary constructor.
    // Doing it reflectively means a new WaterDto field needs no matching change here.
    static WaterDto WithValue(WaterDto src, string field, object value, System.Type expected, out string canonical, out string error)
    {
        canonical = null;
        var ctor = typeof(WaterDto).GetConstructors()[0];
        var parameters = ctor.GetParameters();
        var args = new object[parameters.Length];
        int target = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            args[i] = typeof(WaterDto).GetProperty(parameters[i].Name).GetValue(src);
            if (string.Equals(parameters[i].Name, field, System.StringComparison.OrdinalIgnoreCase))
                target = i;
        }

        if (target < 0) { error = $"unknown field '{field}' (try water.list)"; return null; }
        if (parameters[target].ParameterType != expected)
        {
            error = $"'{parameters[target].Name}' is {parameters[target].ParameterType.Name}, not a {expected.Name}";
            return null;
        }

        canonical = parameters[target].Name;
        args[target] = value;
        error = null;
        return (WaterDto)ctor.Invoke(args);
    }

    void ReapplyMaterial()
    {
        if (_waterMaterial != null)
            UpdateWaterMaterial(_waterMaterial);
    }

    public void Dispose()
    {
        ConsoleRegistry.UnregisterInstance(typeof(PlanetWaterSurface));
        if (_waterMaterial != null)
        {
            Object.Destroy(_waterMaterial);
            _waterMaterial = null;
        }
        // _waterObject is a child of the planet transform and is destroyed with the planet.
        _waterObject = null;
    }
}
