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
    PrecipitationController _cachedController;
    // Throttle re-scans to 1 Hz when controller is absent (URP render features outlive scenes,
    // so a permanent null-cache would break scene reloads).
    float _nextControllerScanTime;
    static readonly int _waterFocusModeId = Shader.PropertyToID("_WaterFocusMode");
    static readonly int _oceanDebugModeId = Shader.PropertyToID("_OceanDebugMode");
    static readonly int _planetCenterId = Shader.PropertyToID("_PlanetCenter");
    static readonly int _atmosphereRadiusId = Shader.PropertyToID("_AtmosphereRadius");
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

        if (DebugModeConstants.SuppressesWeatherPasses(Shader.GetGlobalInt(_oceanDebugModeId)))
            return;

        if (!IsPlanetInFrustum(renderingData.cameraData.camera))
            return;

        if (_cachedController == null && Time.unscaledTime >= _nextControllerScanTime)
        {
            _cachedController = Object.FindAnyObjectByType<PrecipitationController>();
            if (_cachedController == null)
                _nextControllerScanTime = Time.unscaledTime + 1f;
        }
        if (_cachedController == null || !_cachedController.isActiveAndEnabled || !_cachedController.IsRenderingEnabled)
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
    int _rainParticleCount;
    int _snowParticleCount;
    int _distantRainParticleCount;
    bool _drawLocalParticles;
    IRainParticleRenderer _rainParticles;
    int _rainParticlesDrawCount;
    Material _rainParticlesMaterial;

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
        PrecipitationController controller,
        Camera camera)
    {
        _material = material;
        _weatherParticleMaterial = weatherParticleMaterial;
        _drawLocalParticles = controller != null && controller.ShouldRenderLocalParticles(camera);
        _dustParticleCount = _drawLocalParticles ? Mathf.Max(0, controller.DustParticleCount) : 0;
        // Close-rain and distant-rain via the procedural WeatherParticles shader are
        // superseded by the world-anchored compute-buffer rain (IRainParticleRenderer).
        // Force their draw counts to zero so we don't double-render.
        _rainParticleCount = 0;
        _snowParticleCount = _drawLocalParticles ? Mathf.Max(0, controller.SnowParticleCount) : 0;
        _distantRainParticleCount = 0;

        _rainParticles = ServiceLocator.Get<IRainParticleRenderer>();
        if (_rainParticles != null && _rainParticles.IsReadyToDraw && _drawLocalParticles)
        {
            _rainParticlesDrawCount = _rainParticles.ParticleCount;
            _rainParticlesMaterial = _rainParticles.Material;
        }
        else
        {
            _rainParticlesDrawCount = 0;
            _rainParticlesMaterial = null;
        }
    }

    private class PassData
    {
        internal Material material;
        internal Material weatherParticleMaterial;
        internal TextureHandle source;
        internal bool drawLocalParticles;
        internal int dustParticleCount;
        internal int rainParticleCount;
        internal int snowParticleCount;
        internal int distantRainParticleCount;
        internal IRainParticleRenderer rainParticles;
        internal int rainParticlesDrawCount;
        internal Material rainParticlesMaterial;
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
            passData.rainParticleCount = _rainParticleCount;
            passData.snowParticleCount = _snowParticleCount;
            passData.distantRainParticleCount = _distantRainParticleCount;
            passData.rainParticles = _rainParticles;
            passData.rainParticlesDrawCount = _rainParticlesDrawCount;
            passData.rainParticlesMaterial = _rainParticlesMaterial;

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
                if (data.rainParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.weatherParticleMaterial, 1,
                        MeshTopology.Triangles, 18, data.rainParticleCount, _propertyBlock);
                }
                if (data.snowParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.weatherParticleMaterial, 2,
                        MeshTopology.Triangles, 18, data.snowParticleCount, _propertyBlock);
                }
                if (data.distantRainParticleCount > 0)
                {
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.weatherParticleMaterial, 3,
                        MeshTopology.Triangles, 18, data.distantRainParticleCount, _propertyBlock);
                }

                // Rain is drawn by the separate RainParticlesAfterPostPass below,
                // which runs AfterRenderingPostProcessing so atmospheric scattering
                // doesn't bleed over the drops. No rain draw here.
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
