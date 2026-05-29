using UnityEngine;

// Consumer-facing evaluation contract. Setup (Configure + Initialize) is owned by the
// concrete implementation and its owner (Planet), because the settings types live in the
// Planet assembly and cannot be referenced from Core.
public interface ITerrainProvider
{
    float EvaluateElevation(Vector3 pointOnUnitSphere);
    float GetScaledElevation(float unscaledElevation);
    float ElevationMin { get; }
    float ElevationMax { get; }
}
