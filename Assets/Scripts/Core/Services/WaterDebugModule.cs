using System.Collections.Generic;
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
    public static readonly DebugCaptureSetId Wakes = new DebugCaptureSetId(Module, "wakes");
    public static readonly DebugCaptureSetId VolumeDeepDive = new DebugCaptureSetId(Module, "volume-deep-dive");

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

    static readonly int _oceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _oceanFocusModeId = Shader.PropertyToID("_OceanFocusMode");
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
    static readonly int _waterWakeCountId = Shader.PropertyToID("_WaterWakeCount");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamDepthId = Shader.PropertyToID("_ShoreFoamDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");

    struct WaterDebugStats
    {
        public bool Valid;
        public int Vertices;
        public int Triangles;
        public float DepthMin, DepthMax, DepthAvg;
        public float ShoreMin, ShoreMax, ShoreAvg;
        public float BodyMin, BodyMax, BodyAvg;
        public float MotionMaskAvg, MotionMaskMax, MotionMaskSample;
        public float NormalMaskAvg, NormalMaskMax, NormalMaskSample;
        public float SampleDepth, SampleShore, SampleBody;
        public float MotionEligiblePercent;
        public float NormalEligiblePercent;
    }

    struct MeshIntegrityStats
    {
        public int DegenerateTriangles;
        public int BoundaryEdges;
        public int NonManifoldEdges;
        public int OpenEdgeVertices;
        public float RadiusErrorAvgMeters;
        public float RadiusErrorMaxMeters;
    }

    public void Register(DebugRegistry registry)
    {
        RegisterModes(registry);
        RegisterCaptureSets(registry);
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        Shader.SetGlobalInt(_oceanDebugModeId, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        Shader.SetGlobalInt(_oceanDebugModeId, 0);
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

    static void RegisterModes(DebugRegistry registry)
    {
        RegisterMode(registry, 0, "Off", "Water");
        RegisterMode(registry, 1, "Depth", "Water Surface");
        RegisterMode(registry, 2, "Shore", "Water Surface");
        RegisterMode(registry, 3, "Body", "Water Surface");
        RegisterMode(registry, 4, "Lighting", "Water Surface");
        RegisterMode(registry, 5, "Glint", "Water Surface");
        RegisterMode(registry, 6, "Normals", "Water Surface");
        RegisterMode(registry, 7, "Foam", "Water Surface");
        RegisterMode(registry, 8, "MotionMask", "Water Surface");
        RegisterMode(registry, 9, "WaveHeight", "Water Surface");
        RegisterMode(registry, 10, "WaveSlope", "Water Surface");
        RegisterMode(registry, 11, "WaterData", "Water Surface");
        RegisterMode(registry, 12, "Absorption", "Water Surface");
        RegisterMode(registry, 13, "VolumeData", "Water Volume");
        RegisterMode(registry, 14, "VolumeMask", "Water Volume");
        RegisterMode(registry, 15, "VolumePath", "Water Volume");
        RegisterMode(registry, 16, "VolumeLight", "Water Volume");
        RegisterMode(registry, 17, "VolumeRefraction", "Water Volume");
        RegisterMode(registry, 18, "FoamParts", "Water Surface");
        RegisterMode(registry, 19, "SurfaceAlpha", "Water Surface");
        RegisterMode(registry, 20, "VolumeBoundary", "Water Volume");
        RegisterMode(registry, 21, "VolumeOptical", "Water Volume");
        RegisterMode(registry, 22, "SurfaceContact", "Water Surface");
        RegisterMode(registry, 23, "SurfaceBlend", "Water Surface");
        RegisterMode(registry, 24, "VolumeOnly", "Water Split");
        RegisterMode(registry, 25, "SurfaceOnly", "Water Split");
        RegisterMode(registry, 26, "WaterOff", "Water Split");
        RegisterMode(registry, 27, "VolumeContact", "Water Volume");
        RegisterMode(registry, 28, "VolumeDilation", "Water Volume");
        RegisterMode(registry, 29, "VolumeNoRefraction", "Water Volume");
        RegisterMode(registry, 30, "VolumeOcclusion", "Water Volume");
        RegisterMode(registry, 31, "TerrainSourcePink", "Water Source");
        RegisterMode(registry, 32, "FoamPink", "Water Source");
        RegisterMode(registry, 33, "VolumeSphere", "Water Volume");
        RegisterMode(registry, 34, "TerrainFaceId", "Terrain");
        RegisterMode(registry, 35, "SeaRay", "Water Volume");
        RegisterMode(registry, 36, "SeaVsMesh", "Water Volume");
        RegisterMode(registry, 37, "SeaPath", "Water Volume");
        RegisterMode(registry, 38, "SeaMatte", "Water Volume");
        RegisterMode(registry, 39, "SeaSourceMatte", "Water Volume");
        RegisterMode(registry, 40, "AtmosphereBypass", "Atmosphere");
        RegisterMode(registry, 41, "VolumeAfterAtmosphere", "Water Atmosphere");
        RegisterMode(registry, 42, "AtmosphereWaterCut", "Atmosphere");
        RegisterMode(registry, 43, "VolumeContribution", "Water Volume");
        RegisterMode(registry, 44, "AtmosphereContribution", "Atmosphere");
        RegisterMode(registry, 45, "PrecipitationContribution", "Precipitation");
        RegisterMode(registry, 46, "VolumeLipPink", "Water Volume");
        RegisterMode(registry, 47, "VolumeLipRawPink", "Water Volume");
        RegisterMode(registry, 48, "VolumeLipDepthGate", "Water Volume");
        RegisterMode(registry, 49, "SurfaceBackfacePink", "Water Surface");
        RegisterMode(registry, 50, "VolumeLipScenePink", "Water Volume");
        RegisterMode(registry, 51, "WakeMask", "Water Surface");
        RegisterMode(registry, 52, "SurfacePolish", "Water Surface");
        RegisterMode(registry, 53, "SurfaceRawOpaque", "Water Surface Isolation");
        RegisterMode(registry, 54, "SurfaceFxContrib", "Water Surface Isolation");
        RegisterMode(registry, 55, "SurfaceAlphaParts", "Water Surface Isolation");
        RegisterMode(registry, 56, "WaterNoPost", "Water Split");
    }

    static void RegisterCaptureSets(DebugRegistry registry)
    {
        registry.RegisterCaptureSet(WaterDebugIds.Artifact, "Water Artifact",
            Modes(0, 24, 25, 26, 30, 46, 47, 48, 49, 50, 31, 32, 40, 41, 42, 43, 44, 45));
        registry.RegisterCaptureSet(WaterDebugIds.Atmosphere, "Water/Atmosphere",
            Modes(0, 24, 26, 40, 41, 42, 44));
        registry.RegisterCaptureSet(WaterDebugIds.Interface, "Water Interface",
            Modes(0, 11, 14, 15, 20, 27, 28, 33, 34, 35, 36, 37, 46, 47, 48, 49, 50));
        registry.RegisterCaptureSet(WaterDebugIds.Precipitation, "Water Precipitation",
            Modes(0, 40, 42, 44, 45));
        registry.RegisterDefaultCaptureSet(WaterDebugIds.SurfaceFinish, "Water Surface Finish",
            Modes(0, 40, 56, 25, 1, 4, 5, 6, 7, 9, 10, 18, 19, 22, 23, 51, 52, 53, 54, 55));
        registry.RegisterCaptureSet(WaterDebugIds.SurfaceIsolation, "Water Surface Isolation",
            Modes(0, 40, 56, 24, 25, 53, 54, 55, 19, 23, 52));
        registry.RegisterCaptureSet(WaterDebugIds.Wakes, "Water Wakes",
            Modes(0, 51, 7, 18, 6, 9, 10, 52));
        registry.RegisterCaptureSet(WaterDebugIds.VolumeDeepDive, "Water Volume Deep Dive",
            Modes(0, 2, 7, 11, 12, 14, 15, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 46, 47, 48, 49, 50, 31, 32, 33, 34, 35, 36, 37, 38, 39));
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(WaterDebugIds.Mode(localId), name, category);
    }

    static DebugModeId[] Modes(params int[] localIds)
    {
        DebugModeId[] modes = new DebugModeId[localIds.Length];
        for (int i = 0; i < localIds.Length; i++)
            modes[i] = WaterDebugIds.Mode(localIds[i]);
        return modes;
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
            && TryAnalyzeMeshIntegrity(waterFilter.sharedMesh, waterFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats waterIntegrity))
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
                && TryAnalyzeMeshIntegrity(volumeLipMesh, volumeLipFilter.transform, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out MeshIntegrityStats lipIntegrity))
            {
                sb.AppendLine($"VolumeLipIntegrity: degTris={lipIntegrity.DegenerateTriangles}, boundaryEdges={lipIntegrity.BoundaryEdges}, nonManifoldEdges={lipIntegrity.NonManifoldEdges}, openEdgeVerts={lipIntegrity.OpenEdgeVertices}, seaRadErrAvgM={lipIntegrity.RadiusErrorAvgMeters:F3}, seaRadErrMaxM={lipIntegrity.RadiusErrorMaxMeters:F3}");
            }
        }
        else
        {
            sb.AppendLine("VolumeLipMesh: missing");
        }

        sb.AppendLine($"DataRanges: depth={s.DepthMin:F3}-{s.DepthMax:F3} avg={s.DepthAvg:F3}, shore={s.ShoreMin:F3}-{s.ShoreMax:F3} avg={s.ShoreAvg:F3}, body={s.BodyMin:F3}-{s.BodyMax:F3} avg={s.BodyAvg:F3}");
        sb.AppendLine($"CameraSample: depth={s.SampleDepth:F3}, shore={s.SampleShore:F3}, body={s.SampleBody:F3}, motionMask={s.MotionMaskSample:F3}, normalMask={s.NormalMaskSample:F3}");
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
        float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        sb.AppendLine($"Weather: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
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
        float wind01 = Mathf.Clamp01(weatherProvider.WindSpeed / 5f);
        float waveState = Mathf.Clamp01(0.18f + wind01 * 0.82f);
        float foamState = Mathf.Clamp01(0.12f + wind01 * 0.58f + weather.StormIntensity * 0.72f);
        GUILayout.Label($"Weather/waves: wind={weatherProvider.WindSpeed:F2}, wave={waveState:F2}, foam={foamState:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
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

    static bool TryAnalyzeMeshIntegrity(Mesh mesh, Transform meshTransform, Vector3 planetCenter, float expectedRadius, out MeshIntegrityStats stats)
    {
        stats = default;
        if (mesh == null)
            return false;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
            return false;

        Dictionary<ulong, int> edgeUse = new Dictionary<ulong, int>(triangles.Length);
        HashSet<int> openVertices = new HashSet<int>();

        int validTriangles = 0;
        float radiusErrorSum = 0f;
        int radiusSamples = 0;

        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            validTriangles++;

            Vector3 a = vertices[i0];
            Vector3 b = vertices[i1];
            Vector3 c = vertices[i2];
            float areaSq4 = Vector3.Cross(b - a, c - a).sqrMagnitude;
            if (areaSq4 <= 0.0000000001f)
                stats.DegenerateTriangles++;

            IncrementEdgeCount(edgeUse, i0, i1);
            IncrementEdgeCount(edgeUse, i1, i2);
            IncrementEdgeCount(edgeUse, i2, i0);
        }

        foreach (KeyValuePair<ulong, int> kv in edgeUse)
        {
            if (kv.Value == 1)
            {
                stats.BoundaryEdges++;
                DecodeEdge(kv.Key, out int ea, out int eb);
                openVertices.Add(ea);
                openVertices.Add(eb);
            }
            else if (kv.Value > 2)
            {
                stats.NonManifoldEdges++;
            }
        }

        stats.OpenEdgeVertices = openVertices.Count;

        if (expectedRadius > 0f && meshTransform != null)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                float radius = Vector3.Distance(world, planetCenter);
                float error = Mathf.Abs(radius - expectedRadius);
                radiusErrorSum += error;
                stats.RadiusErrorMaxMeters = Mathf.Max(stats.RadiusErrorMaxMeters, error);
                radiusSamples++;
            }
        }

        if (radiusSamples > 0)
            stats.RadiusErrorAvgMeters = radiusErrorSum / radiusSamples;

        return validTriangles > 0;
    }

    static void IncrementEdgeCount(Dictionary<ulong, int> edgeUse, int a, int b)
    {
        ulong key = EncodeEdge(a, b);
        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    static ulong EncodeEdge(int a, int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    static void DecodeEdge(ulong key, out int a, out int b)
    {
        a = (int)(key >> 32);
        b = (int)(key & 0xFFFFFFFFu);
    }

    void RefreshWaterDebugStats(Renderer waterRenderer, ICameraRigContext cameraContext)
    {
        _waterDebugStats = default;

        if (waterRenderer == null || !waterRenderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
            return;

        Mesh mesh = filter.sharedMesh;
        Color[] colors = mesh.colors;
        if (colors == null || colors.Length == 0)
            return;

        Vector3[] vertices = mesh.vertices;
        int count = Mathf.Min(colors.Length, vertices != null ? vertices.Length : colors.Length);
        if (count <= 0)
            return;

        WaterDebugStats stats = new WaterDebugStats
        {
            Valid = true,
            Vertices = mesh.vertexCount,
            Triangles = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0,
            DepthMin = 1f,
            ShoreMin = 1f,
            BodyMin = 1f
        };

        Vector3 localCamera = cameraContext != null ? waterRenderer.transform.InverseTransformPoint(cameraContext.CameraTransform.position) : Vector3.zero;
        Vector3 localCameraDir = localCamera.sqrMagnitude > 0.0001f ? localCamera.normalized : Vector3.up;
        int sampleIndex = 0;
        float bestAlignment = -2f;
        int motionEligible = 0;
        int normalEligible = 0;

        for (int i = 0; i < count; i++)
        {
            Color c = colors[i];
            float depth = Mathf.Clamp01(c.r);
            float shore = Mathf.Clamp01(c.g);
            float body = Mathf.Clamp01(c.b);
            float motionMask = FocusMotionMask(depth, shore, body);
            float normalMask = FocusNormalMask(depth, shore, body);

            stats.DepthMin = Mathf.Min(stats.DepthMin, depth);
            stats.DepthMax = Mathf.Max(stats.DepthMax, depth);
            stats.DepthAvg += depth;
            stats.ShoreMin = Mathf.Min(stats.ShoreMin, shore);
            stats.ShoreMax = Mathf.Max(stats.ShoreMax, shore);
            stats.ShoreAvg += shore;
            stats.BodyMin = Mathf.Min(stats.BodyMin, body);
            stats.BodyMax = Mathf.Max(stats.BodyMax, body);
            stats.BodyAvg += body;
            stats.MotionMaskAvg += motionMask;
            stats.MotionMaskMax = Mathf.Max(stats.MotionMaskMax, motionMask);
            stats.NormalMaskAvg += normalMask;
            stats.NormalMaskMax = Mathf.Max(stats.NormalMaskMax, normalMask);

            if (motionMask > 0.05f) motionEligible++;
            if (normalMask > 0.05f) normalEligible++;

            if (vertices != null && i < vertices.Length && vertices[i].sqrMagnitude > 0.0001f)
            {
                float alignment = Vector3.Dot(vertices[i].normalized, localCameraDir);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    sampleIndex = i;
                }
            }
        }

        float invCount = 1f / count;
        stats.DepthAvg *= invCount;
        stats.ShoreAvg *= invCount;
        stats.BodyAvg *= invCount;
        stats.MotionMaskAvg *= invCount;
        stats.NormalMaskAvg *= invCount;
        stats.MotionEligiblePercent = motionEligible * 100f * invCount;
        stats.NormalEligiblePercent = normalEligible * 100f * invCount;

        Color sample = colors[Mathf.Clamp(sampleIndex, 0, colors.Length - 1)];
        stats.SampleDepth = Mathf.Clamp01(sample.r);
        stats.SampleShore = Mathf.Clamp01(sample.g);
        stats.SampleBody = Mathf.Clamp01(sample.b);
        stats.MotionMaskSample = FocusMotionMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);
        stats.NormalMaskSample = FocusNormalMask(stats.SampleDepth, stats.SampleShore, stats.SampleBody);

        _cachedWaterMesh = mesh;
        _waterDebugStats = stats;
    }

    static float FocusMotionMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.02f, 0.18f, depth);
        float shoreRelease = SmoothStep(0.02f, 0.18f, shore) * 0.58f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float FocusNormalMask(float depth, float shore, float body)
    {
        float depthRelease = SmoothStep(0.012f, 0.10f, depth);
        float shoreRelease = SmoothStep(0.012f, 0.10f, shore) * 0.72f;
        return body * Mathf.Clamp01(Mathf.Max(depthRelease, shoreRelease));
    }

    static float SmoothStep(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    static float GetMaterialFloat(Material mat, int id)
    {
        return mat != null && mat.HasProperty(id) ? mat.GetFloat(id) : float.NaN;
    }
}
