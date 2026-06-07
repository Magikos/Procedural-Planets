using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Biomes/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    public BiomeType Type;
    public Gradient ColorGradient;
    public Color TintColor = Color.white;
    [Range(0, 1)] public float TintPercent;

    [Header("Placement noise")]
    [Tooltip("Per-biome 3D offset in noise-coordinate units, added after world-position scaling "
        + "when sampling placement noise (vegetation, props, detail). "
        + "Decorrelates patterns across biome boundaries so the same noise field produces different "
        + "placement textures per biome. Values in the ~1000-10000 range work well; differences matter more than absolute scale.")]
    public Vector3 BiomeOffset = Vector3.zero;

    [Header("Phase B: Surface textures (required for texture-mode terrain rendering)")]
    [Tooltip("Albedo / base color. All biomes in a registry must share dimensions + format.")]
    public Texture2D SurfaceAlbedo;
    [Tooltip("Optional broad-scale albedo variation mixed into the primary material. "
        + "Leave empty to reuse SurfaceAlbedo. This is intentionally albedo-only until "
        + "profiling proves that secondary normal and ARM samples fit the terrain budget.")]
    public Texture2D SurfaceSecondaryAlbedo;
    [Tooltip("Tangent-space normal map. BC5 or RG-channel format recommended.")]
    public Texture2D SurfaceNormal;
    [Tooltip("R=AO, G=Roughness, B=Metallic. Linear color space.")]
    public Texture2D SurfaceARM;
    [Tooltip("World-space tiling factor for the triplanar sampler (1.0 = one texture tile per meter).")]
    [Range(0.01f, 100f)] public float SurfaceTiling = 1.0f;

    [Header("Phase C: Grass")]
    [Tooltip("Fraction of grass placement lanes that can emit a blade in this biome. Existing assets default to 0 so they do not emit grass until authored.")]
    [Range(0f, 1f)] public float GrassDensity = 0f;
    [Tooltip("Base grass tint used by clumps before per-clump variation.")]
    public Color GrassTintBase = Color.white;
    [Tooltip("Maximum blade height in world units. Default 1.5m reads as visible from low-altitude flight; bump higher for tall-grass biomes.")]
    public float GrassHeight = 1.5f;
    [Tooltip("Base blade half-width in world units.")]
    public float GrassWidth = 0.08f;
    [Tooltip("0 = uniform blades, 1 = strong clumping.")]
    [Range(0f, 1f)] public float GrassClumpStrength = 0.65f;
    [Tooltip("Center of the steep-slope density fade in degrees.")]
    [Range(0f, 90f)] public float GrassMaxSlopeDegrees = 35f;
    [Tooltip("Width of the soft slope fade band around max slope in degrees.")]
    public float GrassSlopeFadeDegrees = 5f;
    [Tooltip("Minimum terrain clearance above water before grass can emit, in world units.")]
    public float GrassMinWaterClearance = 0.05f;
    [Tooltip("Density response to top-K biome blend weights. 1 = linear.")]
    public float GrassBiomeBlendPower = 1f;
}
