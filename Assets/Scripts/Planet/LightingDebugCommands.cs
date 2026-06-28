using UnityEngine;

[CommandPrefix("light")]
public static class LightingDebugCommands
{
    static readonly int _sunParamsId = Shader.PropertyToID(ShaderGlobalIds.SunParams);
    static readonly int _nightAmbientIntensityId = Shader.PropertyToID(ShaderGlobalIds.NightAmbientIntensity);
    static string _lastSource = "none";
    static Vector3 _lastDirection;

    [ConsoleCommand("local-noon", "Aim the sun at the camera-facing planet region. Works with or without a CelestialManager.", MonoTargetType.Static)]
    public static string LocalNoonCmd()
    {
        if (TryFindCelestial(out ICelestialTimeController celestial) && celestial.TrySetLocalTimeOfDay(0.5f))
        {
            celestial.SetTimeFrozen(true);
            Vector3 sunDirection = SafeNormalize(celestial.SunDirection, Vector3.up);
            ApplySunDirection(sunDirection, GetPlanetCenter(), GetPlanetRadius(), "celestial");
            return $"local noon via celestial time; sun=({sunDirection.x:F3},{sunDirection.y:F3},{sunDirection.z:F3})";
        }

        if (!TryGetCameraSunDirection(out Vector3 fallbackDirection, out string error))
            return error;

        ApplySunDirection(fallbackDirection, GetPlanetCenter(), GetPlanetRadius(), "fallback-directional");
        return $"local noon via directional light; sun=({fallbackDirection.x:F3},{fallbackDirection.y:F3},{fallbackDirection.z:F3})";
    }

    [ConsoleCommand("direction", "Set sun direction vector and upload _SunParams.", MonoTargetType.Static)]
    public static string DirectionCmd(Vector3 direction)
    {
        Vector3 sunDirection = SafeNormalize(direction, Vector3.up);
        ApplySunDirection(sunDirection, GetPlanetCenter(), GetPlanetRadius(), "manual-direction");
        return $"sun direction: ({sunDirection.x:F3},{sunDirection.y:F3},{sunDirection.z:F3})";
    }

    [ConsoleCommand("status", "Show active debug lighting path, sun vectors, and ambient fill.", MonoTargetType.Static)]
    public static string StatusCmd()
    {
        bool hasCelestial = TryFindCelestial(out ICelestialTimeController celestial);
        Light light = FindDirectionalLight();
        Vector3 shaderSun = Shader.GetGlobalVector(_sunParamsId);
        Vector3 lightSun = light != null ? -light.transform.forward : Vector3.zero;
        string source = hasCelestial ? "celestial-available" : "fallback-directional";
        string frozen = hasCelestial ? celestial.IsTimeFrozen.ToString() : "n/a";
        string celestialSun = hasCelestial ? FormatVector(celestial.SunDirection) : "n/a";
        string lightStatus = light != null
            ? $"present intensity={light.intensity:F3}"
            : "missing";

        return $"source={source}, last={_lastSource}, frozen={frozen}, "
            + $"shaderSun={FormatVector(shaderSun)}, lightSun={FormatVector(lightSun)}, "
            + $"celestialSun={celestialSun}, ambient={Shader.GetGlobalFloat(_nightAmbientIntensityId):F3}, "
            + $"directionalLight={lightStatus}, lastSun={FormatVector(_lastDirection)}";
    }

    [ConsoleCommand("ambient", "Get or set diagnostic night/fill ambient intensity (0-1).", MonoTargetType.Static)]
    public static string AmbientCmd(float? intensity = null)
    {
        if (intensity == null)
            return $"ambient fill: {Shader.GetGlobalFloat(_nightAmbientIntensityId):F3}";

        float value = Mathf.Clamp01(intensity.Value);
        if (TryFindCelestial(out CelestialManager celestial))
        {
            celestial.AmbientMinIntensity = value;
            celestial.AmbientMaxIntensity = value;
        }

        Shader.SetGlobalFloat(_nightAmbientIntensityId, value);
        return $"ambient fill: {value:F3}";
    }

    static bool TryGetCameraSunDirection(out Vector3 direction, out string error)
    {
        Vector3 center = GetPlanetCenter();
        Transform cameraTransform = GetCameraTransform();
        if (cameraTransform == null)
        {
            direction = Vector3.up;
            error = "no camera available";
            return false;
        }

        direction = cameraTransform.position - center;
        if (direction.sqrMagnitude < 0.0001f)
            direction = -cameraTransform.forward;

        direction = SafeNormalize(direction, Vector3.up);
        error = null;
        return true;
    }

    static Transform GetCameraTransform()
    {
        if (ServiceLocator.TryGet(out ICameraRigContext context) && context.CameraTransform != null)
            return context.CameraTransform;

        Camera camera = Camera.main;
        return camera != null ? camera.transform : null;
    }

    static Vector3 GetPlanetCenter()
    {
        if (ServiceLocator.TryGet(out ICameraRigContext cameraContext))
        {
            if (cameraContext.TargetCenter != null)
                return cameraContext.TargetCenter.position;

            return cameraContext.PlanetCenter;
        }

        if (ServiceLocator.TryGet(out IPlanet planet) && planet.Transform != null)
            return planet.Transform.position;

        return Vector3.zero;
    }

    static float GetPlanetRadius()
    {
        if (ServiceLocator.TryGet(out ICameraRigContext cameraContext) && cameraContext.PlanetRadius > 0f)
            return cameraContext.PlanetRadius;

        if (ServiceLocator.TryGet(out IPlanet planet) && planet.LastGeneratedRadius > 0f)
            return planet.LastGeneratedRadius;

        return 1000f;
    }

    static void ApplySunDirection(Vector3 sunDirection, Vector3 center, float radius, string source)
    {
        sunDirection = SafeNormalize(sunDirection, Vector3.up);
        Light light = FindDirectionalLight();
        if (light != null)
        {
            Vector3 forward = -sunDirection;
            light.transform.rotation = Quaternion.LookRotation(forward, StableUp(forward));
            light.transform.position = center - sunDirection * Mathf.Max(radius * 10f, 1000f);
        }

        Shader.SetGlobalVector(_sunParamsId, sunDirection);
        _lastSource = source;
        _lastDirection = sunDirection;
    }

    static bool TryFindCelestial(out ICelestialTimeController celestial)
    {
        if (ServiceLocator.TryGet(out celestial))
            return true;

        celestial = UnityEngine.Object.FindAnyObjectByType(
            typeof(CelestialManager),
            FindObjectsInactive.Exclude) as ICelestialTimeController;
        return celestial != null;
    }

    static bool TryFindCelestial(out CelestialManager celestial)
    {
        celestial = UnityEngine.Object.FindAnyObjectByType(
            typeof(CelestialManager),
            FindObjectsInactive.Exclude) as CelestialManager;
        return celestial != null;
    }

    static Light FindDirectionalLight()
    {
        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    static Vector3 StableUp(Vector3 forward)
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(Vector3.right, forward);
        return SafeNormalize(up, Vector3.forward);
    }

    static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.sqrMagnitude > 0.0001f ? value.normalized : fallback.normalized;
    }

    static string FormatVector(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }
}
