using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// SP2: draws the SP1 placement stream as instanced meshes with per-instance LOD. Consumes the
// headless ScatterField (kept pure) rather than owning placement. Planet ticks Render each frame;
// instances are cached and only re-gathered when the camera moves a cell, so the per-frame cost is
// just distance-bucketing + RenderMeshInstanced from the cache.
public sealed class ScatterRenderer : IDisposable
{
    const float RegionMeters = 150f;     // gather radius; caps far draw distance (banded gathers are an SP2 refinement)
    const float ReGatherMoveMeters = 10f;
    const int BatchCap = 1023;           // Graphics.RenderMeshInstanced hard cap

    readonly ScatterField _field;
    readonly Transform _planetTransform;
    readonly Matrix4x4[] _batch = new Matrix4x4[BatchCap];
    readonly List<ScatterInstance> _instances = new List<ScatterInstance>(16384);
    readonly List<Matrix4x4> _matrices = new List<Matrix4x4>(16384);

    ScatterLibraryDto _library;
    RenderParams[] _renderParams;
    bool _configured;
    Vector3 _lastGatherPos = new Vector3(1e9f, 1e9f, 1e9f);

    public ScatterRenderer(ScatterField field, Transform planetTransform)
    {
        _field = field;
        _planetTransform = planetTransform;
    }

    public void Configure()
    {
        _library = SettingsProvider.GetSettings<ScatterLibraryDto>();
        var bounds = new Bounds(_planetTransform.position, Vector3.one * 100000f);
        _renderParams = new RenderParams[_library.Prototypes.Length];
        for (int i = 0; i < _library.Prototypes.Length; i++)
        {
            var p = _library.Prototypes[i];
            if (!p.CanRender) continue;
            // RenderMeshInstanced throws every frame if the material lacks GPU instancing. Enable it
            // so a correct authoring mistake can't spam the log; the material asset carries the flag.
            if (!p.Material.enableInstancing)
                p.Material.enableInstancing = true;
            _renderParams[i] = new RenderParams(p.Material)
            {
                shadowCastingMode = p.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = p.ReceiveShadows,
                worldBounds = bounds,
            };
        }
        _lastGatherPos = new Vector3(1e9f, 1e9f, 1e9f);
        _instances.Clear();
        _matrices.Clear();
        _configured = true;
    }

    public void Reset()
    {
        _configured = false;
        _instances.Clear();
        _matrices.Clear();
    }

    public void Render(Camera camera)
    {
        if (!_configured || _library == null || camera == null) return;
        if (_library.Prototypes.Length == 0) return;

        Vector3 camPos = camera.transform.position;
        if ((camPos - _lastGatherPos).sqrMagnitude > ReGatherMoveMeters * ReGatherMoveMeters)
        {
            _instances.Clear();
            _field.Gather(camPos, RegionMeters, ScatterId.MaxLevel, _instances);
            _matrices.Clear();
            for (int i = 0; i < _instances.Count; i++)
            {
                var inst = _instances[i];
                _matrices.Add(Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale));
            }
            _lastGatherPos = camPos;
        }

        for (int p = 0; p < _library.Prototypes.Length; p++)
        {
            var proto = _library.Prototypes[p];
            if (!proto.CanRender) continue;
            RenderParams rp = _renderParams[p];

            int lodCount = Mathf.Min(proto.LodMeshes.Length, proto.LodEndDistances.Length);
            for (int lod = 0; lod < lodCount; lod++)
            {
                Mesh mesh = proto.LodMeshes[lod];
                if (mesh == null) continue;
                float near = lod == 0 ? 0f : proto.LodEndDistances[lod - 1];
                float far = proto.LodEndDistances[lod];
                float near2 = near * near, far2 = far * far;

                int n = 0;
                for (int i = 0; i < _instances.Count; i++)
                {
                    if (_instances[i].PrototypeIndex != p) continue;
                    float d2 = (_instances[i].PositionWS - camPos).sqrMagnitude;
                    if (d2 < near2 || d2 >= far2) continue;
                    _batch[n++] = _matrices[i];
                    if (n == BatchCap)
                    {
                        Graphics.RenderMeshInstanced(rp, mesh, 0, _batch, n);
                        n = 0;
                    }
                }
                if (n > 0)
                    Graphics.RenderMeshInstanced(rp, mesh, 0, _batch, n);
            }
        }
    }

    public void Dispose()
    {
        _instances.Clear();
        _matrices.Clear();
        _configured = false;
    }
}
