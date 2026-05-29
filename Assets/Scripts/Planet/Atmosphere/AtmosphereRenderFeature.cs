using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that injects the screen-space atmospheric scattering pass.
/// Debug mode is controlled via AtmosphereSettings.DebugMode (int uniform in shader).
/// </summary>
[DisallowMultipleRendererFeature("AtmosphereRenderFeature")]
public class AtmosphereRenderFeature : ScriptableRendererFeature
{
    static readonly int _waterVolumeEnabledId = Shader.PropertyToID("_WaterVolumeEnabled");
    static readonly int _oceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
    static readonly Plane[] _frustumPlanes = new Plane[6];

    AtmosphereRenderPass _pass;
    Material _material;
    AtmosphereController _cachedController;

    public override void Create()
    {
        _pass = new AtmosphereRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (_cachedController == null)
            _cachedController = Object.FindAnyObjectByType<AtmosphereController>();
        if (_cachedController == null || !_cachedController.isActiveAndEnabled)
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Atmosphere");
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
            _material.EnableKeyword("DIRECTIONAL_SUN");
        }

        int oceanDebugMode = Shader.GetGlobalInt(_oceanDebugModeId);
        bool useWaterInterface = Shader.GetGlobalFloat(_waterVolumeEnabledId) > 0.5f
            && oceanDebugMode != DebugModeConstants.AtmosphereBypass
            && oceanDebugMode != DebugModeConstants.VolumeAfterAtmosphere;

        if (!IsPlanetInFrustum(renderingData.cameraData.camera))
            return;

        _pass.Setup(_material, useWaterInterface);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    static bool IsPlanetInFrustum(Camera camera)
    {
        float atmRadius = Shader.GetGlobalFloat(_atmosphereRadiusId);
        if (atmRadius <= 0f) return true;
        Vector3 center = Shader.GetGlobalVector(_planetCenterId);
        GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
        return GeometryUtility.TestPlanesAABB(_frustumPlanes, new Bounds(center, Vector3.one * atmRadius * 2f));
    }
}
