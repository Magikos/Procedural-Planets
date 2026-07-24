using UnityEngine;

// Authoring surface for one discrete prop kind. Runtime reads the immutable ScatterLibraryDto,
// never this SO directly (settings SO->DTO pattern). Placement rules only — mesh/LOD/impostor
// fields arrive with the render slice (SP2). SlotId is an immutable persistence key; see ScatterId.
[CreateAssetMenu(menuName = "Planet/Scatter Prototype", fileName = "ScatterPrototype")]
public sealed class ScatterPrototype : ScriptableObject
{
    public string DisplayName = "Prototype";

    [Header("Identity (persistence key — never reuse or reorder)")]
    [Tooltip("Immutable 0-15 id packed into every instance id. Unique per library.")]
    [Range(0, 15)] public int SlotId = 0;

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
}
