using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Cloud Settings")]
public class CloudSettings : ScriptableObject
{
    [Header("Altitude")]
    [Range(1.01f, 1.15f), Tooltip("Cloud shell inner radius as multiple of max terrain radius")]
    public float CloudAltitudeScale = 1.02f;
    [Range(0.005f, 0.05f), Tooltip("Thickness of cloud shell as fraction of planet radius")]
    public float CloudThickness = 0.015f;

    [Header("Shape")]
    [Range(0.1f, 10f)] public float NoiseScale = 2f;
    [Range(0.1f, 10f)] public float DetailNoiseScale = 6f;
    [Range(0f, 1f)] public float DetailWeight = 0.3f;
    [Range(0f, 2f)] public float DensityMultiplier = 1f;
    [Range(-1f, 1f), Tooltip("Shifts density threshold. Negative = more clouds, Positive = fewer")]
    public float DensityOffset = -0.1f;

    [Header("Lighting")]
    [Range(0f, 2f)] public float LightAbsorption = 1f;
    [Range(0f, 1f)] public float DarknessThreshold = 0.15f;
    [Range(0f, 1f)] public float ForwardScattering = 0.8f;
    [Range(0f, 1f)] public float BackScattering = 0.3f;
    [Range(0f, 1f)] public float BaseBrightness = 0.8f;

    [Header("Animation")]
    [Range(0f, 2f)] public float AnimationSpeed = 0.5f;

    [Header("Ray March")]
    [Range(4, 32)] public int ViewSteps = 12;
    [Range(2, 8)] public int LightSteps = 4;

    [Header("Mesh")]
    [Range(16, 64)] public int MeshResolution = 32;
}
