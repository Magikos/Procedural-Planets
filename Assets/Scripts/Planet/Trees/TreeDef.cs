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
    public float GlobalScale = 1f;

    [Range(0f, 1f)] public float Age = 1f; // 0 = sapling, 1 = old; scales height/girth/branch tiers/lean
    public LevelRule[] Levels = Array.Empty<LevelRule>();
    public FoliageStyle FoliageStyle = FoliageStyle.LeafCards;
    public float TrunkTipScale = 0.28f; // trunk tip girth as a fraction of base; low = tapers to a spire (conifer)
    public bool NeedleFoliage = false;  // solid dark-green needle material (fir cone / pine tufts) vs leaf texture

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
}
