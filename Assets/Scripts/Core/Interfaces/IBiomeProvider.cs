using UnityEngine;

public interface IBiomeProvider
{
    void Initialize(int seed);
    BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation);
    Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation);
}
