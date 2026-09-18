using UnityEngine;

// Builds standing meshes and harvest parts from one deterministic skeleton.
public static class TreeGenerator
{
    public static GeneratedTree Generate(TreeDef def, int seed, bool harvestParts = true)
    {
        if (def == null) throw new System.ArgumentNullException(nameof(def));
        if (!float.IsFinite(def.MaxHeight) || def.MaxHeight <= 0f || !float.IsFinite(def.Age))
            throw new System.ArgumentException("Tree height must be positive and age must be finite.", nameof(def));
        var tree = new GeneratedTree { SpeciesName = def.Name };
        TreeSkeleton sk = TreeStructureGenerator.Generate(def, seed);

        // Keep the standing silhouette until the scatter impostor takes over. Reducing bark sides changes
        // trunk shading; rebuilding conifer tiers moves the foliage instead of simplifying the same surface.
        tree.BarkLods = new[] { TreeTubeMesher.Build(sk, 0) };
        tree.FoliageLods = !def.Dead && def.FoliageStyle == FoliageStyle.ConiferCone
            ? new[] { TreeLeafMesher.BuildConiferCone(sk, seed, 1f, def.ConeTiers, 11,
                def.ConeBaseFrac, def.ConeRadiusFrac, def.ConeDroop) }
            : new[] { TreeLeafMesher.Build(sk, 1f, 1) };

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

        if (harvestParts) TreeHarvestGeometry.Build(sk, tree);
        if (harvestParts && !def.Dead && def.FoliageStyle == FoliageStyle.ConiferCone)
            TreeHarvestGeometry.SplitConiferFoliage(tree);
        if (harvestParts) TreeHarvestGeometry.BuildSupport(tree);

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
