using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Cloud Settings")]
public class CloudSettings : ScriptableObject
{
    [Header("Weather Grid")]
    [Range(32, 512)] public int WeatherResolution = 256;
    [Range(0f, 1f)] public float InitialCoverage = 0.48f;
    [Range(0f, 1f)] public float StormThreshold = 0.86f;

    [Header("Weather Evolution")]
    public bool EnableWeatherEvolution = true;
    [Range(0.05f, 5f)] public float EvolutionInterval = 0.1f;

    [Header("Layer")]
    [Range(20f, 1000f)] public float BaseAltitude = 330f;
    [Range(50f, 1000f)] public float LayerThickness = 300f;

    [Header("Shape")]
    [Range(0f, 0.08f)] public float DensityMultiplier = 0.018f;

    [Header("Lighting")]
    public Color CloudColor = new Color(1f, 0.98f, 0.92f, 1f);
    public Color StormColor = new Color(0.35f, 0.37f, 0.42f, 1f);

    [Header("Ray March")]
    [Range(8, 96)] public int ViewSteps = 48;
    [Range(2, 16)] public int LightSteps = 4;
    [Range(0f, 2f)] public float RayOffsetStrength = 1.1f;
    [Tooltip("Minimum view steps used when the camera is very far from the planet.")]
    [Range(4, 64)] public int MinViewSteps = 24;
    [Tooltip("Camera altitude above sea level (meters) below which full ViewSteps are used.")]
    [Range(0f, 500000f)] public float StepScaleNearAltitude = 5000f;
    [Tooltip("Camera altitude above sea level (meters) above which MinViewSteps are used.")]
    [Range(0f, 1000000f)] public float StepScaleFarAltitude = 50000f;

    [Header("Noise Textures")]
    [Range(32, 256)] public int ShapeNoiseResolution = 128;
    [Range(16, 64)] public int DetailNoiseResolution = 32;
}
