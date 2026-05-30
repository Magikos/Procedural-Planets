using UnityEngine;

// Consumer-facing evaluation contract. Setup (Configure + Initialize) is owned by the
// concrete implementation and its owner (Planet), because the settings types live in the
// Planet assembly and cannot be referenced from Core.
public interface IBiomeProvider
{
    BiomeResult EvaluateBiome(Vector3 pointOnUnitSphere, float elevation);
    Color GetBiomeColor(Vector3 pointOnUnitSphere, float elevation);

    // Returns the final blended color (same as GetBiomeColor) and writes the per-vertex
    // diagnostic data needed by the biome debug shaders:
    //   x = temperature (0..1), y = moisture (0..1),
    //   z = primary biome id normalized to BiomeCount, w = |latitude| (0=equator, 1=pole).
    // Computed in a single pass to avoid re-evaluating noise twice per vertex.
    Color GetBiomeColorAndData(Vector3 pointOnUnitSphere, float elevation, out Vector4 biomeData);
}
