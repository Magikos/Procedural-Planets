using System.Collections.Generic;
using UnityEngine;

// The generated tree skeleton (plan 006) — the abstract structure the mesher and cut-set carver consume. Built
// once by TreeStructureGenerator; meshing is a separate stage. Trunk-vs-leaf is explicit here (Branch.IsTrunk +
// the leaf Sprout list), which is the clean separation the Synty black-box meshes denied us.
public sealed class TreeSkeleton
{
    public readonly List<TreeBranch> Branches = new(); // flat list, all tiers
    public readonly List<TreeSprout> Sprouts = new();  // leaf anchors
    public TreeBranch Trunk;                            // the level-0 branch
    public float Height;                                // trunk tip height above the base (metres, tree-local)
    public float TrunkBaseGirth;                        // radius at the trunk base

    public TreeBranch AddBranch(TreeBranch b) { Branches.Add(b); return b; }
}

// One branch: a centerline polyline (sampled points) + a girth (radius) that tapers base->tip. Children attach
// along it; followUp is the child that continues the line (the trunk chain when IsTrunk).
public sealed class TreeBranch
{
    public int Level;
    public bool IsTrunk;
    public TreeBranch Parent;
    public float PositionOnParent; // 0..1 where it attaches on the parent
    public int RadialSides = 5;

    // Centerline in tree-local space; [0] = base, [last] = tip.
    public readonly List<Vector3> Centerline = new();
    // Radius at each centerline point (same length as Centerline).
    public readonly List<float> Girth = new();

    public Vector3 Base => Centerline.Count > 0 ? Centerline[0] : Vector3.zero;
    public Vector3 Tip => Centerline.Count > 0 ? Centerline[Centerline.Count - 1] : Vector3.zero;
    public Vector3 TipDirection
    {
        get
        {
            int n = Centerline.Count;
            return n >= 2 ? (Centerline[n - 1] - Centerline[n - 2]).normalized : Vector3.up;
        }
    }

    // Point + a frame (forward + a stable normal) at parameter t in 0..1 along the centerline.
    public void Sample(float t, out Vector3 pos, out Vector3 forward, out Vector3 normal, out float girth)
    {
        int n = Centerline.Count;
        if (n == 0) { pos = Vector3.zero; forward = Vector3.up; normal = Vector3.right; girth = 0f; return; }
        if (n == 1) { pos = Centerline[0]; forward = Vector3.up; normal = Vector3.right; girth = Girth[0]; return; }
        float f = Mathf.Clamp01(t) * (n - 1);
        int i = Mathf.Min((int)f, n - 2);
        float frac = f - i;
        pos = Vector3.Lerp(Centerline[i], Centerline[i + 1], frac);
        girth = Mathf.Lerp(Girth[i], Girth[i + 1], frac);
        forward = (Centerline[i + 1] - Centerline[i]).normalized;
        normal = Vector3.Cross(forward, Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
    }
}

public struct TreeSprout
{
    public Vector3 Position; // surface anchor (particle emit point + cut-set data)
    public Vector3 Direction;
    public Vector3 Normal;
    public float Size;
    public int LeafGroup;
    public int BranchLevel;
}
