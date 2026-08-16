using System.Collections.Generic;
using UnityEngine;

// Stage 1 of the tree generator (plan 006): grow the abstract skeleton (branches + leaf anchors) from a TreeDef
// + seed. Level-based recursion (trunk -> branch tiers -> leaf tiers), deterministic. Plain C# for now; the math
// is Burst/Unity.Mathematics-ready for T7. Meshing + cut-set carving are later stages that consume the skeleton.
public static class TreeStructureGenerator
{
    public static TreeSkeleton Generate(TreeDef def, int seed)
    {
        var sk = new TreeSkeleton();
        if (def?.Levels == null || def.Levels.Length == 0) return sk;

        // Don't perturb the game's global RNG stream.
        Random.State prev = Random.state;
        Random.InitState(seed);
        try
        {
            // Height is set by normalising to def.MaxHeight at the end, so age drives it through this curve
            // instead of a factor baked into the lengths. Superlinear: a sapling is a fraction of the mature
            // tree (age .25 -> ~15%), not a half-size copy of it.
            float ageHeight = Mathf.Lerp(0.06f, 1f, Mathf.Pow(def.Age, 1.6f));
            float ageGirth = Mathf.Lerp(0.30f, 0.82f, def.Age);
            float ageFreq = Mathf.Lerp(0.35f, 1f, def.Age);
            // Leaves shrink with age too, else a sapling wears adult-size cards and reads as a shrunk old tree
            // rather than a young one (age has to change SHAPE, not just overall scale).
            float ageLeaf = Mathf.Lerp(0.55f, 1f, def.Age);
            float scale = Mathf.Max(0.01f, def.GlobalScale);

            // --- trunk (level 0) ---
            LevelRule t = def.Levels[0];
            float trunkLen = Random.Range(t.Length.x, t.Length.y) * scale;
            float trunkGirth = trunkLen * 0.042f * def.TrunkGirthScale * Mathf.Lerp(0.6f, 1.4f, ageGirth);
            var trunk = new TreeBranch { Level = 0, IsTrunk = true, RadialSides = t.RadialSides, PositionOnParent = 0f };
            int trunkSegs = Mathf.Clamp(Mathf.RoundToInt(trunkLen / 1.4f), 3, 10);
            BuildCenterline(trunk, Vector3.zero, Vector3.up, trunkLen, trunkGirth, trunkGirth * def.TrunkTipScale, t.Curve, t.Noise, trunkSegs, def.RootFlare);
            sk.AddBranch(trunk);
            sk.Trunk = trunk;
            sk.Height = trunk.Tip.y;
            sk.TrunkBaseGirth = trunkGirth;

            // Leaf clumps grow sublinearly with the tree (see NormalizeHeight), so a taller tree needs MORE of
            // them or its crown thins out. Estimated off the trunk here because the true factor isn't known
            // until the whole skeleton exists — the branch overshoot above the trunk tip is a stable per-species
            // ratio, so the estimate is within a few percent and only sets a clump count.
            float leafCount = Mathf.Pow(Mathf.Max(0.05f, def.MaxHeight * ageHeight / Mathf.Max(0.01f, trunk.Tip.y)), 0.35f);

            // Track which branches belong to each level so children attach to the right tier.
            var byLevel = new Dictionary<int, List<TreeBranch>> { [0] = new List<TreeBranch> { trunk } };

            for (int li = 1; li < def.Levels.Length; li++)
            {
                LevelRule rule = def.Levels[li];
                if (rule.IsLeaf && def.Dead) continue; // a dead tree keeps its branch structure, loses its foliage
                if (!byLevel.TryGetValue(rule.ParentLevel, out List<TreeBranch> parents)) continue;
                var made = new List<TreeBranch>();

                foreach (TreeBranch parent in parents)
                {
                    float countScale = ageFreq * (rule.IsLeaf ? leafCount : 1f);
                    int count = Mathf.Max(0, Mathf.RoundToInt(Random.Range(rule.Frequency.x, rule.Frequency.y) * countScale));
                    // Age thinning must never round a tier away completely: a branch with no foliage on it, or a
                    // trunk with no branches, is a bare stick — and on a young tree with only one or two branches
                    // it also throws the crown off the trunk axis, because the one branch that did get leaves
                    // carries the whole canopy to one side.
                    if (count == 0 && (rule.IsLeaf || parent.IsTrunk)) count = 1;
                    if (count == 0) continue;
                    int perNode = Mathf.Max(1, rule.ChildrenPerNode);
                    int nodes = Mathf.CeilToInt(count / (float)perNode);
                    float roll = Random.Range(0f, 360f);
                    int emitted = 0;

                    for (int n = 0; n < nodes && emitted < count; n++)
                    {
                        float nodeT = nodes <= 1 ? 0.5f : n / (float)(nodes - 1);
                        float posT = Mathf.Lerp(rule.Range.x, rule.Range.y, nodeT);
                        parent.Sample(posT, out Vector3 pos, out Vector3 fwd, out Vector3 nrm, out float girth);

                        for (int c = 0; c < perNode && emitted < count; c++, emitted++)
                        {
                            float childRoll = roll + c * (360f / perNode);
                            if (rule.IsLeaf)
                            {
                                Vector3 outward = Quaternion.AngleAxis(childRoll, fwd) * nrm;
                                Vector3 ldir = (Vector3.Slerp(fwd, outward, 0.6f) + Vector3.up * 0.25f).normalized;
                                sk.Sprouts.Add(new TreeSprout
                                {
                                    Position = pos + outward * girth,
                                    Direction = ldir,
                                    Normal = Vector3.Slerp(outward, Vector3.up, 0.5f).normalized,
                                    Size = rule.LeafSize * scale * ageLeaf,
                                    LeafGroup = rule.LeafGroup,
                                    BranchLevel = li,
                                });
                            }
                            else
                            {
                                float grav = Mathf.Lerp(rule.GravityAlign.x, rule.GravityAlign.y, posT);
                                Vector3 azimuth = Quaternion.AngleAxis(childRoll, fwd) * nrm;
                                Vector3 dir = Vector3.Slerp(fwd, azimuth, rule.ParallelAlign).normalized;
                                dir = (dir + Vector3.up * grav).normalized;

                                float len = Mathf.Lerp(rule.Length.x, rule.Length.y, posT) * scale;
                                float bg = girth * rule.GirthScale;
                                var child = new TreeBranch
                                {
                                    Level = li, IsTrunk = false, Parent = parent, PositionOnParent = posT, RadialSides = rule.RadialSides,
                                };
                                int segs = Mathf.Clamp(Mathf.RoundToInt(len / 1.2f), 2, 6);
                                // Dead wood: gnarlier and stubbier — limbs snap off rather than taper away.
                                float curve = def.Dead ? rule.Curve * 1.5f : rule.Curve;
                                float noise = def.Dead ? rule.Noise * 2.2f : rule.Noise;
                                if (def.Dead) len *= 0.72f;
                                BuildCenterline(child, pos, dir, len, bg, bg * 0.3f, curve, noise, segs);
                                sk.AddBranch(child);
                                made.Add(child);
                            }
                        }
                        roll += rule.TwirlDegrees;
                    }
                }
                if (made.Count > 0) byLevel[li] = made;
            }

            NormalizeHeight(sk, def.MaxHeight * ageHeight);
        }
        finally { Random.state = prev; }

        return sk;
    }

    // The recursion builds the tree in proportion units; this is what puts it in METRES. Scaling the finished
    // skeleton (rather than pre-multiplying the lengths) keeps every authored ratio and the segment density
    // exactly as tuned, and makes def.MaxHeight the single legible size knob.
    static void NormalizeHeight(TreeSkeleton sk, float targetHeight)
    {
        if (sk.Branches.Count == 0 || targetHeight <= 0f) return;

        float natural = 0f;
        foreach (TreeBranch b in sk.Branches)
            foreach (Vector3 p in b.Centerline)
                if (p.y > natural) natural = p.y;
        if (natural <= 1e-4f) return;

        float f = targetHeight / natural;
        // Leaf clumps grow sublinearly with the tree: a 30 m oak carries finer foliage relative to its crown
        // than a 10 m one, which is what keeps a big tree from reading as one inflated blob (Synty POLYGON
        // crowns are many modest masses, not one giant facet). See docs/research/tree-references.
        float leafF = Mathf.Pow(f, 0.65f);

        foreach (TreeBranch b in sk.Branches)
        {
            for (int i = 0; i < b.Centerline.Count; i++) b.Centerline[i] *= f;
            for (int i = 0; i < b.Girth.Count; i++) b.Girth[i] *= f;
        }
        for (int i = 0; i < sk.Sprouts.Count; i++)
        {
            TreeSprout s = sk.Sprouts[i];
            s.Position *= f;
            s.Size *= leafF;
            sk.Sprouts[i] = s;
        }

        sk.Height = sk.Trunk != null ? sk.Trunk.Tip.y : targetHeight;
        sk.TrunkBaseGirth *= f;
    }

    static void BuildCenterline(TreeBranch b, Vector3 basePos, Vector3 dir, float length,
        float baseGirth, float tipGirth, float curveDeg, float noise, int segments, float rootFlare = 0f)
    {
        b.Centerline.Clear();
        b.Girth.Clear();
        Vector3 p = basePos;
        Vector3 d = dir.normalized;
        Vector3 bendAxis = Vector3.Cross(d, Vector3.up);
        bendAxis = bendAxis.sqrMagnitude < 1e-4f ? Vector3.right : bendAxis.normalized;
        float segLen = length / segments;

        b.Centerline.Add(p);
        b.Girth.Add(baseGirth * (1f + rootFlare));
        for (int s = 1; s <= segments; s++)
        {
            float tt = s / (float)segments;
            d = (Quaternion.AngleAxis(curveDeg / segments, bendAxis) * d).normalized;
            if (noise > 0f) d = (d + Random.insideUnitSphere * noise).normalized;
            p += d * segLen;
            b.Centerline.Add(p);
            // Root buttress: the flare decays over the bottom fifth, so the trunk meets the ground on a widened
            // base ring instead of a cylinder cut off flat. Trunks carry it; branches pass 0.
            b.Girth.Add(Mathf.Lerp(baseGirth, tipGirth, tt) * (1f + rootFlare * Mathf.Exp(-tt * 9f)));
        }
    }
}
