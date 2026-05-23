using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Screen-space atmospheric scattering render pass.
/// Reads the current camera colour, applies the Hidden/Atmosphere shader,
/// and writes the result to a new colour texture which becomes the active camera colour.
///
/// Uses AddRasterRenderPass + DrawProcedural (SV_VertexID fullscreen triangle),
/// matching the URP 17 FullScreenPassRendererFeature pattern. This ensures correct
/// camera matrices, depth texture access, and intermediate RT allocation for both
/// Game view and Scene view.
/// </summary>
public class AtmosphereRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly int _waterInterfaceTextureId = Shader.PropertyToID("_WaterInterfaceTexture");
    static MaterialPropertyBlock _propertyBlock;

    Material _material;
    bool _useWaterInterface;

    public AtmosphereRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        // Request depth so URP runs CopyDepthPass and sets _CameraDepthTexture globally.
        // We read camera colour directly from resourceData.cameraColor, so no Color copy needed.
        ConfigureInput(ScriptableRenderPassInput.Depth);
        // Force an intermediate RT so source != backbuffer (required for both Game and Scene views).
        requiresIntermediateTexture = true;

        _propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>Call from the render feature each frame before enqueueing the pass.</summary>
    public void Setup(Material material, bool useWaterInterface)
    {
        _material = material;
        _useWaterInterface = useWaterInterface;
    }

    private class PassData
    {
        internal Material material;
        internal TextureHandle source;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        // Skip preview and reflection cameras.
        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        // Source is the camera colour (requiresIntermediateTexture guarantees it is not the backbuffer).
        TextureHandle source = resourceData.cameraColor;

        // Destination: same format/size as source.
        var destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = "CameraColor-Atmosphere";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("AtmosphereEffect", out var passData))
        {
            passData.material = _material;
            passData.source = source;

            // Declare texture reads/writes for the RenderGraph dependency system.
            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

            // Depth is read via the _CameraDepthTexture global set by CopyDepthPass.
            // Declare cameraDepthTexture (the copy) so RenderGraph orders us after CopyDepthPass.
            if (resourceData.cameraDepthTexture.IsValid())
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            if (_useWaterInterface)
                builder.UseGlobalTexture(_waterInterfaceTextureId);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                _propertyBlock.Clear();
                // Bind the source colour (implicit RTHandle cast is valid inside SetRenderFunc).
                _propertyBlock.SetTexture(_sourceId, (RTHandle)data.source);

                // Fullscreen triangle via SV_VertexID (3 vertices, no vertex buffer needed).
                ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 3, 1, _propertyBlock);
            });
        }

        // Hand the composited texture to subsequent passes as the active camera colour.
        resourceData.cameraColor = destination;
    }
}
