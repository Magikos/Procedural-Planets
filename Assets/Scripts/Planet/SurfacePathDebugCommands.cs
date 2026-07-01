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
        if (!TryGetPathBrush(out ISurfacePathBrushService brush, out string error))
            return error;
        if (!TryGetCameraRay(out Ray ray, out error))
            return error;
        if (!TryGetSurfaceDirection(ray, out Vector3 direction, out _, out error))
            return error;

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 5f, out float radius, out float alpha, out float regrow);
        return brush.TryPaintSurfacePathBrushAtLocalDirection(direction, radius, alpha, regrow,
                SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("paint-here", "Paint and save a soft paved path mask under the camera. Args: radiusMeters strength01 regrowSeconds(0=permanent).", MonoTargetType.Static)]
    public static string PaintHereCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null)
    {
        if (!TryGetPathBrush(out ISurfacePathBrushService brush, out string error))
            return error;
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
            return error;
        if (!brush.TryGetSurfacePathLocalDirection(cameraTransform.position, out Vector3 direction))
            return "path paint-here requires a camera away from the planet center";

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 8f, out float radius, out float alpha, out float regrow);
        return brush.TryPaintSurfacePathBrushAtLocalDirection(direction, radius, alpha, regrow,
                SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("pattern-here", "Paint deterministic path test patterns under the camera. Args: sizeMeters strength01.", MonoTargetType.Static)]
    public static string PatternHereCmd(float? sizeMeters = null, float? strength = null)
    {
        if (!TryGetPathEdits(out SurfacePathEditController edits, out string error))
            return error;
        if (!TryGetPathBrush(out ISurfacePathBrushService brush, out error))
            return error;
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
            return error;
        if (!brush.TryGetSurfacePathLocalDirection(cameraTransform.position, out Vector3 direction))
            return "path pattern-here requires a camera away from the planet center";

        float size = Mathf.Clamp(sizeMeters ?? 220f, 16f, 1000f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return edits.TryPaintPattern(direction, size, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("stroke-start", "Start a saved path stroke at the terrain point under the camera aim.", MonoTargetType.Static)]
    public static string StrokeStartCmd()
    {
        if (!TryGetCameraRay(out Ray ray, out string error))
            return error;
        if (!TryGetSurfaceDirection(ray, out _strokeDirection, out _strokeSurfaceRadius, out error))
            return error;

        _strokeActive = true;
        return "path stroke started";
    }

    [ConsoleCommand("stroke-to", "Paint a saved path stroke from the previous point to the terrain point under camera aim. Args: radiusMeters strength01 regrowSeconds stepMeters.", MonoTargetType.Static)]
    public static string StrokeToCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null, float? stepMeters = null)
    {
        if (!_strokeActive)
            return "path stroke-to requires path.stroke-start first";
        if (!TryGetPathBrush(out ISurfacePathBrushService brush, out string error))
            return error;
        if (!TryGetCameraRay(out Ray ray, out error))
            return error;
        if (!TryGetSurfaceDirection(ray, out Vector3 endDirection, out float endSurfaceRadius, out error))
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
            if (brush.TryPaintSurfacePathBrushAtLocalDirection(direction, radius, alpha, regrow,
                    SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, out _))
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
        if (!TryGetPathEdits(out SurfacePathEditController edits, out string error))
            return error;

        int cleared = edits.ClearRuntimeMasks();
        return $"cleared path masks on {cleared} chunks";
    }

    [ConsoleCommand("replay", "Clear runtime path masks, then replay saved path stamps.", MonoTargetType.Static)]
    public static string ReplayCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.ReplaySavedStamps()
            : "path replay requires an active Planet";
    }

    [ConsoleCommand("clear-saved", "Delete saved path stamps and clear runtime path masks.", MonoTargetType.Static)]
    public static string ClearSavedCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.ClearSavedStamps()
            : "path clear-saved requires an active Planet";
    }

    [ConsoleCommand("debug", "Toggle hot-pink path mask visualization.", MonoTargetType.Static)]
    public static string DebugCmd(bool? enabled = null)
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.SetDebug(enabled)
            : "path debug requires an active Planet";
    }

    [ConsoleCommand("debug-wear", "Toggle raw grayscale path-wear texture visualization.", MonoTargetType.Static)]
    public static string DebugWearCmd(bool? enabled = null)
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.SetDebugWear(enabled)
            : "path debug-wear requires an active Planet";
    }

    [ConsoleCommand("status", "Show path mask runtime support status.", MonoTargetType.Static)]
    public static string StatusCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.Status()
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

    [ConsoleCommand("mouse-shape", "Get or set mouse path shape: soft-disc, hard-disc, square.", MonoTargetType.Static)]
    public static string MouseShapeCmd(string shape = null)
    {
        SurfacePathMousePainter tool = SurfacePathMousePainter.GetOrCreate();
        if (!string.IsNullOrWhiteSpace(shape) && !tool.TrySetShape(shape))
            return "path mouse-shape expects: soft-disc, hard-disc, square";
        return tool.Status();
    }

    static bool TryGetPathBrush(out ISurfacePathBrushService brush, out string error)
    {
        if (ServiceLocator.TryGet(out brush))
        {
            error = null;
            return true;
        }

        error = "path paint requires an active surface path service";
        return false;
    }

    static bool TryGetPathEdits(out SurfacePathEditController edits, out string error)
    {
        if (ServiceLocator.TryGet(out edits))
        {
            error = null;
            return true;
        }

        error = "path edit controller requires an active Planet";
        return false;
    }

    static void NormalizePaintArgs(float? radiusMeters, float? strength, float? regrowSeconds,
        float defaultRadius, out float radius, out float alpha, out float regrow)
    {
        radius = Mathf.Clamp(radiusMeters ?? defaultRadius, 0.25f, 250f);
        alpha = Mathf.Clamp01(strength ?? 1f);
        regrow = Mathf.Max(0f, regrowSeconds ?? 0f);
    }

    static bool TryGetSurfaceDirection(Ray ray, out Vector3 localDirection, out float surfaceRadius, out string error)
    {
        localDirection = default;
        surfaceRadius = 0f;
        if (!TryGetPathBrush(out ISurfacePathBrushService brush, out error))
            return false;
        if (!ServiceLocator.TryGet(out IPlanetSurfaceRaycaster raycaster))
        {
            error = "path stroke requires an active planet surface raycaster";
            return false;
        }

        float maxDistance = GetMaxRayDistance();
        if (!raycaster.TryRaycastSurface(ray, maxDistance, out PlanetSurfaceRaycastHit hit))
        {
            error = "path stroke missed the visible planet surface";
            return false;
        }

        if (!brush.TryGetSurfacePathLocalDirection(hit.Point, out localDirection))
        {
            error = "path stroke hit an invalid surface point";
            return false;
        }

        surfaceRadius = hit.SurfaceRadius;
        error = null;
        return true;
    }

    static float GetMaxRayDistance()
    {
        if (ServiceLocator.TryGet(out IPlanet planet) && planet.LastGeneratedRadius > 0f)
            return Mathf.Max(planet.LastGeneratedRadius * 4f, 10000f);

        Camera camera = Camera.main;
        return camera != null ? Mathf.Max(camera.farClipPlane, 10000f) : 10000f;
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

[CommandPrefix("scorch")]
public static class SurfaceScorchDebugCommands
{
    [ConsoleCommand("paint", "Paint and save a soft scorched mask where the camera is aimed. Args: radiusMeters strength01 regrowSeconds(0=permanent).", MonoTargetType.Static)]
    public static string PaintCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null)
    {
        if (!TryGetPathEdits(out SurfacePathEditController edits, out string error))
            return error;
        if (!TryGetCameraRay(out Ray ray, out error))
            return error;
        if (!TryGetSurfaceDirection(edits, ray, out Vector3 direction, out error))
            return error;

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 10f, out float radius, out float alpha, out float regrow);
        return edits.TryPaintScorch(direction, radius, alpha, regrow,
                SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, saveStamp: true, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("paint-here", "Paint and save a soft scorched mask under the camera. Args: radiusMeters strength01 regrowSeconds(0=permanent).", MonoTargetType.Static)]
    public static string PaintHereCmd(float? radiusMeters = null, float? strength = null, float? regrowSeconds = null)
    {
        if (!TryGetPathEdits(out SurfacePathEditController edits, out string error))
            return error;
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
            return error;
        if (!edits.TryGetSurfacePathLocalDirection(cameraTransform.position, out Vector3 direction))
            return "scorch paint-here requires a camera away from the planet center";

        NormalizePaintArgs(radiusMeters, strength, regrowSeconds, 16f, out float radius, out float alpha, out float regrow);
        return edits.TryPaintScorch(direction, radius, alpha, regrow,
                SurfacePathShape.SoftDisc, SurfacePathOperation.Paint, saveStamp: true, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("pattern-here", "Paint deterministic scorch test patterns under the camera. Args: sizeMeters strength01.", MonoTargetType.Static)]
    public static string PatternHereCmd(float? sizeMeters = null, float? strength = null)
    {
        if (!TryGetPathEdits(out SurfacePathEditController edits, out string error))
            return error;
        if (!TryGetCameraTransform(out Transform cameraTransform, out error))
            return error;
        if (!edits.TryGetSurfacePathLocalDirection(cameraTransform.position, out Vector3 direction))
            return "scorch pattern-here requires a camera away from the planet center";

        float size = Mathf.Clamp(sizeMeters ?? 220f, 16f, 1000f);
        float alpha = Mathf.Clamp01(strength ?? 1f);
        return edits.TryPaintScorchPattern(direction, size, alpha, out string summary)
            ? summary
            : summary;
    }

    [ConsoleCommand("clear", "Delete saved scorch stamps and replay remaining surface edits.", MonoTargetType.Static)]
    public static string ClearCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.ClearSavedScorchStamps()
            : "scorch clear requires an active Planet";
    }

    [ConsoleCommand("replay", "Replay saved path and scorch stamps.", MonoTargetType.Static)]
    public static string ReplayCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.ReplaySavedStamps()
            : "scorch replay requires an active Planet";
    }

    [ConsoleCommand("status", "Show saved surface edit status, including scorch count.", MonoTargetType.Static)]
    public static string StatusCmd()
    {
        return TryGetPathEdits(out SurfacePathEditController edits, out _)
            ? edits.Status()
            : "scorch status requires an active Planet";
    }

    static bool TryGetPathEdits(out SurfacePathEditController edits, out string error)
    {
        if (ServiceLocator.TryGet(out edits))
        {
            error = null;
            return true;
        }

        error = "scorch requires an active Planet";
        return false;
    }

    static void NormalizePaintArgs(float? radiusMeters, float? strength, float? regrowSeconds,
        float defaultRadius, out float radius, out float alpha, out float regrow)
    {
        radius = Mathf.Clamp(radiusMeters ?? defaultRadius, 0.25f, 250f);
        alpha = Mathf.Clamp01(strength ?? 1f);
        regrow = Mathf.Max(0f, regrowSeconds ?? 0f);
    }

    static bool TryGetSurfaceDirection(SurfacePathEditController edits, Ray ray, out Vector3 localDirection, out string error)
    {
        localDirection = default;
        if (!ServiceLocator.TryGet(out IPlanetSurfaceRaycaster raycaster))
        {
            error = "scorch paint requires an active planet surface raycaster";
            return false;
        }

        float maxDistance = GetMaxRayDistance();
        if (!raycaster.TryRaycastSurface(ray, maxDistance, out PlanetSurfaceRaycastHit hit))
        {
            error = "scorch paint missed the visible planet surface";
            return false;
        }

        if (!edits.TryGetSurfacePathLocalDirection(hit.Point, out localDirection))
        {
            error = "scorch paint hit an invalid surface point";
            return false;
        }

        error = null;
        return true;
    }

    static float GetMaxRayDistance()
    {
        if (ServiceLocator.TryGet(out IPlanet planet) && planet.LastGeneratedRadius > 0f)
            return Mathf.Max(planet.LastGeneratedRadius * 4f, 10000f);

        Camera camera = Camera.main;
        return camera != null ? Mathf.Max(camera.farClipPlane, 10000f) : 10000f;
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

        error = cameraTransform == null ? "scorch paint requires an active camera" : null;
        return cameraTransform != null;
    }
}
