using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders distant storm precipitation as a depth-aware fullscreen volume pass.
/// </summary>
[DisallowMultipleRendererFeature("PrecipitationRenderFeature")]
public class PrecipitationRenderFeature : ScriptableRendererFeature
{
    PrecipitationRenderPass _pass;
    Material _material;
    PrecipitationController _cachedController;

    public override void Create()
    {
        _pass = new PrecipitationRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (_cachedController == null || !_cachedController.isActiveAndEnabled)
            _cachedController = Object.FindAnyObjectByType<PrecipitationController>();
        if (_cachedController == null || !_cachedController.IsRenderingEnabled)
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Precipitation");
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
        }

        _pass.Setup(_material, _cachedController, renderingData.cameraData.camera);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}

public class PrecipitationRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static MaterialPropertyBlock _propertyBlock;

    Material _material;
    int _localParticleCount;
    bool _drawLocalParticles;

    public PrecipitationRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public void Setup(Material material, PrecipitationController controller, Camera camera)
    {
        _material = material;
        _drawLocalParticles = controller != null && controller.ShouldRenderLocalParticles(camera);
        _localParticleCount = _drawLocalParticles ? Mathf.Max(0, controller.LocalParticleCount) : 0;
    }

    private class PassData
    {
        internal Material material;
        internal TextureHandle source;
        internal bool drawLocalParticles;
        internal int localParticleCount;
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
        destinationDesc.name = "CameraColor-Precipitation";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("PrecipitationEffect", out var passData))
        {
            passData.material = _material;
            passData.source = source;
            passData.drawLocalParticles = _drawLocalParticles;
            passData.localParticleCount = _localParticleCount;

            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

            if (resourceData.cameraDepthTexture.IsValid())
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                _propertyBlock.Clear();
                _propertyBlock.SetTexture(_sourceId, (RTHandle)data.source);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 3, 1, _propertyBlock);

                if (data.drawLocalParticles && data.localParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 1,
                        MeshTopology.Lines, 2, data.localParticleCount, _propertyBlock);
                }
            });
        }

        resourceData.cameraColor = destination;
    }
}
