using System;
using UnityEngine;

// Code-first tree definition (plan 006). A species is a TreeDef: an ordered list of level rules (trunk = level
// 0, then branch levels, then leaf levels) + a global age that scales the whole tree. One TreeDef + an age
// gives the sapling/young/adult/old stages. Deterministic per seed. This is authored in code, not a node graph.
// How the foliage mesh is built. LeafCards = textured leaf clumps / fronds (broadleaf, palm, etc.);
// ConiferCone = a solid low-poly fir cone (no leaf texture available for needles).
public enum FoliageStyle { LeafCards, ConiferCone }

[Serializable]
public sealed class TreeDef
{
    public string Name = "Tree";

    // Finished height in METRES at Age 1 (a mature old specimen), against a 2 m character. Calibrated to real
    // trees — docs/research/tree-references/highest-trees.jpg: human 1.8, English oak 35, Douglas fir 67. The
    // generator normalises the built skeleton to this, so Levels[].Length are relative proportions and this is
    // the one number that sets scale. Younger ages scale down it (see the age curve in TreeStructureGenerator).
    public float MaxHeight = 20f;

    // Proportion knob only — it shapes the skeleton before the height normalisation, so changing it alters the
    // trunk/branch ratio and the segment density, NOT the finished height.
    public float GlobalScale = 1f;

    [Range(0f, 1f)] public float Age = 1f; // 0 = sapling, 1 = old; scales height/girth/branch tiers/lean
    public LevelRule[] Levels = Array.Empty<LevelRule>();
    public FoliageStyle FoliageStyle = FoliageStyle.LeafCards;
    public float TrunkTipScale = 0.28f; // trunk tip girth as a fraction of base; low = tapers to a spire (conifer)
    public float RootFlare = 0.32f;     // extra girth at the very base (root buttress); 0 = a cylinder on the ground

    // Trunk thickness relative to trunk LENGTH. Species whose trunk runs the tree's whole height (conifers) need
    // this below 1 or they come out as columns: the same length-proportional girth that reads right on a broadleaf
    // — whose trunk is only ~60% of the tree, the rest being crown — is far too fat on a full-height trunk.
    public float TrunkGirthScale = 1f;

    // ConiferCone shape, as fractions of tree height. One mesh covers the whole conifer family: fir = the
    // defaults, cypress/juniper = a small ConeRadius (narrow column), cedar = wide radius + few tiers + almost
    // no droop (flat stacked plates). ConeBaseFrac is where the LOWEST skirt hangs from — it droops ConeDroop x
    // that tier's radius below it, and the bottom tier is the widest, so setting it too low buries the skirts in
    // the ground and hides the trunk entirely.
    public float ConeBaseFrac = 0.3f;
    public float ConeRadiusFrac = 0.26f;
    public float ConeDroop = 0.7f;
    public int ConeTiers = 18;
    public bool NeedleFoliage = false;  // solid dark-green needle material (fir cone / pine tufts) vs leaf texture

    // Dead standing tree: ANY species can wear this. Leaf tiers are skipped entirely (bare branches), the bark
    // greys out, and the branches gnarl — so a dead oak still has an oak's silhouette, which is the whole point
    // of it being a modifier rather than its own species.
    public bool Dead = false;

    // The species palette. Bark and leaf color travel with the definition so the gallery and the planet
    // materials both read one source (birch = pale trunk, conifer = dark needles, etc.).
    public Color BarkColor = new Color(0.35f, 0.24f, 0.14f);
    public Color LeafColor = new Color(0.28f, 0.46f, 0.18f);

    // Level 0 is always the trunk (ParentLevel < 0). Later levels reference an earlier level as parent.
    public LevelRule Trunk => Levels != null && Levels.Length > 0 ? Levels[0] : null;
}

// One tier of the hierarchy — the rule block for a whole rank of branches (or leaves). The ~8 parameters the
// Broccoli study found actually drive tree shape; everything else (sharing groups, roots, custom cross-sections)
// is dropped as overkill for a scoped stylized generator.
[Serializable]
public sealed class LevelRule
{
    public string Label = "level";
    public int ParentLevel = -1;   // index into TreeDef.Levels of the parent tier; < 0 = trunk
    public bool IsLeaf = false;     // leaf tier: places sprout anchors instead of branches

    [Header("Count + distribution")]
    public Vector2 Frequency = new Vector2(3, 5);  // children placed on each parent branch (min,max)
    public int ChildrenPerNode = 1;                // 1 = alternate, 2 = opposite, n = whorled
    public float TwirlDegrees = 137.5f;            // golden angle -> convincing spirals
    public Vector2 Range = new Vector2(0.25f, 0.9f); // band along the parent (start,end in 0..1)

    [Header("Orientation")]
    [Range(0f, 1f)] public float ParallelAlign = 0.45f;       // 0 = along parent, 1 = perpendicular ("open" angle)
    public Vector2 GravityAlign = new Vector2(0.25f, -0.15f);  // base->top: + up, - droop

    [Header("Size")]
    public Vector2 Length = new Vector2(3f, 1.6f); // base->top length along the parent
    public float GirthScale = 0.55f;               // child base girth = parent girth at attach * this
    public int RadialSides = 5;                    // tube sides for this tier's branches (low-N, faceted)

    [Header("Curve / lean")]
    public float Curve = 8f;      // degrees of gentle bend along the branch
    public float Noise = 0.06f;   // small per-branch wobble

    [Header("Leaf (IsLeaf tiers)")]
    public float LeafSize = 0.5f;
    public int LeafGroup = 0;

    // Emit this leaf tier into the ACCENT mesh instead of the main foliage mesh, so it can be drawn with a
    // different material. A TreeDef carries ONE LeafColor and every leaf tier merges into one mesh, so this is
    // the only way to get blooms speckled through a green shrub rather than a solid coloured blob.
    public bool AccentLeaf = false;
}
