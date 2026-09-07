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
    [Range(0, ScatterId.MaxSlot)] public int SlotId = 0;

    [Header("Placement")]
    [Min(0.05f)] public float SpacingMeters = 8f;
    public BiomeType Biome = BiomeType.Grassland;
    [Range(0.25f, 4f)] public float BiomeBlendPower = 1f;
    [Range(0f, 4f)] public float Weight = 1f; // independent density multiplier

    [Header("Clumping (groves + clearings)")]
    [Tooltip("0 = uniform placement (unchanged). Higher = the prototype gathers into groves with genuinely open ground between them. Average density stays roughly the same; only its distribution changes.")]
    [Range(0f, 1f)] public float Clumpiness = 0f;
    [Tooltip("Roughly the diameter of one grove/colony in metres. Trees read well at 150-400; flower colonies at 20-60.")]
    [Min(5f)] public float PatchScaleMeters = 250f;

    // MEASURED correlation against tree cover, so authoring does not have to guess:
    //   +1.0 -> +0.83 (deep wood)   0 -> +0.80 (default)   -0.5 -> -0.06 (indifferent)   -1.0 -> -0.84 (open)
    // Note the crossover is near -0.5, NOT 0: the default already leans wooded, because every prototype obeys
    // the same openness field. A prop that should ignore cover entirely wants about -0.5.
    [Tooltip("Where this prop sits relative to tree cover, using the shared openness field. +1 = deep wood " +
             "(mushrooms, ferns). 0 = default, which already leans wooded. -0.5 = indifferent. -1 = open ground " +
             "between stands (meadow flowers). Needs Clumpiness > 0 to do anything.")]
    [Range(-1f, 1f)] public float ShadePreference = 0f;

    [Header("Slope gate")]
    [Range(0f, 90f)] public float MaxSlopeDegrees = 35f;
    [Range(0f, 15f)] public float SlopeFadeDegrees = 5f;
    [Tooltip("How much the prop tilts to lie on the terrain surface. 0 = stands radially upright (trees, mushrooms); 1 = lies flat on the slope (rocks, logs).")]
    [Range(0f, 1f)] public float ConformToSlope = 0f;

    [Header("Altitude gate (metres above sea; negative = underwater)")]
    public bool HasMinAltitude = false;
    public float MinAltitudeMeters = 0f;
    public bool HasMaxAltitude = false;
    public float MaxAltitudeMeters = 0f;
    [Tooltip("Land props: min metres above the waterline. 0 to disable.")]
    [Min(0f)] public float MinWaterClearanceMeters = 0.05f;
    [Tooltip("Float on the water surface (sea radius) inside the biome's water cells instead of standing on the terrain. For lily pads and other on-water scatter. Use with Biome = Lake.")]
    public bool OnWater = false;

    [Header("Transform jitter")]
    public Vector2 ScaleRange = new Vector2(0.85f, 1.2f);
    public bool RandomYaw = true;

    [Header("Interaction (SP5)")]
    public ScatterInteraction Interaction = ScatterInteraction.None;

    [Header("Cut-set (harvest — see plans/005)")]
    [Tooltip("Stump left when this is chopped — the lower trunk. Generate with Tools > ProceduralPlanets > " +
             "Generate Tree Stumps, or assign a mesh. Null = a placeholder stump is drawn.")]
    public Mesh StumpMesh;
    [Tooltip("Material for the stump. Null = the first part's (trunk) material.")]
    public Material StumpMaterial;

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

    [Header("Impostor (pre-baked far-field billboard)")]
    [Min(0), Tooltip("Keep single-LOD geometry through the full draw range when its total vertex count is within this budget. Zero uses impostors. Applies separately to each generated variant.")]
    public int MeshOnlyVertexLimit;

    [Tooltip("Pre-baked octahedral impostor atlas. Generated by Tools > ProceduralPlanets > Bake Impostor Atlases. " +
             "When set, the runtime uses it and skips the on-load bake (faster startup, consistent look). " +
             "Leave null to bake at runtime — the fallback for runtime-placed / custom-saved structures.")]
    public Texture2D BakedImpostorAtlas;

    [Tooltip("Pre-baked view-space normal atlas paired with BakedImpostorAtlas. Lets the far billboard shade " +
             "from the real surface (facets / canopy) instead of a synthesized hemisphere. Baked by the same tool.")]
    public Texture2D BakedImpostorNormal;
    [HideInInspector] public int BakedImpostorGridN;
    [HideInInspector] public bool BakedImpostorHasSurfaceData;
}
