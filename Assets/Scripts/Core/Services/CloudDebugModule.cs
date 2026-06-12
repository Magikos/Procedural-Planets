using UnityEngine;

public static class CloudDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("cloud");
    public static readonly DebugCaptureSetId Diagnostics = new DebugCaptureSetId(Module, "diagnostics");

    public static DebugModeId Mode(int localId)
    {
        return new DebugModeId(Module, localId);
    }
}

public sealed class CloudDebugModule : IDebugModule, IDebugModeApplier, IDebugCaptureMetadataProvider, IDebugOverlayContributor
{
    const int Off = 0;
    const int Weather = 1;
    const int Storm = 2;
    const int Density = 3;
    const int OpticalDepth = 4;
    const int SilverLining = 5;
    const int MoistureSource = 6;
    const int CondensationChange = 7;

    static readonly int _cloudDebugModeId = Shader.PropertyToID("_CloudDebugMode");
    static readonly int _cloudWeatherResolutionId = Shader.PropertyToID("_CloudWeatherResolution");
    static readonly int _cloudInnerRadiusId = Shader.PropertyToID("_CloudInnerRadius");
    static readonly int _cloudOuterRadiusId = Shader.PropertyToID("_CloudOuterRadius");
    static readonly int _cloudViewStepsId = Shader.PropertyToID("_CloudViewSteps");
    static readonly int _cloudRayOffsetStrengthId = Shader.PropertyToID("_CloudRayOffsetStrength");
    static readonly int _cloudDensityThresholdId = Shader.PropertyToID("_CloudDensityThreshold");
    static readonly int _cloudDensityMultiplierId = Shader.PropertyToID("_CloudDensityMultiplier");

    public DebugModuleId Id => CloudDebugIds.Module;
    public DebugModuleId ModuleId => CloudDebugIds.Module;

    public void Register(DebugRegistry registry)
    {
        RegisterMode(registry, Off, "Off", "Clouds");
        RegisterMode(registry, Weather, "CloudWeather", "Clouds");
        RegisterMode(registry, Storm, "CloudStorm", "Clouds");
        RegisterMode(registry, Density, "CloudDensity", "Clouds");
        RegisterMode(registry, OpticalDepth, "CloudOpticalDepth", "Clouds");
        RegisterMode(registry, SilverLining, "CloudSilverLining", "Clouds");
        RegisterMode(registry, MoistureSource, "CloudMoistureSource", "Clouds");
        RegisterMode(registry, CondensationChange, "CloudCondensationChange", "Clouds");

        registry.RegisterCaptureSet(CloudDebugIds.Diagnostics, "Cloud Diagnostics",
            Modes(Off, Weather, Density, OpticalDepth, Storm, MoistureSource, CondensationChange));
        registry.RegisterModeApplier(this);
        registry.RegisterMetadataProvider(this);
        registry.RegisterOverlayContributor(this);
    }

    public void ApplyDebugMode(DebugModeDefinition mode)
    {
        Shader.SetGlobalInt(_cloudDebugModeId, mode.Id.LocalId);
    }

    public void ClearDebugMode()
    {
        Shader.SetGlobalInt(_cloudDebugModeId, Off);
    }

    public void AppendMetadata(DebugCaptureContext context, System.Text.StringBuilder sb)
    {
        sb.AppendLine("--- Clouds ---");
        sb.AppendLine($"DebugMode: {Shader.GetGlobalInt(_cloudDebugModeId)} ({context.ModeId}:{context.ModeName})");
        sb.AppendLine($"WeatherResolution: {Shader.GetGlobalInt(_cloudWeatherResolutionId)}");
        sb.AppendLine($"LayerRadii: inner={Shader.GetGlobalFloat(_cloudInnerRadiusId):F2}, outer={Shader.GetGlobalFloat(_cloudOuterRadiusId):F2}");
        sb.AppendLine($"Raymarch: viewSteps={Shader.GetGlobalInt(_cloudViewStepsId)}, jitter={Shader.GetGlobalFloat(_cloudRayOffsetStrengthId):F2}");
        sb.AppendLine($"Density: threshold={Shader.GetGlobalFloat(_cloudDensityThresholdId):F3}, multiplier={Shader.GetGlobalFloat(_cloudDensityMultiplierId):F4}");

        IWeatherProvider weatherProvider = context.Runtime.WeatherProvider;
        ICameraRigContext cameraContext = context.Runtime.CameraContext;
        if (weatherProvider == null || cameraContext == null)
            return;

        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        sb.AppendLine($"CameraWeather: coverage={weather.CloudCoverage:F3}, storm={weather.StormIntensity:F3}, moisture={weather.MoistureSource:F3}, rain={weather.Precipitation:F3}, state={weather.State}");
    }

    public void DrawOverlay(DebugRuntimeState state)
    {
        if (!state.ShowDetailedDebug)
            return;

        GUILayout.Space(6);
        GUILayout.Label("Cloud Debug");
        GUILayout.Label($"Mode: {Shader.GetGlobalInt(_cloudDebugModeId)} ({state.CurrentModeId}:{state.CurrentModeName})");
        GUILayout.Label($"Weather: {Shader.GetGlobalInt(_cloudWeatherResolutionId)} px, view steps {Shader.GetGlobalInt(_cloudViewStepsId)}");
        GUILayout.Label($"Layer: {Shader.GetGlobalFloat(_cloudInnerRadiusId):F0}-{Shader.GetGlobalFloat(_cloudOuterRadiusId):F0}");

        IWeatherProvider weatherProvider = state.WeatherProvider;
        ICameraRigContext cameraContext = state.CameraContext;
        if (weatherProvider == null || cameraContext == null)
            return;

        Vector3 samplePosition = cameraContext.CameraTransform.position;
        Vector3 fromCenter = cameraContext.CameraTransform.position - cameraContext.PlanetCenter;
        if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            samplePosition = cameraContext.PlanetCenter + fromCenter.normalized * cameraContext.SeaLevelRadius;

        WeatherSample weather = weatherProvider.SampleWeather(samplePosition);
        GUILayout.Label($"Sample: cloud={weather.CloudCoverage:F2}, storm={weather.StormIntensity:F2}, rain={weather.Precipitation:F2}, state={weather.State}");
    }

    static void RegisterMode(DebugRegistry registry, int localId, string name, string category)
    {
        registry.RegisterMode(CloudDebugIds.Mode(localId), name, category);
    }

    static DebugModeId[] Modes(params int[] localIds)
    {
        DebugModeId[] modes = new DebugModeId[localIds.Length];
        for (int i = 0; i < localIds.Length; i++)
            modes[i] = CloudDebugIds.Mode(localIds[i]);
        return modes;
    }
}
