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
            ? new[] { TreeLeafMesher.BuildConiferCone(sk, seed, 1f), TreeLeafMesher.BuildConiferCone(sk, seed, 1f, 12, 8) }
            : new[] { TreeLeafMesher.Build(sk, 1f, 1), TreeLeafMesher.Build(sk, 1.15f, 1) };

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
}
