using UnityEngine;

public sealed class ActorReviewLighting : MonoBehaviour
{
    public Light Sun;
    Vector4 _sun, _center, _cloudShadow;

    void OnEnable()
    {
        _sun = Shader.GetGlobalVector(ShaderGlobalIds.SunParams);
        _center = Shader.GetGlobalVector(ShaderGlobalIds.PlanetCenter);
        _cloudShadow = Shader.GetGlobalVector(ShaderGlobalIds.CloudShadowParams);
    }

    void LateUpdate()
    {
        if (Sun == null) return;
        Shader.SetGlobalVector(ShaderGlobalIds.SunParams, -Sun.transform.forward);
        // The isolated floor represents a small surface patch with a constant local up direction.
        Shader.SetGlobalVector(ShaderGlobalIds.PlanetCenter, new Vector4(0f, -10000f, 0f, 0f));
        Shader.SetGlobalVector(ShaderGlobalIds.CloudShadowParams, Vector4.zero);
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(ShaderGlobalIds.SunParams, _sun);
        Shader.SetGlobalVector(ShaderGlobalIds.PlanetCenter, _center);
        Shader.SetGlobalVector(ShaderGlobalIds.CloudShadowParams, _cloudShadow);
    }
}
