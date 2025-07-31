using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Color Settings")]
public class ColorSettings : ScriptableObject
{
    public Material PlanetMaterial;
    public BiomeColorSettings BiomeSettings;
    public Gradient OceanColorGradient;

    [System.Serializable]
    public class BiomeColorSettings
    {
        public Biome[] Biomes;
        public NoiseSettings NoiseSettings;
        public float NoiseOffset;
        public float NoiseStrength;
        [Range(0, 1)] public float BlendAmount;

        [System.Serializable]
        public class Biome
        {
            public Gradient ColorGradient;
            public Color TintColor;
            [Range(0, 1)] public float TintPercent;
            [Range(0, 1)] public float StartHeight;

        }
    }
}