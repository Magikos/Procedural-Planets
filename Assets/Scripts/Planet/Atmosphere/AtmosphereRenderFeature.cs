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

        if (_cachedController == null || !_cachedController.isActiveAndEnabled)
            _cachedController = Object.FindAnyObjectByType<AtmosphereController>();
        if (_cachedController == null)
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
            && oceanDebugMode != 40
            && oceanDebugMode != 41;

        _pass.Setup(_material, useWaterInterface);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}
