using System.Text;
using UnityEngine;

public static class WaterDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("water");
    public static readonly DebugCaptureSetId Artifact = new DebugCaptureSetId(Module, "artifact");
    public static readonly DebugCaptureSetId Atmosphere = new DebugCaptureSetId(Module, "atmosphere");
    public static readonly DebugCaptureSetId Interface = new DebugCaptureSetId(Module, "interface");
    public static readonly DebugCaptureSetId Precipitation = new DebugCaptureSetId(Module, "precipitation");
    public static readonly DebugCaptureSetId SurfaceFinish = new DebugCaptureSetId(Module, "surface-finish");
    public static readonly DebugCaptureSetId SurfaceIsolation = new DebugCaptureSetId(Module, "surface-isolation");
    public static readonly DebugCaptureSetId Caustics = new DebugCaptureSetId(Module, "caustics");
    public static readonly DebugCaptureSetId Wakes = new DebugCaptureSetId(Module, "wakes");
    public static readonly DebugCaptureSetId VolumeDeepDive = new DebugCaptureSetId(Module, "volume-deep-dive");
    public static readonly DebugCaptureSetId Night = new DebugCaptureSetId(Module, "night");
    public static readonly DebugCaptureSetId Waves = new DebugCaptureSetId(Module, "waves");
    public static readonly DebugCaptureSetId Foam = new DebugCaptureSetId(Module, "foam");
    public static readonly DebugCaptureSetId Glint = new DebugCaptureSetId(Module, "glint");
    public static readonly DebugCaptureSetId Frozen = new DebugCaptureSetId(Module, "frozen");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class WaterDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    public DebugModuleId Id => WaterDebugIds.Module;
    public DebugModuleId ModuleId => WaterDebugIds.Module;

    Renderer _cachedWaterRenderer;
    Mesh _cachedWaterMesh;
    WaterDebugStats _waterDebugStats;
    float _nextWaterDebugRefreshTime;
    readonly WaterMeshAnalysis _analysis = new();

    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _oceanFocusModeId = Shader.PropertyToID(ShaderGlobalIds.OceanFocusMode);
    static readonly int _waveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
    static readonly int _waveScaleId = Shader.PropertyToID("_WaveScale");
    static readonly int _waveSpeedId = Shader.PropertyToID("_WaveSpeed");
    static readonly int _waveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
    static readonly int _waterMotionStrengthId = Shader.PropertyToID("_WaterMotionStrength");
    static readonly int _sunGlitterIntensityId = Shader.PropertyToID("_SunGlitterIntensity");
    static readonly int _shoreFoamIntensityId = Shader.PropertyToID("_ShoreFoamIntensity");
    static readonly int _whitecapIntensityId = Shader.PropertyToID("_WhitecapIntensity");
    static readonly int _wakeFoamIntensityId = Shader.PropertyToID("_WakeFoamIntensity");
    static readonly int _wakeNormalStrengthId = Shader.PropertyToID("_WakeNormalStrength");
    static readonly int _waterWakeCountId = Shader.PropertyToID(ShaderGlobalIds.WaterWakeCount);
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _freezingEnabledId = Shader.PropertyToID("_FreezingEnabled");
    static readonly int _lakeFreezeStartId = Shader.PropertyToID("_LakeFreezeStart");
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID("_LakeFreezeComplete");
    static readonly int _oceanFreezeStartId = Shader.PropertyToID("_OceanFreezeStart");
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID("_OceanFreezeComplete");
    static readonly int _frozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.FrozenWaterBodies);
    static readonly int _partiallyFrozenWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.PartiallyFrozenWaterBodies);
    static readonly int _liquidWaterBodiesId = Shader.PropertyToID(ShaderGlobalIds.LiquidWaterBodies);

    public void Register(DebugRegistry registry)
    {
        WaterDebugRegistration.Register(registry);
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        OceanDebugModeWriter.Set(WaterDebugIds.Module, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        OceanDebugModeWriter.Clear(WaterDebugIds.Module);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        AppendWaterDebugMetadata(sb, context.Runtime);
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        DrawWaterDebugOverlay(state);
    }

    void AppendWaterDebugMetadata(StringBuilder sb, DebugRuntimeState state)
    {
        ICameraRigContext cameraContext = state.CameraContext;
        Renderer waterRenderer = GetWaterRenderer();
        sb.AppendLine("--- Water ---");

        if (waterRenderer == null)
        {
            sb.AppendLine("Renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        sb.AppendLine($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        sb.AppendLine($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F2}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F2}, debug={state.CurrentModeId}:{state.CurrentModeName}");
        sb.AppendLine($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F2}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        sb.AppendLine($"SurfaceFx: shoreFoam={GetMaterialFloat(mat, _shoreFoamIntensityId):F2}, whitecaps={GetMaterialFloat(mat, _whitecapIntensityId):F2}, wakeFoam={GetMaterialFloat(mat, _wakeFoamIntensityId):F2}, wakeNormal={GetMaterialFloat(mat, _wakeNormalStrengthId):F2}, wakeSources={Shader.GetGlobalInt(_waterWakeCountId)}");
        sb.AppendLine($"DepthFoam: shallow={GetMaterialFloat(mat, _shallowDepthId):F2}, deep={GetMaterialFloat(mat, _deepDepthId):F2}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F2}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F2}");
        sb.AppendLine(
            $"FrozenWater: enabled={GetMaterialFloat(mat, _freezingEnabledId) > 0.5f}, " +
            $"lake={GetMaterialFloat(mat, _lakeFreezeCompleteId):F3}-{GetMaterialFloat(mat, _lakeFreezeStartId):F3}, " +
            $"ocean={GetMaterialFloat(mat, _oceanFreezeCompleteId):F3}-{GetMaterialFloat(mat, _oceanFreezeStartId):F3}, " +
            $"bodies frozen/partial/liquid={Shader.GetGlobalInt(_frozenWaterBodiesId)}/" +
            $"{Shader.GetGlobalInt(_partiallyFrozenWaterBodiesId)}/{Shader.GetGlobalInt(_liquidWaterBodiesId)}");

        if (state.WeatherProvider != null && cameraContext != null)
            AppendWeatherMetadata(sb, state.WeatherProvider, cameraContext);

        RefreshWaterDebugStats(waterRenderer, cameraContext);
        if (!_waterDebugStats.Valid)
        {
            sb.AppendLine("MeshData: missing vertex colors");
            return;
        }

        WaterDebugStats s = _waterDebugStats;
        sb.AppendLine($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        if (state.IncludeHeavyDiagnostics && cameraContext != null
            && waterRenderer.TryGetComponent(out MeshFilter waterFilter) && waterFilter.sharedMesh != null
            && WaterMeshAnalysis.TryAnalyzeMeshIntegrity(waterFilter.sharedMesh, waterFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats waterIntegrity))
        {
            sb.AppendLine($"MeshIntegrity: degTris={waterIntegrity.DegenerateTriangles}, boundaryEdges={waterIntegrity.BoundaryEdges}, nonManifoldEdges={waterIntegrity.NonManifoldEdges}, openEdgeVerts={waterIntegrity.OpenEdgeVertices}, seaRadErrAvgM={waterIntegrity.RadiusErrorAvgMeters:F3}, seaRadErrMaxM={waterIntegrity.RadiusErrorMaxMeters:F3}");
        }

        MeshFilter volumeLipFilter = GetWaterVolumeLipFilter(waterRenderer);
        Mesh volumeLipMesh = volumeLipFilter != null ? volumeLipFilter.sharedMesh : null;
        if (volumeLipMesh != null)
        {
            int volumeLipTriangles = volumeLipMesh.subMeshCount > 0 ? (int)(volumeLipMesh.GetIndexCount(0) / 3) : 0;
            sb.AppendLine($"VolumeLipMesh: active={volumeLipFilter.gameObject.activeInHierarchy}, verts={volumeLipMesh.vertexCount}, tris={volumeLipTriangles}");
            if (state.IncludeHeavyDiagnostics && cameraContext != null
                && WaterMeshAnalysis.TryAnalyzeMeshIntegrity(volumeLipMesh, volumeLipFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats lipIntegrity))
            {
                sb.AppendLine($"VolumeLipIntegrity: degTris={lipIntegrity.DegenerateTriangles}, boundaryEdges={lipIntegrity.BoundaryEdges}, nonManifoldEdges={lipIntegrity.NonManifoldEdges}, openEdgeVerts={lipIntegrity.OpenEdgeVertices}, seaRadErrAvgM={lipIntegrity.RadiusErrorAvgMeters:F3}, seaRadErrMaxM={lipIntegrity.RadiusErrorMaxMeters:F3}");
            }
        }
        else
        {
            sb.AppendLine("VolumeLipMesh: missing");
        }

        sb.AppendLine($"DataRanges: depth={s.DepthMin:F3}-{s.DepthMax:F3} avg={s.DepthAvg:F3}, shore={s.ShoreMin:F3}-{s.ShoreMax:F3} avg={s.ShoreAvg:F3}, body={s.BodyMin:F3}-{s.BodyMax:F3} avg={s.BodyAvg:F3}, temp={s.TemperatureMin:F3}-{s.TemperatureMax:F3} avg={s.TemperatureAvg:F3}");
        sb.AppendLine($"CameraSample: depth={s.SampleDepth:F3}, shore={s.SampleShore:F3}, body={s.SampleBody:F3}, temp={s.SampleTemperature:F3}, motionMask={s.MotionMaskSample:F3}, normalMask={s.NormalMaskSample:F3}");
        sb.AppendLine($"MotionMask: avg={s.MotionMaskAvg:F3}, max={s.MotionMaskMax:F3}, eligible={s.MotionEligiblePercent:F1}%");
        sb.AppendLine($"NormalMask: avg={s.NormalMaskAvg:F3}, max={s.NormalMaskMax:F3}, eligible={s.NormalEligiblePercent:F1}%");
    }

    static void AppendWeatherMetadata(StringBuilder sb, IWeatherProvider weatherProvider, ICameraRigContext cameraContext)
    {
        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        float wind01 = weatherProvider.WindStrength01;
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        sb.AppendLine($"Weather: wind={weatherProvider.WindSpeedMetersPerSecond:F2} m/s, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    void DrawWaterDebugOverlay(DebugRuntimeState state)
    {
        ICameraRigContext cameraContext = state.CameraContext;
        Renderer waterRenderer = GetWaterRenderer();
        GUILayout.Space(6);
        GUILayout.Label("Water Debug");

        if (waterRenderer == null)
        {
            GUILayout.Label("Water renderer: missing");
            return;
        }

        Material mat = waterRenderer.sharedMaterial;
        GUILayout.Label($"Shader: {(mat != null && mat.shader != null ? mat.shader.name : "missing")}");
        GUILayout.Label($"Focus: ocean={GetMaterialFloat(mat, _oceanFocusModeId):F1}, waterGlobal={Shader.GetGlobalFloat(_waterFocusModeId):F1}, debug={state.CurrentModeId}:{state.CurrentModeName}");
        GUILayout.Label($"Wave: amp={GetMaterialFloat(mat, _waveAmplitudeId):F2}, scale={GetMaterialFloat(mat, _waveScaleId):F1}, speed={GetMaterialFloat(mat, _waveSpeedId):F2}, normal={GetMaterialFloat(mat, _waveNormalStrengthId):F2}, motion={GetMaterialFloat(mat, _waterMotionStrengthId):F2}, shimmer={GetMaterialFloat(mat, _sunGlitterIntensityId):F2}");
        GUILayout.Label($"SurfaceFx: shoreFoam={GetMaterialFloat(mat, _shoreFoamIntensityId):F2}, whitecaps={GetMaterialFloat(mat, _whitecapIntensityId):F2}, wakeFoam={GetMaterialFloat(mat, _wakeFoamIntensityId):F2}, wakeSources={Shader.GetGlobalInt(_waterWakeCountId)}");
        GUILayout.Label($"Depth/Foam: shallow={GetMaterialFloat(mat, _shallowDepthId):F1}, deep={GetMaterialFloat(mat, _deepDepthId):F1}, foamWidth={GetMaterialFloat(mat, _shoreFoamDepthId):F1}, shoreRange={GetMaterialFloat(mat, _shoreFoamSoftnessId):F1}");

        if (state.WeatherProvider != null && cameraContext != null)
            DrawWeatherOverlay(state.WeatherProvider, cameraContext);

        if (Time.unscaledTime >= _nextWaterDebugRefreshTime || waterRenderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != _cachedWaterMesh)
        {
            RefreshWaterDebugStats(waterRenderer, cameraContext);
            _nextWaterDebugRefreshTime = Time.unscaledTime + 0.75f;
        }

        if (!_waterDebugStats.Valid)
        {
            GUILayout.Label("Mesh water data: missing vertex colors");
            return;
        }

        WaterDebugStats s = _waterDebugStats;
        GUILayout.Label($"Mesh: verts={s.Vertices}, tris={s.Triangles}");
        GUILayout.Label($"Data ranges: depth {s.DepthMin:F2}-{s.DepthMax:F2} avg {s.DepthAvg:F2}, shore {s.ShoreMin:F2}-{s.ShoreMax:F2} avg {s.ShoreAvg:F2}, body {s.BodyMin:F2}-{s.BodyMax:F2} avg {s.BodyAvg:F2}");
        GUILayout.Label($"Camera sample: depth={s.SampleDepth:F2}, shore={s.SampleShore:F2}, body={s.SampleBody:F2}, motionMask={s.MotionMaskSample:F2}, normalMask={s.NormalMaskSample:F2}");
        GUILayout.Label($"Motion mask: avg={s.MotionMaskAvg:F2}, max={s.MotionMaskMax:F2}, eligible>{0.05f:F2}={s.MotionEligiblePercent:F1}%");
        GUILayout.Label($"Normal mask: avg={s.NormalMaskAvg:F2}, max={s.NormalMaskMax:F2}, eligible>{0.05f:F2}={s.NormalEligiblePercent:F1}%");
        GUILayout.Label("F10 sets: Water Artifact is concise; use F7 to cycle focused, current-mode, or full-loop captures.");
    }

    static void DrawWeatherOverlay(IWeatherProvider weatherProvider, ICameraRigContext cameraContext)
    {
        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        float wind01 = weatherProvider.WindStrength01;
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        GUILayout.Label($"Weather/waves: wind={weatherProvider.WindSpeedMetersPerSecond:F2} m/s, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    Renderer GetWaterRenderer()
    {
        if (_cachedWaterRenderer != null && _cachedWaterRenderer.enabled && _cachedWaterRenderer.gameObject.activeInHierarchy)
            return _cachedWaterRenderer;

        GameObject waterObject = GameObject.Find("Water");
        if (waterObject != null && waterObject.TryGetComponent(out Renderer waterRenderer))
        {
            _cachedWaterRenderer = waterRenderer;
            return _cachedWaterRenderer;
        }

        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name == "Planet/Ocean")
            {
                _cachedWaterRenderer = renderers[i];
                return _cachedWaterRenderer;
            }
        }

        return null;
    }

    static MeshFilter GetWaterVolumeLipFilter(Renderer waterRenderer)
    {
        if (waterRenderer == null)
            return null;

        Transform lip = waterRenderer.transform.Find("WaterVolumeLip");
        return lip != null ? lip.GetComponent<MeshFilter>() : null;
    }

    void RefreshWaterDebugStats(Renderer waterRenderer, ICameraRigContext cameraContext)
    {
        _waterDebugStats = _analysis.Compute(waterRenderer, cameraContext, out Mesh analyzedMesh);
        if (_waterDebugStats.Valid)
            _cachedWaterMesh = analyzedMesh;
    }

    static float GetMaterialFloat(Material mat, int id)
    {
        return mat != null && mat.HasProperty(id) ? mat.GetFloat(id) : float.NaN;
    }
}
