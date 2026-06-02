using UnityEngine;

public interface IPlanetSurfaceSampler
{
    bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius);
}

public struct PlanetSurfaceRaycastHit
{
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
    public float SurfaceRadius;
}

public interface IPlanetSurfaceRaycaster
{
    bool TryRaycastSurface(Ray worldRay, float maxDistance, out PlanetSurfaceRaycastHit hit);
}
