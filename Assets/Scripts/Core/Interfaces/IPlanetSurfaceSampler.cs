using UnityEngine;

public interface IPlanetSurfaceSampler
{
    bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius);
}
