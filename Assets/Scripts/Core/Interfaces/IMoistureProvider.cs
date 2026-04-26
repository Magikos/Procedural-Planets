using UnityEngine;

public interface IMoistureProvider
{
    void Initialize(int seed);
    float Evaluate(Vector3 pointOnUnitSphere);
}
