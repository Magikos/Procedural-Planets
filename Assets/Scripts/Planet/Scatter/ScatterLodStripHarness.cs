using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Lightweight LOD test harness (no planet / no world services): draws one scatter prototype through
// ScatterLodBatcher — the same LOD draw the planet's ScatterRenderer uses — so mesh-LOD transitions and
// the far-field impostor tier can be built and tuned in a fast-loading scene. Two views: a fixed row at
// increasing distances (all LOD tiers on screen at once) and a single "swap" asset that changes LOD as
// the camera moves toward/away.
public sealed class ScatterLodStripHarness : MonoBehaviour
{
    public ScatterLibrary Library;
    public int PrototypeIndex;
    public float[] RowDistances = { 10f, 50f, 150f, 400f, 900f };
    public Vector3 RowDirection = Vector3.forward;
    public Vector3 SwapAssetPosition = new Vector3(25f, 0f, 6f);

    [Header("Impostor (far-field billboard tier)")]
    public bool DrawImpostor = true;
    public Vector3 ImpostorLightEuler = new Vector3(35f, 150f, 0f);
    public float ImpostorEndDistance = 1400f;

    ScatterPrototypeDto _proto;
    RenderParams[] _partParams;
    ScatterLodBatcher _batcher;
    ScatterLodBatcher.Impostor _impostor;
    readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
    readonly List<Vector3> _positions = new List<Vector3>();

    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
    static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int _cutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
    static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
    static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
    static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");

    void Start() => Build();

    public void Build()
    {
        _proto = null;
        _impostor = default;
        if (Library == null) return;
        ScatterLibraryDto dto = ScatterLibraryDto.From(Library);
        if (PrototypeIndex < 0 || PrototypeIndex >= dto.Prototypes.Length) return;
        ScatterPrototypeDto proto = dto.Prototypes[PrototypeIndex];
        if (!proto.CanRender) return;
        _proto = proto;
        _batcher = new ScatterLodBatcher();

        var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        float meshCull = 0f;
        _partParams = new RenderParams[proto.Parts.Length];
        for (int j = 0; j < proto.Parts.Length; j++)
        {
            ScatterPartDto part = proto.Parts[j];
            if (!part.CanRender) continue;
            if (!part.Material.enableInstancing) part.Material.enableInstancing = true;
            float cull = part.MaxCullDistance;
            meshCull = Mathf.Max(meshCull, cull);
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat(_fadeStartId, cull * 0.85f);
            mpb.SetFloat(_fadeEndId, cull);
            _partParams[j] = new RenderParams(part.Material)
            {
                shadowCastingMode = part.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = part.ReceiveShadows,
                worldBounds = bounds,
                matProps = mpb,
            };
        }

        if (DrawImpostor) BuildImpostor(proto, meshCull, bounds);

        _matrices.Clear();
        _positions.Clear();
        Vector3 dir = RowDirection.sqrMagnitude > 1e-4f ? RowDirection.normalized : Vector3.forward;
        if (RowDistances != null)
            foreach (float d in RowDistances) AddInstance(transform.position + dir * d);
        AddInstance(transform.position + SwapAssetPosition);
    }

    void BuildImpostor(ScatterPrototypeDto proto, float meshCull, Bounds bounds)
    {
        Shader shader = Shader.Find("Scatter/Impostor");
        if (shader == null) return;

        var meshes = new List<Mesh>();
        var materials = new List<Material>();
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (!part.CanRender || part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
            meshes.Add(part.LodMeshes[0]);
            materials.Add(part.Material);
        }
        if (meshes.Count == 0) return;

        ScatterImpostorBaker.Card card = ScatterImpostorBaker.Bake(meshes, materials, ImpostorLightEuler);

        // Cross-fade in over the mesh-LOD's own dither-out band so the two tiers hand off cleanly.
        float fadeInStart = meshCull * 0.85f;
        float fadeInEnd = meshCull;
        float end = Mathf.Max(ImpostorEndDistance, fadeInEnd + 1f);
        var mat = new Material(shader) { enableInstancing = true };
        mat.SetTexture(_baseMapId, card.Texture);
        mat.SetFloat(_cutoffId, 0.3f);
        mat.SetFloat(_fadeInStartId, fadeInStart);
        mat.SetFloat(_fadeInEndId, fadeInEnd);
        mat.SetFloat(_fadeOutStartId, end * 0.95f);
        mat.SetFloat(_fadeOutEndId, end);

        Mesh quad = BuildQuad(card.Width, card.Height);
        var rp = new RenderParams(mat) { worldBounds = bounds };
        _impostor = new ScatterLodBatcher.Impostor(rp, quad, fadeInStart, end);
    }

    static Mesh BuildQuad(float w, float h)
    {
        var m = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-w / 2f, 0f, 0f), new Vector3(w / 2f, 0f, 0f),
                new Vector3(w / 2f, h, 0f), new Vector3(-w / 2f, h, 0f),
            },
            uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        m.RecalculateBounds();
        return m;
    }

    void AddInstance(Vector3 pos)
    {
        _matrices.Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
        _positions.Add(pos);
    }

    void Update()
    {
        if (_batcher == null || _proto == null) return;
        Camera cam = Camera.main;
        if (cam != null)
            _batcher.Draw(_proto, _partParams, _matrices, _positions, cam.transform.position, _impostor);
    }
}
