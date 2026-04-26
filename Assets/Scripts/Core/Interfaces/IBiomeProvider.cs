using UnityEngine;

public interface IBiomeProvider
{
    void Initialize(int seed);
    BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation);
    float BiomePercentFromPoint(Vector3 pointOnUnitSphere, float elevation);
}
