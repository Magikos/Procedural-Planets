using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shared LOD draw for one prototype's scatter instances: distance-banded mesh LODs, then an optional
// far-field billboard impostor tier. ScatterRenderer (the planet) and the ScatterLodStrip test harness
// both draw through this, so LOD distances / impostors tuned in the lightweight strip scene are exactly
// what the planet renders — no separate preview mechanism, no Unity LODGroups.
public sealed class ScatterLodBatcher
{
    const int BatchCap = 1023; // Graphics.RenderMeshInstanced hard cap

    readonly Matrix4x4[] _batch = new Matrix4x4[BatchCap];
    // Per-instance camera distance², computed ONCE per prototype per frame and reused across every LOD
    // band + impostor pass — the vector distance was the batcher's dominant per-frame cost and it was being
    // recomputed once per band (5x). Grows to the largest prototype instance count seen.
    float[] _dist = System.Array.Empty<float>();

    // Debug view (scatter.lodview): tint every instance by which LOD band it draws in, so LOD transitions
    // and pop-in are visible. LOD0 green, LOD1 yellow, LOD2 orange, LOD3+ red, impostor magenta. Consumers
    // (FoliageLit / Scatter) lerp albedo toward _LodDebugTint.rgb by its alpha; alpha 0 (default) = no tint.
    public static bool LodTintDebug;
    static readonly int _lodTintId = Shader.PropertyToID("_LodDebugTint");

    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
    static readonly Color[] _lodColors =
    {
        new Color(0.2f, 1f, 0.2f, 1f),   // LOD0
        new Color(1f, 0.95f, 0.2f, 1f),  // LOD1
        new Color(1f, 0.55f, 0.1f, 1f),  // LOD2
        new Color(1f, 0.15f, 0.15f, 1f), // LOD3+
    };
    static readonly Color _impostorColor = new Color(1f, 0.2f, 1f, 1f);
    static readonly Color _tintOff = new Color(0f, 0f, 0f, 0f); // alpha 0 => shader lerp is a no-op

    // Shared with ScatterGpuDraw, which is the path the planet actually draws through (UseGpuDraw defaults
    // to true). Without this the debug view only coloured the CPU fallback, so scatter.lodview looked broken
    // on the planet while still working in the LOD strip workbench.
    public static Color DebugTintFor(int lod) =>
        !LodTintDebug ? _tintOff
                      : lod < 0 ? _impostorColor
                                : _lodColors[Mathf.Min(lod, _lodColors.Length - 1)];

    // One camera-facing quad (billboarded in the impostor shader), drawn beyond the mesh-LOD range out
    // to EndDistance. Valid is false when there is no baked card, which skips the tier (mesh-LOD only).
    public readonly struct Impostor
    {
        public readonly RenderParams Params;
        public readonly Mesh Quad;
        public readonly float StartDistance; // where the impostor takes over (usually the mesh-LOD cull)
        public readonly float EndDistance;   // where the impostor itself culls
        public readonly bool Valid;
        // True when the _BaseMap card is a runtime bake owned by this impostor (destroy it on teardown);
        // false when it is a pre-baked atlas ASSET, which must never be destroyed.
        public readonly bool OwnsCard;

        public Impostor(RenderParams parameters, Mesh quad, float startDistance, float endDistance, bool ownsCard = true)
        {
            Params = parameters;
            Quad = quad;
            StartDistance = startDistance;
            EndDistance = endDistance;
            Valid = quad != null && endDistance > startDistance;
            OwnsCard = ownsCard;
        }
    }

    // matrices[i]/positionsWS[i] describe instance i of THIS prototype (the caller supplies its own set).
    public void Draw(ScatterPrototypeDto proto, RenderParams[] partParams,
                     IReadOnlyList<Matrix4x4> matrices, IReadOnlyList<Vector3> positionsWS, Vector3 camPos,
                     in Impostor impostor = default)
    {
        // One distance pass for the whole prototype; every band below reads _dist instead of recomputing.
        int count = matrices.Count;
        if (_dist.Length < count) _dist = new float[Mathf.NextPowerOfTwo(count)];
        for (int i = 0; i < count; i++) _dist[i] = (positionsWS[i] - camPos).sqrMagnitude;

        float meshCull = proto.MeshCullDistance;
        for (int part = 0; part < proto.Parts.Length; part++)
        {
            ScatterPartDto pd = proto.Parts[part];
            if (!pd.CanRender) continue;
            RenderParams rp = partParams[part];

            int lodCount = Mathf.Min(pd.LodMeshes.Length, pd.LodEndDistances.Length);
            for (int lod = 0; lod < lodCount; lod++)
            {
                Mesh mesh = pd.LodMeshes[lod];
                if (mesh == null) continue;
                float far = BandFarFor(lod, pd.LodEndDistances, meshCull);
                float near = BandNearFor(lod, pd.LodEndDistances);
                if (near >= far) continue;
                SetFade(rp, FadeStartFor(lod, lodCount, far, impostor), far);
                SetLodTint(rp, LodTintDebug ? _lodColors[Mathf.Min(lod, _lodColors.Length - 1)] : _tintOff);
                DrawBand(rp, mesh, near * near, far * far, matrices, count);
            }
        }

        if (impostor.Valid)
        {
            // The card's band begins where the last mesh band culls, so exactly one tier draws at any
            // distance. Its fade lives on its material (_FadeIn*/_FadeOut*, baked by
            // ScatterImpostorFactory), not in _FadeStart/_FadeEnd.
            float start = ImpostorNearFor(impostor);
            SetLodTint(impostor.Params, LodTintDebug ? _impostorColor : _tintOff);
            DrawBand(impostor.Params, impostor.Quad,
                     start * start,
                     impostor.EndDistance * impostor.EndDistance,
                     matrices, count);
        }
    }

    static void SetLodTint(RenderParams rp, Color c)
    {
        if (rp.matProps == null) return; // no property block on this part; skip the debug tint
        rp.matProps.SetColor(_lodTintId, c);
    }

    // Mesh bands partition the distance range exactly — this band starts where the previous one culls. Both
    // band tests (DrawBand here, ScatterCull.compute on the GPU path) are half-open [near, far), so an exact
    // partition leaves no gap at the seam and draws no instance twice.
    public static float BandNearFor(int lod, float[] lodEndDistances) =>
        lod == 0 ? 0f : lodEndDistances[lod - 1];

    // The authored band end, clipped to where the card takes over. A prototype's handover is decided by its
    // on-screen size (ScatterPrototypeDto.MeshCullDistance), not by the authored distance, so a band that
    // lies wholly past it comes back with far <= near and the caller skips it. Without the clip the mesh
    // and the card would both draw between the handover and the authored cull.
    public static float BandFarFor(int lod, float[] lodEndDistances, float meshCull) =>
        Mathf.Min(lodEndDistances[lod], meshCull);

    // The card's band starts at the mesh cull, which is also where ScatterImpostorFactory bakes its
    // coverage ramp to reach 1 — the card is opaque on the first frame it draws.
    public static float ImpostorNearFor(in Impostor impostor) => impostor.StartDistance;

    // A screen-door only hides a swap while whatever is BEHIND the dithered-away pixels is what should be
    // there. Exactly one successor qualifies: the background, because a prop with no card is *supposed* to
    // be gone past its cull, so dissolving into the world behind it is the disappearance and not an
    // artifact. A coarser mesh LOD does not — it is a decimation of its predecessor with a thinner canopy,
    // so dithering LOD n out exposes LOD n+1's gaps. Neither does the impostor card: both tiers screen-door
    // against the same 4x4 Bayer table, and because the card's baked silhouette does not agree with the
    // mesh's per pixel, the two clip the same thresholds and the sky shows through as a lattice of holes
    // along the horizon tree line. So a mesh band fades only when the background is what comes next.
    // Everywhere else it holds full coverage to its cull, and the card — opaque from the first frame it
    // draws — takes over there with no dither on either side of the swap.
    public static float FadeStartFor(int lod, int lodCount, float far, in Impostor impostor)
    {
        bool coveredByCard = impostor.Valid && impostor.StartDistance <= far;
        if (coveredByCard || lod != lodCount - 1) return far;
        return far * ScatterPrototypeDto.MeshFadeFraction;
    }

    static void SetFade(RenderParams rp, float start, float end)
    {
        if (rp.matProps == null) return;
        rp.matProps.SetFloat(_fadeStartId, start);
        rp.matProps.SetFloat(_fadeEndId, end);
    }

    void DrawBand(RenderParams rp, Mesh mesh, float near2, float far2, IReadOnlyList<Matrix4x4> matrices, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            float d2 = _dist[i];
            if (d2 < near2 || d2 >= far2) continue;
            _batch[n++] = matrices[i];
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
