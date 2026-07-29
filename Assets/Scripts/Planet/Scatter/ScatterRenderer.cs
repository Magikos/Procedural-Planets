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
    const float DefaultRegionMeters = 150f; // fallback when no prototype declares a cull distance
    const float ReGatherMoveMeters = 10f;

    readonly ScatterField _field;
    readonly Transform _planetTransform;
    readonly ILogger _log = LoggerProvider.Get();
    readonly ScatterLodBatcher _batcher = new ScatterLodBatcher();
    readonly FaceSpaceCell[] _asyncRanges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    // Double buffer: _instances is the drawn (front) set; _back is filled off-thread.
    List<ScatterInstance> _instances = new List<ScatterInstance>(16384);
    List<ScatterInstance> _back = new List<ScatterInstance>(16384);
    // Per-prototype instance buckets rebuilt on swap (N1): the draw bands each prototype's own list
    // instead of scanning the whole instance stream once per prototype/part/LOD band every frame.
    List<Matrix4x4>[] _protoMatrices;
    List<Vector3>[] _protoPositions;
    ScatterLodBatcher.Impostor[] _impostors; // per prototype; default (Valid=false) = mesh-only

    ScatterLibraryDto _library;
    RenderParams[][] _renderParams; // [prototype][part]
    float _gatherRegion = DefaultRegionMeters; // = the farthest prototype cull; each prototype bands to its own
    bool _configured;
    Vector3 _lastGatherPos = FarAway;
    volatile bool _gathering;
    CancellationTokenSource _cts = new CancellationTokenSource();

    static readonly Vector3 FarAway = new Vector3(1e9f, 1e9f, 1e9f);
    // Material-scoped (per-Material) fade properties on Scatter.shader — not shader globals.
    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
    static readonly int _impostorBaseMapId = Shader.PropertyToID("_BaseMap");

    public ScatterRenderer(ScatterField field, Transform planetTransform)
    {
        _field = field;
        _planetTransform = planetTransform;
    }

    public void Configure()
    {
        DestroyImpostors(); // a previous world's baked cards/materials/quads
        _library = SettingsProvider.GetSettings<ScatterLibraryDto>();
        var bounds = new Bounds(_planetTransform.position, Vector3.one * 100000f);
        int protoCount = _library.Prototypes.Length;
        _renderParams = new RenderParams[protoCount][];
        _protoMatrices = new List<Matrix4x4>[protoCount];
        _protoPositions = new List<Vector3>[protoCount];
        _impostors = new ScatterLodBatcher.Impostor[protoCount];
        for (int i = 0; i < protoCount; i++)
        {
            _protoMatrices[i] = new List<Matrix4x4>();
            _protoPositions[i] = new List<Vector3>();
        }
        float region = DefaultRegionMeters;
        for (int i = 0; i < _library.Prototypes.Length; i++)
        {
            var p = _library.Prototypes[i];
            _renderParams[i] = new RenderParams[p.Parts.Length];
            if (!p.CanRender) continue;
            region = Mathf.Max(region, p.FarGatherRadius);
            // Bake the far-field billboard for coarse prototypes (trees); default for the rest. One-shot
            // at Configure (loading), so the per-frame draw just bands the prebuilt impostor.
            _impostors[i] = ScatterImpostorFactory.TryBuild(p, bounds);
            for (int j = 0; j < p.Parts.Length; j++)
            {
                var part = p.Parts[j];
                if (!part.CanRender) continue;
                // RenderMeshInstanced throws every frame if the material lacks GPU instancing. Enable it
                // so a correct authoring mistake can't spam the log; the material asset carries the flag.
                if (!part.Material.enableInstancing)
                    part.Material.enableInstancing = true;
                // Per-part dither fade band tied to that part's own cull, so each fades at its far edge
                // over the shared material rather than the material's baked fade values.
                float cull = part.MaxCullDistance;
                var fadeProps = new MaterialPropertyBlock();
                fadeProps.SetFloat(_fadeStartId, cull * 0.85f);
                fadeProps.SetFloat(_fadeEndId, cull);
                _renderParams[i][j] = new RenderParams(part.Material)
                {
                    shadowCastingMode = part.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = part.ReceiveShadows,
                    worldBounds = bounds,
                    matProps = fadeProps,
                };
            }
        }
        // Cancel any gather from a previous world and force a fresh one. The front buffers are only
        // written on the main thread (here + the swap), so clearing them here cannot race the gather,
        // which only writes _back.
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _gatherRegion = region;
        _instances.Clear();
        for (int i = 0; i < protoCount; i++) { _protoMatrices[i].Clear(); _protoPositions[i].Clear(); }
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
        long gatherMs = 0;
        int gatherEmitted = 0;
        try
        {
            if (_field.TryCaptureGatherContext(out var ctx))
            {
                var snap = PlanetTransformSnapshot.Capture(_planetTransform);
                _back.Clear();
                await Awaitable.BackgroundThreadAsync();
                if (!token.IsCancellationRequested)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    gatherEmitted = _field.GatherOffThread(ctx, snap, camPos, _gatherRegion, ScatterId.MaxLevel, _back, _asyncRanges);
                    sw.Stop();
                    gatherMs = sw.ElapsedMilliseconds;
                    haveResult = true;
                }
                await Awaitable.MainThreadAsync(); // always return to the main thread before the finally
                if (haveResult)
                    _log.Log(LogLevel.Debug, "Scatter", $"gather {gatherMs} ms, region {_gatherRegion:F0} m, {gatherEmitted} instances");
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
        for (int i = 0; i < _protoMatrices.Length; i++)
        {
            _protoMatrices[i].Clear();
            _protoPositions[i].Clear();
        }
        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            int p = inst.PrototypeIndex;
            if ((uint)p >= (uint)_protoMatrices.Length) continue;
            _protoMatrices[p].Add(Matrix4x4.TRS(inst.PositionWS, inst.Rotation, Vector3.one * inst.Scale));
            _protoPositions[p].Add(inst.PositionWS);
        }
    }

    void Draw(Vector3 camPos)
    {
        for (int p = 0; p < _library.Prototypes.Length; p++)
        {
            var proto = _library.Prototypes[p];
            if (!proto.CanRender || _protoMatrices[p].Count == 0) continue;
            _batcher.Draw(proto, _renderParams[p], _protoMatrices[p], _protoPositions[p], camPos, _impostors[p]);
        }
    }

    public void Dispose()
    {
        _configured = false;
        _cts.Cancel();
        _cts.Dispose();
        _instances.Clear();
        _back.Clear();
        if (_protoMatrices != null)
            for (int i = 0; i < _protoMatrices.Length; i++) { _protoMatrices[i].Clear(); _protoPositions[i].Clear(); }
        DestroyImpostors();
    }

    // Impostors bake a Texture2D + Material + quad Mesh per prototype; free them on regen/teardown so
    // repeated world generation doesn't leak one set per bake.
    void DestroyImpostors()
    {
        if (_impostors == null) return;
        for (int i = 0; i < _impostors.Length; i++)
        {
            ScatterLodBatcher.Impostor imp = _impostors[i];
            if (!imp.Valid) continue;
            Material m = imp.Params.material;
            if (m != null)
            {
                Texture card = m.GetTexture(_impostorBaseMapId);
                if (card != null) UnityEngine.Object.Destroy(card);
                UnityEngine.Object.Destroy(m);
            }
            if (imp.Quad != null) UnityEngine.Object.Destroy(imp.Quad);
        }
        _impostors = null;
    }
}
