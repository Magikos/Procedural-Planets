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
    static readonly int _freezingEnabledId = Shader.PropertyToID("_FreezingEnabled");
    static readonly int _lakeFreezeStartId = Shader.PropertyToID("_LakeFreezeStart");
    static readonly int _lakeFreezeCompleteId = Shader.PropertyToID("_LakeFreezeComplete");
    static readonly int _oceanFreezeStartId = Shader.PropertyToID("_OceanFreezeStart");
    static readonly int _oceanFreezeCompleteId = Shader.PropertyToID("_OceanFreezeComplete");
    static readonly int _refractionStrengthId = Shader.PropertyToID("_RefractionStrength");
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int _causticIntensityId = Shader.PropertyToID("_CausticIntensity");
    static readonly int _causticDepthId = Shader.PropertyToID("_CausticDepth");
    static readonly int _causticContrastId = Shader.PropertyToID("_CausticContrast");
    static readonly int _causticPrismStrengthId = Shader.PropertyToID("_CausticPrismStrength");
    const float RefractionStrength = 0.30f;
    const float CausticIntensity = 0.42f;
    const float CausticDepth = 115f;
    const float CausticContrast = 1.35f;
    const float CausticPrismStrength = 0.46f;

    WaterVolumePrepassRenderPass _prepassPass;
    WaterVolumeCompositeRenderPass _compositePass;
    Material _prepassMaterial;
    Material _volumeMaterial;
    MeshRenderer _cachedRenderer;
    MeshFilter _cachedFilter;
    MeshFilter _cachedVolumeLipFilter;

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

        if (!TryFindWater(out MeshFilter meshFilter, out MeshRenderer meshRenderer, out MeshFilter volumeLipFilter))
        {
            Shader.SetGlobalFloat(_waterVolumeEnabledId, 0f);
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Mesh volumeLipMesh = volumeLipFilter != null ? volumeLipFilter.sharedMesh : null;
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

        Mesh renderableVolumeLipMesh = IsRenderableMesh(volumeLipMesh) ? volumeLipMesh : null;
        bool drawRelaxedVolumeLip = renderableVolumeLipMesh != null && IsCameraInsideWaterMesh(camera, meshFilter, mesh);
        bool drawVolumeLipSceneDebug = renderableVolumeLipMesh != null && oceanDebugMode == DebugModeConstants.VolumeLipScenePink;
        _prepassPass.Setup(
            _prepassMaterial,
            mesh,
            meshFilter.transform.localToWorldMatrix,
            renderableVolumeLipMesh,
            volumeLipFilter != null ? volumeLipFilter.transform.localToWorldMatrix : Matrix4x4.identity,
            drawRelaxedVolumeLip);
        _compositePass.Setup(
            _volumeMaterial,
            _prepassMaterial,
            renderableVolumeLipMesh,
            volumeLipFilter != null ? volumeLipFilter.transform.localToWorldMatrix : Matrix4x4.identity,
            drawVolumeLipSceneDebug);
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

    bool TryFindWater(out MeshFilter meshFilter, out MeshRenderer meshRenderer, out MeshFilter volumeLipFilter)
    {
        if (_cachedRenderer != null && IsRendererActive(_cachedRenderer) && _cachedFilter != null)
        {
            if (_cachedVolumeLipFilter == null)
                _cachedVolumeLipFilter = FindVolumeLipFilter(_cachedRenderer.transform);

            meshFilter = _cachedFilter;
            meshRenderer = _cachedRenderer;
            volumeLipFilter = _cachedVolumeLipFilter;
            return true;
        }

        GameObject water = GameObject.Find("Water");
        if (water == null)
        {
            meshFilter = null;
            meshRenderer = null;
            volumeLipFilter = null;
            return false;
        }

        _cachedFilter = water.GetComponent<MeshFilter>();
        _cachedRenderer = water.GetComponent<MeshRenderer>();
        _cachedVolumeLipFilter = FindVolumeLipFilter(water.transform);
        meshFilter = _cachedFilter;
        meshRenderer = _cachedRenderer;
        volumeLipFilter = _cachedVolumeLipFilter;
        return meshFilter != null && meshRenderer != null && IsRendererActive(meshRenderer);
    }

    static MeshFilter FindVolumeLipFilter(Transform waterTransform)
    {
        if (waterTransform == null)
            return null;

        Transform lip = waterTransform.Find("WaterVolumeLip");
        return lip != null && lip.gameObject.activeInHierarchy ? lip.GetComponent<MeshFilter>() : null;
    }

    static bool IsRendererActive(Renderer renderer)
    {
        return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
    }

    static bool IsRenderableMesh(Mesh mesh)
    {
        return mesh != null && mesh.vertexCount > 0 && mesh.subMeshCount > 0 && mesh.GetIndexCount(0) > 0;
    }

    static bool IsCameraInsideWaterMesh(Camera camera, MeshFilter waterFilter, Mesh waterMesh)
    {
        if (camera == null || waterFilter == null || waterMesh == null)
            return false;

        Bounds bounds = waterMesh.bounds;
        float localRadius = EstimateLocalRadius(bounds);
        if (localRadius <= 0f)
            return false;

        Vector3 scale = waterFilter.transform.lossyScale;
        float worldScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        if (worldScale <= 0f)
            return false;

        Vector3 center = waterFilter.transform.TransformPoint(Vector3.zero);
        float cameraRadius = Vector3.Distance(camera.transform.position, center);
        return cameraRadius <= localRadius * worldScale + 0.5f;
    }

    static float EstimateLocalRadius(Bounds bounds)
    {
        float x = Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x));
        float y = Mathf.Max(Mathf.Abs(bounds.min.y), Mathf.Abs(bounds.max.y));
        float z = Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
        return Mathf.Max(x, Mathf.Max(y, z));
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
        CopyFloatIfPresent(source, destination, _freezingEnabledId);
        CopyFloatIfPresent(source, destination, _lakeFreezeStartId);
        CopyFloatIfPresent(source, destination, _lakeFreezeCompleteId);
        CopyFloatIfPresent(source, destination, _oceanFreezeStartId);
        CopyFloatIfPresent(source, destination, _oceanFreezeCompleteId);
        if (destination.HasProperty(_refractionStrengthId))
            destination.SetFloat(_refractionStrengthId, RefractionStrength);
        if (destination.HasProperty(_causticIntensityId))
            destination.SetFloat(_causticIntensityId, CausticIntensity);
        if (destination.HasProperty(_causticDepthId))
            destination.SetFloat(_causticDepthId, CausticDepth);
        if (destination.HasProperty(_causticContrastId))
            destination.SetFloat(_causticContrastId, CausticContrast);
        if (destination.HasProperty(_causticPrismStrengthId))
            destination.SetFloat(_causticPrismStrengthId, CausticPrismStrength);
    }

    static void CopyFloatIfPresent(Material source, Material destination, int propertyId)
    {
        if (source.HasProperty(propertyId) && destination.HasProperty(propertyId))
            destination.SetFloat(propertyId, source.GetFloat(propertyId));
    }
}

public sealed class WaterVolumePrepassRenderPass : ScriptableRenderPass
{
    static readonly int _waterVolumeDataId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeData);
    static readonly int _waterInterfaceTextureId = Shader.PropertyToID(ShaderGlobalIds.WaterInterfaceTexture);
    const int WaterPrepassPass = 0;
    const int WaterVolumeLipPrepassPass = 1;

    Material _prepassMaterial;
    Mesh _mesh;
    Matrix4x4 _localToWorld;
    Mesh _volumeLipMesh;
    Matrix4x4 _volumeLipLocalToWorld;
    bool _drawRelaxedVolumeLip;

    public WaterVolumePrepassRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public void Setup(
        Material prepassMaterial,
        Mesh mesh,
        Matrix4x4 localToWorld,
        Mesh volumeLipMesh,
        Matrix4x4 volumeLipLocalToWorld,
        bool drawRelaxedVolumeLip)
    {
        _prepassMaterial = prepassMaterial;
        _mesh = mesh;
        _localToWorld = localToWorld;
        _volumeLipMesh = volumeLipMesh;
        _volumeLipLocalToWorld = volumeLipLocalToWorld;
        _drawRelaxedVolumeLip = drawRelaxedVolumeLip;
    }

    sealed class PrepassData
    {
        internal Material material;
        internal Mesh mesh;
        internal Matrix4x4 localToWorld;
        internal Mesh volumeLipMesh;
        internal Matrix4x4 volumeLipLocalToWorld;
        internal bool drawRelaxedVolumeLip;
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
            passData.volumeLipMesh = _volumeLipMesh;
            passData.volumeLipLocalToWorld = _volumeLipLocalToWorld;
            passData.drawRelaxedVolumeLip = _drawRelaxedVolumeLip;

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
                if (data.drawRelaxedVolumeLip && data.volumeLipMesh != null)
                    ctx.cmd.DrawMesh(data.volumeLipMesh, data.volumeLipLocalToWorld, data.material, 0, WaterVolumeLipPrepassPass);
            });
        }
    }
}

public sealed class WaterVolumeCompositeRenderPass : ScriptableRenderPass
{
    static readonly int _sourceId = Shader.PropertyToID("_Source");
    static readonly int _waterVolumeDataId = Shader.PropertyToID(ShaderGlobalIds.WaterVolumeData);
    const int WaterVolumeLipSceneDebugPass = 2;
    static MaterialPropertyBlock _propertyBlock;

    Material _volumeMaterial;
    Material _prepassMaterial;
    Mesh _volumeLipMesh;
    Matrix4x4 _volumeLipLocalToWorld;
    bool _drawVolumeLipSceneDebug;

    public WaterVolumeCompositeRenderPass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        ConfigureInput(ScriptableRenderPassInput.Depth);
        requiresIntermediateTexture = true;
        _propertyBlock = new MaterialPropertyBlock();
    }

    public void Setup(
        Material volumeMaterial,
        Material prepassMaterial,
        Mesh volumeLipMesh,
        Matrix4x4 volumeLipLocalToWorld,
        bool drawVolumeLipSceneDebug)
    {
        _volumeMaterial = volumeMaterial;
        _prepassMaterial = prepassMaterial;
        _volumeLipMesh = volumeLipMesh;
        _volumeLipLocalToWorld = volumeLipLocalToWorld;
        _drawVolumeLipSceneDebug = drawVolumeLipSceneDebug;
    }

    sealed class CompositeData
    {
        internal Material material;
        internal TextureHandle source;
    }

    sealed class LipSceneDebugData
    {
        internal Material material;
        internal Mesh volumeLipMesh;
        internal Matrix4x4 volumeLipLocalToWorld;
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

        if (_drawVolumeLipSceneDebug && _volumeLipMesh != null)
        {
            using (var builder = renderGraph.AddRasterRenderPass<LipSceneDebugData>("WaterVolumeLipSceneDebug", out var passData))
            {
                passData.material = _prepassMaterial;
                passData.volumeLipMesh = _volumeLipMesh;
                passData.volumeLipLocalToWorld = _volumeLipLocalToWorld;

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                if (resourceData.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (LipSceneDebugData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawMesh(data.volumeLipMesh, data.volumeLipLocalToWorld, data.material, 0, WaterVolumeLipSceneDebugPass);
                });
            }
        }

        resourceData.cameraColor = destination;
    }
}
