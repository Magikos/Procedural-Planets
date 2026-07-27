using UnityEngine;

// One renderable piece of a scatter prop: a material + its own LOD mesh chain, drawn at the
// instance transform. A simple prop is one part; a composite prop (a tree = opaque trunk + a
// cutout-foliage canopy, each its own mesh + material) is several parts. This is also the seed of
// the nested-scatter model — a prop is a set of sub-pieces, not a single mesh.
[System.Serializable]
public sealed class ScatterPart
{
    public string Name = "Part";
    public Material Material;
    [Tooltip("LOD meshes near->far. Element i is drawn out to LodEndDistances[i].")]
    public Mesh[] LodMeshes = System.Array.Empty<Mesh>();
    [Tooltip("Ascending metres. Instance farther than the last entry is culled. Must match LodMeshes length.")]
    public float[] LodEndDistances = System.Array.Empty<float>();
    public bool CastShadows = true;
    public bool ReceiveShadows = true;
}
