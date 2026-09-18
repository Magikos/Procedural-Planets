using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Moon Settings")]
public sealed class MoonSettings : ScriptableObject
{
    public const float MinCycleDays = 0.1f, MaxCycleDays = 365f;
    public const float MinDistance = 2f, MaxDistance = 100f;
    public const float MaxInclination = 45f;
    public const float MinDiameter = 0.1f, MaxDiameter = 30f;
    public const float MaxBrightness = 4f, MaxDetail = 2f, MaxEarthshine = 0.1f;

    [Range(MinCycleDays, MaxCycleDays)] public float CycleDays = 8f;
    [Range(0f, 1f)] public float StartPhase = 0.5f;
    [Range(0f, MaxInclination)] public float Inclination = 5f;
    [Range(0f, 360f)] public float NodeAngle;
    [Range(MinDistance, MaxDistance), Tooltip("Distance from the planet center, in planet radii.")]
    public float Distance = 3f;
    [Range(MinDiameter, MaxDiameter), Tooltip("Angular diameter viewed from the planet center, in degrees.")]
    public float Diameter = 9.03f;
    [Range(0f, MaxBrightness)] public float Brightness = 1f;
    [Range(0f, MaxDetail)] public float Detail = 1f;
    [Range(0f, MaxEarthshine)] public float Earthshine = 0.01f;
    public Color Tint = Color.white;
    public Material Material;
}
