using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Cloud Settings")]
public class CloudSettings : ScriptableObject
{
    [Header("Shape")]
    [Range(0.0001f, 0.01f)] public float NoiseScale = 0.003f;
    [Range(0.0001f, 0.02f)] public float DetailNoiseScale = 0.008f;
    [Range(0f, 1f)] public float DetailWeight = 0.3f;
    public Vector4 ShapeNoiseWeights = new Vector4(1f, 0.5f, 0.25f, 0.125f);
    [Range(0f, 5f)] public float DensityMultiplier = 1.5f;

    [Header("Lighting")]
    [Range(0f, 3f)] public float LightAbsorption = 1.2f;
    [Range(0f, 1f)] public float DarknessThreshold = 0.1f;
    [Range(0f, 1f)] public float ForwardScattering = 0.8f;
    [Range(0f, 1f)] public float BackScattering = 0.3f;
    [Range(0f, 1f)] public float BaseBrightness = 0.8f;

    [Header("Animation")]
    [Range(0f, 2f)] public float AnimationSpeed = 0.5f;

    [Header("Ray March")]
    [Range(4, 64)] public int ViewSteps = 24;
    [Range(2, 8)] public int LightSteps = 4;

    [Header("Noise Textures")]
    [Range(32, 256)] public int ShapeNoiseResolution = 128;
    [Range(16, 64)] public int DetailNoiseResolution = 32;
}
