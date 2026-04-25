using UnityEngine;

public interface ITerrainProvider
{
    void Initialize(ShapeSettings settings, int seed);
    float EvaluateElevation(Vector3 pointOnUnitSphere);
    float GetScaledElevation(float unscaledElevation);
    MinMax ElevationRange { get; }
}
