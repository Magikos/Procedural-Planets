using System.Collections.Generic;
using UnityEngine;

// Stage 4 (cut-set) of the tree generator (plan 006): carve the stump + fallen-log meshes from the SAME trunk
// skeleton the standing tree uses, so the stump top and the log bottom are the same cut and line up by
// construction. This is what the opaque Synty meshes couldn't give. Foliage is naturally excluded (only the
// trunk branch is used). Both meshes are pivoted at their base.
public static class TreeCutSet
{
    // stump = trunk from base to chopFraction (capped); log = trunk from chopFraction to tip (capped).
    public static void Carve(TreeSkeleton sk, float chopFraction, out Mesh stump, out Mesh log)
    {
        stump = null;
        log = null;
        TreeBranch trunk = sk?.Trunk;
        if (trunk == null || trunk.Centerline.Count < 2) return;

        int sides = Mathf.Max(3, trunk.RadialSides);
        SplitCenterline(trunk, Mathf.Clamp(chopFraction, 0.02f, 0.6f),
            out List<Vector3> lower, out List<float> lowerG, out List<Vector3> upper, out List<float> upperG);

        if (lower.Count >= 2) stump = TreeTubeMesher.BuildCappedTube(lower, lowerG, sides);
        if (upper.Count >= 2) log = TreeTubeMesher.BuildCappedTube(upper, upperG, sides);
    }

    static void SplitCenterline(TreeBranch trunk, float cf,
        out List<Vector3> lower, out List<float> lowerG, out List<Vector3> upper, out List<float> upperG)
    {
        lower = new List<Vector3>(); lowerG = new List<float>();
        upper = new List<Vector3>(); upperG = new List<float>();

        int n = trunk.Centerline.Count;
        float ff = cf * (n - 1);
        int cut = Mathf.Clamp((int)ff, 0, n - 2);
        float frac = ff - cut;
        Vector3 cutPos = Vector3.Lerp(trunk.Centerline[cut], trunk.Centerline[cut + 1], frac);
        float cutG = Mathf.Lerp(trunk.Girth[cut], trunk.Girth[cut + 1], frac);

        for (int i = 0; i <= cut; i++) { lower.Add(trunk.Centerline[i]); lowerG.Add(trunk.Girth[i]); }
        lower.Add(cutPos); lowerG.Add(cutG);

        upper.Add(cutPos); upperG.Add(cutG);
        for (int i = cut + 1; i < n; i++) { upper.Add(trunk.Centerline[i]); upperG.Add(trunk.Girth[i]); }
    }
}
