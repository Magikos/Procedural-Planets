using UnityEngine;

public static class SunLighting
{
    static readonly int SunParamsId = Shader.PropertyToID(ShaderGlobalIds.SunParams);

    public static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
    {
        float lengthSquared = direction.sqrMagnitude;
        normalized = Vector3.zero;
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-8f) return false;
        normalized = direction / Mathf.Sqrt(lengthSquared);
        return true;
    }

    public static void Apply(Vector3 direction, Light light, Vector3 center, float radius)
    {
        if (light != null)
        {
            Vector3 forward = -direction;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);
            light.transform.SetPositionAndRotation(
                center + direction * Mathf.Max(radius * 10f, 1000f),
                Quaternion.LookRotation(forward, up.normalized));
        }
        Shader.SetGlobalVector(SunParamsId, direction);
    }
}
