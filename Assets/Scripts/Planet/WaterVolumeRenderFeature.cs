using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[DisallowMultipleRendererFeature("WaterVolumeRenderFeature")]
public sealed class WaterVolumeRenderFeature : ScriptableRendererFeature
{
    static readonly int _waterVolumeEnabledId = Shader.PropertyToID("_WaterVolumeEnabled");
    static readonly int _shallowColorId = Shader.PropertyToID("_ShallowColor");
    static readonly int _deepColorId = Shader.PropertyToID("_DeepColor");
    static readonly int _shallowDepthId = Shader.PropertyToID("_ShallowDepth");
    static readonly int _deepDepthId = Shader.PropertyToID("_DeepDepth");
    static readonly int _shoreFoamSoftnessId = Shader.PropertyToID("_ShoreFoamSoftness");
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");

    WaterVolumeRenderPass _pass;
    Material _prepassMaterial;
    Material _volumeMaterial;
    MeshRenderer _cachedRenderer;
    MeshFilter _cachedFilter;

    public override void Create()
    {
        _pass = new WaterVolumeRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            return;

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

        CopyWaterMaterialSettings(meshRenderer.sharedMaterial, _volumeMaterial);
        Shader.SetGlobalFloat(_waterVolumeEnabledId, 1f);

        _pass.Setup(_prepassMaterial, _volumeMaterial, mesh, meshFilter.transform.localToWorldMatrix);
        renderer.EnqueuePass(_pass);
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
    }
}

public sealed class WaterVolumeRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly int _waterVolumeDataId = Shader.PropertyToID("_WaterVolumeData");
    static MaterialPropertyBlock _propertyBlock;

    Material _prepassMaterial;
    Material _volumeMaterial;
    Mesh _mesh;
    Matrix4x4 _localToWorld;

    public WaterVolumeRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public void Setup(Material prepassMaterial, Material volumeMaterial, Mesh mesh, Matrix4x4 localToWorld)
    {
        _prepassMaterial = prepassMaterial;
        _volumeMaterial = volumeMaterial;
        _mesh = mesh;
        _localToWorld = localToWorld;
    }

    sealed class PrepassData
    {
        internal Material material;
        internal Mesh mesh;
        internal Matrix4x4 localToWorld;
    }

    sealed class CompositeData
    {
        internal Material material;
        internal TextureHandle source;
        internal TextureHandle waterData;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_prepassMaterial == null || _volumeMaterial == null || _mesh == null)
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

            builder.SetRenderAttachment(waterData, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PrepassData data, RasterGraphContext ctx) =>
            {
                ctx.cmd.DrawMesh(data.mesh, data.localToWorld, data.material, 0, 0);
            });
        }

        TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = "CameraColor-WaterVolume";
        destinationDesc.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("WaterVolumeComposite", out var passData))
        {
            passData.material = _volumeMaterial;
            passData.source = source;
            passData.waterData = waterData;

            builder.UseTexture(source, AccessFlags.Read);
            builder.UseTexture(waterData, AccessFlags.Read);
            builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (CompositeData data, RasterGraphContext ctx) =>
            {
                _propertyBlock.Clear();
                _propertyBlock.SetTexture(_sourceId, (RTHandle)data.source);
                _propertyBlock.SetTexture(_waterVolumeDataId, (RTHandle)data.waterData);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 3, 1, _propertyBlock);
            });
        }

        resourceData.cameraColor = destination;
    }
}
