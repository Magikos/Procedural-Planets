using UnityEngine;

// Consumer-facing evaluation contract. Setup (Configure + Initialize) is owned by the
// concrete implementation and its owner (Planet), because the settings types live in the
// Planet assembly and cannot be referenced from Core.
public interface IBiomeProvider
{
    BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation);
    Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation);
}
