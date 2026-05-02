using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Settings/Atmosphere Settings")]
public class AtmosphereSettings : ScriptableObject
{
    [Header("Scale")]
    [Range(0.01f, 1f)] public float AtmosphereScale = 0.15f;

    [Header("Scattering")]
    [Range(1, 30)] public int InScatteringPoints = 10;
    [Range(0.1f, 100f)] public float Intensity = 10f;

    [Header("Rayleigh (Sky Color)")]
    public Vector3 RayleighScattering = new Vector3(5.8e-3f, 13.5e-3f, 33.1e-3f);
    [Range(1f, 30f)] public float RayleighFalloff = 15f;

    [Header("Mie (Sun Glow / Haze)")]
    [Range(0f, 0.01f)] public float MieStrength = 0.001f;
    [Range(1f, 30f)] public float MieFalloff = 5f;
    [Range(0f, 0.999f)] public float MieAnisotropy = 0.76f;

    [Header("Absorption (Ozone)")]
    public Vector3 AbsorptionBeta = new Vector3(2.04e-5f, 4.97e-5f, 1.95e-6f);
    [Range(0f, 1f)] public float HeightAbsorption = 0.25f;

    [Header("Ambient")]
    public Color AmbientBeta = Color.black;

    [Header("Night")]
    public Color NightAmbient = new Color(0.01f, 0.012f, 0.02f, 1f);

    [Header("Sun Disc")]
    [Range(0.99f, 0.9999f)] public float SunDiscSize = 0.9998f;
    [Range(0.0001f, 0.01f)] public float SunDiscBlend = 0.001f;

    [Header("Dithering")]
    public Texture2D BlueNoise;
    [Range(0f, 2f)] public float DitherStrength = 0.8f;
    [Range(1f, 8f)] public float DitherScale = 4f;

    [Header("Optical Depth Bake")]
    [Range(64, 512)] public int BakeTextureSize = 256;
    [Range(8, 64)] public int BakeSteps = 40;
}
