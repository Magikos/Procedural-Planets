using UnityEngine;

public interface ITemperatureProvider
{
    void Initialize(int seed);
    float Evaluate(Vector3 pointOnUnitSphere);
}
