using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// GPU-driven indirect draw for scatter prototypes. Each prototype's instance transforms live in per-proto
// master StructuredBuffers; a compute (ScatterCull.compute) selects, per LOD band, the instances in that
// band's distance range and writes their master indices + the indirect draw count; then one
// Graphics.RenderMeshIndirect per band draws them (any instance count, no 1023 chunking). This replaces
// ScatterLodBatcher's ~610 chunked RenderMeshInstanced calls with ~one indirect draw per band.
//
// The scatter shaders read the transform via procedural:setup (_ScatterVisible[instanceID] -> master
// index -> _ScatterMatrices). Distance-only cull (no frustum) keeps off-camera shadow casters, so the same
// culled list is shadow-correct. LOD banding + the 15 m crossfade overlap mirror ScatterLodBatcher exactly.
public sealed class ScatterGpuDraw : IDisposable
{
    static readonly int _matricesId = Shader.PropertyToID("_ScatterMatrices");
    static readonly int _matricesInvId = Shader.PropertyToID("_ScatterMatricesInv");
    static readonly int _visibleId = Shader.PropertyToID("_ScatterVisible");
    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");

    static readonly int _cMaster = Shader.PropertyToID("_Master");
    static readonly int _cVisible = Shader.PropertyToID("_Visible");
    static readonly int _cArgs = Shader.PropertyToID("_Args");
    static readonly int _cCount = Shader.PropertyToID("_Count");
    static readonly int _cVisCap = Shader.PropertyToID("_VisibleCapacity");
    static readonly int _cCamPos = Shader.PropertyToID("_CamPos");
    static readonly int _cNear2 = Shader.PropertyToID("_Near2");
    static readonly int _cFar2 = Shader.PropertyToID("_Far2");

    const float TransitionWidth = 15f; // must match ScatterLodBatcher

    sealed class Band
    {
        public Mesh Mesh;
        public float Near2, Far2;
        public RenderParams Rp;            // material + shadow mode + bounds; matProps holds buffers + fade
        public MaterialPropertyBlock Mpb;
        public GraphicsBuffer Visible;     // this band's master indices
        public GraphicsBuffer Args;        // IndirectDrawIndexedArgs (5 uints)
    }

    sealed class ProtoGpu
    {
        public GraphicsBuffer Master, MasterInv;
        public int Capacity;
        public Band[] Bands;
        public void Dispose()
        {
            Master?.Dispose(); MasterInv?.Dispose();
            if (Bands != null) foreach (var b in Bands) { b.Visible?.Dispose(); b.Args?.Dispose(); }
            Master = null; MasterInv = null; Bands = null; Capacity = 0;
        }
    }

    ComputeShader _cull;
    int _kernel;
    bool _supported;
    ProtoGpu[] _protos = Array.Empty<ProtoGpu>();
    Matrix4x4[] _m = Array.Empty<Matrix4x4>();
    Matrix4x4[] _inv = Array.Empty<Matrix4x4>();
    readonly uint[] _argScratch = new uint[5];

    public bool Supported => _supported;

    public void Configure(ScatterLibraryDto library, Bounds bounds, ScatterLodBatcher.Impostor[] impostors)
    {
        Dispose();
        _cull = Resources.Load<ComputeShader>("ScatterCull");
        _supported = _cull != null && SystemInfo.supportsComputeShaders && SystemInfo.graphicsShaderLevel >= 45;
        if (!_supported) return;
        _kernel = _cull.FindKernel("CullBand");

        int protoCount = library.Prototypes.Length;
        _protos = new ProtoGpu[protoCount];
        for (int p = 0; p < protoCount; p++)
        {
            var proto = library.Prototypes[p];
            if (!proto.CanRender) continue;
            var bands = new List<Band>();
            for (int part = 0; part < proto.Parts.Length; part++)
            {
                var pd = proto.Parts[part];
                if (!pd.CanRender) continue;
                int lodCount = Mathf.Min(pd.LodMeshes.Length, pd.LodEndDistances.Length);
                for (int lod = 0; lod < lodCount; lod++)
                {
                    var mesh = pd.LodMeshes[lod];
                    if (mesh == null) continue;
                    float far = pd.LodEndDistances[lod];
                    float near = lod == 0 ? 0f : Mathf.Max(0f, pd.LodEndDistances[lod - 1] - TransitionWidth);
                    bands.Add(MakeMeshBand(mesh, near, far, pd.Material, pd.CastShadows, pd.ReceiveShadows, bounds));
                }
            }
            var imp = impostors != null && p < impostors.Length ? impostors[p] : default;
            if (imp.Valid)
                bands.Add(MakeImpostorBand(imp));
            if (bands.Count > 0)
                _protos[p] = new ProtoGpu { Bands = bands.ToArray() };
        }
    }

    Band MakeMeshBand(Mesh mesh, float near, float far, Material mat, bool cast, bool receive, Bounds bounds)
    {
        var mpb = new MaterialPropertyBlock();
        mpb.SetFloat(_fadeStartId, far - TransitionWidth); // fade the outgoing LOD out over its last band
        mpb.SetFloat(_fadeEndId, far);
        var rp = new RenderParams(mat)
        {
            shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = receive,
            worldBounds = bounds,
            matProps = mpb,
        };
        return new Band { Mesh = mesh, Near2 = near * near, Far2 = far * far, Rp = rp, Mpb = mpb };
    }

    Band MakeImpostorBand(ScatterLodBatcher.Impostor imp)
    {
        // The impostor material carries its own fade (baked _FadeIn*/_FadeOut*); reuse its RenderParams and
        // its property block (or a fresh one) just to attach the instance buffers each frame.
        var mpb = imp.Params.matProps ?? new MaterialPropertyBlock();
        var rp = imp.Params;
        rp.matProps = mpb;
        float start = Mathf.Max(0f, imp.StartDistance - TransitionWidth);
        return new Band { Mesh = imp.Quad, Near2 = start * start, Far2 = imp.EndDistance * imp.EndDistance, Rp = rp, Mpb = mpb };
    }

    public void DrawProto(int p, IReadOnlyList<Matrix4x4> matrices, Vector3 camPos)
    {
        if (!_supported) return;
        var g = _protos[p];
        if (g == null || g.Bands.Length == 0) return;
        int count = matrices.Count;
        if (count == 0) return;
        EnsureCapacity(g, count);

        // Stage 2: full master upload + CPU inverse every frame (dirty-only + GPU inverse come in stage 3).
        if (_m.Length < count) { _m = new Matrix4x4[count]; _inv = new Matrix4x4[count]; }
        for (int i = 0; i < count; i++) { Matrix4x4 mm = matrices[i]; _m[i] = mm; _inv[i] = mm.inverse; }
        g.Master.SetData(_m, 0, 0, count);
        g.MasterInv.SetData(_inv, 0, 0, count);

        int groups = Mathf.Max(1, Mathf.CeilToInt(count / 64f));
        _cull.SetBuffer(_kernel, _cMaster, g.Master);
        _cull.SetInt(_cCount, count);
        _cull.SetInt(_cVisCap, g.Capacity);
        _cull.SetVector(_cCamPos, camPos);

        foreach (var b in g.Bands)
        {
            _argScratch[0] = (uint)b.Mesh.GetIndexCount(0);
            _argScratch[1] = 0; // instanceCount, filled by the compute
            _argScratch[2] = (uint)b.Mesh.GetIndexStart(0);
            _argScratch[3] = (uint)b.Mesh.GetBaseVertex(0);
            _argScratch[4] = 0;
            b.Args.SetData(_argScratch);

            _cull.SetBuffer(_kernel, _cVisible, b.Visible);
            _cull.SetBuffer(_kernel, _cArgs, b.Args);
            _cull.SetFloat(_cNear2, b.Near2);
            _cull.SetFloat(_cFar2, b.Far2);
            _cull.Dispatch(_kernel, groups, 1, 1);

            b.Mpb.SetBuffer(_matricesId, g.Master);
            b.Mpb.SetBuffer(_matricesInvId, g.MasterInv);
            b.Mpb.SetBuffer(_visibleId, b.Visible);
            RenderParams rp = b.Rp;
            rp.matProps = b.Mpb;
            Graphics.RenderMeshIndirect(rp, b.Mesh, b.Args, 1, 0);
        }
    }

    void EnsureCapacity(ProtoGpu g, int count)
    {
        if (g.Master != null && g.Capacity >= count) return;
        g.Master?.Dispose(); g.MasterInv?.Dispose();
        int cap = Mathf.NextPowerOfTwo(Mathf.Max(count, 256));
        g.Master = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 64);      // sizeof(float4x4)
        g.MasterInv = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 64);
        foreach (var b in g.Bands)
        {
            b.Visible?.Dispose();
            b.Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, sizeof(uint));
            b.Args ??= new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 5, sizeof(uint));
        }
        g.Capacity = cap;
    }

    public void Dispose()
    {
        if (_protos != null) foreach (var g in _protos) g?.Dispose();
        _protos = Array.Empty<ProtoGpu>();
    }
}
