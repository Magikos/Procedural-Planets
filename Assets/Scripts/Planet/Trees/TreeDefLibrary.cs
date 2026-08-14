using System;
using System.Text;
using UnityEngine;

// Code-first tree definitions (plan 006). One TreeDef per species; age is a parameter. The per-biome species
// set lives here (Species + biome map). Params are stylized-toon guesses tuned by eye via `tree.gallery`, not
// physically derived. Trunk always grows +Y (Curve leans it); leaves attach to their ParentLevel tier.
public static class TreeDefLibrary
{
    public enum TreeSpecies { Broadleaf, Conifer, Birch, Palm, Acacia, Shrub }

    public static readonly TreeSpecies[] AllSpecies =
        { TreeSpecies.Broadleaf, TreeSpecies.Conifer, TreeSpecies.Birch, TreeSpecies.Palm, TreeSpecies.Acacia, TreeSpecies.Shrub };

    public static TreeDef Species(TreeSpecies s, float age = 1f) => s switch
    {
        TreeSpecies.Conifer => Conifer(age),
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
            case BiomeType.Snow:
            case BiomeType.Mountain: s = TreeSpecies.Conifer; return true;
            case BiomeType.Tropical:
            case BiomeType.Beach: s = TreeSpecies.Palm; return true;
            case BiomeType.Savanna:
            case BiomeType.Scrub: s = TreeSpecies.Acacia; return true;
            case BiomeType.Desert:
            case BiomeType.Steppe:
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

    public static TreeDef Broadleaf(float age = 1f) => new TreeDef
    {
        Name = "Broadleaf", GlobalScale = 1f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.35f, 0.24f, 0.14f), LeafColor = new Color(0.28f, 0.46f, 0.18f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(6f, 8f), RadialSides = 6, Curve = 6f, Noise = 0.04f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.35f, 0.92f), ParallelAlign = 0.5f, GravityAlign = new Vector2(0.35f, -0.12f),
                Length = new Vector2(3.2f, 1.6f), GirthScale = 0.5f, RadialSides = 4, Curve = 12f, Noise = 0.08f,
            },
            new LevelRule
            {
                Label = "secondary", ParentLevel = 1, Frequency = new Vector2(3, 5), ChildrenPerNode = 1,
                Range = new Vector2(0.3f, 0.95f), ParallelAlign = 0.55f, GravityAlign = new Vector2(0.3f, -0.05f),
                Length = new Vector2(1.5f, 0.8f), GirthScale = 0.5f, RadialSides = 3, Curve = 14f, Noise = 0.1f,
            },
            new LevelRule
            {
                Label = "leaves", ParentLevel = 2, IsLeaf = true, Frequency = new Vector2(4, 7),
                Range = new Vector2(0.4f, 1f), LeafSize = 0.6f, LeafGroup = 0,
            },
        },
    };

    // Tall straight trunk, whorled branches longest at the base and short at the crown (cone), dense needles.
    public static TreeDef Conifer(float age = 1f) => new TreeDef
    {
        Name = "Conifer", GlobalScale = 1.1f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.26f, 0.19f, 0.13f), LeafColor = new Color(0.13f, 0.30f, 0.19f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(9f, 12f), RadialSides = 6, Curve = 2f, Noise = 0.02f },
            new LevelRule
            {
                Label = "boughs", ParentLevel = 0, Frequency = new Vector2(8, 12), ChildrenPerNode = 4,
                Range = new Vector2(0.12f, 0.96f), ParallelAlign = 0.72f, GravityAlign = new Vector2(0.0f, -0.28f),
                Length = new Vector2(3.0f, 0.5f), GirthScale = 0.35f, RadialSides = 3, Curve = 8f, Noise = 0.05f,
            },
            new LevelRule
            {
                Label = "needles", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(6, 10),
                Range = new Vector2(0.15f, 1f), LeafSize = 0.4f, LeafGroup = 0,
            },
        },
    };

    // Slender pale trunk, sparse high canopy of upward branches.
    public static TreeDef Birch(float age = 1f) => new TreeDef
    {
        Name = "Birch", GlobalScale = 1f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.80f, 0.80f, 0.76f), LeafColor = new Color(0.45f, 0.60f, 0.22f),
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
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(4, 7),
                Range = new Vector2(0.35f, 1f), LeafSize = 0.5f, LeafGroup = 0,
            },
        },
    };

    // Bare leaning trunk, a crown of big fronds clustered at the very top.
    public static TreeDef Palm(float age = 1f) => new TreeDef
    {
        Name = "Palm", GlobalScale = 1.05f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.42f, 0.31f, 0.18f), LeafColor = new Color(0.24f, 0.44f, 0.20f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 11f), RadialSides = 6, Curve = 22f, Noise = 0.05f },
            new LevelRule
            {
                Label = "fronds", ParentLevel = 0, IsLeaf = true, Frequency = new Vector2(7, 11),
                Range = new Vector2(0.9f, 1f), LeafSize = 1.7f, LeafGroup = 1,
            },
        },
    };

    // Short trunk, branches spreading wide up-and-out into a flat umbrella of dry canopy.
    public static TreeDef Acacia(float age = 1f) => new TreeDef
    {
        Name = "Acacia", GlobalScale = 1f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.36f, 0.26f, 0.16f), LeafColor = new Color(0.44f, 0.52f, 0.26f),
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
                Label = "canopy", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(5, 8),
                Range = new Vector2(0.65f, 1f), LeafSize = 0.7f, LeafGroup = 0,
            },
        },
    };

    // Low multi-stem bush: short trunk, many low branches up-and-out, dense small leaves.
    public static TreeDef Shrub(float age = 1f) => new TreeDef
    {
        Name = "Shrub", GlobalScale = 0.6f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.34f, 0.26f, 0.16f), LeafColor = new Color(0.30f, 0.42f, 0.18f),
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
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(5, 8),
                Range = new Vector2(0.2f, 1f), LeafSize = 0.5f, LeafGroup = 0,
            },
        },
    };
}
