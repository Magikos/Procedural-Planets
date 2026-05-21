using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Renders volumetric clouds as a fullscreen ray march pass.
/// Runs after opaque geometry (so depth buffer occludes clouds behind terrain)
/// and before the atmosphere post-process (so atmospheric scattering tints clouds).
/// </summary>
[DisallowMultipleRendererFeature("CloudRenderFeature")]
public class CloudRenderFeature : ScriptableRendererFeature
{
    CloudRenderPass _pass;
    Material _material;
    CloudController _cachedController;
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");

    public override void Create()
    {
        _pass = new CloudRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (Shader.GetGlobalFloat(_waterFocusModeId) > 0.5f)
            return;

        if (_cachedController == null || !_cachedController.isActiveAndEnabled)
            _cachedController = Object.FindAnyObjectByType<CloudController>();
        if (_cachedController == null)
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Clouds");
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

public class CloudRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static MaterialPropertyBlock _propertyBlock;

    Material _material;

    public CloudRenderPass()
    {
        // Render before the atmosphere pass so scattering and aerial perspective tint clouds.
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
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
        destinationDesc.name = "CameraColor-Clouds";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("CloudEffect", out var passData))
        {
            passData.material = _material;
            passData.source = source;

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
            });
        }

        resourceData.cameraColor = destination;
    }
}
