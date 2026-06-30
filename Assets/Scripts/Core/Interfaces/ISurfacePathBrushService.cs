using UnityEngine;

public enum SurfacePathShape
{
    SoftDisc,
    HardDisc,
    HardSquare,
}

public enum SurfacePathOperation
{
    Paint,
    Erase,
}

public interface ISurfacePathBrushService
{
    bool TryGetSurfacePathLocalDirection(Vector3 worldPoint, out Vector3 localUnitDirection);

    bool TryPaintSurfacePathBrushAtLocalDirection(
        Vector3 localUnitDirection,
        float radiusMeters,
        float strength,
        float regrowSeconds,
        SurfacePathShape shape,
        SurfacePathOperation operation,
        out string summary,
        bool saveStamp = true,
        bool invalidateGrass = true,
        bool saveImmediately = true);

    void FlushSurfacePathEdits();

    bool TryBuildSurfacePathBrushPreview(
        Vector3 localUnitDirection,
        float surfaceRadius,
        float radiusMeters,
        SurfacePathShape shape,
        Vector3[] points,
        int segments,
        float liftMeters,
        out int count);
}
