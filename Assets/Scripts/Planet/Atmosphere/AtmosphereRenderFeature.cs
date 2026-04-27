using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that injects the screen-space atmospheric scattering pass.
///
/// Setup:
///   1. Open the URP Renderer Asset (e.g. Assets/Settings/PC_RPAsset).
///   2. Click "Add Renderer Feature" → "Atmosphere Render Feature".
///   3. Add an AtmosphereController component to a GameObject in your scene
///      and assign the OpticalDepthCompute shader and CelestialManager references.
///
/// The pass is automatically skipped when no AtmosphereController is active in the scene,
/// or when the planet radius has not yet been set (i.e., before first planet generation).
/// </summary>
[DisallowMultipleRendererFeature("AtmosphereRenderFeature")]
public class AtmosphereRenderFeature : ScriptableRendererFeature
{
    AtmosphereRenderPass _pass;
    Material _material;

    public override void Create()
    {
        _pass = new AtmosphereRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Only render during gameplay (not in edit mode) and only in game/scene cameras
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        // Require an active AtmosphereController in the scene
        var controller = Object.FindAnyObjectByType<AtmosphereController>();
        if (controller == null || !controller.isActiveAndEnabled)
            return;

        // Lazily create the atmosphere material
        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Atmosphere");
            if (shader == null)
            {
                Debug.LogWarning("AtmosphereRenderFeature: could not find shader 'Hidden/Atmosphere'. " +
                                 "Make sure Assets/Graphics/Shaders/Atmosphere.shader is present.");
                return;
            }

            _material = CoreUtils.CreateEngineMaterial(shader);
            // Use the directional-sun variant — CelestialManager exposes a direction, not a position
            _material.EnableKeyword("DIRECTIONAL_SUN");
        }

        _pass.Setup(_material);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}
