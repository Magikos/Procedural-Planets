using System;
using System.Text;
using UnityEngine;

// Code-first tree definitions (plan 006). One TreeDef per species; age is a parameter. The per-biome species
// set lives here (Species + biome map). Shape params are stylized-toon guesses tuned by eye via `tree.gallery`;
// MaxHeight is NOT a guess — it is the real mature height in metres from
// docs/research/tree-references/highest-trees.jpg and species norms, against a 2 m character.
// Trunk always grows +Y (Curve leans it); leaves attach to their ParentLevel tier.
public static class TreeDefLibrary
{
    public enum TreeSpecies { Broadleaf, Conifer, Pine, Birch, Palm, Acacia, Shrub, Willow, Cypress, Cedar, Poplar, Fern }

    public static readonly TreeSpecies[] AllSpecies =
    {
        TreeSpecies.Broadleaf, TreeSpecies.Conifer, TreeSpecies.Pine, TreeSpecies.Birch, TreeSpecies.Palm,
        TreeSpecies.Acacia, TreeSpecies.Shrub, TreeSpecies.Willow, TreeSpecies.Cypress, TreeSpecies.Cedar,
        TreeSpecies.Poplar, TreeSpecies.Fern,
    };

    public static TreeDef Species(TreeSpecies s, float age = 1f) => s switch
    {
        TreeSpecies.Conifer => Conifer(age),
        TreeSpecies.Pine => Pine(age),
        TreeSpecies.Birch => Birch(age),
        TreeSpecies.Palm => Palm(age),
        TreeSpecies.Acacia => Acacia(age),
        TreeSpecies.Shrub => Shrub(age),
        TreeSpecies.Willow => Willow(age),
        TreeSpecies.Cypress => Cypress(age),
        TreeSpecies.Cedar => Cedar(age),
        TreeSpecies.Poplar => Poplar(age),
        TreeSpecies.Fern => Fern(age),
        _ => Broadleaf(age),
    };

    public static bool TryParseSpecies(string name, out TreeSpecies s) =>
        Enum.TryParse(name, true, out s);

    // Kill any species: bare branches (the generator skips leaf tiers), bleached grey bark, and a shorter,
    // gnarlier frame. Keeps the species' own structure, so a dead oak still reads as an oak.
    public static TreeDef AsDead(TreeDef def)
    {
        def.Dead = true;
        def.MaxHeight *= 0.8f;
        def.BarkColor = new Color(0.44f, 0.40f, 0.35f);
        def.NeedleFoliage = false;
        def.FoliageStyle = FoliageStyle.LeafCards; // never build a conifer cone for a dead trunk
        return def;
    }

    public static TreeDef DeadSpecies(TreeSpecies s, float age = 1f) => AsDead(Species(s, age));

    // Primary tree species per biome. Biomes not listed grow no trees (ocean/cave/water/ice-bog/lake).
    public static bool HasTree(BiomeType biome, out TreeSpecies s)
    {
        switch (biome)
        {
            case BiomeType.Forest:
            case BiomeType.Grassland: s = TreeSpecies.Broadleaf; return true;
            case BiomeType.Swamp: s = TreeSpecies.Willow; return true;
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

    // A biome grows a SET of species, not one. One species per biome made every tropical region a palm
    // monoculture and left Cypress/Cedar/Poplar/Fern unreachable — generated correctly and placed nowhere.
    // The prototype name picks within the set (stably hashed by the caller), so a biome's several tree
    // prototypes become several species instead of the same tree repeated.
    public static TreeSpecies[] SpeciesSet(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.Forest: return new[] { TreeSpecies.Broadleaf, TreeSpecies.Conifer, TreeSpecies.Birch, TreeSpecies.Poplar };
            case BiomeType.Grassland: return new[] { TreeSpecies.Broadleaf, TreeSpecies.Poplar, TreeSpecies.Birch };
            case BiomeType.Swamp: return new[] { TreeSpecies.Willow, TreeSpecies.Broadleaf };
            case BiomeType.Taiga: return new[] { TreeSpecies.Conifer, TreeSpecies.Cedar, TreeSpecies.Pine };
            // Snow is fir/spruce country. Cedar lives in Taiga instead — it is not a snow-line tree.
            case BiomeType.Snow: return new[] { TreeSpecies.Conifer, TreeSpecies.Pine };
            // A frozen bog: stunted, mostly dead. Deliberately NO Conifer — the IceBog prototype is a "Dead
            // Tree", and a dead Conifer is a bare tapered spike, because the cone species carries no branch
            // tiers to keep when its foliage is stripped. Broadleaf and Shrub still have limbs as snags.
            case BiomeType.IceBog: return new[] { TreeSpecies.Shrub, TreeSpecies.Broadleaf };
            case BiomeType.Steppe: return new[] { TreeSpecies.Pine, TreeSpecies.Cypress };
            case BiomeType.Mountain: return new[] { TreeSpecies.Pine, TreeSpecies.Cypress, TreeSpecies.Conifer };
            case BiomeType.Tropical: return new[] { TreeSpecies.Palm, TreeSpecies.Broadleaf };
            case BiomeType.Beach: return new[] { TreeSpecies.Palm };
            case BiomeType.Savanna: return new[] { TreeSpecies.Acacia, TreeSpecies.Shrub };
            case BiomeType.Scrub: return new[] { TreeSpecies.Acacia, TreeSpecies.Cypress, TreeSpecies.Shrub };
            case BiomeType.Desert:
            case BiomeType.Tundra: return new[] { TreeSpecies.Shrub };
            default: return System.Array.Empty<TreeSpecies>();
        }
    }

    // Pick by the prototype's ORDINAL within its biome, not by hashing its name: with only one or two tree
    // prototypes per biome a hash can miss the primary species entirely (Swamp drew Broadleaf instead of
    // Willow, Savanna drew Shrub instead of Acacia). Ordinal 0 always gets the set's first entry, so every
    // biome keeps the species it had, and the alternates only appear where a biome has extra prototypes.
    public static TreeSpecies SpeciesForPrototype(BiomeType biome, int ordinalInBiome)
    {
        TreeSpecies[] set = SpeciesSet(biome);
        if (set.Length == 0) return TreeSpecies.Broadleaf;
        return set[((ordinalInBiome % set.Length) + set.Length) % set.Length];
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
        Name = "Broadleaf", MaxHeight = 32f, GlobalScale = 1.9f, Age = Mathf.Clamp01(age), // English oak ~35 m
        BarkColor = new Color(0.35f, 0.24f, 0.14f), LeafColor = new Color(0.20f, 0.40f, 0.15f),
        // Thin tip + chunky primaries: the trunk gives its girth up to a few heavy limbs (a fork), instead of
        // running full-width to the top with twigs poking out of it.
        TrunkTipScale = 0.18f, RootFlare = 0.35f,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(4f, 5.5f), RadialSides = 6, Curve = 6f, Noise = 0.05f },
            // Round/spreading crown (tree-forms-urban-forestry.jpg): primaries start low on the trunk and reach
            // out nearly as far as the tree is tall, so the crown is wide and starts around a third up — not a
            // narrow tuft perched on a bare pole.
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(5, 8), ChildrenPerNode = 1,
                Range = new Vector2(0.3f, 0.92f), ParallelAlign = 0.74f, GravityAlign = new Vector2(0.32f, 0.06f),
                Length = new Vector2(4.3f, 2.4f), GirthScale = 0.62f, RadialSides = 4, Curve = 16f, Noise = 0.1f,
            },
            new LevelRule
            {
                Label = "secondary", ParentLevel = 1, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.25f, 0.95f), ParallelAlign = 0.6f, GravityAlign = new Vector2(0.35f, 0.12f),
                Length = new Vector2(1.7f, 1.0f), GirthScale = 0.5f, RadialSides = 3, Curve = 18f, Noise = 0.12f,
            },
            new LevelRule
            {
                Label = "leaves_outer", ParentLevel = 2, IsLeaf = true, Frequency = new Vector2(4, 6),
                Range = new Vector2(0.1f, 1f), LeafSize = 0.95f, LeafGroup = 0,
            },
            new LevelRule
            {
                Label = "leaves_inner", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(3, 5),
                Range = new Vector2(0.4f, 1f), LeafSize = 1.15f, LeafGroup = 0,
            },
        },
    };

    // Tall straight trunk under a solid drooping fir cone (parametric foliage; no needle texture in this pack).
    public static TreeDef Conifer(float age = 1f) => new TreeDef
    {
        Name = "Conifer", MaxHeight = 42f, GlobalScale = 1.6f, Age = Mathf.Clamp01(age), // spruce/noble fir 40-50 m
        BarkColor = new Color(0.26f, 0.19f, 0.13f), LeafColor = new Color(0.12f, 0.30f, 0.18f),
        FoliageStyle = FoliageStyle.ConiferCone, TrunkTipScale = 0.05f, NeedleFoliage = true, // spire hidden by the cone
        TrunkGirthScale = 0.55f, // its trunk runs the full height, so length-proportional girth would be a column
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 11f), RadialSides = 6, Curve = 2f, Noise = 0.02f },
        },
    };

    // Open pine: tall bare trunk, sparse upward branches only in the top half, each ending in a spiky needle tuft.
    public static TreeDef Pine(float age = 1f) => new TreeDef
    {
        Name = "Pine", MaxHeight = 38f, GlobalScale = 1.5f, Age = Mathf.Clamp01(age), // ponderosa/scots 35-40 m
        BarkColor = new Color(0.30f, 0.22f, 0.15f), LeafColor = new Color(0.16f, 0.34f, 0.20f),
        TrunkTipScale = 0.06f, NeedleFoliage = true, TrunkGirthScale = 0.6f,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(10f, 14f), RadialSides = 6, Curve = 3f, Noise = 0.03f },
            new LevelRule
            {
                Label = "branches", ParentLevel = 0, Frequency = new Vector2(5, 8), ChildrenPerNode = 2,
                Range = new Vector2(0.5f, 0.96f), ParallelAlign = 0.55f, GravityAlign = new Vector2(0.25f, -0.05f),
                Length = new Vector2(2.6f, 1.3f), GirthScale = 0.3f, RadialSides = 3, Curve = 10f, Noise = 0.08f,
            },
            // FEW tufts, only at the branch ends. Sky between the puffs is the whole point — enough of them, or
            // large enough, and they merge into a solid mass indistinguishable from the Conifer cone.
            new LevelRule
            {
                Label = "tufts", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(1, 3),
                Range = new Vector2(0.72f, 1f), LeafSize = 1.5f, LeafGroup = 2,
            },
        },
    };

    // Slender pale trunk, sparse high canopy of upward branches.
    public static TreeDef Birch(float age = 1f) => new TreeDef
    {
        Name = "Birch", MaxHeight = 22f, GlobalScale = 1.4f, Age = Mathf.Clamp01(age), // silver birch ~20-25 m
        BarkColor = new Color(0.80f, 0.80f, 0.76f), LeafColor = new Color(0.38f, 0.54f, 0.20f),
        TrunkGirthScale = 0.35f, RootFlare = 0.15f, // a birch is slender and barely buttressed — that IS the species
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(7f, 9.5f), RadialSides = 5, Curve = 4f, Noise = 0.03f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(6, 9), ChildrenPerNode = 1,
                Range = new Vector2(0.42f, 0.95f), ParallelAlign = 0.58f, GravityAlign = new Vector2(0.3f, 0.3f),
                Length = new Vector2(3.2f, 1.9f), GirthScale = 0.42f, RadialSides = 3, Curve = 16f, Noise = 0.09f,
            },
            // Open but not bare: a birch crown is see-through, yet it is still a crown — too few branches and it
            // reads as a stripe of leaves stuck to one side of a pole.
            new LevelRule
            {
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(5, 8),
                Range = new Vector2(0.3f, 1f), LeafSize = 1f, LeafGroup = 0,
            },
        },
    };

    // Bare leaning trunk, a crown of big fronds clustered at the very top.
    public static TreeDef Palm(float age = 1f) => new TreeDef
    {
        Name = "Palm", MaxHeight = 20f, GlobalScale = 1.4f, Age = Mathf.Clamp01(age), // coconut palm 15-25 m
        BarkColor = new Color(0.42f, 0.31f, 0.18f), LeafColor = new Color(0.24f, 0.44f, 0.20f),
        RootFlare = 0.18f, TrunkGirthScale = 0.3f, // slim, near-constant stem with a slight base swell
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 11f), RadialSides = 6, Curve = 12f, Noise = 0.06f },
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
        Name = "Acacia", MaxHeight = 14f, GlobalScale = 1.5f, Age = Mathf.Clamp01(age), // umbrella thorn 10-15 m
        BarkColor = new Color(0.36f, 0.26f, 0.16f), LeafColor = new Color(0.36f, 0.46f, 0.22f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(4.5f, 6.5f), RadialSides = 6, Curve = 8f, Noise = 0.05f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(4, 6), ChildrenPerNode = 1,
                Range = new Vector2(0.45f, 0.85f), ParallelAlign = 0.84f, GravityAlign = new Vector2(0.3f, -0.02f),
                Length = new Vector2(4.6f, 3.4f), GirthScale = 0.55f, RadialSides = 4, Curve = 10f, Noise = 0.06f,
            },
            // Foliage only at the outer ends of long near-horizontal limbs = the flat savanna umbrella. Leaves
            // spread along the branch would fill the space under it and lose the shape.
            new LevelRule
            {
                Label = "canopy", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(4, 6),
                Range = new Vector2(0.72f, 1f), LeafSize = 1.7f, LeafGroup = 0,
            },
        },
    };

    // Low multi-stem bush: short trunk, many low branches up-and-out, dense small leaves.
    public static TreeDef Shrub(float age = 1f) => new TreeDef
    {
        Name = "Shrub", MaxHeight = 2.6f, GlobalScale = 0.9f, Age = Mathf.Clamp01(age), // waist-to-head high bush
        BarkColor = new Color(0.34f, 0.26f, 0.16f), LeafColor = new Color(0.26f, 0.40f, 0.16f),
        RootFlare = 0.12f,
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

    // Columnar cypress / juniper: a narrow dark pillar. Same cone mesh as the fir with a small radius, foliage
    // hugging the trunk from low down to a rounded top — width ~15% of height.
    public static TreeDef Cypress(float age = 1f) => new TreeDef
    {
        Name = "Cypress", MaxHeight = 25f, GlobalScale = 1.5f, Age = Mathf.Clamp01(age), // italian cypress 20-30 m
        BarkColor = new Color(0.30f, 0.24f, 0.18f), LeafColor = new Color(0.13f, 0.26f, 0.16f),
        FoliageStyle = FoliageStyle.ConiferCone, TrunkTipScale = 0.05f, NeedleFoliage = true,
        TrunkGirthScale = 0.4f,
        ConeBaseFrac = 0.16f, ConeRadiusFrac = 0.1f, ConeDroop = 0.35f, ConeTiers = 26,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(9f, 12f), RadialSides = 5, Curve = 2f, Noise = 0.02f },
        },
    };

    // Cedar: broad flat horizontal tiers, wider and far flatter than the fir's drooping skirts — stacked plates.
    public static TreeDef Cedar(float age = 1f) => new TreeDef
    {
        Name = "Cedar", MaxHeight = 30f, GlobalScale = 1.6f, Age = Mathf.Clamp01(age), // western red cedar 30-60 m
        BarkColor = new Color(0.34f, 0.23f, 0.17f), LeafColor = new Color(0.17f, 0.32f, 0.19f),
        FoliageStyle = FoliageStyle.ConiferCone, TrunkTipScale = 0.08f, NeedleFoliage = true,
        TrunkGirthScale = 0.6f,
        // Few tiers is what separates cedar from fir: at 7+ the plates overlap back into one smooth cone.
        ConeBaseFrac = 0.34f, ConeRadiusFrac = 0.38f, ConeDroop = 0.12f, ConeTiers = 5,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(8f, 10f), RadialSides = 6, Curve = 3f, Noise = 0.03f },
        },
    };

    // Lombardy poplar: a narrow DECIDUOUS column — leaf clumps, not needles. Branches stay short and sweep
    // steeply upward so the crown hugs the trunk instead of spreading.
    public static TreeDef Poplar(float age = 1f) => new TreeDef
    {
        Name = "Poplar", MaxHeight = 28f, GlobalScale = 1.5f, Age = Mathf.Clamp01(age), // lombardy poplar 25-30 m
        BarkColor = new Color(0.40f, 0.35f, 0.27f), LeafColor = new Color(0.33f, 0.50f, 0.20f),
        TrunkGirthScale = 0.42f, TrunkTipScale = 0.12f,
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(9f, 12f), RadialSides = 5, Curve = 3f, Noise = 0.03f },
            new LevelRule
            {
                Label = "primary", ParentLevel = 0, Frequency = new Vector2(12, 16), ChildrenPerNode = 1,
                Range = new Vector2(0.12f, 0.97f), ParallelAlign = 0.22f, GravityAlign = new Vector2(0.75f, 0.85f),
                Length = new Vector2(2.3f, 1.6f), GirthScale = 0.3f, RadialSides = 3, Curve = 10f, Noise = 0.06f,
            },
            new LevelRule
            {
                Label = "leaves", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(4, 6),
                Range = new Vector2(0.15f, 1f), LeafSize = 0.8f, LeafGroup = 0,
            },
        },
    };

    // Fern: no trunk to speak of — a crown of arching blade fronds straight off the ground. The same level
    // machinery as a tree, just with the "trunk" reduced to a stub the fronds radiate from.
    public static TreeDef Fern(float age = 1f) => new TreeDef
    {
        // MaxHeight normalises the STEM (the height solve measures branch tips, and a fern has none), so the
        // fronds arch above it — a 0.75 m stem plus ~0.6 m blades lands the plant around knee height.
        Name = "Fern", MaxHeight = 0.72f, GlobalScale = 1f, Age = Mathf.Clamp01(age),
        BarkColor = new Color(0.30f, 0.32f, 0.20f), LeafColor = new Color(0.26f, 0.44f, 0.18f),
        TrunkGirthScale = 0.5f, TrunkTipScale = 0.4f, RootFlare = 0.1f,
        Levels = new[]
        {
            new LevelRule { Label = "stem", ParentLevel = -1, Length = new Vector2(0.5f, 0.8f), RadialSides = 4, Curve = 4f, Noise = 0.04f },
            new LevelRule
            {
                Label = "fronds", ParentLevel = 0, IsLeaf = true, Frequency = new Vector2(14, 20),
                Range = new Vector2(0.05f, 1f), LeafSize = 0.78f, LeafGroup = 4,
            },
        },
    };

    // Weeping willow: SHORT thick trunk under a BROAD dome — a real willow is as wide as it is tall or wider
    // (see the reference photos Bryan supplied) — with long strands cascading from the whole spread, not a
    // narrow column. Width comes from long near-horizontal branches, not from more of them.
    public static TreeDef Willow(float age = 1f) => new TreeDef
    {
        Name = "Willow", MaxHeight = 18f, GlobalScale = 1.6f, Age = Mathf.Clamp01(age), // weeping willow 15-20 m
        BarkColor = new Color(0.32f, 0.26f, 0.18f), LeafColor = new Color(0.42f, 0.56f, 0.24f),
        Levels = new[]
        {
            new LevelRule { Label = "trunk", ParentLevel = -1, Length = new Vector2(2.6f, 3.6f), RadialSides = 6, Curve = 8f, Noise = 0.05f },
            new LevelRule
            {
                Label = "branches", ParentLevel = 0, Frequency = new Vector2(7, 10), ChildrenPerNode = 1,
                Range = new Vector2(0.25f, 0.95f), ParallelAlign = 0.86f, GravityAlign = new Vector2(0.42f, 0.12f),
                Length = new Vector2(5.2f, 3.6f), GirthScale = 0.5f, RadialSides = 4, Curve = 26f, Noise = 0.1f,
            },
            new LevelRule
            {
                Label = "strands", ParentLevel = 1, IsLeaf = true, Frequency = new Vector2(16, 22),
                Range = new Vector2(0.2f, 1f), LeafSize = 3.4f, LeafGroup = 3,
            },
        },
    };
}
