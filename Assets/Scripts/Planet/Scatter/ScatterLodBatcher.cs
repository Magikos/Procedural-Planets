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

    // Debug view (scatter.lodview): tint every instance by which LOD band it draws in, so LOD transitions
    // and pop-in are visible. LOD0 green, LOD1 yellow, LOD2 orange, LOD3+ red, impostor magenta. Consumers
    // (FoliageLit / Scatter) lerp albedo toward _LodDebugTint.rgb by its alpha; alpha 0 (default) = no tint.
    public static bool LodTintDebug;
    static readonly int _lodTintId = Shader.PropertyToID("_LodDebugTint");
    static readonly Color[] _lodColors =
    {
        new Color(0.2f, 1f, 0.2f, 1f),   // LOD0
        new Color(1f, 0.95f, 0.2f, 1f),  // LOD1
        new Color(1f, 0.55f, 0.1f, 1f),  // LOD2
        new Color(1f, 0.15f, 0.15f, 1f), // LOD3+
    };
    static readonly Color _impostorColor = new Color(1f, 0.2f, 1f, 1f);

    // One camera-facing quad (billboarded in the impostor shader), drawn beyond the mesh-LOD range out
    // to EndDistance. Valid is false when there is no baked card, which skips the tier (mesh-LOD only).
    public readonly struct Impostor
    {
        public readonly RenderParams Params;
        public readonly Mesh Quad;
        public readonly float StartDistance; // where the impostor takes over (usually the mesh-LOD cull)
        public readonly float EndDistance;   // where the impostor itself culls
        public readonly bool Valid;

        public Impostor(RenderParams parameters, Mesh quad, float startDistance, float endDistance)
        {
            Params = parameters;
            Quad = quad;
            StartDistance = startDistance;
            EndDistance = endDistance;
            Valid = quad != null && endDistance > startDistance;
        }
    }

    // matrices[i]/positionsWS[i] describe instance i of THIS prototype (the caller supplies its own set).
    public void Draw(ScatterPrototypeDto proto, RenderParams[] partParams,
                     IReadOnlyList<Matrix4x4> matrices, IReadOnlyList<Vector3> positionsWS, Vector3 camPos,
                     in Impostor impostor = default)
    {
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
                float near = lod == 0 ? 0f : pd.LodEndDistances[lod - 1];
                float far = pd.LodEndDistances[lod];
                if (LodTintDebug) SetLodTint(rp, _lodColors[Mathf.Min(lod, _lodColors.Length - 1)]);
                DrawBand(rp, mesh, near * near, far * far, matrices, positionsWS, camPos);
            }
        }

        if (impostor.Valid)
        {
            if (LodTintDebug) SetLodTint(impostor.Params, _impostorColor);
            DrawBand(impostor.Params, impostor.Quad,
                     impostor.StartDistance * impostor.StartDistance,
                     impostor.EndDistance * impostor.EndDistance,
                     matrices, positionsWS, camPos);
        }
    }

    static void SetLodTint(RenderParams rp, Color c)
    {
        if (rp.matProps == null) return; // no property block on this part; skip the debug tint
        rp.matProps.SetColor(_lodTintId, c);
    }

    void DrawBand(RenderParams rp, Mesh mesh, float near2, float far2,
                  IReadOnlyList<Matrix4x4> matrices, IReadOnlyList<Vector3> positionsWS, Vector3 camPos)
    {
        int n = 0;
        for (int i = 0; i < matrices.Count; i++)
        {
            float d2 = (positionsWS[i] - camPos).sqrMagnitude;
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
