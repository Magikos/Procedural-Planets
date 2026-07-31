using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// GPU-driven indirect draw for scatter prototypes: uploads per-instance transforms to StructuredBuffers and
// draws each prototype via Graphics.RenderMeshIndirect (one draw per mesh, unbounded instance count) instead
// of ScatterLodBatcher's ~610 chunked RenderMeshInstanced calls. The scatter shaders read the transform from
// _ScatterMatrices via procedural:setup (see FoliageLit/Scatter/ScatterImpostor .shader).
//
// STAGE 1 (this file): draws only LOD0 of each part, all instances, no distance/LOD cull — enough to prove
// the buffer-fed transform + cast shadows render correctly. Stage 2 adds the ScatterCull.compute (LOD bands
// + crossfade + impostor tier) and dirty-only upload.
public sealed class ScatterGpuDraw : IDisposable
{
    static readonly int _matricesId = Shader.PropertyToID("_ScatterMatrices");
    static readonly int _matricesInvId = Shader.PropertyToID("_ScatterMatricesInv");

    sealed class ProtoGpu
    {
        public GraphicsBuffer Matrices;   // object->world per instance
        public GraphicsBuffer Inv;        // world->object per instance (correct normals without a per-vertex inverse)
        public GraphicsBuffer[] Args;     // per part: IndirectDrawIndexedArgs
        public int Capacity;

        public void Dispose()
        {
            Matrices?.Dispose(); Inv?.Dispose();
            if (Args != null) foreach (var a in Args) a?.Dispose();
            Matrices = null; Inv = null; Args = null; Capacity = 0;
        }
    }

    ProtoGpu[] _protos = Array.Empty<ProtoGpu>();
    Matrix4x4[] _m = Array.Empty<Matrix4x4>();
    Matrix4x4[] _inv = Array.Empty<Matrix4x4>();
    readonly GraphicsBuffer.IndirectDrawIndexedArgs[] _argScratch = new GraphicsBuffer.IndirectDrawIndexedArgs[1];

    public void Configure(int protoCount)
    {
        Dispose();
        _protos = new ProtoGpu[protoCount];
    }

    public void DrawProtoLod0(int p, IReadOnlyList<Matrix4x4> matrices, ScatterPrototypeDto proto, RenderParams[] partParams)
    {
        int count = matrices.Count;
        if (count == 0) return;
        var g = _protos[p] ??= new ProtoGpu();
        EnsureCapacity(g, count, proto.Parts.Length);

        // Stage 1: full upload every frame (dirty-only + GPU inverse come in stage 2/3).
        if (_m.Length < count) { _m = new Matrix4x4[count]; _inv = new Matrix4x4[count]; }
        for (int i = 0; i < count; i++) { Matrix4x4 mm = matrices[i]; _m[i] = mm; _inv[i] = mm.inverse; }
        g.Matrices.SetData(_m, 0, 0, count);
        g.Inv.SetData(_inv, 0, 0, count);

        for (int part = 0; part < proto.Parts.Length; part++)
        {
            ScatterPartDto pd = proto.Parts[part];
            if (!pd.CanRender) continue;
            Mesh mesh = pd.LodMeshes[0];
            if (mesh == null) continue;

            RenderParams rp = partParams[part];
            rp.matProps ??= new MaterialPropertyBlock();
            rp.matProps.SetBuffer(_matricesId, g.Matrices);
            rp.matProps.SetBuffer(_matricesInvId, g.Inv);

            _argScratch[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = mesh.GetIndexCount(0),
                instanceCount = (uint)count,
                startIndex = mesh.GetIndexStart(0),
                baseVertexIndex = mesh.GetBaseVertex(0),
                startInstance = 0,
            };
            g.Args[part].SetData(_argScratch);
            Graphics.RenderMeshIndirect(rp, mesh, g.Args[part], 1, 0);
        }
    }

    void EnsureCapacity(ProtoGpu g, int count, int partCount)
    {
        if (g.Matrices == null || g.Capacity < count)
        {
            g.Matrices?.Dispose(); g.Inv?.Dispose();
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(count, 256));
            g.Matrices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 64); // sizeof(float4x4)
            g.Inv = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 64);
            g.Capacity = cap;
        }
        if (g.Args == null || g.Args.Length != partCount)
        {
            if (g.Args != null) foreach (var a in g.Args) a?.Dispose();
            g.Args = new GraphicsBuffer[partCount];
            for (int i = 0; i < partCount; i++)
                g.Args[i] = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }
    }

    public void Dispose()
    {
        if (_protos != null) foreach (var g in _protos) g?.Dispose();
        _protos = Array.Empty<ProtoGpu>();
    }
}
