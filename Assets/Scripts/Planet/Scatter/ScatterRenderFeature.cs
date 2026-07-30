using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Draws the scatter (trees/rocks/bushes) inside URP's opaque phase, into the camera colour+depth, so
/// the scatter lands in _CameraDepthTexture. The scatter used to draw via immediate-mode
/// Graphics.RenderMeshInstanced from Planet.Update, which never reached _CameraDepthTexture — so the
/// atmosphere's depth-based sky mask painted sky over tree canopies that rose above the terrain horizon.
/// Placement/gather still lives in ScatterRenderer/ScatterTileCache; this feature only issues the draws.
/// </summary>
[DisallowMultipleRendererFeature("ScatterRenderFeature")]
public class ScatterRenderFeature : ScriptableRendererFeature
{
    ScatterRenderPass _pass;
    ScatterDepthNormalsPass _depthPass;
    IScatterDrawRuntime _cached;

    public override void Create()
    {
        _pass = new ScatterRenderPass();
        _depthPass = new ScatterDepthNormalsPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (!TryGetRuntime() || !_cached.HasDrawData)
            return;

        // Prepass depth+normals so the scatter is in _CameraDepthTexture / _CameraNormalsTexture (clouds,
        // atmosphere, SSAO all read those). Then the opaque colour pass.
        _depthPass.Setup(_cached);
        renderer.EnqueuePass(_depthPass);
        _pass.Setup(_cached);
        renderer.EnqueuePass(_pass);
    }

    bool TryGetRuntime()
    {
        if (!ServiceLocator.IsAlive(_cached))
            _cached = null;
        if (_cached == null)
            ServiceLocator.TryGet(out _cached);
        return ServiceLocator.IsAlive(_cached);
    }
}

public class ScatterRenderPass : ScriptableRenderPass
{
    IScatterDrawRuntime _runtime;

    public ScatterRenderPass()
    {
        // After scene opaques so scatter depth-tests against terrain; before URP's CopyDepthPass (which
        // the atmosphere triggers) so the scatter's depth is captured into _CameraDepthTexture.
        renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public void Setup(IScatterDrawRuntime runtime) => _runtime = runtime;

    private class PassData
    {
        internal IScatterDrawRuntime runtime;
        internal Vector3 camPos;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_runtime == null || !_runtime.HasDrawData) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("ScatterOpaque", out var passData))
        {
            passData.runtime = _runtime;
            passData.camPos = cameraData.camera.transform.position;

            // Draw into the live camera colour+depth: colour is written (depth-tested), depth is
            // read+written so the scatter both occludes and is occluded correctly and its depth persists
            // for the CopyDepthPass that follows.
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                data.runtime.RecordDraws(ctx.cmd, data.camPos));
        }
    }
}

/// <summary>
/// Appends the scatter to URP's depth-normals prepass (AfterRenderingPrePasses), so the scatter's depth
/// and normals land in _CameraDepthTexture / _CameraNormalsTexture. Without this the scatter (drawn by
/// immediate/opaque-phase means) is absent from that prepass, and clouds/atmosphere/SSAO composite over
/// tree canopies (see-through canopy against sky/clouds). No-ops when no prepass ran this frame.
/// </summary>
public class ScatterDepthNormalsPass : ScriptableRenderPass
{
    IScatterDrawRuntime _runtime;

    public ScatterDepthNormalsPass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
    }

    public void Setup(IScatterDrawRuntime runtime) => _runtime = runtime;

    private class PassData
    {
        internal IScatterDrawRuntime runtime;
        internal Vector3 camPos;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_runtime == null || !_runtime.HasDrawData) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        // Only append when URP actually produced a depth prepass this frame; the depth target the prepass
        // wrote is what _CameraDepthTexture is taken from.
        if (!resourceData.cameraDepthTexture.IsValid())
            return;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("ScatterDepthNormals", out var passData))
        {
            passData.runtime = _runtime;
            passData.camPos = cameraData.camera.transform.position;

            if (resourceData.cameraNormalsTexture.IsValid())
                builder.SetRenderAttachment(resourceData.cameraNormalsTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.cameraDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                data.runtime.RecordDepthNormalsDraws(ctx.cmd, data.camPos));
        }
    }
}
