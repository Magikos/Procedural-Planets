using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Biome Settings")]
public class BiomeSettings : ScriptableObject
{
    public BiomeRegistry Registry;

    [Header("Temperature Noise")]
    public NoiseSettings TemperatureNoise;
    [Range(0f, 0.5f)] public float TemperatureNoiseStrength = 0.15f;

    [Header("Moisture Noise")]
    public NoiseSettings MoistureNoise;
}
