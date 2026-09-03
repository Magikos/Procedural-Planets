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
    static readonly int _leafCardId = Shader.PropertyToID("_LeafCard");
    static readonly int _gridNId = Shader.PropertyToID("_GridN");
    static readonly int _centerOffsetId = Shader.PropertyToID("_CenterOffset");
    static readonly int _worldSizeId = Shader.PropertyToID("_WorldSize");
    static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
    static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
    static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
    static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");

    // Frames per axis in the hemi-octahedral atlas. 4 = 16 angles into a 512 px card (128 px cells).
    // FromPrebaked INFERS this from atlas width, so an atlas baked at another gridN still reads correctly;
    // AtlasCellPx does not have that escape hatch and must never move.
    const int OctGridN = 4;

    // Parts drawn through this shader are foliage, so their card gets the leaf translucency the mesh
    // canopy already has. A rock or a structure card must not glow with the sun behind it.
    const string FoliageShaderName = "Scatter/FoliageLit";

    // What the bake tool preserves coverage against. Every atlas in the project is imported with
    // mipMapsPreserveCoverage measured at THIS alpha, so changing it invalidates all of them and needs a
    // reimport, not just a rebake.
    public const float CoveragePreserveReference = 0.4f;

    // What the shader clips at, deliberately half a step ABOVE the preserve reference. Preserve-coverage
    // holds the COUNT of texels over its reference, but the card is sampled bilinearly, so two rescaled
    // neighbours interpolate over that same threshold and the blob between them draws wider than the texels
    // it came from. That widening grows with mip level, which makes a card GAIN silhouette as it shrinks -
    // backwards for an LOD, and the pop Bryan saw across the lake. Measured as coverage/height^2 at 192, 48,
    // 24 and 12 px on screen: at 0.4 the Taiga Pine runs 0.074 0.078 0.102 0.069 and even a solid rock
    // climbs 0.398 -> 0.410, while at 0.5 the pine holds 0.072 0.072 0.069 0.069 and the rock 0.395 0.396
    // 0.396 0.389. Flat is the whole point - the card must read as the same prop at every distance it
    // covers. Past 0.6 it inverts and erodes instead.
    public const float CardAlphaCutoff = 0.5f;

    public static ScatterLodBatcher.Impostor TryBuild(ScatterPrototypeDto proto, Bounds worldBounds)
    {
        if (!proto.HasImpostor) return default;
        Shader shader = Shader.Find("Scatter/Impostor");
        if (shader == null) return default;

        var meshes = new List<Mesh>();
        var materials = new List<Material>();
        bool leafCard = false;
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (part.LodMeshes.Length == 0 || part.LodMeshes[0] == null) continue;
            meshes.Add(part.LodMeshes[0]);
            materials.Add(part.Material);
            leafCard |= part.Material != null && part.Material.shader != null
                     && part.Material.shader.name == FoliageShaderName;
        }
        if (meshes.Count == 0) return default;

        // Prefer a pre-baked atlas (editor bake tool) to skip the on-load bake; fall back to baking live
        // for prototypes without one (runtime-placed / custom-saved structures).
        ScatterImpostorBaker.AtlasCard card = proto.BakedImpostorAtlas != null
            ? ScatterImpostorBaker.FromPrebaked(proto.BakedImpostorAtlas, proto.BakedImpostorNormal, meshes)
            : ScatterImpostorBaker.BakeAtlas(meshes, materials, OctGridN);
        if (!card.Valid) return default;

        float start = proto.ImpostorStartDistance;
        float end = proto.ImpostorEndDistance;
        var mat = new Material(shader) { enableInstancing = true };
        mat.SetTexture(_baseMapId, card.Texture);
        if (card.NormalTexture != null) mat.SetTexture(_normalMapId, card.NormalTexture);
        mat.SetFloat(_cutoffId, CardAlphaCutoff);
        // Whole-card, because the atlas carries no leaf mask, so a tree's trunk pixels transmit light too.
        // ponytail: no mask, bake one into an atlas channel if a trunk ever reads wrong at card range.
        mat.SetFloat(_leafCardId, leafCard ? 1f : 0f);
        mat.SetFloat(_gridNId, card.GridN);
        mat.SetFloat(_centerOffsetId, card.CenterOffset); // billboard centred on the tree centre
        mat.SetFloat(_worldSizeId, card.WorldSize);       // square side (max of footprint / height)
        // Opaque across the card's whole band. The 1 m ramp exists only to keep the shader's saturate off
        // its own band edge; the card must never draw partly dithered while the mesh is still solid.
        mat.SetFloat(_fadeInStartId, start - 1f);
        mat.SetFloat(_fadeInEndId, start);
        mat.SetFloat(_fadeOutStartId, end * 0.6f); // long dither-out so the tree line thins into the distance
        mat.SetFloat(_fadeOutEndId, end);          // instead of a hard ~5% pop at the cull edge

        var rp = new RenderParams(mat) { worldBounds = worldBounds, shadowCastingMode = ShadowCastingMode.On };
        // A pre-baked atlas is a shared ASSET this impostor borrows; a live bake is a runtime texture it owns.
        // Teardown destroys only the owned one (destroying the asset — or a card other prototypes still
        // share — corrupts it / throws).
        return new ScatterLodBatcher.Impostor(rp, BuildUnitQuad(), start, end,
            ownsCard: proto.BakedImpostorAtlas == null);
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
