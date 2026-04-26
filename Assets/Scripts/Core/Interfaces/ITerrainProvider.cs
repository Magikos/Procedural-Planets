using UnityEngine;

public interface ITerrainProvider
{
    void Initialize(int seed);
    float EvaluateElevation(Vector3 pointOnUnitSphere);
    float GetScaledElevation(float unscaledElevation);
    float ElevationMin { get; }
    float ElevationMax { get; }
}
