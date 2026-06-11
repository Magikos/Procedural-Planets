using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Cloud Settings")]
public class CloudSettings : ScriptableObject
{
    public enum DebugView
    {
        Off = 0,
        Weather = 1,
        Storm = 2,
        Density = 3,
        OpticalDepth = 4,
        SilverLining = 5,
        MoistureSource = 6,
        CondensationChange = 7
    }

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

    [Header("Animation")]
    [Range(0f, 2f)] public float AnimationSpeed = 0.35f;

    [Header("Ray March")]
    [Range(8, 96)] public int ViewSteps = 72;
    [Range(2, 16)] public int LightSteps = 8;
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

    // Debug visualization: runtime toggles, not authored settings. Kept here for now;
    // candidate to move to a CloudDebugState runtime module in a follow-up.
    [Header("Debug")]
    public DebugView DebugMode = DebugView.Off;
    [Range(0f, 0.01f)] public float CondensationChangeDebugThreshold = 0.0002f;
    [Range(0.0005f, 0.02f)] public float CondensationChangeDebugSaturation = 0.004f;
}
