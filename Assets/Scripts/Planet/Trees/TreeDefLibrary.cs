using System;
using System.Text;
using UnityEngine;

// Code-first tree definitions (plan 006). One TreeDef per species; age is a parameter. The per-biome species
// set lives here (Species + biome map). Params are stylized-toon guesses tuned by eye via `tree.gallery`, not
// physically derived. Trunk always grows +Y (Curve leans it); leaves attach to their ParentLevel tier.
public static class TreeDefLibrary
{
    public enum TreeSpecies { Broadleaf, Conifer, Pine, Birch, Palm, Acacia, Shrub }

    public static readonly TreeSpecies[] AllSpecies =
        { TreeSpecies.Broadleaf, TreeSpecies.Conifer, TreeSpecies.Pine, TreeSpecies.Birch, TreeSpecies.Palm, TreeSpecies.Acacia, TreeSpecies.Shrub };

    public static TreeDef Species(TreeSpecies s, float age = 1f) => s switch
    {
        TreeSpecies.Conifer => Conifer(age),
        TreeSpecies.Pine => Pine(age),
        TreeSpecies.Birch => Birch(age),
        TreeSpecies.Palm => Palm(age),
        TreeSpecies.Acacia => Acacia(age),
        TreeSpecies.Shrub => Shrub(age),
        _ => Broadleaf(age),
    };

    public static bool TryParseSpecies(string name, out TreeSpecies s) =>
        Enum.TryParse(name, true, out s);

    // Primary tree species per biome. Biomes not listed grow no trees (ocean/cave/water/ice-bog/lake).
    public static bool HasTree(BiomeType biome, out TreeSpecies s)
    {
        switch (biome)
        {
            case BiomeType.Forest:
            case BiomeType.Grassland:
            case BiomeType.Swamp: s = TreeSpecies.Broadleaf; return true;
            case BiomeType.Taiga:
            case BiomeType.Snow: s = TreeSpecies.Conifer; return true;   // dense fir/spruce cone
            case BiomeType.Steppe:
            case BiomeType.Mountain: s = TreeSpecies.Pine; return true;  // open tufted pine (dry / montane)
            case BiomeType.Tropical:
            case BiomeType.Beach: s = TreeSpecies.Palm; return true;
            case BiomeType.Savanna:
            case BiomeType.Scrub: s = TreeSpecies.Acacia; return true;
            case BiomeType.Desert:
            case BiomeType.Tundra: s = TreeSpecies.Shrub; return true;
            default: s = default; return false;
        }
    }

    public static string BiomeMapSummary()
    {
        var sb = new StringBuilder();
        foreach (BiomeType b in Enum.GetValues(typeof(BiomeType)))
            if (HasTree(b, out TreeSpecies s)) sb.AppendLine($"  {b} -> {s}");
        return sb.ToString();
    }

    // --- species ---

    public static TreeDef SampleBroadleaf(float age = 1f) => Broadleaf(age);

    // Lush rounded canopy (Synty-style): a short thick trunk under a dense dome of big overlapping leaf cards,
    // filled by two leaf tiers (outer on the twigs, inner on the primaries) so the crown reads solid, not spindly.
    public static TreeDef Broadleaf(float age = 1f) => new TreeDef
    {
        Name = "Broadleaf", GlobalScale = 1.6f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.35f, 0.24f, 0.14f), LeafColor = new Color(0.20f, 0.40f, 0.15f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(4f, 5.5f), RadialSides = 6, Curve = 6f, Noise = 0.05f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(5, 8), ChildrenPerNode = 1,
                Range = new Vector2(0.4f, 0.92f), ParallelAlign = 0.58f, GravityAlign = new Vector2(0.45f, 0.1f),
                Length = new Vector2(3.0f, 1.8f), GirthScale = 0.52f, RadialSides = 4, Curve = 16f, Noise = 0.1f,
            },
            new LevelRule
            {
                Label = "secondary", ParentLevel = 1, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.25f, 0.95f), ParallelAlign = 0.6f, GravityAlign = new Vector2(0.35f, 0.12f),
                Length = new Vector2(1.7f, 1.0f), GirthScale = 0.5f, RadialSides = 3, Curve = 18f, Noise = 0.12f,
            },
            new LevelRule
            {
                Label = "leaves_outer", ParentLevel = 2, IsLeaf = true, Frequency = new Vector2(3, 5),
                Range = new Vector2(0.1f, 1f), LeafSize = 0.9f, LeafGroup = 0,
            },
            new LevelRule
            {
                Label = "leaves_inner", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(2, 4),
                Range = new Vector2(0.4f, 1f), LeafSize = 1.1f, LeafGroup = 0,
            },
        },
    };

    // Tall straight trunk under a solid drooping fir cone (parametric foliage; no needle texture in this pack).
    public static TreeDef Conifer(float age = 1f) => new TreeDef
    {
        Name = "Conifer", GlobalScale = 1.6f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.26f, 0.19f, 0.13f), LeafColor = new Color(0.12f, 0.30f, 0.18f),
        FoliageStyle = FoliageStyle.ConiferCone, TrunkTipScale = 0.05f, NeedleFoliage = true, // spire hidden by the cone
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 11f), RadialSides = 6, Curve = 2f, Noise = 0.02f },
        },
    };

    // Open pine: tall bare trunk, sparse upward branches only in the top half, each ending in a spiky needle tuft.
    public static TreeDef Pine(float age = 1f) => new TreeDef
    {
        Name = "Pine", GlobalScale = 1.5f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.30f, 0.22f, 0.15f), LeafColor = new Color(0.16f, 0.34f, 0.20f),
        TrunkTipScale = 0.06f, NeedleFoliage = true,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(10f, 14f), RadialSides = 6, Curve = 3f, Noise = 0.03f },
            new LevelRule
            {
                Label = "branches", ParentLevel = 0, Frequency = new Vector2(6, 10), ChildrenPerNode = 2,
                Range = new Vector2(0.5f, 0.96f), ParallelAlign = 0.55f, GravityAlign = new Vector2(0.25f, -0.05f),
                Length = new Vector2(2.2f, 1.1f), GirthScale = 0.3f, RadialSides = 3, Curve = 10f, Noise = 0.08f,
            },
            new LevelRule
            {
                Label = "tufts", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(2, 4),
                Range = new Vector2(0.55f, 1f), LeafSize = 1.1f, LeafGroup = 2,
            },
        },
    };

    // Slender pale trunk, sparse high canopy of upward branches.
    public static TreeDef Birch(float age = 1f) => new TreeDef
    {
        Name = "Birch", GlobalScale = 1.4f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.80f, 0.80f, 0.76f), LeafColor = new Color(0.38f, 0.54f, 0.20f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(7f, 9.5f), RadialSides = 5, Curve = 4f, Noise = 0.03f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(3, 5), ChildrenPerNode = 1,
                Range = new Vector2(0.5f, 0.95f), ParallelAlign = 0.42f, GravityAlign = new Vector2(0.25f, 0.35f),
                Length = new Vector2(2.2f, 1.2f), GirthScale = 0.42f, RadialSides = 3, Curve = 16f, Noise = 0.09f,
            },
            new LevelRule
            {
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(3, 5),
                Range = new Vector2(0.35f, 1f), LeafSize = 0.8f, LeafGroup = 0,
            },
        },
    };

    // Bare leaning trunk, a crown of big fronds clustered at the very top.
    public static TreeDef Palm(float age = 1f) => new TreeDef
    {
        Name = "Palm", GlobalScale = 1.4f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.42f, 0.31f, 0.18f), LeafColor = new Color(0.24f, 0.44f, 0.20f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 11f), RadialSides = 6, Curve = 22f, Noise = 0.05f },
            new LevelRule
            {
                Label = "fronds", ParentLevel = 0, IsLeaf = true, Frequency = new Vector2(10, 14),
                Range = new Vector2(0.9f, 1f), LeafSize = 4.5f, LeafGroup = 1,
            },
        },
    };

    // Short trunk, branches spreading wide up-and-out into a flat umbrella of dry canopy.
    public static TreeDef Acacia(float age = 1f) => new TreeDef
    {
        Name = "Acacia", GlobalScale = 1.5f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.36f, 0.26f, 0.16f), LeafColor = new Color(0.36f, 0.46f, 0.22f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(4.5f, 6.5f), RadialSides = 6, Curve = 8f, Noise = 0.05f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.45f, 0.85f), ParallelAlign = 0.68f, GravityAlign = new Vector2(0.45f, 0.15f),
                Length = new Vector2(3.0f, 2.2f), GirthScale = 0.55f, RadialSides = 4, Curve = 10f, Noise = 0.06f,
            },
            new LevelRule
            {
                Label = "canopy", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(3, 5),
                Range = new Vector2(0.6f, 1f), LeafSize = 1.0f, LeafGroup = 0,
            },
        },
    };

    // Low multi-stem bush: short trunk, many low branches up-and-out, dense small leaves.
    public static TreeDef Shrub(float age = 1f) => new TreeDef
    {
        Name = "Shrub", GlobalScale = 0.9f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.34f, 0.26f, 0.16f), LeafColor = new Color(0.26f, 0.40f, 0.16f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(1.6f, 2.6f), RadialSides = 5, Curve = 8f, Noise = 0.06f },
            new LevelRule
            {
                Label = "stems", ParentLevel = 0, Frequency = new Vector2(4, 7), ChildrenPerNode = 1,
                Range = new Vector2(0.05f, 0.7f), ParallelAlign = 0.62f, GravityAlign = new Vector2(0.5f, 0.2f),
                Length = new Vector2(1.4f, 0.9f), GirthScale = 0.6f, RadialSides = 3, Curve = 14f, Noise = 0.12f,
            },
            new LevelRule
            {
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(3, 5),
                Range = new Vector2(0.2f, 1f), LeafSize = 0.65f, LeafGroup = 0,
            },
        },
    };
}
