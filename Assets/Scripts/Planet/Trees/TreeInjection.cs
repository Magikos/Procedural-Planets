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

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
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
            float age = 0.4f + (seed % 100) / 100f * 0.6f; // 0.4..1.0 per type -> age variety across the world
            TreeDef def = TreeDefLibrary.Species(species, age);
            GeneratedTree t = TreeGenerator.Generate(def, seed);
            if (t.Bark == null || t.Bark.vertexCount == 0) return null; // keep Synty

            (Material bark, Material foliage) = MatsFor(species, def);

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 50f) cull = 300f;
            float[] dist = { cull * 0.4f, cull };

            var barkPart = new ScatterPartDto(bark, t.BarkLods, Trim(dist, t.BarkLods.Length), true, true);
            var foliagePart = new ScatterPartDto(foliage, t.FoliageLods, Trim(dist, t.FoliageLods.Length), true, false);

            // Keep everything else about the prototype (slot/biome/placement); swap the parts + stump, and clear
            // the Synty impostor atlas so the scatter renderer bakes a fresh impostor from the generated LOD0.
            return p with
            {
                Parts = new[] { barkPart, foliagePart },
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

    // Bark + foliage materials per species (cached). The scatter GPU-indirect shader (procedural:setup) feeds
    // the per-instance transform; Scatter/VertexColorLit tints albedo by _BaseColor, so the species palette
    // lives on the material (the shader ignores mesh vertex color for albedo).
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
