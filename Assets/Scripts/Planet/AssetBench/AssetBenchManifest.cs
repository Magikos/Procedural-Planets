using UnityEngine;

[System.Serializable]
public sealed class AssetBenchEntry
{
    public GameObject CandidatePrefab;

    [Tooltip("The Synty comparand shown beside the candidate. Optional — leave empty to judge alone.")]
    public GameObject ReferencePrefab;

    public string Label;

    [Tooltip("Why this is being judged; shown in the HUD. e.g. \"vs Polyperfect for wildlife\"")]
    public string Question;

    [Tooltip("Material to force onto the candidate's renderers. Vendor FBXs often embed a textureless " +
             "placeholder; leave empty when the prefab's own material is the thing being judged.")]
    public Material CandidateMaterial;

    [Tooltip("Preferred over ReferencePrefab. Builds the reference from a live scatter prototype — LOD0 mesh " +
             "and authored material per part — so you compare against what the planet actually plants, not a " +
             "raw FBX carrying a vendor placeholder material.")]
    public ScatterPrototype ReferencePrototype;
}

/// <summary>
/// An authored batch of candidates for the asset bench. This is content data rather than a settings SO —
/// it is read once on <c>bench.load</c> instead of being consumed per frame, so it needs no DTO snapshot.
/// </summary>
[CreateAssetMenu(menuName = "ProceduralPlanets/Asset Bench Manifest", fileName = "AssetBenchManifest")]
public sealed class AssetBenchManifest : ScriptableObject
{
    [Tooltip("Used in the report filename. e.g. \"scatter-candidates\"")]
    public string BatchId = "batch";

    public AssetBenchEntry[] Entries = System.Array.Empty<AssetBenchEntry>();
}
