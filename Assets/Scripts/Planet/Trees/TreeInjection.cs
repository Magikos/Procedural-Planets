using System;
using System.Collections.Generic;
using UnityEngine;

// Replaces the scatter library's TREE prototypes (Interaction == Chop) with generated trees (plan 006). The
// species is chosen from the prototype's Biome (TreeDefLibrary.HasTree), so each biome grows the right tree.
// Applied at boot (Planet.RegisterWorldSettings) so play shows generated trees with no console step; `tree.inject
// off` reverts to Synty at runtime. Heavily guarded — any failure keeps the original Synty prototype, and a
// top-level catch keeps scatter working. Age varies per tree TYPE (seeded by name); true per-INSTANCE age needs
// per-instance scatter mesh variants (later).
// ponytail: boot generates ~18 trees on the main thread here; fine as a one-shot, move to a background job (T7)
// if boot hitches.
public static class TreeInjection
{
    public static bool Enabled = true;

    static readonly Dictionary<TreeDefLibrary.TreeSpecies, (Material bark, Material foliage)> _mats = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _cleanFallback = new();
    static readonly Dictionary<TreeDefLibrary.TreeSpecies, Material> _coniferMats = new();
    static Material _cleanBase; // a clean leaf-image FoliageLit material (not a Synty palette atlas)

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
            _cleanBase = FindCleanLeaf(lib);
            var protos = new ScatterPrototypeDto[lib.Prototypes.Length];
            int replaced = 0;
            for (int i = 0; i < protos.Length; i++)
            {
                ScatterPrototypeDto p = lib.Prototypes[i];
                ScatterPrototypeDto gen = IsTree(p) ? TryReplace(p) : null;
                protos[i] = gen ?? p;
                if (gen != null) replaced++;
            }
            LoggerProvider.Log(LogLevel.Info, "TreeInject", $"Replaced {replaced} tree prototype(s) with generated trees.");
            return new ScatterLibraryDto(protos);
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("TreeInject", e);
            return lib; // never break scatter
        }
    }

    // Rebuild the scatter DTO from the source library at the current Enabled state, for a runtime toggle.
    public static ScatterLibraryDto Rebuild()
    {
        var so = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
        return so != null ? Apply(ScatterLibraryDto.From(so)) : null;
    }

    static bool IsTree(ScatterPrototypeDto p) =>
        p != null && p.Interaction == ScatterInteraction.Chop && p.Parts != null && p.Parts.Length > 0;

    static ScatterPrototypeDto TryReplace(ScatterPrototypeDto p)
    {
        try
        {
            if (!TreeDefLibrary.HasTree(p.Biome, out TreeDefLibrary.TreeSpecies species))
                species = TreeDefLibrary.TreeSpecies.Broadleaf;

            int seed = Mathf.Abs((p.DisplayName ?? "tree").GetHashCode()) % 900000 + 1;
            float age = 0.3f + (seed % 100) / 100f * 0.7f; // 0.3..1.0 per type -> age variety across the world
            TreeDef def = TreeDefLibrary.Species(species, age);
            GeneratedTree t = TreeGenerator.Generate(def, seed);
            if (t.Bark == null || t.Bark.vertexCount == 0) return null; // keep Synty

            (Material bark, Material foliageFallback) = MatsFor(species, def);
            // Conifer cone is solid dark-green geometry (VertexColorLit); other species reuse the prototype's own
            // Synty leaf-patch material so each biome gets its correct leaf look (our cards carry the vtx.B leaf
            // mask it expects). Palette-atlas materials (pine's Generic_*) get a clean tinted leaf substitute.
            Material foliage = def.NeedleFoliage
                ? ConiferMat(species, def)
                : SyntyFoliage(p, species, def) ?? foliageFallback;

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 50f) cull = 300f;
            float[] dist = { cull * 0.6f, cull }; // hold full LOD0 canopy farther before the impostor takes over

            var barkPart = new ScatterPartDto(bark, t.BarkLods, Trim(dist, t.BarkLods.Length), true, true);
            var foliagePart = new ScatterPartDto(foliage, t.FoliageLods, Trim(dist, t.FoliageLods.Length), true, false);

            // Keep everything else about the prototype (slot/biome/placement); swap the parts + stump, and clear
            // the Synty impostor atlas so the scatter renderer bakes a fresh impostor from the generated LOD0.
            // Widen ScaleRange so instances vary in size (all instances share one mesh; size is the only cheap
            // per-instance variance until true per-instance mesh variants land).
            return p with
            {
                Parts = new[] { barkPart, foliagePart },
                ScaleRange = new Vector2(0.6f, 1.45f),
                StumpMesh = t.Stump,
                StumpMaterial = bark,
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
            };
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("TreeInject", e);
            return null; // keep Synty
        }
    }

    static Material SyntyFoliage(ScatterPrototypeDto p, TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        Material picked = PickFoliageMaterial(MatsOf(p));
        if (picked != null && !IsPaletteAtlas(picked)) return picked; // clean leaf-image material — use as-is
        return CleanFoliage(s, def);                                  // palette atlas / none -> tinted clean leaf
    }

    static Material[] MatsOf(ScatterPrototypeDto p)
    {
        if (p?.Parts == null) return System.Array.Empty<Material>();
        var mats = new Material[p.Parts.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = p.Parts[i]?.Material;
        return mats;
    }

    // Textures our whole-card [0,1] UVs can't use: palette/gradient atlases (Synty "Generic_*"), biome palette
    // sheets ("..._Texture_NN", e.g. FoliageDead), and multi-leaf atlases (pohutukawa). Only the clean single-patch
    // leaf textures (leafPatch_*) and the palm frond atlas (handled per-cell) survive; substitute for the rest.
    public static bool IsPaletteAtlas(Material m)
    {
        if (m == null) return true;
        Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
        if (t == null) return true;
        string n = t.name != null ? t.name.ToLowerInvariant() : "";
        return n.Contains("generic") || n.Contains("_texture") || n.Contains("palette") || n.Contains("pohutukawa");
    }

    static Material FindCleanLeaf(ScatterLibraryDto lib)
    {
        foreach (ScatterPrototypeDto pr in lib.Prototypes)
        {
            if (!IsTree(pr)) continue;
            Material m = PickFoliageMaterial(MatsOf(pr));
            if (m != null && !IsPaletteAtlas(m)) return m;
        }
        return null;
    }

    // A tinted copy of a known clean leaf material, for species whose Synty material is a palette atlas (conifer).
    static Material CleanFoliage(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (_cleanBase == null) return null;
        if (!_cleanFallback.TryGetValue(s, out Material m))
        {
            m = new Material(_cleanBase) { name = $"Gen {def.Name} leaf" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", def.LeafColor * 2f); // texture already dark; lift tint
            _cleanFallback[s] = m;
        }
        return m;
    }

    // The leaf material among a tree prototype's parts. Both trunk and foliage can use Scatter/FoliageLit, so pick
    // by name: prefer a "...Canopy" material, else the first non-"beard" FoliageLit part, else any FoliageLit part.
    // Handles combined 1-part trees (pine/palm/dead), trunk+foliage, and canopy+branches ordering. Public so the
    // preview can match the world per species.
    public static Material PickFoliageMaterial(IReadOnlyList<Material> mats)
    {
        if (mats == null) return null;
        Material nonBeard = null, any = null;
        foreach (Material m in mats)
        {
            if (m == null || m.shader == null || m.shader.name != "Scatter/FoliageLit") continue;
            any ??= m;
            string n = m.name != null ? m.name.ToLowerInvariant() : "";
            if (n.Contains("canopy")) return m;
            if (nonBeard == null && !n.Contains("beard")) nonBeard = m;
        }
        return nonBeard ?? any;
    }

    // Conifer needles have no texture in this pack, so the cone is solid geometry rendered with FoliageLit set to
    // _ForceLeaf (leaf mask on everywhere) so it gets the leaf lighting + reads the baked AO in vtx.G — shaded like
    // foliage, not a flat block. _BaseColor carries the dark-green tint.
    static Material ConiferMat(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (!_coniferMats.TryGetValue(s, out Material m))
        {
            Shader sh = Shader.Find("Scatter/FoliageLit") ?? Shader.Find("Scatter/VertexColorLit");
            m = new Material(sh) { name = $"Gen {def.Name} needles" };
            // FoliageLit colors from _BaseMap (no _BaseColor), so paint a solid-green base; vtx.G still shades it.
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", SolidTex(def.LeafColor));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", def.LeafColor);
            if (m.HasProperty("_ForceLeaf")) m.SetFloat("_ForceLeaf", 1f);
            m.enableInstancing = true;
            _coniferMats[s] = m;
        }
        return m;
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Gen needle tint" };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static (Material, Material) MatsFor(TreeDefLibrary.TreeSpecies s, TreeDef def)
    {
        if (!_mats.TryGetValue(s, out (Material bark, Material foliage) pair))
        {
            pair = (Mat($"Gen {def.Name} bark", def.BarkColor), Mat($"Gen {def.Name} foliage", def.LeafColor));
            _mats[s] = pair;
        }
        return pair;
    }

    static float[] Trim(float[] dist, int n)
    {
        var r = new float[Mathf.Max(1, n)];
        for (int i = 0; i < r.Length; i++) r[i] = dist[Mathf.Min(i, dist.Length - 1)];
        return r;
    }

    static Material Mat(string name, Color c)
    {
        Shader sh = Shader.Find("Scatter/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.enableInstancing = true;
        return m;
    }
}
