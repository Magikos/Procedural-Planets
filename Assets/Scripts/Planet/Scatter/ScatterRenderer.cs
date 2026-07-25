using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

// SP2: draws the SP1 placement stream as instanced meshes with per-instance LOD. Consumes the
// headless ScatterField (kept pure) rather than owning placement.
//
// The re-gather runs on a background thread (Awaitable.BackgroundThreadAsync): the transform and the
// per-generation config are captured on the main thread, the pure gather fills a back buffer off the
// main thread, and the buffers are swapped back on the main thread. So camera travel never stalls the
// frame on placement — the current (front) set keeps drawing while the next set is computed. Only one
// gather is in flight at a time; a regen/teardown cancels it via the token so a stale result is
// dropped instead of swapped in.
public sealed class ScatterRenderer : IDisposable
{
    const float RegionMeters = 150f;     // gather radius; caps far draw distance (banded gathers are an SP2 refinement)
    const float ReGatherMoveMeters = 10f;
    const int BatchCap = 1023;           // Graphics.RenderMeshInstanced hard cap

    readonly ScatterField _field;
    readonly Transform _planetTransform;
    readonly ILogger _log = LoggerProvider.Get();
    readonly Matrix4x4[] _batch = new Matrix4x4[BatchCap];
    readonly FaceSpaceCell[] _asyncRanges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    // Double buffer: _instances/_matrices are the drawn (front) set; _back is filled off-thread.
    List<ScatterInstance> _instances = new List<ScatterInstance>(16384);
    List<ScatterInstance> _back = new List<ScatterInstance>(16384);
    readonly List<Matrix4x4> _matrices = new List<Matrix4x4>(16384);

    ScatterLibraryDto _library;
    RenderParams[] _renderParams;
    bool _configured;
    Vector3 _lastGatherPos = FarAway;
    volatile bool _gathering;
    CancellationTokenSource _cts = new CancellationTokenSource();

    static readonly Vector3 FarAway = new Vector3(1e9f, 1e9f, 1e9f);

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
        // Cancel any gather from a previous world and force a fresh one. The front buffers are only
        // written on the main thread (here + the swap), so clearing them here cannot race the gather,
        // which only writes _back.
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _instances.Clear();
        _matrices.Clear();
        _lastGatherPos = FarAway;
        _configured = true;
    }

    public void Reset()
    {
        _configured = false;
        _cts.Cancel(); // an in-flight gather will see the cancelled token and drop its result
        _lastGatherPos = FarAway;
    }

    public void Render(Camera camera)
    {
        if (!_configured || _library == null || camera == null) return;
        if (_library.Prototypes.Length == 0) return;

        Vector3 camPos = camera.transform.position;
        if (!_gathering && (camPos - _lastGatherPos).sqrMagnitude > ReGatherMoveMeters * ReGatherMoveMeters)
            _ = GatherAndSwapAsync(camPos);

        Draw(camPos);
    }

    // Fire-and-forget from Render. The synchronous prefix (up to the first await) sets _gathering and
    // captures the inputs on the calling frame, so a second Render can't launch an overlapping gather.
    async Awaitable GatherAndSwapAsync(Vector3 camPos)
    {
        _gathering = true;
        CancellationToken token = _cts.Token;
        bool haveResult = false;
        try
        {
            if (_field.TryCaptureGatherContext(out var ctx))
            {
                var snap = PlanetTransformSnapshot.Capture(_planetTransform);
                _back.Clear();
                await Awaitable.BackgroundThreadAsync();
                if (!token.IsCancellationRequested)
                {
                    _field.GatherOffThread(ctx, snap, camPos, RegionMeters, ScatterId.MaxLevel, _back, _asyncRanges);
                    haveResult = true;
                }
                await Awaitable.MainThreadAsync(); // always return to the main thread before the finally
            }
        }
        catch (OperationCanceledException) { /* teardown mid-await; expected */ }
        catch (Exception e)
        {
            haveResult = false;
            _log.Log(LogLevel.Warning, "Scatter", $"scatter gather failed: {e}");
        }
        finally
        {
            if (haveResult && !token.IsCancellationRequested && _configured)
            {
                SwapAndBuildMatrices();
                _lastGatherPos = camPos;
            }
            _gathering = false;
        }
    }

    void SwapAndBuildMatrices()
    {
        (_instances, _back) = (_back, _instances);
        _matrices.Clear();
        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            _matrices.Add(Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale));
        }
    }

    void Draw(Vector3 camPos)
    {
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
        _configured = false;
        _cts.Cancel();
        _cts.Dispose();
        _instances.Clear();
        _back.Clear();
        _matrices.Clear();
    }
}
