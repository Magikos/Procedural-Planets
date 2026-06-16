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
    RainParticlesAfterPostPass _rainPass;
    Material _material;
    Material _weatherParticleMaterial;
    IPrecipitationDebugControl _cachedController;
    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int _debugSuppressWeatherPassesId = Shader.PropertyToID(ShaderGlobalIds.DebugSuppressWeatherPasses);
    static readonly int _planetCenterId = Shader.PropertyToID(ShaderGlobalIds.PlanetCenter);
    static readonly int _atmosphereRadiusId = Shader.PropertyToID(ShaderGlobalIds.AtmosphereRadius);
    static readonly Plane[] _frustumPlanes = new Plane[6];

    public override void Create()
    {
        _pass = new PrecipitationRenderPass();
        _rainPass = new RainParticlesAfterPostPass();
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
            || !DebugModeConstants.PerformanceWeatherIncludesPrecipitation(oceanDebugMode))
            return;

        if (!IsPlanetInFrustum(renderingData.cameraData.camera))
            return;

        if (_cachedController == null)
            ServiceLocator.TryGet(out _cachedController);
        if (_cachedController == null || !_cachedController.IsRenderingEnabled)
            return;

        if (_material == null)
        {
            var shader = Shader.Find("Hidden/Precipitation");
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
        }

        if (_weatherParticleMaterial == null)
        {
            var shader = Shader.Find("Hidden/WeatherParticles");
            if (shader == null) return;
            _weatherParticleMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        _pass.Setup(
            _material,
            _weatherParticleMaterial,
            _cachedController,
            renderingData.cameraData.camera);
        renderer.EnqueuePass(_pass);

        // Rain runs as a separate pass AFTER post-processing so atmospheric
        // scattering — which composites colored haze over the scene during the
        // main precipitation pass — has already finished. Without this, rain
        // drops near the horizon get washed out by sunset/sunrise scattering
        // because they were drawn before the atmosphere overlay.
        var rainRenderer = ServiceLocator.Get<IRainParticleRenderer>();
        if (rainRenderer != null && rainRenderer.IsReadyToDraw
            && _cachedController.ShouldRenderLocalParticles(renderingData.cameraData.camera))
        {
            _rainPass.Setup(rainRenderer);
            renderer.EnqueuePass(_rainPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
        CoreUtils.Destroy(_weatherParticleMaterial);
        _weatherParticleMaterial = null;
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

public class PrecipitationRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static MaterialPropertyBlock _propertyBlock;

    Material _material;
    Material _weatherParticleMaterial;
    int _dustParticleCount;
    int _snowParticleCount;
    bool _drawLocalParticles;

    public PrecipitationRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public void Setup(
        Material material,
        Material weatherParticleMaterial,
        IPrecipitationDebugControl controller,
        Camera camera)
    {
        _material = material;
        _weatherParticleMaterial = weatherParticleMaterial;
        _drawLocalParticles = controller != null && controller.ShouldRenderLocalParticles(camera);
        _dustParticleCount = _drawLocalParticles ? Mathf.Max(0, controller.DustParticleCount) : 0;
        _snowParticleCount = _drawLocalParticles ? Mathf.Max(0, controller.SnowParticleCount) : 0;
    }

    private class PassData
    {
        internal Material material;
        internal Material weatherParticleMaterial;
        internal TextureHandle source;
        internal bool drawLocalParticles;
        internal int dustParticleCount;
        internal int snowParticleCount;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null || _weatherParticleMaterial == null) return;

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
            passData.weatherParticleMaterial = _weatherParticleMaterial;
            passData.source = source;
            passData.drawLocalParticles = _drawLocalParticles;
            passData.dustParticleCount = _dustParticleCount;
            passData.snowParticleCount = _snowParticleCount;

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

                if (!data.drawLocalParticles)
                    return;

                if (data.dustParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.weatherParticleMaterial, 0,
                        MeshTopology.Triangles, 18, data.dustParticleCount, _propertyBlock);
                }
                if (data.snowParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.weatherParticleMaterial, 1,
                        MeshTopology.Triangles, 18, data.snowParticleCount, _propertyBlock);
                }

                // Rain draws in RainParticlesAfterPostPass below at
                // AfterRenderingPostProcessing so atmospheric scattering does
                // not wash over the drops.
            });
        }

        resourceData.cameraColor = destination;
    }
}

/// <summary>
/// Draws the world-anchored rain particles AFTER URP's post-processing has
/// finished compositing the scene. The atmosphere render feature runs at
/// BeforeRenderingPostProcessing and composites colored haze (sunset/sunrise
/// scattering) over the camera color. If rain draws before that, the haze
/// gets blended on top of the drops and washes them out. By running this
/// pass at AfterRenderingPostProcessing, the drops are composited LAST,
/// directly on top of the final atmospheric color.
/// </summary>
public sealed class RainParticlesAfterPostPass : ScriptableRenderPass
{
    static MaterialPropertyBlock _propertyBlock;

    IRainParticleRenderer _renderer;
    int _drawCount;
    Material _material;

    public RainParticlesAfterPostPass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        _propertyBlock ??= new MaterialPropertyBlock();
    }

    public void Setup(IRainParticleRenderer renderer)
    {
        _renderer = renderer;
        _drawCount = renderer?.ParticleCount ?? 0;
        _material = renderer?.Material;
    }

    private class PassData
    {
        internal Material material;
        internal int drawCount;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null || _drawCount <= 0) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var camType = cameraData.camera.cameraType;
        if (camType == CameraType.Preview || camType == CameraType.Reflection)
            return;

        TextureHandle target = resourceData.cameraColor;
        if (!target.IsValid()) return;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("RainAfterPost", out var passData))
        {
            passData.material = _material;
            passData.drawCount = _drawCount;

            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            if (resourceData.cameraDepthTexture.IsValid())
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 6, data.drawCount, _propertyBlock);
            });
        }
    }
}
