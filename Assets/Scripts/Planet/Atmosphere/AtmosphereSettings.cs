using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Atmosphere Settings")]
public class AtmosphereSettings : ScriptableObject
{
    [Header("Scale")]
    [Range(1.01f, 1.5f), Tooltip("Atmosphere outer radius as a multiple of planet radius")]
    public float AtmosphereScale = 1.25f;

    [Header("Scattering")]
    [Range(1, 30)] public int InScatteringPoints = 10;
    [Range(1f, 80f)] public float Intensity = 15f;

    [Header("Rayleigh (Sky Color)")]
    public Vector3 RayleighScattering = new Vector3(5.8e-3f, 13.5e-3f, 33.1e-3f);
    [Range(1f, 20f)] public float RayleighFalloff = 8f;

    [Header("Mie (Haze / Sun Glow)")]
    public Vector3 MieScattering = new Vector3(3.0e-5f, 3.0e-5f, 3.0e-5f);
    [Range(0.5f, 20f)] public float MieFalloff = 1.2f;
    [Range(0f, 0.99f)] public float MieAnisotropy = 0.85f;

    [Header("Absorption (Ozone)")]
    public Vector3 AbsorptionBeta = new Vector3(2.04e-5f, 4.97e-5f, 1.95e-6f);
    [Range(0f, 1f)] public float HeightAbsorption = 0.3f;

    [Header("Ambient")]
    public Vector3 AmbientBeta = Vector3.zero;

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9995f;
    [Range(0.0001f, 0.01f)] public float SunDiscBlend = 0.002f;

    [Header("Optical Depth Bake")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(8, 64)] public int BakeSteps = 40;
}
