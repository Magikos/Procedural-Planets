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

    ScatterPrototypeDto _proto;
    RenderParams[] _partParams;
    ScatterLodBatcher _batcher;
    ScatterLodBatcher.Impostor _impostor;
    readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
    readonly List<Vector3> _positions = new List<Vector3>();

    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");

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
        _partParams = new RenderParams[proto.Parts.Length];
        for (int j = 0; j < proto.Parts.Length; j++)
        {
            ScatterPartDto part = proto.Parts[j];
            if (!part.CanRender) continue;
            if (!part.Material.enableInstancing) part.Material.enableInstancing = true;
            float cull = part.MaxCullDistance;
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

        if (DrawImpostor) _impostor = ScatterImpostorFactory.TryBuild(proto, bounds);

        _matrices.Clear();
        _positions.Clear();
        Vector3 dir = RowDirection.sqrMagnitude > 1e-4f ? RowDirection.normalized : Vector3.forward;
        if (RowDistances != null)
            foreach (float d in RowDistances) AddInstance(transform.position + dir * d);
        AddInstance(transform.position + SwapAssetPosition);
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
