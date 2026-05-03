using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Renders procedural stars as a fullscreen pass before opaque geometry.
/// Stars are drawn right after the buffer clear, so all geometry (planet, trees,
/// moon, buildings, clouds) naturally draws over them via the depth buffer.
/// The atmosphere post-process then dims them during daytime via scattering.
/// </summary>
[DisallowMultipleRendererFeature("StarRenderFeature")]
public class StarRenderFeature : ScriptableRendererFeature
{
    StarRenderPass _pass;
    Material _material;

    public override void Create()
    {
        _pass = new StarRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Stars");
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
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

public class StarRenderPass : ScriptableRenderPass
{
    Material _material;

    public StarRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        requiresIntermediateTexture = true;
    }

    public void Setup(Material material) => _material = material;

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

        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        TextureHandle source = resourceData.cameraColor;

        var destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = "CameraColor-Stars";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("StarBackground", out var passData))
        {
            passData.material = _material;
            passData.source = source;

            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 3, 1);
            });
        }

        resourceData.cameraColor = destination;
    }
}
