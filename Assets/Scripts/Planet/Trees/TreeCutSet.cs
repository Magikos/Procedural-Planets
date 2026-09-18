using UnityEngine;

/// <summary>Builds matching cut faces through the shared tube slicing path.</summary>
public static class TreeCutSet
{
    public static void Carve(TreeSkeleton skeleton, float chopFraction, out Mesh stump, out Mesh log)
    {
        stump = null; log = null;
        TreeBranch trunk = skeleton?.Trunk;
        if (trunk == null || trunk.Centerline.Count < 2) return;
        float cut = Mathf.Clamp(chopFraction, 0.001f, 0.6f);
        trunk.Sample(cut, out Vector3 position, out _, out _, out _);
        stump = TreeHarvestGeometry.Slice(trunk, 0f, cut, Vector3.zero);
        log = TreeHarvestGeometry.Slice(trunk, cut, 1f, position);
    }
}
