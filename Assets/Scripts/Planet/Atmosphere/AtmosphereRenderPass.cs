using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Screen-space atmospheric scattering render pass.
/// Reads the current camera colour, applies the Hidden/Atmosphere blit shader,
/// and writes the result back to the camera colour target.
///
/// Uses the legacy Execute() compatibility path so the atmosphere shader can
/// use its own vertex shader (required for correct per-pixel view-vector reconstruction).
/// </summary>
public class AtmosphereRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly int _tempId   = Shader.PropertyToID("_TempAtmosphere");

    Material _material;

    public AtmosphereRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        // Tell URP we need to read camera colour — ensures an intermediate RT is allocated.
        ConfigureInput(ScriptableRenderPassInput.Color);
    }

    /// <summary>Call from the render feature each frame before enqueueing the pass.</summary>
    public void Setup(Material material)
    {
        _material = material;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_material == null) return;

        ref var cameraData = ref renderingData.cameraData;

        // Skip preview and reflection cameras
        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("AtmosphereEffect");

        RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;

        RTHandle colorHandle = cameraData.renderer.cameraColorTargetHandle;

        // 1. Copy camera colour to a temporary RT so we can read it as _Source
        cmd.GetTemporaryRT(_tempId, desc, FilterMode.Bilinear);
        cmd.Blit(colorHandle, new RenderTargetIdentifier(_tempId));

        // 2. Bind temp as _Source (the atmosphere shader reads from _Source, not _MainTex)
        cmd.SetGlobalTexture(_sourceId, new RenderTargetIdentifier(_tempId));

        // 3. Blit through the atmosphere material back to camera colour
        cmd.Blit(new RenderTargetIdentifier(_tempId), colorHandle, _material);

        cmd.ReleaseTemporaryRT(_tempId);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
