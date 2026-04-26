using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Biomes/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    public BiomeType Type;
    public Gradient ColorGradient;
    public Color TintColor = Color.white;
    [Range(0, 1)] public float TintPercent;
}
