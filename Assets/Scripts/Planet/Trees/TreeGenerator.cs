using UnityEngine;

// Top-level tree generator (plan 006): one seeded TreeDef -> a GeneratedTree bundle (bark + foliage meshes,
// cut-set, anchors, gameplay metadata). Hard-wired stage order (structure -> mesh -> leaves -> cut-set), no
// node graph. Cut-set carving + LODs are filled by later stages; T2 produces the standing bark + foliage.
public static class TreeGenerator
{
    public static GeneratedTree Generate(TreeDef def, int seed)
    {
        var tree = new GeneratedTree();
        TreeSkeleton sk = TreeStructureGenerator.Generate(def, seed);

        // LOD0/LOD1 differ mainly in bark ring sides; foliage stays full (leaves thinned by LOD read as bare
        // trunks). The far billboard is the scatter impostor, baked from LOD0, so the mesh LODs never need to
        // shed the canopy — the impostor is the perf tier.
        tree.BarkLods = new[] { TreeTubeMesher.Build(sk, 0), TreeTubeMesher.Build(sk, 2) };
        tree.FoliageLods = def.FoliageStyle == FoliageStyle.ConiferCone
            ? new[]
            {
                TreeLeafMesher.BuildConiferCone(sk, seed, 1f, def.ConeTiers, 11, def.ConeBaseFrac, def.ConeRadiusFrac, def.ConeDroop),
                TreeLeafMesher.BuildConiferCone(sk, seed, 1f, Mathf.Max(4, def.ConeTiers * 2 / 3), 8, def.ConeBaseFrac, def.ConeRadiusFrac, def.ConeDroop),
            }
            : new[] { TreeLeafMesher.Build(sk, 1f, 1), TreeLeafMesher.Build(sk, 1.15f, 1) };

        // Accent tiers (blooms) build into their own mesh so they can carry a second material. Left null when
        // no tier asked for it, which is every species today except the flowering plants.
        if (def.FoliageStyle != FoliageStyle.ConiferCone && HasAccent(def))
        {
            Mesh accent = TreeLeafMesher.Build(sk, 1f, 1, accent: true);
            if (accent != null && accent.vertexCount > 0) tree.AccentLods = new[] { accent };
            else if (accent != null) Object.DestroyImmediate(accent);
        }

        tree.Height = sk.Height;
        tree.TrunkBaseGirth = sk.TrunkBaseGirth;

        int n = sk.Sprouts.Count;
        tree.LeafAnchors = new Vector3[n];
        for (int i = 0; i < n; i++) tree.LeafAnchors[i] = sk.Sprouts[i].Position;

        // Rough gameplay metadata (tune later): a chop fells the tree low on the trunk; HP + wood scale with
        // trunk size (a sapling is thin -> ~1 HP, little wood; an old tree is thick -> more hits, more wood).
        tree.ChopFraction = 0.14f;
        tree.ChopHp = Mathf.Max(1, Mathf.RoundToInt(sk.TrunkBaseGirth * 12f));
        tree.WoodYield = Mathf.Max(1, Mathf.RoundToInt(sk.Height * sk.TrunkBaseGirth * 4f));

        // Cut-set (stump + log) — carved from the trunk skeleton.
        TreeCutSet.Carve(sk, tree.ChopFraction, out tree.Stump, out tree.Log);

        return tree;
    }

    // Cheap pre-check: building an accent mesh walks every sprout, so skip it entirely for the species that
    // declare no accent tier, which is all of them except the flowering plants.
    static bool HasAccent(TreeDef def)
    {
        if (def.Levels == null) return false;
        foreach (LevelRule r in def.Levels)
            if (r != null && r.IsLeaf && r.AccentLeaf) return true;
        return false;
    }
}
