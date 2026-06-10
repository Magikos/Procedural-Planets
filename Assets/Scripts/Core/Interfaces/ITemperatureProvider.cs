using UnityEngine;

public interface ITemperatureProvider
{
    void Initialize(int seed);
    float Evaluate(Vector3 pointOnUnitSphere);
}

// Canonical biome-climate contract. Temperature and moisture are normalized signals;
// elevation is carried with the sample so altitude effects can be added without changing
// every biome consumer again.
public interface IClimateProvider
{
    void Initialize(int seed);
    ClimateSample Evaluate(Vector3 pointOnUnitSphere, float elevation);
}

// Authoritative world-space climate query. Consumers should use this instead of
// reconstructing latitude, terrain elevation, or temperature ranges independently.
public interface IClimateSampler
{
    bool TrySampleClimate(Vector3 worldPosition, out ClimateSample sample);
}
