using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[DisallowMultipleRendererFeature("WaterVolumeRenderFeature")]
public sealed class WaterVolumeRenderFeature : ScriptableRendererFeature
{
    static readonly int _waterVolumeEnabledId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeEnabled);
    static readonly int _shallowColorId = Shader.PropertyToID("_ShallowColor");
    static readonly int _deepColorId = Shader.PropertyToID("_DeepColor");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");
    static readonly int _refractionStrengthId = Shader.PropertyToID("_RefractionStrength");
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int _causticIntensityId = Shader.PropertyToID("_CausticIntensity");
    static readonly int _causticDepthId = Shader.PropertyToID("_CausticDepth");
    static readonly int _causticContrastId = Shader.PropertyToID("_CausticContrast");
    static readonly int _causticPrismStrengthId = Shader.PropertyToID("_CausticPrismStrength");

    WaterVolumePrepassRenderPass _prepassPass;
    WaterVolumeCompositeRenderPass _compositePass;
    Material _prepassMaterial;
    Material _volumeMaterial;
    MeshRenderer _cachedRenderer;
    MeshFilter _cachedFilter;

    public override void Create()
    {
        _prepassPass = new WaterVolumePrepassRenderPass();
        _compositePass = new WaterVolumeCompositeRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            return;

        int oceanDebugMode = Shader.GetGlobalInt(_oceanDebugModeId);
        if (oceanDebugMode >= DebugModeConstants.TerrainCoastMask
            && oceanDebugMode <= DebugModeConstants.TerrainMixedAlbedo)
        {
            Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
            return;
        }

        if (!TryFindWater(out MeshFilter meshFilter, out MeshRenderer meshRenderer))
        {
            Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        if (mesh == null || mesh.vertexCount == 0)
        {
            Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
            return;
        }

        if (!EnsureMaterials())
        {
            Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
            return;
        }

        CopyWaterMaterialSettings(meshRenderer.sharedMaterial, _prepassMaterial);
        CopyWaterMaterialSettings(meshRenderer.sharedMaterial, _volumeMaterial);
        Shader.SetGlobalFloat(_waterVolumeEnabledId, 1f);

        _prepassPass.Setup(_prepassMaterial, mesh, meshFilter.transform.localToWorldMatrix);
        _compositePass.Setup(_volumeMaterial);
        renderer.EnqueuePass(_prepassPass);
        renderer.EnqueuePass(_compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
        CoreUtils.Destroy(_prepassMaterial);
        CoreUtils.Destroy(_volumeMaterial);
        _prepassMaterial = null;
        _volumeMaterial = null;
    }

    bool EnsureMaterials()
    {
        if (_prepassMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/WaterVolumePrepass");
            if (shader == null)
                return false;

            _prepassMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        if (_volumeMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/WaterVolume");
            if (shader == null)
                return false;

            _volumeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }

        return true;
    }

    bool TryFindWater(out MeshFilter meshFilter, out MeshRenderer meshRenderer)
    {
        if (_cachedRenderer != null && IsRendererActive(_cachedRenderer) && _cachedFilter != null)
        {
            meshFilter = _cachedFilter;
            meshRenderer = _cachedRenderer;
            return true;
        }

        GameObject water = GameObject.Find("Water");
        if (water == null)
        {
            meshFilter = null;
            meshRenderer = null;
            return false;
        }

        _cachedFilter = water.GetComponent<MeshFilter>();
        _cachedRenderer = water.GetComponent<MeshRenderer>();
        meshFilter = _cachedFilter;
        meshRenderer = _cachedRenderer;
        return meshFilter != null && meshRenderer != null && IsRendererActive(meshRenderer);
    }

    static bool IsRendererActive(Renderer renderer)
    {
        return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
    }

    static void CopyWaterMaterialSettings(Material source, Material destination)
    {
        if (source == null || destination == null)
            return;

        if (source.HasProperty(_shallowColorId))
            destination.SetColor(_shallowColorId, source.GetColor(_shallowColorId));
        if (source.HasProperty(_deepColorId))
            destination.SetColor(_deepColorId, source.GetColor(_deepColorId));
        if (source.HasProperty(_shallowDepthId))
            destination.SetFloat(_shallowDepthId, source.GetFloat(_shallowDepthId));
        if (source.HasProperty(_deepDepthId))
            destination.SetFloat(_deepDepthId, source.GetFloat(_deepDepthId));
        if (source.HasProperty(_shoreFoamSoftnessId))
            destination.SetFloat(_shoreFoamSoftnessId, source.GetFloat(_shoreFoamSoftnessId));
        if (source.HasProperty(_alphaId))
            destination.SetFloat(_alphaId, source.GetFloat(_alphaId));
        // This runs for editor cameras with no active world, where GetSettings would throw, so the
        // volume falls back to the shader's own property defaults until a world is up.
        if (!SettingsProvider.TryGetFrozen(out WaterDto water))
            return;

        if (destination.HasProperty(_refractionStrengthId))
            destination.SetFloat(_refractionStrengthId, water.RefractionStrength);
        if (destination.HasProperty(_causticIntensityId))
            destination.SetFloat(_causticIntensityId, water.CausticIntensity);
        if (destination.HasProperty(_causticDepthId))
            destination.SetFloat(_causticDepthId, water.CausticDepth);
        if (destination.HasProperty(_causticContrastId))
            destination.SetFloat(_causticContrastId, water.CausticContrast);
        if (destination.HasProperty(_causticPrismStrengthId))
            destination.SetFloat(_causticPrismStrengthId, water.CausticPrismStrength);
    }

}

public sealed class WaterVolumePrepassRenderPass : ScriptableRenderPass
{
    static readonly int _waterVolumeDataId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeData);
    static readonly int _waterInterfaceTextureId = Shader.PropertyToID(ShaderGlobalIds.WaterInterfaceTexture);
    const int WaterPrepassPass = 0;

    Material _prepassMaterial;
    Mesh _mesh;
    Matrix4x4 _localToWorld;

    public WaterVolumePrepassRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public void Setup(Material prepassMaterial, Mesh mesh, Matrix4x4 localToWorld)
    {
        _prepassMaterial = prepassMaterial;
        _mesh = mesh;
        _localToWorld = localToWorld;
    }

    sealed class PrepassData
    {
        internal Material material;
        internal Mesh mesh;
        internal Matrix4x4 localToWorld;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_prepassMaterial == null || _mesh == null)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        CameraType cameraType = cameraData.camera.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        if (!resourceData.cameraDepthTexture.IsValid() || !resourceData.activeDepthTexture.IsValid())
            return;

        TextureHandle source = resourceData.cameraColor;

        TextureDesc waterDesc = renderGraph.GetTextureDesc(source);
        waterDesc.name = "WaterVolumeData";
        waterDesc.clearBuffer = true;
        waterDesc.clearColor = Color.clear;
        waterDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
        TextureHandle waterData = renderGraph.CreateTexture(waterDesc);

        using (var builder = renderGraph.AddRasterRenderPass<PrepassData>("WaterVolumePrepass", out var passData))
        {
            passData.material = _prepassMaterial;
            passData.mesh = _mesh;
            passData.localToWorld = _localToWorld;

            builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.SetRenderAttachment(waterData, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(waterData, _waterInterfaceTextureId);
            builder.SetGlobalTextureAfterPass(waterData, _waterVolumeDataId);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PrepassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawMesh(data.mesh, data.localToWorld, data.material, 0, WaterPrepassPass);
            });
        }
    }
}

public sealed class WaterVolumeCompositeRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly int _waterVolumeDataId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeData);
    static MaterialPropertyBlock _propertyBlock;

    Material _volumeMaterial;

    public WaterVolumeCompositeRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public void Setup(Material volumeMaterial)
    {
        _volumeMaterial = volumeMaterial;
    }

    sealed class CompositeData
    {
        internal Material material;
        internal TextureHandle source;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_volumeMaterial == null)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        CameraType cameraType = cameraData.camera.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        if (!resourceData.cameraDepthTexture.IsValid())
            return;

        TextureHandle source = resourceData.cameraColor;

        TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = "CameraColor-WaterVolume";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("WaterVolumeComposite", out var passData))
        {
            passData.material = _volumeMaterial;
            passData.source = source;

            builder.UseTexture(source, AccessFlags.Read);
            builder.UseGlobalTexture(_waterVolumeDataId);
            builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (CompositeData data, RasterGraphContext ctx) =>
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
