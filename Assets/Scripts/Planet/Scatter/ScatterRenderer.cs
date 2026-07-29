using System;
using UnityEngine;
using UnityEngine.Rendering;

// SP2: draws the SP1 placement stream as instanced meshes with per-instance LOD. Placement generation is
// delegated to ScatterTileCache — a fixed cube-face tile grid whose (tile, prototype) payloads are
// gathered once on a background thread and cached, so camera travel only gathers the frontier instead of
// re-scanning the whole disc every move. This renderer owns the draw side: per-part RenderParams, the
// far-field impostor bake, and the LOD banding, drawing from the cache's per-prototype buckets.
public sealed class ScatterRenderer : IDisposable, IScatterDrawRuntime
{
    readonly Transform _planetTransform;
    readonly ScatterLodBatcher _batcher = new ScatterLodBatcher();
    readonly ScatterTileCache _cache;

    ScatterLibraryDto _library;
    RenderParams[][] _renderParams; // [prototype][part]
    ScatterLodBatcher.Impostor[] _impostors; // per prototype; default (Valid=false) = mesh-only
    bool _configured;

    // Material-scoped (per-Material) fade properties on Scatter.shader — not shader globals.
    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
    static readonly int _impostorBaseMapId = Shader.PropertyToID("_BaseMap");

    public ScatterRenderer(ScatterField field, Transform planetTransform)
    {
        _planetTransform = planetTransform;
        _cache = new ScatterTileCache(field, planetTransform);
    }

    public ScatterTileCache Cache => _cache;

    public void Configure()
    {
        DestroyImpostors(); // a previous world's baked cards/materials/quads
        _library = SettingsProvider.GetSettings<ScatterLibraryDto>();
        var bounds = new Bounds(_planetTransform.position, Vector3.one * 100000f);
        int protoCount = _library.Prototypes.Length;
        _renderParams = new RenderParams[protoCount][];
        _impostors = new ScatterLodBatcher.Impostor[protoCount];
        for (int i = 0; i < protoCount; i++)
        {
            var p = _library.Prototypes[i];
            _renderParams[i] = new RenderParams[p.Parts.Length];
            if (!p.CanRender) continue;
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
        _cache.Configure();
        _configured = true;
    }

    public void Reset()
    {
        _configured = false;
        _cache.Reset();
    }

    // Drives the incremental gather each frame (main thread). Drawing is NOT done here: it runs in
    // ScatterRenderPass during the opaque phase so the scatter writes camera colour+depth and lands in
    // _CameraDepthTexture. Immediate-mode Graphics.RenderMeshInstanced never reached that depth, so the
    // atmosphere's sky mask painted over tree canopies that rose above the terrain horizon.
    public void Render(Camera camera)
    {
        if (!_configured || _library == null || camera == null) return;
        if (_library.Prototypes.Length == 0) return;
        _cache.Update(camera.transform.position);
    }

    public bool HasDrawData => _configured && _library != null && _library.Prototypes.Length > 0;

    // Called by ScatterRenderPass inside its RenderGraph render func, targeting the camera colour+depth.
    public void RecordDraws(RasterCommandBuffer cmd, Vector3 camPos)
    {
        if (!_configured || _library == null) return;
        for (int p = 0; p < _library.Prototypes.Length; p++)
        {
            var proto = _library.Prototypes[p];
            if (!proto.CanRender) continue;
            var matrices = _cache.Matrices(p);
            if (matrices.Count == 0) continue;
            _batcher.Draw(cmd, proto, _renderParams[p], matrices, _cache.Positions(p), camPos, _impostors[p]);
        }
    }

    public void Dispose()
    {
        _configured = false;
        _cache.Dispose();
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
