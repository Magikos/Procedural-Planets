using System;
using UnityEngine;

// Replaces the scatter library's TREE prototypes (Interaction == Chop) with generated trees at build time
// (plan 006). Default OFF so the normal world is untouched; `tree.inject on` + regenerate turns it on. Heavily
// guarded — any failure (generation throws, empty mesh) keeps the original Synty prototype, and a top-level
// catch keeps scatter working no matter what. Age varies per tree TYPE (seeded by name) so the world shows a
// mix of ages; true per-INSTANCE age variety needs per-instance scatter mesh variants (a later change).
public static class TreeInjection
{
    public static bool Enabled = false;

    static Material _bark;
    static Material _foliage;

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

    static bool IsTree(ScatterPrototypeDto p) =>
        p != null && p.Interaction == ScatterInteraction.Chop && p.Parts != null && p.Parts.Length > 0;

    static ScatterPrototypeDto TryReplace(ScatterPrototypeDto p)
    {
        try
        {
            int seed = Mathf.Abs((p.DisplayName ?? "tree").GetHashCode()) % 900000 + 1;
            float age = 0.4f + (seed % 100) / 100f * 0.6f; // 0.4..1.0 per type -> age variety across the world
            TreeDef def = TreeDefLibrary.SampleBroadleaf(age);
            GeneratedTree t = TreeGenerator.Generate(def, seed);
            if (t.Bark == null || t.Bark.vertexCount == 0) return null; // keep Synty

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 50f) cull = 300f;
            float[] dist = { cull * 0.4f, cull };

            var barkPart = new ScatterPartDto(BarkMat(), t.BarkLods, Trim(dist, t.BarkLods.Length), true, true);
            var foliagePart = new ScatterPartDto(FoliageMat(), t.FoliageLods, Trim(dist, t.FoliageLods.Length), true, false);

            // Keep everything else about the prototype (slot/biome/placement); swap the parts + stump, and clear
            // the Synty impostor atlas so the scatter renderer bakes a fresh impostor from the generated LOD0.
            return p with
            {
                Parts = new[] { barkPart, foliagePart },
                StumpMesh = t.Stump,
                StumpMaterial = BarkMat(),
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

    static float[] Trim(float[] dist, int n)
    {
        var r = new float[Mathf.Max(1, n)];
        for (int i = 0; i < r.Length; i++) r[i] = dist[Mathf.Min(i, dist.Length - 1)];
        return r;
    }

    static Material BarkMat() { if (_bark == null) _bark = Mat("Gen tree bark", new Color(0.35f, 0.24f, 0.14f)); return _bark; }
    static Material FoliageMat() { if (_foliage == null) _foliage = Mat("Gen tree foliage", new Color(0.24f, 0.44f, 0.16f)); return _foliage; }

    static Material Mat(string name, Color c)
    {
        Shader sh = Shader.Find("Planet/PropLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.enableInstancing = true;
        return m;
    }
}
