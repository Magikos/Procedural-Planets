using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// Renders volumetric clouds as a fullscreen ray march pass.
/// Runs after opaque geometry (so depth buffer occludes clouds behind terrain)
/// and after the atmosphere pass so terrain-depth fog does not wash out cloud bodies.
/// </summary>
[DisallowMultipleRendererFeature("CloudRenderFeature")]
public class CloudRenderFeature : ScriptableRendererFeature
{
    CloudRenderPass _pass;
    GodRayStreakRenderPass _godRayPass;
    Material _material;
    Material _godRayMaterial;
    ICloudRuntime _cachedController;
    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int _debugSuppressWeatherPassesId = Shader.PropertyToID(ShaderGlobalIds.DebugSuppressWeatherPasses);
    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _atmosphereRadiusId = Shader.PropertyToID(ShaderGlobalIds.AtmosphereRadius);
    static readonly Plane[] _frustumPlanes = new Plane[6];

    public override void Create()
    {
        _pass = new CloudRenderPass();
        _godRayPass = new GodRayStreakRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camType = renderingData.cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        if (Shader.GetGlobalFloat(_waterFocusModeId) > 0.5f)
            return;

        if (Shader.GetGlobalFloat(_debugSuppressWeatherPassesId) > 0.5f)
            return;

        int oceanDebugMode = Shader.GetGlobalInt(_oceanDebugModeId);
        if (DebugModeConstants.SuppressesWeatherPasses(oceanDebugMode)
            || !DebugModeConstants.PerformanceWeatherIncludesClouds(oceanDebugMode))
            return;

        if (!TryGetLiveController())
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Clouds");
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
        }

        if (!IsPlanetInFrustum(renderingData.cameraData.camera))
            return;

        _pass.Setup(_material);
        renderer.EnqueuePass(_pass);

        // God-ray streaks consume the cloud pass's output (cloud opacity in alpha), so they
        // run immediately after it — occluded by actual cloud gaps, not just terrain depth.
        if (_godRayMaterial == null)
        {
            var godRayShader = Shader.Find("Hidden/GodRayStreaks");
            if (godRayShader != null)
                _godRayMaterial = CoreUtils.CreateEngineMaterial(godRayShader);
        }
        if (_godRayMaterial != null)
        {
            _godRayPass.Setup(_godRayMaterial);
            renderer.EnqueuePass(_godRayPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        CoreUtils.Destroy(_godRayMaterial);
        _material = null;
        _godRayMaterial = null;
        _cachedController = null;
    }

    bool TryGetLiveController()
    {
        if (!ServiceLocator.IsAlive(_cachedController))
            _cachedController = null;
        if (_cachedController == null)
            ServiceLocator.TryGet(out _cachedController);
        if (ServiceLocator.IsAlive(_cachedController))
            return true;

        _cachedController = null;
        return false;
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

public class CloudRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly MaterialPropertyBlock _propertyBlock = new();

    Material _material;

    public CloudRenderPass()
    {
        // Atmosphere also uses BeforeRenderingPostProcessing. Run just after it; clouds do
        // not write depth, so atmosphere would otherwise fog them using terrain depth.
        renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing + 1);
        ConfigureInput(ScriptableRenderPassInput.Depth);
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
        destinationDesc.name = "CameraColor-Clouds";
        destinationDesc.clearBuffer = false;
        // The HDR color buffer is R11G11B10 (no alpha) at this project's precision setting.
        // The cloud pass stashes its opacity in alpha for the god-ray streak pass to read, so
        // the handoff texture needs an alpha channel.
        destinationDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
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

public class GodRayStreakRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly MaterialPropertyBlock _propertyBlock = new();

    Material _material;

    public GodRayStreakRenderPass()
    {
        // Immediately after the cloud pass, so cloud opacity (alpha) is available as an
        // occluder, and still before post-processing so the streaks bloom.
        renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing + 2);
        ConfigureInput(ScriptableRenderPassInput.Depth);
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
        destinationDesc.name = "CameraColor-GodRayStreaks";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("GodRayStreakEffect", out var passData))
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
