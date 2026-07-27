using UnityEngine;

// Authoring surface for one discrete prop kind. Runtime reads the immutable ScatterLibraryDto,
// never this SO directly (settings SO->DTO pattern). Placement rules only — mesh/LOD/impostor
// fields arrive with the render slice (SP2). SlotId is an immutable persistence key; see ScatterId.
[CreateAssetMenu(menuName = "Planet/Scatter Prototype", fileName = "ScatterPrototype")]
public sealed class ScatterPrototype : ScriptableObject
{
    public string DisplayName = "Prototype";

    [Header("Identity (persistence key — never reuse or reorder)")]
    [Tooltip("Immutable id packed into every instance id (persistence key). Unique per library, 0..ScatterId.MaxSlot.")]
    [Range(0, 63)] public int SlotId = 0;

    [Header("Placement")]
    [Min(0.05f)] public float SpacingMeters = 8f;
    public BiomeType Biome = BiomeType.Grassland;
    [Range(0.25f, 4f)] public float BiomeBlendPower = 1f;
    [Range(0f, 4f)] public float Weight = 1f; // independent density multiplier

    [Header("Slope gate")]
    [Range(0f, 90f)] public float MaxSlopeDegrees = 35f;
    [Range(0f, 15f)] public float SlopeFadeDegrees = 5f;

    [Header("Altitude gate (metres above sea; negative = underwater)")]
    public bool HasMinAltitude = false;
    public float MinAltitudeMeters = 0f;
    public bool HasMaxAltitude = false;
    public float MaxAltitudeMeters = 0f;
    [Tooltip("Land props: min metres above the waterline. 0 to disable.")]
    [Min(0f)] public float MinWaterClearanceMeters = 0.05f;

    [Header("Transform jitter")]
    public Vector2 ScaleRange = new Vector2(0.85f, 1.2f);
    public bool RandomYaw = true;

    [Header("Interaction (SP5)")]
    public ScatterInteraction Interaction = ScatterInteraction.None;

    [Header("Rendering (SP2 — optional; no drawable part = placed but not drawn)")]
    [Tooltip("Renderable parts, each a material + LOD mesh chain, drawn together at the instance. A simple prop is one part; a composite prop (tree = opaque trunk + cutout foliage) is several.")]
    public ScatterPart[] Parts = System.Array.Empty<ScatterPart>();

    [Header("Rendering (legacy single-part — used only when Parts is empty)")]
    public Material Material;
    [Tooltip("LOD meshes near->far. Element i is drawn out to LodEndDistances[i].")]
    public Mesh[] LodMeshes = System.Array.Empty<Mesh>();
    [Tooltip("Ascending metres. Instance farther than the last entry is culled. Must match LodMeshes length.")]
    public float[] LodEndDistances = System.Array.Empty<float>();
    public bool CastShadows = true;
    public bool ReceiveShadows = true;
}
