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
            float ageHeight = Mathf.Lerp(0.22f, 1f, def.Age);
            float ageGirth = Mathf.Lerp(0.30f, 1f, def.Age);
            float ageFreq = Mathf.Lerp(0.35f, 1f, def.Age);
            float scale = Mathf.Max(0.01f, def.GlobalScale);

            // --- trunk (level 0) ---
            LevelRule t = def.Levels[0];
            float trunkLen = Random.Range(t.Length.x, t.Length.y) * scale * ageHeight;
            float trunkGirth = trunkLen * 0.05f * Mathf.Lerp(0.6f, 1.4f, ageGirth);
            var trunk = new TreeBranch { Level = 0, IsTrunk = true, RadialSides = t.RadialSides, PositionOnParent = 0f };
            int trunkSegs = Mathf.Clamp(Mathf.RoundToInt(trunkLen / 1.4f), 3, 10);
            BuildCenterline(trunk, Vector3.zero, Vector3.up, trunkLen, trunkGirth, trunkGirth * 0.28f, t.Curve, t.Noise, trunkSegs);
            sk.AddBranch(trunk);
            sk.Trunk = trunk;
            sk.Height = trunk.Tip.y;
            sk.TrunkBaseGirth = trunkGirth;

            // Track which branches belong to each level so children attach to the right tier.
            var byLevel = new Dictionary<int, List<TreeBranch>> { [0] = new List<TreeBranch> { trunk } };

            for (int li = 1; li < def.Levels.Length; li++)
            {
                LevelRule rule = def.Levels[li];
                if (!byLevel.TryGetValue(rule.ParentLevel, out List<TreeBranch> parents)) continue;
                var made = new List<TreeBranch>();

                foreach (TreeBranch parent in parents)
                {
                    int count = Mathf.Max(0, Mathf.RoundToInt(Random.Range(rule.Frequency.x, rule.Frequency.y) * ageFreq));
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
                                    Size = rule.LeafSize * scale,
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

                                float len = Mathf.Lerp(rule.Length.x, rule.Length.y, posT) * scale * ageHeight;
                                float bg = girth * rule.GirthScale;
                                var child = new TreeBranch
                                {
                                    Level = li, IsTrunk = false, Parent = parent, PositionOnParent = posT, RadialSides = rule.RadialSides,
                                };
                                int segs = Mathf.Clamp(Mathf.RoundToInt(len / 1.2f), 2, 6);
                                BuildCenterline(child, pos, dir, len, bg, bg * 0.3f, rule.Curve, rule.Noise, segs);
                                sk.AddBranch(child);
                                made.Add(child);
                            }
                        }
                        roll += rule.TwirlDegrees;
                    }
                }
                if (made.Count > 0) byLevel[li] = made;
            }
        }
        finally { Random.state = prev; }

        return sk;
    }

    static void BuildCenterline(TreeBranch b, Vector3 basePos, Vector3 dir, float length,
        float baseGirth, float tipGirth, float curveDeg, float noise, int segments)
    {
        b.Centerline.Clear();
        b.Girth.Clear();
        Vector3 p = basePos;
        Vector3 d = dir.normalized;
        Vector3 bendAxis = Vector3.Cross(d, Vector3.up);
        bendAxis = bendAxis.sqrMagnitude < 1e-4f ? Vector3.right : bendAxis.normalized;
        float segLen = length / segments;

        b.Centerline.Add(p);
        b.Girth.Add(baseGirth);
        for (int s = 1; s <= segments; s++)
        {
            float tt = s / (float)segments;
            d = (Quaternion.AngleAxis(curveDeg / segments, bendAxis) * d).normalized;
            if (noise > 0f) d = (d + Random.insideUnitSphere * noise).normalized;
            p += d * segLen;
            b.Centerline.Add(p);
            b.Girth.Add(Mathf.Lerp(baseGirth, tipGirth, tt));
        }
    }
}
