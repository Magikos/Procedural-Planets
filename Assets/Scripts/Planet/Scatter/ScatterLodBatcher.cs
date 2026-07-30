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
                DrawBand(rp, mesh, near * near, far * far, matrices, positionsWS, camPos);
            }
        }

        if (impostor.Valid)
            DrawBand(impostor.Params, impostor.Quad,
                     impostor.StartDistance * impostor.StartDistance,
                     impostor.EndDistance * impostor.EndDistance,
                     matrices, positionsWS, camPos);
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

    // Command-buffer draw path (planet render pass): identical banding to Draw() above, but records into a
    // RasterCommandBuffer so the scatter writes camera colour+depth — and thus lands in _CameraDepthTexture,
    // letting depth-dependent passes (atmosphere) treat canopies as geometry, not sky. Draws each part's
    // forward pass; the scatter shaders light from _SunParams globals, so no URP per-object light setup is
    // needed. RenderMeshInstanced (immediate) never reached _CameraDepthTexture; this does.
    public void Draw(RasterCommandBuffer cmd, ScatterPrototypeDto proto, RenderParams[] partParams,
                     IReadOnlyList<Matrix4x4> matrices, IReadOnlyList<Vector3> positionsWS, Vector3 camPos,
                     in Impostor impostor = default, string passName = "ForwardLit")
    {
        for (int part = 0; part < proto.Parts.Length; part++)
        {
            ScatterPartDto pd = proto.Parts[part];
            if (!pd.CanRender) continue;
            RenderParams rp = partParams[part];
            int pass = ResolvePass(rp.material, passName);
            if (pass < 0) continue;

            int lodCount = Mathf.Min(pd.LodMeshes.Length, pd.LodEndDistances.Length);
            for (int lod = 0; lod < lodCount; lod++)
            {
                Mesh mesh = pd.LodMeshes[lod];
                if (mesh == null) continue;
                float near = lod == 0 ? 0f : pd.LodEndDistances[lod - 1];
                float far = pd.LodEndDistances[lod];
                DrawBand(cmd, rp.material, pass, rp.matProps, mesh, near * near, far * far, matrices, positionsWS, camPos);
            }
        }

        if (impostor.Valid)
            DrawBand(cmd, impostor.Params.material, ResolvePass(impostor.Params.material, passName), impostor.Params.matProps,
                     impostor.Quad, impostor.StartDistance * impostor.StartDistance,
                     impostor.EndDistance * impostor.EndDistance, matrices, positionsWS, camPos);
    }

    void DrawBand(RasterCommandBuffer cmd, Material material, int pass, MaterialPropertyBlock props, Mesh mesh,
                  float near2, float far2, IReadOnlyList<Matrix4x4> matrices, IReadOnlyList<Vector3> positionsWS, Vector3 camPos)
    {
        if (material == null || pass < 0 || mesh == null) return;
        int n = 0;
        for (int i = 0; i < matrices.Count; i++)
        {
            float d2 = (positionsWS[i] - camPos).sqrMagnitude;
            if (d2 < near2 || d2 >= far2) continue;
            _batch[n++] = matrices[i];
            if (n == BatchCap)
            {
                cmd.DrawMeshInstanced(mesh, 0, material, pass, _batch, n, props);
                n = 0;
            }
        }
        if (n > 0)
            cmd.DrawMeshInstanced(mesh, 0, material, pass, _batch, n, props);
    }

    // Pass index per (material, passName), cached. "ForwardLit" falls back to pass 0 (the forward pass is
    // first in every scatter shader); "DepthNormals" falls back to -1 so a material without it is skipped.
    readonly Dictionary<(Material, string), int> _passCache = new Dictionary<(Material, string), int>();
    int ResolvePass(Material m, string passName)
    {
        if (m == null) return -1;
        var key = (m, passName);
        if (_passCache.TryGetValue(key, out int p)) return p;
        p = m.FindPass(passName);
        if (p < 0 && passName == "ForwardLit") p = m.FindPass("UniversalForward");
        if (p < 0 && passName == "ForwardLit") p = 0;
        _passCache[key] = p;
        return p;
    }
}
