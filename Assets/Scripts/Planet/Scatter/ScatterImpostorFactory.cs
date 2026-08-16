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
    static readonly int _normalMapId = Shader.PropertyToID("_NormalMap");
    static readonly int _cutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int _gridNId = Shader.PropertyToID("_GridN");
    static readonly int _centerOffsetId = Shader.PropertyToID("_CenterOffset");
    static readonly int _worldSizeId = Shader.PropertyToID("_WorldSize");
    static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
    static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
    static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
    static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");

    // Frames per axis in the hemi-octahedral atlas. 8 = 64 angles into a 1024² card (128² cells).
    const int OctGridN = 8;

    // Prototypes sharing an ImpostorShareKey (the generated age/seed variants of one tree species) reuse one
    // baked atlas instead of each paying 64 camera renders + two 1024² textures. FromPrebaked re-frames the
    // shared texture to each prototype's own bounds, so a sapling variant still billboards at sapling size.
    // Cached cards are session-lived and never owned by an impostor, so a world regenerate reuses them.
    // ponytail: keyed by name only — change the variant count mid-session and the far card stays as first
    // baked (a 300 m+ silhouette nuance). Key on the tree def if that ever reads wrong.
    static readonly Dictionary<string, ScatterImpostorBaker.AtlasCard> _sharedCards = new();

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

        // Prefer a pre-baked atlas (editor bake tool) to skip the on-load bake; fall back to baking live
        // for prototypes without one (runtime-placed / custom-saved structures).
        string shareKey = proto.BakedImpostorAtlas == null ? proto.ImpostorShareKey : null;
        bool shared = !string.IsNullOrEmpty(shareKey);
        ScatterImpostorBaker.AtlasCard card;
        if (proto.BakedImpostorAtlas != null)
            card = ScatterImpostorBaker.FromPrebaked(proto.BakedImpostorAtlas, proto.BakedImpostorNormal, meshes);
        else if (shared && _sharedCards.TryGetValue(shareKey, out ScatterImpostorBaker.AtlasCard hit) && hit.Texture != null)
            card = ScatterImpostorBaker.FromPrebaked(hit.Texture, hit.NormalTexture, meshes);
        else
        {
            card = ScatterImpostorBaker.BakeAtlas(meshes, materials, OctGridN);
            if (shared && card.Valid)
            {
                card.Texture.hideFlags = HideFlags.HideAndDontSave;
                if (card.NormalTexture != null) card.NormalTexture.hideFlags = HideFlags.HideAndDontSave;
                _sharedCards[shareKey] = card;
            }
        }
        if (!card.Valid) return default;

        float meshCull = proto.MaxCullDistance;
        float start = proto.ImpostorStartDistance;
        float end = proto.ImpostorEndDistance;
        var mat = new Material(shader) { enableInstancing = true };
        mat.SetTexture(_baseMapId, card.Texture);
        if (card.NormalTexture != null) mat.SetTexture(_normalMapId, card.NormalTexture);
        mat.SetFloat(_cutoffId, 0.3f);
        mat.SetFloat(_gridNId, card.GridN);
        mat.SetFloat(_centerOffsetId, card.CenterOffset); // billboard centred on the tree centre
        mat.SetFloat(_worldSizeId, card.WorldSize);       // square side (max of footprint / height)
        mat.SetFloat(_fadeInStartId, start);    // cross-fade in over the mesh-LOD dither-out band
        mat.SetFloat(_fadeInEndId, meshCull);
        mat.SetFloat(_fadeOutStartId, end * 0.6f); // long dither-out so the tree line thins into the distance
        mat.SetFloat(_fadeOutEndId, end);          // instead of a hard ~5% pop at the cull edge

        var rp = new RenderParams(mat) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.On };
        // A pre-baked atlas is a shared ASSET this impostor borrows; a live bake is a runtime texture it owns.
        // Teardown destroys only the owned one (destroying the asset — or a card other prototypes still
        // share — corrupts it / throws).
        return new ScatterLodBatcher.Impostor(rp, BuildUnitQuad(), start, end,
            ownsCard: proto.BakedImpostorAtlas == null && !shared);
    }

    // Unit centred quad (xy in [-0.5,0.5], uv [0,1]); the octahedral shader billboards + scales it by
    // _WorldSize and centres it at _CenterOffset above the pivot.
    static Mesh BuildUnitQuad()
    {
        var m = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        m.RecalculateBounds();
        return m;
    }
}
