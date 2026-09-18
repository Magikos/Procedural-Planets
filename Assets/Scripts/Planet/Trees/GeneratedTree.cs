using UnityEngine;

// The output bundle of the tree generator (plan 006). One seeded TreeDef produces this: the standing meshes
// (bark + foliage, per LOD), the harvest cut-set (stump + log carved from the same trunk), leaf/trunk anchor
// points for particles, and gameplay metadata (HP, wood yield). Everything downstream — scatter prototype,
// stump/log renderers, particles, multi-hit chopping — is fed from here instead of reverse-engineering a mesh.
public sealed class GeneratedTree : System.IDisposable
{
    public string SpeciesName;
    // Standing geometry, index 0 = highest detail. Bark and foliage are separate meshes (separate materials).
    public Mesh[] BarkLods = System.Array.Empty<Mesh>();
    public Mesh[] FoliageLods = System.Array.Empty<Mesh>();
    // Bloom/accent tiers, drawn with their own material. Empty for every species that declares no accent tier.
    public Mesh[] AccentLods = System.Array.Empty<Mesh>();

    // Harvest cut-set — carved from the same trunk skeleton, so stump top and log bottom match by construction.
    public Mesh Stump;
    public Mesh Log;
    public Vector3 CutPosition;
    public Mesh[] BranchBark = System.Array.Empty<Mesh>();
    public Mesh[] BranchFoliage = System.Array.Empty<Mesh>();
    public Vector3[] BranchAnchors = System.Array.Empty<Vector3>();
    public Mesh[] LogSections = System.Array.Empty<Mesh>();
    public Vector3[] SectionAnchors = System.Array.Empty<Vector3>();
    public Vector3[] TrunkPoints = System.Array.Empty<Vector3>();
    public float[] TrunkRadii = System.Array.Empty<float>();
    public Vector3[] LogSupport = System.Array.Empty<Vector3>();
    public Vector3[][] BranchSupport = System.Array.Empty<Vector3[]>();
    public bool IsSapling;

    // Anchors (tree-local space) for particles + the "leaves scatter on chop" effect.
    public Vector3[] LeafAnchors = System.Array.Empty<Vector3>();

    // Gameplay metadata.
    public float Height;          // trunk tip height (m, tree-local)
    public float TrunkBaseGirth;  // radius at the trunk base
    public float ChopFraction;    // 0..1 up the trunk where a chop fells it (the stump top / log bottom cut)
    public int ChopHp;            // hits to fell (scales with girth/age)
    public int WoodYield;         // wood from the felled log (scales with trunk volume)

    public Mesh Bark => BarkLods.Length > 0 ? BarkLods[0] : null;
    public Mesh Foliage => FoliageLods.Length > 0 ? FoliageLods[0] : null;
    public Mesh Accent => AccentLods.Length > 0 ? AccentLods[0] : null;

    public void Dispose()
    {
        var meshes = new System.Collections.Generic.HashSet<Mesh>();
        foreach (var group in new[] { BarkLods, FoliageLods, AccentLods, BranchBark, BranchFoliage, LogSections })
            foreach (var mesh in group) if (mesh != null) meshes.Add(mesh);
        if (Stump != null) meshes.Add(Stump);
        if (Log != null) meshes.Add(Log);
        foreach (var mesh in meshes)
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
    }
}
