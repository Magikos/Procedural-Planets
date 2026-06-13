using System.Text;
using UnityEngine;

public readonly struct DebugCaptureMetadataInputs
{
    public readonly DebugRegistry Registry;
    public readonly DebugCaptureSetDefinition CaptureSet;
    public readonly ICameraRigContext CameraContext;
    public readonly ICelestialTimeController CelestialManager;
    public readonly Light SunLight;
    public readonly IPrecipitationDebugControl PrecipitationController;
    public readonly IWeatherProvider WeatherProvider;
    public readonly int ClimateMapResolutionId;
    public readonly bool ShowDetailedDebug;
    public readonly bool IncludeMeshIntegrityInDebugCaptures;

    public DebugCaptureMetadataInputs(
        DebugRegistry registry,
        DebugCaptureSetDefinition captureSet,
        ICameraRigContext cameraContext,
        ICelestialTimeController celestialManager,
        Light sunLight,
        IPrecipitationDebugControl precipitationController,
        IWeatherProvider weatherProvider,
        int climateMapResolutionId,
        bool showDetailedDebug,
        bool includeMeshIntegrityInDebugCaptures)
    {
        Registry = registry;
        CaptureSet = captureSet;
        CameraContext = cameraContext;
        CelestialManager = celestialManager;
        SunLight = sunLight;
        PrecipitationController = precipitationController;
        WeatherProvider = weatherProvider;
        ClimateMapResolutionId = climateMapResolutionId;
        ShowDetailedDebug = showDetailedDebug;
        IncludeMeshIntegrityInDebugCaptures = includeMeshIntegrityInDebugCaptures;
    }
}

public static class DebugCaptureMetadataBuilder
{
    public static string Build(
        in DebugCaptureMetadataInputs inputs,
        DebugModeId modeId,
        string modeName,
        int sourceWidth,
        int sourceHeight,
        int savedWidth,
        int savedHeight,
        string imagePath)
    {
        ICameraRigContext cameraContext = inputs.CameraContext;
        DebugCaptureSetDefinition captureSet = inputs.CaptureSet;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== F10 DEBUG CAPTURE ===");
        sb.AppendLine($"Image: {imagePath}");
        sb.AppendLine($"Source: {sourceWidth}x{sourceHeight}");
        sb.AppendLine($"Saved: {savedWidth}x{savedHeight}");
        sb.AppendLine($"Mode: {modeId}:{modeName}");
        sb.AppendLine($"CaptureSet: {captureSet.Name} ({captureSet.Id})");
        sb.AppendLine($"Time: {System.DateTime.Now:O}");
        sb.AppendLine();

        sb.AppendLine("--- Camera ---");
        if (cameraContext != null)
        {
            sb.AppendLine($"Position: {cameraContext.CameraTransform.position.x:F2}, {cameraContext.CameraTransform.position.y:F2}, {cameraContext.CameraTransform.position.z:F2}");
            sb.AppendLine($"Forward: {cameraContext.CameraTransform.forward.x:F4}, {cameraContext.CameraTransform.forward.y:F4}, {cameraContext.CameraTransform.forward.z:F4}");
            sb.AppendLine($"Up: {cameraContext.CameraTransform.up.x:F4}, {cameraContext.CameraTransform.up.y:F4}, {cameraContext.CameraTransform.up.z:F4}");
            sb.AppendLine($"Right: {cameraContext.CameraTransform.right.x:F4}, {cameraContext.CameraTransform.right.y:F4}, {cameraContext.CameraTransform.right.z:F4}");
            Camera captureCamera = cameraContext.CameraComponent;
            if (captureCamera != null)
            {
                sb.AppendLine($"Projection: orthographic={captureCamera.orthographic}, fov={captureCamera.fieldOfView:F2}, aspect={captureCamera.aspect:F4}, near={captureCamera.nearClipPlane:F3}, far={captureCamera.farClipPlane:F1}");
                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(captureCamera);
                string[] planeNames = { "Left", "Right", "Bottom", "Top", "Near", "Far" };
                for (int i = 0; i < frustumPlanes.Length; i++)
                {
                    Plane plane = frustumPlanes[i];
                    Vector3 normal = plane.normal;
                    string planeName = i < planeNames.Length ? planeNames[i] : i.ToString();
                    sb.AppendLine($"FrustumPlane.{planeName}: normal=({normal.x:F5},{normal.y:F5},{normal.z:F5}), distance={plane.distance:F3}");
                }
            }
            sb.AppendLine($"Surface view: {cameraContext.SurfaceView}");
            if (cameraContext.TargetCenter != null)
            {
                Vector3 dirToSurface = (cameraContext.CameraTransform.position - cameraContext.TargetCenter.position).normalized;
                (float lat, float lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
                sb.AppendLine($"LatLonDeg: {lat * Mathf.Rad2Deg:F2}, {lon * Mathf.Rad2Deg:F2}");
                sb.AppendLine($"DistanceToCenter: {Vector3.Distance(cameraContext.CameraTransform.position, cameraContext.TargetCenter.position):F2}");
            }
            sb.AppendLine($"PlanetRadius: {cameraContext.PlanetRadius:F2}");
            sb.AppendLine($"SeaLevelRadius: {cameraContext.SeaLevelRadius:F2}");
            sb.AppendLine($"ElevationMinMax: {cameraContext.ElevationMin:F4}, {cameraContext.ElevationMax:F4}");
        }
        sb.AppendLine();

        sb.AppendLine("--- Runtime ---");
        sb.AppendLine($"FPS: {(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):F1}");
        sb.AppendLine($"FrameTarget: {Application.targetFrameRate}");
        sb.AppendLine($"VSync: {QualitySettings.vSyncCount}");
        int qualityLevel = QualitySettings.GetQualityLevel();
        string[] qualityNames = QualitySettings.names;
        string qualityName = qualityLevel >= 0 && qualityLevel < qualityNames.Length
            ? qualityNames[qualityLevel]
            : "Unknown";
        sb.AppendLine($"QualityLevel: {qualityLevel} ({qualityName})");
        sb.AppendLine($"CloudQuality: tier={QualityController.AppliedQualityTier}, low={QualityController.IsCloudLowQualityEnabled}, stepMultiplier={QualityController.CloudStepMultiplier:F2}");
        if (inputs.CelestialManager != null)
            sb.AppendLine($"SunFrozen: {inputs.CelestialManager.IsTimeFrozen}");

        if (inputs.SunLight != null && cameraContext != null && cameraContext.PlanetRadius > 0f)
        {
            Vector3 sd = -inputs.SunLight.transform.forward;
            sb.AppendLine($"SunDirection: {sd.x:F4}, {sd.y:F4}, {sd.z:F4}");
            sb.AppendLine($"SunLight: intensity={inputs.SunLight.intensity:F3}, color=({inputs.SunLight.color.r:F3},{inputs.SunLight.color.g:F3},{inputs.SunLight.color.b:F3})");
            float sunElevation = Vector3.Dot(sd, (cameraContext.CameraTransform.position - cameraContext.PlanetCenter).normalized);
            sb.AppendLine($"SunElevationDeg: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F2}");
        }
        IPrecipitationDebugControl precipitation = inputs.PrecipitationController;
        if (precipitation != null && cameraContext != null)
        {
            sb.AppendLine($"PrecipitationEnabled: {precipitation.PrecipitationRenderingEnabled}");
            sb.AppendLine($"PrecipLocalParticlesEnabled: {precipitation.LocalPrecipitationParticlesEnabled}");
            sb.AppendLine($"PrecipLocalParticlesForCamera: {precipitation.ShouldRenderLocalParticles(cameraContext.CameraComponent)}");
            sb.AppendLine($"WeatherParticles: radius={precipitation.LocalParticleRadius:F1}m, dust={precipitation.DustParticleCount}, snow={precipitation.SnowParticleCount}, proof={precipitation.WeatherParticleProofMode}");
            sb.AppendLine($"WeatherParticleDimensions: {precipitation.WeatherParticleSettingsSummary}");
        }
        if (inputs.WeatherProvider != null && cameraContext != null)
        {
            Vector3 samplePosition = cameraContext.CameraTransform.position;
            Vector3 fromCenter = samplePosition - cameraContext.PlanetCenter;
            if (cameraContext.SeaLevelRadius > 0f && fromCenter.sqrMagnitude > 0.0001f)
            {
                samplePosition = cameraContext.PlanetCenter +
                    fromCenter.normalized * cameraContext.SeaLevelRadius;
            }

            WeatherSample weather = inputs.WeatherProvider.SampleWeather(samplePosition);
            sb.AppendLine($"Wind: speed={inputs.WeatherProvider.WindSpeedMetersPerSecond:F2}m/s, strength={inputs.WeatherProvider.WindStrength01:F3}, direction=({inputs.WeatherProvider.WindDirection.x:F3},{inputs.WeatherProvider.WindDirection.y:F3},{inputs.WeatherProvider.WindDirection.z:F3})");
            sb.AppendLine($"ClimateMapResolution: {Shader.GetGlobalFloat(inputs.ClimateMapResolutionId):F0}");
            sb.AppendLine($"CameraClimate: temperature={weather.TemperatureCelsius:F2}C");
        }
        sb.AppendLine();

        DebugRuntimeState runtimeState = new DebugRuntimeState(
            inputs.Registry,
            inputs.CameraContext,
            inputs.CelestialManager,
            inputs.PrecipitationController,
            inputs.WeatherProvider,
            modeId,
            modeName,
            captureSet.Id,
            captureSet.Name,
            inputs.ShowDetailedDebug,
            inputs.IncludeMeshIntegrityInDebugCaptures);
        var captureContext = new DebugCaptureContext(
            runtimeState,
            modeId,
            modeName,
            sourceWidth,
            sourceHeight,
            savedWidth,
            savedHeight,
            imagePath);

        for (int i = 0; i < inputs.Registry.Diagnostics.Count; i++)
        {
            IDebugDiagnosticProvider diagnostic = inputs.Registry.Diagnostics[i];
            if (!diagnostic.IsEnabled)
                continue;

            diagnostic.Refresh(runtimeState);
            diagnostic.AppendCachedResult(captureContext, sb);
        }

        for (int i = 0; i < inputs.Registry.MetadataProviders.Count; i++)
            inputs.Registry.MetadataProviders[i].AppendMetadata(captureContext, sb);

        return sb.ToString();
    }
}
