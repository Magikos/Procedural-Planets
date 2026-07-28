using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds a prototype's far-field impostor tier: bakes LOD0 to a billboard card and assembles the
// draw-ready ScatterLodBatcher.Impostor (quad + lit material + cross-fade distances from the DTO's
// impostor policy). Both the planet renderer and the LOD strip workbench build impostors through this,
// so they are identical. Returns default (Valid=false) when the prototype has no impostor, its shader
// is missing, or the bake keys too little coverage — the caller then draws mesh-only.
public static class ScatterImpostorFactory
{
    static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int _cutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
    static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
    static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
    static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");

    public static ScatterLodBatcher.Impostor TryBuild(ScatterPrototypeDto proto, Bounds worldBounds)
    {
        if (!proto.HasImpostor) return default;
        Shader shader = Shader.Find("Scatter/Impostor");
        if (shader == null) return default;

        var meshes = new List<Mesh>();
        var materials = new List<Material>();
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
            meshes.Add(part.LodMeshes[0]);
            materials.Add(part.Material);
        }
        if (meshes.Count == 0) return default;

        ScatterImpostorBaker.Card card = ScatterImpostorBaker.Bake(meshes, materials);
        if (!card.Valid) return default;

        float meshCull = proto.MaxCullDistance;
        float start = proto.ImpostorStartDistance;
        float end = proto.ImpostorEndDistance;
        var mat = new Material(shader) { enableInstancing = true };
        mat.SetTexture(_baseMapId, card.Texture);
        mat.SetFloat(_cutoffId, 0.3f);
        mat.SetFloat(_fadeInStartId, start);    // cross-fade in over the mesh-LOD dither-out band
        mat.SetFloat(_fadeInEndId, meshCull);
        mat.SetFloat(_fadeOutStartId, end * 0.95f);
        mat.SetFloat(_fadeOutEndId, end);

        var rp = new RenderParams(mat) { worldBounds = worldBounds };
        return new ScatterLodBatcher.Impostor(rp, BuildQuad(card.Width, card.Height), start, end);
    }

    static Mesh BuildQuad(float w, float h)
    {
        var m = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-w / 2f, 0f, 0f), new Vector3(w / 2f, 0f, 0f),
                new Vector3(w / 2f, h, 0f), new Vector3(-w / 2f, h, 0f),
            },
            uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        m.RecalculateBounds();
        return m;
    }
}
