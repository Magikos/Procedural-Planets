using UnityEngine;

[CommandPrefix("path")]
public static class SurfacePathDebugCommands
{
    static bool _strokeActive;
    static Vector3 _strokeDirection;
    static float _strokeSurfaceRadius;

    [ConsoleCommand("paint", "Paint and save a soft paved path mask where the camera is aimed. Args: radiusMeters strength01 regrowSeconds(0=permanent).", MonoTargetType.Static)]
    public static string PaintCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path paint requires an active Planet";
        if (!TryGetCameraRay(out Ray ray, out string error))
            return error;

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 5f, out float radius, out float alpha, out float regrow);
        return planet.TryPaintSurfacePathFromCamera(ray, radius, alpha, regrow, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("paint-here", "Paint and save a soft paved path mask under the camera. Args: radiusMeters strength01 regrowSeconds(0=permanent).", MonoTargetType.Static)]
    public static string PaintHereCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path paint-here requires an active Planet";
        if (!TryGetCameraTransform(out Transform cameraTransform, out string error))
            return error;

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 8f, out float radius, out float alpha, out float regrow);
        return planet.TryPaintSurfacePathAtWorldPosition(cameraTransform.position, radius, alpha, regrow, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("pattern-here", "Paint deterministic path test patterns under the camera. Args: sizeMeters strength01.", MonoTargetType.Static)]
    public static string PatternHereCmd(float? sizeMeters = null, float? strength = null)
    {
        if (!TryGetPlanet(out Planet planet))
            return "path pattern-here requires an active Planet";
        if (!TryGetCameraTransform(out Transform cameraTransform, out string error))
            return error;

        float size = Mathf.Clamp(sizeMeters ?? 220f, 16f, 1000f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return planet.TryPaintSurfacePathPatternAtWorldPosition(cameraTransform.position, size, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("stroke-start", "Start a saved path stroke at the terrain point under the camera aim.", MonoTargetType.Static)]
    public static string StrokeStartCmd()
    {
        if (!TryGetPlanet(out Planet planet))
            return "path stroke-start requires an active Planet";
        if (!TryGetCameraRay(out Ray ray, out string error))
            return error;
        if (!TryGetSurfaceDirection(planet, ray, out _strokeDirection, out _strokeSurfaceRadius, out error))
            return error;

        _strokeActive = true;
        return "path stroke started";
    }

    [ConsoleCommand("stroke-to", "Paint a saved path stroke from the previous point to the terrain point under camera aim. Args: radiusMeters strength01 regrowSeconds stepMeters.", MonoTargetType.Static)]
    public static string StrokeToCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null, float? stepMeters = null)
    {
        if (!_strokeActive)
            return "path stroke-to requires path.stroke-start first";
        if (!TryGetPlanet(out Planet planet))
            return "path stroke-to requires an active Planet";
        if (!TryGetCameraRay(out Ray ray, out string error))
            return error;
        if (!TryGetSurfaceDirection(planet, ray, out Vector3 endDirection, out float endSurfaceRadius, out error))
            return error;

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 5f, out float radius, out float alpha, out float regrow);
        float step = Mathf.Clamp(stepMeters ?? Mathf.Max(radius * 0.75f, 0.5f), 0.25f, 100f);
        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(_strokeDirection.normalized, endDirection.normalized), -1f, 1f));
        float surfaceRadius = Mathf.Max(_strokeSurfaceRadius, endSurfaceRadius, 1f);
        int segments = Mathf.Max(1, Mathf.CeilToInt(angle * surfaceRadius / step));

        int painted = 0;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 direction = Vector3.Slerp(_strokeDirection, endDirection, t).normalized;
            if (planet.TryPaintSurfacePathAtLocalDirection(direction, radius, alpha, regrow, out _))
                painted++;
        }

        _strokeDirection = endDirection;
        _strokeSurfaceRadius = endSurfaceRadius;
        return $"path stroke painted {painted}/{segments + 1} stamp(s), step={step:F1}m";
    }

    [ConsoleCommand("stroke-end", "End the active saved path stroke.", MonoTargetType.Static)]
    public static string StrokeEndCmd()
    {
        _strokeActive = false;
        _strokeDirection = default;
        _strokeSurfaceRadius = 0f;
        return "path stroke ended";
    }

    [ConsoleCommand("stroke-cancel", "Alias for path.stroke-end.", MonoTargetType.Static)]
    public static string StrokeCancelCmd() => StrokeEndCmd();

    [ConsoleCommand("clear", "Clear runtime painted path masks without deleting saved path stamps.", MonoTargetType.Static)]
    public static string ClearCmd()
    {
        if (!TryGetPlanet(out Planet planet))
            return "path clear requires an active Planet";

        int cleared = planet.ClearSurfacePaths();
        return $"cleared path masks on {cleared} chunks";
    }

    [ConsoleCommand("replay", "Clear runtime path masks, then replay saved path stamps.", MonoTargetType.Static)]
    public static string ReplayCmd()
    {
        return TryGetPlanet(out Planet planet)
            ? planet.ReplaySurfacePaths()
            : "path replay requires an active Planet";
    }

    [ConsoleCommand("clear-saved", "Delete saved path stamps and clear runtime path masks.", MonoTargetType.Static)]
    public static string ClearSavedCmd()
    {
        return TryGetPlanet(out Planet planet)
            ? planet.ClearSavedSurfacePaths()
            : "path clear-saved requires an active Planet";
    }

    [ConsoleCommand("debug", "Toggle hot-pink path mask visualization.", MonoTargetType.Static)]
    public static string DebugCmd(bool? enabled = null)
    {
        return TryGetPlanet(out Planet planet)
            ? planet.SetSurfacePathDebug(enabled)
            : "path debug requires an active Planet";
    }

    [ConsoleCommand("status", "Show path mask runtime support status.", MonoTargetType.Static)]
    public static string StatusCmd()
    {
        return TryGetPlanet(out Planet planet)
            ? planet.SurfacePathStatus()
            : "path mask unavailable: no active Planet";
    }

    [ConsoleCommand("mouse", "Get or set mouse path painting. Left-drag paints saved path stamps.", MonoTargetType.Static)]
    public static string MouseCmd(bool? enabled = null)
    {
        SurfacePathMousePainter tool = SurfacePathMousePainter.Find();
        if (!enabled.HasValue)
            return tool != null ? tool.Status() : "path mouse: off";

        if (!enabled.Value)
        {
            if (tool != null)
                tool.BrushActive = false;
            return "path mouse: off";
        }

        tool = SurfacePathMousePainter.GetOrCreate();
        tool.BrushActive = true;
        return tool.Status();
    }

    [ConsoleCommand("mouse-brush", "Get or set mouse path brush. Args: radiusMeters strength01 regrowSeconds spacingMeters.", MonoTargetType.Static)]
    public static string MouseBrushCmd(float? radiusMeters = null, float? strength = null,
        float? regrowSeconds = null, float? spacingMeters = null)
    {
        SurfacePathMousePainter tool = SurfacePathMousePainter.GetOrCreate();
        tool.SetBrush(radiusMeters, strength, regrowSeconds, spacingMeters);
        return tool.Status();
    }

    static bool TryGetPlanet(out Planet planet)
    {
        planet = null;
        if (ServiceLocator.TryGet(out IPlanet servicePlanet) && servicePlanet is Planet concrete)
        {
            planet = concrete;
            return true;
        }

        planet = Object.FindAnyObjectByType<Planet>();
        return planet != null;
    }

    static void NormalizePaintArgs(float? radiusMeters, float? strength, float? regrowSeconds,
        float defaultRadius, out float radius, out float alpha, out float regrow)
    {
        radius = Mathf.Clamp(radiusMeters ?? defaultRadius, 0.25f, 250f);
        alpha = Mathf.Clamp01(strength ?? 1f);
        regrow = Mathf.Max(0f, regrowSeconds ?? 0f);
    }

    static bool TryGetSurfaceDirection(Planet planet, Ray ray, out Vector3 localDirection, out float surfaceRadius, out string error)
    {
        localDirection = default;
        surfaceRadius = 0f;
        float maxDistance = Mathf.Max(planet.LastGeneratedRadius * 4f, 10000f);
        if (!planet.TryRaycastSurface(ray, maxDistance, out PlanetSurfaceRaycastHit hit))
        {
            error = "path stroke missed the visible planet surface";
            return false;
        }

        localDirection = planet.transform.InverseTransformPoint(hit.Point).normalized;
        surfaceRadius = hit.SurfaceRadius;
        error = null;
        return true;
    }

    static bool TryGetCameraRay(out Ray ray, out string error)
    {
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
        {
            ray = default;
            return false;
        }

        ray = new Ray(cameraTransform.position, cameraTransform.forward);
        error = null;
        return true;
    }

    static bool TryGetCameraTransform(out Transform cameraTransform, out string error)
    {
        cameraTransform = null;
        if (ServiceLocator.TryGet(out ICameraRigContext context) && context.CameraTransform != null)
            cameraTransform = context.CameraTransform;

        Camera camera = Camera.main;
        if (cameraTransform == null && camera != null)
            cameraTransform = camera.transform;

        error = cameraTransform == null ? "path paint requires an active camera" : null;
        return cameraTransform != null;
    }
}
